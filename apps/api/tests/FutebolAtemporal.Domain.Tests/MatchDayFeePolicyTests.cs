using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class MatchDayFeePolicyTests
{
    private static readonly TimeZoneInfo SaoPaulo = MatchDayFeePolicy.ResolveTimeZone("America/Sao_Paulo");

    [Fact]
    public void Due_is_next_local_midnight_in_group_time_zone()
    {
        // 23:30 em São Paulo (UTC-3) = 02:30 UTC do dia seguinte: vence à meia-noite local, não UTC.
        var closedAt = new DateTimeOffset(2026, 11, 28, 2, 30, 0, TimeSpan.Zero);
        var due = MatchDayFeePolicy.DueAt(closedAt, SaoPaulo);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.FromHours(-3)), due);
    }

    [Fact]
    public void Closing_after_local_midnight_anchors_on_the_closing_day()
    {
        var closedAt = new DateTimeOffset(2026, 11, 28, 0, 30, 0, TimeSpan.FromHours(-3));
        Assert.Equal(new DateTimeOffset(2026, 11, 29, 0, 0, 0, TimeSpan.FromHours(-3)), MatchDayFeePolicy.DueAt(closedAt, SaoPaulo));
    }

    [Fact]
    public void Unknown_time_zone_falls_back_to_utc()
        => Assert.Equal(TimeZoneInfo.Utc, MatchDayFeePolicy.ResolveTimeZone("Nowhere/Invalid"));

    [Fact]
    public void Pending_becomes_overdue_only_after_due()
    {
        var due = new DateTimeOffset(2026, 11, 28, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal(PaymentStatus.Pending, MatchDayFeePolicy.EffectiveStatus(PaymentStatus.Pending, due, due.AddTicks(-1)));
        Assert.Equal(PaymentStatus.Overdue, MatchDayFeePolicy.EffectiveStatus(PaymentStatus.Pending, due, due));
        Assert.Equal(PaymentStatus.Pending, MatchDayFeePolicy.EffectiveStatus(PaymentStatus.Pending, null, due.AddYears(1)));
        Assert.Equal(PaymentStatus.Paid, MatchDayFeePolicy.EffectiveStatus(PaymentStatus.Paid, due, due.AddDays(1)));
    }

    [Theory]
    [InlineData(true, false, 25, true)]
    [InlineData(false, false, 25, false)]
    [InlineData(true, true, 25, false)]
    [InlineData(true, false, 0, false)]
    public void Launch_only_for_present_non_monthly_with_positive_amount(bool present, bool monthly, int amount, bool expected)
        => Assert.Equal(expected, MatchDayFeePolicy.ShouldLaunch(present, monthly, amount));

    [Theory]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Overdue, PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.RefundPending)]
    public void Presence_removed_cancels_unpaid_and_refunds_paid(PaymentStatus from, PaymentStatus to)
        => Assert.Equal(to, MatchDayFeePolicy.OnPresenceRemoved(from));

    [Theory]
    [InlineData(PaymentStatus.Exempt)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.RefundPending)]
    [InlineData(PaymentStatus.Refunded)]
    public void Presence_removed_leaves_other_states(PaymentStatus from)
        => Assert.Null(MatchDayFeePolicy.OnPresenceRemoved(from));

    [Theory]
    [InlineData(PaymentStatus.RefundPending, PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.RefundPending, PaymentStatus.Pending, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.RefundPending, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Refunded, false)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Paid, true)]
    public void Manual_refund_transitions_are_restricted(PaymentStatus from, PaymentStatus to, bool allowed)
        => Assert.Equal(allowed, MatchDayFeePolicy.IsAllowedManualTransition(from, to));
}
