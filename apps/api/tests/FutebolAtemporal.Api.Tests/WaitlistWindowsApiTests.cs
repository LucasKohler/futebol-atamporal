using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FutebolAtemporal.Api.Tests;

public class WaitlistWindowsApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Reason = "Ajuste operacional da rodada";
    private static readonly DateTimeOffset Monthly = new(2026, 11, 28, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Casual = Monthly.AddHours(2);
    private readonly TestClock _clock = new(Monthly.AddSeconds(-1));
    private readonly WebApplicationFactory<Program> _host;

    public WaitlistWindowsApiTests(WebApplicationFactory<Program> fixture)
    {
        _host = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=waitlist-{Guid.NewGuid():N}.db");
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock)));
        });
    }

    [Fact]
    public async Task Early_admin_self_join_is_waitlisted_idempotently_and_only_admin_can_see_audit()
    {
        var (admin, groupId, route) = await CreateMatchAsync();
        var (player, playerId) = await RegisterAsync("11988881201");
        var configured = await ConfigureAsync(admin, route);
        var before = await ReadAsync(await player.GetAsync(route));
        Assert.False(before.GetProperty("registrationWindows").GetProperty("canJoinNow").GetBoolean());
        Assert.True(configured.GetProperty("registrationWindows").GetProperty("isAdminEarlyBypass").GetBoolean());

        var denial = await player.PostAsync($"{route}/join", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denial.StatusCode);
        var problem = await denial.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("registration_window_closed", problem.GetProperty("code").GetString());
        Assert.Equal(Casual, problem.GetProperty("opensAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{route}/join", null)).StatusCode);
        _clock.Now = _clock.Now.AddMilliseconds(100);
        var early = await ReadAsync(await admin.PostAsJsonAsync($"{route}/join", new { reason = Reason }));
        Assert.Equal("Waitlisted", early.GetProperty("myStatus").GetString());
        Assert.Equal(1, early.GetProperty("myWaitlistPosition").GetInt32());
        Assert.Equal(0, early.GetProperty("match").GetProperty("confirmedCount").GetInt32());
        Assert.Equal(Version(early), Version(await ReadAsync(await admin.PostAsync($"{route}/join", null))));

        var audit = await ReadAsync(await admin.GetAsync($"{route}/waitlist/audit?limit=1"));
        Assert.Equal("AdminEarlySelfJoin", audit[0].GetProperty("action").GetString());
        Assert.Equal(Reason, audit[0].GetProperty("reason").GetString());
        Assert.Equal(Version(early), audit[0].GetProperty("version").GetInt32());
        Assert.Equal(0, audit[0].GetProperty("before").GetProperty("waitlist").GetArrayLength());
        Assert.Equal(1, audit[0].GetProperty("after").GetProperty("waitlist").GetArrayLength());
        Assert.Equal(2, (await ReadAsync(await admin.GetAsync($"{route}/waitlist/audit"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync($"{route}/waitlist/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await player.PostAsJsonAsync($"{route}/waitlist/move", new { playerProfileId = playerId, targetPosition = 1, expectedVersion = Version(early), reason = Reason })).StatusCode);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.GroupMemberships.SingleAsync(g => g.GroupId == groupId && g.Role == GroupRole.Player);
        var otherGroup = new FootballGroup { Id = Guid.NewGuid(), Name = "Outro grupo", Slug = "outro-fila" };
        db.FootballGroups.Add(otherGroup);
        db.GroupMemberships.Add(new GroupMembership
        {
            Id = Guid.NewGuid(), UserId = member.UserId, GroupId = otherGroup.Id, Role = GroupRole.Admin, Status = MembershipStatus.Active
        });
        db.GroupMemberships.Remove(member);
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync($"{route}/waitlist/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await player.PutAsJsonAsync($"{route}/registration-windows", new { monthlyOpensAt = Monthly, casualOpensAt = Casual, expectedVersion = Version(early), reason = Reason })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await EnrollAsync(admin, route, playerId, "Confirmed", Version(early))).StatusCode);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, true)]
    [InlineData(PaymentStatus.Paid, true)]
    [InlineData(PaymentStatus.Exempt, true)]
    [InlineData(PaymentStatus.Overdue, false)]
    [InlineData(PaymentStatus.Cancelled, false)]
    public async Task Billing_eligibility_is_checked_at_the_exact_openings(PaymentStatus paymentStatus, bool earlyAccess)
    {
        var (admin, groupId, route) = await CreateMatchAsync();
        var (player, profile) = await RegisterAsync("11988881202");
        await ConfigureAsync(admin, route);
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = new MonthlyMembership { Id = Guid.NewGuid(), GroupId = groupId, PlayerProfileId = profile };
            var period = new MembershipBillingPeriod { Id = Guid.NewGuid(), GroupId = groupId, Year = Monthly.Year, Month = Monthly.Month, Label = "Novembro" };
            db.MonthlyMemberships.Add(membership);
            db.MembershipBillingPeriods.Add(period);
            db.MembershipPayments.Add(new MembershipPayment
            {
                Id = Guid.NewGuid(), MonthlyMembershipId = membership.Id, BillingPeriodId = period.Id, Status = paymentStatus
            });
            await db.SaveChangesAsync();
        }

        _clock.Now = Monthly;
        var dto = await ReadAsync(await player.GetAsync($"/api/matches/upcoming?groupId={groupId}"));
        var windows = dto.GetProperty("registrationWindows");
        Assert.Equal(earlyAccess ? "Monthly" : "Casual", windows.GetProperty("myEligibility").GetString());
        Assert.Equal(earlyAccess, windows.GetProperty("canJoinNow").GetBoolean());
        var result = await player.PostAsync($"{route}/join", null);
        if (!earlyAccess)
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, result.StatusCode);
            Assert.Equal("casual_window_not_open", (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            _clock.Now = Casual;
            result = await player.PostAsync($"{route}/join", null);
        }
        Assert.Equal("Confirmed", (await ReadAsync(result)).GetProperty("myStatus").GetString());
    }

    [Fact]
    public async Task Admin_adjustments_preserve_capacity_queue_order_and_reject_stale_requests()
    {
        var (admin, _, route) = await CreateMatchAsync();
        var (_, a) = await RegisterAsync("11988881211");
        var (_, b) = await RegisterAsync("11988881212");
        var (_, c) = await RegisterAsync("11988881213");
        var (_, d) = await RegisterAsync("11988881214");
        var configuration = await ConfigureAsync(admin, route);
        var first = await ReadAsync(await EnrollAsync(admin, route, a, "Confirmed", Version(configuration)));
        var second = await ReadAsync(await EnrollAsync(admin, route, b, "Confirmed", Version(first)));
        var third = await ReadAsync(await EnrollAsync(admin, route, c, "Waitlist", Version(second)));
        var fourth = await ReadAsync(await EnrollAsync(admin, route, d, "Waitlist", Version(third)));

        var move = new { playerProfileId = d, targetPosition = 1, expectedVersion = Version(fourth), reason = Reason };
        var moved = await ReadAsync(await admin.PostAsJsonAsync($"{route}/waitlist/move", move));
        Assert.Equal(new[] { d, c }, Ids(moved, "waitlist"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{route}/waitlist/move", move)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{route}/waitlist/move", move with { expectedVersion = Version(moved), targetPosition = 3 })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{route}/registrations/{d}/status",
            new { targetStatus = "Confirmed", expectedVersion = Version(moved), reason = Reason })).StatusCode);
        var swapped = await ReadAsync(await admin.PostAsJsonAsync($"{route}/registrations/{d}/status",
            new { targetStatus = "Confirmed", demotePlayerProfileId = a, demoteToPosition = 1, expectedVersion = Version(moved), reason = Reason }));
        Assert.Equal(new[] { a, c }, Ids(swapped, "waitlist"));
        Assert.Equal(2, Ids(swapped, "confirmed").Length);

        var demoted = await ReadAsync(await admin.PostAsJsonAsync($"{route}/registrations/{b}/status",
            new { targetStatus = "Waitlisted", targetWaitlistPosition = 1, expectedVersion = Version(swapped), reason = Reason }));
        Assert.Equal(new[] { b, c }, Ids(demoted, "waitlist"));
        Assert.Contains(a, Ids(demoted, "confirmed"));
        Assert.DoesNotContain(b, Ids(demoted, "confirmed"));
        var events = await ReadAsync(await admin.GetAsync($"{route}/waitlist/audit"));
        Assert.Equal(8, events.GetArrayLength());
    }

    [Fact]
    public async Task Windows_reject_missing_offsets_and_can_be_cleared_explicitly()
    {
        var (admin, _, route) = await CreateMatchAsync();
        var configured = await ConfigureAsync(admin, route);
        var partial = await admin.PutAsJsonAsync($"{route}/registration-windows",
            new { monthlyOpensAt = Monthly, expectedVersion = Version(configured), reason = Reason });
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        var ambiguous = await admin.PutAsJsonAsync($"{route}/registration-windows",
            new { monthlyOpensAt = "2026-11-28T12:00:00", casualOpensAt = Casual, expectedVersion = Version(configured), reason = Reason });
        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        var cleared = await ReadAsync(await admin.PutAsJsonAsync($"{route}/registration-windows",
            new { monthlyOpensAt = (string?)null, casualOpensAt = (string?)null, expectedVersion = Version(configured), reason = Reason }));
        Assert.False(cleared.GetProperty("registrationWindows").GetProperty("isConfigured").GetBoolean());
        Assert.Equal("Confirmed", (await ReadAsync(await admin.PostAsync($"{route}/join", null))).GetProperty("myStatus").GetString());
    }

    [Fact]
    public async Task Only_one_concurrent_admin_action_with_same_version_is_saved()
    {
        var (admin, _, route) = await CreateMatchAsync();
        var (_, a) = await RegisterAsync("11988881231");
        var (_, b) = await RegisterAsync("11988881232");
        var before = await ConfigureAsync(admin, route);
        var responses = await Task.WhenAll(
            EnrollAsync(admin, route, a, "Waitlist", Version(before)),
            EnrollAsync(admin, route, b, "Waitlist", Version(before)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var after = await ReadAsync(await admin.GetAsync(route));
        Assert.Single(Ids(after, "waitlist"));
        Assert.Equal(Version(before) + 1, Version(after));
        var audit = await ReadAsync(await admin.GetAsync($"{route}/waitlist/audit"));
        Assert.Equal(2, audit.GetArrayLength());
    }

    [Fact]
    public async Task Confirmed_roster_change_invalidates_the_draw_and_cannot_bypass_freeze()
    {
        var (admin, _, route) = await CreateMatchAsync(playerLimit: 3);
        var (aClient, a) = await RegisterAsync("11988881241");
        var (bClient, _) = await RegisterAsync("11988881242");
        await ReadAsync(await admin.PostAsync($"{route}/join", null));
        await ReadAsync(await aClient.PostAsync($"{route}/join", null));
        await ReadAsync(await bClient.PostAsync($"{route}/join", null));
        await ReadAsync(await admin.PostAsJsonAsync($"{route}/transition", new { status = "RegistrationClosed" }));
        var draw = await ReadAsync(await admin.PostAsJsonAsync($"{route}/draw", new { seed = "waitlist-guard-test" }));
        var drawId = draw.GetProperty("id").GetGuid();
        await ReadAsync(await admin.PostAsync($"{route}/draw/{drawId}/confirm", null));
        var drawn = await ReadAsync(await admin.GetAsync(route));
        var mutation = new { targetStatus = "Waitlisted", promoteNext = false, expectedVersion = Version(drawn), reason = Reason };
        var changed = await ReadAsync(await admin.PostAsJsonAsync($"{route}/registrations/{a}/status", mutation));
        Assert.Equal("RegistrationClosed", changed.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal(new[] { a }, Ids(changed, "waitlist"));
        var currentDraw = await ReadAsync(await admin.GetAsync($"{route}/draw"));
        Assert.Equal("Draft", currentDraw.GetProperty("status").GetString());

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var match = await db.Matches.SingleAsync();
            match.Status = MatchStatus.InProgress;
            await db.SaveChangesAsync();
        }

        var frozen = await admin.PostAsJsonAsync($"{route}/registrations/{a}/status",
            mutation with { targetStatus = "Confirmed", expectedVersion = Version(changed) });
        Assert.Equal(HttpStatusCode.BadRequest, frozen.StatusCode);
        var audit = await ReadAsync(await admin.GetAsync($"{route}/waitlist/audit"));
        Assert.Single(audit.EnumerateArray());
    }

    [Fact]
    public async Task Sqlite_migration_roundtrip_keeps_legacy_positions_and_membership_category()
    {
        var (admin, _, route) = await CreateMatchAsync();
        var (aClient, _) = await RegisterAsync("11988881251");
        var (bClient, b) = await RegisterAsync("11988881252");
        await ReadAsync(await admin.PostAsync($"{route}/join", null));
        await ReadAsync(await aClient.PostAsync($"{route}/join", null));
        await ReadAsync(await bClient.PostAsync($"{route}/join", null));

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registration = await db.MatchRegistrations.SingleAsync(r => r.PlayerProfileId == b);
        registration.PriorityClass = PriorityClass.Monthly;
        await db.SaveChangesAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261127034343_MatchFeeDueAt");
        await migrator.MigrateAsync();

        var after = await ReadAsync(await admin.GetAsync(route));
        Assert.False(after.GetProperty("registrationWindows").GetProperty("isConfigured").GetBoolean());
        var waiting = Assert.Single(after.GetProperty("waitlist").EnumerateArray());
        Assert.Equal(b, waiting.GetProperty("playerProfileId").GetGuid());
        Assert.Equal(1, waiting.GetProperty("waitlistPosition").GetInt32());
        Assert.Equal("Monthly", waiting.GetProperty("priorityClass").GetString());
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_registration_and_match_version()
    {
        var (admin, _, route) = await CreateMatchAsync();
        var (_, player) = await RegisterAsync("11988881221");
        var before = await ConfigureAsync(admin, route);
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_waitlist_audit BEFORE INSERT ON AuditEntries
                WHEN NEW.Action = 'AdminEnrolledOutsideWindow'
                BEGIN SELECT RAISE(ABORT, 'forced test failure'); END
                """);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, (await EnrollAsync(admin, route, player, "Confirmed", Version(before))).StatusCode);
        var after = await ReadAsync(await admin.GetAsync(route));
        Assert.Empty(Ids(after, "confirmed"));
        Assert.Empty(Ids(after, "waitlist"));
        Assert.Equal(Version(before), Version(after));
        var events = await ReadAsync(await admin.GetAsync($"{route}/waitlist/audit"));
        Assert.Equal(1, events.GetArrayLength());
    }

    private async Task<(HttpClient Admin, Guid GroupId, string Route)> CreateMatchAsync(int playerLimit = 2)
    {
        var admin = _host.CreateClient();
        (await TestAuth.LoginAsync(admin, "11999999999")).EnsureSuccessStatusCode();
        var me = await ReadAsync(await admin.GetAsync("/api/auth/me"));
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var match = await ReadAsync(await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Fila teste", startsAt = Casual.AddHours(6), location = "Arena", playerLimit, openRegistrations = true
        }));
        return (admin, groupId, $"/api/matches/{match.GetProperty("id").GetGuid()}");
    }

    private async Task<(HttpClient Client, Guid Profile)> RegisterAsync(string phone)
    {
        var client = _host.CreateClient();
        var registered = await ReadAsync(await client.PostAsJsonAsync("/api/auth/register",
            new { phone, displayName = "Jogador de teste", password = TestAuth.Password }));
        return (client, registered.GetProperty("playerProfileId").GetGuid());
    }

    private static async Task<JsonElement> ConfigureAsync(HttpClient admin, string route)
    {
        var detail = await ReadAsync(await admin.GetAsync(route));
        return await ReadAsync(await admin.PutAsJsonAsync($"{route}/registration-windows", new
        {
            monthlyOpensAt = Monthly.ToOffset(TimeSpan.FromHours(-3)),
            casualOpensAt = Casual.ToOffset(TimeSpan.FromHours(-3)),
            expectedVersion = Version(detail), reason = Reason
        }));
    }

    private static Task<HttpResponseMessage> EnrollAsync(HttpClient admin, string route, Guid target, string placement, int version)
        => admin.PostAsJsonAsync($"{route}/registrations/admin-enroll",
            new { playerProfileId = target, placement, expectedVersion = version, reason = Reason });

    private static int Version(JsonElement detail) => detail.GetProperty("version").GetInt32();
    private static Guid[] Ids(JsonElement detail, string list)
        => detail.GetProperty(list).EnumerateArray().Select(r => r.GetProperty("playerProfileId").GetGuid()).ToArray();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
