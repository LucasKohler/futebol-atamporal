using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class WaitlistRebalanceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static MatchRegistration Reg(RegistrationStatus status, int? position = null,
        PriorityClass priority = PriorityClass.Casual, int minute = 0) => new()
    {
        Id = Guid.NewGuid(),
        PlayerProfileId = Guid.NewGuid(),
        Status = status,
        WaitlistPosition = position,
        PriorityClass = priority,
        CreatedAt = T0.AddMinutes(minute)
    };

    [Fact]
    public void Withdrawal_promotes_first_in_queue_and_clears_its_position()
    {
        var leaving = Reg(RegistrationStatus.Confirmed);
        var stays = Reg(RegistrationStatus.Confirmed);
        var first = Reg(RegistrationStatus.Waitlisted, 1, minute: 1);
        var second = Reg(RegistrationStatus.Waitlisted, 2, minute: 2);
        var regs = new List<MatchRegistration> { leaving, stays, first, second };

        leaving.Status = RegistrationStatus.Withdrawn;
        var promoted = WaitlistLogic.Rebalance(regs, playerLimit: 2, T0);

        Assert.Equal([first], promoted);
        Assert.Equal(RegistrationStatus.Confirmed, first.Status);
        Assert.Null(first.WaitlistPosition);
        Assert.Equal(T0, first.ConfirmedAt);
        Assert.Equal(1, second.WaitlistPosition);
        Assert.Equal(2, regs.Count(r => r.Status == RegistrationStatus.Confirmed));
    }

    [Fact]
    public void Leaving_own_queue_reindexes_without_gaps_and_without_promotion()
    {
        var c1 = Reg(RegistrationStatus.Confirmed);
        var w1 = Reg(RegistrationStatus.Waitlisted, 1);
        var w2 = Reg(RegistrationStatus.Waitlisted, 2);
        var w3 = Reg(RegistrationStatus.Waitlisted, 3);
        var regs = new List<MatchRegistration> { c1, w1, w2, w3 };

        w2.Status = RegistrationStatus.Withdrawn;
        var promoted = WaitlistLogic.Rebalance(regs, playerLimit: 1, T0);

        Assert.Empty(promoted);
        Assert.Equal(1, w1.WaitlistPosition);
        Assert.Equal(2, w3.WaitlistPosition);
        Assert.Null(w2.WaitlistPosition);
    }

    [Fact]
    public void Persisted_position_wins_over_monthly_category()
    {
        var c1 = Reg(RegistrationStatus.Confirmed);
        var casual = Reg(RegistrationStatus.Waitlisted, 1, minute: 1);
        var monthly = Reg(RegistrationStatus.Waitlisted, 2, PriorityClass.Monthly, minute: 2);
        var regs = new List<MatchRegistration> { c1, casual, monthly };

        WaitlistLogic.Rebalance(regs, playerLimit: 1, T0);
        Assert.Equal(1, casual.WaitlistPosition);
        Assert.Equal(2, monthly.WaitlistPosition);

        c1.Status = RegistrationStatus.Removed;
        var promoted = WaitlistLogic.Rebalance(regs, playerLimit: 1, T0);
        Assert.Equal([casual], promoted);
        Assert.Equal(1, monthly.WaitlistPosition);
    }

    [Fact]
    public void Reentry_appends_and_manual_move_controls_next_promotion()
    {
        var first = Reg(RegistrationStatus.Waitlisted, 1, minute: 2);
        var second = Reg(RegistrationStatus.Waitlisted, 2, minute: 3);
        var returned = Reg(RegistrationStatus.Withdrawn, priority: PriorityClass.Monthly);
        var regs = new List<MatchRegistration> { first, second, returned };

        WaitlistLogic.SetPosition(regs, returned, null, T0);
        Assert.Equal(3, returned.WaitlistPosition);
        WaitlistLogic.SetPosition(regs, second, 1, T0);
        Assert.Equal([second], WaitlistLogic.Rebalance(regs, 1, T0));
        Assert.Equal(new int?[] { 1, 2 }, WaitlistLogic.QueueOrder(regs).Select(r => r.WaitlistPosition));
    }

    [Fact]
    public void Never_confirms_beyond_limit_and_fills_all_new_spots()
    {
        var regs = new List<MatchRegistration>
        {
            Reg(RegistrationStatus.Confirmed),
            Reg(RegistrationStatus.Waitlisted, 1),
            Reg(RegistrationStatus.Waitlisted, 2),
            Reg(RegistrationStatus.Waitlisted, 3)
        };

        WaitlistLogic.Rebalance(regs, playerLimit: 3, T0);

        Assert.Equal(3, regs.Count(r => r.Status == RegistrationStatus.Confirmed));
        var remaining = Assert.Single(regs, r => r.Status == RegistrationStatus.Waitlisted);
        Assert.Equal(1, remaining.WaitlistPosition);
    }
}
