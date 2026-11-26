using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Application.Memberships;

public sealed record MonthlyMemberDto(
    Guid Id,
    Guid PlayerProfileId,
    string DisplayName,
    string Status,
    DateTimeOffset StartedAt);

public sealed record BillingPeriodDto(Guid Id, int Year, int Month, string Label);

public sealed record PaymentDto(
    Guid Id,
    Guid MonthlyMembershipId,
    string PlayerName,
    Guid BillingPeriodId,
    string PeriodLabel,
    decimal Amount,
    string Status,
    DateTimeOffset? PaidAt,
    string? Notes,
    Guid PlayerProfileId = default,
    string PaymentSource = "None",
    PixChargeSummaryDto? LatestPix = null);

public sealed record UpsertPaymentRequest(decimal Amount, PaymentStatus Status, string? Notes);

public interface IMembershipService
{
    Task<IReadOnlyList<MonthlyMemberDto>> ListMembersAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<MonthlyMemberDto> AddMemberAsync(Guid userId, Guid groupId, Guid playerProfileId, CancellationToken ct = default);
    Task<MonthlyMemberDto> SetStatusAsync(Guid userId, Guid groupId, Guid membershipId, MonthlyMembershipStatus status, CancellationToken ct = default);
    Task<BillingPeriodDto> EnsurePeriodAsync(Guid userId, Guid groupId, int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentDto>> ListPaymentsAsync(Guid userId, Guid groupId, int? year, int? month, PaymentStatus? status, CancellationToken ct = default);
    Task<PaymentDto> UpsertPaymentAsync(Guid userId, Guid groupId, Guid membershipId, Guid periodId, UpsertPaymentRequest request, CancellationToken ct = default);
}