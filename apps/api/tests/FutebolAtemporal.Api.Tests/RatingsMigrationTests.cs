using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FutebolAtemporal.Api.Tests;

/// <summary>SO-1 (SQLite): atualizar uma base da versão anterior mantém a nota de linha e não inventa nota de goleiro.</summary>
public class RatingsMigrationTests
{
    [Fact]
    public async Task Upgrade_from_previous_version_keeps_line_rating_and_leaves_goalkeeper_empty()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source=upgrade-{Guid.NewGuid():N}.db")
            .Options;
        var profileId = Guid.NewGuid();

        await using (var db = new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260820000000_UserNickname");
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO PlayerProfiles (Id, UserId, SkillRating, CreatedAt) VALUES ({0}, {1}, {2}, {3})",
                profileId, Guid.NewGuid(), 7.25m, DateTimeOffset.UtcNow);
            await db.Database.CloseConnectionAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var profile = await db.PlayerProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId);
            Assert.Equal(7.25m, profile.SkillRating);
            Assert.False(profile.CanPlayGoalkeeper);
            Assert.Null(profile.GoalkeeperRating);
        }
    }
}
