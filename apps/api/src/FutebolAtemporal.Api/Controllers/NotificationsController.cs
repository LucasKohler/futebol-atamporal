using FutebolAtemporal.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FutebolAtemporal.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public ActionResult<VapidPublicKeyDto> GetVapidPublicKey()
        => Ok(_notifications.GetVapidPublicKey());

    [HttpPost("subscriptions")]
    [Authorize]
    public async Task<ActionResult<SubscribePushResponseDto>> Subscribe([FromBody] SubscribePushRequest body, CancellationToken ct)
    {
        // Precondição no próprio POST: o cookie pode ter trocado de conta depois da última consulta de sessão do cliente.
        // A identidade esperada só condiciona a operação; o dono gravado continua sendo o principal autenticado.
        if (!SessionPrecondition.Matches(User, body.ExpectedUserId))
            return this.SessionChanged();

        var sessionId = FutebolAtemporal.Infrastructure.Identity.SessionCookieEvents.GetSessionId(User)
            ?? throw new UnauthorizedAccessException();
        return Ok(await _notifications.UpsertSubscriptionAsync(User.GetUserId(), sessionId, body, ct));
    }

    [HttpDelete("subscriptions")]
    [Authorize]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribePushRequest body, CancellationToken ct)
    {
        // Mesma precondição do POST: uma operação iniciada por A não pode remover a inscrição de B
        // quando o cookie trocou de conta. Divergência = 409 sem tocar em dados.
        if (!SessionPrecondition.Matches(User, body.ExpectedUserId))
            return this.SessionChanged();

        await _notifications.DeleteSubscriptionAsync(User.GetUserId(), body, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MyNotificationsResponseDto>> Me(
        [FromQuery] Guid groupId,
        [FromQuery] Guid? before,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await _notifications.GetMyNotificationsAsync(User.GetUserId(), groupId, before, limit, ct));

    [HttpPost("{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [Authorize]
    public async Task<ActionResult<ReadAllNotificationsResponseDto>> MarkAllRead([FromBody] ReadAllNotificationsRequest body, CancellationToken ct)
        => Ok(await _notifications.MarkAllReadAsync(User.GetUserId(), body.GroupId, ct));
}

[ApiController]
[Authorize]
[Route("api/groups/{groupId:guid}/admin/notifications")]
public sealed class AdminNotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public AdminNotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet("summary")]
    public async Task<ActionResult<AdminNotificationSummaryDto>> Summary(Guid groupId, CancellationToken ct)
        => Ok(await _notifications.GetAdminSummaryAsync(User.GetUserId(), groupId, ct));

    [HttpPost("preview")]
    public async Task<ActionResult<AdminNotificationPreviewDto>> Preview(
        Guid groupId,
        [FromBody] AdminNotificationPreviewRequest body,
        CancellationToken ct)
        => Ok(await _notifications.PreviewAdminNotificationAsync(User.GetUserId(), groupId, body, ct));

    [HttpPost("quick")]
    public async Task<ActionResult<CampaignDto>> Quick(
        Guid groupId,
        [FromBody] AdminQuickNotificationRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _notifications.SendQuickReminderAsync(User.GetUserId(), groupId, body, idempotencyKey, ct);
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Campaign)
            : Ok(result.Campaign);
    }

    [HttpPost("broadcast")]
    public async Task<ActionResult<CampaignDto>> Broadcast(
        Guid groupId,
        [FromBody] AdminBroadcastNotificationRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _notifications.SendBroadcastAsync(User.GetUserId(), groupId, body, idempotencyKey, ct);
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Campaign)
            : Ok(result.Campaign);
    }

    [HttpGet("campaigns")]
    public async Task<ActionResult<CampaignListResponseDto>> Campaigns(
        Guid groupId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _notifications.ListCampaignsAsync(User.GetUserId(), groupId, page, pageSize, ct));
}
