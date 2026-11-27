import type { MatchDetail, MyMatchDayPayment } from "./api";
import { isLaunchedOpenFee } from "./pix";

/**
 * Home do jogador orientada à etapa (decisão do Lucas, 27/11):
 * confirmar presença → ver time/placar → resumo e pagamento da diária.
 * Diária atrasada fica no topo e a etapa da rodada aparece logo abaixo.
 * Derivado só de contratos existentes: upcomingMatch + myPayments (dueAt/Overdue do backend 27/11).
 */

export type MatchStage =
  | { kind: "none" }
  | { kind: "confirm"; detail: MatchDetail; joined: boolean }
  | { kind: "closed"; detail: MatchDetail }
  | { kind: "play"; detail: MatchDetail }
  | { kind: "other"; detail: MatchDetail };

export type HomePlan = {
  /** Diária atrasada mais antiga: vai para o topo, antes da etapa da rodada. */
  overdueFee: MyMatchDayPayment | null;
  overdueCount: number;
  stage: MatchStage;
  /** Diária lançada hoje e ainda pendente (etapa "resumo e pagamento"), mostrada depois da etapa da rodada. */
  pendingFee: MyMatchDayPayment | null;
};

const PLAY_STATUSES = ["Drawn", "InProgress", "AwaitingClose"];

export function matchStage(detail: MatchDetail | null): MatchStage {
  if (!detail) return { kind: "none" };
  const status = detail.match.status;
  if (status === "RegistrationOpen") {
    const joined = detail.myStatus === "Confirmed" || detail.myStatus === "Waitlisted";
    return { kind: "confirm", detail, joined };
  }
  if (status === "RegistrationClosed") return { kind: "closed", detail };
  if (PLAY_STATUSES.includes(status)) return { kind: "play", detail };
  return { kind: "other", detail };
}

function byStart(a: MyMatchDayPayment, b: MyMatchDayPayment) {
  return Date.parse(a.startsAt) - Date.parse(b.startsAt);
}

export function homePlan(detail: MatchDetail | null, fees: MyMatchDayPayment[] | null | undefined): HomePlan {
  const open = (fees ?? []).filter((f) => isLaunchedOpenFee(f));
  const overdue = open.filter((f) => f.status === "Overdue").sort(byStart);
  const pending = open.filter((f) => f.status === "Pending").sort(byStart);
  return {
    overdueFee: overdue[0] ?? null,
    overdueCount: overdue.length,
    stage: matchStage(detail),
    pendingFee: pending[0] ?? null,
  };
}

/** "18/18" vira texto rotulado: "3 vagas livres de 18" / "Lista cheia · 2 na fila". */
export function spotsText(m: { playerLimit: number; spotsRemaining: number; waitlistCount: number }): string {
  if (m.spotsRemaining > 0) return `${m.spotsRemaining} ${m.spotsRemaining === 1 ? "vaga livre" : "vagas livres"} de ${m.playerLimit}`;
  return m.waitlistCount > 0 ? `Lista cheia · ${m.waitlistCount} na fila` : "Lista cheia";
}

/** Data sem segundos e relativa quando é hoje/amanhã: "Hoje · 20:00", "sáb., 29 nov. · 20:00". */
export function whenText(iso: string, timeZone?: string, now: Date = new Date()): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  const dayKey = (x: Date) => x.toLocaleDateString("en-CA", { timeZone });
  const time = d.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone });
  const tomorrow = new Date(now.getTime() + 24 * 60 * 60 * 1000);
  if (dayKey(d) === dayKey(now)) return `Hoje · ${time}`;
  if (dayKey(d) === dayKey(tomorrow)) return `Amanhã · ${time}`;
  const day = d.toLocaleDateString("pt-BR", { weekday: "short", day: "2-digit", month: "short", timeZone });
  return `${day} · ${time}`;
}
