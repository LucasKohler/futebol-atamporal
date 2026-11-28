using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Application.Notifications;

// ---------- Jogador ----------

public sealed record VapidPublicKeyDto(string PublicKey, string KeyId);

public sealed record SubscribePushKeysInput(string P256dh, string Auth);

/// <param name="ExpectedUserId">
/// Precondição: id (UUID) da conta que o cliente pretende inscrever. Deve ser igual ao usuário autenticado
/// desta requisição; senão a API responde 409 session_changed sem gravar. Nunca é usado como dono da inscrição.
/// Texto (e não Guid) para que ausente, vazio ou inválido também resulte em 409 e não em 400 de binding.
/// </param>
public sealed record SubscribePushRequest(
    string Endpoint,
    SubscribePushKeysInput Keys,
    string? UserAgent = null,
    string? DeviceLabel = null,
    string? ExpectedUserId = null);

public sealed record SubscribePushResponseDto(Guid Id, bool IsActive, string KeyId);

/// <param name="ExpectedUserId">Precondição obrigatória, como no POST: ausente/inválido/divergente = 409 session_changed sem remover.</param>
public sealed record UnsubscribePushRequest(string Endpoint, string? ExpectedUserId = null);

public sealed record NotificationDto(
    Guid Id,
    Guid GroupId,
    string Category,
    string Title,
    string Body,
    string ActionUrl,
    Guid? MatchId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record NotificationPushInfoDto(string Mode, string? KeyId);

public sealed record PushDeviceDto(
    Guid Id,
    string? DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSuccessAt,
    bool IsActive,
    bool Stale);

public sealed record MyNotificationsResponseDto(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount,
    Guid? NextCursor,
    NotificationPushInfoDto Push,
    IReadOnlyList<PushDeviceDto> Devices);

public sealed record ReadAllNotificationsRequest(Guid GroupId);

public sealed record ReadAllNotificationsResponseDto(int Updated);

// ---------- Admin ----------

public sealed record AdminNotificationSummaryDto(
    int MembersActive,
    int MembersWithPush,
    int ActiveDevices,
    int SentLast30d,
    string PushMode,
    DateTimeOffset? LastCampaignAt);

public sealed record AdminNotificationPreviewRequest(string? Audience = null, string? Quick = null);

public sealed record AdminNotificationPreviewDto(int Recipients, int PushDevices, Guid? MatchId);

public sealed record AdminQuickNotificationRequest(string Kind, string? Audience = null);

public sealed record AdminBroadcastNotificationRequest(
    string Audience,
    string Title,
    string Body,
    string ActionUrl);

public sealed record CampaignDto(
    Guid Id,
    string Source,
    string Category,
    string Audience,
    string Title,
    string ActionUrl,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    int RecipientCount,
    int PushDeviceCount,
    int PushSentCount,
    int PushFailedCount);

public sealed record CampaignListResponseDto(IReadOnlyList<CampaignDto> Items, int Total);

public sealed record CampaignDispatchResult(CampaignDto Campaign, bool Created);

// ---------- Sender / Outbox ----------

public enum WebPushSendOutcome
{
    Sent = 0,
    Gone = 1,
    ClientError = 2,
    KeyMismatch = 3,
    TransientError = 4
}

public sealed record WebPushTarget(Guid SubscriptionId, string Endpoint, string P256dh, string Auth);

public sealed record WebPushPayload(
    Guid Id,
    string Title,
    string Body,
    string Url,
    string Tag,
    bool HighUrgency);

public sealed record WebPushSendResult(WebPushSendOutcome Outcome, int? StatusCode, string? Reason = null);

public interface IWebPushSender
{
    Task<WebPushSendResult> SendAsync(WebPushTarget target, WebPushPayload payload, CancellationToken ct = default);
}

public interface INotificationDispatchRunner
{
    Task<int> ProcessPendingAsync(CancellationToken ct = default);
}

public interface INotificationService
{
    VapidPublicKeyDto GetVapidPublicKey();
    Task<SubscribePushResponseDto> UpsertSubscriptionAsync(Guid userId, Guid sessionId, SubscribePushRequest request, CancellationToken ct = default);
    Task DeleteSubscriptionAsync(Guid userId, UnsubscribePushRequest request, CancellationToken ct = default);
    Task<MyNotificationsResponseDto> GetMyNotificationsAsync(Guid userId, Guid groupId, Guid? before, int limit, CancellationToken ct = default);
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
    Task<ReadAllNotificationsResponseDto> MarkAllReadAsync(Guid userId, Guid groupId, CancellationToken ct = default);

    Task<AdminNotificationSummaryDto> GetAdminSummaryAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<AdminNotificationPreviewDto> PreviewAdminNotificationAsync(Guid userId, Guid groupId, AdminNotificationPreviewRequest request, CancellationToken ct = default);
    Task<CampaignDispatchResult> SendQuickReminderAsync(Guid userId, Guid groupId, AdminQuickNotificationRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<CampaignDispatchResult> SendBroadcastAsync(Guid userId, Guid groupId, AdminBroadcastNotificationRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<CampaignListResponseDto> ListCampaignsAsync(Guid userId, Guid groupId, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>
/// Gatilhos automáticos chamados dentro da transação do evento de domínio (D1, Seção 3).
/// Nunca lançam exceção que interrompa o evento principal.
/// </summary>
public interface INotificationTriggerService
{
    Task EnqueueMatchOpenAsync(Guid groupId, Guid matchId, Guid actorUserId, CancellationToken ct = default);
    Task EnqueueWaitlistPromotedAsync(Guid groupId, Guid matchId, IReadOnlyList<(Guid RegistrationId, Guid PlayerProfileId, DateTimeOffset ConfirmedAt)> promoted, CancellationToken ct = default);
    Task EnqueueDrawPublishedAsync(Guid groupId, Guid matchId, Guid drawId, DateTimeOffset confirmedAt, IReadOnlyList<(Guid PlayerProfileId, string TeamColor)> assignments, CancellationToken ct = default);
    Task EnqueueMatchCompletedAsync(Guid groupId, Guid matchId, IReadOnlyList<Guid> participantProfileIds, CancellationToken ct = default);
    void SignalDispatcher();
}
