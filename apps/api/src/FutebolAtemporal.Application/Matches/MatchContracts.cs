using System.Text.Json;
using System.Text.Json.Serialization;
using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Application.Matches;

public sealed record CreateMatchRequest(
    Guid GroupId,
    string? Title,
    DateTimeOffset StartsAt,
    string Location,
    int PlayerLimit,
    bool OpenRegistrations);

public sealed record UpdateMatchRequest(
    string? Title,
    DateTimeOffset StartsAt,
    string Location,
    int PlayerLimit,
    string? Notes);

public sealed record MatchDto(
    Guid Id,
    Guid GroupId,
    string? Title,
    DateTimeOffset StartsAt,
    string Location,
    int PlayerLimit,
    string Status,
    string? Notes,
    int ConfirmedCount,
    int WaitlistCount,
    int SpotsRemaining);

public sealed record RegistrationPlayerDto(
    Guid RegistrationId,
    Guid PlayerProfileId,
    string DisplayName,
    string Status,
    string PriorityClass,
    int? WaitlistPosition,
    decimal? SkillRating); // somente para administradores do grupo

public sealed record MatchDetailDto(
    MatchDto Match,
    string? MyStatus,
    int? MyWaitlistPosition,
    IReadOnlyList<RegistrationPlayerDto> Confirmed,
    IReadOnlyList<RegistrationPlayerDto> Waitlist,
    int Version,
    string GroupTimeZoneId,
    RegistrationWindowsDto RegistrationWindows);

public sealed record RegistrationWindowsDto(
    bool IsConfigured,
    DateTimeOffset? MonthlyOpensAt,
    DateTimeOffset? CasualOpensAt,
    DateTimeOffset ServerNowUtc,
    string MyEligibility,
    bool IsAdminEarlyBypass,
    bool CanJoinNow,
    DateTimeOffset? MyOpensAt,
    string? ReasonCode);

public sealed record JoinMatchRequest(string? Reason);
public sealed record UpdateRegistrationWindowsRequest(
    [property: JsonRequired] string? MonthlyOpensAt, [property: JsonRequired] string? CasualOpensAt,
    int? ExpectedVersion, string? Reason);
public sealed record MoveWaitlistRequest(Guid PlayerProfileId, int TargetPosition, int? ExpectedVersion, string? Reason);
public sealed record AdminEnrollRequest(
    Guid PlayerProfileId, string Placement, int? TargetWaitlistPosition, Guid? DemotePlayerProfileId,
    int? ExpectedVersion, string? Reason);
public sealed record AdjustRegistrationStatusRequest(
    string TargetStatus, int? TargetWaitlistPosition, bool? PromoteNext, Guid? DemotePlayerProfileId,
    int? ExpectedVersion, string? Reason, int? DemoteToPosition = null);

public sealed record WaitlistAuditDto(
    Guid Id, Guid ActorUserId, DateTimeOffset CreatedAt, string Action, string Reason, int Version,
    JsonElement Before, JsonElement After, JsonElement Details);

public interface IMatchService
{
    Task<MatchDto> CreateAsync(Guid userId, CreateMatchRequest request, CancellationToken ct = default);
    Task<MatchDto> UpdateAsync(Guid userId, Guid matchId, UpdateMatchRequest request, CancellationToken ct = default);
    Task<MatchDto> TransitionAsync(Guid userId, Guid matchId, MatchStatus target, CancellationToken ct = default);
    Task<MatchDetailDto?> GetAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task<MatchDetailDto?> GetUpcomingAsync(Guid userId, Guid groupId, CancellationToken ct = default);
    Task<IReadOnlyList<MatchDto>> ListAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}

public interface IRegistrationService
{
    Task<MatchDetailDto> JoinAsync(Guid userId, Guid matchId, JoinMatchRequest? request, CancellationToken ct = default);
    Task<MatchDetailDto> UpdateWindowsAsync(Guid userId, Guid matchId, UpdateRegistrationWindowsRequest request, CancellationToken ct);
    Task<MatchDetailDto> MoveAsync(Guid userId, Guid matchId, MoveWaitlistRequest request, CancellationToken ct);
    Task<MatchDetailDto> AdminEnrollAsync(Guid userId, Guid matchId, AdminEnrollRequest request, CancellationToken ct);
    Task<MatchDetailDto> AdjustStatusAsync(Guid userId, Guid matchId, Guid playerProfileId, AdjustRegistrationStatusRequest request, CancellationToken ct);
    Task<IReadOnlyList<WaitlistAuditDto>> GetAuditAsync(Guid userId, Guid matchId, int limit, int offset, CancellationToken ct);
    Task<MatchDetailDto> LeaveAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task<MatchDetailDto> AdminRemoveAsync(Guid userId, Guid matchId, Guid playerProfileId, CancellationToken ct = default);
}
