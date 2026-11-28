using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class RegistrationWindowPolicyTests
{
    private static readonly DateTimeOffset Opening = new(2026, 11, 27, 21, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, -1, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 60, false)]
    public void Opening_is_inclusive_for_each_category(bool monthly, int minutes, bool early)
    {
        var match = new Match
        {
            MonthlyRegistrationOpensAt = Opening,
            CasualRegistrationOpensAt = Opening.AddHours(1)
        };

        Assert.Equal(early, RegistrationWindowPolicy.IsEarly(RegistrationWindowPolicy.OpensAt(match, monthly), Opening.AddMinutes(minutes)));
    }

    [Fact]
    public void Windows_are_optional_but_must_be_complete_ordered_and_no_later_than_start()
    {
        Assert.False(RegistrationWindowPolicy.IsEarly(RegistrationWindowPolicy.OpensAt(new Match(), false), Opening));
        RegistrationWindowPolicy.Validate(null, null, Opening);
        RegistrationWindowPolicy.Validate(Opening.AddHours(-1), Opening, Opening);
        Assert.Throws<DomainException>(() => RegistrationWindowPolicy.Validate(Opening, null, Opening));
        Assert.Throws<DomainException>(() => RegistrationWindowPolicy.Validate(Opening, Opening, Opening));
        Assert.Throws<DomainException>(() => RegistrationWindowPolicy.Validate(Opening, Opening.AddSeconds(1), Opening));
    }
}
