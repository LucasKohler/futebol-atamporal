import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { groupPeriod } from "./season";

// Regressão do QA (corte 3): aparelho em UTC às 01:30 de 01/01/2027, grupo em São Paulo ainda em 31/12/2026.
const NEW_YEAR_UTC = new Date("2027-01-01T01:30:00Z");

describe("groupPeriod (fuso do grupo, mesma regra da API)", () => {
  it("keeps December 2026 for America/Sao_Paulo while UTC is already January 2027", () => {
    expect(groupPeriod(NEW_YEAR_UTC, "America/Sao_Paulo")).toEqual({ year: 2026, month: 12 });
    expect(groupPeriod(NEW_YEAR_UTC, "UTC")).toEqual({ year: 2027, month: 1 });
  });

  it("falls back to UTC like the API when the time zone is missing or invalid", () => {
    expect(groupPeriod(NEW_YEAR_UTC, "Fuso/Inexistente")).toEqual({ year: 2027, month: 1 });
    expect(groupPeriod(NEW_YEAR_UTC, null)).toEqual({ year: 2027, month: 1 });
    expect(groupPeriod(NEW_YEAR_UTC, "")).toEqual({ year: 2027, month: 1 });
  });

  it("matches the API month boundary used in the .NET tests (01/12 01:30 UTC = 30/11 in São Paulo)", () => {
    expect(groupPeriod(new Date("2026-12-01T01:30:00Z"), "America/Sao_Paulo")).toEqual({ year: 2026, month: 11 });
  });
});

describe("rankings and perfil start in the group's period", () => {
  const read = (rel: string) => readFileSync(join(__dirname, "..", "app", rel), "utf8");

  it.each(["rankings/page.tsx", "perfil/page.tsx"])("%s never derives the period from the device clock", (file) => {
    const source = read(file);
    expect(source).toContain("groupPeriod(new Date(), group.timeZoneId)");
    expect(source).not.toMatch(/\.getMonth\(\)|\.getFullYear\(\)/);
  });
});
