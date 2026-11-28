using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FutebolAtemporal.Application.Notifications;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Domain.Enums;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Notifications;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>
/// Correções da revisão PWA: disparos simultâneos (idempotência e intervalo), seleção da rodada fechada,
/// entrega por aparelho sem duplicar, despachantes concorrentes, vínculo revalidado e tela bloqueada genérica.
/// </summary>
public class NotificationsHardeningApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public NotificationsHardeningApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=notif-hard-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
            builder.UseSetting("WebPush:Mode", "Simulator");
        });
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(HttpClient Admin, Guid GroupId)> LoginAdminAsync()
    {
        var admin = NewClient();
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (admin, (await Json(login)).GetProperty("memberships")[0].GetProperty("groupId").GetGuid());
    }

    private async Task<(HttpClient Client, Guid UserId, Guid ProfileId)> RegisterAsync(string phone, string name)
    {
        var c = NewClient();
        var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone, displayName = name, password = TestAuth.Password });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        var body = await Json(reg);
        return (c, body.GetProperty("id").GetGuid(), body.GetProperty("playerProfileId").GetGuid());
    }

    private static HttpRequestMessage Quick(Guid groupId, string kind, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/quick")
        {
            Content = JsonContent.Create(new { kind })
        };
        req.Headers.Add("Idempotency-Key", key);
        return req;
    }

    private static (string P256dh, string Auth) Keys()
    {
        var (pub, _) = WebPushOptions.GenerateP256KeyPair();
        return (pub, NotificationPolicies.ToBase64Url(Guid.NewGuid().ToByteArray()));
    }

    [Fact]
    public async Task Simultaneous_quick_reminders_same_key_replay_and_different_keys_respect_cooldown()
    {
        var (admin, groupId) = await LoginAdminAsync();
        await RegisterAsync("11964000001", "Alvo");

        // Mesma chave, 6 chamadas simultâneas: uma cria (201), as outras repetem o resultado (200). Nunca 500.
        var key = Guid.NewGuid().ToString("N");
        var same = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => admin.SendAsync(Quick(groupId, "MatchAvailable", key))));
        Assert.DoesNotContain(same, r => (int)r.StatusCode >= 500);
        Assert.Single(same, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(same.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = new HashSet<Guid>();
        foreach (var r in same) ids.Add((await Json(r)).GetProperty("id").GetGuid());
        Assert.Single(ids);

        // Mesma chave com outro conteúdo: 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.SendAsync(Quick(groupId, "RegistrationReminder", key))).StatusCode);

        // Chaves diferentes e simultâneas: só uma passa; as outras esbarram no intervalo de 30 min.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Afasta a campanha MatchAvailable para fora da janela e testa chaves diferentes ao mesmo tempo.
            foreach (var c in await db.NotificationCampaigns.ToListAsync()) c.CreatedAt = c.CreatedAt.AddHours(-1);
            foreach (var c in await db.NotificationCooldowns.ToListAsync()) c.LastSentAt = c.LastSentAt.AddHours(-1);
            await db.SaveChangesAsync();
        }
        var parallel = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            admin.SendAsync(Quick(groupId, "MatchAvailable", Guid.NewGuid().ToString("N")))));
        Assert.DoesNotContain(parallel, r => (int)r.StatusCode >= 500);
        Assert.Single(parallel, r => r.StatusCode == HttpStatusCode.Created);
        foreach (var r in parallel.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("reminder_cooldown", (await Json(r)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Payment_reminder_reaches_fee_of_last_closed_round_even_with_next_round_open()
    {
        var (admin, groupId) = await LoginAdminAsync();
        var (_, debtorUser, debtorProfile) = await RegisterAsync("11964000011", "Devedor");

        // Rodada 1: já fechada, com diária pendente (fechamento simulado direto no banco de teste).
        var closedId = (await Json(await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Rodada fechada", startsAt = DateTimeOffset.UtcNow.AddDays(-2), location = "Arena", playerLimit = 14, openRegistrations = true
        }))).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Matches.FirstAsync(m => m.Id == closedId)).Status = MatchStatus.Completed;
            db.MatchFeePayments.Add(new MatchFeePayment
            {
                Id = Guid.NewGuid(), GroupId = groupId, MatchId = closedId, PlayerProfileId = debtorProfile,
                Amount = 25m, Status = PaymentStatus.Pending, DueAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        // Rodada 2: aberta depois e com data mais à frente — antes ocupava "próxima" e "mais recente".
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Próxima", startsAt = DateTimeOffset.UtcNow.AddDays(5), location = "Arena", playerLimit = 14, openRegistrations = true
        })).StatusCode);

        var res = await admin.SendAsync(Quick(groupId, "PaymentReminder", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(1, (await Json(res)).GetProperty("recipientCount").GetInt32());

        using var check = _factory.Services.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await cdb.UserNotifications.AnyAsync(n => n.UserId == debtorUser && n.Category == NotificationCategory.PaymentReminder));
    }

    [Fact]
    public async Task Retry_reaches_only_pending_devices_concurrent_dispatchers_do_not_duplicate_and_lockscreen_is_generic()
    {
        var (admin, groupId) = await LoginAdminAsync();
        var (player, userId, _) = await RegisterAsync("11964000021", "Dois Aparelhos");

        var epOk = $"https://fcm.googleapis.com/fcm/send/ok-{Guid.NewGuid():N}";
        var epFlaky = $"https://fcm.googleapis.com/fcm/send/flaky-{Guid.NewGuid():N}";
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        var flakyCalls = 0;
        store.Overrides[epOk] = (_, _) => { Thread.Sleep(150); return new WebPushSendResult(WebPushSendOutcome.Sent, 201); };
        store.Overrides[epFlaky] = (_, _) => Interlocked.Increment(ref flakyCalls) == 1
            ? new WebPushSendResult(WebPushSendOutcome.TransientError, 500, "HTTP 500")
            : new WebPushSendResult(WebPushSendOutcome.Sent, 201);

        foreach (var ep in new[] { epOk, epFlaky })
        {
            var (p, a) = Keys();
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(player, new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);
        }

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/broadcast")
        {
            Content = JsonContent.Create(new { audience = "AllMembers", title = "Mudança de horário", body = "Pelada às 21h no campo 2", actionUrl = "/" })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(req)).StatusCode);

        var runner = _factory.Services.GetRequiredService<INotificationDispatchRunner>();
        // Dois despachantes ao mesmo tempo (além do worker em segundo plano).
        await Task.WhenAll(runner.ProcessPendingAsync(), runner.ProcessPendingAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 40; i++)
            {
                var n = await db.UserNotifications.AsNoTracking().FirstAsync(x => x.UserId == userId && x.Category == NotificationCategory.AdminBroadcast);
                if (n.PushState != NotificationPushState.Pending) break;
                if (n.ClaimedUntil is not null) { await Task.Delay(100); continue; } // outro despachante está no meio do envio
                await db.UserNotifications.Where(x => x.Id == n.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextPushAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
                await runner.ProcessPendingAsync();
            }

            var notif = await db.UserNotifications.AsNoTracking().FirstAsync(x => x.UserId == userId && x.Category == NotificationCategory.AdminBroadcast);
            Assert.Equal(NotificationPushState.Sent, notif.PushState);
            Assert.Equal(2, await db.NotificationDeliveries.CountAsync(d => d.NotificationId == notif.Id));
        }

        var okSends = store.Sent.Where(s => s.Endpoint == epOk && s.Result.Outcome == WebPushSendOutcome.Sent).ToList();
        Assert.Single(okSends); // aparelho que já recebeu não recebe de novo na nova tentativa
        Assert.Single(store.Sent, s => s.Endpoint == epFlaky && s.Result.Outcome == WebPushSendOutcome.Sent);

        // Tela bloqueada: texto genérico, sem título nem corpo do comunicado.
        Assert.All(store.Sent.Where(s => s.Endpoint == epOk || s.Endpoint == epFlaky), s =>
        {
            Assert.Equal(NotificationDispatchRunner.LockscreenTitle, s.Payload.Title);
            Assert.Equal(NotificationDispatchRunner.LockscreenBody, s.Payload.Body);
            Assert.DoesNotContain("21h", s.Payload.Body);
        });
    }

    [Fact]
    public async Task Cooldown_row_written_by_another_instance_blocks_quick_reminder()
    {
        var (admin, groupId) = await LoginAdminAsync();
        await RegisterAsync("11964000041", "Alvo Intervalo");

        // Outra instância da API acabou de enviar (só a linha de intervalo existe, nenhuma campanha local).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.NotificationCooldowns.Add(new NotificationCooldown
            {
                GroupId = groupId, Category = NotificationCategory.MatchOpen, LastSentAt = DateTimeOffset.UtcNow.AddMinutes(-5), Version = 1
            });
            await db.SaveChangesAsync();
        }

        var blocked = await admin.SendAsync(Quick(groupId, "MatchAvailable", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await Json(blocked);
        Assert.Equal("reminder_cooldown", body.GetProperty("code").GetString());

        // Fora da janela, envia e atualiza a linha (compare-and-set no Version).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.NotificationCooldowns.FirstAsync(c => c.GroupId == groupId && c.Category == NotificationCategory.MatchOpen);
            row.LastSentAt = DateTimeOffset.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(Quick(groupId, "MatchAvailable", Guid.NewGuid().ToString("N")))).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().NotificationCooldowns.AsNoTracking()
                .FirstAsync(c => c.GroupId == groupId && c.Category == NotificationCategory.MatchOpen);
            Assert.Equal(2, row.Version);
            Assert.True(row.LastSentAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        }
    }

    [Fact]
    public async Task Claim_is_renewed_before_each_device_and_released_at_the_end()
    {
        var (admin, groupId) = await LoginAdminAsync();
        var (player, userId, _) = await RegisterAsync("11964000051", "Três Aparelhos");
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        for (var i = 0; i < 3; i++)
        {
            var ep = $"https://fcm.googleapis.com/fcm/send/renew-{i}-{Guid.NewGuid():N}";
            store.Overrides[ep] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Sent, 201);
            var (p, a) = Keys();
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(player, new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);
        }

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/groups/{groupId}/admin/notifications/broadcast")
        {
            Content = JsonContent.Create(new { audience = "AllMembers", title = "Renovação", body = "Teste de reserva", actionUrl = "/" })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(req)).StatusCode);

        await _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        UserNotification? n = null;
        for (var i = 0; i < 40; i++) // o worker em segundo plano pode estar com o aviso
        {
            n = await db.UserNotifications.AsNoTracking().FirstAsync(x => x.UserId == userId && x.Category == NotificationCategory.AdminBroadcast);
            if (n.PushState != NotificationPushState.Pending) break;
            await Task.Delay(100);
        }
        Assert.Equal(NotificationPushState.Sent, n!.PushState);
        Assert.Null(n.ClaimedUntil);
        // 1 reserva + 1 renovação por aparelho (3) = pelo menos 4 versões.
        Assert.True(n.DispatchVersion >= 4, $"DispatchVersion={n.DispatchVersion}");
        Assert.Equal(3, await db.NotificationDeliveries.CountAsync(d => d.NotificationId == n.Id));
    }

    [Fact]
    public async Task Dispatch_skips_users_who_lost_active_membership()
    {
        var (_, groupId) = await LoginAdminAsync();
        var (player, userId, _) = await RegisterAsync("11964000031", "Saiu do Grupo");
        var ep = $"https://fcm.googleapis.com/fcm/send/left-{Guid.NewGuid():N}";
        var (p, a) = Keys();
        Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(player, new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);

        Guid notifId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.GroupMemberships.FirstAsync(m => m.UserId == userId && m.GroupId == groupId)).Status = MembershipStatus.Inactive;
            var campaign = new NotificationCampaign
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = NotificationSource.AdminBroadcast, Category = NotificationCategory.AdminBroadcast,
                Audience = NotificationAudience.AllMembers, Title = "Antes de sair", Body = "Aviso criado antes da saída", ActionUrl = "/", RecipientCount = 1
            };
            var notif = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = userId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending
            };
            db.NotificationCampaigns.Add(campaign);
            db.UserNotifications.Add(notif);
            await db.SaveChangesAsync();
            notifId = notif.Id;
        }

        await _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync();

        using var check = _factory.Services.CreateScope();
        var state = (await check.ServiceProvider.GetRequiredService<AppDbContext>().UserNotifications.AsNoTracking().FirstAsync(n => n.Id == notifId)).PushState;
        Assert.Equal(NotificationPushState.Skipped, state);
        Assert.DoesNotContain(_factory.Services.GetRequiredService<SimulatedWebPushStore>().Sent, s => s.Endpoint == ep);
    }

    [Fact]
    public async Task Concurrent_idempotency_race_before_precheck_cooldown_returns_200_or_422_never_409()
    {
        var dbFile = $"notif-race-pre-{Guid.NewGuid():N}.db";
        var connStr = $"Data Source={dbFile}";
        var interceptor = new SqlTriggerInterceptor("NotificationCooldowns");

        await using var raceFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", connStr);
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
            builder.UseSetting("WebPush:Mode", "Simulator");
            builder.ConfigureServices(services =>
                services.AddDbContext<AppDbContext>(o => o.AddInterceptors(interceptor)));
        });

        var admin = raceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var groupId = (await Json(login)).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var regClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await regClient.PostAsJsonAsync("/api/auth/register",
            new { phone = "11964000061", displayName = "Alvo Corrida Pre", password = TestAuth.Password })).StatusCode);

        // Cenário 1: outra instância grava a campanha + cooldown da mesma chave logo após FindByIdempotencyKeyAsync
        // e antes da leitura prévia de cooldown. Conteúdo igual => 200 com a mesma campanha.
        var sameKey = Guid.NewGuid().ToString("N");
        var winnerId = Guid.NewGuid();
        var matchAvailableHash = ComputeQuickHash(NotificationCategory.MatchOpen, NotificationAudience.AllMembers);
        interceptor.ArmOnce(() => SeedConcurrentCampaign(connStr, groupId, winnerId, NotificationCategory.MatchOpen, NotificationAudience.AllMembers, sameKey, matchAvailableHash));

        var replayRes = await admin.SendAsync(Quick(groupId, "MatchAvailable", sameKey));
        Assert.Equal(HttpStatusCode.OK, replayRes.StatusCode);
        Assert.Equal(winnerId, (await Json(replayRes)).GetProperty("id").GetGuid());

        // Limpa cooldown/campanha para testar conteúdo diferente na mesma corrida.
        await ClearNotificationsAsync(raceFactory.Services);

        // Cenário 2: mesma chave gravada por outra instância com conteúdo diferente => 422, nunca 409 cooldown.
        var mismatchKey = Guid.NewGuid().ToString("N");
        var differentHash = ComputeQuickHash(NotificationCategory.RegistrationReminder, NotificationAudience.NotResponded);
        interceptor.ArmOnce(() => SeedConcurrentCampaign(connStr, groupId, Guid.NewGuid(), NotificationCategory.MatchOpen, NotificationAudience.AllMembers, mismatchKey, differentHash));

        var mismatchRes = await admin.SendAsync(Quick(groupId, "MatchAvailable", mismatchKey));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatchRes.StatusCode);
        Assert.Equal("idempotency_key_mismatch", (await Json(mismatchRes)).GetProperty("code").GetString());

        // Cenário 3: chave diferente na mesma janela continua recebendo 409 reminder_cooldown.
        var otherKeyRes = await admin.SendAsync(Quick(groupId, "MatchAvailable", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, otherKeyRes.StatusCode);
        Assert.Equal("reminder_cooldown", (await Json(otherKeyRes)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_idempotency_race_before_transaction_cooldown_returns_200_or_422_never_409()
    {
        var dbFile = $"notif-race-tx-{Guid.NewGuid():N}.db";
        var connStr = $"Data Source={dbFile}";
        var txInterceptor = new TransactionTriggerInterceptor();

        await using var raceFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", connStr);
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
            builder.UseSetting("WebPush:Mode", "Simulator");
            builder.ConfigureServices(services =>
                services.AddDbContext<AppDbContext>(o => o.AddInterceptors(txInterceptor)));
        });

        var admin = raceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var groupId = (await Json(login)).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();

        var regClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await regClient.PostAsJsonAsync("/api/auth/register",
            new { phone = "11964000071", displayName = "Alvo Corrida Tx", password = TestAuth.Password })).StatusCode);

        // Cenário 1: leitura prévia de cooldown passa; outra instância grava campanha + cooldown antes da transação.
        // Dentro da transação ocorre ThrowIfInCooldown, mas a releitura pela chave devolve 200 com a mesma campanha.
        var sameKey = Guid.NewGuid().ToString("N");
        var winnerId = Guid.NewGuid();
        var matchAvailableHash = ComputeQuickHash(NotificationCategory.MatchOpen, NotificationAudience.AllMembers);
        txInterceptor.ArmOnce(() => SeedConcurrentCampaign(connStr, groupId, winnerId, NotificationCategory.MatchOpen, NotificationAudience.AllMembers, sameKey, matchAvailableHash));

        var replayRes = await admin.SendAsync(Quick(groupId, "MatchAvailable", sameKey));
        Assert.Equal(HttpStatusCode.OK, replayRes.StatusCode);
        Assert.Equal(winnerId, (await Json(replayRes)).GetProperty("id").GetGuid());

        await ClearNotificationsAsync(raceFactory.Services);

        // Cenário 2: mesma corrida antes da transação, mas conteúdo diferente => 422, nunca 409 cooldown.
        var mismatchKey = Guid.NewGuid().ToString("N");
        var differentHash = ComputeQuickHash(NotificationCategory.RegistrationReminder, NotificationAudience.AllMembers);
        txInterceptor.ArmOnce(() => SeedConcurrentCampaign(connStr, groupId, Guid.NewGuid(), NotificationCategory.MatchOpen, NotificationAudience.AllMembers, mismatchKey, differentHash));

        var mismatchRes = await admin.SendAsync(Quick(groupId, "MatchAvailable", mismatchKey));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatchRes.StatusCode);
        Assert.Equal("idempotency_key_mismatch", (await Json(mismatchRes)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispatch_does_not_suppress_non_duplicate_delivery_persistence_error_or_mark_sent()
    {
        var (_, groupId) = await LoginAdminAsync();
        var (player, userId, _) = await RegisterAsync("11964000081", "Falha Entrega");
        var ep = $"https://fcm.googleapis.com/fcm/send/dbfail-{Guid.NewGuid():N}";
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        store.Overrides[ep] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Sent, 201);

        var (p, a) = Keys();
        Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(player,
            new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);

        Guid notifId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var campaign = new NotificationCampaign
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = NotificationSource.AdminBroadcast, Category = NotificationCategory.AdminBroadcast,
                Audience = NotificationAudience.AllMembers, Title = "Erro DB", Body = "Falha simulada", ActionUrl = "/", RecipientCount = 1
            };
            var notif = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = userId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending
            };
            db.NotificationCampaigns.Add(campaign);
            db.UserNotifications.Add(notif);
            await db.SaveChangesAsync();
            notifId = notif.Id;

            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER trg_block_delivery_insert BEFORE INSERT ON NotificationDeliveries BEGIN SELECT RAISE(ABORT, 'simulated_delivery_failure'); END;");
        }

        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync());
        }
        finally
        {
            using var cleanup = _factory.Services.CreateScope();
            await cleanup.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS trg_block_delivery_insert;");
        }

        using var check = _factory.Services.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == notifId);
        Assert.Equal(NotificationPushState.Pending, saved.PushState);
        Assert.Equal(0, saved.PushSentCount);
        Assert.Equal(0, await cdb.NotificationDeliveries.CountAsync(d => d.NotificationId == notifId));
    }

    [Fact]
    public async Task Dispatch_suppresses_delivery_duplicate_only_after_confirming_persisted_row()
    {
        var (_, groupId) = await LoginAdminAsync();
        var (player, userId, _) = await RegisterAsync("11964000091", "Duplicata Real");
        var ep = $"https://fcm.googleapis.com/fcm/send/dup-{Guid.NewGuid():N}";
        var (p, a) = Keys();
        Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(player,
            new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);

        Guid notifId;
        Guid subId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            subId = (await db.PushSubscriptions.AsNoTracking().FirstAsync(s => s.Endpoint == ep)).Id;
            var campaign = new NotificationCampaign
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = NotificationSource.AdminBroadcast, Category = NotificationCategory.AdminBroadcast,
                Audience = NotificationAudience.AllMembers, Title = "Duplicata", Body = "Entrega concorrente", ActionUrl = "/", RecipientCount = 1
            };
            var notif = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = userId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending
            };
            db.NotificationCampaigns.Add(campaign);
            db.UserNotifications.Add(notif);
            await db.SaveChangesAsync();
            notifId = notif.Id;
        }

        // Simula outra instância gravando a entrega enquanto este despachante envia ao provedor.
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        store.Overrides[ep] = (_, _) =>
        {
            using var other = _factory.Services.CreateScope();
            var odb = other.ServiceProvider.GetRequiredService<AppDbContext>();
            odb.NotificationDeliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(),
                NotificationId = notifId,
                SubscriptionId = subId,
                SentAt = DateTimeOffset.UtcNow
            });
            odb.SaveChanges();
            return new WebPushSendResult(WebPushSendOutcome.Sent, 201);
        };

        await _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync();

        using var check = _factory.Services.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == notifId);
        Assert.Equal(NotificationPushState.Sent, saved.PushState);
        Assert.Equal(0, saved.PushSentCount); // outra instância já contabilizou; este despachante não soma duplicado
        Assert.Equal(1, await cdb.NotificationDeliveries.CountAsync(d => d.NotificationId == notifId && d.SubscriptionId == subId));
    }

    [Fact]
    public async Task Dispatch_revalidates_membership_and_subscription_during_batch_before_each_send()
    {
        var (_, groupId) = await LoginAdminAsync();
        var (firstPlayer, firstUserId, _) = await RegisterAsync("11964000101", "Primeiro do Lote");
        var (leavingPlayer, leavingUserId, _) = await RegisterAsync("11964000102", "Sai Durante Lote");
        var (revokingPlayer, revokingUserId, _) = await RegisterAsync("11964000103", "Revoga Durante Lote");

        var epFirst = $"https://fcm.googleapis.com/fcm/send/batch-first-{Guid.NewGuid():N}";
        var epLeaving = $"https://fcm.googleapis.com/fcm/send/batch-leave-{Guid.NewGuid():N}";
        var epRevoked = $"https://fcm.googleapis.com/fcm/send/batch-revoke-{Guid.NewGuid():N}";
        var epKept = $"https://fcm.googleapis.com/fcm/send/batch-kept-{Guid.NewGuid():N}";

        foreach (var (client, ep) in new[] { (firstPlayer, epFirst), (leavingPlayer, epLeaving), (revokingPlayer, epRevoked), (revokingPlayer, epKept) })
        {
            var (p, a) = Keys();
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(client,
                new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);
        }

        Guid firstNotifId;
        Guid leavingNotifId;
        Guid revokingNotifId;
        var baseTime = DateTimeOffset.UtcNow.AddSeconds(-30);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var campaign = new NotificationCampaign
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = NotificationSource.AdminBroadcast, Category = NotificationCategory.AdminBroadcast,
                Audience = NotificationAudience.AllMembers, Title = "Lote revalidado", Body = "Teste durante lote", ActionUrl = "/", RecipientCount = 3
            };
            var n1 = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = firstUserId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending, CreatedAt = baseTime
            };
            var n2 = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = leavingUserId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending, CreatedAt = baseTime.AddSeconds(1)
            };
            var n3 = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = revokingUserId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending, CreatedAt = baseTime.AddSeconds(2)
            };
            db.NotificationCampaigns.Add(campaign);
            db.UserNotifications.AddRange(n1, n2, n3);
            await db.SaveChangesAsync();
            firstNotifId = n1.Id;
            leavingNotifId = n2.Id;
            revokingNotifId = n3.Id;
        }

        // Durante o envio do 1º item do lote (já após o claim de todos os 3 avisos):
        // - leavingPlayer perde o vínculo ativo do grupo;
        // - revokingPlayer revoga um de seus dois aparelhos (epRevoked) e mantém epKept ativo.
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        store.Overrides[epFirst] = (_, _) =>
        {
            using var other = _factory.Services.CreateScope();
            var odb = other.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = odb.GroupMemberships.First(m => m.GroupId == groupId && m.UserId == leavingUserId);
            membership.Status = MembershipStatus.Inactive;
            var revokedSub = odb.PushSubscriptions.First(s => s.Endpoint == epRevoked);
            odb.PushSubscriptions.Remove(revokedSub);
            odb.SaveChanges();
            return new WebPushSendResult(WebPushSendOutcome.Sent, 201);
        };
        store.Overrides[epLeaving] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Sent, 201);
        store.Overrides[epRevoked] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Sent, 201);
        store.Overrides[epKept] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Sent, 201);

        var processed = await _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync();
        Assert.True(processed >= 3);

        // Nenhum envio ocorreu para o membro que saiu nem para a inscrição revogada durante o lote.
        Assert.Single(store.Sent, s => s.Endpoint == epFirst);
        Assert.DoesNotContain(store.Sent, s => s.Endpoint == epLeaving);
        Assert.DoesNotContain(store.Sent, s => s.Endpoint == epRevoked);
        Assert.Single(store.Sent, s => s.Endpoint == epKept);

        using var check = _factory.Services.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(NotificationPushState.Sent, (await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == firstNotifId)).PushState);
        Assert.Equal(NotificationPushState.Skipped, (await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == leavingNotifId)).PushState);
        Assert.Equal(NotificationPushState.Sent, (await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == revokingNotifId)).PushState);
    }

    [Fact]
    public async Task Dispatch_stops_mid_notification_when_membership_or_second_device_is_revoked_between_sends()
    {
        var (_, groupId) = await LoginAdminAsync();
        var (playerA, userA, _) = await RegisterAsync("11964000111", "Sai Entre Aparelhos");
        var (playerB, userB, _) = await RegisterAsync("11964000112", "Revoga Segundo Aparelho");

        var epA1 = $"https://fcm.googleapis.com/fcm/send/mid-a1-{Guid.NewGuid():N}";
        var epA2 = $"https://fcm.googleapis.com/fcm/send/mid-a2-{Guid.NewGuid():N}";
        var epB1 = $"https://fcm.googleapis.com/fcm/send/mid-b1-{Guid.NewGuid():N}";
        var epB2 = $"https://fcm.googleapis.com/fcm/send/mid-b2-{Guid.NewGuid():N}";

        foreach (var (client, ep) in new[] { (playerA, epA1), (playerA, epA2), (playerB, epB1), (playerB, epB2) })
        {
            var (p, a) = Keys();
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(client,
                new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);
        }

        Guid notifAId;
        Guid notifBId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var campaign = new NotificationCampaign
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = NotificationSource.AdminBroadcast, Category = NotificationCategory.AdminBroadcast,
                Audience = NotificationAudience.AllMembers, Title = "Entre aparelhos", Body = "Revalidação por aparelho", ActionUrl = "/", RecipientCount = 2
            };
            var na = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = userA, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending
            };
            var nb = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = userB, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending
            };
            db.NotificationCampaigns.Add(campaign);
            db.UserNotifications.AddRange(na, nb);
            await db.SaveChangesAsync();
            notifAId = na.Id;
            notifBId = nb.Id;
        }

        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        // No 1º aparelho de playerA (independente da ordem), desativa o vínculo antes do 2º aparelho.
        void DeactivateUserA()
        {
            using var other = _factory.Services.CreateScope();
            var odb = other.ServiceProvider.GetRequiredService<AppDbContext>();
            odb.GroupMemberships.First(m => m.GroupId == groupId && m.UserId == userA).Status = MembershipStatus.Inactive;
            odb.SaveChanges();
        }
        store.Overrides[epA1] = (_, _) => { DeactivateUserA(); return new WebPushSendResult(WebPushSendOutcome.Sent, 201); };
        store.Overrides[epA2] = (_, _) => { DeactivateUserA(); return new WebPushSendResult(WebPushSendOutcome.Sent, 201); };

        // No 1º aparelho de playerB (independente da ordem), revoga o outro aparelho antes de ele ser enviado.
        void RevokeOtherSubOfUserB(string currentEp)
        {
            var otherEp = currentEp == epB1 ? epB2 : epB1;
            using var other = _factory.Services.CreateScope();
            var odb = other.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = odb.PushSubscriptions.FirstOrDefault(s => s.Endpoint == otherEp);
            if (sub is not null)
            {
                odb.PushSubscriptions.Remove(sub);
                odb.SaveChanges();
            }
        }
        store.Overrides[epB1] = (_, _) => { RevokeOtherSubOfUserB(epB1); return new WebPushSendResult(WebPushSendOutcome.Sent, 201); };
        store.Overrides[epB2] = (_, _) => { RevokeOtherSubOfUserB(epB2); return new WebPushSendResult(WebPushSendOutcome.Sent, 201); };

        await _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync();

        // playerA: apenas 1 aparelho recebeu antes da perda de vínculo; o 2º não foi enviado.
        Assert.Equal(1, store.Sent.Count(s => s.Endpoint == epA1 || s.Endpoint == epA2));
        // playerB: apenas 1 aparelho recebeu; o 2º foi revogado antes do envio e não foi chamado.
        Assert.Equal(1, store.Sent.Count(s => s.Endpoint == epB1 || s.Endpoint == epB2));

        using var check = _factory.Services.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(NotificationPushState.PartiallySent, (await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == notifAId)).PushState);
        Assert.Equal(NotificationPushState.Sent, (await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == notifBId)).PushState);
    }

    [Fact]
    public async Task Dispatch_handles_subscription_removed_during_successful_send_without_aborting_batch()
    {
        var (_, groupId) = await LoginAdminAsync();
        var (revokingPlayer, revokingUserId, _) = await RegisterAsync("11964000121", "Revoga Em Voo");
        var (nextPlayer, nextUserId, _) = await RegisterAsync("11964000122", "Proximo do Lote");

        var epRevoking = $"https://fcm.googleapis.com/fcm/send/inflight-rm-{Guid.NewGuid():N}";
        var epNext = $"https://fcm.googleapis.com/fcm/send/inflight-next-{Guid.NewGuid():N}";

        foreach (var (client, ep) in new[] { (revokingPlayer, epRevoking), (nextPlayer, epNext) })
        {
            var (p, a) = Keys();
            Assert.Equal(HttpStatusCode.OK, (await TestAuth.SubscribePushAsync(client,
                new { endpoint = ep, keys = new { p256dh = p, auth = a } })).StatusCode);
        }

        Guid campaignId;
        Guid revokingNotifId;
        Guid nextNotifId;
        var baseTime = DateTimeOffset.UtcNow.AddSeconds(-20);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var campaign = new NotificationCampaign
            {
                Id = Guid.NewGuid(), GroupId = groupId, Source = NotificationSource.AdminBroadcast, Category = NotificationCategory.AdminBroadcast,
                Audience = NotificationAudience.AllMembers, Title = "Revogação em voo", Body = "Teste de lote resiliente", ActionUrl = "/", RecipientCount = 2
            };
            var n1 = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = revokingUserId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending, CreatedAt = baseTime
            };
            var n2 = new UserNotification
            {
                Id = Guid.NewGuid(), GroupId = groupId, UserId = nextUserId, CampaignId = campaign.Id, Category = NotificationCategory.AdminBroadcast,
                Title = campaign.Title, Body = campaign.Body, ActionUrl = "/", PushState = NotificationPushState.Pending, CreatedAt = baseTime.AddSeconds(1)
            };
            db.NotificationCampaigns.Add(campaign);
            db.UserNotifications.AddRange(n1, n2);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;
            revokingNotifId = n1.Id;
            nextNotifId = n2.Id;
        }

        // Remove a inscrição enquanto SendAsync está em voo e retorna sucesso (201).
        var store = _factory.Services.GetRequiredService<SimulatedWebPushStore>();
        store.Overrides[epRevoking] = (_, _) =>
        {
            using var other = _factory.Services.CreateScope();
            var odb = other.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = odb.PushSubscriptions.First(s => s.Endpoint == epRevoking);
            odb.PushSubscriptions.Remove(sub);
            odb.SaveChanges();
            return new WebPushSendResult(WebPushSendOutcome.Sent, 201);
        };
        store.Overrides[epNext] = (_, _) => new WebPushSendResult(WebPushSendOutcome.Sent, 201);

        var processed = await _factory.Services.GetRequiredService<INotificationDispatchRunner>().ProcessPendingAsync();
        Assert.True(processed >= 2);
        Assert.Single(store.Sent, s => s.Endpoint == epNext);

        using var check = _factory.Services.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedRevoking = await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == revokingNotifId);
        var savedNext = await cdb.UserNotifications.AsNoTracking().FirstAsync(n => n.Id == nextNotifId);
        var savedCampaign = await cdb.NotificationCampaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId);

        Assert.Equal(NotificationPushState.Skipped, savedRevoking.PushState);
        Assert.Null(savedRevoking.ClaimedUntil);
        Assert.Equal(0, savedRevoking.PushSentCount);
        Assert.Equal(0, await cdb.NotificationDeliveries.CountAsync(d => d.NotificationId == revokingNotifId));

        Assert.Equal(NotificationPushState.Sent, savedNext.PushState);
        Assert.Null(savedNext.ClaimedUntil);
        Assert.Equal(1, savedNext.PushSentCount);
        Assert.Equal(1, await cdb.NotificationDeliveries.CountAsync(d => d.NotificationId == nextNotifId));

        Assert.Equal(1, savedCampaign.PushSentCount);
        Assert.Equal(0, savedCampaign.PushFailedCount);
    }

    private static string ComputeQuickHash(NotificationCategory category, NotificationAudience audience)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "quick", category = category.ToString(), audience = audience.ToString() });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void SeedConcurrentCampaign(
        string connectionString, Guid groupId, Guid campaignId, NotificationCategory category, NotificationAudience audience, string key, string requestHash)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options;
        using var db = new AppDbContext(opts);
        var now = DateTimeOffset.UtcNow;
        db.NotificationCooldowns.Add(new NotificationCooldown
        {
            GroupId = groupId,
            Category = category,
            LastSentAt = now,
            Version = 1
        });
        db.NotificationCampaigns.Add(new NotificationCampaign
        {
            Id = campaignId,
            GroupId = groupId,
            Source = NotificationSource.AdminQuick,
            Category = category,
            Audience = audience,
            Title = "⚽ Partida liberada!",
            Body = "⚽ A partida e as informações da rodada estão disponíveis no app.",
            ActionUrl = "/partida",
            IdempotencyKey = key,
            RequestHash = requestHash,
            RecipientCount = 1,
            CreatedAt = now
        });
        db.SaveChanges();
    }

    private static async Task ClearNotificationsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.NotificationCooldowns.RemoveRange(await db.NotificationCooldowns.ToListAsync());
        db.NotificationCampaigns.RemoveRange(await db.NotificationCampaigns.ToListAsync());
        await db.SaveChangesAsync();
    }

    private sealed class SqlTriggerInterceptor(string sqlMarker) : DbCommandInterceptor
    {
        private Action? _pending;

        public void ArmOnce(Action action) => Interlocked.Exchange(ref _pending, action);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(sqlMarker, StringComparison.Ordinal))
            {
                var action = Interlocked.Exchange(ref _pending, null);
                action?.Invoke();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TransactionTriggerInterceptor : DbTransactionInterceptor
    {
        private Action? _pending;

        public void ArmOnce(Action action) => Interlocked.Exchange(ref _pending, action);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            var action = Interlocked.Exchange(ref _pending, null);
            action?.Invoke();
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }
}
