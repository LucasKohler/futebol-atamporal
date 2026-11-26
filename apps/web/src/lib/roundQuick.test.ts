import { describe, expect, it } from "vitest";
import type { DrawTeam } from "./api";
import { addQuickGoal, clampScore, emptySheet, goalProgress, markAllPresent, pruneAwards, type GameForm, type RosterPlayer } from "./round";
import { lineRange, ratingBarPercent } from "./teams";

// Dados fictícios: R = Vermelho, B = Preto.
const roster: RosterPlayer[] = [
  { playerProfileId: "r1", displayName: "Ana", teamId: "R", isGoalkeeper: true },
  { playerProfileId: "r2", displayName: "Beto", teamId: "R", isGoalkeeper: false },
  { playerProfileId: "b1", displayName: "Caio", teamId: "B", isGoalkeeper: true },
  { playerProfileId: "b2", displayName: "Duda", teamId: "B", isGoalkeeper: false },
];
const game = (hs: number, as: number, goals: GameForm["goals"] = []): GameForm => ({ homeTeamId: "R", awayTeamId: "B", homeScore: hs, awayScore: as, goals });

describe("súmula beira de campo", () => {
  it("placar nunca fica negativo nem passa de 99", () => {
    expect(clampScore(-1)).toBe(0);
    expect(clampScore(120)).toBe(99);
    expect(clampScore(Number.NaN)).toBe(0);
    expect(clampScore(2.7)).toBe(2);
  });

  it("progresso de gols atribuídos por lado", () => {
    const p = goalProgress(game(2, 1, [{ playerProfileId: "r2", ownGoal: false }]), roster);
    expect(p.home).toMatchObject({ credited: 1, score: 2, missing: 1, ok: false });
    expect(p.away).toMatchObject({ credited: 0, score: 1, missing: 1, ok: false });
    const extra = goalProgress(game(0, 0, [{ playerProfileId: "b2", ownGoal: false }]), roster);
    expect(extra.away).toMatchObject({ extra: 1, ok: false });
    expect(goalProgress(game(0, 0), roster).ok).toBe(true);
  });

  it("gol rápido sobe o placar só quando necessário; gol contra conta para o adversário", () => {
    const g1 = addQuickGoal(game(0, 0), roster, "r2", false);
    expect([g1.homeScore, g1.awayScore, g1.goals.length]).toEqual([1, 0, 1]);
    const already = addQuickGoal(game(3, 0), roster, "r2", false);
    expect(already.homeScore).toBe(3);
    const own = addQuickGoal(game(0, 0), roster, "b2", true);
    expect([own.homeScore, own.awayScore]).toEqual([1, 0]);
    expect(goalProgress(own, roster).ok).toBe(true);
  });

  it("marcar todos presentes preserva quem atuou no gol; por time afeta só o time", () => {
    const sheet = emptySheet(roster);
    sheet.presence.r2 = { present: false, playedGoalkeeper: false };
    sheet.presence.b2 = { present: false, playedGoalkeeper: true };
    const onlyB = markAllPresent(sheet.presence, roster, "B");
    expect(onlyB.r2.present).toBe(false);
    expect(onlyB.b2).toEqual({ present: true, playedGoalkeeper: true });
    expect(Object.values(markAllPresent(sheet.presence, roster)).every((p) => p.present)).toBe(true);
  });

  it("prêmio de quem ficou ausente ou saiu do gol é limpo", () => {
    const sheet = { ...emptySheet(roster), bestPlayerProfileId: "r2", bestGoalkeeperProfileId: "b1" };
    sheet.presence = { ...sheet.presence, r2: { present: false, playedGoalkeeper: false }, b1: { present: true, playedGoalkeeper: false } };
    const pruned = pruneAwards(sheet, roster);
    expect(pruned.bestPlayerProfileId).toBe("");
    expect(pruned.bestGoalkeeperProfileId).toBe("");
    const kept = pruneAwards({ ...emptySheet(roster), bestPlayerProfileId: "r1", bestGoalkeeperProfileId: "r1" }, roster);
    expect([kept.bestPlayerProfileId, kept.bestGoalkeeperProfileId]).toEqual(["r1", "r1"]);
  });
});

describe("comparador de equilíbrio", () => {
  const team = (members: DrawTeam["members"]) => ({ members });
  it("amplitude só da linha e ignora notas ocultas", () => {
    expect(
      lineRange(
        team([
          { playerProfileId: "1", displayName: "A", role: "Goalkeeper", skillRating: 9, goalkeeperRating: 8 },
          { playerProfileId: "2", displayName: "B", role: "Line", skillRating: 4, goalkeeperRating: null },
          { playerProfileId: "3", displayName: "C", role: "Line", skillRating: 7.5, goalkeeperRating: null },
        ]),
      ),
    ).toEqual({ min: 4, max: 7.5 });
    expect(lineRange(team([{ playerProfileId: "2", displayName: "B", role: "Line", skillRating: null, goalkeeperRating: null }]))).toBeNull();
  });

  it("barra em escala fixa 0–10", () => {
    expect(ratingBarPercent(5)).toBe(50);
    expect(ratingBarPercent(12)).toBe(100);
    expect(ratingBarPercent(null)).toBe(0);
  });
});
