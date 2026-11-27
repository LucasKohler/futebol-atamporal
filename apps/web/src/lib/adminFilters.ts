import type { GroupPlayer, MonthlyMember, Payment, RegistrationPlayer } from "./api";
import { playerDisplayName } from "./playerDisplayName";

/** Minúsculas e sem acento, para busca tolerante ("joao" acha "João"). */
export function normalizeSearch(text: string): string {
  return text
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .trim();
}

function matchesText(query: string, ...fields: (string | null | undefined)[]): boolean {
  const q = normalizeSearch(query);
  if (!q) return true;
  const digits = q.replace(/\D/g, "");
  return fields.some((f) => {
    if (!f) return false;
    if (normalizeSearch(f).includes(q)) return true;
    // Telefone: compara só dígitos, quando a busca tem ao menos 3 dígitos.
    return digits.length >= 3 && digits === q.replace(/[\s()+-]/g, "") && f.replace(/\D/g, "").includes(digits);
  });
}

// ---------- Elenco ----------

export type PlayerFilter = "all" | "goalkeepers" | "monthly" | "casual";

export const PLAYER_FILTERS: { id: PlayerFilter; label: string }[] = [
  { id: "all", label: "Todos" },
  { id: "goalkeepers", label: "Goleiros aptos" },
  { id: "monthly", label: "Mensalistas" },
  { id: "casual", label: "Avulsos" },
];

export function filterPlayers(players: GroupPlayer[], query: string, filter: PlayerFilter): GroupPlayer[] {
  return players
    .filter((p) => {
      if (filter === "goalkeepers" && !p.canPlayGoalkeeper) return false;
      if (filter === "monthly" && !p.isMonthlyMember) return false;
      if (filter === "casual" && p.isMonthlyMember) return false;
      return matchesText(query, p.registeredName, p.nickname, p.displayName, p.phone);
    })
    .sort((a, b) =>
      playerDisplayName(a.registeredName, a.nickname).localeCompare(playerDisplayName(b.registeredName, b.nickname), "pt-BR"),
    );
}

export function countPlayers(players: GroupPlayer[]): Record<PlayerFilter, number> {
  return {
    all: players.length,
    goalkeepers: players.filter((p) => p.canPlayGoalkeeper).length,
    monthly: players.filter((p) => p.isMonthlyMember).length,
    casual: players.filter((p) => !p.isMonthlyMember).length,
  };
}

// ---------- Mensalidades ----------

export type PaymentFilter = "all" | "open" | "paid" | "exempt";

export const PAYMENT_FILTERS: { id: PaymentFilter; label: string }[] = [
  { id: "all", label: "Todos" },
  { id: "open", label: "Pendentes" },
  { id: "paid", label: "Pagos" },
  { id: "exempt", label: "Isentos" },
];

export const PAYMENT_STATUS_LABEL: Record<string, string> = {
  Pending: "Pendente",
  Overdue: "Atrasado",
  Paid: "Pago",
  Exempt: "Isento",
  Cancelled: "Cancelado",
  // Diária: presença corrigida para ausente depois de paga (contrato backend 2026-11-27).
  RefundPending: "Devolução pendente",
  Refunded: "Devolvido",
  NotCharged: "Sem cobrança ainda",
};

/** Em aberto = pendente ou atrasado. */
export function isOpenPayment(p: Pick<Payment, "status">): boolean {
  return p.status === "Pending" || p.status === "Overdue";
}

export function filterPayments(payments: Payment[], query: string, filter: PaymentFilter): Payment[] {
  return payments
    .filter((p) => {
      if (filter === "open" && !isOpenPayment(p)) return false;
      if (filter === "paid" && p.status !== "Paid") return false;
      if (filter === "exempt" && p.status !== "Exempt") return false;
      return matchesText(query, p.playerName);
    })
    .sort((a, b) => playerDisplayName(a.playerName).localeCompare(playerDisplayName(b.playerName), "pt-BR"));
}

export type PaymentSummary = {
  total: number;
  paid: number;
  open: number;
  overdue: number;
  exempt: number;
  cancelled: number;
  /** Soma dos lançamentos pagos. */
  received: number;
  /** Soma do que é cobrável na competência (exclui isentos e cancelados). */
  expected: number;
};

/** Resumo financeiro apenas com os lançamentos existentes da competência (sem estimativas). */
export function summarizePayments(payments: Payment[]): PaymentSummary {
  const s: PaymentSummary = { total: payments.length, paid: 0, open: 0, overdue: 0, exempt: 0, cancelled: 0, received: 0, expected: 0 };
  for (const p of payments) {
    if (p.status === "Paid") {
      s.paid++;
      s.received += p.amount;
    }
    if (isOpenPayment(p)) s.open++;
    if (p.status === "Overdue") s.overdue++;
    if (p.status === "Exempt") s.exempt++;
    if (p.status === "Cancelled") s.cancelled++;
    if (p.status !== "Exempt" && p.status !== "Cancelled") s.expected += p.amount;
  }
  return s;
}

export function formatMoney(value: number): string {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

// ---------- Vínculos ----------

export type MemberFilter = "all" | "active" | "inactive";

export const MEMBER_FILTERS: { id: MemberFilter; label: string }[] = [
  { id: "all", label: "Todos" },
  { id: "active", label: "Ativos" },
  { id: "inactive", label: "Inativos" },
];

export const MEMBER_STATUS_LABEL: Record<string, string> = { Active: "Ativo", Inactive: "Inativo", Suspended: "Suspenso" };

export function filterMembers(members: MonthlyMember[], query: string, filter: MemberFilter): MonthlyMember[] {
  return members.filter((m) => {
    if (filter === "active" && m.status !== "Active") return false;
    if (filter === "inactive" && m.status === "Active") return false;
    return matchesText(query, m.displayName);
  });
}

// ---------- Lista da partida ----------

export function filterRegistrations(players: RegistrationPlayer[], query: string): RegistrationPlayer[] {
  return players.filter((p) => matchesText(query, p.displayName));
}

// ---------- Competência ----------

/** Últimos `count` meses terminando em `current` (inclusive), do mais recente para o mais antigo. */
export function recentPeriods(current: { year: number; month: number }, count: number) {
  const list: { year: number; month: number }[] = [];
  let { year, month } = current;
  for (let i = 0; i < count; i++) {
    list.push({ year, month });
    month--;
    if (month === 0) {
      month = 12;
      year--;
    }
  }
  return list;
}
