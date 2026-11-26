import { describe, expect, it } from "vitest";
import {
  countPlayers,
  filterMembers,
  filterPayments,
  filterPlayers,
  normalizeSearch,
  recentPeriods,
  summarizePayments,
} from "./adminFilters";
import type { GroupPlayer, MonthlyMember, Payment } from "./api";

// Dados fictícios.
const player = (id: string, name: string, extra: Partial<GroupPlayer> = {}): GroupPlayer => ({
  playerProfileId: id,
  registeredName: name,
  nickname: null,
  displayName: name,
  phone: "+55 11 99999-0000",
  isMonthlyMember: false,
  lineRating: 5,
  canPlayGoalkeeper: false,
  goalkeeperRating: null,
  ...extra,
});

const players = [
  player("1", "João Silva", { nickname: "Jota", isMonthlyMember: true, phone: "+55 11 98888-1234" }),
  player("2", "Ana Souza", { canPlayGoalkeeper: true, goalkeeperRating: 7 }),
  player("3", "Bruno Lima", { isMonthlyMember: true, canPlayGoalkeeper: true, goalkeeperRating: 6 }),
];

const pay = (id: string, name: string, status: string, amount = 100): Payment => ({
  id,
  monthlyMembershipId: `m${id}`,
  playerName: name,
  billingPeriodId: "p",
  periodLabel: "novembro 2026",
  amount,
  status,
});

describe("filtros do elenco", () => {
  it("busca sem acento, por apelido e por dígitos do celular", () => {
    expect(normalizeSearch("  JOÃO ")).toBe("joao");
    expect(filterPlayers(players, "joao", "all").map((p) => p.playerProfileId)).toEqual(["1"]);
    expect(filterPlayers(players, "jota", "all").map((p) => p.playerProfileId)).toEqual(["1"]);
    expect(filterPlayers(players, "1234", "all").map((p) => p.playerProfileId)).toEqual(["1"]);
  });

  it("chips: goleiros aptos, mensalistas e avulsos; contagens", () => {
    expect(filterPlayers(players, "", "goalkeepers").map((p) => p.playerProfileId)).toEqual(["2", "3"]);
    expect(filterPlayers(players, "", "monthly").map((p) => p.playerProfileId)).toEqual(["3", "1"]);
    expect(filterPlayers(players, "", "casual").map((p) => p.playerProfileId)).toEqual(["2"]);
    expect(countPlayers(players)).toEqual({ all: 3, goalkeepers: 2, monthly: 2, casual: 1 });
    expect(filterPlayers([], "x", "all")).toEqual([]);
  });
});

describe("mensalidades", () => {
  const payments = [pay("1", "Ana", "Paid", 100), pay("2", "Beto", "Pending", 100), pay("3", "Caio", "Overdue", 100), pay("4", "Duda", "Exempt", 100), pay("5", "Edu", "Cancelled", 80)];

  it("resumo só com lançamentos da competência: isento e cancelado fora do previsto", () => {
    expect(summarizePayments(payments)).toEqual({ total: 5, paid: 1, open: 2, overdue: 1, exempt: 1, cancelled: 1, received: 100, expected: 300 });
    expect(summarizePayments([])).toMatchObject({ total: 0, received: 0, expected: 0 });
  });

  it("filtro pendentes inclui atrasados; busca por nome", () => {
    expect(filterPayments(payments, "", "open").map((p) => p.id)).toEqual(["2", "3"]);
    expect(filterPayments(payments, "", "paid").map((p) => p.id)).toEqual(["1"]);
    expect(filterPayments(payments, "", "exempt").map((p) => p.id)).toEqual(["4"]);
    expect(filterPayments(payments, "beto", "all").map((p) => p.id)).toEqual(["2"]);
  });

  it("vínculos ativos e inativos", () => {
    const members: MonthlyMember[] = [
      { id: "a", playerProfileId: "1", displayName: "Ana", status: "Active", startedAt: "2026-01-01" },
      { id: "b", playerProfileId: "2", displayName: "Beto", status: "Inactive", startedAt: "2026-01-01" },
    ];
    expect(filterMembers(members, "", "active").map((m) => m.id)).toEqual(["a"]);
    expect(filterMembers(members, "", "inactive").map((m) => m.id)).toEqual(["b"]);
  });

  it("competências recentes atravessam a virada do ano", () => {
    expect(recentPeriods({ year: 2027, month: 2 }, 4)).toEqual([
      { year: 2027, month: 2 },
      { year: 2027, month: 1 },
      { year: 2026, month: 12 },
      { year: 2026, month: 11 },
    ]);
  });
});
