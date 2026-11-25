namespace FutebolAtemporal.Application.Results;

// ---------- Entrada: súmula da rodada de três cores ----------

/// <summary>PlayedGoalkeeper nulo = usa a função do sorteio confirmado (goleiro titular).</summary>
public sealed record RoundPresenceInput(Guid PlayerProfileId, bool Present, bool? PlayedGoalkeeper);

public sealed record RoundGameInput(Guid HomeTeamId, Guid AwayTeamId, int HomeScore, int AwayScore);

/// <summary>Game = número do confronto (1, 2, 3...). OwnGoal = gol contra (conta para o adversário, fora da artilharia).</summary>
public sealed record RoundGoalInput(int Game, Guid PlayerProfileId, bool OwnGoal);

/// <summary>Reason é obrigatório na correção (PUT) e ignorado no fechamento (POST).</summary>
public sealed record RoundSheetRequest(
    IReadOnlyList<RoundPresenceInput> Presence,
    IReadOnlyList<RoundGameInput> Games,
    IReadOnlyList<RoundGoalInput> Goals,
    Guid? BestPlayerProfileId,
    Guid? BestGoalkeeperProfileId,
    string? Notes,
    string? Reason);

// ---------- Saída: rodada ----------

public sealed record RoundTeamDto(
    Guid TeamId, string Name, string? Color,
    int Played, int Wins, int Draws, int Losses, int GoalsFor, int GoalsAgainst, int Points, bool IsRoundWinner);

public sealed record RoundGoalDto(Guid PlayerProfileId, string DisplayName, Guid TeamId, bool OwnGoal);

public sealed record RoundGameDto(
    int Order, Guid HomeTeamId, Guid AwayTeamId, int HomeScore, int AwayScore, string Outcome, IReadOnlyList<RoundGoalDto> Goals);

public sealed record RoundParticipantDto(
    Guid PlayerProfileId, string DisplayName, Guid TeamId, bool Present, bool PlayedGoalkeeper, int Goals, int OwnGoals);

public sealed record RoundRevisionInfoDto(int Revision, bool IsCurrent, DateTimeOffset CreatedAt, string? Reason);

/// <summary>
/// Rodada encerrada. IsLegacy = fechamento antigo (Time A/B): cores, goleiros e gols ficam indisponíveis (null/vazio).
/// Revisions só para administradores.
/// </summary>
public sealed record RoundResultDto(
    Guid MatchId,
    bool IsLegacy,
    int? Revision,
    bool IsCurrentRevision,
    DateTimeOffset ClosedAt,
    /// <summary>Data do jogo no fuso do grupo (yyyy-MM-dd), base da artilharia mensal/anual.</summary>
    string LocalDate,
    IReadOnlyList<RoundTeamDto> Teams,
    IReadOnlyList<RoundGameDto> Games,
    IReadOnlyList<RoundParticipantDto> Participants,
    Guid? BestPlayerProfileId,
    string? BestPlayerName,
    Guid? BestGoalkeeperProfileId,
    string? BestGoalkeeperName,
    string? Notes,
    int? LegacyHomeScore,
    int? LegacyAwayScore,
    Guid? LegacyWinnerTeamId,
    IReadOnlyList<RoundRevisionInfoDto> Revisions);

// ---------- Saída: temporada (artilharia, cores, rodadas) ----------

public sealed record ScorerDto(Guid PlayerProfileId, string DisplayName, int Goals, int Position, bool IsLeader);

public sealed record ColorStandingDto(string Color, int RoundsWon, int Wins, int Draws, int Losses, int GoalsFor, int GoalsAgainst);

public sealed record SeasonPlayerDto(
    Guid PlayerProfileId, string DisplayName,
    int RoundsPlayed, int RoundsWon, int GamesWon, int Goals, int OwnGoals, int BestPlayer, int BestGoalkeeper);

public sealed record SeasonRoundDto(
    Guid MatchId, string? Title, string LocalDate, bool IsLegacy, IReadOnlyList<string> WinnerColors, IReadOnlyList<string> Games);

/// <summary>
/// Período pela data local do jogo no fuso do grupo. Month* = mês pedido; Year* = ano pedido.
/// Jogadores, cores e rodadas usam o mês se informado, senão o ano. Rodadas antigas A/B não têm gols nem cores.
/// </summary>
public sealed record SeasonStatsDto(
    int Year,
    int? Month,
    string TimeZoneId,
    IReadOnlyList<ScorerDto> MonthScorers,
    IReadOnlyList<ScorerDto> YearScorers,
    IReadOnlyList<ColorStandingDto> Colors,
    IReadOnlyList<SeasonPlayerDto> Players,
    IReadOnlyList<SeasonRoundDto> Rounds);

public interface IRoundResultService
{
    Task<RoundResultDto> CloseRoundAsync(Guid userId, Guid matchId, RoundSheetRequest request, CancellationToken ct = default);
    Task<RoundResultDto> CorrectRoundAsync(Guid userId, Guid matchId, RoundSheetRequest request, CancellationToken ct = default);
    Task<RoundResultDto?> GetRoundAsync(Guid userId, Guid matchId, int? revision, CancellationToken ct = default);
    Task<SeasonStatsDto> GetSeasonAsync(Guid userId, Guid groupId, int? year, int? month, CancellationToken ct = default);
}
