namespace FutebolAtemporal.Application.WeeklyLoop;

public interface IWeeklyLoopService
{
    Task<WeeklyLoopDto> GetAsync(Guid userId, Guid groupId, CancellationToken ct = default);
}

public sealed record WeeklyLoopDto(
    IReadOnlyList<WeeklyLoopEventDto> Events,
    IReadOnlyList<Guid> ReturnNextWeekPlayerIds);

public sealed record WeeklyLoopEventDto(
    string Type,
    DateTimeOffset At,
    Guid? UserId,
    Guid? PlayerProfileId,
    Guid? MatchId,
    Guid? EntityId);
