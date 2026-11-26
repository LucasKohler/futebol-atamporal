import { describe, expect, it } from "vitest";
import { ApiError, type LiveDay, type LiveGame } from "./api";
import {
  applySaveDelta,
  inProgressSavesBody,
  buildCorrectionSheet,
  closeDayIssue,
  currentGame,
  defaultMatchup,
  draftFromGame,
  draftIssues,
  finishIssue,
  gameResultText,
  goalkeepersForGame,
  isVersionConflict,
  lastGoalForSide,
  newGoalId,
  penaltyWinnerFor,
  savesText,
  savesToInput,
  scoreFromGoals,
  sortedStandings,
  waitingTeamId,
} from "./live";

// Dados fictícios: R = Vermelho, B = Preto, G = Verde.
const player = (id: string, teamId: string, gk = false, present = true) => ({
  playerProfileId: id,
  displayName: id.toUpperCase(),
  teamId,
  present,
  playedGoalkeeper: gk,
  goals: 0,
  ownGoals: 0,
  gamesWon: 0,
  gamesWonOnPenalties: 0,
  penaltySaves: 0,
  teamGoalsConceded: null,
  soleGoalkeeperForTeam: false,
});

const game = (over: Partial<LiveGame> = {}): LiveGame => ({
  id: "g1",
  order: 1,
  status: "Finished",
  homeTeamId: "R",
  awayTeamId: "B",
  homeScore: 1,
  awayScore: 1,
  decidedByPenalties: true,
  goals: [
    { id: "x1", playerProfileId: "r2", displayName: "R2", ownGoal: false, teamCreditedId: "R" },
    { id: "x2", playerProfileId: "b2", displayName: "B2", ownGoal: false, teamCreditedId: "B" },
  ],
  penaltyWinnerTeamId: "R",
  winnerTeamId: "R",
  penaltySaves: [{ goalkeeperProfileId: "r1", displayName: "R1", count: 1 }],
  ...over,
});

const day = (over: Partial<LiveDay> = {}): LiveDay => ({
  matchId: "m",
  localDate: "2026-11-28",
  matchStatus: "InProgress",
  version: 3,
  teams: [
    { id: "R", name: "Time 1", color: "Vermelho" },
    { id: "B", name: "Time 2", color: "Preto" },
    { id: "G", name: "Time 3", color: "Verde" },
  ],
  games: [game()],
  standings: [
    { teamId: "R", played: 1, wins: 1, winsRegular: 0, winsPenalties: 1, losses: 0, goalsFor: 1, goalsAgainst: 1, isDayLeader: true },
    { teamId: "B", played: 1, wins: 0, winsRegular: 0, winsPenalties: 0, losses: 1, goalsFor: 1, goalsAgainst: 1, isDayLeader: false },
    { teamId: "G", played: 0, wins: 0, winsRegular: 0, winsPenalties: 0, losses: 0, goalsFor: 0, goalsAgainst: 0, isDayLeader: false },
  ],
  players: [player("r1", "R", true), player("r2", "R"), player("b1", "B", true), player("b2", "B"), player("g1", "G", true), player("g2", "G", false, false)],
  suggestedNext: { homeTeamId: "R", awayTeamId: "G" },
  ...over,
});

const colorOf = (id: string) => ({ R: "Vermelho", B: "Preto", G: "Verde" })[id] ?? "?";

