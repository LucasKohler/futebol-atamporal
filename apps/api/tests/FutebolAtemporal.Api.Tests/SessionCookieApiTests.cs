using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Infrastructure.Identity;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FutebolAtemporal.Api.Tests;

/// <summary>
/// Cookie fa_auth sem renovação e com sessão revogável no servidor. Os testes controlam o "pote de cookies"
/// à mão para reproduzir respostas fora de ordem entre abas (com e sem Web Locks no cliente).
/// </summary>
public class SessionCookieApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public SessionCookieApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=session-cookie-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    /// <summary>Cliente sem pote de cookies: cada requisição leva exatamente o cookie informado.</summary>
    private HttpClient Raw() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private static string? AuthCookie(HttpResponseMessage res)
        => res.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith("fa_auth=", StringComparison.Ordinal))
                .Select(v => v.Split(';')[0]["fa_auth=".Length..]).LastOrDefault()
            : null;

    private static bool SetsAnyAuthCookie(HttpResponseMessage res)
        => res.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(v => v.StartsWith("fa_auth=", StringComparison.Ordinal));

    private static HttpRequestMessage With(HttpMethod method, string url, string? cookie, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (cookie is not null) req.Headers.Add("Cookie", $"fa_auth={cookie}");
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(string Cookie, Guid UserId)> RegisterAsync(string phone, string name)
    {
        var res = await Raw().PostAsJsonAsync("/api/auth/register", new { phone, displayName = name, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cookie = AuthCookie(res);
        Assert.False(string.IsNullOrEmpty(cookie));
        return (cookie!, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private async Task<string> LoginAsync(string phone, string? currentCookie = null)
    {
        var res = await Raw().SendAsync(With(HttpMethod.Post, "/api/auth/login", currentCookie, new { phone, password = TestAuth.Password }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return AuthCookie(res)!;
    }

    private async Task<HttpResponseMessage> MeAsync(string? cookie) => await Raw().SendAsync(With(HttpMethod.Get, "/api/auth/me", cookie));

    private async Task EnsureAdminAsync()
    {
        using var c = _factory.CreateClient();
        await TestAuth.LoginAsync(c, AdminPhone);
    }

    [Fact]
    public async Task Login_cookie_and_server_session_expire_together_after_28_days_without_sliding()
    {
        const string phone = "11962000018";
        var (_, userId) = await RegisterAsync(phone, "Sessão 28 dias");
        using var login = await Raw().PostAsJsonAsync("/api/auth/login", new { phone, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.IsType<string>(AuthCookie(login));
        var header = Microsoft.Net.Http.Headers.SetCookieHeaderValue.Parse(
            Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("fa_auth=", StringComparison.Ordinal)));
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var ticket = Assert.IsType<Microsoft.AspNetCore.Authentication.AuthenticationTicket>(options.TicketDataFormat.Unprotect(cookie));
        Assert.False(options.SlidingExpiration);
        Assert.False(ticket.Properties.AllowRefresh ?? true);
        Assert.Equal(TimeSpan.FromDays(28), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        Assert.Equal(ticket.Properties.ExpiresUtc, header.Expires);
        var sessionId = SessionCookieEvents.GetSessionId(ticket.Principal);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId && s.UserId == userId);
        Assert.Equal(ticket.Properties.ExpiresUtc, session.ExpiresAt);
        Assert.Equal(ticket.Properties.IssuedUtc, session.CreatedAt);

        using var response = await MeAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(SetsAnyAuthCookie(response));
    }

    [Fact]
    public async Task Ordinary_authenticated_and_rejected_responses_never_set_the_auth_cookie()
    {
        await EnsureAdminAsync();
        var (cookieA, userA) = await RegisterAsync("11962000001", "Conta A");

        // Várias consultas comuns autenticadas: nenhuma renova (antes: SlidingExpiration + carimbo com intervalo zero).
        for (var i = 0; i < 3; i++)
        {
            var me = await MeAsync(cookieA);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal(userA, (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            Assert.False(SetsAnyAuthCookie(me));
        }
        var groups = await Raw().SendAsync(With(HttpMethod.Get, "/api/groups/current", cookieA));
        Assert.False(SetsAnyAuthCookie(groups));

        // Logout: 204 sem Set-Cookie. A rejeição posterior do cookie revogado também não emite Set-Cookie.
        var logout = await Raw().SendAsync(With(HttpMethod.Post, "/api/auth/logout", cookieA, new { expectedUserId = userA }));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.False(SetsAnyAuthCookie(logout));
        var rejected = await MeAsync(cookieA);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.False(SetsAnyAuthCookie(rejected));
    }

    [Fact]
    public async Task Late_response_of_A_cannot_restore_A_after_logout_and_login_of_B()
    {
        await EnsureAdminAsync();
        var (cookieA, userA) = await RegisterAsync("11962000002", "Conta A");
        var (_, userB) = await RegisterAsync("11962000003", "Conta B");

        // Aba 1 dispara uma consulta como A (resposta fica "em voo" no navegador).
        var lateA = await MeAsync(cookieA);
        Assert.Equal(HttpStatusCode.OK, lateA.StatusCode);

        // Aba 2: logout de A e login de B; o pote passa a ter B.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Raw().SendAsync(With(HttpMethod.Post, "/api/auth/logout", cookieA, new { expectedUserId = userA }))).StatusCode);
        var jar = await LoginAsync("11962000003", currentCookie: null);

        // A resposta tardia de A chega agora: sem Set-Cookie, o pote continua com B.
        jar = AuthCookie(lateA) ?? jar;
        var me = await MeAsync(jar);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(userB, (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        // Mesmo que o navegador ainda tenha (ou volte a ter) qualquer cookie já emitido para A, ele não autentica mais.
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(cookieA)).StatusCode);
    }

    [Fact]
    public async Task Without_web_locks_late_logout_of_A_does_not_expire_cookie_of_B()
    {
        await EnsureAdminAsync();
        var (cookieA, userA) = await RegisterAsync("11962000004", "Conta A");
        var (_, userB) = await RegisterAsync("11962000005", "Conta B");

        // Filas por aba: aba 1 envia logout como A; aba 2 conclui login de B antes da resposta do logout chegar.
        var jar = await LoginAsync("11962000005", currentCookie: null);
        var lateLogout = await Raw().SendAsync(With(HttpMethod.Post, "/api/auth/logout", cookieA, new { expectedUserId = userA }));
        Assert.Equal(HttpStatusCode.NoContent, lateLogout.StatusCode);

        // Antes: Set-Cookie "fa_auth=; expires=1970" apagava B. Agora o logout não toca no cookie do navegador.
        Assert.False(SetsAnyAuthCookie(lateLogout));
        jar = AuthCookie(lateLogout) ?? jar;
        var me = await MeAsync(jar);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(userB, (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        // A saiu de verdade: a sessão de A foi revogada no servidor.
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(cookieA)).StatusCode);
    }

    [Fact]
    public async Task Logout_without_body_revokes_only_the_current_session()
    {
        await EnsureAdminAsync();
        var (cookie1, _) = await RegisterAsync("11962000006", "Dois Aparelhos");
        var cookie2 = await LoginAsync("11962000006"); // outro aparelho, sem cookie anterior

        var logout = await Raw().SendAsync(With(HttpMethod.Post, "/api/auth/logout", cookie1));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(cookie1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(cookie2)).StatusCode);

        // Repetir o logout com o cookie revogado: 401, sem efeito.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Raw().SendAsync(With(HttpMethod.Post, "/api/auth/logout", cookie1))).StatusCode);
    }

    [Fact]
    public async Task New_login_in_same_browser_revokes_previous_session_and_stamp_or_expiry_still_end_sessions()
    {
        await EnsureAdminAsync();
        var (first, userId) = await RegisterAsync("11962000007", "Relogin");

        // Login enviando o cookie anterior: novo cookie, o anterior deixa de valer.
        var second = await LoginAsync("11962000007", currentCookie: first);
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(second)).StatusCode);

        // Troca do carimbo de segurança (reset/recuperação de senha) continua encerrando a sessão na hora.
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            await users.UpdateSecurityStampAsync(user!);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(second)).StatusCode);

        // Sessão expirada no servidor também não autentica.
        var third = await LoginAsync("11962000007");
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(third)).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var s in await db.UserSessions.Where(s => s.UserId == userId).ToListAsync())
                s.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(third)).StatusCode);
    }
}
