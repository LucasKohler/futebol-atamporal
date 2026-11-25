using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

/// <summary>Política de preservação do elenco: muda até o sorteio; congela a partir do início.</summary>
public class LineupFreezePolicyTests
{
    [Theory]
    [InlineData(MatchStatus.RegistrationOpen)]
    [InlineData(MatchStatus.RegistrationClosed)]
    [InlineData(MatchStatus.Drawn)]
    public void Withdrawal_is_allowed_until_the_match_starts(MatchStatus status)
    {
        RegistrationPolicy.EnsureCanWithdraw(status);
        Assert.False(RegistrationPolicy.IsLineupFrozen(status));
    }

    [Theory]
    [InlineData(MatchStatus.InProgress)]
    [InlineData(MatchStatus.AwaitingClose)]
    public void Started_match_freezes_lineup(MatchStatus status)
    {
        Assert.True(RegistrationPolicy.IsLineupFrozen(status));
        var ex = Assert.Throws<DomainException>(() => RegistrationPolicy.EnsureCanWithdraw(status));
        Assert.Contains("já começou", ex.Message);
    }

    [Theory]
    [InlineData(MatchStatus.Completed)]
    [InlineData(MatchStatus.Cancelled)]
    public void Closed_match_refuses_withdrawal(MatchStatus status)
        => Assert.Throws<DomainException>(() => RegistrationPolicy.EnsureCanWithdraw(status));
}
