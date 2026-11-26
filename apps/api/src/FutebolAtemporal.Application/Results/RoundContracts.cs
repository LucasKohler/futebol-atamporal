using FutebolAtemporal.Domain.Enums;

namespace FutebolAtemporal.Application.Results;

// ---------- Entrada: súmula da rodada de três cores ----------

/// <summary>PlayedGoalkeeper nulo = usa a função do sorteio confirmado (goleiro titular).</summary>
public sealed record RoundPresenceInput(Guid PlayerProfileId, bool Present, bool? PlayedGoalkeeper);

public sealed record RoundPenaltySaveInput(Guid GoalkeeperProfileId, int Count);

public sealed record RoundGameInput(
    Guid HomeTeamId,
    Guid AwayTeamId,
    int HomeScore,
    int AwayScore,
    Guid? PenaltyWinnerTeamId = null,
    IReadOnlyList<RoundPenaltySaveInput>? PenaltySaves = null,
    Guid? Id = null);

/// <summary>Game = número do confronto (1, 2, 3...). OwnGoal = gol contra (conta para o adversário, fora da artilharia).</summary>
public sealed record RoundGoalInput(int Game, Guid PlayerProfileId, bool OwnGoal, Guid? Id = null);

/// <summary>
/// Reason é obrigatório na correção (PUT) e ignorado no fechamento (POST).
/// ExpectedLiveVersion: quando informado no POST, fecha o dia usando os jogos do rascunho ao vivo no servidor.
/// </summary>
public sealed record RoundSheetRequest(
    IReadOnlyList<RoundPresenceInput>? Presence,
    IReadOnlyList<RoundGameInput>? Games,
    IReadOnlyList<RoundGoalInput>? Goals,
    Guid? BestPlayerProfileId,
    Guid? BestGoalkeeperProfileId,
    string? Notes,
    string? Reason,
    int? ExpectedLiveVersion = null);

// ---------- Entrada: modo ao vivo (jogo a jogo) ----------

public sealed record LiveGoalInput(Guid GoalId, Guid PlayerProfileId, bool OwnGoal);

public sealed record StartLiveGameRequest(
    Guid HomeTeamId,
    Guid AwayTeamId,
    int ExpectedVersion,
    int? InsertAt = null,
    IReadOnlyList<LiveGoalInput>? Goals = null,
    Guid? PenaltyWinnerTeamId = null,
    IReadOnlyList<RoundPenaltySaveInput>? PenaltySaves = null);

public sealed record AddLiveGoalRequest(
    Guid GoalId,
    Guid PlayerProfileId,
    bool OwnGoal,
    int ExpectedVersion);

public sealed record FinishLiveGameRequest(
    Guid? PenaltyWinnerTeamId,
    IReadOnlyList<RoundPenaltySaveInput>? PenaltySaves,
    int ExpectedVersion);

public sealed record UpdateLiveGameRequest(
    Guid HomeTeamId,
    Guid AwayTeamId,
    RoundGameStatus Status,
    IReadOnlyList<LiveGoalInput> Goals,
    Guid? PenaltyWinnerTeamId,
    IReadOnlyList<RoundPenaltySaveInput>? PenaltySaves,
    int ExpectedVersion);

public sealed record UpdateLivePresenceRequest(
    int ExpectedVersion,
    IReadOnlyList<RoundPresenceInput> Presence);

// ---------- Saída: modo ao vivo ----------

public sealed record RoundPenaltySaveDto(Guid GoalkeeperProfileId, string DisplayName, int Count);

public sealed record LiveTeamDto(Guid Id, string Name, string? Color);

public sealed record LiveGoalDto(Guid Id, Guid PlayerProfileId, string DisplayName, bool OwnGoal, Guid TeamCreditedId);

public sealed record LiveGameDto(
    Guid Id,
    int Order,
    string Status,
    Guid HomeTeamId,
    Guid AwayTeamId,
    int HomeScore,
    int AwayScore,
    bool DecidedByPenalties,
    IReadOnlyList<LiveGoalDto> Goals,
    Guid? PenaltyWinnerTeamId,
    Guid? WinnerTeamId,
    IReadOnlyList<RoundPenaltySaveDto> PenaltySaves);

public sealed record LiveStandingDto(
    Guid TeamId,
    int Played,
    int Wins,
    int WinsRegular,
    int WinsPenalties,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    bool IsDayLeader);

