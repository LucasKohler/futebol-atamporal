using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

public class AuthAndHealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthAndHealthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=test-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", "11999999999");
        });
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Health_returns_success()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_seed_admin_and_get_me()
    {
        var client = CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { phone = "11999999999" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("fa_auth", StringComparison.Ordinal));

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await using var stream = await me.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
        Assert.Equal("11999999999", doc.RootElement.GetProperty("phone").GetString());
        Assert.Equal("Administrador", doc.RootElement.GetProperty("displayName").GetString());
        Assert.False(doc.RootElement.TryGetProperty("email", out _));
        Assert.True(doc.RootElement.GetProperty("memberships").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Current_group_requires_auth_then_returns_seed_group()
    {
        var client = CreateClient();
        var anonymous = await client.GetAsync("/api/groups/current");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await client.PostAsJsonAsync("/api/auth/login", new { phone = "11 99999-9999" });
        var group = await client.GetAsync("/api/groups/current");
        Assert.Equal(HttpStatusCode.OK, group.StatusCode);
        var json = await group.Content.ReadAsStringAsync();
        Assert.Contains("futebol-atemporal", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_phone_and_name_then_login_without_password()
    {
        var client = CreateClient();
        var phone = "11988881111";
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone, displayName = "Jogador Um" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);

        await client.PostAsync("/api/auth/logout", null);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { phone = "(11) 98888-1111" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Jogador Um", me.GetProperty("registeredName").GetString());
        Assert.Equal("11988881111", me.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Invalid_or_unnormalized_phone_is_rejected()
    {
        var client = CreateClient();
        var badLogin = await client.PostAsJsonAsync("/api/auth/login", new { phone = "123" });
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var badRegister = await client.PostAsJsonAsync("/api/auth/register", new { phone = "abc", displayName = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, badRegister.StatusCode);

        var emailLegacy = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@futebolatemporal.local", password = "Admin123!" });
        Assert.NotEqual(HttpStatusCode.OK, emailLegacy.StatusCode);
    }

    [Fact]
    public async Task Ddi_and_spacing_variants_match_same_user()
    {
        var client = CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11977770001", displayName = "Equivalente" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var first = await reg.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = first.GetProperty("id").GetGuid();

        await client.PostAsync("/api/auth/logout", null);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { phone = "+55 11 97777-0001" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await login.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal(id, me.GetProperty("id").GetGuid());
        Assert.Equal("11977770001", me.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Player_cannot_edit_own_registered_name()
    {
        var client = CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11966660001", displayName = "Nome Fixo" });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var me = await reg.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var profileId = me.GetProperty("playerProfileId").GetGuid();

        var putName = await client.PutAsJsonAsync("/api/auth/me", new { displayName = "Hacker" });
        Assert.Equal(HttpStatusCode.Forbidden, putName.StatusCode);
        var patchName = await client.PatchAsJsonAsync("/api/auth/me", new { displayName = "Hacker" });
        Assert.Equal(HttpStatusCode.Forbidden, patchName.StatusCode);

        var putNick = await client.PutAsJsonAsync($"/api/groups/{groupId}/players/{profileId}/nickname", new { nickname = "Hack" });
        Assert.Equal(HttpStatusCode.Forbidden, putNick.StatusCode);

        var still = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Nome Fixo", still.GetProperty("registeredName").GetString());
        Assert.Equal("Nome Fixo", still.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Nickname_persists_after_reload_and_cookie_still_authenticates()
    {
        var client = CreateClient();
        var player = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11955550001", displayName = "Carlos Eduardo" });
        Assert.Equal(HttpStatusCode.OK, player.StatusCode);
        var playerMe = await player.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var profileId = playerMe.GetProperty("playerProfileId").GetGuid();

        await client.PostAsJsonAsync("/api/auth/login", new { phone = "11999999999" });
        var adminMe = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var groupId = adminMe.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var nick = await client.PutAsJsonAsync($"/api/groups/{groupId}/players/{profileId}/nickname", new { nickname = "Cadu" });
        Assert.Equal(HttpStatusCode.OK, nick.StatusCode);

        var reload = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);

        await client.PostAsJsonAsync("/api/auth/login", new { phone = "11955550001" });
        var after = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Cadu", after.GetProperty("nickname").GetString());
        Assert.Equal("Carlos Eduardo", after.GetProperty("registeredName").GetString());
        Assert.Equal("Cadu", after.GetProperty("displayName").GetString());

        await client.PostAsJsonAsync("/api/auth/login", new { phone = "11999999999" });
        var players = await (await client.GetAsync($"/api/groups/{groupId}/players")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var row = players.EnumerateArray().First(p => p.GetProperty("playerProfileId").GetGuid() == profileId);
        Assert.Equal("Cadu", row.GetProperty("nickname").GetString());
        Assert.Equal("Cadu", row.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Admin_promotes_monthly_member_for_mensalistas()
    {
        var client = CreateClient();
        var player = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11944440001", displayName = "Mensal Novo" });
        Assert.Equal(HttpStatusCode.OK, player.StatusCode);
        var profileId = (await player.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("playerProfileId").GetGuid();

        await client.PostAsJsonAsync("/api/auth/login", new { phone = "11999999999" });
        var groupId = (await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var add = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = profileId });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var members = await (await client.GetAsync($"/api/groups/{groupId}/memberships")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Contains(members.EnumerateArray(), m => m.GetProperty("playerProfileId").GetGuid() == profileId
            && m.GetProperty("status").GetString() == "Active");

        var players = await (await client.GetAsync($"/api/groups/{groupId}/players")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var row = players.EnumerateArray().First(p => p.GetProperty("playerProfileId").GetGuid() == profileId);
        Assert.True(row.GetProperty("isMonthlyMember").GetBoolean());
    }
}
