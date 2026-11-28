using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Notifications;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>
/// Ciclo de vida push x sessão (inscrição só recebe enquanto a sessão que a gravou vale) e
/// corte de versão do cookie/migration (versão antiga não aceita cookie v2; rollback não ressuscita sessões v1).
/// </summary>
public class SessionPushLifecycleApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public SessionPushLifecycleApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=session-push-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
            builder.UseSetting("WebPush:Mode", "Simulator");
        });
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static (string P256dh, string Auth) Keys()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdh.ExportParameters(false);
        var raw = new byte[65];
        raw[0] = 0x04;
        p.Q.X!.CopyTo(raw, 1);
        p.Q.Y!.CopyTo(raw, 33);
        return (NotificationPolicies.ToBase64Url(raw), NotificationPolicies.ToBase64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private async Task<(HttpClient Client, Guid UserId)> RegisterAsync(HttpClient c, string phone, string name)
    {
        var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone, displayName = name, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        return (c, (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private static async Task SubscribeAsync(HttpClient c, string endpoint, (string P256dh, string Auth) keys)
        => Assert.Equal(HttpStatusCode.OK,
            (await TestAuth.SubscribePushAsync(c, new { endpoint, keys = new { p256dh = keys.P256dh, auth = keys.Auth } })).StatusCode);

    [Fact]
    public async Task Push_is_delivered_only_while_the_session_that_subscribed_is_valid()
    {
        var admin = NewClient();
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        var groupId = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        string Ep(string tag) => $"https://fcm.googleapis.com/fcm/send/{tag}-{Guid.NewGuid():N}";

        // Sessão válida: recebe.
        var (okClient, okUser) = await RegisterAsync(NewClient(), "11963000001", "Valida");
        var epOk = Ep("ok");
        await SubscribeAsync(okClient, epOk, Keys());

        // Logout direto pela API, sem DELETE da inscrição antes.
        var (outClient, outUser) = await RegisterAsync(NewClient(), "11963000002", "Saiu");
        var epLogout = Ep("logout");
        await SubscribeAsync(outClient, epLogout, Keys());
        Assert.Equal(HttpStatusCode.NoContent, (await outClient.PostAsync("/api/auth/logout", null)).StatusCode);

        // Sessão expirada no servidor (validade fixa).
        var (expClient, expUser) = await RegisterAsync(NewClient(), "11963000003", "Expirou");
        var epExpired = Ep("expired");
        await SubscribeAsync(expClient, epExpired, Keys());

        // Reset/recuperação de senha (troca do carimbo de segurança).
        var (resetClient, resetUser) = await RegisterAsync(NewClient(), "11963000004", "Reset");
        var epReset = Ep("reset");
        await SubscribeAsync(resetClient, epReset, Keys());

        // Troca de conta no mesmo navegador sem DELETE: Q entra por cima da sessão de S.
        var shared = NewClient();
        var (_, switchedUser) = await RegisterAsync(shared, "11963000005", "Trocada");
        var epSwitch = Ep("switch");
        await SubscribeAsync(shared, epSwitch, Keys());
        await RegisterAsync(NewClient(), "11963000006", "Outra Conta");
        Assert.Equal(HttpStatusCode.OK, (await shared.PostAsJsonAsync("/api/auth/login", new { phone = "11963000006", password = TestAuth.Password })).StatusCode);

        // Saiu e entrou de novo: a sincronização do app regrava a mesma inscrição na sessão nova e volta a receber.
        var (backClient, backUser) = await RegisterAsync(NewClient(), "11963000007", "Voltou");
        var epBack = Ep("back");
        var backKeys = Keys();
        await SubscribeAsync(backClient, epBack, backKeys);
        Assert.Equal(HttpStatusCode.NoContent, (await backClient.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await backClient.PostAsJsonAsync("/api/auth/login", new { phone = "11963000007", password = TestAuth.Password })).StatusCode);
        await SubscribeAsync(backClient, epBack, backKeys);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var s in await db.UserSessions.Where(s => s.UserId == expUser).ToListAsync())
                s.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByIdAsync(resetUser.ToString()))!);

            // Logout e novo login por cima desativam explicitamente as inscrições da sessão encerrada.
            Assert.False((await db.PushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == epLogout)).IsActive);
            Assert.False((await db.PushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == epSwitch)).IsActive);
            Assert.True((await db.PushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == epBack)).IsActive);
        }

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/broadcast")
        {
            Content = JsonContent.Create(new { audience = "AllMembers", title = "Aviso", body = "Teste de sessão", actionUrl = "/" })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(req)).StatusCode);

        var watched = new[] { okUser, outUser, expUser, resetUser, switchedUser, backUser };
        var runner = _factory.Services.GetRequiredService<INotificationDispatchRunner>();
        for (var i = 0; i < 40; i++)
        {
            await runner.ProcessPendingAsync();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.UserNotifications.AsNoTracking()
                .Where(n => watched.Contains(n.UserId) && n.Category == NotificationCategory.AdminBroadcast)
                .CountAsync(n => n.PushState == NotificationPushState.Pending);
            if (pending == 0) break;
            await Task.Delay(100);
        }

        var sent = _factory.Services.GetRequiredService<SimulatedWebPushStore>().Sent.Select(s => s.Endpoint).ToHashSet();
        Assert.Contains(epOk, sent);
        Assert.Contains(epBack, sent);
        Assert.DoesNotContain(epLogout, sent);
        Assert.DoesNotContain(epExpired, sent);
        Assert.DoesNotContain(epReset, sent);
        Assert.DoesNotContain(epSwitch, sent);
    }

    [Fact]
    public async Task Cookie_v2_is_unreadable_by_the_v1_format_and_v1_tickets_are_rejected()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await TestAuth.LoginAsync(NewClient(), AdminPhone); // garante o seed/admin
        var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone = "11963000010", displayName = "Versao", password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var cookie = reg.Headers.GetValues("Set-Cookie").First(v => v.StartsWith("fa_auth=", StringComparison.Ordinal)).Split(';')[0]["fa_auth=".Length..];

        var dp = _factory.Services.GetRequiredService<IDataProtectionProvider>();
        var v2 = new TicketDataFormat(dp.CreateProtector(SessionCookieEvents.TicketPurpose));
        // Formato padrão do Identity (o que a versão antiga usa para o esquema Identity.Application).
        var v1 = new TicketDataFormat(dp.CreateProtector(
            "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", IdentityConstants.ApplicationScheme, "v2"));

        // Versão antiga (rollback/réplica antiga com as mesmas chaves) não consegue ler o cookie novo.
        var ticket = v2.Unprotect(cookie);
        Assert.NotNull(ticket);
        Assert.Null(v1.Unprotect(cookie));

        // Versão nova recusa o mesmo ticket (sessão válida, carimbo certo) se vier no formato v1.
        var asV1 = v1.Protect(ticket!);
        var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", $"fa_auth={asV1}");
        var res = await c.SendAsync(me);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.False(res.Headers.Contains("Set-Cookie"));

        var ok = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        ok.Headers.Add("Cookie", $"fa_auth={cookie}");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(ok)).StatusCode);
    }

    [Fact]
    public async Task UserSessions_migration_rotates_security_stamps_and_down_up_round_trips()
    {
        var file = $"session-migration-{Guid.NewGuid():N}.db";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file}").Options;
        const string previous = "20261127054850_PwaNotificationCooldown";
        var userId = Guid.NewGuid();
        try
        {
            await using (var db = new AppDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync(previous);
                // Usuário existente antes do deploy, com carimbo que cookies v1 carregam.
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""AspNetUsers"" (""Id"", ""DisplayName"", ""IsActive"", ""CreatedAt"", ""SecurityStamp"",
                        ""EmailConfirmed"", ""PhoneNumberConfirmed"", ""TwoFactorEnabled"", ""LockoutEnabled"", ""AccessFailedCount"")
                    VALUES ({userId}, {"Antigo"}, 1, {DateTimeOffset.UtcNow}, {"STAMP-V1"}, 0, 0, 0, 1, 0)");
            }

            await using (var db = new AppDbContext(options))
            {
                await db.Database.MigrateAsync();
                var stamp = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync();
                Assert.NotEqual("STAMP-V1", stamp);
                Assert.Matches("^[0-9A-F]{32}$", stamp!);
            }

            // Down e Up de novo funcionam (SQLite reconstrói a tabela ao remover a coluna).
            await using (var db = new AppDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync(previous);
                await db.Database.MigrateAsync();
                Assert.Empty(await db.UserSessions.ToListAsync());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