describe("partida ao vivo", () => {
  it("próximo confronto vem do servidor (quem ganha fica) e a 3ª cor espera", () => {
    expect(defaultMatchup(day())).toEqual({ homeTeamId: "R", awayTeamId: "G" });
    expect(defaultMatchup(day({ suggestedNext: null }))).toEqual({ homeTeamId: "R", awayTeamId: "B" });
    expect(waitingTeamId(day(), "R", "G")).toBe("B");
  });

  it("jogo em andamento e o − remove o último gol daquele lado", () => {
    expect(currentGame(day())).toBeNull();
    const live = game({ status: "InProgress" });
    expect(currentGame(day({ games: [live] }))?.id).toBe("g1");
    expect(lastGoalForSide(live, "B")?.id).toBe("x2");
    expect(lastGoalForSide(live, "G")).toBeNull();
  });

  it("empate exige vencedor nos pênaltis entre as duas cores; sem empate ele não vai", () => {
    expect(finishIssue(1, 1, { homeTeamId: "R", awayTeamId: "B" }, null)).toMatch(/pênaltis/);
    expect(finishIssue(1, 1, { homeTeamId: "R", awayTeamId: "B" }, "G")).toMatch(/duas cores/);
    expect(finishIssue(1, 1, { homeTeamId: "R", awayTeamId: "B" }, "B")).toBeNull();
    expect(finishIssue(2, 1, { homeTeamId: "R", awayTeamId: "B" }, null)).toBeNull();
    expect(penaltyWinnerFor(2, 1, "R")).toBeNull();
    expect(penaltyWinnerFor(0, 0, "R")).toBe("R");
  });

  it("🧤 só goleiros presentes das duas cores; contagem 1..10", () => {
    expect(goalkeepersForGame(day(), { homeTeamId: "R", awayTeamId: "B" }).map((p) => p.playerProfileId)).toEqual(["r1", "b1"]);
    expect(savesToInput({ r1: 2, b1: 0, g1: 15 })).toEqual([
      { goalkeeperProfileId: "r1", count: 2 },
      { goalkeeperProfileId: "g1", count: 10 },
    ]);
  });
});

describe("checagem e correção do dia", () => {
  const teamOf = (id: string) => ({ r1: "R", r2: "R", b1: "B", b2: "B", g1: "G" })[id];

  it("placar derivado dos gols, gol contra para a adversária", () => {
    expect(
      scoreFromGoals(
        [
          { goalId: "1", playerProfileId: "r2", ownGoal: false },
          { goalId: "2", playerProfileId: "b2", ownGoal: true },
          { goalId: "3", playerProfileId: "b2", ownGoal: false },
        ],
        "R",
        "B",
        teamOf,
      ),
    ).toEqual({ home: 2, away: 1 });
  });

  it("rascunho: autor de fora, empate sem pênaltis e defesa de não goleiro são apontados", () => {
    const d = day();
    const draft = draftFromGame(game());
    expect(draftIssues(draft, d)).toEqual([]);
    expect(draftIssues({ ...draft, penaltyWinnerTeamId: null }, d)).toContain("Empate: escolha quem ganhou nos pênaltis.");
    expect(draftIssues({ ...draft, goals: [...draft.goals, { goalId: "z", playerProfileId: "g1", ownGoal: false }] }, d)).toContain(
      "Todo gol precisa de autor de uma das duas cores.",
    );
    expect(draftIssues({ ...draft, saves: { r2: 1 } }, d)).toContain("Defesa de pênalti só para quem atuou no gol nessas cores.");
  });

  it("correção do dia fechado gera súmula v2 completa com placar dos gols e motivo", () => {
    const g2 = game({ id: "g2", order: 2, homeTeamId: "R", awayTeamId: "G", homeScore: 0, awayScore: 0, goals: [], penaltyWinnerTeamId: "G", penaltySaves: [] });
    const d = day({ matchStatus: "Completed", games: [game(), g2] });
    const edited = { ...draftFromGame(game()), goals: [...draftFromGame(game()).goals, { goalId: "n", playerProfileId: "r2", ownGoal: false }] };
    const sheet = buildCorrectionSheet(d, { g1: edited, g2: "deleted" }, { bestPlayerProfileId: "r2", bestGoalkeeperProfileId: "r1", notes: null }, "  gol esquecido  ");
    expect(sheet.games).toEqual([
      { id: "g1", homeTeamId: "R", awayTeamId: "B", homeScore: 2, awayScore: 1, penaltyWinnerTeamId: null, penaltySaves: [{ goalkeeperProfileId: "r1", count: 1 }] },
    ]);
    expect(sheet.goals.map((g) => [g.game, g.id])).toEqual([
      [1, "x1"],
      [1, "x2"],
      [1, "n"],
    ]);
    expect(sheet.reason).toBe("gol esquecido");
    expect(sheet.bestPlayerProfileId).toBe("r2");
    expect(sheet.presence).toHaveLength(6);
  });

  it("classificação pela regra A e bloqueios para concluir o dia", () => {
    const d = day({
      standings: [
        { teamId: "B", played: 2, wins: 1, winsRegular: 0, winsPenalties: 1, losses: 1, goalsFor: 3, goalsAgainst: 3, isDayLeader: false },
        { teamId: "R", played: 2, wins: 1, winsRegular: 1, winsPenalties: 0, losses: 1, goalsFor: 2, goalsAgainst: 2, isDayLeader: true },
      ],
    });
    expect(sortedStandings(d).map((s) => s.teamId)).toEqual(["R", "B"]);
    expect(closeDayIssue(day())).toBeNull();
    expect(closeDayIssue(day({ games: [] }))).toMatch(/ao menos uma/);
    expect(closeDayIssue(day({ games: [game({ status: "InProgress", order: 4 })] }))).toMatch(/ao menos uma/);
    expect(closeDayIssue(day({ games: [game(), game({ id: "g9", status: "InProgress", order: 2 })] }))).toBe("Finalize a Partida #2 antes de concluir o dia.");
    expect(closeDayIssue(day({ matchStatus: "Completed" }))).toMatch(/já foi concluído/);
  });

  it("textos de resultado, conflito de versão e id de gol", () => {
    expect(gameResultText(game(), colorOf)).toBe("Vermelho 1 x 1 Preto · pênaltis: Vermelho");
    expect(savesText(game())).toBe("🧤 R1 (1)");
    expect(isVersionConflict(new ApiError("x", 409, "live_version_conflict", 4))).toBe(true);
    expect(isVersionConflict(new ApiError("x", 409, "game_in_progress", null))).toBe(false);
    expect(newGoalId()).toMatch(/^[0-9a-f-]{36}$/);
  });
});

