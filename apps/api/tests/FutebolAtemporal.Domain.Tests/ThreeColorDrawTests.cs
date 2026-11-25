using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class ThreeColorDrawTests
{
    private static Guid Id(int i) => new($"00000000-0000-0000-0000-{i:D12}");

    private static List<DrawCandidate> Roster(int count, int keepers = 0, Func<int, decimal>? line = null, Func<int, decimal?>? gk = null)
        => Enumerable.Range(1, count).Select(i => new DrawCandidate(
            Id(i), $"J{i:D2}", line?.Invoke(i) ?? (i % 10), i <= keepers, i <= keepers ? gk?.Invoke(i) ?? 7m : null)).ToList();

    private static string Fingerprint(ThreeColorDrawResult r)
        => string.Join(" / ", r.Teams.Select(t => t.Color + ":" + string.Join(",", t.Members.Select(m => $"{m.PlayerProfileId.ToString()[^2..]}{(m.Role == TeamRole.Goalkeeper ? "G" : "")}"))));

    [Fact]
    public void Always_three_named_colors_with_everyone_once()
    {
        var roster = Roster(15, keepers: 3);
        var r = ThreeColorDraw.Draw(roster, "abc");

        Assert.Equal(["Vermelho", "Preto", "Verde"], r.Teams.Select(t => t.Color));
        Assert.Equal([0, 1, 2], r.Teams.Select(t => t.SortOrder));
        var ids = r.Teams.SelectMany(t => t.Members).Select(m => m.PlayerProfileId).ToList();
        Assert.Equal(15, ids.Count);
        Assert.Equal(15, ids.Distinct().Count());
        Assert.Equal(ThreeColorDraw.AlgorithmVersion, r.AlgorithmVersion);
    }

    [Theory]
    [InlineData(13, 0)]
    [InlineData(14, 1)]
    [InlineData(15, 2)]
    [InlineData(16, 3)]
    [InlineData(17, 5)]
    [InlineData(10, 2)]
    public void Sizes_differ_by_at_most_one_in_total_and_in_line(int count, int keepers)
    {
        var r = ThreeColorDraw.Draw(Roster(count, keepers), "sizes");
        Assert.True(r.Balance.TotalCountSpread <= 1, Fingerprint(r));
        Assert.True(r.Balance.LineCountSpread <= 1, Fingerprint(r));
        Assert.All(r.Teams, t => Assert.True(t.Members.Count(m => m.Role == TeamRole.Goalkeeper) <= 1));
        Assert.Empty(r.Balance.Blockers);
    }

    [Theory]
    [InlineData(0, 0, 3)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 0)]
    [InlineData(5, 3, 0)]
    public void Goalkeepers_one_per_team_and_deficit_is_flagged(int apt, int expectedKeepers, int teamsWithout)
    {
        var r = ThreeColorDraw.Draw(Roster(15, apt), "gk");

        Assert.Equal(expectedKeepers, r.Balance.GoalkeepersAssigned);
        Assert.Equal(teamsWithout, r.Balance.Warnings.Count(w => w.Contains("sem goleiro apto")));
        if (apt > 3)
        {
            var aptOnLine = r.Teams.SelectMany(t => t.Members).Count(m => m.Role == TeamRole.Line && m.CanPlayGoalkeeper);
            Assert.Equal(apt - 3, aptOnLine);
            Assert.Contains(r.Balance.Warnings, w => w.StartsWith("Aptos ao gol jogando na linha"));
        }
    }

    [Fact]
    public void Strong_goalkeeper_never_compensates_weak_line()
    {
        // Linha: seis fortes (9) e seis fracos (1). Goleiros: um 10 e dois 0.
        // Se o goleiro entrasse na conta, um time com goleiro 10 poderia receber mais linha fraca.
        var roster = new List<DrawCandidate>();
        for (var i = 1; i <= 6; i++) roster.Add(new(Id(i), $"Forte{i}", 9m, false, null));
        for (var i = 7; i <= 12; i++) roster.Add(new(Id(i), $"Fraco{i}", 1m, false, null));
        roster.Add(new(Id(13), "Paredão", 1m, true, 10m));
        roster.Add(new(Id(14), "GolA", 1m, true, 0m));
        roster.Add(new(Id(15), "GolB", 1m, true, 0m));

        foreach (var seed in new[] { "a", "b", "c", "d", "e" })
        {
            var r = ThreeColorDraw.Draw(roster, seed);
            // Cada time: 4 de linha com 2 fortes e 2 fracos -> médias iguais (5,0) apesar dos goleiros desiguais.
            Assert.All(r.Balance.Teams, t =>
            {
                Assert.Equal(4, t.LineCount);
                Assert.Equal(20m, t.LineSum);
            });
            Assert.Equal(0m, r.Balance.LineAverageSpread);
            Assert.Equal(10m, r.Balance.GoalkeeperSpread);
        }
    }

    [Fact]
    public void Goalkeeper_choice_is_filtered_after_line_balance()
    {
        // 4 aptos: notas de gol 9, 8, 8 e 1. Com linha equilibrável, o trio escolhido deve ser o de menor amplitude (9,8,8).
        var roster = Roster(16, keepers: 4, line: _ => 5m, gk: i => i switch { 1 => 9m, 2 => 8m, 3 => 8m, _ => 1m });
        var r = ThreeColorDraw.Draw(roster, "gk-filter");
        var keepers = r.Teams.Select(t => t.Members.Single(m => m.Role == TeamRole.Goalkeeper).GoalkeeperRating).OrderBy(x => x).ToList();
        Assert.Equal([8m, 8m, 9m], keepers);
        Assert.Equal(0m, r.Balance.LineAverageSpread);
    }

    [Fact]
    public void Same_input_and_seed_reproduce_even_with_shuffled_input()
    {
        var roster = Roster(15, keepers: 3, line: i => (i * 7 % 11) / 1.1m);
        var a = ThreeColorDraw.Draw(roster, "semente-1");
        var b = ThreeColorDraw.Draw(roster.AsEnumerable().Reverse().ToList(), "semente-1");
        Assert.Equal(Fingerprint(a), Fingerprint(b));
        Assert.Equal("semente-1", a.Seed);
    }

    [Fact]
    public void Different_seeds_vary_among_balanced_candidates()
    {
        var roster = Roster(15, keepers: 3, line: _ => 6m, gk: _ => 7m);
        var fingerprints = Enumerable.Range(0, 12).Select(i => Fingerprint(ThreeColorDraw.Draw(roster, $"s{i}"))).Distinct().Count();
        Assert.True(fingerprints >= 5, $"apenas {fingerprints} variações");

        var colorsForPlayer1 = Enumerable.Range(0, 12)
            .Select(i => ThreeColorDraw.Draw(roster, $"s{i}").Teams.First(t => t.Members.Any(m => m.PlayerProfileId == Id(1))).Color)
            .Distinct().Count();
        Assert.True(colorsForPlayer1 >= 2, "a cor atribuída também deve variar");
    }

    [Fact]
    public void Zero_ratings_are_valid_and_missing_goalkeeper_rating_is_flagged()
    {
        var roster = Enumerable.Range(1, 9).Select(i => new DrawCandidate(
            Id(i), $"J{i}", 0m, i <= 3, i is 2 or 3 ? 0m : null)).ToList(); // J1 apto sem nota de goleiro
        var r = ThreeColorDraw.Draw(roster, "zero");
        Assert.Equal(0m, r.Balance.LineAverageSpread);
        Assert.Equal(3, r.Balance.GoalkeepersAssigned);
        Assert.Contains(r.Balance.Warnings, w => w.Contains("sem nota de goleiro"));
        Assert.Equal(0m, r.Balance.GoalkeeperSpread);
    }

    [Fact]
    public void Small_roster_prioritizes_line_players()
    {
        // 5 confirmados, 3 aptos: só 2 vão ao gol para sobrar ao menos 3 na linha.
        var r = ThreeColorDraw.Draw(Roster(5, keepers: 3), "small");
        Assert.Equal(2, r.Balance.GoalkeepersAssigned);
        Assert.Equal(3, r.Balance.Teams.Sum(t => t.LineCount));
        Assert.Empty(r.Balance.Blockers);
    }

    [Fact]
    public void Fewer_than_three_players_is_refused_with_reason()
    {
        var ex = Assert.Throws<DomainException>(() => ThreeColorDraw.Draw(Roster(2), "x"));
        Assert.Contains("ao menos 3", ex.Message);
    }

    [Fact]
    public void Evaluate_blocks_team_without_line_and_double_keeper()
    {
        var gk1 = new ColorTeamMember(Id(1), "G1", TeamRole.Goalkeeper, 5m, true, 7m);
        var gk2 = new ColorTeamMember(Id(2), "G2", TeamRole.Goalkeeper, 5m, true, 6m);
        var l1 = new ColorTeamMember(Id(3), "L1", TeamRole.Line, 5m, false, null);
        var l2 = new ColorTeamMember(Id(4), "L2", TeamRole.Line, 5m, false, null);
        var balance = ThreeColorDraw.Evaluate([
            new ColorTeam("Vermelho", 0, [gk1, gk2, l1]),
            new ColorTeam("Preto", 1, [l2]),
            new ColorTeam("Verde", 2, [])
        ]);
        Assert.Contains(balance.Blockers, b => b.Contains("Verde está sem jogador de linha"));
        Assert.Contains(balance.Blockers, b => b.Contains("mais de um goleiro"));
    }
}
