using System.Security.Cryptography;
using System.Text;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;

namespace FutebolAtemporal.Domain.Services;

/// <summary>Regras puras de cobrança Pix e validação de parâmetros financeiros.</summary>
public static class PixChargePolicy
{
    public const int MinExpirationMinutes = 30;
    public const int MaxExpirationMinutes = 1440;

    public static bool IsReusable(PixCharge charge, DateTimeOffset now)
        => charge.Status == PixChargeStatus.Pending && charge.ExpiresAt > now;

    public static bool IsExpired(PixCharge charge, DateTimeOffset now)
        => charge.Status == PixChargeStatus.Pending && charge.ExpiresAt <= now;

    public static PixChargeStatus MapProviderStatus(string? mpStatus)
        => (mpStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "approved" => PixChargeStatus.Approved,
            "cancelled" or "rejected" or "refunded" or "charged_back" => PixChargeStatus.Cancelled,
            _ => PixChargeStatus.Pending
        };

    public static void ValidateSettings(decimal defaultMonthlyFee, decimal defaultMatchDayFee, int pixExpirationMinutes)
    {
        if (defaultMonthlyFee < 0)
            throw new DomainException("O valor padrão da mensalidade não pode ser negativo.");
        if (defaultMatchDayFee < 0)
            throw new DomainException("O valor padrão da diária não pode ser negativo.");
        if (pixExpirationMinutes < MinExpirationMinutes || pixExpirationMinutes > MaxExpirationMinutes)
            throw new DomainException($"A expiração do Pix deve ficar entre {MinExpirationMinutes} e {MaxExpirationMinutes} minutos.");
    }

    public static void ValidateChargeAmount(decimal amount)
    {
        if (amount < 0.01m)
            throw new DomainException("O valor para gerar cobrança Pix deve ser maior que zero (mínimo R$ 0,01).");
    }

    public static int ClampExpirationMinutes(int minutes)
        => Math.Clamp(minutes, MinExpirationMinutes, MaxExpirationMinutes);
}

/// <summary>
/// Valida assinatura HMAC-SHA256 do header x-signature do Mercado Pago.
/// Formato: "ts=1700000000,v1=abcdef..." sobre o manifesto "id:{dataId};request-id:{requestId};ts:{ts};".
/// </summary>
public static class MercadoPagoSignatureValidator
{
    public static bool Validate(string? xSignatureHeader, string? xRequestId, string? dataId, string? webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrWhiteSpace(xSignatureHeader))
            return false;

        string? ts = null;
        string? v1 = null;

        foreach (var part in xSignatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;
            if (kv[0].Equals("ts", StringComparison.OrdinalIgnoreCase)) ts = kv[1];
            else if (kv[0].Equals("v1", StringComparison.OrdinalIgnoreCase)) v1 = kv[1];
        }

        if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(v1))
            return false;

        var manifest = BuildManifest(dataId, xRequestId, ts);
        var expectedHex = ComputeHmacSha256Hex(manifest, webhookSecret);

        try
        {
            var expectedBytes = Convert.FromHexString(expectedHex);
            var providedBytes = Convert.FromHexString(v1);
            return expectedBytes.Length == providedBytes.Length
                && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string BuildManifest(string? dataId, string? xRequestId, string ts)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(dataId))
            sb.Append("id:").Append(dataId.Trim().ToLowerInvariant()).Append(';');
        if (!string.IsNullOrWhiteSpace(xRequestId))
            sb.Append("request-id:").Append(xRequestId.Trim()).Append(';');
        sb.Append("ts:").Append(ts.Trim()).Append(';');
        return sb.ToString();
    }

    public static string ComputeHmacSha256Hex(string manifest, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}