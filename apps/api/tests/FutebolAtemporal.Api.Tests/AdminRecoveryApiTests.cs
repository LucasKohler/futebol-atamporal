using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>Recuperação do único admin por código temporário de uso único configurado pelo operador.</summary>
public class AdminRecoveryApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private const string RecoveryCode = "codigo-de-recuperacao-teste-2026";
    private readonly WebApplicationFactory<Program> _base;

    public AdminRecoveryApiTests(WebApplicationFactory<Program> factory) => _base = factory;

    private WebApplicationFactory<Program> Host(string db, string? expiresAt) => _base.WithWebHostBuilder(b =>
    {
        b.UseSetting("Database:UseSqlite", "true");
        b.UseSetting("ConnectionStrings:Default", $"Data Source={db};Pooling=False");
        b.UseSetting("Seed:AdminPhone", AdminPhone);
        if (expiresAt is not null)
        {
            b.UseSetting("Seed:AdminRecoveryCode", RecoveryCode);
            b.UseSetting("Seed:AdminRecoveryExpiresAt", expiresAt);
        }
    });

    private static HttpClient Client(WebApplicationFactory<Program> h) => h.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static Task<HttpResponseMessage> Recover(HttpClient c, string code, string phone, string? pwd)
        => c.PostAsJsonAsync("/api/auth/admin-recovery", new { recoveryCode = code, phone, newPassword = pwd });
    private static async Task<string?> Code(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    private static string In(TimeSpan t) => DateTimeOffset.UtcNow.Add(t).ToString("O");

    [Fact]
    public async Task Recovery_code_is_short_lived_single_use_admin_only_and_ends_old_sessions()
    {
        var db = Path.Combine(Path.GetTempPath(), $"recovery-{Guid.NewGuid():N}.db");
        try
        {
            await using var h = Host(db, In(TimeSpan.FromMinutes(5)));

            // Admin ainda não configurado: recuperação não substitui a configuração inicial.
            Assert.Equal("setup_pending", await Code(await Recover(Client(h), RecoveryCode, AdminPhone, "Nova12345")));

            var oldSession = Client(h);
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginAsync(oldSession, AdminPhone)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync("/api/auth/me")).StatusCode);

            var c = Client(h);
            Assert.Equal("invalid_recovery_code", await Code(await Recover(c, "codigo-errado-mas-bem-comprido", AdminPhone, "Nova12345")));
            Assert.Equal("admin_phone_mismatch", await Code(await Recover(c, RecoveryCode, "11961110000", "Nova12345")));
            Assert.Equal("weak_password", await Code(await Recover(c, RecoveryCode, AdminPhone, "fraca")));
            // Senha fraca não apagou a senha atual.
            Assert.Equal(HttpStatusCode.OK, (await Client(h).PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = TestAuth.Password })).StatusCode);

            var ok = await Recover(c, RecoveryCode, AdminPhone, "Nova12345");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);

            // Sessão antiga encerrada; senha antiga não vale; nova vale.
            Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(h).PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = TestAuth.Password })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Client(h).PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = "Nova12345" })).StatusCode);

            // Uso único, e a configuração inicial continua consumida.
            Assert.Equal("recovery_code_used", await Code(await Recover(Client(h), RecoveryCode, AdminPhone, "Outra1234")));
            Assert.Equal("setup_already_done", await Code(await Client(h).PostAsJsonAsync("/api/auth/admin-setup",
                new { setupCode = TestAuth.SetupCode, phone = AdminPhone, password = "Outra1234" })));

            using var scope = h.Services.CreateScope();
            var audit = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEntries.AsNoTracking()
                .SingleAsync(e => e.Action == "auth.admin_recovery");
            Assert.DoesNotContain(RecoveryCode, audit.PayloadJson);
            Assert.DoesNotContain("Nova12345", audit.PayloadJson);
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public async Task Simultaneous_recoveries_with_the_same_code_succeed_only_once()
    {
        var db = Path.Combine(Path.GetTempPath(), $"recovery-{Guid.NewGuid():N}.db");
        try
        {
            await using var h = Host(db, In(TimeSpan.FromMinutes(5)));
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginAsync(Client(h), AdminPhone)).StatusCode);

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
                Recover(Client(h), RecoveryCode, AdminPhone, $"Paralela{i}234")));
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

            using var scope = h.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEntries.CountAsync(e => e.Action == "auth.admin_recovery"));
            // Só a senha da requisição vencedora vale.
            var winner = Enumerable.Range(0, 4).Count(i => results[i].StatusCode == HttpStatusCode.OK
                && Client(h).PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = $"Paralela{i}234" }).Result.StatusCode == HttpStatusCode.OK);
            Assert.Equal(1, winner);
        }
        finally { File.Delete(db); }
    }

    [Fact]
    public async Task Failed_credential_write_does_not_consume_the_recovery_code()
    {
        var db = Path.Combine(Path.GetTempPath(), $"recovery-{Guid.NewGuid():N}.db");
        try
        {
            await using var host = Host(db, In(TimeSpan.FromMinutes(5)));
            using var oldSession = Client(host);
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginAsync(oldSession, AdminPhone)).StatusCode);

            using var scope = host.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Reject the new hash after the earlier audit insert and password removal.
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_recovery_password
                BEFORE UPDATE OF PasswordHash ON AspNetUsers
                WHEN OLD.PasswordHash IS NULL AND NEW.PasswordHash IS NOT NULL
                BEGIN
                    SELECT RAISE(ABORT, 'Forced recovery credential write failure');
                END;
                """);

            using var recovering = Client(host);
            using var failed = await Recover(recovering, RecoveryCode, AdminPhone, "Nova12345");
            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
            Assert.Equal("recovery_conflict", await Code(failed));
            Assert.False(await context.AuditEntries.AnyAsync(e => e.Action == "auth.admin_recovery"));
            Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync("/api/auth/me")).StatusCode);
            using var login = Client(host);
            Assert.Equal(HttpStatusCode.OK, (await login.PostAsJsonAsync("/api/auth/login",
                new { phone = AdminPhone, password = TestAuth.Password })).StatusCode);

            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_recovery_password");
            Assert.Equal(HttpStatusCode.OK, (await Recover(recovering, RecoveryCode, AdminPhone, "Nova12345")).StatusCode);
            Assert.Equal(1, await context.AuditEntries.CountAsync(e => e.Action == "auth.admin_recovery"));
        }
        finally { File.Delete(db); }
    }


    [Theory]
    [InlineData(null, "recovery_disabled", 404)]
    [InlineData(30, "recovery_disabled", 404)]   // validade acima de 10 minutos não é aceita
    [InlineData(-1, "recovery_code_expired", 410)]
    public async Task Recovery_requires_configured_short_window(int? minutes, string code, int status)
    {
        var db = Path.Combine(Path.GetTempPath(), $"recovery-{Guid.NewGuid():N}.db");
        try
        {
            await using var h = Host(db, minutes is null ? null : In(TimeSpan.FromMinutes(minutes.Value)));
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginAsync(Client(h), AdminPhone)).StatusCode);
            var r = await Recover(Client(h), RecoveryCode, AdminPhone, "Nova12345");
            Assert.Equal(status, (int)r.StatusCode);
            Assert.Equal(code, await Code(r));
        }
        finally { File.Delete(db); }
    }
}
