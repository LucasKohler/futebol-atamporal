import { describe, expect, it } from "vitest";
import { formatRating, validateRatings } from "./ratings";

describe("validateRatings", () => {
  it("accepts zero and keeps an empty goalkeeper rating as null", () => {
    expect(validateRatings({ lineRating: "0", canPlayGoalkeeper: true, goalkeeperRating: "" })).toEqual({
      ok: true,
      value: { lineRating: 0, canPlayGoalkeeper: true, goalkeeperRating: null },
    });
    expect(validateRatings({ lineRating: "7,5", canPlayGoalkeeper: true, goalkeeperRating: "0" })).toEqual({
      ok: true,
      value: { lineRating: 7.5, canPlayGoalkeeper: true, goalkeeperRating: 0 },
    });
  });

  it.each(["-1", "10.01", "11", "abc", "5.555"])("rejects line rating %s", (value) => {
    expect(validateRatings({ lineRating: value, canPlayGoalkeeper: false, goalkeeperRating: "" }).ok).toBe(false);
  });

  it("requires a line rating", () => {
    expect(validateRatings({ lineRating: " ", canPlayGoalkeeper: false, goalkeeperRating: "" }).ok).toBe(false);
  });

  it("requires goalkeeper aptitude to store a goalkeeper rating", () => {
    const result = validateRatings({ lineRating: "5", canPlayGoalkeeper: false, goalkeeperRating: "8" });
    expect(result).toEqual({ ok: false, error: expect.stringContaining("Apto para o gol") });
  });
});

describe("formatRating", () => {
  it("distinguishes absent from zero", () => {
    expect(formatRating(null)).toBe("sem nota");
    expect(formatRating(0)).toBe("0");
    expect(formatRating(7.25)).toBe("7,25");
  });
});
