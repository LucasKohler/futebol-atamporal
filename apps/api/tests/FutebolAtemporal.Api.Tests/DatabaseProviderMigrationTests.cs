using FutebolAtemporal.Infrastructure;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

public class DatabaseProviderMigrationTests
{
    [Theory]
    [InlineData(true, typeof(AppDbContext), "_InitialCreate", "_PostgreSqlBaseline")]
    [InlineData(false, typeof(PostgresAppDbContext), "_PostgreSqlBaseline", "_InitialCreate")]
    public void Each_provider_uses_its_own_migrations_and_current_snapshot(
        bool useSqlite, Type expectedContext, string expectedBaseline, string otherBaseline)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:UseSqlite"] = useSqlite ? "true" : "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.IsType(expectedContext, db);
        Assert.Equal(useSqlite, db.Database.IsSqlite());
        Assert.Equal(!useSqlite, db.Database.IsNpgsql());
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith(expectedBaseline, migrations[0], StringComparison.Ordinal);
        Assert.EndsWith(useSqlite ? "_MatchRegistrationWindows" : "_PwaNotificationsAndUserSessions",
            migrations[^1], StringComparison.Ordinal);
        var queue = Array.FindIndex(migrations, id => id.EndsWith("_MatchRegistrationWindows", StringComparison.Ordinal));
        var pwa = Array.FindIndex(migrations, id => id.EndsWith(useSqlite ? "_UserSessions" : "_PwaNotificationsAndUserSessions", StringComparison.Ordinal));
        Assert.True(queue >= 0 && pwa >= 0);
        Assert.True(useSqlite ? pwa < queue : queue < pwa);
        Assert.DoesNotContain(migrations, id => id.EndsWith(otherBaseline, StringComparison.Ordinal));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
