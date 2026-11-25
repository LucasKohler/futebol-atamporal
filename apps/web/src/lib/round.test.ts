import { describe, expect, it } from "vitest";
import {
  awardCandidates,
  buildPayload,
  creditedScore,
  emptySheet,
  gameIssues,
  gameLabel,
  previewStandings,
  type GameForm,
  type RosterPlayer,
} from "./round";

// Dados fictícios: R = Vermelho, B = Preto, G = Verde.
const roster: RosterPlayer[] = [
  { playerProfileId: "r1", displayName: "Ana", teamId: "R", isGoalkeeper: true },
  { playerProfileId: "r2", displayName: "Beto", teamId: "R", isGoalkeeper: false },
  { playerProfileId: "b1", displayName: "Caio", teamId: "B", isGoalkeeper: true },
  { playerProfileId: "b2", displayName: "Duda", teamId: "B", isGoalkeeper: false },
  { playerProfileId: "g1", displayName: "Edu", teamId: "G", isGoalkeeper: false },
];

const game = (h: string, a: string, hs: number, as: number, goals: GameForm["goals"] = []): GameForm => ({
  homeTeamId: h,
  awayTeamId: a,
  homeScore: hs,
  awayScore: as,
  goals,
});

describe("creditedScore and gameIssues", () => {
  it("credits own goals to the opponent and accepts a closed score", () => {
    const g = game("R", "B", 1, 1, [
      { playerProfileId: "r2", ownGoal: false },
      { playerProfileId: "r2", ownGoal: true },
    ]);
    expect(creditedScore(g, roster)).toEqual({ home: 1, away: 1 });
    expect(gameIssues(g, roster, emptySheet(roster).presence)).toEqual([]);
  });

  it("flags unclosed score, absent scorer, wrong team and same color", () => {
    const presence = { ...emptySheet(roster).presence, r2: { present: false, playedGoalkeeper: false } };
    const issues = gameIssues(game("R", "B", 2, 0, [{ playerProfileId: "r2", ownGoal: false }, { playerProfileId: "g1", ownGoal: false }]), roster, presence);
    expect(issues).toContain("Gol de jogador marcado ausente.");
    expect(issues).toContain("Autor do gol não joga nesse confronto.");
    expect(issues.some((i) => i.includes("não fecham o placar"))).toBe(true);
    expect(gameIssues(game("R", "R", 0, 0), roster, presence)).toContain("Escolha duas cores diferentes.");
  });
});

describe("previewStandings", () => {
  it("derives points and a single or shared round winner", () => {
    const single = previewStandings([game("R", "B", 2, 1), game("B", "G", 0, 0), game("G", "R", 1, 0)], ["R", "B", "G"]);
    expect(single.find((s) => s.teamId === "G")).toMatchObject({ points: 4, isRoundWinner: true });
    expect(single.find((s) => s.teamId === "R")).toMatchObject({ points: 3, isRoundWinner: false });

    const shared = previewStandings([game("R", "B", 1, 1)], ["R", "B", "G"]);
    expect(shared.filter((s) => s.isRoundWinner).map((s) => s.teamId)).toEqual(["R", "B"]);
  });
});

describe("buildPayload and awardCandidates", () => {
  it("numbers goals by game and keeps presence for everyone", () => {
    const form = emptySheet(roster);
    form.games = [game("R", "B", 1, 0, [{ playerProfileId: "r2", ownGoal: false }]), game("B", "G", 0, 1, [{ playerProfileId: "b2", ownGoal: true }])];
    form.bestPlayerProfileId = "r2";
    form.reason = "  ";
    const payload = buildPayload(form, roster);
    expect(payload.goals).toEqual([
      { game: 1, playerProfileId: "r2", ownGoal: false },
      { game: 2, playerProfileId: "b2", ownGoal: true },
    ]);
    expect(payload.presence).toHaveLength(5);
    expect(payload.reason).toBeNull();
    expect(payload.bestGoalkeeperProfileId).toBeNull();
  });

  it("limits best goalkeeper to present players who played in goal", () => {
    const presence = { ...emptySheet(roster).presence, b1: { present: false, playedGoalkeeper: true }, g1: { present: true, playedGoalkeeper: true } };
    const c = awardCandidates(roster, presence);
    expect(c.bestGoalkeeper.map((p) => p.playerProfileId)).toEqual(["r1", "g1"]);
    expect(c.bestPlayer.map((p) => p.playerProfileId)).not.toContain("b1");
  });
});

describe("gameLabel", () => {
  it("formats by color", () => {
    const names: Record<string, string> = { R: "Vermelho", B: "Preto" };
    expect(gameLabel(game("R", "B", 2, 1), (id) => names[id])).toBe("Vermelho 2 x 1 Preto");
  });
});
