using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Application.Payments;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

public class PixMercadoPagoApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private const string WebhookSecret = "whsec_futebol_test_secret_2026";
    private readonly WebApplicationFactory<Program> _factory;

    public PixMercadoPagoApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=pix-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
            builder.UseSetting("MercadoPago:UseSandboxSimulator", "true");
            builder.UseSetting("MercadoPago:WebhookSecret", WebhookSecret);
        });
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<(Guid ProfileId, Guid GroupId)> RegisterAsync(HttpClient client, string phone, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/register", new { phone, displayName = name });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (
            json.GetProperty("playerProfileId").GetGuid(),
            json.GetProperty("memberships")[0].GetProperty("groupId").GetGuid());
    }

    private static async Task<Guid> LoginAdminAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
    }

    [Fact]
    public async Task Membership_pix_reuses_valid_charge_regenerates_after_expiration_blocks_idor_and_processes_hmac_webhook_idempotently()
    {
        var admin = CreateClient();
        var member = CreateClient();
        var outsider = CreateClient();

        var (memberProfileId, groupId) = await RegisterAsync(member, "11977770001", "Carlos Mensalista");
        await RegisterAsync(outsider, "11977770002", "Outro Jogador");
        await LoginAdminAsync(admin);

        // Abre competência e adiciona mensalista
        var addMem = await admin.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = memberProfileId });
        Assert.Equal(HttpStatusCode.OK, addMem.StatusCode);
        var periodResp = await admin.PostAsJsonAsync($"/api/groups/{groupId}/memberships/periods", new { year = 2026, month = 11 });
        Assert.Equal(HttpStatusCode.OK, periodResp.StatusCode);

        // Jogador vê sua mensalidade em my-payments
        var myPayResp = await member.GetAsync($"/api/groups/{groupId}/my-payments");
        Assert.Equal(HttpStatusCode.OK, myPayResp.StatusCode);
        var myPay = await myPayResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(myPay.GetProperty("isMonthlyMember").GetBoolean());
        var paymentId = myPay.GetProperty("memberships")[0].GetProperty("paymentId").GetGuid();

        // Outro jogador não pode gerar Pix da mensalidade de Carlos (IDOR -> 403)
        var idorGen = await outsider.PostAsync($"/api/payments/mensalidades/{paymentId}/pix", null);
        Assert.Equal(HttpStatusCode.Forbidden, idorGen.StatusCode);

        // Primeira geração cria cobrança nova
        var firstResp = await member.PostAsync($"/api/payments/mensalidades/{paymentId}/pix", null);
        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<JsonElement>();
        var firstChargeId = first.GetProperty("id").GetGuid();
        var firstMpId = first.GetProperty("mpPaymentId").GetString()!;
        Assert.False(first.GetProperty("reused").GetBoolean());
        Assert.Equal("Pending", first.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("qrCode").GetString()));
        Assert.StartsWith("iVBOR", first.GetProperty("qrCodeBase64").GetString());

        // Outro jogador não pode ler/verificar a cobrança de Carlos (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/payments/pix/{firstChargeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsync($"/api/payments/pix/{firstChargeId}/verify", null)).StatusCode);

        // Segunda chamada dentro do prazo reaproveita o mesmo QR Code
        var secondResp = await member.PostAsync($"/api/payments/mensalidades/{paymentId}/pix", null);
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);
        var second = await secondResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(second.GetProperty("reused").GetBoolean());
        Assert.Equal(firstChargeId, second.GetProperty("id").GetGuid());
        Assert.Equal(firstMpId, second.GetProperty("mpPaymentId").GetString());

        // Força expiração no banco e confirma que nova chamada marca a antiga como Expired e gera nova cobrança
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.PixCharges.FirstAsync(x => x.Id == firstChargeId);
            c.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var thirdResp = await member.PostAsync($"/api/payments/mensalidades/{paymentId}/pix", null);
        Assert.Equal(HttpStatusCode.OK, thirdResp.StatusCode);
        var third = await thirdResp.Content.ReadFromJsonAsync<JsonElement>();
        var thirdChargeId = third.GetProperty("id").GetGuid();
        var thirdMpId = third.GetProperty("mpPaymentId").GetString()!;
        Assert.False(third.GetProperty("reused").GetBoolean());
        Assert.NotEqual(firstChargeId, thirdChargeId);
        Assert.NotEqual(firstMpId, thirdMpId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var oldCharge = await db.PixCharges.AsNoTracking().FirstAsync(x => x.Id == firstChargeId);
            Assert.Equal("Expired", oldCharge.Status.ToString());
        }

        // Webhook sem assinatura ou com assinatura inválida -> 401
        var anonymous = CreateClient();
        var badWebhook = await anonymous.PostAsJsonAsync($"/api/webhooks/mercadopago?type=payment&data.id={thirdMpId}", new { type = "payment", data = new { id = thirdMpId } });
        Assert.Equal(HttpStatusCode.Unauthorized, badWebhook.StatusCode);

        var wrongSigReq = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/mercadopago?type=payment&data.id={thirdMpId}")
        {
            Content = JsonContent.Create(new { type = "payment", data = new { id = thirdMpId } })
        };
        wrongSigReq.Headers.Add("x-signature", "ts=1732600000,v1=deadbeef");
        wrongSigReq.Headers.Add("x-request-id", "req-bad");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(wrongSigReq)).StatusCode);

        // Marca pagamento como aprovado no simulador (simulando confirmação na fonte MP) e envia webhook com HMAC válido
        using (var scope = _factory.Services.CreateScope())
        {
            var sim = scope.ServiceProvider.GetRequiredService<ISimulatedMercadoPagoControl>();
            sim.SetStatus(thirdMpId, "approved", DateTimeOffset.UtcNow);
        }

        async Task<HttpResponseMessage> SendSignedWebhookAsync(string reqId)
        {
            const string ts = "1732600123";
            var manifest = MercadoPagoSignatureValidator.BuildManifest(thirdMpId, reqId, ts);
            var v1 = MercadoPagoSignatureValidator.ComputeHmacSha256Hex(manifest, WebhookSecret);
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/mercadopago?type=payment&data.id={thirdMpId}")
            {
                Content = JsonContent.Create(new { type = "payment", data = new { id = thirdMpId } })
            };
            req.Headers.Add("x-signature", $"ts={ts},v1={v1}");
            req.Headers.Add("x-request-id", reqId);
            return await anonymous.SendAsync(req);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendSignedWebhookAsync("req-ok-1")).StatusCode);
        // Reenvio idempotente do webhook -> 200 OK sem duplicar auditoria
        Assert.Equal(HttpStatusCode.OK, (await SendSignedWebhookAsync("req-ok-2")).StatusCode);

        // Verifica estado via polling GET /api/payments/pix/{id}
        var polled = await (await member.GetAsync($"/api/payments/pix/{thirdChargeId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", polled.GetProperty("status").GetString());
        Assert.Equal("Paid", polled.GetProperty("obligationStatus").GetString());

        // Tentar gerar novo Pix para mensalidade já paga -> 409 Conflict
        var alreadyPaid = await member.PostAsync($"/api/payments/mensalidades/{paymentId}/pix", null);
        Assert.Equal(HttpStatusCode.Conflict, alreadyPaid.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audits = await db.AuditEntries.CountAsync(a => a.Action == "PixPaymentApproved" && a.EntityId == paymentId);
            Assert.Equal(1, audits);
        }
    }

    [Fact]
    public async Task MatchDay_casual_player_pix_verify_admin_settings_fees_and_batch_reconcile_work_end_to_end()
    {
        var admin = CreateClient();
        var casual1 = CreateClient();
        var casual2 = CreateClient();

        var (p1, groupId) = await RegisterAsync(casual1, "11977770011", "Avulso Um");
        var (p2, _) = await RegisterAsync(casual2, "11977770012", "Avulso Dois");
        await LoginAdminAsync(admin);

        // Admin configura valores e expiração
        var badSettings = await admin.PutAsJsonAsync($"/api/groups/{groupId}/payments/settings", new { defaultMonthlyFee = 160m, defaultMatchDayFee = 35m, pixExpirationMinutes = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, badSettings.StatusCode);

        var okSettings = await admin.PutAsJsonAsync($"/api/groups/{groupId}/payments/settings", new { defaultMonthlyFee = 160m, defaultMatchDayFee = 35m, pixExpirationMinutes = 45 });
        Assert.Equal(HttpStatusCode.OK, okSettings.StatusCode);
        var gw = await okSettings.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Simulator", gw.GetProperty("mode").GetString());
        Assert.Equal(35m, gw.GetProperty("defaultMatchDayFee").GetDecimal());
        Assert.Equal(45, gw.GetProperty("pixExpirationMinutes").GetInt32());

        // Jogador comum não pode alterar configurações (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await casual1.PutAsJsonAsync($"/api/groups/{groupId}/payments/settings", new { defaultMonthlyFee = 10m, defaultMatchDayFee = 10m, pixExpirationMinutes = 60 })).StatusCode);

        // Cria partida, abre inscrições e inscreve os dois avulsos
        var createMatch = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Pelada Pix",
            startsAt = DateTimeOffset.UtcNow.AddDays(2),
            location = "Arena Central",
            playerLimit = 14
        });
        Assert.Equal(HttpStatusCode.OK, createMatch.StatusCode);
        var matchId = (await createMatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationOpen" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await casual1.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await casual2.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        // Avulso 1 vê a diária de R$ 35 em my-payments e gera o Pix
        var my1 = await (await casual1.GetAsync($"/api/groups/{groupId}/my-payments")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(my1.GetProperty("isMonthlyMember").GetBoolean());
        Assert.Single(my1.GetProperty("matchDays").EnumerateArray());
        Assert.Equal(35m, my1.GetProperty("matchDays")[0].GetProperty("amount").GetDecimal());

        // Avulso 1 não pode gerar diária para Avulso 2 (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await casual1.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { playerProfileId = p2 })).StatusCode);

        var pix1 = await (await casual1.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var charge1Id = pix1.GetProperty("id").GetGuid();
        var mp1Id = pix1.GetProperty("mpPaymentId").GetString()!;
        Assert.Equal("MatchDay", pix1.GetProperty("kind").GetString());
        Assert.Equal(35m, pix1.GetProperty("amount").GetDecimal());

        // Simula pagamento aprovado na fonte e jogador aciona "Já paguei / Verificar"
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ISimulatedMercadoPagoControl>().SetStatus(mp1Id, "approved", DateTimeOffset.UtcNow);
        }

        var verify1 = await (await casual1.PostAsync($"/api/payments/pix/{charge1Id}/verify", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(verify1.GetProperty("wasUpdated").GetBoolean());
        Assert.Equal("Paid", verify1.GetProperty("obligationStatus").GetString());

        // Segunda verificação é idempotente
        var verify1Again = await (await casual1.PostAsync($"/api/payments/pix/{charge1Id}/verify", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(verify1Again.GetProperty("wasUpdated").GetBoolean());

        // Avulso 2 gera Pix; admin aprova na fonte e testa reconciliação em lote
        var pix2 = await (await casual2.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var mp2Id = pix2.GetProperty("mpPaymentId").GetString()!;
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ISimulatedMercadoPagoControl>().SetStatus(mp2Id, "approved", DateTimeOffset.UtcNow);
        }

        var rec = await (await admin.PostAsync($"/api/groups/{groupId}/payments/pix/reconcile", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, rec.GetProperty("checked").GetInt32());
        Assert.Equal(1, rec.GetProperty("approved").GetInt32());
        Assert.Equal(0, rec.GetProperty("errors").GetInt32());

        // Admin lista diárias da partida e overview Pix (charges sem qrCode/qrCodeBase64)
        var fees = await (await admin.GetAsync($"/api/groups/{groupId}/matches/{matchId}/fees")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, fees.GetArrayLength());
        Assert.All(fees.EnumerateArray(), f =>
        {
            Assert.Equal("Paid", f.GetProperty("status").GetString());
            Assert.Equal("Pix", f.GetProperty("paymentSource").GetString());
        });

        var overview = await (await admin.GetAsync($"/api/groups/{groupId}/payments/pix")).Content.ReadFromJsonAsync<JsonElement>();
        var kpis = overview.GetProperty("kpis");
        Assert.Equal(2, kpis.GetProperty("approvedCount").GetInt32());
        Assert.Equal(70m, kpis.GetProperty("receivedPix").GetDecimal());
        Assert.All(overview.GetProperty("charges").EnumerateArray(), c =>
        {
            Assert.Equal(JsonValueKind.Null, c.GetProperty("qrCode").ValueKind);
            Assert.Equal(JsonValueKind.Null, c.GetProperty("qrCodeBase64").ValueKind);
        });
    }

    [Fact]
    public async Task Admin_simulate_approve_and_manual_match_fee_upsert_work_and_block_non_admin()
    {
        var admin = CreateClient();
        var player = CreateClient();
        var (playerProfileId, groupId) = await RegisterAsync(player, "11977770021", "Diarista Sim");
        await LoginAdminAsync(admin);

        var createMatch = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = "Pelada Sim",
            startsAt = DateTimeOffset.UtcNow.AddDays(1),
            location = "Quadra Sul",
            playerLimit = 14
        });
        var matchId = (await createMatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationOpen" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        var pix = await (await player.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var chargeId = pix.GetProperty("id").GetGuid();

        // Jogador comum não pode chamar simulate-approve (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsync($"/api/payments/pix/{chargeId}/simulate-approve", null)).StatusCode);

        // Admin aprova via simulador
        var simResp = await admin.PostAsync($"/api/payments/pix/{chargeId}/simulate-approve", null);
        Assert.Equal(HttpStatusCode.OK, simResp.StatusCode);
        var simJson = await simResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(simJson.GetProperty("wasUpdated").GetBoolean());
        Assert.Equal("Paid", simJson.GetProperty("obligationStatus").GetString());

        // Admin pode marcar isenção/baixa manual em diária de outro jogador
        var (p2, _) = await RegisterAsync(CreateClient(), "11977770022", "Diarista Manual");
        var manualResp = await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{p2}", new { amount = 30m, status = "Paid", notes = "Dinheiro em mãos" });
        Assert.Equal(HttpStatusCode.OK, manualResp.StatusCode);
        var manualJson = await manualResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paid", manualJson.GetProperty("status").GetString());
        Assert.Equal("Manual", manualJson.GetProperty("paymentSource").GetString());
        Assert.Equal(30m, manualJson.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task MercadoPagoHttpClient_sends_bearer_and_idempotency_key_and_parses_response()
    {
        string? capturedAuth = null;
        string? capturedIdempotency = null;
        string? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedAuth = req.Headers.Authorization?.ToString();
            if (req.Headers.TryGetValues("X-Idempotency-Key", out var vals))
                capturedIdempotency = vals.FirstOrDefault();
            if (req.Content is not null)
                capturedBody = await req.Content.ReadAsStringAsync(ct);

            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new
                    {
                        id = 9876543210L,
                        status = "pending",
                        date_of_expiration = "2026-11-26T15:00:00.000Z",
                        point_of_interaction = new
                        {
                            transaction_data = new
                            {
                                qr_code = "000201-mp-real-qr",
                                qr_code_base64 = "iVBORw0KGgoAAAANSUhEUg=="
                            }
                        }
                    })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = 9876543210L,
                    status = "approved",
                    status_detail = "accredited",
                    external_reference = "membership:123",
                    transaction_amount = 150.00m,
                    date_approved = "2026-11-26T14:30:00.000Z"
                })
            };
        });

        var opts = new FutebolAtemporal.Infrastructure.MercadoPago.MercadoPagoOptions
        {
            AccessToken = "APP_USR_SECRET_TOKEN_TEST",
            WebhookSecret = "whsec_test",
            UseSandboxSimulator = false,
            ApiBaseUrl = "https://api.mercadopago.com"
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri(opts.ApiBaseUrl) };
        var client = new FutebolAtemporal.Infrastructure.MercadoPago.MercadoPagoHttpClient(
            http,
            opts,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FutebolAtemporal.Infrastructure.MercadoPago.MercadoPagoHttpClient>.Instance);

        var created = await client.CreatePixPaymentAsync(new MpCreatePixPaymentRequest(
            150m, "Mensalidade Futebol - 11/2026", "membership:123",
            DateTimeOffset.UtcNow.AddHours(1), "jogador@teste.com", "Carlos", "11111111-2222-4333-8444-555555555555"));

        Assert.Equal("Bearer APP_USR_SECRET_TOKEN_TEST", capturedAuth);
        Assert.Equal("11111111-2222-4333-8444-555555555555", capturedIdempotency);
        Assert.Contains("\"payment_method_id\":\"pix\"", capturedBody);
        Assert.Equal("9876543210", created.MpPaymentId);
        Assert.Equal("000201-mp-real-qr", created.QrCode);
        Assert.False(created.IsSimulated);

        var fetched = await client.GetPaymentAsync("9876543210");
        Assert.Equal("approved", fetched.Status);
        Assert.Equal("9876543210", fetched.MpPaymentId);
    }

    [Fact]
    public async Task Paid_via_pix_membership_and_match_fee_cannot_be_overwritten_manually_and_return_409()
    {
        var admin = CreateClient();
        var member = CreateClient();
        var casual = CreateClient();

        var (memberProfileId, groupId) = await RegisterAsync(member, "11977770031", "Mensalista Protegido");
        var (casualProfileId, _) = await RegisterAsync(casual, "11977770032", "Diarista Protegido");
        await LoginAdminAsync(admin);

        // 1. Mensalidade quitada via Pix não pode virar Exempt/Pending manualmente
        var addMem = await (await admin.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = memberProfileId })).Content.ReadFromJsonAsync<JsonElement>();
        var membershipId = addMem.GetProperty("id").GetGuid();
        var period = await (await admin.PostAsJsonAsync($"/api/groups/{groupId}/memberships/periods", new { year = 2026, month = 12 })).Content.ReadFromJsonAsync<JsonElement>();
        var periodId = period.GetProperty("id").GetGuid();

        var myPay = await (await member.GetAsync($"/api/groups/{groupId}/my-payments")).Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = myPay.GetProperty("memberships")[0].GetProperty("paymentId").GetGuid();

        var memPix = await (await member.PostAsync($"/api/payments/mensalidades/{paymentId}/pix", null)).Content.ReadFromJsonAsync<JsonElement>();
        var memChargeId = memPix.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/payments/pix/{memChargeId}/simulate-approve", null)).StatusCode);

        var overwriteExempt = await admin.PutAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/payments/{periodId}", new { amount = 150m, status = "Exempt", notes = "Tentativa indevida" });
        Assert.Equal(HttpStatusCode.Conflict, overwriteExempt.StatusCode);

        var overwritePending = await admin.PutAsJsonAsync($"/api/groups/{groupId}/memberships/{membershipId}/payments/{periodId}", new { amount = 150m, status = "Pending", notes = "Estorno indevido" });
        Assert.Equal(HttpStatusCode.Conflict, overwritePending.StatusCode);

        // Confirma no banco que permaneceu Paid + Pix
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.MembershipPayments.AsNoTracking().FirstAsync(p => p.Id == paymentId);
            Assert.Equal("Paid", saved.Status.ToString());
            Assert.Equal("Pix", saved.PaymentSource.ToString());
        }

        // 2. Diária quitada via Pix não pode ser sobrescrita manualmente
        var match = await (await admin.PostAsJsonAsync("/api/matches", new { groupId, title = "Pelada Lock", startsAt = DateTimeOffset.UtcNow.AddDays(3), location = "Arena Lock", playerLimit = 14 })).Content.ReadFromJsonAsync<JsonElement>();
        var matchId = match.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationOpen" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await casual.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        var feePix = await (await casual.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { })).Content.ReadFromJsonAsync<JsonElement>();
        var feeChargeId = feePix.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/payments/pix/{feeChargeId}/simulate-approve", null)).StatusCode);

        var overwriteFee = await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{casualProfileId}", new { amount = 25m, status = "Exempt", notes = "Tentativa indevida" });
        Assert.Equal(HttpStatusCode.Conflict, overwriteFee.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedFee = await db.MatchFeePayments.AsNoTracking().FirstAsync(f => f.MatchId == matchId && f.PlayerProfileId == casualProfileId);
            Assert.Equal("Paid", savedFee.Status.ToString());
            Assert.Equal("Pix", savedFee.PaymentSource.ToString());
        }
    }

    [Fact]
    public async Task Active_monthly_member_cannot_create_self_match_day_pix_and_returns_409()
    {
        var admin = CreateClient();
        var member = CreateClient();

        var (memberProfileId, groupId) = await RegisterAsync(member, "11977770041", "Mensalista Isento Diaria");
        await LoginAdminAsync(admin);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/groups/{groupId}/memberships", new { playerProfileId = memberProfileId })).StatusCode);

        var match = await (await admin.PostAsJsonAsync("/api/matches", new { groupId, title = "Pelada Mensalista", startsAt = DateTimeOffset.UtcNow.AddDays(4), location = "Arena Norte", playerLimit = 14 })).Content.ReadFromJsonAsync<JsonElement>();
        var matchId = match.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationOpen" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        var resp = await member.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.MatchFeePayments.AnyAsync(f => f.MatchId == matchId && f.PlayerProfileId == memberProfileId));
            Assert.False(await db.PixCharges.AnyAsync(c => c.MatchId == matchId && c.PlayerProfileId == memberProfileId));
        }
    }

    [Fact]
    public async Task Simulator_restarts_with_the_same_sqlite_database_and_keeps_charge_ids_unique()
    {
        const string playerPhone = "11977770061";
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"pix-restart-{Guid.NewGuid():N}.db");
        WebApplicationFactory<Program> StartHost() => _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={sqlitePath};Pooling=False"));

        try
        {
            Guid oldChargeId;
            Guid firstMatchId;
            string oldProviderId;
            await using (var firstHost = StartHost())
            {
                using var admin = firstHost.CreateClient();
                using var player = firstHost.CreateClient();
                var (_, groupId) = await RegisterAsync(player, playerPhone, "Diarista Reinício");
                await LoginAdminAsync(admin);
                firstMatchId = await CreateConfirmedMatchAsync(admin, player, groupId);
                var charge = await GenerateMatchDayPixAsync(player, firstMatchId);
                oldChargeId = charge.GetProperty("id").GetGuid();
                oldProviderId = charge.GetProperty("mpPaymentId").GetString()!;
            }

            await using (var restartedHost = StartHost())
            {
                using var admin = restartedHost.CreateClient();
                using var player = restartedHost.CreateClient();
                var groupId = await LoginAdminAsync(admin);
                Assert.Equal(HttpStatusCode.OK,
                    (await player.PostAsJsonAsync("/api/auth/login", new { phone = playerPhone })).StatusCode);

                var reused = await GenerateMatchDayPixAsync(player, firstMatchId);
                Assert.Equal(oldProviderId, reused.GetProperty("mpPaymentId").GetString());
                Assert.True(reused.GetProperty("reused").GetBoolean());

                var secondMatchId = await CreateConfirmedMatchAsync(admin, player, groupId);
                var newCharge = await GenerateMatchDayPixAsync(player, secondMatchId);
                Assert.NotEqual(oldProviderId, newCharge.GetProperty("mpPaymentId").GetString());

                var approval = await SimulateApprovalAsync(admin, oldChargeId);
                Assert.Equal("Paid", approval.GetProperty("obligationStatus").GetString());

                using var scope = restartedHost.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.Equal(2, await db.PixCharges.CountAsync());
            }
        }
        finally
        {
            File.Delete(sqlitePath);
        }
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(false, -10)]
    [InlineData(true, 10)]
    [InlineData(true, -10)]
    public async Task Changing_a_match_day_amount_cancels_old_pix_and_late_approval_cannot_settle(
        bool changeWhenGeneratingNewPix, int amountDelta)
    {
        using var admin = CreateClient();
        using var player = CreateClient();
        var (playerId, groupId) = await RegisterAsync(player, "11977770062", "Diarista Ajuste");
        await LoginAdminAsync(admin);
        var matchId = await CreateConfirmedMatchAsync(admin, player, groupId);
        var oldPix = await GenerateMatchDayPixAsync(player, matchId);
        var oldId = oldPix.GetProperty("id").GetGuid();
        var newAmount = oldPix.GetProperty("amount").GetDecimal() + amountDelta;

        JsonElement replacement;
        if (changeWhenGeneratingNewPix)
        {
            using var response = await admin.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix",
                new { playerProfileId = playerId, amount = newAmount });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            replacement = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        else
        {
            using var update = await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{playerId}",
                new { amount = newAmount, status = "Pending" });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Cancelled", (await db.PixCharges.AsNoTracking().SingleAsync(c => c.Id == oldId)).Status.ToString());
            Assert.Equal(newAmount, (await db.MatchFeePayments.AsNoTracking().SingleAsync(f => f.MatchId == matchId)).Amount);
            replacement = await GenerateMatchDayPixAsync(player, matchId);
        }

        Assert.NotEqual(oldId, replacement.GetProperty("id").GetGuid());
        Assert.False(replacement.GetProperty("reused").GetBoolean());
        Assert.Equal(newAmount, replacement.GetProperty("amount").GetDecimal());

        using var oldChargeResponse = await player.GetAsync($"/api/payments/pix/{oldId}");
        Assert.Equal(HttpStatusCode.OK, oldChargeResponse.StatusCode);
        var cancelled = await oldChargeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("qrCode").ValueKind);

        var lateApproval = await SimulateApprovalAsync(admin, oldId);
        Assert.False(lateApproval.GetProperty("wasUpdated").GetBoolean());
        Assert.Equal("Pending", lateApproval.GetProperty("obligationStatus").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fee = await db.MatchFeePayments.AsNoTracking().SingleAsync(f => f.MatchId == matchId);
            Assert.Equal("Pending", fee.Status.ToString());
            Assert.Equal(newAmount, fee.Amount);
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "PixApprovalAfterCancellation" && a.EntityId == oldId));
        }

        var approved = await SimulateApprovalAsync(admin, replacement.GetProperty("id").GetGuid());
        Assert.True(approved.GetProperty("wasUpdated").GetBoolean());
        Assert.Equal("Paid", approved.GetProperty("obligationStatus").GetString());
    }

    [Fact]
    public async Task Failed_pix_invalidation_leaves_match_day_amount_and_charge_unchanged()
    {
        using var admin = CreateClient();
        using var player = CreateClient();
        var (playerId, groupId) = await RegisterAsync(player, "11977770063", "Diarista Transação");
        await LoginAdminAsync(admin);
        var matchId = await CreateConfirmedMatchAsync(admin, player, groupId);
        var pix = await GenerateMatchDayPixAsync(player, matchId);
        var chargeId = pix.GetProperty("id").GetGuid();
        var originalAmount = pix.GetProperty("amount").GetDecimal();

        using (var setupScope = _factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER FailPixInvalidation BEFORE UPDATE OF Status ON PixCharges
                WHEN NEW.Status = 'Cancelled'
                BEGIN
                    SELECT RAISE(ABORT, 'Pix invalidation failed');
                END
                """);
        }

        using var update = await admin.PutAsJsonAsync($"/api/groups/{groupId}/matches/{matchId}/fees/{playerId}",
            new { amount = originalAmount + 10m, status = "Pending" });
        Assert.Equal(HttpStatusCode.InternalServerError, update.StatusCode);

        using var resultScope = _factory.Services.CreateScope();
        var resultDb = resultScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fee = await resultDb.MatchFeePayments.AsNoTracking().SingleAsync(f => f.MatchId == matchId);
        var charge = await resultDb.PixCharges.AsNoTracking().SingleAsync(p => p.Id == chargeId);
        Assert.Equal(originalAmount, fee.Amount);
        Assert.Equal("Pending", charge.Status.ToString());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public async Task Verify_rejects_missing_or_insufficient_transaction_amount(int? amount, bool raiseAmountInDatabase)
    {
        using var admin = CreateClient();
        using var player = CreateClient();
        var (_, groupId) = await RegisterAsync(player, "11977770051", "Diarista Conferência");
        await LoginAdminAsync(admin);
        var matchId = await CreateConfirmedMatchAsync(admin, player, groupId);
        var pix = await GenerateMatchDayPixAsync(player, matchId);
        var chargeId = pix.GetProperty("id").GetGuid();
        var mpId = pix.GetProperty("mpPaymentId").GetString()!;
        var originalAmount = pix.GetProperty("amount").GetDecimal();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (raiseAmountInDatabase)
            {
                // Representa cobranças legadas cujo valor da obrigação já mudou sem invalidar o Pix.
                var fee = await db.MatchFeePayments.SingleAsync(f => f.MatchId == matchId);
                fee.Amount = originalAmount + 10m;
                await db.SaveChangesAsync();
            }

            var store = scope.ServiceProvider.GetRequiredService<FutebolAtemporal.Infrastructure.MercadoPago.SimulatedMercadoPagoStore>();
            decimal? reportedAmount = raiseAmountInDatabase ? originalAmount : amount;
            store.SetAmountForTest(mpId, reportedAmount);
            scope.ServiceProvider.GetRequiredService<ISimulatedMercadoPagoControl>().SetStatus(mpId, "approved", DateTimeOffset.UtcNow);
        }

        using var response = await player.PostAsync($"/api/payments/pix/{chargeId}/verify", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verify = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(verify.GetProperty("wasUpdated").GetBoolean());
        Assert.Equal("Pending", verify.GetProperty("obligationStatus").GetString());

        using var resultScope = _factory.Services.CreateScope();
        var resultDb = resultScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Pending", (await resultDb.MatchFeePayments.AsNoTracking().SingleAsync(f => f.MatchId == matchId)).Status.ToString());
        Assert.Equal("Pending", (await resultDb.PixCharges.AsNoTracking().SingleAsync(p => p.Id == chargeId)).Status.ToString());
        Assert.True(await resultDb.AuditEntries.AnyAsync(a => a.Action == "PixAmountMismatch" && a.EntityId == chargeId));
    }

    private static async Task<Guid> CreateConfirmedMatchAsync(HttpClient admin, HttpClient player, Guid groupId)
    {
        using var response = await admin.PostAsJsonAsync("/api/matches",
            new { groupId, title = "Partida Pix", startsAt = DateTimeOffset.UtcNow.AddDays(5), location = "Arena Pix", playerLimit = 14 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = await response.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = match.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationOpen" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);
        return matchId;
    }

    private static async Task<JsonElement> GenerateMatchDayPixAsync(HttpClient player, Guid matchId)
    {
        using var response = await player.PostAsJsonAsync($"/api/payments/diarias/{matchId}/pix", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> SimulateApprovalAsync(HttpClient admin, Guid chargeId)
    {
        using var response = await admin.PostAsync($"/api/payments/pix/{chargeId}/simulate-approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _fn(request, cancellationToken);
    }
}
