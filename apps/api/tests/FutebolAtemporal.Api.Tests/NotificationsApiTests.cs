using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Notifications;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>
/// Cobertura ponta a ponta do contrato de PWA e Notificações (CA-03 a CA-11):
/// VAPID P-256, inscrições/SSRF, privacidade/auth, 4 gatilhos automáticos,
/// outbox/retentativa/410, lembretes rápidos (Pix/cooldown/idempotência) e broadcast.
/// </summary>
public class NotificationsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationsApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=notif-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
            builder.UseSetting("WebPush:Mode", "Simulator");
        });
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private static (string P256dh, string Auth) ValidKeys()
    {
        var (pub, _) = WebPushOptions.GenerateP256KeyPair();
        var auth = NotificationPolicies.ToBase64Url(Guid.NewGuid().ToByteArray());
        return (pub, auth);
    }

    private async Task<(HttpClient Admin, Guid GroupId, Guid AdminProfileId)> LoginAdminAsync()
    {
        var admin = NewClient();
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Json(login);
        var groupId = body.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var adminProfileId = body.GetProperty("playerProfileId").GetGuid();
        return (admin, groupId, adminProfileId);
    }

    private async Task<(HttpClient Client, Guid UserId, Guid ProfileId)> RegisterPlayerAsync(string phone, string name)
    {
        var client = NewClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register", new { phone, displayName = name, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var body = await Json(reg);
        return (client, body.GetProperty("id").GetGuid(), body.GetProperty("playerProfileId").GetGuid());
    }

    private async Task<Guid> CreateClosedMatchAsync(
        HttpClient admin,
        Guid groupId,
        IReadOnlyList<(HttpClient Client, Guid ProfileId)> players)
    {
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Pelada Pix", startsAt = DateTimeOffset.UtcNow.AddDays(3), location = "Arena", playerLimit = 14, openRegistrations = true
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var matchId = (await Json(create)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        foreach (var player in players)
            Assert.Equal(HttpStatusCode.OK, (await player.Client.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        foreach (var player in players.Take(3))
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{player.ProfileId}/ratings",
                new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = 7m })).StatusCode);

        var drawResponse = await admin.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = "notification-fees" });
        Assert.Equal(HttpStatusCode.OK, drawResponse.StatusCode);
        var draw = await Json(drawResponse);
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null)).StatusCode);

        var teamData = draw.GetProperty("teams").EnumerateArray().ToArray();
        var teams = teamData.Select(t => t.GetProperty("id").GetGuid()).ToArray();
        var scorer = teamData[0].GetProperty("members").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Line").GetProperty("playerProfileId").GetGuid();
        var close = await admin.PostAsJsonAsync($"/api/matches/{matchId}/result/round", new
        {
            presence = Array.Empty<object>(),
            games = new[] { new { homeTeamId = teams[0], awayTeamId = teams[1], homeScore = 1, awayScore = 0, penaltyWinnerTeamId = (Guid?)null } },
            goals = new[] { new { game = 1, playerProfileId = scorer, ownGoal = false } },
            bestPlayerProfileId = (Guid?)null,
            bestGoalkeeperProfileId = (Guid?)null,
            notes = (string?)null,
            reason = (string?)null
        });
        Assert.True(close.StatusCode == HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
        return matchId;
    }

    [Fact]
    public async Task CA03_Vapid_public_key_is_65_bytes_stable_in_dev_and_503_in_production_without_keys()
    {
        // Anônimo em Simulator/Testing: 65 bytes descomprimidos (0x04 || X || Y)
        var anon = NewClient();
        var res = await anon.GetAsync("/api/notifications/vapid-public-key");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await Json(res);
        var pub = body.GetProperty("publicKey").GetString()!;
        var keyId = body.GetProperty("keyId").GetString()!;
        Assert.True(NotificationPolicies.TryDecodeBase64Url(pub, out var bytes));
        Assert.Equal(65, bytes.Length);
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(16, keyId.Length);

        // Development com DevKeyFile: chave permanece idêntica após reinício da API
        var devKeyFile = Path.Combine(Path.GetTempPath(), $"vapid-dev-test-{Guid.NewGuid():N}.json");
        try
        {
            string pub1;
            using (var f1 = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Database:UseSqlite", "true");
                b.UseSetting("ConnectionStrings:Default", $"Data Source=notif-dev1-{Guid.NewGuid():N}.db");
                b.UseSetting("WebPush:DevKeyFile", devKeyFile);
            }))
            {
                var r1 = await f1.CreateClient().GetAsync("/api/notifications/vapid-public-key");
                Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
                pub1 = (await Json(r1)).GetProperty("publicKey").GetString()!;
            }

            using (var f2 = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Development");
                b.UseSetting("Database:UseSqlite", "true");
                b.UseSetting("ConnectionStrings:Default", $"Data Source=notif-dev2-{Guid.NewGuid():N}.db");
                b.UseSetting("WebPush:DevKeyFile", devKeyFile);
            }))
            {
                var r2 = await f2.CreateClient().GetAsync("/api/notifications/vapid-public-key");
                Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
                Assert.Equal(pub1, (await Json(r2)).GetProperty("publicKey").GetString());
            }
        }
        finally
        {
            if (File.Exists(devKeyFile)) File.Delete(devKeyFile);
        }

        // Production sem chaves: 503 push_not_configured e o resto da API funciona
        using var prod = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Database:UseSqlite", "true");
            b.UseSetting("ConnectionStrings:Default", $"Data Source=notif-prod-{Guid.NewGuid():N}.db");
            b.UseSetting("Seed:AdminPhone", AdminPhone);
        });
        var prodClient = prod.CreateClient();
        var vapidProd = await prodClient.GetAsync("/api/notifications/vapid-public-key");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, vapidProd.StatusCode);
        Assert.Equal("push_not_configured", (await Json(vapidProd)).GetProperty("code").GetString());
        var health = await prodClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task CA04_CA05_Subscriptions_ssrf_transfer_privacy_and_auth()
    {
        var (admin, groupId, _) = await LoginAdminAsync();
        var (p1, u1, _) = await RegisterPlayerAsync("11960000101", "P1");
        var (p2, u2, _) = await RegisterPlayerAsync("11960000102", "P2");
        var (p256dh, auth) = ValidKeys();

        // SSRF: host fora da lista permitida -> 400 invalid_subscription
        var ssrf = await TestAuth.SubscribePushAsync(p1, new
        {
            endpoint = "https://169.254.169.254/latest/meta-data",
            keys = new { p256dh, auth },
            deviceLabel = "Evil"
        });
        Assert.Equal(HttpStatusCode.BadRequest, ssrf.StatusCode);
        Assert.Equal("invalid_subscription", (await Json(ssrf)).GetProperty("code").GetString());

        // Inscrever 2x o mesmo endpoint mantém 1 linha só; outro usuário assume o endpoint (D2)
        var ep = $"https://fcm.googleapis.com/fcm/send/device-{Guid.NewGuid():N}";
        var sub1 = await TestAuth.SubscribePushAsync(p1, new
        {
            endpoint = ep, keys = new { p256dh, auth }, deviceLabel = "Pixel"
        });
        Assert.Equal(HttpStatusCode.OK, sub1.StatusCode);
        var subId = (await Json(sub1)).GetProperty("id").GetGuid();

        var sub1Again = await TestAuth.SubscribePushAsync(p1, new
        {
            endpoint = ep, keys = new { p256dh, auth }, deviceLabel = "Pixel"
        });
        Assert.Equal(HttpStatusCode.OK, sub1Again.StatusCode);
        Assert.Equal(subId, (await Json(sub1Again)).GetProperty("id").GetGuid());

        // Outra conta que só conhece a URL (chaves diferentes) não toma a inscrição: 409, a de P1 segue intacta.
        var otherAuth = NotificationPolicies.ToBase64Url(Guid.NewGuid().ToByteArray());
        var hijack = await TestAuth.SubscribePushAsync(p2, new
        {
            endpoint = ep, keys = new { p256dh, auth = otherAuth }, deviceLabel = "Invasor"
        });
        Assert.Equal(HttpStatusCode.Conflict, hijack.StatusCode);
        Assert.Equal("subscription_conflict", (await Json(hijack)).GetProperty("code").GetString());

        // P2 loga no mesmo aparelho (mesmas chaves = prova de posse): a inscrição de P1 é revogada e outra é criada.
        var sub2 = await TestAuth.SubscribePushAsync(p2, new
        {
            endpoint = ep, keys = new { p256dh, auth }, deviceLabel = "Pixel Compartilhado"
        });
        Assert.Equal(HttpStatusCode.OK, sub2.StatusCode);
        Assert.NotEqual(subId, (await Json(sub2)).GetProperty("id").GetGuid());
        using (var scope = _factory.Services.CreateScope())
        {
            var subs = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PushSubscriptions.AsNoTracking()
                .Where(s => s.Endpoint == ep).ToListAsync();
            Assert.Single(subs);
            Assert.DoesNotContain(subs, s => s.Id == subId);
        }

        // P1 tentando apagar endpoint que agora é de P2 -> 204 sem efeito
        var delWrongUser = await p1.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/subscriptions")
        {
            Content = JsonContent.Create(new { endpoint = ep, expectedUserId = u1 })
        });
        Assert.Equal(HttpStatusCode.NoContent, delWrongUser.StatusCode);

        // GET /me de P2 nunca expõe endpoint, p256dh ou auth (CA-05)
        var meRes = await p2.GetAsync($"/api/notifications/me?groupId={groupId}");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
        var meRaw = await meRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ep, meRaw);
        Assert.DoesNotContain(p256dh, meRaw);
        Assert.DoesNotContain(auth, meRaw);
        var meJson = JsonDocument.Parse(meRaw).RootElement;
        Assert.Single(meJson.GetProperty("devices").EnumerateArray());

        // Jogador comum nas rotas de admin -> 403 forbidden
        var forbid = await p1.GetAsync($"/api/groups/{groupId}/admin/notifications/summary");
        Assert.Equal(HttpStatusCode.Forbidden, forbid.StatusCode);
        Assert.Equal("forbidden", (await Json(forbid)).GetProperty("code").GetString());

        // Broadcast cria notificação para P1 e P2; P1 não pode marcar como lida a notificação de P2 -> 404
        var bcReq = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/broadcast")
        {
            Content = JsonContent.Create(new { audience = "AllMembers", title = "Aviso", body = "Teste", actionUrl = "/" })
        };
        bcReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(bcReq)).StatusCode);

        var p2Me = await Json(await p2.GetAsync($"/api/notifications/me?groupId={groupId}"));
        var p2NotifId = p2Me.GetProperty("items")[0].GetProperty("id").GetGuid();

        var crossRead = await p1.PostAsync($"/api/notifications/{p2NotifId}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, crossRead.StatusCode);

        // P2 marca como lida (idempotente) e read-all funciona
        Assert.Equal(HttpStatusCode.NoContent, (await p2.PostAsync($"/api/notifications/{p2NotifId}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await p2.PostAsync($"/api/notifications/{p2NotifId}/read", null)).StatusCode);
        var readAll = await Json(await p1.PostAsJsonAsync("/api/notifications/read-all", new { groupId }));
        Assert.True(readAll.GetProperty("updated").GetInt32() >= 1);
    }

    [Fact]
    public async Task Subscribe_rejects_when_expected_user_differs_from_session_without_writing()
    {
        await LoginAdminAsync();
        var (a, userA, _) = await RegisterPlayerAsync("11960000111", "Conta A");
        var (b, userB, _) = await RegisterPlayerAsync("11960000112", "Conta B");
        var (p256dh, auth) = ValidKeys();
        const string sub = "/api/notifications/subscriptions";

        async Task<List<FutebolAtemporal.Domain.Entities.PushSubscription>> RowsAsync(string endpoint)
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PushSubscriptions.AsNoTracking()
                .Where(s => s.Endpoint == endpoint).ToListAsync();
        }

        async Task AssertSessionChangedAsync(HttpResponseMessage res)
        {
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            var raw = await res.Content.ReadAsStringAsync();
            Assert.Equal("session_changed", JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(userA.ToString(), raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(userB.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        }

        // Ausente, vazio, inválido, Guid vazio e id de outra conta: 409 genérico e nenhuma linha criada.
        var epNew = $"https://fcm.googleapis.com/fcm/send/precond-{Guid.NewGuid():N}";
        await AssertSessionChangedAsync(await b.PostAsJsonAsync(sub, new { endpoint = epNew, keys = new { p256dh, auth } }));
        foreach (var bad in new[] { "", "   ", "nao-e-uuid", Guid.Empty.ToString(), userA.ToString() })
            await AssertSessionChangedAsync(await b.PostAsJsonAsync(sub, new { endpoint = epNew, keys = new { p256dh, auth }, expectedUserId = bad }));
        Assert.Empty(await RowsAsync(epNew));

        // Igualdade: 200 e o dono gravado é o principal autenticado.
        var ep = $"https://fcm.googleapis.com/fcm/send/precond-{Guid.NewGuid():N}";
        var okA = await a.PostAsJsonAsync(sub, new { endpoint = ep, keys = new { p256dh, auth }, deviceLabel = "Aparelho A", expectedUserId = userA });
        Assert.Equal(HttpStatusCode.OK, okA.StatusCode);
        var before = Assert.Single(await RowsAsync(ep));
        Assert.Equal(userA, before.UserId);

        // TOCTOU: intenção de A, mas o cookie do POST já é de B. Mesmas chaves (prova de posse) — sem a precondição,
        // o upsert transferiria a inscrição para B. Com ela: 409 e a linha de A fica intacta.
        await AssertSessionChangedAsync(await b.PostAsJsonAsync(sub, new { endpoint = ep, keys = new { p256dh, auth }, deviceLabel = "Trocou", expectedUserId = userA }));
        var after = Assert.Single(await RowsAsync(ep));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(userA, after.UserId);
        Assert.True(after.IsActive);
        Assert.Equal(before.DeviceLabel, after.DeviceLabel);
        Assert.Equal(before.LastSeenAt, after.LastSeenAt);
        Assert.Null(after.DeactivatedAt);

        // Sem sessão continua 401 (autenticação antes da precondição).
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync(sub, new { endpoint = ep, keys = new { p256dh, auth }, expectedUserId = userA })).StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_and_logout_require_matching_expected_user_without_mutation()
    {
        await LoginAdminAsync();
        var (a, userA, _) = await RegisterPlayerAsync("11960000113", "Conta A");
        var (b, userB, _) = await RegisterPlayerAsync("11960000114", "Conta B");
        var (p256dh, auth) = ValidKeys();
        const string sub = "/api/notifications/subscriptions";

        async Task<List<FutebolAtemporal.Domain.Entities.PushSubscription>> RowsAsync(string endpoint)
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PushSubscriptions.AsNoTracking()
                .Where(s => s.Endpoint == endpoint).ToListAsync();
        }

        async Task AssertSessionChangedAsync(HttpResponseMessage res)
        {
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            var raw = await res.Content.ReadAsStringAsync();
            Assert.Equal("session_changed", JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(userA.ToString(), raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(userB.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        }

        static HttpRequestMessage Delete(object body) =>
            new(HttpMethod.Delete, "/api/notifications/subscriptions") { Content = JsonContent.Create(body) };

        // ---------- DELETE ----------
        // Inscrição de B (cookie atual = B). Operação iniciada por A (rotação de chave) chega com o cookie de B.
        var epB = $"https://fcm.googleapis.com/fcm/send/del-precond-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await b.PostAsJsonAsync(sub, new { endpoint = epB, keys = new { p256dh, auth }, expectedUserId = userB })).StatusCode);
        var before = Assert.Single(await RowsAsync(epB));

        await AssertSessionChangedAsync(await b.SendAsync(Delete(new { endpoint = epB })));
        foreach (var bad in new[] { "", "   ", "nao-e-uuid", Guid.Empty.ToString(), userA.ToString() })
            await AssertSessionChangedAsync(await b.SendAsync(Delete(new { endpoint = epB, expectedUserId = bad })));

        var after = Assert.Single(await RowsAsync(epB));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(userB, after.UserId);
        Assert.True(after.IsActive);
        Assert.Null(after.DeactivatedAt);

        // Igualdade: 204 e a linha some.
        Assert.Equal(HttpStatusCode.NoContent, (await b.SendAsync(Delete(new { endpoint = epB, expectedUserId = userB }))).StatusCode);
        Assert.Empty(await RowsAsync(epB));

        // ---------- LOGOUT ----------
        // Aba antiga de A envia logout com o cookie de B: 409, sem apagar o cookie, B continua logado.
        foreach (var bad in new[] { "", "nao-e-uuid", Guid.Empty.ToString(), userA.ToString() })
        {
            var res = await b.PostAsJsonAsync("/api/auth/logout", new { expectedUserId = bad });
            await AssertSessionChangedAsync(res);
            Assert.False(res.Headers.Contains("Set-Cookie"));
        }
        var stillB = await b.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillB.StatusCode);
        Assert.Equal(userB, (await Json(stillB)).GetProperty("id").GetGuid());

        // Igualdade: 204 e a sessão termina no servidor (sem Set-Cookie: ver SessionCookieApiTests).
        var okLogout = await b.PostAsJsonAsync("/api/auth/logout", new { expectedUserId = userB });
        Assert.Equal(HttpStatusCode.NoContent, okLogout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);

        // Compatibilidade: logout sem corpo ou sem o campo segue incondicional.
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/auth/me")).StatusCode);
        var a2 = await LoginPlayerAsync("11960000113");
        Assert.Equal(HttpStatusCode.NoContent, (await a2.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a2.GetAsync("/api/auth/me")).StatusCode);
    }

    private async Task<HttpClient> LoginPlayerAsync(string phone)
    {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/login", new { phone, password = TestAuth.Password })).StatusCode);
        return c;
    }

    [Fact]
    public async Task CA06_CA07_Automatic_triggers_dedupe_resilience_and_no_notify_on_correction()
    {
        var (admin, groupId, _) = await LoginAdminAsync();

        // Força o sender a lançar exceção para provar CA-07 (evento de domínio nunca falha!)
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        store.GlobalOverride = (_, _) => throw new InvalidOperationException("Falha simulada no provedor push");
        try
        {
            var players = new List<(HttpClient Client, Guid UserId, Guid ProfileId, string Name)>();
            foreach (var (name, i) in new[] { "G1", "G2", "G3", "L1", "L2", "L3", "L4", "L5", "Wait1" }.Select((n, i) => (n, i)))
            {
                var (c, uid, pid) = await RegisterPlayerAsync($"11961000{i:D3}", name);
                if (name.StartsWith('G'))
                    await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{pid}/ratings", new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = 7m });
                players.Add((c, uid, pid, name));
            }

            // 1. Gatilho MatchOpen (limite 8 para ter fila de espera com Wait1)
            var create = await admin.PostAsJsonAsync("/api/matches", new
            {
                groupId, title = "Pelada Gatilhos", startsAt = DateTimeOffset.UtcNow.AddDays(2), location = "Arena", playerLimit = 8, openRegistrations = true
            });
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            var matchId = (await Json(create)).GetProperty("id").GetGuid();

            // Fechar e reabrir lista não deve duplicar MatchOpen
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationClosed" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationOpen" })).StatusCode);

            var p0Me = await Json(await players[0].Client.GetAsync($"/api/notifications/me?groupId={groupId}"));
            var matchOpenCount = p0Me.GetProperty("items").EnumerateArray()
                .Count(n => n.GetProperty("category").GetString() == "MatchOpen" && n.GetProperty("matchId").GetGuid() == matchId);
            Assert.Equal(1, matchOpenCount);

            // 2. Gatilho WaitlistPromoted: 8 confirmam + Wait1 entra na fila; L5 sai -> Wait1 sobe!
            foreach (var p in players)
                Assert.Equal(HttpStatusCode.OK, (await p.Client.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

            var l5 = players.First(p => p.Name == "L5");
            var wait1 = players.First(p => p.Name == "Wait1");
            Assert.Equal(HttpStatusCode.OK, (await l5.Client.PostAsync($"/api/matches/{matchId}/leave", null)).StatusCode);

            var wait1Me = await Json(await wait1.Client.GetAsync($"/api/notifications/me?groupId={groupId}"));
            Assert.Contains(wait1Me.GetProperty("items").EnumerateArray(),
                n => n.GetProperty("category").GetString() == "WaitlistPromoted" && n.GetProperty("matchId").GetGuid() == matchId);
            // Outro jogador (G1) não recebe WaitlistPromoted
            var g1Me = await Json(await players[0].Client.GetAsync($"/api/notifications/me?groupId={groupId}"));
            Assert.DoesNotContain(g1Me.GetProperty("items").EnumerateArray(),
                n => n.GetProperty("category").GetString() == "WaitlistPromoted" && n.GetProperty("matchId").GetGuid() == matchId);

            // Admin entra também para termos 9 confirmados no sorteio de 3 cores (ou aumentamos para 9)
            await admin.PutAsJsonAsync($"/api/matches/{matchId}", new
            {
                title = "Pelada Gatilhos", startsAt = DateTimeOffset.UtcNow.AddDays(2), location = "Arena", playerLimit = 9, notes = ""
            });
            await admin.PostAsync($"/api/matches/{matchId}/join", null);

            // 3. Gatilho DrawPublished: confirmar sorteio avisa cada escalado com sua cor
            var draw = await Json(await admin.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = "notif-draw" }));
            var drawId = draw.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null)).StatusCode);

            var teams = draw.GetProperty("teams").EnumerateArray().ToList();
            var redTeamId = teams[0].GetProperty("id").GetGuid();
            var blackTeamId = teams[1].GetProperty("id").GetGuid();
            var g1TeamName = teams.First(t => t.GetProperty("members").EnumerateArray()
                .Any(m => m.GetProperty("playerProfileId").GetGuid() == players[0].ProfileId)).GetProperty("name").GetString()!;

            var g1AfterDraw = await Json(await players[0].Client.GetAsync($"/api/notifications/me?groupId={groupId}"));
            var drawNotif = g1AfterDraw.GetProperty("items").EnumerateArray()
                .Single(n => n.GetProperty("category").GetString() == "DrawPublished" && n.GetProperty("matchId").GetGuid() == matchId);
            Assert.Contains(g1TeamName, drawNotif.GetProperty("body").GetString()!);

            // 4. Gatilho MatchCompleted ao fechar súmula (rev 1); correção (rev 2) NÃO notifica!
            var closeRes = await admin.PostAsJsonAsync($"/api/matches/{matchId}/result/round", new
            {
                presence = Array.Empty<object>(),
                games = new[] { new { homeTeamId = redTeamId, awayTeamId = blackTeamId, homeScore = 0, awayScore = 0, penaltyWinnerTeamId = redTeamId } },
                goals = Array.Empty<object>()
            });
            Assert.Equal(HttpStatusCode.OK, closeRes.StatusCode);

            var g1AfterClose = await Json(await players[0].Client.GetAsync($"/api/notifications/me?groupId={groupId}"));
            Assert.Equal(1, g1AfterClose.GetProperty("items").EnumerateArray()
                .Count(n => n.GetProperty("category").GetString() == "MatchCompleted" && n.GetProperty("matchId").GetGuid() == matchId));

            var fixRes = await admin.PutAsJsonAsync($"/api/matches/{matchId}/result/round", new
            {
                presence = Array.Empty<object>(),
                games = new[] { new { homeTeamId = redTeamId, awayTeamId = blackTeamId, homeScore = 0, awayScore = 0, penaltyWinnerTeamId = blackTeamId } },
                goals = Array.Empty<object>(),
                reason = "Correção de vencedor nos pênaltis"
            });
            Assert.Equal(HttpStatusCode.OK, fixRes.StatusCode);

            var g1AfterFix = await Json(await players[0].Client.GetAsync($"/api/notifications/me?groupId={groupId}"));
            Assert.Equal(1, g1AfterFix.GetProperty("items").EnumerateArray()
                .Count(n => n.GetProperty("category").GetString() == "MatchCompleted" && n.GetProperty("matchId").GetGuid() == matchId));
        }
        finally
        {
            store.GlobalOverride = null;
        }
    }

    [Fact]
    public async Task CA08_Dispatcher_deactivates_410_and_retries_5xx_up_to_3_times()
    {
        var (admin, groupId, _) = await LoginAdminAsync();
        var (p1, u1, _) = await RegisterPlayerAsync("11962000101", "Sub410");
        var (p2, u2, _) = await RegisterPlayerAsync("11962000102", "Sub500");
        var (k1, a1) = ValidKeys();
        var (k2, a2) = ValidKeys();
        var ep410 = $"https://fcm.googleapis.com/fcm/send/gone-{Guid.NewGuid():N}";
        var ep500 = $"https://fcm.googleapis.com/fcm/send/err-{Guid.NewGuid():N}";

        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        store.Overrides[ep410] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Gone, 410, "HTTP 410");
        store.Overrides[ep500] = (_, _) => new WebPushSendResult(WebPushSendOutcome.TransientError, 500, "HTTP 500");

        Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(p1, new { endpoint = ep410, keys = new { p256dh = k1, auth = a1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(p2, new { endpoint = ep500, keys = new { p256dh = k2, auth = a2 } })).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/broadcast")
        {
            Content = JsonContent.Create(new { audience = "AllMembers", title = "Teste Dispatcher", body = "Testando 410 e 500", actionUrl = "/" })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var bcRes = await admin.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, bcRes.StatusCode);
        var campaignId = (await Json(bcRes)).GetProperty("id").GetGuid();

        var runner = _factory.Services.GetRequiredService<INotificationDispatchRunner>();

        // 1ª rodada: ep410 deve ser desativado; ep500 vai para tentativa 1 (Pending + NextPushAt)
        await runner.ProcessPendingAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Com a reserva por aviso, quem processa pode ser o worker em segundo plano: espera o resultado.
            var sub410 = await db.PushSubscriptions.AsNoTracking().FirstAsync(s => s.Endpoint == ep410);
            for (var i = 0; i < 50 && sub410.IsActive; i++)
            {
                await Task.Delay(100);
                await runner.ProcessPendingAsync();
                sub410 = await db.PushSubscriptions.AsNoTracking().FirstAsync(s => s.Endpoint == ep410);
            }
            Assert.False(sub410.IsActive);
            Assert.NotNull(sub410.DeactivatedAt);

            var notif500 = await db.UserNotifications.AsNoTracking().FirstAsync(n => n.CampaignId == campaignId && n.UserId == u2);
            // Pode já ter sido processada pelo worker em background + runner; avançamos NextPushAt para completar 3 tentativas.
            // ExecuteUpdate não disputa o token de reserva (DispatchVersion) com o despachante.
            for (var i = 0; i < 100 && notif500.PushState == NotificationPushState.Pending; i++)
            {
                var id = notif500.Id;
                await db.UserNotifications.Where(n => n.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextPushAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
                await runner.ProcessPendingAsync();
                if (notif500.ClaimedUntil is not null) await Task.Delay(50);
                notif500 = await db.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == id);
            }
            Assert.Equal(NotificationPushState.Failed, notif500.PushState);
            Assert.Equal(3, notif500.PushAttempts);
        }
    }

    [Fact]
    public async Task CA09_CA10_CA11_Payment_reminder_validation_idempotency_and_cooldown()
    {
        var (admin, groupId, adminProfileId) = await LoginAdminAsync();
        var (pendingPlayer, _, pendingProfile) = await RegisterPlayerAsync("11963000101", "Devedor");
        var (paidPlayer, _, paidProfile) = await RegisterPlayerAsync("11963000102", "Pago");
        var otherPlayers = new List<(HttpClient Client, Guid ProfileId)> { (pendingPlayer, pendingProfile), (paidPlayer, paidProfile) };
        for (var i = 0; i < 6; i++)
        {
            var (client, _, profileId) = await RegisterPlayerAsync($"119630001{10 + i:D2}", $"Outro {i}");
            otherPlayers.Add((client, profileId));
        }

        // A regra de diária exige fechamento: criar as cobranças pelo fluxo real, não numa partida aberta.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/payments/settings",
            new { defaultMonthlyFee = 160m, defaultMatchDayFee = 25m, pixExpirationMinutes = 30 })).StatusCode);
        var matchId = await CreateClosedMatchAsync(admin, groupId, otherPlayers);

        var fees = await Json(await admin.GetAsync($"/api/groups/{groupId}/matches/{matchId}/fees"));
        Assert.Contains(fees.EnumerateArray(), f => f.GetProperty("playerProfileId").GetGuid() == pendingProfile
            && f.GetProperty("status").GetString() == "Pending");
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{paidProfile}", new
        {
            amount = 25m, status = "Paid"
        })).StatusCode);
        foreach (var player in otherPlayers.Where(p => p.ProfileId != pendingProfile && p.ProfileId != paidProfile))
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{player.ProfileId}",
                new { amount = 25m, status = "Paid" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{adminProfileId}",
            new { amount = 25m, status = "Paid" })).StatusCode);

        // CA-10: validações de broadcast
        var badUrl = await admin.PostAsJsonAsync($"/api/groups/{groupId}/admin/notifications/broadcast", new
        {
            audience = "AllMembers", title = "T", body = "B", actionUrl = "javascript:alert(1)"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badUrl.StatusCode);
        Assert.Equal("invalid_action_url", (await Json(badUrl)).GetProperty("code").GetString());

        var badTitle = await admin.PostAsJsonAsync($"/api/groups/{groupId}/admin/notifications/broadcast", new
        {
            audience = "AllMembers", title = new string('x', 121), body = "B", actionUrl = "/"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badTitle.StatusCode);
        Assert.Equal("validation_failed", (await Json(badTitle)).GetProperty("code").GetString());

        // CA-09: lembrete de Pix vai só para Devedor, sem valor em R$ no texto
        var key1 = Guid.NewGuid().ToString("N");
        var q1 = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/quick")
        {
            Content = JsonContent.Create(new { kind = "PaymentReminder" })
        };
        q1.Headers.Add("Idempotency-Key", key1);
        var q1Res = await admin.SendAsync(q1);
        Assert.Equal(HttpStatusCode.Created, q1Res.StatusCode);
        var c1 = await Json(q1Res);
        Assert.Equal(1, c1.GetProperty("recipientCount").GetInt32());

        var devedorMe = await Json(await pendingPlayer.GetAsync($"/api/notifications/me?groupId={groupId}"));
        var payNotif = devedorMe.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("category").GetString() == "PaymentReminder");
        Assert.Equal("/pagamentos", payNotif.GetProperty("actionUrl").GetString());
        Assert.DoesNotContain("25", payNotif.GetProperty("body").GetString()!);
        Assert.DoesNotContain("R$", payNotif.GetProperty("body").GetString()!);

        var pagoMe = await Json(await paidPlayer.GetAsync($"/api/notifications/me?groupId={groupId}"));
        Assert.DoesNotContain(pagoMe.GetProperty("items").EnumerateArray(), n => n.GetProperty("category").GetString() == "PaymentReminder");

        // CA-11: clique duplo (mesma Idempotency-Key + mesmo payload) -> 200 com mesma campanha
        var q1Dup = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/quick")
        {
            Content = JsonContent.Create(new { kind = "PaymentReminder" })
        };
        q1Dup.Headers.Add("Idempotency-Key", key1);
        var q1DupRes = await admin.SendAsync(q1Dup);
        Assert.Equal(HttpStatusCode.OK, q1DupRes.StatusCode);
        Assert.Equal(c1.GetProperty("id").GetGuid(), (await Json(q1DupRes)).GetProperty("id").GetGuid());

        // Mesma chave com outro payload -> 422 idempotency_key_mismatch
        var q1Mismatch = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/quick")
        {
            Content = JsonContent.Create(new { kind = "MatchAvailable" })
        };
        q1Mismatch.Headers.Add("Idempotency-Key", key1);
        var q1MismatchRes = await admin.SendAsync(q1Mismatch);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, q1MismatchRes.StatusCode);
        Assert.Equal("idempotency_key_mismatch", (await Json(q1MismatchRes)).GetProperty("code").GetString());

        // Nova chave para o mesmo lembrete em < 30 min -> 409 reminder_cooldown com retryAfterSeconds
        var q2 = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/quick")
        {
            Content = JsonContent.Create(new { kind = "PaymentReminder" })
        };
        q2.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var q2Res = await admin.SendAsync(q2);
        Assert.Equal(HttpStatusCode.Conflict, q2Res.StatusCode);
        var q2Body = await Json(q2Res);
        Assert.Equal("reminder_cooldown", q2Body.GetProperty("code").GetString());
        Assert.True(q2Body.GetProperty("retryAfterSeconds").GetInt32() > 0);

        // Summary, Preview e Campaigns
        var summary = await Json(await admin.GetAsync($"/api/groups/{groupId}/admin/notifications/summary"));
        Assert.True(summary.GetProperty("membersActive").GetInt32() >= 3);
        Assert.True(summary.GetProperty("sentLast30d").GetInt32() >= 1);

        var preview = await Json(await admin.PostAsJsonAsync($"/api/groups/{groupId}/admin/notifications/preview", new { quick = "PaymentReminder" }));
        Assert.Equal(1, preview.GetProperty("recipients").GetInt32());

        var campaigns = await Json(await admin.GetAsync($"/api/groups/{groupId}/admin/notifications/campaigns?page=1&pageSize=10"));
        Assert.True(campaigns.GetProperty("total").GetInt32() >= 1);
    }
}
