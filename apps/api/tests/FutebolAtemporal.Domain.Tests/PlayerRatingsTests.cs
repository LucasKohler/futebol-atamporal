using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Exceptions;
using FutebolAtemporal.Domain.Services;

namespace FutebolAtemporal.Domain.Tests;

public class PlayerRatingsTests
{
    [Fact]
    public void New_profile_has_no_goalkeeper_rating()
    {
        var profile = new PlayerProfile();
        Assert.False(profile.CanPlayGoalkeeper);
        Assert.Null(profile.GoalkeeperRating);
    }

    [Fact]
    public void Zero_is_a_valid_rating_and_distinct_from_absent()
    {
        var profile = new PlayerProfile();
        PlayerRatings.Apply(profile, 0m, canPlayGoalkeeper: true, goalkeeperRating: 0m);
        Assert.Equal(0m, profile.SkillRating);
        Assert.Equal(0m, profile.GoalkeeperRating);

        PlayerRatings.Apply(profile, 6.5m, canPlayGoalkeeper: true, goalkeeperRating: null);
        Assert.True(profile.CanPlayGoalkeeper);
        Assert.Null(profile.GoalkeeperRating);
    }

    [Fact]
    public void Line_and_goalkeeper_ratings_are_independent()
    {
        var profile = new PlayerProfile();
        PlayerRatings.Apply(profile, 3m, canPlayGoalkeeper: true, goalkeeperRating: 10m);
        Assert.Equal(3m, profile.SkillRating);
        Assert.Equal(10m, profile.GoalkeeperRating);
    }

    [Theory]
    [InlineData(-0.01, null)]
    [InlineData(10.01, null)]
    [InlineData(5, -1.0)]
    [InlineData(5, 11.0)]
    [InlineData(5.555, null)]
    public void Out_of_range_or_too_precise_is_rejected(double line, double? gk)
    {
        var profile = new PlayerProfile { SkillRating = 5m };
        Assert.Throws<DomainException>(() =>
            PlayerRatings.Apply(profile, (decimal)line, canPlayGoalkeeper: true, goalkeeperRating: (decimal?)gk));
        Assert.Equal(5m, profile.SkillRating);
    }

    [Fact]
    public void Goalkeeper_rating_requires_aptitude()
    {
        var profile = new PlayerProfile();
        Assert.Throws<DomainException>(() => PlayerRatings.Apply(profile, 5m, canPlayGoalkeeper: false, goalkeeperRating: 7m));
    }
}
