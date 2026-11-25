import { describe, expect, it } from "vitest";
import type { MatchDetail, RegistrationPlayer } from "./api";
import { buildWhatsAppSummary } from "./matchSummary";

// Dados fictícios.
function player(name: string, status: string, position: number | null = null): RegistrationPlayer {
  return {
    registrationId: `r-${name}`,
    playerProfileId: `p-${name}`,
    displayName: name,
    status,
    priorityClass: "Casual",
    waitlistPosition: position,
    skillRating: 9.5,
  };
}

const detail: MatchDetail = {
  match: {
    id: "m1",
    groupId: "g1",
    title: "Pelada semanal",
    startsAt: "2026-11-29T13:00:00Z",
    location: "Arena Central",
    playerLimit: 3,
    status: "RegistrationOpen",
    notes: "Pix 11999998888 — não divulgar",
    confirmedCount: 3,
    waitlistCount: 2,
    spotsRemaining: 0,
  },
  myStatus: "Confirmed",
  myWaitlistPosition: null,
  confirmed: [player("Cadu", "Confirmed"), player("Beto", "Confirmed"), player("Ana", "Confirmed")],
  waitlist: [player("Duda", "Waitlisted", 1), player("Edu", "Waitlisted", 2)],
};

const options = {
  link: "https://exemplo.test/partida",
  now: new Date("2026-11-25T21:40:00Z"),
  timeZone: "America/Sao_Paulo",
};

describe("buildWhatsAppSummary", () => {
  it("matches the list fixture in persisted order", () => {
    expect(buildWhatsAppSummary(detail, options)).toBe(
      [
        "⚽ *Pelada semanal*",
        "📅 domingo, 29/11 às 10:00",
        "📍 Arena Central",
        "Vagas: 3/3 · lista aberta",
        "",
        "✅ Confirmados (3)",
        "1. Cadu",
        "2. Beto",
        "3. Ana",
        "",
        "⏳ Fila de espera (2)",
        "1. Duda",
        "2. Edu",
        "",
        "Confirme ou desista pelo app: https://exemplo.test/partida",
        "Atualizado em quarta-feira, 25/11 às 18:40",
      ].join("\n"),
    );
  });

  it("never includes phone, ratings, notes or payment data", () => {
    const text = buildWhatsAppSummary(detail, options);
    expect(text).not.toMatch(/\d{10,}/);
    expect(text).not.toContain("9,5");
    expect(text).not.toContain("9.5");
    expect(text).not.toContain("Pix");
    expect(text).not.toMatch(/pago|pendente|mensalista|avulso/i);
  });

  it("inserts the confirmed teams block before the link", () => {
    const text = buildWhatsAppSummary(detail, { ...options, teams: "\n🎽 Times\n🔴 Vermelho: Cadu (gol)" });
    expect(text).toContain("2. Edu\n\n🎽 Times\n🔴 Vermelho: Cadu (gol)\n\nConfirme ou desista");
  });

  it("shows empty lists and the closed state explicitly", () => {
    const empty: MatchDetail = {
      ...detail,
      match: { ...detail.match, status: "RegistrationClosed", confirmedCount: 0, waitlistCount: 0, title: null },
      confirmed: [],
      waitlist: [],
    };
    const text = buildWhatsAppSummary(empty, options);
    expect(text).toContain("⚽ *Pelada*");
    expect(text).toContain("Vagas: 0/3 · lista encerrada");
    expect(text).toContain("✅ Confirmados (0)\n—");
    expect(text).toContain("⏳ Fila de espera (0)\n—");
  });
});
