import { describe, expect, it } from "vitest";
import type { SeasonStats } from "./api";
import { leaderText, monthName, myStats, plural } from "./season";

const scorer = (id: string, name: string, goals: number, position: number, isLeader: boolean) => ({
  playerProfileId: id,
  displayName: name,
  goals,
  position,
  isLeader,
});

describe("leaderText", () => {
  it("names a single leader or a shared leadership", () => {
    expect(leaderText([scorer("a", "Ana", 3, 1, true), scorer("b", "Beto", 1, 2, false)])).toBe("Ana, 3 gols");
    expect(leaderText([scorer("a", "Ana", 1, 1, true), scorer("b", "Beto", 1, 1, true), scorer("c", "Caio", 1, 1, true)])).toBe(
      "Ana, Beto e Caio dividem a liderança, 1 gol",
    );
    expect(leaderText([])).toBe("Sem artilheiro ainda.");
  });
});

describe("monthName and plural", () => {
  it("returns Portuguese month names and plurals", () => {
    expect(monthName(11)).toBe("novembro");
    expect(plural(1, "gol", "gols")).toBe("1 gol");
    expect(plural(0, "gol", "gols")).toBe("0 gols");
  });
});

describe("myStats", () => {
  it("reads month and year goals and awards for the player", () => {
    const season: SeasonStats = {
      year: 2026,
      month: 11,
      timeZoneId: "America/Sao_Paulo",
      monthScorers: [scorer("a", "Ana", 2, 1, true)],
      yearScorers: [scorer("a", "Ana", 5, 1, true)],
      colors: [],
      players: [
        { playerProfileId: "a", displayName: "Ana", roundsPlayed: 3, roundsWon: 1, gamesWon: 4, goals: 2, ownGoals: 0, bestPlayer: 1, bestGoalkeeper: 0 },
      ],
      rounds: [],
    };
    expect(myStats(season, "a")).toEqual({ monthGoals: 2, yearGoals: 5, roundsPlayed: 3, roundsWon: 1, bestPlayer: 1, bestGoalkeeper: 0 });
    expect(myStats(season, "x")).toMatchObject({ monthGoals: 0, yearGoals: 0, roundsPlayed: 0 });
    expect(myStats(null, "a")).toBeNull();
  });
});
