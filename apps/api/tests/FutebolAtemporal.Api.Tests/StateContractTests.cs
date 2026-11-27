using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

/// <summary>PL-2: a web envia estados como texto; cada transição precisa responder com o código e o estado persistido.</summary>
public class StateContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public StateContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=contract-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<Guid> LoginAdminAsync(HttpClient client)
    {
        var login = await TestAuth.LoginAsync(client, AdminPhone);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var me = await login.Content.ReadFromJsonAsync<JsonElement>();
        return me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string phone, string name)
    {
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone, displayName = name, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        return (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerProfileId").GetGuid();
    }

    private static async Task<JsonElement> TransitionAsync(HttpClient client, Guid matchId, string status, HttpStatusCode expected)
    {
        var res = await client.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status });
        Assert.Equal(expected, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Close_reopen_close_and_draw_from_closed_list_using_text_states()
    {
        var client = NewClient();
        await RegisterAsync(client, "11988881001", "Linha Um");
        var groupId = await LoginAdminAsync(client);

        var create = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Contrato",
            startsAt = DateTimeOffset.UtcNow.AddDays(2),
            location = "Arena",
            playerLimit = 10,
            openRegistrations = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var matchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        await TestAuth.LoginAsync(client, "11988881001");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);
        await RegisterAsync(client, "11988881003", "Linha Tres"); // três cores exigem 3 confirmados
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);
        await LoginAdminAsync(client);

        var closed = await TransitionAsync(client, matchId, "RegistrationClosed", HttpStatusCode.OK);
        Assert.Equal("RegistrationClosed", closed.GetProperty("status").GetString());

        var reopened = await TransitionAsync(client, matchId, "RegistrationOpen", HttpStatusCode.OK);
        Assert.Equal("RegistrationOpen", reopened.GetProperty("status").GetString());

        // Transição imediatamente anterior ao sorteio: precisa passar, senão o teste para aqui.
        var closedAgain = await TransitionAsync(client, matchId, "RegistrationClosed", HttpStatusCode.OK);
        Assert.Equal("RegistrationClosed", closedAgain.GetProperty("status").GetString());

        var persisted = await (await client.GetAsync($"/api/matches/{matchId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RegistrationClosed", persisted.GetProperty("match").GetProperty("status").GetString());

        var draw = await client.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = "contrato" });
        Assert.Equal(HttpStatusCode.OK, draw.StatusCode);
        var drawId = (await draw.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var confirm = await client.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var afterDraw = await (await client.GetAsync($"/api/matches/{matchId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Drawn", afterDraw.GetProperty("match").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Numeric_state_is_still_accepted()
    {
        var client = NewClient();
        var groupId = await LoginAdminAsync(client);
        var create = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId, startsAt = DateTimeOffset.UtcNow.AddDays(1), location = "Arena", playerLimit = 10, openRegistrations = true
        });
        var matchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var res = await client.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = 2 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("RegistrationClosed", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalid_state_returns_field_error_and_forbidden_transition_returns_reason()
    {
        var client = NewClient();
        var groupId = await LoginAdminAsync(client);
        var create = await client.PostAsJsonAsync("/api/matches", new
        {
            groupId, startsAt = DateTimeOffset.UtcNow.AddDays(1), location = "Arena", playerLimit = 10, openRegistrations = false
        });
        var matchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var unknown = await TransitionAsync(client, matchId, "Fechada", HttpStatusCode.BadRequest);
        Assert.True(unknown.TryGetProperty("errors", out var errors));
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Contains("status", StringComparison.OrdinalIgnoreCase));

        var forbidden = await TransitionAsync(client, matchId, "Completed", HttpStatusCode.BadRequest);
        Assert.Contains("não é permitida", forbidden.GetProperty("detail").GetString());

        var stillDraft = await (await client.GetAsync($"/api/matches/{matchId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", stillDraft.GetProperty("match").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Membership_status_and_payment_accept_text_states()
    {
        var client = NewClient();
        var profileId = await RegisterAsync(client, "11988881002", "Mensalista");
        var groupId = await LoginAdminAsync(client);

        var add = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = profileId });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var membershipId = (await add.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var inactive = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/status", new { status = "Inactive" });
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        Assert.Equal("Inactive", (await inactive.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var active = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/status", new { status = "Active" });
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);

        var badStatus = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/status", new { status = 42 });
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);

        var period = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/periods", new { year = 2026, month = 11 });
        Assert.Equal(HttpStatusCode.OK, period.StatusCode);
        var periodId = (await period.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var paid = await client.PutAsJsonAsync(
            $"/api/groups/{groupId}/memberships/{membershipId}/payments/{periodId}", new { amount = 150m, status = "Paid" });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var paidBody = await paid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paid", paidBody.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, paidBody.GetProperty("paidAt").ValueKind);

        var listed = await (await client.GetAsync($"/api/groups/{groupId}/memberships/payments?year=2026&month=11&status=Paid"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(listed.EnumerateArray());

        var negative = await client.PutAsJsonAsync(
            $"/api/groups/{groupId}/memberships/{membershipId}/payments/{periodId}", new { amount = -1m, status = "Paid" });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }
}
