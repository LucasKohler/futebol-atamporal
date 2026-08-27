using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class PhoneAndDisplayNameTests
{
    [Theory]
    [InlineData("(11) 99999-9999", "11999999999")]
    [InlineData("55 11 98888-0001", "11988880001")]
    [InlineData("+55 11 99999-9999", "11999999999")]
    [InlineData("11988880001", "11988880001")]
    [InlineData("11 98888-0001", "11988880001")]
    public void Normalizes_brazilian_mobile(string raw, string expected)
    {
        Assert.True(PhoneNormalizer.TryNormalize(raw, out var phone, out _));
        Assert.Equal(expected, phone);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("admin@futebolatemporal.local")]
    public void Rejects_invalid_phone(string raw)
    {
        Assert.False(PhoneNormalizer.TryNormalize(raw, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Nickname_wins_over_registered_name()
    {
        Assert.Equal("Zico", PlayerDisplayName.Effective("Arthur Antunes", "Zico"));
        Assert.Equal("Arthur Antunes", PlayerDisplayName.Effective("Arthur Antunes", "  "));
        Assert.Equal("Jogador", PlayerDisplayName.Effective("", null));
    }
}
