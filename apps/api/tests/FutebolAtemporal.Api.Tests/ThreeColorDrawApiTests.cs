using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

/// <summary>Tarefas 10/11: sorteio de três cores, privacidade, troca, revalidação e preservação do elenco.</summary>
public class ThreeColorDrawApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public ThreeColorDrawApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=draw3-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private sealed record Scenario(HttpClient Admin, Guid GroupId, Guid MatchId, Dictionary<string, (HttpClient Client, Guid ProfileId)> Players);

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Admin + 8 jogadores fictícios confirmados; G1..G3 aptos ao gol com notas 9, 8 e 7.</summary>
    private async Task<Scenario> ScenarioAsync(string prefix)
    {
        var admin = NewClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone });
        var groupId = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Três cores", startsAt = DateTimeOffset.UtcNow.AddDays(2), location = "Arena", playerLimit = 20, openRegistrations = true
        });
        var matchId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);

        var players = new Dictionary<string, (HttpClient, Guid)>();
        var names = new[] { "G1", "G2", "G3", "L1", "L2", "L3", "L4", "L5" };
        for (var i = 0; i < names.Length; i++)
        {
            var c = NewClient();
            var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone = $"1198{prefix}{i:D5}", displayName = names[i] });
            Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
            var id = (await reg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("playerProfileId").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"/api/matches/{matchId}/join", null)).StatusCode);
            players[names[i]] = (c, id);
        }

        decimal[] gk = [9m, 8m, 7m];
        for (var i = 0; i < 3; i++)
        {
            var r = await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{players[$"G{i + 1}"].Item2}/ratings",
                new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = gk[i] });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        return new Scenario(admin, groupId, matchId, players);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> DrawAsync(Scenario s, string seed)
    {
        var res = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/draw", new { seed });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await Json(res);
    }

    /// <summary>Fecha a rodada pela súmula de três cores com um confronto 0 x 0 entre as duas primeiras cores.</summary>
    private static Task<HttpResponseMessage> CloseScorelessAsync(Scenario s, JsonElement draw, object[] presence)
        => s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/result/round", new
        {
            presence,
            games = new[]
            {
                new
                {
                    homeTeamId = draw.GetProperty("teams")[0].GetProperty("id").GetGuid(),
                    awayTeamId = draw.GetProperty("teams")[1].GetProperty("id").GetGuid(),
                    homeScore = 0,
                    awayScore = 0,
                    penaltyWinnerTeamId = draw.GetProperty("teams")[0].GetProperty("id").GetGuid()
                }
            },
            goals = Array.Empty<object>()
        });

    private static string Composition(JsonElement draw)
        => string.Join(" / ", draw.GetProperty("teams").EnumerateArray().Select(t =>
            t.GetProperty("color").GetString() + ":" + string.Join(",", t.GetProperty("members").EnumerateArray()
                .Select(m => m.GetProperty("displayName").GetString() + (m.GetProperty("role").GetString() == "Goalkeeper" ? "(G)" : "")))));

    [Fact]
    public async Task Admin_draws_three_colors_with_one_keeper_each_and_seed_reproduces()
    {
        var s = await ScenarioAsync("71");
        var draw = await DrawAsync(s, "rodada-1");

        var teams = draw.GetProperty("teams").EnumerateArray().ToList();
        Assert.Equal(["Vermelho", "Preto", "Verde"], teams.Select(t => t.GetProperty("color").GetString()));
        Assert.All(teams, t => Assert.Single(t.GetProperty("members").EnumerateArray(), m => m.GetProperty("role").GetString() == "Goalkeeper"));
        Assert.All(teams, t => Assert.Equal(3, t.GetProperty("members").GetArrayLength()));
        Assert.Equal("3cores-v1", draw.GetProperty("algorithmVersion").GetString());
        Assert.Equal(3, draw.GetProperty("goalkeepersAssigned").GetInt32());
        Assert.Equal(2m, draw.GetProperty("goalkeeperSpread").GetDecimal());
        Assert.Equal(JsonValueKind.Number, draw.GetProperty("lineAverageSpread").ValueKind);
        Assert.True(draw.GetProperty("canConfirm").GetBoolean());

        var again = await DrawAsync(s, "rodada-1");
        Assert.Equal(Composition(draw), Composition(again));

        var match = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}"));
        Assert.Equal("RegistrationClosed", match.GetProperty("match").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Players_see_only_confirmed_draw_without_ratings_and_history_hides_rating()
    {
        var s = await ScenarioAsync("72");
        var player = s.Players["L1"].Client;
        var draw = await DrawAsync(s, "privacidade");

        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/api/matches/{s.MatchId}/draw")).StatusCode);

        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null)).StatusCode);

        var seen = await Json(await player.GetAsync($"/api/matches/{s.MatchId}/draw"));
        Assert.Equal("Confirmed", seen.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("lineAverageSpread").ValueKind);
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("goalkeeperSpread").ValueKind);
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("strengthDelta").ValueKind);
        Assert.Empty(seen.GetProperty("warnings").EnumerateArray());
        Assert.All(seen.GetProperty("teams").EnumerateArray(), t =>
        {
            Assert.Equal(JsonValueKind.Null, t.GetProperty("lineSum").ValueKind);
            Assert.Equal(JsonValueKind.Null, t.GetProperty("totalSkill").ValueKind);
            Assert.Equal(JsonValueKind.Null, t.GetProperty("goalkeeperRating").ValueKind);
            Assert.All(t.GetProperty("members").EnumerateArray(), m =>
            {
                Assert.Equal(JsonValueKind.Null, m.GetProperty("skillRating").ValueKind);
                Assert.Equal(JsonValueKind.Null, m.GetProperty("goalkeeperRating").ValueKind);
            });
        });

        var playerHistory = await Json(await player.GetAsync($"/api/players/{s.Players["L1"].ProfileId}/history"));
        Assert.Equal(JsonValueKind.Null, playerHistory.GetProperty("skillRating").ValueKind);
        var adminHistory = await Json(await s.Admin.GetAsync($"/api/players/{s.Players["L1"].ProfileId}/history"));
        Assert.Equal(JsonValueKind.Number, adminHistory.GetProperty("skillRating").ValueKind);
    }

    [Fact]
    public async Task Swap_updates_metrics_invalidates_confirmation_and_checks_goalkeeper_aptitude()
    {
        var s = await ScenarioAsync("73");
        var draw = await DrawAsync(s, "troca");
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null)).StatusCode);
        // Confirmação repetida é idempotente.
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null)).StatusCode);

        var teams = draw.GetProperty("teams").EnumerateArray().ToList();
        Guid LineOf(int team) => teams[team].GetProperty("members").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Line").GetProperty("playerProfileId").GetGuid();
        Guid KeeperOf(int team) => teams[team].GetProperty("members").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Goalkeeper").GetProperty("playerProfileId").GetGuid();

        var (a, b) = (LineOf(0), LineOf(1));
        var swap = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/draw/{drawId}/swap", new { playerA = a, playerB = b });
        Assert.Equal(HttpStatusCode.OK, swap.StatusCode);
        var swapped = await Json(swap);
        Assert.Equal("Draft", swapped.GetProperty("status").GetString());
        Assert.Contains(swapped.GetProperty("teams")[0].GetProperty("members").EnumerateArray(), m => m.GetProperty("playerProfileId").GetGuid() == b);
        var match = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}"));
        Assert.Equal("RegistrationClosed", match.GetProperty("match").GetProperty("status").GetString());

        // Jogador de linha não apto não pode assumir o gol (os três aptos G1..G3 estão todos no gol).
        var badSwap = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/draw/{drawId}/swap", new { playerA = KeeperOf(0), playerB = LineOf(2) });
        Assert.Equal(HttpStatusCode.BadRequest, badSwap.StatusCode);
        Assert.Contains("apto para o gol", (await Json(badSwap)).GetProperty("detail").GetString());

        // Escopo de rota: sorteio de outra partida não é aceito.
        var wrong = await s.Admin.PostAsync($"/api/matches/{Guid.NewGuid()}/draw/{drawId}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        // Jogador comum não troca.
        var asPlayer = await s.Players["L2"].Client.PostAsJsonAsync($"/api/matches/{s.MatchId}/draw/{drawId}/swap", new { playerA = a, playerB = b });
        Assert.Equal(HttpStatusCode.Forbidden, asPlayer.StatusCode);
    }

    [Fact]
    public async Task Roster_change_after_confirmed_draw_requires_new_review()
    {
        var s = await ScenarioAsync("74");
        var draw = await DrawAsync(s, "revalidar");
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await s.Players["L5"].Client.PostAsync($"/api/matches/{s.MatchId}/leave", null)).StatusCode);

        var match = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}"));
        Assert.Equal("RegistrationClosed", match.GetProperty("match").GetProperty("status").GetString());
        var current = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}/draw"));
        Assert.Equal("Draft", current.GetProperty("status").GetString());
        Assert.True(current.GetProperty("rosterChanged").GetBoolean());
        Assert.False(current.GetProperty("canConfirm").GetBoolean());
        Assert.Contains("L5", current.GetProperty("rosterChanges")[0].GetString());

        var confirm = await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);

        // Jogador deixa de ver a escalação que não vale mais.
        Assert.Equal(HttpStatusCode.NotFound, (await s.Players["L1"].Client.GetAsync($"/api/matches/{s.MatchId}/draw")).StatusCode);

        var redraw = await DrawAsync(s, "revalidar-2");
        Assert.False(redraw.GetProperty("rosterChanged").GetBoolean());
        Assert.True(redraw.GetProperty("canConfirm").GetBoolean());
        Assert.Equal(8, redraw.GetProperty("teams").EnumerateArray().Sum(t => t.GetProperty("members").GetArrayLength()));
    }

    [Fact]
    public async Task Started_match_freezes_lineup_leave_remove_and_limit_are_refused()
    {
        // Regressão do QA (corte 2): sorteio confirmado + InProgress + desistência rebaixava o sorteio e travava a rodada.
        var s = await ScenarioAsync("76");
        var draw = await DrawAsync(s, "em-andamento");
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null)).StatusCode);
        var start = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/transition", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var leave = await s.Players["L5"].Client.PostAsync($"/api/matches/{s.MatchId}/leave", null);
        Assert.Equal(HttpStatusCode.BadRequest, leave.StatusCode);
        Assert.Contains("já começou", (await Json(leave)).GetProperty("detail").GetString());

        var remove = await s.Admin.PostAsync($"/api/matches/{s.MatchId}/registrations/{s.Players["L4"].ProfileId}/remove", null);
        Assert.Equal(HttpStatusCode.BadRequest, remove.StatusCode);

        var body = new { title = "Três cores", startsAt = DateTimeOffset.UtcNow.AddDays(2), location = "Arena", playerLimit = 25, notes = (string?)null };
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.PutAsJsonAsync($"/api/matches/{s.MatchId}", body)).StatusCode);
        // Editar outros campos com o mesmo limite continua permitido.
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PutAsJsonAsync($"/api/matches/{s.MatchId}", body with { playerLimit = 20, notes = "Chuva" })).StatusCode);

        // Elenco e status preservados; jogador continua vendo a escalação confirmada.
        var match = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}"));
        Assert.Equal("InProgress", match.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal(9, match.GetProperty("match").GetProperty("confirmedCount").GetInt32());
        var seen = await s.Players["L5"].Client.GetAsync($"/api/matches/{s.MatchId}/draw");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        var kept = await Json(seen);
        Assert.Equal("Confirmed", kept.GetProperty("status").GetString());
        Assert.Equal(Composition(draw), Composition(kept));
        var adminView = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}/draw"));
        Assert.False(adminView.GetProperty("rosterChanged").GetBoolean());
        Assert.True(adminView.GetProperty("isLocked").GetBoolean());

        // A rodada segue até o fechamento, com a ausência registrada na presença.
        var close = await CloseScorelessAsync(s, draw, [new { playerProfileId = s.Players["L5"].ProfileId, present = false }]);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var history = await Json(await s.Players["L5"].Client.GetAsync($"/api/players/{s.Players["L5"].ProfileId}/history"));
        Assert.Equal("Absent", history.GetProperty("history")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Completed_match_preserves_lineup_against_new_draw_and_swap()
    {
        var s = await ScenarioAsync("75");
        var draw = await DrawAsync(s, "fechada");
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/matches/{s.MatchId}/draw/{drawId}/confirm", null)).StatusCode);

        var close = await CloseScorelessAsync(s, draw, []);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        var teams = draw.GetProperty("teams").EnumerateArray().ToList();
        var a = teams[0].GetProperty("members")[1].GetProperty("playerProfileId").GetGuid();
        var b = teams[1].GetProperty("members")[1].GetProperty("playerProfileId").GetGuid();
        var swap = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/draw/{drawId}/swap", new { playerA = a, playerB = b });
        Assert.Equal(HttpStatusCode.BadRequest, swap.StatusCode);
        Assert.Contains("preservado", (await Json(swap)).GetProperty("detail").GetString());

        var redraw = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/draw", new { seed = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, redraw.StatusCode);

        var kept = await Json(await s.Admin.GetAsync($"/api/matches/{s.MatchId}/draw"));
        Assert.Equal("Confirmed", kept.GetProperty("status").GetString());
        Assert.True(kept.GetProperty("isLocked").GetBoolean());
        Assert.Equal(Composition(draw), Composition(kept));
    }
}
