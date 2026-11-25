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
    IReadOnlyList<RegistrationPlayerDto> Waitlist);

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
    Task<MatchDetailDto> JoinAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task<MatchDetailDto> LeaveAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task<MatchDetailDto> AdminRemoveAsync(Guid userId, Guid matchId, Guid playerProfileId, CancellationToken ct = default);
}