public sealed record LivePlayerDto(
    Guid PlayerProfileId,
    string DisplayName,
    Guid TeamId,
    bool Present,
    bool PlayedGoalkeeper,
    int Goals,
    int OwnGoals,
    int GamesWon,
    int GamesWonOnPenalties,
    int PenaltySaves,
    int? TeamGoalsConceded,
    bool SoleGoalkeeperForTeam);

public sealed record SuggestedNextGameDto(Guid HomeTeamId, Guid AwayTeamId);

public sealed record LiveDayDto(
    Guid MatchId,
    string LocalDate,
    string MatchStatus,
    int Version,
    IReadOnlyList<LiveTeamDto> Teams,
    IReadOnlyList<LiveGameDto> Games,
    IReadOnlyList<LiveStandingDto> Standings,
    IReadOnlyList<LivePlayerDto> Players,
    SuggestedNextGameDto? SuggestedNext);

// ---------- Saída: rodada ----------

public sealed record RoundTeamDto(
    Guid TeamId, string Name, string? Color,
    int Played, int Wins, int Draws, int Losses, int GoalsFor, int GoalsAgainst, int Points, bool IsRoundWinner,
    int WinsRegular = 0, int WinsPenalties = 0);

public sealed record RoundGoalDto(
    Guid PlayerProfileId, string DisplayName, Guid TeamId, bool OwnGoal,
    Guid Id = default, Guid TeamCreditedId = default);

public sealed record RoundGameDto(
    int Order, Guid HomeTeamId, Guid AwayTeamId, int HomeScore, int AwayScore, string Outcome, IReadOnlyList<RoundGoalDto> Goals,
    Guid Id = default, string Status = "Finished", bool DecidedByPenalties = false,
    Guid? PenaltyWinnerTeamId = null, Guid? WinnerTeamId = null, IReadOnlyList<RoundPenaltySaveDto>? PenaltySaves = null);

public sealed record RoundParticipantDto(
    Guid PlayerProfileId, string DisplayName, Guid TeamId, bool Present, bool PlayedGoalkeeper, int Goals, int OwnGoals,
    int GamesWon = 0, int GamesWonOnPenalties = 0, int PenaltySaves = 0);

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
    IReadOnlyList<RoundRevisionInfoDto> Revisions,
    int RulesVersion = 1);

// ---------- Saída: temporada (artilharia, cores, rodadas) ----------

public sealed record ScorerDto(Guid PlayerProfileId, string DisplayName, int Goals, int Position, bool IsLeader);

public sealed record ColorStandingDto(
    string Color, int RoundsWon, int Wins, int Draws, int Losses, int GoalsFor, int GoalsAgainst,
    int WinsRegular = 0, int WinsPenalties = 0);

public sealed record SeasonPlayerDto(
    Guid PlayerProfileId, string DisplayName,
    int RoundsPlayed, int RoundsWon, int GamesWon, int Goals, int OwnGoals, int BestPlayer, int BestGoalkeeper,
    int GamesWonOnPenalties = 0, int PenaltySaves = 0);

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

    Task<LiveDayDto?> GetLiveAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task<LiveDayDto> StartOrInsertLiveGameAsync(Guid userId, Guid matchId, StartLiveGameRequest request, CancellationToken ct = default);
    Task<LiveDayDto> AddLiveGoalAsync(Guid userId, Guid matchId, Guid gameId, AddLiveGoalRequest request, CancellationToken ct = default);
    Task<LiveDayDto> RemoveLiveGoalAsync(Guid userId, Guid matchId, Guid gameId, Guid goalId, int expectedVersion, CancellationToken ct = default);
    Task<LiveDayDto> FinishLiveGameAsync(Guid userId, Guid matchId, Guid gameId, FinishLiveGameRequest request, CancellationToken ct = default);
    Task<LiveDayDto> UpdateLiveGameAsync(Guid userId, Guid matchId, Guid gameId, UpdateLiveGameRequest request, CancellationToken ct = default);
    Task<LiveDayDto> DeleteLiveGameAsync(Guid userId, Guid matchId, Guid gameId, int expectedVersion, CancellationToken ct = default);
    Task<LiveDayDto> UpdateLivePresenceAsync(Guid userId, Guid matchId, UpdateLivePresenceRequest request, CancellationToken ct = default);
}
