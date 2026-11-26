using System.Security.Claims;
using System.Text.Json;
using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutebolAtemporal.Api.Controllers;

[ApiController]
[Authorize]
public class PixPaymentsController : ControllerBase
{
    private readonly IPixPaymentService _pix;

    public PixPaymentsController(IPixPaymentService pix) => _pix = pix;

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("api/groups/{groupId:guid}/my-payments")]
    public async Task<ActionResult<MyPaymentsDto>> MyPayments(Guid groupId, CancellationToken ct)
        => Ok(await _pix.GetMyPaymentsAsync(CurrentUserId(), groupId, ct));

    [HttpPost("api/payments/mensalidades/{paymentId:guid}/pix")]
    public async Task<ActionResult<PixChargeDto>> CreateMembershipPix(Guid paymentId, CancellationToken ct)
        => Ok(await _pix.CreateOrReuseMembershipPixAsync(CurrentUserId(), paymentId, ct));

    [HttpPost("api/payments/diarias/{matchId:guid}/pix")]
    public async Task<ActionResult<PixChargeDto>> CreateMatchDayPix(Guid matchId, [FromBody] CreateMatchDayPixRequest? body, CancellationToken ct)
        => Ok(await _pix.CreateOrReuseMatchDayPixAsync(CurrentUserId(), matchId, body, ct));

    [HttpGet("api/payments/pix/{pixChargeId:guid}")]
    public async Task<ActionResult<PixChargeDto>> GetPixCharge(Guid pixChargeId, CancellationToken ct)
        => Ok(await _pix.GetPixChargeAsync(CurrentUserId(), pixChargeId, ct));

    [HttpPost("api/payments/pix/{pixChargeId:guid}/verify")]
    public async Task<ActionResult<PixVerifyResultDto>> VerifyPixCharge(Guid pixChargeId, CancellationToken ct)
        => Ok(await _pix.VerifyPixChargeAsync(CurrentUserId(), pixChargeId, ct));

    [HttpPost("api/payments/pix/{pixChargeId:guid}/simulate-approve")]
    public async Task<ActionResult<PixVerifyResultDto>> SimulateApprove(Guid pixChargeId, CancellationToken ct)
        => Ok(await _pix.SimulateApproveAsync(CurrentUserId(), pixChargeId, ct));

    [HttpGet("api/groups/{groupId:guid}/payments/settings")]
    public async Task<ActionResult<GatewayStatusDto>> GetSettings(Guid groupId, CancellationToken ct)
        => Ok(await _pix.GetPaymentSettingsAsync(CurrentUserId(), groupId, ct));

    [HttpPut("api/groups/{groupId:guid}/payments/settings")]
    public async Task<ActionResult<GatewayStatusDto>> UpdateSettings(Guid groupId, [FromBody] UpdatePaymentSettingsRequest body, CancellationToken ct)
        => Ok(await _pix.UpdatePaymentSettingsAsync(CurrentUserId(), groupId, body, ct));

    [HttpGet("api/groups/{groupId:guid}/matches/{matchId:guid}/fees")]
    public async Task<ActionResult<IReadOnlyList<AdminMatchFeeDto>>> ListMatchFees(Guid groupId, Guid matchId, CancellationToken ct)
        => Ok(await _pix.ListMatchFeesAsync(CurrentUserId(), groupId, matchId, ct));

    [HttpPut("api/groups/{groupId:guid}/matches/{matchId:guid}/fees/{playerProfileId:guid}")]
    public async Task<ActionResult<AdminMatchFeeDto>> UpsertMatchFee(Guid groupId, Guid matchId, Guid playerProfileId, [FromBody] UpsertMatchFeeRequest body, CancellationToken ct)
        => Ok(await _pix.UpsertMatchFeeAsync(CurrentUserId(), groupId, matchId, playerProfileId, body, ct));

    [HttpGet("api/groups/{groupId:guid}/payments/pix")]
    public async Task<ActionResult<AdminPixOverviewDto>> Overview(
        Guid groupId,
        [FromQuery] PixChargeStatus? status,
        [FromQuery] PixChargeKind? kind,
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] Guid? matchId,
        CancellationToken ct)
        => Ok(await _pix.GetAdminPixOverviewAsync(CurrentUserId(), groupId, status, kind, year, month, matchId, ct));

    [HttpPost("api/groups/{groupId:guid}/payments/pix/reconcile")]
    public async Task<ActionResult<PixReconcileResultDto>> Reconcile(Guid groupId, CancellationToken ct)
        => Ok(await _pix.ReconcilePendingAsync(CurrentUserId(), groupId, ct));
}

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/mercadopago")]
public class MercadoPagoWebhookController : ControllerBase
{
    private readonly IPixPaymentService _pix;

    public MercadoPagoWebhookController(IPixPaymentService pix) => _pix = pix;

    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] JsonElement? payload, CancellationToken ct)
    {
        var xSignature = Request.Headers["x-signature"].FirstOrDefault();
        var xRequestId = Request.Headers["x-request-id"].FirstOrDefault();

        var topic = Request.Query["type"].FirstOrDefault()
            ?? Request.Query["topic"].FirstOrDefault()
            ?? TryReadString(payload, "type")
            ?? TryReadString(payload, "topic");

        var dataId = Request.Query["data.id"].FirstOrDefault()
            ?? Request.Query["id"].FirstOrDefault()
            ?? TryReadNestedId(payload);

        var accepted = await _pix.ProcessWebhookAsync(topic, dataId, xSignature, xRequestId, ct);
        if (!accepted)
            return Unauthorized(new ProblemDetails
            {
                Title = "Assinatura inválida",
                Detail = "Cabeçalho x-signature ausente ou inválido.",
                Status = StatusCodes.Status401Unauthorized
            });

        return Ok(new { received = true });
    }

    private static string? TryReadString(JsonElement? payload, string prop)
    {
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Object) return null;
        return payload.Value.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
    }

    private static string? TryReadNestedId(JsonElement? payload)
    {
        if (payload is null || payload.Value.ValueKind != JsonValueKind.Object) return null;
        if (payload.Value.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("id", out var idEl))
        {
            return idEl.ValueKind switch
            {
                JsonValueKind.String => idEl.GetString(),
                JsonValueKind.Number => idEl.GetRawText(),
                _ => null
            };
        }
        return null;
    }
}