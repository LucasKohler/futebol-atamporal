using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FutebolAtemporal.Domain.Entities;
using FutebolAtemporal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FutebolAtemporal.Api.Tests;

/// <summary>
/// Cobertura de integração do modo ao vivo (D1–D6), pênaltis alternados (Opção A),
/// defesas de pênalti do goleiro, fechamento com Melhores do Dia e estatísticas.
/// </summary>
public class LiveMatchApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminPhone = "11999999999";
    private readonly WebApplicationFactory<Program> _factory;

    public LiveMatchApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:UseSqlite", "true");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source=live-{Guid.NewGuid():N}.db");
            builder.UseSetting("Seed:AdminPhone", AdminPhone);
        });
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Team(Guid Id, string Color, Guid Keeper, List<Guid> Line);
    private sealed record LiveScenario(HttpClient Admin, HttpClient Player, Guid GroupId, Guid MatchId, List<Team> Teams);

    private async Task<LiveScenario> DrawnScenarioAsync(string prefix, DateTimeOffset? startsAt = null)
    {
        var admin = NewClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone });
        var groupId = (await Json(login)).GetProperty("memberships")[0].GetProperty("groupId").GetGuid();
        var create = await admin.PostAsJsonAsync("/api/matches", new
        {
            groupId,
            title = $"Pelada {prefix}",
            startsAt = startsAt ?? new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero),
            location = "Arena",
            playerLimit = 20,
            openRegistrations = true
        });
        var matchId = (await Json(create)).GetProperty("id").GetGuid();
        await admin.PostAsync($"/api/matches/{matchId}/join", null);

        HttpClient? player = null;
        foreach (var (name, i) in new[] { "G1", "G2", "G3", "L1", "L2", "L3", "L4", "L5" }.Select((n, i) => (n, i)))
        {
            var c = NewClient();
            var reg = await c.PostAsJsonAsync("/api/auth/register", new { phone = $"1197{prefix}{i:D5}", displayName = name });
            var id = (await Json(reg)).GetProperty("playerProfileId").GetGuid();
            await c.PostAsync($"/api/matches/{matchId}/join", null);
            if (name.StartsWith('G'))
                await admin.PutAsJsonAsync($"/api/groups/{groupId}/players/{id}/ratings", new { lineRating = 4m, canPlayGoalkeeper = true, goalkeeperRating = 7m });
            player ??= c;
        }

        var draw = await Json(await admin.PostAsJsonAsync($"/api/matches/{matchId}/draw", new { seed = prefix }));
        var drawId = draw.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/matches/{matchId}/draw/{drawId}/confirm", null)).StatusCode);

        var teams = draw.GetProperty("teams").EnumerateArray().Select(t =>
        {
            var members = t.GetProperty("members").EnumerateArray().ToList();
            var keeper = members.First(m => m.GetProperty("role").GetString() == "Goalkeeper").GetProperty("playerProfileId").GetGuid();
            var line = members.Where(m => m.GetProperty("role").GetString() != "Goalkeeper")
                .Select(m => m.GetProperty("playerProfileId").GetGuid()).ToList();
            return new Team(t.GetProperty("id").GetGuid(), t.GetProperty("name").GetString()!, keeper, line);
        }).ToList();

        return new LiveScenario(admin, player!, groupId, matchId, teams);
    }

    [Fact]
    public async Task Full_live_day_flow_with_penalties_saves_rotation_close_and_stats()
    {
        var s = await DrawnScenarioAsync("11");
        var (red, black, green) = (s.Teams[0], s.Teams[1], s.Teams[2]);

        // GET inicial por membro comum (somente leitura) em status Drawn
        var initialRes = await s.Player.GetAsync($"/api/matches/{s.MatchId}/live");
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        var initial = await Json(initialRes);
        Assert.Equal(0, initial.GetProperty("version").GetInt32());
        Assert.Equal("Drawn", initial.GetProperty("matchStatus").GetString());
        Assert.Equal(red.Id, initial.GetProperty("suggestedNext").GetProperty("homeTeamId").GetGuid());
        Assert.Equal(black.Id, initial.GetProperty("suggestedNext").GetProperty("awayTeamId").GetGuid());

        // Jogador comum não pode iniciar partida
        var forbidden = await s.Player.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = red.Id, awayTeamId = black.Id, expectedVersion = 0
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Iniciar Partida #1: transiciona Match para InProgress e cria jogo 1
        var start1 = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = red.Id, awayTeamId = black.Id, expectedVersion = 0
        });
        Assert.Equal(HttpStatusCode.Created, start1.StatusCode);
        var live = await Json(start1);
        Assert.Equal("InProgress", live.GetProperty("matchStatus").GetString());
        Assert.Equal(1, live.GetProperty("version").GetInt32());
        var game1Id = live.GetProperty("games")[0].GetProperty("id").GetGuid();

        // Tentar iniciar segunda partida com uma aberta -> 409 game_in_progress
        var dupStart = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = black.Id, awayTeamId = green.Id, expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, dupStart.StatusCode);
        Assert.Equal("game_in_progress", (await Json(dupStart)).GetProperty("code").GetString());

        // Adicionar gol 1 (Vermelho) com goalId fixo e testar idempotência (retry com versão antiga)
        var goal1Id = Guid.NewGuid();
        var g1Res = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/goals", new
        {
            goalId = goal1Id, playerProfileId = red.Line[0], ownGoal = false, expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, g1Res.StatusCode);
        live = await Json(g1Res);
        Assert.Equal(2, live.GetProperty("version").GetInt32());
        Assert.Equal(1, live.GetProperty("games")[0].GetProperty("homeScore").GetInt32());

        var g1Retry = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/goals", new
        {
            goalId = goal1Id, playerProfileId = red.Line[0], ownGoal = false, expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, g1Retry.StatusCode);
        live = await Json(g1Retry);
        Assert.Equal(2, live.GetProperty("version").GetInt32());
        Assert.Single(live.GetProperty("games")[0].GetProperty("goals").EnumerateArray());

        // Gol contra do Vermelho empata 1x1 para o Preto
        var goal2Id = Guid.NewGuid();
        var g2Res = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/goals", new
        {
            goalId = goal2Id, playerProfileId = red.Line[1], ownGoal = true, expectedVersion = 2
        });
        Assert.Equal(HttpStatusCode.OK, g2Res.StatusCode);
        live = await Json(g2Res);
        Assert.Equal(3, live.GetProperty("version").GetInt32());
        Assert.Equal(1, live.GetProperty("games")[0].GetProperty("homeScore").GetInt32());
        Assert.Equal(1, live.GetProperty("games")[0].GetProperty("awayScore").GetInt32());

        // Conflito de versão ao tentar encerrar com versão defasada -> 409 live_version_conflict + currentVersion
        var conflict = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/finish", new
        {
            penaltyWinnerTeamId = black.Id, penaltySaves = Array.Empty<object>(), expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await Json(conflict);
        Assert.Equal("live_version_conflict", conflictBody.GetProperty("code").GetString());
        Assert.Equal(3, conflictBody.GetProperty("currentVersion").GetInt32());

        // Tentar encerrar empate sem vencedor nos pênaltis -> 400 invalid_game
        var tieNoWinner = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/finish", new
        {
            penaltyWinnerTeamId = (Guid?)null, penaltySaves = Array.Empty<object>(), expectedVersion = 3
        });
        Assert.Equal(HttpStatusCode.BadRequest, tieNoWinner.StatusCode);
        Assert.Equal("invalid_game", (await Json(tieNoWinner)).GetProperty("code").GetString());

        // Tentar atribuir defesa de pênalti a jogador de linha -> 400 invalid_game
        var badSave = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/finish", new
        {
            penaltyWinnerTeamId = black.Id,
            penaltySaves = new[] { new { goalkeeperProfileId = black.Line[0], count = 1 } },
            expectedVersion = 3
        });
        Assert.Equal(HttpStatusCode.BadRequest, badSave.StatusCode);
        Assert.Equal("invalid_game", (await Json(badSave)).GetProperty("code").GetString());

        // Encerrar Partida #1 com vitória do Preto nos pênaltis e 2 defesas do goleiro do Preto
        var finish1 = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game1Id}/finish", new
        {
            penaltyWinnerTeamId = black.Id,
            penaltySaves = new[] { new { goalkeeperProfileId = black.Keeper, count = 2 } },
            expectedVersion = 3
        });
        Assert.Equal(HttpStatusCode.OK, finish1.StatusCode);
        live = await Json(finish1);
        Assert.Equal(4, live.GetProperty("version").GetInt32());
        Assert.True(live.GetProperty("games")[0].GetProperty("decidedByPenalties").GetBoolean());
        Assert.Equal(black.Id, live.GetProperty("games")[0].GetProperty("winnerTeamId").GetGuid());
        // Sugestão automática: Preto (vencedor) x Verde (3ª cor)
        Assert.Equal(black.Id, live.GetProperty("suggestedNext").GetProperty("homeTeamId").GetGuid());
        Assert.Equal(green.Id, live.GetProperty("suggestedNext").GetProperty("awayTeamId").GetGuid());

        // Partida #2: Preto 1 x 0 Verde (vitória no tempo normal)
        var start2 = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = black.Id, awayTeamId = green.Id, expectedVersion = 4
        });
        Assert.Equal(HttpStatusCode.Created, start2.StatusCode);
        live = await Json(start2);
        var game2Id = live.GetProperty("games")[1].GetProperty("id").GetGuid();

        // Tentar fechar o dia com Partida #2 em andamento -> 409 game_in_progress
        var closeWhileOpen = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/result/round", new
        {
            expectedLiveVersion = 5,
            bestPlayerProfileId = black.Line[0],
            bestGoalkeeperProfileId = black.Keeper
        });
        Assert.Equal(HttpStatusCode.Conflict, closeWhileOpen.StatusCode);
        Assert.Equal("game_in_progress", (await Json(closeWhileOpen)).GetProperty("code").GetString());

        var g2Goal = Guid.NewGuid();
        await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game2Id}/goals", new
        {
            goalId = g2Goal, playerProfileId = black.Line[0], ownGoal = false, expectedVersion = 5
        });

        // Tentar remover e recolocar o gol para validar DELETE /goals/{goalId}
        var delGoal = await s.Admin.DeleteAsync($"/api/matches/{s.MatchId}/live/games/{game2Id}/goals/{g2Goal}?expectedVersion=6");
        Assert.Equal(HttpStatusCode.OK, delGoal.StatusCode);
        Assert.Equal(0, (await Json(delGoal)).GetProperty("games")[1].GetProperty("homeScore").GetInt32());

        await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game2Id}/goals", new
        {
            goalId = Guid.NewGuid(), playerProfileId = black.Line[0], ownGoal = false, expectedVersion = 7
        });

        // Vitória no tempo normal não pode ter penaltyWinnerTeamId -> 400
        var badWinPen = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game2Id}/finish", new
        {
            penaltyWinnerTeamId = black.Id, penaltySaves = Array.Empty<object>(), expectedVersion = 8
        });
        Assert.Equal(HttpStatusCode.BadRequest, badWinPen.StatusCode);

        var finish2 = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{game2Id}/finish", new
        {
            penaltyWinnerTeamId = (Guid?)null,
            penaltySaves = new[] { new { goalkeeperProfileId = green.Keeper, count = 1 } },
            expectedVersion = 8
        });
        Assert.Equal(HttpStatusCode.OK, finish2.StatusCode);
        live = await Json(finish2);
        Assert.Equal(9, live.GetProperty("version").GetInt32());

        // Fechar o dia com Melhores do Dia usando expectedLiveVersion = 9
        var closeRes = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/result/round", new
        {
            expectedLiveVersion = 9,
            bestPlayerProfileId = black.Line[0],
            bestGoalkeeperProfileId = black.Keeper,
            notes = "Pelada encerrada ao vivo"
        });
        Assert.Equal(HttpStatusCode.OK, closeRes.StatusCode);
        var round = await Json(closeRes);
        Assert.Equal(2, round.GetProperty("rulesVersion").GetInt32());
        var blackTeam = round.GetProperty("teams").EnumerateArray().Single(t => t.GetProperty("teamId").GetGuid() == black.Id);
        Assert.Equal(2, blackTeam.GetProperty("wins").GetInt32());
        Assert.Equal(1, blackTeam.GetProperty("winsRegular").GetInt32());
        Assert.Equal(1, blackTeam.GetProperty("winsPenalties").GetInt32());
        Assert.True(blackTeam.GetProperty("isRoundWinner").GetBoolean());

        // Após Completed: GET /live retorna 200 com matchStatus=Completed e escrita retorna 409 day_completed
        var afterCloseLive = await s.Player.GetAsync($"/api/matches/{s.MatchId}/live");
        Assert.Equal(HttpStatusCode.OK, afterCloseLive.StatusCode);
        Assert.Equal("Completed", (await Json(afterCloseLive)).GetProperty("matchStatus").GetString());

        var writeAfterClose = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = red.Id, awayTeamId = green.Id, expectedVersion = 9
        });
        Assert.Equal(HttpStatusCode.Conflict, writeAfterClose.StatusCode);
        Assert.Equal("day_completed", (await Json(writeAfterClose)).GetProperty("code").GetString());

        // Season, Rankings (sortBy=penaltySaves) e Player History refletem penaltySaves e bestGoalkeeper
        var season = await Json(await s.Player.GetAsync($"/api/groups/{s.GroupId}/season?year=2026&month=11"));
        var blackSeasonColor = season.GetProperty("colors").EnumerateArray().Single(c => c.GetProperty("color").GetString() == black.Color);
        Assert.Equal(2, blackSeasonColor.GetProperty("wins").GetInt32());
        Assert.Equal(1, blackSeasonColor.GetProperty("winsRegular").GetInt32());
        Assert.Equal(1, blackSeasonColor.GetProperty("winsPenalties").GetInt32());
        var keeperSeason = season.GetProperty("players").EnumerateArray().Single(p => p.GetProperty("playerProfileId").GetGuid() == black.Keeper);
        Assert.Equal(2, keeperSeason.GetProperty("penaltySaves").GetInt32());
        Assert.Equal(1, keeperSeason.GetProperty("gamesWonOnPenalties").GetInt32());

        var ranks = await Json(await s.Player.GetAsync($"/api/groups/{s.GroupId}/rankings?sortBy=penaltySaves"));
        Assert.Equal(black.Keeper, ranks[0].GetProperty("playerProfileId").GetGuid());
        Assert.Equal(2, ranks[0].GetProperty("penaltySaves").GetInt32());
        Assert.Equal(1, ranks[0].GetProperty("bestGoalkeeper").GetInt32());

        var history = await Json(await s.Player.GetAsync($"/api/players/{black.Keeper}/history"));
        Assert.Equal(2, history.GetProperty("penaltySaves").GetInt32());
        Assert.Equal(1, history.GetProperty("bestGoalkeeper").GetInt32());
        Assert.Equal(2, history.GetProperty("history")[0].GetProperty("penaltySaves").GetInt32());
        Assert.True(history.GetProperty("history")[0].GetProperty("playedGoalkeeper").GetBoolean());
    }

    [Fact]
    public async Task Live_edit_insert_delete_and_presence_update_work_with_renumbering()
    {
        var s = await DrawnScenarioAsync("12");
        var (red, black, green) = (s.Teams[0], s.Teams[1], s.Teams[2]);

        // Inserção retroativa na posição 1 (Finished)
        var ins1 = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = red.Id,
            awayTeamId = black.Id,
            insertAt = 1,
            expectedVersion = 0,
            goals = new[] { new { goalId = Guid.NewGuid(), playerProfileId = red.Line[0], ownGoal = false } }
        });
        Assert.Equal(HttpStatusCode.Created, ins1.StatusCode);
        var live = await Json(ins1);
        var g1Id = live.GetProperty("games")[0].GetProperty("id").GetGuid();

        // Inserção retroativa na posição 1 desloca o jogo anterior para Order=2
        var ins0 = await s.Admin.PostAsJsonAsync($"/api/matches/{s.MatchId}/live/games", new
        {
            homeTeamId = green.Id,
            awayTeamId = black.Id,
            insertAt = 1,
            expectedVersion = 1,
            goals = Array.Empty<object>(),
            penaltyWinnerTeamId = green.Id
        });
        Assert.Equal(HttpStatusCode.Created, ins0.StatusCode);
        live = await Json(ins0);
        Assert.Equal(2, live.GetProperty("games").GetArrayLength());
        var firstId = live.GetProperty("games")[0].GetProperty("id").GetGuid();
        Assert.Equal(1, live.GetProperty("games")[0].GetProperty("order").GetInt32());
        Assert.Equal(g1Id, live.GetProperty("games")[1].GetProperty("id").GetGuid());
        Assert.Equal(2, live.GetProperty("games")[1].GetProperty("order").GetInt32());

        // Marcar jogador de linha como goleiro via PUT /live/presence e recusar ausente para quem marcou gol
        var badAbsent = await s.Admin.PutAsJsonAsync($"/api/matches/{s.MatchId}/live/presence", new
        {
            expectedVersion = 2,
            presence = new[] { new { playerProfileId = red.Line[0], present = false } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, badAbsent.StatusCode);

        var promoteGk = await s.Admin.PutAsJsonAsync($"/api/matches/{s.MatchId}/live/presence", new
        {
            expectedVersion = 2,
            presence = new[] { new { playerProfileId = green.Line[0], present = true, playedGoalkeeper = true } }
        });
        Assert.Equal(HttpStatusCode.OK, promoteGk.StatusCode);

        // Editar jogo 1 via PUT /live/games/{id} adicionando defesa do novo goleiro
        var edit = await s.Admin.PutAsJsonAsync($"/api/matches/{s.MatchId}/live/games/{firstId}", new
        {
            homeTeamId = green.Id,
            awayTeamId = black.Id,
            status = "Finished",
            goals = Array.Empty<object>(),
            penaltyWinnerTeamId = green.Id,
            penaltySaves = new[] { new { goalkeeperProfileId = green.Line[0], count = 1 } },
            expectedVersion = 3
        });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        // Excluir jogo 1 renumera jogo 2 para Order=1 preservando seus gols
        var del = await s.Admin.DeleteAsync($"/api/matches/{s.MatchId}/live/games/{firstId}?expectedVersion=4");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        live = await Json(del);
        Assert.Single(live.GetProperty("games").EnumerateArray());
        Assert.Equal(g1Id, live.GetProperty("games")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, live.GetProperty("games")[0].GetProperty("order").GetInt32());
        Assert.Equal(1, live.GetProperty("games")[0].GetProperty("homeScore").GetInt32());
    }

    [Fact]
    public async Task Legacy_v1_round_revision_keeps_v1_draw_scoring_without_breaking()
    {
        var s = await DrawnScenarioAsync("13");
        var (red, black) = (s.Teams[0], s.Teams[1]);

        // Insere diretamente uma revisão v1 legada (sem rulesVersion, sem penaltyWinnerTeamId) no banco.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var match = await db.Matches.FirstAsync(m => m.Id == s.MatchId);
            var draw = await db.TeamDraws.Include(d => d.Teams).ThenInclude(t => t.Members)
                .FirstAsync(d => d.MatchId == s.MatchId);
            var participants = draw.Teams.SelectMany(t => t.Members.Select(m => new
            {
                playerProfileId = m.PlayerProfileId,
                teamId = t.Id,
                present = true,
                playedGoalkeeper = m.Role == Domain.Enums.TeamRole.Goalkeeper
            })).ToList();

            var v1Json = JsonSerializer.Serialize(new
            {
                participants,
                games = new[] { new { order = 1, homeTeamId = red.Id, awayTeamId = black.Id, homeScore = 1, awayScore = 1 } },
                goals = new[]
                {
                    new { gameOrder = 1, playerProfileId = red.Line[0], ownGoal = false },
                    new { gameOrder = 1, playerProfileId = black.Line[0], ownGoal = false }
                },
                bestPlayerId = (Guid?)null,
                bestGoalkeeperId = (Guid?)null,
                notes = "v1 legado"
            });

            db.MatchResultRevisions.Add(new MatchResultRevision
            {
                Id = Guid.NewGuid(),
                MatchId = s.MatchId,
                Revision = 1,
                IsCurrent = true,
                SheetJson = v1Json,
                CreatedByUserId = Guid.NewGuid()
            });
            match.Status = Domain.Enums.MatchStatus.Completed;
            await db.SaveChangesAsync();
        }

        var round = await Json(await s.Player.GetAsync($"/api/matches/{s.MatchId}/result/round"));
        Assert.Equal(1, round.GetProperty("rulesVersion").GetInt32());
        var redTeam = round.GetProperty("teams").EnumerateArray().Single(t => t.GetProperty("teamId").GetGuid() == red.Id);
        Assert.Equal(1, redTeam.GetProperty("draws").GetInt32());
        Assert.Equal(1, redTeam.GetProperty("points").GetInt32());
        Assert.True(redTeam.GetProperty("isRoundWinner").GetBoolean());
        Assert.Equal("Draw", round.GetProperty("games")[0].GetProperty("outcome").GetString());
    }
}
