namespace FutebolAtemporal.Application.Results;

public sealed record CloseMatchRequest(
    int HomeScore,
    int AwayScore,
    Guid? WinnerTeamId,
    Guid? ManOfTheMatchPlayerProfileId,
    string? Notes,
    IReadOnlyList<PresenceItem> Presence,
    IReadOnlyList<RatingItem> Ratings);

public sealed record PresenceItem(Guid PlayerProfileId, bool Present);
public sealed record RatingItem(Guid PlayerProfileId, decimal Score, string? Notes);

public sealed record MatchResultDto(
    Guid MatchId,
    int HomeScore,
    int AwayScore,
    Guid? WinnerTeamId,
    Guid? ManOfTheMatchPlayerProfileId,
    string? Notes,
    DateTimeOffset ClosedAt);

public sealed record RankingRowDto(
    Guid PlayerProfileId,
    string DisplayName,
    int Matches,
    int Wins,
    int Losses,
    int Absences,
    int ManOfTheMatch,
    decimal? AverageRating,
    decimal WinRate,
    int PenaltySaves = 0,
    int BestGoalkeeper = 0);

public interface IResultService
{
    Task<MatchResultDto> CloseAsync(Guid userId, Guid matchId, CloseMatchRequest request, CancellationToken ct = default);
    Task<MatchResultDto?> GetAsync(Guid userId, Guid matchId, CancellationToken ct = default);
}

public interface IStatsService
{
    Task<IReadOnlyList<RankingRowDto>> GetRankingsAsync(Guid userId, Guid groupId, string sortBy, CancellationToken ct = default);
    Task<object?> GetPlayerHistoryAsync(Guid userId, Guid playerProfileId, CancellationToken ct = default);
}
