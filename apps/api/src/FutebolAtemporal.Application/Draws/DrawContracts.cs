namespace FutebolAtemporal.Application.Draws;

/// <summary>Notas (SkillRating = linha, GoalkeeperRating) só vêm para administradores; para jogadores são null.</summary>
public sealed record DrawMemberDto(
    Guid PlayerProfileId,
    string DisplayName,
    string Role,
    decimal? SkillRating,
    decimal? GoalkeeperRating);

/// <summary>Color é null em sorteios antigos (Time A/B). Métricas de nota só para administradores.</summary>
public sealed record DrawTeamDto(
    Guid Id,
    string Name,
    string? Color,
    int SortOrder,
    int LineCount,
    decimal? LineSum,
    decimal? LineAverage,
    string? GoalkeeperName,
    decimal? GoalkeeperRating,
    IReadOnlyList<DrawMemberDto> Members,
    decimal? TotalSkill);

/// <summary>
/// Prévia/escalação com métricas em dois eixos separados (linha e goleiro).
/// RosterChanged: a lista de confirmados mudou depois do sorteio e ele precisa ser refeito ou revisado.
/// Para jogadores: só sorteio confirmado, sem notas, métricas, avisos nem bloqueios.
/// </summary>
public sealed record DrawDto(
    Guid Id,
    Guid MatchId,
    string Seed,
    string Status,
    int TeamCount,
    string? AlgorithmVersion,
    bool IsLegacy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    decimal? StrengthDelta,
    decimal? LineAverageSpread,
    decimal? LineSumSpread,
    int LineCountSpread,
    int TotalCountSpread,
    int GoalkeepersAssigned,
    decimal? GoalkeeperSpread,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    bool RosterChanged,
    IReadOnlyList<string> RosterChanges,
    bool CanConfirm,
    bool IsLocked,
    IReadOnlyList<DrawTeamDto> Teams);

public interface IDrawService
{
    Task<DrawDto> CreateDrawAsync(Guid userId, Guid matchId, string? seed, CancellationToken ct = default);
    Task<DrawDto> ConfirmDrawAsync(Guid userId, Guid matchId, Guid drawId, CancellationToken ct = default);
    Task<DrawDto?> GetCurrentAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task<DrawDto> SwapPlayersAsync(Guid userId, Guid matchId, Guid drawId, Guid playerA, Guid playerB, CancellationToken ct = default);
}
