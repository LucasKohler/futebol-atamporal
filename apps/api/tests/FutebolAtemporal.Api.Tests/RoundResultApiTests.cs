using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FutebolAtemporal.Api.Tests;

/// <summary>Tarefas 12/13: súmula de três cores, prêmios, correção auditada, artilharia mensal/anual e legado A/B.</summary>
public class RoundResultApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public RoundResultApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=round-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Team(Guid Id, string Color, Guid Keeper, List<Guid> Line);
    private sealed record Round(HttpClient Admin, HttpClient Player, Guid GroupId, Guid MatchId, List<Team> Teams, Dictionary<Guid, string> Names);

    /// <summary>Admin + 8 fictícios (G1..G3 aptos ao gol), sorteio confirmado e partida em andamento.</summary>
    private async Task<Round> RoundAsync(string prefix, DateTimeOffset? startsAt = null)
    {
        var admin = NewClient();
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        var groupId = (await Json(login)).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Rodada", startsAt = startsAt ?? DateTimeOffset.UtcNow.AddDays(1), location = "Arena", playerLimit = 20, openRegistrations = true
        });
        var matchId = (await Json(create)).GetProperty("id").GetGuid();
        await admin.PostAsync($"/api/matches/{matchId}/join", null);

        HttpClient? player = null;
        foreach (var (name, i) in new[] { "G1", "G2", "G3", "L1", "L2", "L3", "L4", "L5" }.Select((n, i) => (n, i)))
        {
            var c = NewClient();
            var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone = $"1198{prefix}{i:D5}", displayName = name, password = TestAuth.Password });
            var id = (await Json(reg)).GetProperty("playerProfileId").GetGuid();
            await c.PostAsync($"/api/matches/{matchId}/join", null);
            if (name.StartsWith('G'))
                await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{id}/ratings", new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = 7m });
            player ??= c;
        }

        var draw = await Json(await admin.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = prefix }));
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "InProgress" })).StatusCode);

        var names = new Dictionary<Guid, string>();
        var teams = draw.GetProperty("teams").EnumerateArray().Select(t =>
        {
            var members = t.GetProperty("members").EnumerateArray().ToList();
            foreach (var m in members) names[m.GetProperty("playerProfileId").GetGuid()] = m.GetProperty("displayName").GetString()!;
            return new Team(
                t.GetProperty("id").GetGuid(),
                t.GetProperty("color").GetString()!,
                members.Single(m => m.GetProperty("role").GetString() == "Goalkeeper").GetProperty("playerProfileId").GetGuid(),
                members.Where(m => m.GetProperty("role").GetString() == "Line").Select(m => m.GetProperty("playerProfileId").GetGuid()).ToList());
        }).ToList();
        return new Round(admin, player!, groupId, matchId, teams, names);
    }

    /// <summary>Vermelho 2x1 Preto · Preto 0x0 Verde · Verde 1x0 Vermelho (gol contra de um vermelho).</summary>
    private static object Sheet(Round r, Guid? bestPlayer = null, Guid? bestKeeper = null, string? reason = null, Guid? scorerOverride = null)
    {
        var (red, black, green) = (r.Teams[0], r.Teams[1], r.Teams[2]);
        return new
        {
            presence = Array.Empty<object>(),
            games = new[]
            {
                new { homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 2, awayScore = 1, penaltyWinnerTeamId = (Guid?)null },
                new { homeTeamId = black.Id, awayTeamId = green.Id, homeScore = 0, awayScore = 0, penaltyWinnerTeamId = (Guid?)green.Id },
                new { homeTeamId = green.Id, awayTeamId = red.Id, homeScore = 1, awayScore = 0, penaltyWinnerTeamId = (Guid?)null }
            },
            goals = new[]
            {
                new { game = 1, playerProfileId = scorerOverride ?? red.Line[0], ownGoal = false },
                new { game = 1, playerProfileId = scorerOverride ?? red.Line[0], ownGoal = false },
                new { game = 1, playerProfileId = black.Line[0], ownGoal = false },
                new { game = 3, playerProfileId = red.Line[^1], ownGoal = true }
            },
            bestPlayerProfileId = bestPlayer,
            bestGoalkeeperProfileId = bestKeeper,
            notes = "Rodada fictícia",
            reason
        };
    }

    private static async Task<JsonElement> Season(HttpClient c, Guid groupId, int year, int? month)
        => await Json(await c.GetAsync($"/api/groups/{groupId}/season?year={year}" + (month is null ? "" : $"&month={month}")));

    [Fact]
    public async Task Close_three_color_round_with_goals_own_goal_and_awards()
    {
        var r = await RoundAsync("51");
        var (red, black, green) = (r.Teams[0], r.Teams[1], r.Teams[2]);
        var close = await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, red.Line[0], black.Keeper));
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var round = await Json(close);

        Assert.False(round.GetProperty("isLegacy").GetBoolean());
        Assert.Equal(1, round.GetProperty("revision").GetInt32());
        var teams = round.GetProperty("teams").EnumerateArray().ToDictionary(t => t.GetProperty("color").GetString()!);
        Assert.Equal((1, 0, 1, 1), (teams["Vermelho"].GetProperty("wins").GetInt32(), teams["Vermelho"].GetProperty("draws").GetInt32(),
            teams["Vermelho"].GetProperty("losses").GetInt32(), teams["Vermelho"].GetProperty("points").GetInt32()));
        Assert.Equal(2, teams["Verde"].GetProperty("points").GetInt32()); // 1 vitória normal + 1 nos pênaltis (Opção A)
        Assert.True(teams["Verde"].GetProperty("isRoundWinner").GetBoolean());
        Assert.False(teams["Vermelho"].GetProperty("isRoundWinner").GetBoolean());

        var participants = round.GetProperty("participants").EnumerateArray().ToDictionary(p => p.GetProperty("playerProfileId").GetGuid());
        Assert.Equal(2, participants[red.Line[0]].GetProperty("goals").GetInt32());
        Assert.Equal(0, participants[red.Line[^1]].GetProperty("goals").GetInt32());
        Assert.Equal(1, participants[red.Line[^1]].GetProperty("ownGoals").GetInt32());
        Assert.True(participants[black.Keeper].GetProperty("playedGoalkeeper").GetBoolean());
        Assert.Equal(r.Names[black.Keeper], round.GetProperty("bestGoalkeeperName").GetString());

        var match = await Json(await r.Admin.GetAsync($"/api/matches/{r.MatchId}"));
        Assert.Equal("Completed", match.GetProperty("match").GetProperty("status").GetString());

        var year = DateTime.UtcNow.AddHours(-3).AddDays(1).Year;
        var season = await Season(r.Player, r.GroupId, year, null);
        var leader = season.GetProperty("yearScorers")[0];
        Assert.Equal(red.Line[0], leader.GetProperty("playerProfileId").GetGuid());
        Assert.Equal(2, leader.GetProperty("goals").GetInt32());
        Assert.DoesNotContain(season.GetProperty("yearScorers").EnumerateArray(), s => s.GetProperty("playerProfileId").GetGuid() == red.Line[^1]);
        var verde = season.GetProperty("colors").EnumerateArray().Single(c => c.GetProperty("color").GetString() == "Verde");
        Assert.Equal(1, verde.GetProperty("roundsWon").GetInt32());
        var keeperRow = season.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("playerProfileId").GetGuid() == black.Keeper);
        Assert.Equal(1, keeperRow.GetProperty("bestGoalkeeper").GetInt32());

        // Fechar de novo não é permitido: mudanças só pela correção.
        Assert.Equal(HttpStatusCode.BadRequest, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r))).StatusCode);
    }

    [Fact]
    public async Task Invalid_sheets_are_refused_and_nothing_is_saved()
    {
        var r = await RoundAsync("52");
        var (red, black) = (r.Teams[0], r.Teams[1]);

        // Gols que não fecham o placar.
        var bad = await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", new
        {
            games = new[] { new { homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 3, awayScore = 0 } },
            goals = new[] { new { game = 1, playerProfileId = red.Line[0], ownGoal = false } },
            presence = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("não fecham o placar", (await Json(bad)).GetProperty("detail").GetString());

        // Melhor Goleiro que não atuou no gol.
        var keeper = await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, bestKeeper: red.Line[1]));
        Assert.Equal(HttpStatusCode.BadRequest, keeper.StatusCode);
        Assert.Contains("atuado no gol", (await Json(keeper)).GetProperty("detail").GetString());

        // Gol de jogador marcado ausente.
        var absent = await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", new
        {
            games = new[] { new { homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 1, awayScore = 0 } },
            goals = new[] { new { game = 1, playerProfileId = red.Line[0], ownGoal = false } },
            presence = new[] { new { playerProfileId = red.Line[0], present = false } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);

        // Jogador comum não fecha.
        Assert.Equal(HttpStatusCode.Forbidden, (await r.Player.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r))).StatusCode);

        var match = await Json(await r.Admin.GetAsync($"/api/matches/{r.MatchId}"));
        Assert.Equal("InProgress", match.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await r.Admin.GetAsync($"/api/matches/{r.MatchId}/result/round")).StatusCode);
    }

    [Fact]
    public async Task Correction_requires_reason_keeps_previous_revision_and_never_double_counts()
    {
        var r = await RoundAsync("53");
        var (red, black) = (r.Teams[0], r.Teams[1]);
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, red.Line[0]))).StatusCode);

        var noReason = await r.Admin.PutAsJsonAsync($"/api/matches/{r.MatchId}/result/round", Sheet(r, black.Line[0], scorerOverride: red.Line[1]));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Contains("motivo", (await Json(noReason)).GetProperty("detail").GetString());

        // Corrige: os 2 gols do 1º jogo eram de outro vermelho; Melhor Jogador muda.
        var fix = await r.Admin.PutAsJsonAsync($"/api/matches/{r.MatchId}/result/round",
            Sheet(r, black.Line[0], reason: "Autor dos gols lançado errado", scorerOverride: red.Line[1]));
        Assert.Equal(HttpStatusCode.OK, fix.StatusCode);
        var current = await Json(fix);
        Assert.Equal(2, current.GetProperty("revision").GetInt32());
        var revisions = current.GetProperty("revisions").EnumerateArray().ToList();
        Assert.Equal(2, revisions.Count);
        Assert.Equal("Autor dos gols lançado errado", revisions[1].GetProperty("reason").GetString());

        var year = DateTime.UtcNow.AddHours(-3).AddDays(1).Year;
        var season = await Season(r.Admin, r.GroupId, year, null);
        var scorers = season.GetProperty("yearScorers").EnumerateArray().ToList();
        Assert.Equal(red.Line[1], scorers[0].GetProperty("playerProfileId").GetGuid());
        Assert.Equal(2, scorers[0].GetProperty("goals").GetInt32());
        Assert.DoesNotContain(scorers, s => s.GetProperty("playerProfileId").GetGuid() == red.Line[0]);
        Assert.Equal(3, scorers.Sum(s => s.GetProperty("goals").GetInt32())); // 3 gols normais, uma vez só
        var players = season.GetProperty("players").EnumerateArray().ToDictionary(p => p.GetProperty("playerProfileId").GetGuid());
        Assert.Equal(0, players[red.Line[0]].GetProperty("bestPlayer").GetInt32());
        Assert.Equal(1, players[black.Line[0]].GetProperty("bestPlayer").GetInt32());

        // Ranking antigo também reflete só a revisão vigente.
        var ranks = await Json(await r.Admin.GetAsync($"/api/groups/{r.GroupId}/rankings?sortBy=motm"));
        Assert.Equal(1, ranks.EnumerateArray().Sum(x => x.GetProperty("manOfTheMatch").GetInt32()));

        // Revisão anterior consultável pelo admin; jogador só vê a vigente.
        var old = await Json(await r.Admin.GetAsync($"/api/matches/{r.MatchId}/result/round?revision=1"));
        Assert.False(old.GetProperty("isCurrentRevision").GetBoolean());
        Assert.Equal(red.Line[0], old.GetProperty("bestPlayerProfileId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await r.Player.GetAsync($"/api/matches/{r.MatchId}/result/round?revision=1")).StatusCode);
        var playerView = await Json(await r.Player.GetAsync($"/api/matches/{r.MatchId}/result/round"));
        Assert.Empty(playerView.GetProperty("revisions").EnumerateArray());
    }

    [Fact]
    public async Task Scorers_use_local_game_date_and_share_leadership_on_ties()
    {
        // 01/12/2026 01:30 UTC = 30/11/2026 22:30 em São Paulo: conta para novembro.
        var r = await RoundAsync("54", new DateTimeOffset(2026, 12, 1, 1, 30, 0, TimeSpan.Zero));
        var (red, black) = (r.Teams[0], r.Teams[1]);
        var sheet = new
        {
            presence = Array.Empty<object>(),
            games = new[] { new { homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 1, awayScore = 1, penaltyWinnerTeamId = red.Id } },
            goals = new[]
            {
                new { game = 1, playerProfileId = red.Line[0], ownGoal = false },
                new { game = 1, playerProfileId = black.Line[0], ownGoal = false }
            }
        };
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", sheet)).StatusCode);

        var nov = await Season(r.Player, r.GroupId, 2026, 11);
        var monthScorers = nov.GetProperty("monthScorers").EnumerateArray().ToList();
        Assert.Equal(2, monthScorers.Count);
        Assert.All(monthScorers, s =>
        {
            Assert.Equal(1, s.GetProperty("position").GetInt32());
            Assert.True(s.GetProperty("isLeader").GetBoolean());
        });
        Assert.Equal("2026-11-30", nov.GetProperty("rounds")[0].GetProperty("localDate").GetString());
        Assert.Single(nov.GetProperty("rounds")[0].GetProperty("winnerColors").EnumerateArray()); // Vermelho venceu nos pênaltis

        var dec = await Season(r.Player, r.GroupId, 2026, 12);
        Assert.Empty(dec.GetProperty("monthScorers").EnumerateArray());
        Assert.Equal(2, dec.GetProperty("yearScorers").GetArrayLength());
    }

    [Fact]
    public async Task Year_boundary_uses_group_local_date_not_utc()
    {
        // Regressão do QA (corte 3): 01/01/2027 01:30 UTC ainda é 31/12/2026 em São Paulo.
        var r = await RoundAsync("55", new DateTimeOffset(2027, 1, 1, 1, 30, 0, TimeSpan.Zero));
        var (red, black) = (r.Teams[0], r.Teams[1]);
        var sheet = new
        {
            presence = Array.Empty<object>(),
            games = new[] { new { homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 1, awayScore = 0 } },
            goals = new[] { new { game = 1, playerProfileId = red.Line[0], ownGoal = false } }
        };
        Assert.Equal(HttpStatusCode.OK, (await r.Admin.PostAsJsonAsync($"/api/matches/{r.MatchId}/result/round", sheet)).StatusCode);

        var dec = await Season(r.Player, r.GroupId, 2026, 12);
        Assert.Equal(red.Line[0], dec.GetProperty("monthScorers")[0].GetProperty("playerProfileId").GetGuid());
        Assert.Single(dec.GetProperty("yearScorers").EnumerateArray());
        Assert.Equal("2026-12-31", dec.GetProperty("rounds")[0].GetProperty("localDate").GetString());

        var jan = await Season(r.Player, r.GroupId, 2027, 1);
        Assert.Empty(jan.GetProperty("monthScorers").EnumerateArray());
        Assert.Empty(jan.GetProperty("yearScorers").EnumerateArray());
        Assert.Empty(jan.GetProperty("rounds").EnumerateArray());
    }

    [Fact]
    public async Task Legacy_closed_match_stays_readable_without_colors_or_goals()
    {
        var admin = NewClient();
        var login = await TestAuth.LoginAsync(admin, AdminPhone);
        var groupId = (await Json(login)).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId, title = "Antiga A/B", startsAt = new DateTimeOffset(2026, 8, 10, 13, 0, 0, TimeSpan.Zero), location = "Arena", playerLimit = 10, openRegistrations = true
        });
        var matchId = (await Json(create)).GetProperty("id").GetGuid();
        await admin.PostAsync($"/api/matches/{matchId}/join", null);
        await admin.PostAsJsonAsync($"/api/matches/{matchId}/transition", new { status = "RegistrationClosed" });
        var me = await Json(await admin.GetAsync("/api/auth/me"));
        var adminProfile = me.GetProperty("playerProfileId").GetGuid();

        // Sem sorteio de três cores: o fechamento antigo continua aceito.
        var legacy = await admin.PostAsJsonAsync($"/api/matches/{matchId}/result/close", new
        {
            homeScore = 3, awayScore = 2, manOfTheMatchPlayerProfileId = adminProfile,
            presence = new[] { new { playerProfileId = adminProfile, present = true } }, ratings = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);

        var round = await Json(await admin.GetAsync($"/api/matches/{matchId}/result/round"));
        Assert.True(round.GetProperty("isLegacy").GetBoolean());
        Assert.Equal(3, round.GetProperty("legacyHomeScore").GetInt32());
        Assert.Empty(round.GetProperty("teams").EnumerateArray());
        Assert.Empty(round.GetProperty("games").EnumerateArray());
        Assert.Equal(adminProfile, round.GetProperty("bestPlayerProfileId").GetGuid());

        var correction = await admin.PutAsJsonAsync($"/api/matches/{matchId}/result/round", new
        {
            games = Array.Empty<object>(), goals = Array.Empty<object>(), presence = Array.Empty<object>(), reason = "Tentativa em rodada antiga"
        });
        Assert.Equal(HttpStatusCode.BadRequest, correction.StatusCode);

        var season = await Season(admin, groupId, 2026, 8);
        var r0 = season.GetProperty("rounds")[0];
        Assert.True(r0.GetProperty("isLegacy").GetBoolean());
        Assert.Empty(season.GetProperty("monthScorers").EnumerateArray());
        var adminRow = season.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("playerProfileId").GetGuid() == adminProfile);
        Assert.Equal(1, adminRow.GetProperty("roundsPlayed").GetInt32());
        Assert.Equal(1, adminRow.GetProperty("bestPlayer").GetInt32());
        Assert.Equal(0, adminRow.GetProperty("goals").GetInt32());
    }
}
