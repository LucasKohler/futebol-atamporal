using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

/// <summary>PR-1: última vaga disputada, retry, desistência da fila e privacidade das notas na lista.</summary>
public class PresenceQueueTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public PresenceQueueTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=queue-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> PlayerAsync(string phone, string name)
    {
        var client = NewClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone, displayName = name });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        return client;
    }

    private async Task<(HttpClient Admin, Guid MatchId)> AdminWithMatchAsync(int limit)
    {
        var admin = NewClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone });
        var groupId = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Fila", startsAt = DateTimeOffset.UtcNow.AddDays(2), location = "Arena",
            playerLimit = limit, openRegistrations = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        return (admin, (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid matchId)
        => await (await client.GetAsync($"/api/matches/{matchId}")).Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Concurrent_requests_for_last_spot_and_retry_keep_one_active_registration_each()
    {
        var (admin, matchId) = await AdminWithMatchAsync(limit: 2);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        var p1 = await PlayerAsync("11988882001", "Disputa Um");
        var p2 = await PlayerAsync("11988882002", "Disputa Dois");

        var responses = await Task.WhenAll(
            p1.PostAsync($"/api/matches/{matchId}/join", null),
            p2.PostAsync($"/api/matches/{matchId}/join", null),
            p1.PostAsync($"/api/matches/{matchId}/join", null)); // retry do mesmo jogador

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var detail = await DetailAsync(admin, matchId);
        var confirmed = detail.GetProperty("confirmed").EnumerateArray().ToList();
        var waitlist = detail.GetProperty("waitlist").EnumerateArray().ToList();
        Assert.Equal(2, confirmed.Count);
        Assert.Single(waitlist);
        Assert.Equal(1, waitlist[0].GetProperty("waitlistPosition").GetInt32());
        Assert.Equal(0, detail.GetProperty("match").GetProperty("spotsRemaining").GetInt32());

        var ids = confirmed.Concat(waitlist).Select(r => r.GetProperty("playerProfileId").GetGuid()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public async Task Leaving_from_queue_reindexes_and_confirmed_leave_promotes_next()
    {
        var (admin, matchId) = await AdminWithMatchAsync(limit: 2);
        await admin.PostAsync($"/api/matches/{matchId}/join", null);
        var p1 = await PlayerAsync("11988883001", "Confirmado");
        await p1.PostAsync($"/api/matches/{matchId}/join", null);
        var w1 = await PlayerAsync("11988883002", "Fila Um");
        await w1.PostAsync($"/api/matches/{matchId}/join", null);
        var w2 = await PlayerAsync("11988883003", "Fila Dois");
        await w2.PostAsync($"/api/matches/{matchId}/join", null);
        var w3 = await PlayerAsync("11988883004", "Fila Tres");
        var w3Join = await (await w3.PostAsync($"/api/matches/{matchId}/join", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, w3Join.GetProperty("myWaitlistPosition").GetInt32());

        var leftQueue = await w2.PostAsync($"/api/matches/{matchId}/leave", null);
        Assert.Equal(HttpStatusCode.OK, leftQueue.StatusCode);
        var afterQueueLeave = await DetailAsync(w3, matchId);
        Assert.Equal(2, afterQueueLeave.GetProperty("myWaitlistPosition").GetInt32());

        var leftConfirmed = await p1.PostAsync($"/api/matches/{matchId}/leave", null);
        Assert.Equal(HttpStatusCode.OK, leftConfirmed.StatusCode);

        var w1Detail = await DetailAsync(w1, matchId);
        Assert.Equal("Confirmed", w1Detail.GetProperty("myStatus").GetString());
        Assert.Equal(JsonValueKind.Null, w1Detail.GetProperty("myWaitlistPosition").ValueKind);
        var w3Detail = await DetailAsync(w3, matchId);
        Assert.Equal(1, w3Detail.GetProperty("myWaitlistPosition").GetInt32());

        var again = await p1.PostAsync($"/api/matches/{matchId}/leave", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Raising_limit_promotes_queue_and_lowering_below_confirmed_is_rejected()
    {
        var (admin, matchId) = await AdminWithMatchAsync(limit: 2);
        await admin.PostAsync($"/api/matches/{matchId}/join", null);
        foreach (var i in Enumerable.Range(1, 3))
            await (await PlayerAsync($"1198888400{i}", $"Limite {i}")).PostAsync($"/api/matches/{matchId}/join", null);

        var body = new { title = "Fila", startsAt = DateTimeOffset.UtcNow.AddDays(2), location = "Arena", playerLimit = 3, notes = (string?)null };
        var raise = await admin.PutAsJsonAsync($"/api/matches/{matchId}", body);
        Assert.Equal(HttpStatusCode.OK, raise.StatusCode);
        var detail = await DetailAsync(admin, matchId);
        Assert.Equal(3, detail.GetProperty("confirmed").GetArrayLength());
        Assert.Equal(1, detail.GetProperty("waitlist")[0].GetProperty("waitlistPosition").GetInt32());

        var lower = await admin.PutAsJsonAsync($"/api/matches/{matchId}", body with { playerLimit = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);
    }

    [Fact]
    public async Task Player_list_hides_ratings_but_admin_sees_them()
    {
        var (admin, matchId) = await AdminWithMatchAsync(limit: 10);
        await admin.PostAsync($"/api/matches/{matchId}/join", null);
        var player = await PlayerAsync("11988885001", "Curioso");
        await player.PostAsync($"/api/matches/{matchId}/join", null);

        var asPlayer = await DetailAsync(player, matchId);
        Assert.All(asPlayer.GetProperty("confirmed").EnumerateArray(),
            r => Assert.Equal(JsonValueKind.Null, r.GetProperty("skillRating").ValueKind));

        var asAdmin = await DetailAsync(admin, matchId);
        Assert.All(asAdmin.GetProperty("confirmed").EnumerateArray(),
            r => Assert.Equal(JsonValueKind.Number, r.GetProperty("skillRating").ValueKind));
    }
}
