import { describe, expect, it } from "vitest";
import type { Draw, DrawTeam } from "./api";
import { buildTeamsSummary, isLineupFrozen, nextSwapSelection, orderedMembers, teamStyle } from "./teams";

describe("isLineupFrozen", () => {
  it("freezes only after the match starts", () => {
    expect(["RegistrationOpen", "RegistrationClosed", "Drawn"].map(isLineupFrozen)).toEqual([false, false, false]);
    expect(["InProgress", "AwaitingClose"].map(isLineupFrozen)).toEqual([true, true]);
  });
});

// Dados fictícios.
function team(color: string | null, name: string, sortOrder: number, members: [string, string][]): DrawTeam {
  return {
    id: `t-${name}`,
    name,
    color,
    sortOrder,
    lineCount: members.filter(([, r]) => r === "Line").length,
    lineSum: 20,
    lineAverage: 5,
    goalkeeperName: members.find(([, r]) => r === "Goalkeeper")?.[0] ?? null,
    goalkeeperRating: 9,
    totalSkill: 20,
    members: members.map(([n, role]) => ({ playerProfileId: `p-${n}`, displayName: n, role, skillRating: 7.5, goalkeeperRating: 9 })),
  };
}

const draw: Draw = {
  id: "d1",
  matchId: "m1",
  seed: "s",
  status: "Confirmed",
  teamCount: 3,
  algorithmVersion: "3cores-v1",
  isLegacy: false,
  createdAt: "2026-11-25T00:00:00Z",
  confirmedAt: "2026-11-25T00:00:00Z",
  strengthDelta: 1,
  lineAverageSpread: 0.5,
  lineSumSpread: 1,
  lineCountSpread: 0,
  totalCountSpread: 0,
  goalkeepersAssigned: 2,
  goalkeeperSpread: 1,
  warnings: ["Verde sem goleiro apto"],
  blockers: [],
  rosterChanged: false,
  rosterChanges: [],
  canConfirm: false,
  isLocked: false,
  teams: [
    team("Preto", "Preto", 1, [["Beto", "Line"], ["Gui", "Goalkeeper"]]),
    team("Vermelho", "Vermelho", 0, [["Ana", "Line"], ["Paulo", "Goalkeeper"]]),
    team("Verde", "Verde", 2, [["Caio", "Line"], ["Duda", "Line"]]),
  ],
};

describe("teamStyle", () => {
  it("labels colors by text and falls back for legacy teams", () => {
    expect(teamStyle({ color: "Vermelho", name: "Vermelho" })).toMatchObject({ label: "Vermelho", swatch: "bg-red-600" });
    expect(teamStyle({ color: null, name: "Time A" })).toMatchObject({ label: "Time A", emoji: "⚪" });
  });
});

describe("orderedMembers", () => {
  it("puts the goalkeeper first", () => {
    expect(orderedMembers(draw.teams[0]).map((m) => m.displayName)).toEqual(["Gui", "Beto"]);
  });
});

describe("buildTeamsSummary", () => {
  it("lists confirmed teams in color order without ratings or warnings", () => {
    expect(buildTeamsSummary(draw)).toBe(
      ["", "🎽 Times", "🔴 Vermelho: Paulo (gol), Ana", "⚫ Preto: Gui (gol), Beto", "🟢 Verde: Caio, Duda · gol em rodízio"].join("\n"),
    );
    const text = buildTeamsSummary(draw)!;
    expect(text).not.toMatch(/7,5|7\.5|9|sem goleiro apto/);
  });

  it("returns nothing for drafts or stale lineups", () => {
    expect(buildTeamsSummary({ ...draw, status: "Draft" })).toBeNull();
    expect(buildTeamsSummary({ ...draw, rosterChanged: true })).toBeNull();
  });
});

describe("nextSwapSelection", () => {
  const a = { playerProfileId: "a", teamId: "t1", role: "Line" };
  const b = { playerProfileId: "b", teamId: "t2", role: "Line" };
  const sameTeam = { playerProfileId: "c", teamId: "t1", role: "Line" };
  const keeperSameTeam = { playerProfileId: "k", teamId: "t1", role: "Goalkeeper" };

  it("selects, swaps across teams and toggles off", () => {
    expect(nextSwapSelection(null, a)).toEqual({ selected: a, swap: null });
    expect(nextSwapSelection(a, b)).toEqual({ selected: null, swap: ["a", "b"] });
    expect(nextSwapSelection(a, a)).toEqual({ selected: null, swap: null });
  });

  it("moves selection inside the same team and role, but swaps roles inside a team", () => {
    expect(nextSwapSelection(a, sameTeam)).toEqual({ selected: sameTeam, swap: null });
    expect(nextSwapSelection(a, keeperSameTeam)).toEqual({ selected: null, swap: ["a", "k"] });
  });
});
