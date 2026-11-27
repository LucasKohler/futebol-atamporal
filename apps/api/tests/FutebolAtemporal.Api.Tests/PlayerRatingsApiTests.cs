using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

/// <summary>SO-1: admin edita notas de linha e goleiro; jogador comum não; faixas validadas e persistidas.</summary>
public class PlayerRatingsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public PlayerRatingsApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=ratings-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    [Fact]
    public async Task Admin_sets_separate_ratings_and_player_cannot()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11988886001", displayName = "Goleiro Linha", password = TestAuth.Password });
        var profileId = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerProfileId").GetGuid();

        var login = await TestAuth.LoginAsync(client, AdminPhone);
        var groupId = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var url = $"/api/groups/{groupId}/players/{profileId}/ratings";

        // Jogador recém-criado: sem nota de goleiro (migração/cadastro não inventa nota).
        var before = await (await client.GetAsync($"/api/groups/{groupId}/players")).Content.ReadFromJsonAsync<JsonElement>();
        var fresh = before.EnumerateArray().Single(p => p.GetProperty("playerProfileId").GetGuid() == profileId);
        Assert.False(fresh.GetProperty("canPlayGoalkeeper").GetBoolean());
        Assert.Equal(JsonValueKind.Null, fresh.GetProperty("goalkeeperRating").ValueKind);

        var ok = await client.PutAsJsonAsync(url, new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = 0m });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4m, body.GetProperty("lineRating").GetDecimal());
        Assert.Equal(0m, body.GetProperty("goalkeeperRating").GetDecimal());

        var after = await (await client.GetAsync($"/api/groups/{groupId}/players")).Content.ReadFromJsonAsync<JsonElement>();
        var saved = after.EnumerateArray().Single(p => p.GetProperty("playerProfileId").GetGuid() == profileId);
        Assert.True(saved.GetProperty("canPlayGoalkeeper").GetBoolean());
        Assert.Equal(0m, saved.GetProperty("goalkeeperRating").GetDecimal());
        Assert.Equal(4m, saved.GetProperty("lineRating").GetDecimal());

        var outOfRange = await client.PutAsJsonAsync(url, new { lineRating = 11m, canPlayGoalkeeper = true, goalkeeperRating = 5m });
        Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
        Assert.Contains("entre 0 e 10", (await outOfRange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        await TestAuth.LoginAsync(client, "11988886001");
        var forbidden = await client.PutAsJsonAsync(url, new { lineRating = 10m, canPlayGoalkeeper = true, goalkeeperRating = 10m });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
