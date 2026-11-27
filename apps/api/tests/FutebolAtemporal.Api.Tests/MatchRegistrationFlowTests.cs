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
            builder.UseSetting("Seed:AdminPhone", "11999999999");
        });
    }

    [Fact]
    public async Task Join_fills_spots_then_waitlist_and_promote_on_leave()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.LoginAsync(client, "11999999999");

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

        var join1 = await client.PostAsync($"/api/matches/{matchId}/join", null);
        Assert.Equal(HttpStatusCode.OK, join1.StatusCode);
        var d1 = await join1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", d1.GetProperty("myStatus").GetString());

        var reg = await client.PostAsJsonAsync("/api/auth/register", new
        {
            phone = "11988880002",
            displayName = "Jogador Dois", password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        var join2 = await client.PostAsync($"/api/matches/{matchId}/join", null);
        Assert.Equal(HttpStatusCode.OK, join2.StatusCode);
        var d2 = await join2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", d2.GetProperty("myStatus").GetString());
        Assert.Equal(2, d2.GetProperty("match").GetProperty("confirmedCount").GetInt32());

        var reg3 = await client.PostAsJsonAsync("/api/auth/register", new
        {
            phone = "11988880003",
            displayName = "Jogador Tres", password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg3.StatusCode);
        var join3 = await client.PostAsync($"/api/matches/{matchId}/join", null);
        Assert.Equal(HttpStatusCode.OK, join3.StatusCode);
        var d3 = await join3.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Waitlisted", d3.GetProperty("myStatus").GetString());
        Assert.Equal(1, d3.GetProperty("myWaitlistPosition").GetInt32());

        await TestAuth.LoginAsync(client, "11999999999");
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

    [Fact]
    public async Task Admin_nickname_is_used_on_match_list()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var player = await client.PostAsJsonAsync("/api/auth/register", new
        {
            phone = "11988880009",
            displayName = "Carlos Eduardo", password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, player.StatusCode);
        var playerMe = await player.Content.ReadFromJsonAsync<JsonElement>();
        var profileId = playerMe.GetProperty("playerProfileId").GetGuid();

        await TestAuth.LoginAsync(client, "11999999999");
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var nick = await client.PutAsJsonAsync($"/api/groups/{groupId}/players/{profileId}/nickname", new { nickname = "Cadu" });
        Assert.Equal(HttpStatusCode.OK, nick.StatusCode);

        var create = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Nick Test",
            startsAt = DateTimeOffset.UtcNow.AddDays(2),
            location = "Arena",
            playerLimit = 14,
            openRegistrations = true
        });
        var matchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await TestAuth.LoginAsync(client, "11988880009");
        var joined = await client.PostAsync($"/api/matches/{matchId}/join", null);
        var detail = await joined.Content.ReadFromJsonAsync<JsonElement>();
        var names = detail.GetProperty("confirmed").EnumerateArray().Select(c => c.GetProperty("displayName").GetString()).ToList();
        Assert.Contains("Cadu", names);
        Assert.DoesNotContain("Carlos Eduardo", names);
    }

    [Fact]
    public async Task Close_match_then_rankings_only_include_completed()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await TestAuth.LoginAsync(client, "11999999999");
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var adminProfile = me.GetProperty("playerProfileId").GetGuid();

        var create = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Fechamento",
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            location = "Arena",
            playerLimit = 14,
            openRegistrations = true
        });
        var matchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await client.PostAsync($"/api/matches/{matchId}/join", null);

        await client.PostAsJsonAsync("/api/auth/register", new { phone = "11988880021", displayName = "Jogador Fechamento", password = TestAuth.Password });
        await client.PostAsync($"/api/matches/{matchId}/join", null);
        var playerMe = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        var playerProfile = playerMe.GetProperty("playerProfileId").GetGuid();
        // Sorteio de três cores exige ao menos 3 confirmados.
        await client.PostAsJsonAsync("/api/auth/register", new { phone = "11988880022", displayName = "Terceiro Fechamento", password = TestAuth.Password });
        await client.PostAsync($"/api/matches/{matchId}/join", null);

        await TestAuth.LoginAsync(client, "11999999999");
        var closeList = await client.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationClosed" });
        Assert.Equal(HttpStatusCode.OK, closeList.StatusCode);
        var drawRes = await client.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = "close-test" });
        Assert.Equal(HttpStatusCode.OK, drawRes.StatusCode);
        var draw = await drawRes.Content.ReadFromJsonAsync<JsonElement>();
        var drawId = draw.GetProperty("id").GetGuid();
        var confirm = await client.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var teamA = draw.GetProperty("teams")[0].GetProperty("id").GetGuid();
        var teamB = draw.GetProperty("teams")[1].GetProperty("id").GetGuid();

        // Rodada de três cores fecha pela súmula; o fechamento A/B antigo é recusado.
        var legacy = await client.PostAsJsonAsync($"/api/matches/{matchId}/result/close", new
        {
            homeScore = 3, awayScore = 1, winnerTeamId = teamA, presence = Array.Empty<object>(), ratings = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);

        var close = await client.PostAsJsonAsync($"/api/matches/{matchId}/result/round", new
        {
            presence = new[]
            {
                new { playerProfileId = adminProfile, present = true },
                new { playerProfileId = playerProfile, present = true }
            },
            games = new[] { new { homeTeamId = teamA, awayTeamId = teamB, homeScore = 0, awayScore = 0, penaltyWinnerTeamId = teamA } },
            goals = Array.Empty<object>(),
            bestPlayerProfileId = playerProfile,
            notes = "ok"
        });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        var open = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Ainda aberta",
            startsAt = DateTimeOffset.UtcNow.AddDays(8),
            location = "Arena",
            playerLimit = 14,
            openRegistrations = true
        });
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);

        var ranks = await (await client.GetAsync($"/api/groups/{groupId}/rankings")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ranks.GetArrayLength() >= 1);
        var names = ranks.EnumerateArray().Select(r => r.GetProperty("displayName").GetString()).ToList();
        Assert.Contains("Jogador Fechamento", names);
        Assert.DoesNotContain("Ainda aberta", names);
        var row = ranks.EnumerateArray().First(r => r.GetProperty("displayName").GetString() == "Jogador Fechamento");
        Assert.Equal(1, row.GetProperty("matches").GetInt32());
        Assert.Equal(1, row.GetProperty("manOfTheMatch").GetInt32());
    }
}
