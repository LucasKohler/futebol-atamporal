using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Application.Payments;

public sealed record GatewayStatusDto(
    string Mode,
    bool IsConfigured,
    bool IsSimulated,
    bool WebhookSecretConfigured,
    decimal DefaultMonthlyFee,
    decimal DefaultMatchDayFee,
    int PixExpirationMinutes,
    string WebhookPath);

public sealed record PixChargeSummaryDto(
    Guid Id,
    string MpPaymentId,
    string Status,
    decimal Amount,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    bool IsSimulated);

public sealed record PixChargeDto(
    Guid Id,
    Guid GroupId,
    Guid PlayerProfileId,
    string PlayerName,
    string Kind,
    Guid? MembershipPaymentId,
    Guid? MatchFeePaymentId,
    Guid? MatchId,
    decimal Amount,
    string Description,
    string MpPaymentId,
    string? QrCode,
    string? QrCodeBase64,
    DateTimeOffset ExpiresAt,
    string Status,
    string ObligationStatus,
    bool IsSimulated,
    bool Reused,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt);

public sealed record PlayerPeriodDto(int Year, int Month, string Label);

public sealed record PlayerMembershipPaymentDto(
    Guid PaymentId,
    Guid MonthlyMembershipId,
    Guid BillingPeriodId,
    string PeriodLabel,
    int Year,
    int Month,
    decimal Amount,
    string Status,
    string PaymentSource,
    DateTimeOffset? PaidAt,
    PixChargeDto? ActivePix);

public sealed record PlayerMatchFeeDto(
    Guid? MatchFeeId,
    Guid MatchId,
    string MatchTitle,
    DateTimeOffset StartsAt,
    string Location,
    decimal Amount,
    string Status,
    string PaymentSource,
    DateTimeOffset? PaidAt,
    PixChargeDto? ActivePix,
    /// <summary>Vencimento (fim do dia do fechamento, fuso do grupo). Nulo = cobrança ainda não lançada no fechamento.</summary>
    DateTimeOffset? DueAt = null);

public sealed record MyPaymentsDto(
    GatewayStatusDto Gateway,
    bool IsMonthlyMember,
    PlayerPeriodDto CurrentPeriod,
    IReadOnlyList<PlayerMembershipPaymentDto> Memberships,
    IReadOnlyList<PlayerMatchFeeDto> MatchDays);

public sealed record CreateMatchDayPixRequest(Guid? PlayerProfileId = null, decimal? Amount = null);

public sealed record PixVerifyResultDto(
    PixChargeDto Charge,
    string ObligationStatus,
    bool WasUpdated,
    string Message);

public sealed record AdminMatchFeeDto(
    Guid? MatchFeeId,
    Guid MatchId,
    Guid PlayerProfileId,
    string PlayerName,
    string PriorityClass,
    decimal Amount,
    string Status,
    string PaymentSource,
    DateTimeOffset? PaidAt,
    string? Notes,
    PixChargeSummaryDto? LatestPix,
    DateTimeOffset? DueAt = null);

public sealed record UpsertMatchFeeRequest(decimal Amount, PaymentStatus Status, string? Notes);

public sealed record UpdatePaymentSettingsRequest(
    decimal DefaultMonthlyFee,
    decimal DefaultMatchDayFee,
    int PixExpirationMinutes);

public sealed record PixAdminKpisDto(
    int PendingCount,
    int ApprovedCount,
    int ExpiredCount,
    int CancelledCount,
    int ActiveCount,
    int PaidByPixCount,
    int PaidManualCount,
    int ExemptCount,
    decimal ReceivedPix,
    decimal ReceivedManual,
    decimal PendingAmount);

public sealed record AdminPixOverviewDto(
    GatewayStatusDto Gateway,
    PixAdminKpisDto Kpis,
    IReadOnlyList<PixChargeDto> Charges);

public sealed record PixReconcileResultDto(
    int Checked,
    int Approved,
    int Expired,
    int Cancelled,
    int StillPending,
    int Errors);

public sealed record MpCreatePixPaymentRequest(
    decimal Amount,
    string Description,
    string ExternalReference,
    DateTimeOffset ExpiresAt,
    string PayerEmail,
    string PayerName,
    string IdempotencyKey);

public sealed record MpPixPaymentResult(
    string MpPaymentId,
    string Status,
    string QrCode,
    string? QrCodeBase64,
    DateTimeOffset ExpiresAt,
    bool IsSimulated);

public sealed record MpPixPaymentStatus(
    string MpPaymentId,
    string Status,
    string? StatusDetail,
    string? ExternalReference,
    decimal? TransactionAmount,
    DateTimeOffset? DateApproved);

public interface IMercadoPagoClient
{
    string Mode { get; }
    bool IsConfigured { get; }
    bool IsSimulated { get; }
    Task<MpPixPaymentResult> CreatePixPaymentAsync(MpCreatePixPaymentRequest request, CancellationToken ct = default);
    Task<MpPixPaymentStatus> GetPaymentAsync(string mpPaymentId, CancellationToken ct = default);
}

public interface ISimulatedMercadoPagoControl
{
    void SetStatus(string mpPaymentId, string status, DateTimeOffset? approvedAt = null);
}

public interface IPixPaymentService
{
    Task<MyPaymentsDto> GetMyPaymentsAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<PixChargeDto> CreateOrReuseMembershipPixAsync(Guid userId, Guid membershipPaymentId, CancellationToken ct = default);
    Task<PixChargeDto> CreateOrReuseMatchDayPixAsync(Guid userId, Guid matchId, CreateMatchDayPixRequest? request, CancellationToken ct = default);
    Task<PixChargeDto> GetPixChargeAsync(Guid userId, Guid pixChargeId, CancellationToken ct = default);
    Task<PixVerifyResultDto> VerifyPixChargeAsync(Guid userId, Guid pixChargeId, CancellationToken ct = default);
    Task<PixVerifyResultDto> SimulateApproveAsync(Guid userId, Guid pixChargeId, CancellationToken ct = default);
    Task<bool> ProcessWebhookAsync(string? topic, string? dataId, string? xSignature, string? xRequestId, CancellationToken ct = default);
    Task<GatewayStatusDto> GetPaymentSettingsAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<GatewayStatusDto> UpdatePaymentSettingsAsync(Guid userId, Guid groupId, UpdatePaymentSettingsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AdminMatchFeeDto>> ListMatchFeesAsync(Guid userId, Guid groupId, Guid matchId, CancellationToken ct = default);
    Task<AdminMatchFeeDto> UpsertMatchFeeAsync(Guid userId, Guid groupId, Guid matchId, Guid playerProfileId, UpsertMatchFeeRequest request, CancellationToken ct = default);
    Task<AdminPixOverviewDto> GetAdminPixOverviewAsync(Guid userId, Guid groupId, PixChargeStatus? status, PixChargeKind? kind, int? year, int? month, Guid? matchId, CancellationToken ct = default);
    Task<PixReconcileResultDto> ReconcilePendingAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}