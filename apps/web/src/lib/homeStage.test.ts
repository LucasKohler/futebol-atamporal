import { describe, expect, it } from "vitest";
import type { MatchDetail, MyMatchDayPayment } from "./api";
import { homePlan, spotsText, whenText } from "./homeStage";
import { dueAtText, isLaunchedOpenFee, isSettled, matchFeeBadge } from "./pix";

const match = (status: string, myStatus: string | null = null): MatchDetail => ({
  match: {
    id: "m1",
    groupId: "g",
    startsAt: "2026-11-29T23:00:00Z",
    location: "Quadra",
    playerLimit: 18,
    status,
    confirmedCount: 18,
    waitlistCount: 2,
    spotsRemaining: 0,
  },
  myStatus,
  confirmed: [],
  waitlist: [],
});

const fee = (matchId: string, status: string, startsAt: string, dueAt: string | null = null): MyMatchDayPayment => ({
  matchFeeId: `f-${matchId}`,
  matchId,
  matchTitle: null,
  startsAt,
  location: "Quadra",
  amount: 25,
  status,
  paymentSource: "None",
  paidAt: null,
  activePix: null,
  dueAt,
});

describe("Home por etapa", () => {
  it("lista aberta e jogador fora: etapa confirmar", () => {
    const plan = homePlan(match("RegistrationOpen"), []);
    expect(plan.stage).toMatchObject({ kind: "confirm", joined: false });
    expect(plan.overdueFee).toBeNull();
  });

  it("diária atrasada vai ao topo e a confirmação continua logo abaixo", () => {
    const fees = [
      fee("old2", "Overdue", "2026-11-22T23:00:00Z", "2026-11-23T03:00:00Z"),
      fee("old1", "Overdue", "2026-11-15T23:00:00Z", "2026-11-16T03:00:00Z"),
    ];
    const plan = homePlan(match("RegistrationOpen"), fees);
    expect(plan.overdueFee?.matchId).toBe("old1"); // a mais antiga primeiro
    expect(plan.overdueCount).toBe(2);
    expect(plan.stage.kind).toBe("confirm");
  });

  it("times sorteados / em andamento: etapa ver time e placar", () => {
    expect(homePlan(match("Drawn", "Confirmed"), null).stage.kind).toBe("play");
    expect(homePlan(match("InProgress", "Confirmed"), null).stage.kind).toBe("play");
    expect(homePlan(match("AwaitingClose", "Confirmed"), null).stage.kind).toBe("play");
  });

  it("diária só aparece depois de lançada no fechamento (dueAt); antes do jogo não há cobrança na Home", () => {
    const future = fee("next", "Pending", "2026-12-06T23:00:00Z", null);
    const today = fee("today", "Pending", "2026-11-27T23:00:00Z", "2026-11-28T03:00:00Z");
    const plan = homePlan(null, [future, today]);
    expect(plan.pendingFee?.matchId).toBe("today");
    expect(plan.overdueFee).toBeNull();
    expect(plan.stage.kind).toBe("none");
  });

  it("paga, isenta, removida ou em devolução não pede pagamento", () => {
    for (const s of ["Paid", "Exempt", "Cancelled", "RefundPending", "Refunded"]) {
      expect(isSettled(s)).toBe(true);
      expect(isLaunchedOpenFee({ status: s, dueAt: "2026-11-28T03:00:00Z" })).toBe(false);
    }
    expect(homePlan(null, [fee("x", "RefundPending", "2026-11-22T23:00:00Z", "2026-11-23T03:00:00Z")]).pendingFee).toBeNull();
  });
});

describe("textos da Home e da diária", () => {
  it("vagas com rótulo em vez de 18/18", () => {
    expect(spotsText({ playerLimit: 18, spotsRemaining: 3, waitlistCount: 0 })).toBe("3 vagas livres de 18");
    expect(spotsText({ playerLimit: 18, spotsRemaining: 1, waitlistCount: 0 })).toBe("1 vaga livre de 18");
    expect(spotsText({ playerLimit: 18, spotsRemaining: 0, waitlistCount: 2 })).toBe("Lista cheia · 2 na fila");
  });

  it("data sem segundos e relativa", () => {
    const now = new Date("2026-11-27T15:00:00Z");
    expect(whenText("2026-11-27T23:00:00Z", "America/Sao_Paulo", now)).toBe("Hoje · 20:00");
    expect(whenText("2026-11-28T23:00:00Z", "America/Sao_Paulo", now)).toBe("Amanhã · 20:00");
    expect(whenText("2026-12-06T23:00:00Z", "America/Sao_Paulo", now)).not.toMatch(/:\d\d:\d\d/);
  });

  it("vencimento mostrado como 23:59 do dia do fechamento no fuso do grupo", () => {
    // Meia-noite de 28/11 em São Paulo = 03:00Z
    expect(dueAtText("2026-11-28T03:00:00Z", "America/Sao_Paulo")).toBe("até 23:59 de 27/11");
  });

  it("selos da diária", () => {
    expect(matchFeeBadge("Overdue", "x")).toEqual({ label: "Atrasada", tone: "danger" });
    expect(matchFeeBadge("Pending", "x")).toEqual({ label: "Pendente", tone: "warning" });
    expect(matchFeeBadge("Pending", null)).toEqual({ label: "Sem cobrança ainda", tone: "neutral" });
    // sem dueAt nunca é cobrável, nem para jogo já passado (sem pagamento antes do fechamento)
    expect(isLaunchedOpenFee({ status: "Pending", dueAt: null })).toBe(false);
    expect(matchFeeBadge("RefundPending", "x").label).toBe("Devolução pendente");
    expect(matchFeeBadge("Refunded", "x").label).toBe("Devolvida");
  });
});
