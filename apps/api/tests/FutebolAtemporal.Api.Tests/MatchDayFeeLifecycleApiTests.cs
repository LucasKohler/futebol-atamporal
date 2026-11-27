using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Domain.Services;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>
/// Diária nasce no fechamento da rodada pelo admin, valor congelado, vence no fim do dia (fuso do grupo),
/// e a correção presente → ausente remove a não paga ou marca a paga para devolução.
/// </summary>
public class MatchDayFeeLifecycleApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public MatchDayFeeLifecycleApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> NewHost(string dbPath) => _factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Database:UseSqlite", "true");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={dbPath};Pooling=False");
        builder.UseSetting("Seed:AdminPhone", AdminPhone);
        builder.UseSetting("MercadoPago:UseSandboxSimulator", "true");
    });

    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Team(Guid Id, Guid Keeper, List<Guid> Line);
    private sealed record Round(WebApplicationFactory<Program> Host, string DbPath, HttpClient Admin, Guid GroupId, Guid MatchId, List<Team> Teams, Dictionary<Guid, HttpClient> Players) : IDisposable
    {
        public void Dispose()
        {
            Admin.Dispose();
            foreach (var player in Players.Values) player.Dispose();
            Host.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = DbPath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private async Task<Round> RoundAsync(string prefix)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"fees-{prefix}-{Guid.NewGuid():N}.db");
        var host = NewHost(dbPath);
        var admin = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        var loginJson = await Json(login);
        if (!login.IsSuccessStatusCode || !loginJson.TryGetProperty("memberships", out _))
            throw new InvalidOperationException($"Test admin login failed ({(int)login.StatusCode}): {loginJson}");
        var groupId = loginJson.GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Rodada", startsAt = DateTimeOffset.UtcNow.AddDays(1), location = "Arena", playerLimit = 20, openRegistrations = true
        });
        var matchId = (await Json(create)).GetProperty("id").GetGuid();
        await admin.PostAsync($"/api/matches/{matchId}/join", null);

        var players = new Dictionary<Guid, HttpClient>();
        foreach (var (name, i) in new[] { "G1", "G2", "G3", "L1", "L2", "L3", "L4", "L5" }.Select((n, i) => (n, i)))
        {
            var c = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone = $"1197{prefix}{i:D5}", displayName = name, password = TestAuth.Password });
            var id = (await Json(reg)).GetProperty("playerProfileId").GetGuid();
            await c.PostAsync($"/api/matches/{matchId}/join", null);
            if (name.StartsWith('G'))
                await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{id}/ratings", new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = 7m });
            players[id] = c;
        }

        var draw = await Json(await admin.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = prefix }));
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "InProgress" })).StatusCode);

        var teams = draw.GetProperty("teams").EnumerateArray().Select(t =>
        {
            var members = t.GetProperty("members").EnumerateArray().ToList();
            return new Team(
                t.GetProperty("id").GetGuid(),
                members.Single(m => m.GetProperty("role").GetString() == "Goalkeeper").GetProperty("playerProfileId").GetGuid(),
                members.Where(m => m.GetProperty("role").GetString() == "Line").Select(m => m.GetProperty("playerProfileId").GetGuid()).ToList());
        }).ToList();
        return new Round(host, dbPath, admin, groupId, matchId, teams, players);
    }

    private static object Sheet(Round r, IEnumerable<Guid> absent, string? reason = null)
    {
        var (red, black, green) = (r.Teams[0], r.Teams[1], r.Teams[2]);
        return new
        {
            presence = absent.Select(id => new { playerProfileId = id, present = false }).ToArray(),
            games = new[]
            {
                new { homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 1, awayScore = 0, penaltyWinnerTeamId = (Guid?)null },
                new { homeTeamId = black.Id, awayTeamId = green.Id, homeScore = 0, awayScore = 0, penaltyWinnerTeamId = (Guid?)green.Id }
            },
            goals = new[] { new { game = 1, playerProfileId = red.Line[0], ownGoal = false } },
            bestPlayerProfileId = (Guid?)null,
            bestGoalkeeperProfileId = (Guid?)null,
            notes = (string?)null,
            reason
        };
    }

    private static async Task<Dictionary<Guid, JsonElement>> FeesAsync(Round r)
    {
        var res = await r.Admin.GetAsync($"/api/groups/{r.GroupId}/matches/{r.MatchId}/fees");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await Json(res)).EnumerateArray().ToDictionary(f => f.GetProperty("playerProfileId").GetGuid());
    }

    private async Task SetFeeAsync(Round r, decimal fee)
        => Assert.Equal(HttpStatusCode.OK, (await r.Admin.PutAsJsonAsync($"/api/groups/{r.GroupId}/payments/settings",
            new { defaultMonthlyFee = 160m, defaultMatchDayFee = fee, pixExpirationMinutes = 30 })).StatusCode);

    [Fact]
    public async Task Closing_the_round_launches_frozen_fees_for_present_players_due_at_end_of_local_day()
    {
        using var r = await RoundAsync("61");
        await SetFeeAsync(r, 30m);
        var absent = r.Teams.SelectMany(t => t.Line).First(id => id != r.Teams[0].Line[0]);

        var before = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, [absent]))).StatusCode);
        await SetFeeAsync(r, 40m); // mudar o padrão depois não altera diária lançada

        using var scope = r.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fees = await db.MatchFeePayments.AsNoTracking().Where(f => f.MatchId == r.MatchId).ToListAsync();
        var group = await db.FootballGroups.AsNoTracking().FirstAsync(g => g.Id == r.GroupId);
        var tz = MatchDayFeePolicy.ResolveTimeZone(group.TimeZoneId);

        Assert.Equal(8, fees.Count); // 9 presentes (admin + 8) menos 1 ausente
        Assert.DoesNotContain(fees, f => f.PlayerProfileId == absent);
        Assert.All(fees, f =>
        {
            Assert.Equal(30m, f.Amount);
            Assert.Equal("Pending", f.Status.ToString());
            Assert.NotNull(f.DueAt);
            Assert.Equal(MatchDayFeePolicy.DueAt(before, tz), f.DueAt!.Value); // flutua só se o teste cruzar a meia-noite local
            Assert.Equal(TimeSpan.Zero, TimeZoneInfo.ConvertTime(f.DueAt.Value, tz).TimeOfDay);
        });

        var playerId = r.Teams.SelectMany(t => t.Line).First(id => id != absent && r.Players.ContainsKey(id));
        var mine = await Json(await r.Players[playerId].GetAsync($"/api/groups/{r.GroupId}/my-payments"));
        var day = mine.GetProperty("matchDays").EnumerateArray().Single(d => d.GetProperty("matchId").GetGuid() == r.MatchId);
        Assert.Equal("Pending", day.GetProperty("status").GetString());
        Assert.Equal(30m, day.GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.String, day.GetProperty("dueAt").ValueKind);

        // Depois do vencimento a situação exibida vira atrasada, sem job.
        var fee = await db.MatchFeePayments.FirstAsync(f => f.MatchId == r.MatchId && f.PlayerProfileId == playerId);
        fee.DueAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        mine = await Json(await r.Players[playerId].GetAsync($"/api/groups/{r.GroupId}/my-payments"));
        day = mine.GetProperty("matchDays").EnumerateArray().Single(d => d.GetProperty("matchId").GetGuid() == r.MatchId);
        Assert.Equal("Overdue", day.GetProperty("status").GetString());
        Assert.Equal("Overdue", (await FeesAsync(r))[playerId].GetProperty("status").GetString());

        // Atrasada continua pagável via Pix (simulador, sem chamada real).
        var pix = await r.Players[playerId].PostAsJsonAsync($"/api/payments/diarias/{r.MatchId}/pix", new { });
        Assert.Equal(HttpStatusCode.OK, pix.StatusCode);
        Assert.Equal(30m, (await Json(pix)).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Correcting_presence_to_absent_removes_unpaid_fee_and_requests_refund_of_paid_fee()
    {
        using var r = await RoundAsync("62");
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, []))).StatusCode);

        // Jogadores de linha com cliente próprio (o admin pode cair em qualquer time) e que não marcaram gol.
        var scorer = r.Teams[0].Line[0];
        var candidates = r.Teams.SelectMany(t => t.Line).Where(id => id != scorer && r.Players.ContainsKey(id)).ToList();
        var (unpaid, paid, untouched) = (candidates[0], candidates[1], candidates[2]);

        // Unpaid tem um Pix em aberto que deve ser invalidado.
        var pixRes = await r.Players[unpaid].PostAsJsonAsync($"/api/payments/diarias/{r.MatchId}/pix", new { });
        Assert.Equal(HttpStatusCode.OK, pixRes.StatusCode);
        var unpaidChargeId = (await Json(pixRes)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PutAsJsonAsync($"/api/groups/{r.GroupId}/matches/{r.MatchId}/fees/{paid}",
            new { amount = 25m, status = "Paid", notes = "Dinheiro" })).StatusCode);

        var correct = await r.Admin.PutAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, [unpaid, paid], reason: "Dois faltaram"));
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);

        var fees = await FeesAsync(r);
        Assert.Equal("Cancelled", fees[unpaid].GetProperty("status").GetString());
        Assert.Equal("RefundPending", fees[paid].GetProperty("status").GetString());
        Assert.Equal("Manual", fees[paid].GetProperty("paymentSource").GetString());
        Assert.Equal("Pending", fees[untouched].GetProperty("status").GetString());

        using (var scope = r.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Cancelled", (await db.PixCharges.AsNoTracking().FirstAsync(c => c.Id == unpaidChargeId)).Status.ToString());
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "match_fee.removed_absent"));
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "match_fee.refund_requested"));
        }

        // Em devolução: jogador não gera Pix e admin não volta a cobrar.
        Assert.Equal(HttpStatusCode.Conflict, (await r.Players[paid].PostAsJsonAsync($"/api/payments/diarias/{r.MatchId}/pix", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await r.Admin.PutAsJsonAsync($"/api/groups/{r.GroupId}/matches/{r.MatchId}/fees/{paid}",
            new { amount = 25m, status = "Pending", notes = (string?)null })).StatusCode);
        // RefundPending/Refunded não podem ser marcados à mão em diária comum.
        Assert.Equal(HttpStatusCode.Conflict, (await r.Admin.PutAsJsonAsync($"/api/groups/{r.GroupId}/matches/{r.MatchId}/fees/{untouched}",
            new { amount = 25m, status = "Refunded", notes = (string?)null })).StatusCode);

        // Admin confirma a devolução; origem do pagamento é preservada.
        var refunded = await r.Admin.PutAsJsonAsync($"/api/groups/{r.GroupId}/matches/{r.MatchId}/fees/{paid}",
            new { amount = 25m, status = "Refunded", notes = "Devolvido em dinheiro" });
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        var dto = await Json(refunded);
        Assert.Equal("Refunded", dto.GetProperty("status").GetString());
        Assert.Equal("Manual", dto.GetProperty("paymentSource").GetString());

        // Nova correção sem mudança de presença não mexe em nada.
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PutAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, [unpaid, paid], reason: "Ajuste de nota"))).StatusCode);
        fees = await FeesAsync(r);
        Assert.Equal("Refunded", fees[paid].GetProperty("status").GetString());
        Assert.Equal("Cancelled", fees[unpaid].GetProperty("status").GetString());
    }

    [Fact]
    public async Task No_charge_or_checkout_exists_before_the_round_is_closed()
    {
        using var r = await RoundAsync("63");
        var playerId = r.Players.Keys.First();
        var player = r.Players[playerId];

        var own = await player.PostAsJsonAsync($"/api/payments/diarias/{r.MatchId}/pix", new { });
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Contains("match_fee_not_open", await own.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await r.Admin.PostAsJsonAsync($"/api/payments/diarias/{r.MatchId}/pix", new { playerProfileId = playerId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await r.Admin.PutAsJsonAsync($"/api/groups/{r.GroupId}/matches/{r.MatchId}/fees/{playerId}",
            new { amount = 25m, status = "Paid", notes = "Adiantado" })).StatusCode);

        var mine = await Json(await player.GetAsync($"/api/groups/{r.GroupId}/my-payments"));
        var day = mine.GetProperty("matchDays").EnumerateArray().Single(d => d.GetProperty("matchId").GetGuid() == r.MatchId);
        Assert.Equal("NotCharged", day.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, day.GetProperty("dueAt").ValueKind);
        Assert.Equal("NotCharged", (await FeesAsync(r))[playerId].GetProperty("status").GetString());

        using (var scope = r.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.MatchFeePayments.AnyAsync(f => f.MatchId == r.MatchId));
            Assert.False(await db.PixCharges.AnyAsync(c => c.MatchId == r.MatchId));
        }

        // Depois do fechamento a cobrança abre.
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, []))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.PostAsJsonAsync($"/api/payments/diarias/{r.MatchId}/pix", new { })).StatusCode);
    }
}
