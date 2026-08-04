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
            builder.UseSetting("Seed:AdminEmail", "admin@test.local");
            builder.UseSetting("Seed:AdminPassword", "Admin123!");
        });
    }

    [Fact]
    public async Task Health_returns_success()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_seed_admin_and_get_me()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        await using var stream = await me.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        Assert.Equal("admin@test.local", doc.RootElement.GetProperty("email").GetString());
        Assert.True(doc.RootElement.GetProperty("memberships").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Current_group_requires_auth_then_returns_seed_group()
    {
        var client = _factory.CreateClient();
        var anonymous = await client.GetAsync("/api/groups/current");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.local", password = "Admin123!" });
        var group = await client.GetAsync("/api/groups/current");
        Assert.Equal(HttpStatusCode.OK, group.StatusCode);
        var json = await group.Content.ReadAsStringAsync();
        Assert.Contains("futebol-atemporal", json, StringComparison.OrdinalIgnoreCase);
    }
}
