using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

public class MatchRegistrationFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MatchRegistrationFlowTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=flow-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminEmail", "admin@test.local");
            builder.UseSetting("Seed:AdminPassword", "Admin123!");
        });
    }

    [Fact]
    public async Task Join_fills_spots_then_waitlist_and_promote_on_leave()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });

        var meRes = await client.GetAsync("/api/auth/me");
        var me = await meRes.Content.ReadFromJsonAsync<JsonElement>();
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var create = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Pelada Teste",
            startsAt = DateTimeOffset.UtcNow.AddDays(3),
            location = "Arena",
            playerLimit = 2,
            openRegistrations = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var match = await create.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = match.GetProperty("id").GetGuid();

        // Admin joins -> confirmed
        var join1 = await client.PostAsync($"/api/matches/{matchId}/join", null);
        Assert.Equal(HttpStatusCode.OK, join1.StatusCode);
        var d1 = await join1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", d1.GetProperty("myStatus").GetString());

        // Second player
        var reg = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"p2-{Guid.NewGuid():N}@test.local",
            password = "Player123!",
            displayName = "Jogador Dois"
        });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        var join2 = await client.PostAsync($"/api/matches/{matchId}/join", null);
        Assert.Equal(HttpStatusCode.OK, join2.StatusCode);
        var d2 = await join2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", d2.GetProperty("myStatus").GetString());
        Assert.Equal(2, d2.GetProperty("match").GetProperty("confirmedCount").GetInt32());

        // Third -> waitlist
        var reg3 = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"p3-{Guid.NewGuid():N}@test.local",
            password = "Player123!",
            displayName = "Jogador Tres"
        });
        Assert.Equal(HttpStatusCode.OK, reg3.StatusCode);
        var join3 = await client.PostAsync($"/api/matches/{matchId}/join", null);
        Assert.Equal(HttpStatusCode.OK, join3.StatusCode);
        var d3 = await join3.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Waitlisted", d3.GetProperty("myStatus").GetString());
        Assert.Equal(1, d3.GetProperty("myWaitlistPosition").GetInt32());

        // Leave as p3 does nothing for promotion; re-login as admin and leave to promote p3
        // Actually leave as player 2 (confirmed) while logged as p3 - need player 2 to leave
        // Login as player 2: we lost cookie. Register returns login. Current cookie is p3.
        // Leave as p3 (waitlisted) then login admin, remove one confirmed... simpler: admin remove second confirmed.

        await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });
        var detail = await (await client.GetAsync($"/api/matches/{matchId}")).Content.ReadFromJsonAsync<JsonElement>();
        var confirmed = detail.GetProperty("confirmed").EnumerateArray().ToList();
        Assert.Equal(2, confirmed.Count);
        var other = confirmed.First(c => c.GetProperty("displayName").GetString() != "Administrador");
        var otherProfile = other.GetProperty("playerProfileId").GetGuid();

        var remove = await client.PostAsync($"/api/matches/{matchId}/registrations/{otherProfile}/remove", null);
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        var after = await remove.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, after.GetProperty("match").GetProperty("confirmedCount").GetInt32());
        Assert.Equal(0, after.GetProperty("match").GetProperty("waitlistCount").GetInt32());
        var promoted = after.GetProperty("confirmed").EnumerateArray()
            .Any(c => c.GetProperty("displayName").GetString() == "Jogador Tres");
        Assert.True(promoted);
    }
}