describe("🧤 ao vivo grava na hora (regressão QA: defesa sumia ao recarregar antes de finalizar)", () => {
  it("+1/−1 parte do valor salvo no servidor, com limite 0..10, e zera remove o goleiro da lista", () => {
    const live = game({ status: "InProgress", penaltySaves: [{ goalkeeperProfileId: "r1", displayName: "R1", count: 1 }] });
    expect(applySaveDelta(live, "r1", 1)).toEqual([{ goalkeeperProfileId: "r1", count: 2 }]);
    expect(applySaveDelta(live, "b1", 1)).toEqual([
      { goalkeeperProfileId: "r1", count: 1 },
      { goalkeeperProfileId: "b1", count: 1 },
    ]);
    expect(applySaveDelta(live, "r1", -1)).toEqual([]);
    expect(applySaveDelta(game({ penaltySaves: [{ goalkeeperProfileId: "r1", displayName: "R1", count: 10 }] }), "r1", 1)).toEqual([
      { goalkeeperProfileId: "r1", count: 10 },
    ]);
  });

  it("depois de conflito, recalcula sobre a súmula nova (não perde a defesa do outro aparelho)", () => {
    const other = game({ status: "InProgress", penaltySaves: [{ goalkeeperProfileId: "r1", displayName: "R1", count: 2 }] });
    expect(applySaveDelta(other, "r1", 1)).toEqual([{ goalkeeperProfileId: "r1", count: 3 }]);
  });

  it("corpo do PUT mantém jogo em andamento, gols pelos ids e sem vencedor nos pênaltis", () => {
    const live = game({ status: "InProgress", penaltyWinnerTeamId: null });
    expect(inProgressSavesBody(live, [{ goalkeeperProfileId: "r1", count: 1 }], 7)).toEqual({
      homeTeamId: "R",
      awayTeamId: "B",
      status: "InProgress",
      goals: [
        { goalId: "x1", playerProfileId: "r2", ownGoal: false },
        { goalId: "x2", playerProfileId: "b2", ownGoal: false },
      ],
      penaltyWinnerTeamId: null,
      penaltySaves: [{ goalkeeperProfileId: "r1", count: 1 }],
      expectedVersion: 7,
    });
  });
});
