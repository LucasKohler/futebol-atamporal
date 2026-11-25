using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class RoundSheetTests
{
    private static readonly Guid Red = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Black = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Green = Guid.Parse("00000000-0000-0000-0000-0000000000a3");
    private static readonly Guid[] Teams = [Red, Black, Green];

    private static Guid P(int i) => new($"00000000-0000-0000-0000-{i:D12}");

    // Vermelho: P1 (gol), P2, P3 · Preto: P4 (gol), P5, P6 · Verde: P7, P8, P9 (P9 ausente)
    private static List<RoundParticipant> Lineup() =>
    [
        new(P(1), Red, true, true), new(P(2), Red, true, false), new(P(3), Red, true, false),
        new(P(4), Black, true, true), new(P(5), Black, true, false), new(P(6), Black, true, false),
        new(P(7), Green, true, false), new(P(8), Green, true, false), new(P(9), Green, false, false)
    ];

    private static RoundSheet Sheet(IReadOnlyList<RoundGame> games, IReadOnlyList<RoundGoal> goals, Guid? best = null, Guid? keeper = null)
        => new(Lineup(), games, goals, best, keeper, null);

    [Fact]
    public void Valid_round_derives_standings_and_shared_winner_from_scores()
    {
        // Vermelho 2x1 Preto (P2, P3 / P5); Preto 1x0 Verde (P6); Verde 1x1 Vermelho (gol contra de P2 para o Verde; P3 para o Vermelho)
        var sheet = Sheet(
            [new(1, Red, Black, 2, 1), new(2, Black, Green, 1, 0), new(3, Green, Red, 1, 1)],
            [new(1, P(2), false), new(1, P(3), false), new(1, P(5), false),
             new(2, P(6), false),
             new(3, P(2), true), new(3, P(3), false)],
            best: P(3), keeper: P(4));

        RoundRules.Validate(sheet, Teams);
        var st = RoundRules.Standings(sheet, Teams).ToDictionary(s => s.TeamId);

        Assert.Equal((1, 1, 0, 4), (st[Red].Wins, st[Red].Draws, st[Red].Losses, st[Red].Points));
        Assert.Equal((1, 0, 1, 3), (st[Black].Wins, st[Black].Draws, st[Black].Losses, st[Black].Points));
        Assert.Equal((0, 1, 1, 1), (st[Green].Wins, st[Green].Draws, st[Green].Losses, st[Green].Points));
        Assert.True(st[Red].IsRoundWinner);
        Assert.False(st[Black].IsRoundWinner);

        var lines = RoundRules.PlayerLines(sheet, Teams).ToDictionary(l => l.PlayerProfileId);
        Assert.Equal(1, lines[P(2)].Goals);      // o gol contra não conta
        Assert.Equal(1, lines[P(2)].OwnGoals);
        Assert.Equal(2, lines[P(3)].Goals);
        Assert.True(lines[P(3)].BestPlayer);
        Assert.True(lines[P(4)].BestGoalkeeper);
        Assert.Equal(1, lines[P(1)].GamesWon);
        Assert.True(lines[P(1)].RoundWon);
        Assert.Equal(0, lines[P(9)].GamesWon); // ausente não soma vitória
    }

    [Fact]
    public void Tie_on_points_goal_difference_and_goals_shares_the_round()
    {
        var sheet = Sheet([new(1, Red, Black, 1, 1)], [new(1, P(2), false), new(1, P(5), false)]);
        var st = RoundRules.Standings(sheet, Teams).ToDictionary(s => s.TeamId);
        Assert.True(st[Red].IsRoundWinner);
        Assert.True(st[Black].IsRoundWinner);
        Assert.False(st[Green].IsRoundWinner); // não jogou
    }

    [Theory]
    [InlineData("placar")]
    [InlineData("ausente")]
    [InlineData("fora-do-jogo")]
    [InlineData("sem-autores")]
    [InlineData("mesma-cor")]
    [InlineData("goleiro-sem-gol")]
    [InlineData("melhor-ausente")]
    [InlineData("ordem")]
    public void Invalid_sheets_are_refused_with_reason(string kind)
    {
        RoundSheet sheet = kind switch
        {
            "placar" => Sheet([new(1, Red, Black, 2, 0)], [new(1, P(2), false)]),
            "ausente" => Sheet([new(1, Green, Red, 1, 0)], [new(1, P(9), false)]),
            "fora-do-jogo" => Sheet([new(1, Red, Black, 1, 0)], [new(1, P(7), false)]),
            "sem-autores" => Sheet([new(1, Red, Black, 1, 0)], []),
            "mesma-cor" => Sheet([new(1, Red, Red, 0, 0)], []),
            "goleiro-sem-gol" => Sheet([new(1, Red, Black, 0, 0)], [], keeper: P(2)),
            "melhor-ausente" => Sheet([new(1, Red, Black, 0, 0)], [], best: P(9)),
            _ => Sheet([new(2, Red, Black, 0, 0)], [])
        };
        var ex = Assert.Throws<DomainException>(() => RoundRules.Validate(sheet, Teams));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Own_goal_closes_the_opponent_side()
    {
        // Vermelho 0x1 Preto, gol contra de P1 (Vermelho)
        var sheet = Sheet([new(1, Red, Black, 0, 1)], [new(1, P(1), true)]);
        RoundRules.Validate(sheet, Teams);
        Assert.True(RoundRules.Standings(sheet, Teams).Single(s => s.TeamId == Black).IsRoundWinner);
    }

    [Fact]
    public void Reason_is_required_for_corrections()
    {
        Assert.Throws<DomainException>(() => RoundRules.EnsureReason(null));
        Assert.Throws<DomainException>(() => RoundRules.EnsureReason("  ok "));
        RoundRules.EnsureReason("Placar do 2º jogo lançado invertido");
    }

    [Fact]
    public void Scorer_table_shares_positions_and_leadership_on_ties()
    {
        var rows = ScorerTable.Rank([(P(1), 3), (P(2), 2), (P(3), 1), (P(2), 1), (P(4), 0), (P(5), 2)]);
        Assert.Equal([P(1), P(2), P(5), P(3)], rows.Select(r => r.PlayerProfileId).ToList());
        var byId = rows.ToDictionary(r => r.PlayerProfileId);
        Assert.Equal(1, byId[P(1)].Position);
        Assert.Equal(1, byId[P(2)].Position); // 3 gols somados, divide a liderança
        Assert.True(byId[P(1)].IsLeader);
        Assert.True(byId[P(2)].IsLeader);
        Assert.Equal(3, byId[P(5)].Position);
        Assert.Equal(4, byId[P(3)].Position);
        Assert.DoesNotContain(rows, r => r.PlayerProfileId == P(4));
        Assert.Empty(ScorerTable.Rank([]));
    }
}
