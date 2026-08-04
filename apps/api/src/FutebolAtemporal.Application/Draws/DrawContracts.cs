namespace FutebolAtemporal.Application.Draws;

public sealed record DrawMemberDto(Guid PlayerProfileId, string DisplayName, decimal SkillRating);
public sealed record DrawTeamDto(Guid Id, string Name, int SortOrder, decimal TotalSkill, IReadOnlyList<DrawMemberDto> Members);
public sealed record DrawDto(
    Guid Id,
    Guid MatchId,
    string Seed,
    string Status,
    int TeamCount,
    decimal StrengthDelta,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DrawTeamDto> Teams);

public interface IDrawService
{
    Task<DrawDto> CreateDrawAsync(Guid userId, Guid matchId, string? seed, CancellationToken ct = default);
    Task<DrawDto> ConfirmDrawAsync(Guid userId, Guid drawId, CancellationToken ct = default);
    Task<DrawDto?> GetCurrentAsync(Guid userId, Guid matchId, CancellationToken ct = default);
    Task SwapPlayersAsync(Guid userId, Guid drawId, Guid playerA, Guid playerB, CancellationToken ct = default);
}
