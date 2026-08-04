using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class WaitlistAndDrawTests
{
    [Fact]
    public void Place_confirms_when_capacity_available()
    {
        var (status, pos) = WaitlistLogic.Place(10, 14, 0);
        Assert.Equal(RegistrationStatus.Confirmed, status);
        Assert.Null(pos);
    }

    [Fact]
    public void Place_waitlists_when_full()
    {
        var (status, pos) = WaitlistLogic.Place(14, 14, 2);
        Assert.Equal(RegistrationStatus.Waitlisted, status);
        Assert.Equal(3, pos);
    }

    [Fact]
    public void Draw_is_deterministic_with_seed()
    {
        var players = Enumerable.Range(1, 10)
            .Select(i => new DrawPlayer(Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), 5 + (i % 4), $"P{i}"))
            .ToList();

        var a = BalancedTeamDraw.Draw(players, 2, "seed-fixed");
        var b = BalancedTeamDraw.Draw(players, 2, "seed-fixed");

        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal(
            a.Teams[0].Players.Select(p => p.PlayerProfileId),
            b.Teams[0].Players.Select(p => p.PlayerProfileId));
    }

    [Fact]
    public void Draw_handles_odd_count()
    {
        var players = Enumerable.Range(1, 7)
            .Select(i => new DrawPlayer(Guid.NewGuid(), 7.0m, $"P{i}"))
            .ToList();
        var result = BalancedTeamDraw.Draw(players, 2, "odd");
        Assert.Equal(7, result.Teams.Sum(t => t.Players.Count));
        Assert.Equal(2, result.Teams.Count);
    }

    [Fact]
    public void Rankings_order_by_wins_then_matches()
    {
        var stats = new[]
        {
            new RankingCalculator.PlayerStats(Guid.NewGuid(), "Bruno", 5, 3, 2, 0, 0, null, 0.6m),
            new RankingCalculator.PlayerStats(Guid.NewGuid(), "Ana", 8, 3, 5, 0, 0, null, 0.375m),
            new RankingCalculator.PlayerStats(Guid.NewGuid(), "Carlos", 5, 3, 2, 0, 0, null, 0.6m),
        };
        var ordered = RankingCalculator.Order(stats, "wins");
        Assert.Equal("Ana", ordered[0].DisplayName);
        Assert.Equal("Bruno", ordered[1].DisplayName);
    }
}
