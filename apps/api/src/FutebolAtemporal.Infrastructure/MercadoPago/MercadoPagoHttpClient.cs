using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace FutebolAtemporal.Infrastructure.MercadoPago;

public sealed class MercadoPagoHttpClient : IMercadoPagoClient
{
    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _options;
    private readonly ILogger<MercadoPagoHttpClient> _logger;

    public MercadoPagoHttpClient(HttpClient http, MercadoPagoOptions options, ILogger<MercadoPagoHttpClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public string Mode => _options.EffectiveMode;
    public bool IsConfigured => _options.HasAccessToken;
    public bool IsSimulated => false;

    public async Task<MpPixPaymentResult> CreatePixPaymentAsync(MpCreatePixPaymentRequest request, CancellationToken ct = default)
    {
        if (!_options.HasAccessToken)
            throw new DomainException("Gateway Mercado Pago não configurado no servidor (MP_ACCESS_TOKEN ausente).");

        var payload = new Dictionary<string, object?>
        {
            ["transaction_amount"] = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero),
            ["payment_method_id"] = "pix",
            ["description"] = request.Description,
            ["external_reference"] = request.ExternalReference,
            ["date_of_expiration"] = request.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
            ["payer"] = new Dictionary<string, object?>
            {
                ["email"] = request.PayerEmail,
                ["first_name"] = request.PayerName
            }
        };

        if (!string.IsNullOrWhiteSpace(_options.NotificationUrl))
            payload["notification_url"] = _options.NotificationUrl;

        using var msg = new HttpRequestMessage(HttpMethod.Post, "/v1/payments");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        msg.Headers.Add("X-Idempotency-Key", request.IdempotencyKey);
        msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mercado Pago CreatePixPayment falhou com HTTP {StatusCode} para ref {ExternalRef}", (int)resp.StatusCode, request.ExternalReference);
            throw new DomainException($"Não foi possível gerar a cobrança Pix no Mercado Pago (HTTP {(int)resp.StatusCode}). Verifique se a chave Pix da conta está ativa.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var mpId = ReadIdString(root);
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "pending" : "pending";

        string qrCode = string.Empty;
        string? qrCodeBase64 = null;
        if (root.TryGetProperty("point_of_interaction", out var poi)
            && poi.TryGetProperty("transaction_data", out var tx))
        {
            if (tx.TryGetProperty("qr_code", out var qc)) qrCode = qc.GetString() ?? string.Empty;
            if (tx.TryGetProperty("qr_code_base64", out var qcb)) qrCodeBase64 = qcb.GetString();
        }

        if (string.IsNullOrWhiteSpace(mpId) || string.IsNullOrWhiteSpace(qrCode))
            throw new DomainException("Resposta incompleta do Mercado Pago ao gerar QR Code Pix.");

        var expiresAt = request.ExpiresAt;
        if (root.TryGetProperty("date_of_expiration", out var expEl)
            && expEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(expEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedExp))
        {
            expiresAt = parsedExp.ToUniversalTime();
        }

        return new MpPixPaymentResult(mpId, status, qrCode, qrCodeBase64, expiresAt, IsSimulated: false);
    }

    public async Task<MpPixPaymentStatus> GetPaymentAsync(string mpPaymentId, CancellationToken ct = default)
    {
        if (!_options.HasAccessToken)
            throw new DomainException("Gateway Mercado Pago não configurado no servidor (MP_ACCESS_TOKEN ausente).");

        using var msg = new HttpRequestMessage(HttpMethod.Get, $"/v1/payments/{Uri.EscapeDataString(mpPaymentId)}");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var resp = await _http.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mercado Pago GetPayment falhou com HTTP {StatusCode} para pagamento {MpPaymentId}", (int)resp.StatusCode, mpPaymentId);
            throw new DomainException($"Não foi possível consultar o pagamento no Mercado Pago (HTTP {(int)resp.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var id = ReadIdString(root);
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "pending" : "pending";
        var statusDetail = root.TryGetProperty("status_detail", out var sd) ? sd.GetString() : null;
        var extRef = root.TryGetProperty("external_reference", out var er) ? er.GetString() : null;
        decimal? amount = root.TryGetProperty("transaction_amount", out var ta) && ta.ValueKind == JsonValueKind.Number
            ? ta.GetDecimal()
            : null;
        DateTimeOffset? dateApproved = null;
        if (root.TryGetProperty("date_approved", out var da)
            && da.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(da.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDa))
        {
            dateApproved = parsedDa.ToUniversalTime();
        }

        return new MpPixPaymentStatus(
            string.IsNullOrWhiteSpace(id) ? mpPaymentId : id,
            status,
            statusDetail,
            extRef,
            amount,
            dateApproved);
    }

    private static string ReadIdString(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idEl)) return string.Empty;
        return idEl.ValueKind switch
        {
            JsonValueKind.Number => idEl.GetRawText(),
            JsonValueKind.String => idEl.GetString() ?? string.Empty,
            _ => string.Empty
        };
    }
}