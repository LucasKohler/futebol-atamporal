namespace FutebolAtemporal.Domain.Services;

public sealed record DrawPlayer(Guid PlayerProfileId, decimal SkillRating, string DisplayName);
public sealed record DrawnTeam(string Name, IReadOnlyList<DrawPlayer> Players, decimal TotalSkill);
public sealed record DrawResult(string Seed, IReadOnlyList<DrawnTeam> Teams, decimal StrengthDelta);

/// <summary>
/// Sorteio simples e equilibrado: embaralha com seed, ordena por skill e faz snake-draft.
/// Adequado para peladas (~10–20 jogadores).
/// </summary>
public static class BalancedTeamDraw
{
    public static DrawResult Draw(IReadOnlyList<DrawPlayer> players, int teamCount, string? seed = null)
    {
        if (teamCount < 2) throw new ArgumentOutOfRangeException(nameof(teamCount));
        if (players.Count == 0) throw new ArgumentException("Nenhum jogador para sortear.", nameof(players));

        seed ??= Guid.NewGuid().ToString("N");
        var rng = new Random(StableHash(seed));

        var ordered = players
            .OrderByDescending(p => p.SkillRating)
            .ThenBy(_ => rng.Next())
            .ThenBy(p => p.PlayerProfileId)
            .ToList();

        var teams = Enumerable.Range(0, teamCount).Select(_ => new List<DrawPlayer>()).ToList();
        var totals = new decimal[teamCount];
        var direction = 1;
        var index = 0;

        foreach (var player in ordered)
        {
            teams[index].Add(player);
            totals[index] += player.SkillRating;
            index += direction;
            if (index == teamCount)
            {
                direction = -1;
                index = teamCount - 1;
            }
            else if (index < 0)
            {
                direction = 1;
                index = 0;
            }
        }

        var drawn = teams.Select((t, i) => new DrawnTeam($"Time {(char)('A' + i)}", t, totals[i])).ToList();
        var delta = totals.Max() - totals.Min();
        return new DrawResult(seed, drawn, delta);
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s)
                hash = hash * 31 + c;
            return hash;
        }
    }
}

public static class RankingCalculator
{
    public sealed record PlayerStats(
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

    public static IReadOnlyList<PlayerStats> Order(IEnumerable<PlayerStats> stats, string sortBy)
        => sortBy.ToLowerInvariant() switch
        {
            "winrate" => stats.Where(s => s.Matches >= 1)
                .OrderByDescending(s => s.WinRate).ThenByDescending(s => s.Matches)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            "rating" => stats.Where(s => s.Matches >= 1 && s.AverageRating.HasValue)
                .OrderByDescending(s => s.AverageRating).ThenByDescending(s => s.Matches)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            "motm" or "craque" => stats.OrderByDescending(s => s.ManOfTheMatch)
                .ThenByDescending(s => s.Matches)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            "penaltysaves" => stats.OrderByDescending(s => s.PenaltySaves)
                .ThenByDescending(s => s.BestGoalkeeper)
                .ThenByDescending(s => s.Matches)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => stats.OrderByDescending(s => s.Wins).ThenByDescending(s => s.Matches)
                .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
        };
}
