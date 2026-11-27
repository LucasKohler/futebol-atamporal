using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>Login telefone + senha, cadastro com senha, admin por segredo de ambiente e reset pelo admin.</summary>
public class PhonePasswordAuthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public PhonePasswordAuthTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=auth-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> Login(HttpClient c, string phone, string? password)
    {
        if (phone == AdminPhone && password == TestAuth.Password)
            return await TestAuth.LoginAsync(c, phone);
        return await c.PostAsJsonAsync("/api/auth/login", new { phone, password });
    }

    private static async Task<string?> Detail(HttpResponseMessage r)
        => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    [Fact]
    public async Task Register_requires_password_meeting_current_policy_and_login_checks_it()
    {
        var c = NewClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/register", new { phone = "11961110001", displayName = "Sem Senha" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/register", new { phone = "11961110001", displayName = "Fraca", password = "abc" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/register", new { phone = "11961110001", displayName = "Boa", password = TestAuth.Password })).StatusCode);

        var other = NewClient();
        var wrong = await Login(other, "11961110001", "Errada1234");
        var unknown = await Login(other, "11961119999", TestAuth.Password);
        var missing = await Login(other, "11961110001", null);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        // Mesma mensagem: não revela se o celular existe.
        Assert.Equal(await Detail(wrong), await Detail(unknown));
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(other, "(11) 96111-0001", TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Admin_password_is_created_once_with_setup_code_and_legacy_account_without_password_cannot_login()
    {
        // Antes da configuração o admin não entra; código errado, celular errado e senha fraca são recusados.
        var admin = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(NewClient(), AdminPhone, "Qualquer123")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/auth/admin-setup", new { setupCode = "errado", phone = AdminPhone, password = TestAuth.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/admin-setup", new { setupCode = TestAuth.SetupCode, phone = "11961110000", password = TestAuth.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/admin-setup", new { setupCode = TestAuth.SetupCode, phone = AdminPhone, password = "fraca" })).StatusCode);

        var setup = await admin.PostAsJsonAsync("/api/auth/admin-setup", new { setupCode = TestAuth.SetupCode, phone = AdminPhone, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);

        // Uso único: o mesmo código não troca mais a senha.
        var again = await NewClient().PostAsJsonAsync("/api/auth/admin-setup", new { setupCode = TestAuth.SetupCode, phone = AdminPhone, password = "Outra1234" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(NewClient(), AdminPhone, "Outra1234")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(NewClient(), AdminPhone, TestAuth.Password)).StatusCode);

        // Mesmo removendo a senha do admin, o código já consumido não volta a valer.
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var adminUser = await um.FindByNameAsync(AdminPhone);
            Assert.True((await um.RemovePasswordAsync(adminUser!)).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.DoesNotContain(TestAuth.SetupCode, (await db.AuditEntries.AsNoTracking().SingleAsync(e => e.Action == "auth.first_admin_setup")).PayloadJson);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await NewClient().PostAsJsonAsync("/api/auth/admin-setup",
            new { setupCode = TestAuth.SetupCode, phone = AdminPhone, password = TestAuth.Password })).StatusCode);

        // Conta antiga criada sem senha: sem fluxo de primeiro acesso, não entra.
        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var legacy = new ApplicationUser { Id = Guid.NewGuid(), UserName = "11961112222", PhoneNumber = "11961112222", DisplayName = "Antigo", IsActive = true };
            Assert.True((await um.CreateAsync(legacy)).Succeeded);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(NewClient(), "11961112222", TestAuth.Password)).StatusCode);
    }

    [Fact]
    public async Task Only_group_admin_resets_player_password_and_secret_is_not_audited()
    {
        var player = NewClient();
        var reg = await player.PostAsJsonAsync("/api/auth/register", new { phone = "11961113333", displayName = "Esquecido", password = TestAuth.Password });
        var me = await reg.Content.ReadFromJsonAsync<JsonElement>();
        var profileId = me.GetProperty("playerProfileId").GetGuid();
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var url = $"/api/groups/{groupId}/players/{profileId}/password";
        const string newPassword = "Nova12345";

        Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync(url, new { newPassword })).StatusCode);

        var admin = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await Login(admin, AdminPhone, TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new { newPassword = "curta" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{Guid.NewGuid()}/password", new { newPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(url, new { newPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(NewClient(), "11961113333", TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(NewClient(), "11961113333", newPassword)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "auth.password_reset_by_admin");
        Assert.Equal(profileId, audit.EntityId);
        Assert.DoesNotContain(newPassword, audit.PayloadJson);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_admin_reset_unlocks_it()
    {
        var player = NewClient();
        var reg = await player.PostAsJsonAsync("/api/auth/register", new { phone = "11961114444", displayName = "Bloqueado", password = TestAuth.Password });
        var me = await reg.Content.ReadFromJsonAsync<JsonElement>();
        var profileId = me.GetProperty("playerProfileId").GetGuid();
        var groupId = me.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var anon = NewClient();
        for (var i = 0; i < 4; i++)
        {
            var wrong = await Login(anon, "11961114444", "Errada1234");
            Assert.Equal("invalid_credentials", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var fifth = await Login(anon, "11961114444", "Errada1234");
        Assert.Equal(HttpStatusCode.Unauthorized, fifth.StatusCode);
        Assert.Equal("locked_out", (await fifth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Mesmo com a senha certa, segue bloqueado.
        var right = await Login(anon, "11961114444", TestAuth.Password);
        Assert.Equal("locked_out", (await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await um.FindByNameAsync("11961114444");
            var end = await um.GetLockoutEndDateAsync(user!);
            Assert.InRange(end!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
        }

        var admin = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await Login(admin, AdminPhone, TestAuth.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{profileId}/password", new { newPassword = "Nova12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(NewClient(), "11961114444", "Nova12345")).StatusCode);
    }
}
