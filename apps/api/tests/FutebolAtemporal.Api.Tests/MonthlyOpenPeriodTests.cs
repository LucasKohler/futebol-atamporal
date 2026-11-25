using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

/// <summary>Mensalista incluído/reativado com a competência aberta recebe o lançamento pendente; rota respeita o grupo.</summary>
public class MonthlyOpenPeriodTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public MonthlyOpenPeriodTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=monthly-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private static DateTime GroupToday()
        => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")).DateTime;

    [Fact]
    public async Task Member_added_or_reactivated_after_period_opened_gets_one_pending_charge()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11988887001", displayName = "Mensal Tardio" });
        var profileId = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerProfileId").GetGuid();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone });
        var groupId = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var today = GroupToday();
        var period = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/periods", new { year = today.Year, month = today.Month });
        Assert.Equal(HttpStatusCode.OK, period.StatusCode);
        var periodId = (await period.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var add = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = profileId });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var membershipId = (await add.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        async Task<List<JsonElement>> Charges()
            => (await (await client.GetAsync($"/api/groups/{groupId}/memberships/payments?year={today.Year}&month={today.Month}"))
                    .Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray().Where(p => p.GetProperty("monthlyMembershipId").GetGuid() == membershipId).ToList();

        var first = Assert.Single(await Charges());
        Assert.Equal("Pending", first.GetProperty("status").GetString());
        Assert.Equal(periodId, first.GetProperty("billingPeriodId").GetGuid());

        // Desativar e reativar não duplica; incluir de novo também não.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/status", new { status = "Inactive" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/status", new { status = "Active" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = profileId })).StatusCode);
        Assert.Single(await Charges());

        // Baixa agora possível pela tela: o lançamento existe.
        var paid = await client.PutAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/payments/{periodId}", new { amount = 150m, status = "Paid" });
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        // Reativar depois de pago não volta para pendente.
        await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/status", new { status = "Active" });
        Assert.Equal("Paid", Assert.Single(await Charges()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Membership_routes_reject_a_group_id_that_does_not_match()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone = "11988887002", displayName = "Rota Errada" });
        var profileId = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerProfileId").GetGuid();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone });
        var groupId = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var add = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = profileId });
        var membershipId = (await add.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var today = GroupToday();
        var period = await client.PostAsJsonAsync($"/api/groups/{groupId}/memberships/periods", new { year = today.Year, month = today.Month });
        var periodId = (await period.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var otherGroup = Guid.NewGuid();
        var status = await client.PostAsJsonAsync($"/api/groups/{otherGroup}/memberships/{membershipId}/status", new { status = "Inactive" });
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
        var pay = await client.PutAsJsonAsync($"/api/groups/{otherGroup}/memberships/{membershipId}/payments/{periodId}", new { amount = 1m, status = "Paid" });
        Assert.Equal(HttpStatusCode.Forbidden, pay.StatusCode);

        var members = await (await client.GetAsync($"/api/groups/{groupId}/memberships")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", members.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == membershipId).GetProperty("status").GetString());
    }
}
