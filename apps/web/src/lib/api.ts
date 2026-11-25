// Empty = same-origin `/api/*` via Next.js rewrites (reliable cookies in local dev).
// Set NEXT_PUBLIC_API_URL only when the browser must call the API directly.
const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "";

export type Membership = {
  groupId: string;
  groupName: string;
  groupSlug: string;
  role: string;
  status: string;
};

export type User = {
  id: string;
  phone: string;
  registeredName: string;
  nickname?: string | null;
  displayName: string;
  playerProfileId?: string | null;
  memberships: Membership[];
};

export type GroupPlayer = {
  playerProfileId: string;
  registeredName: string;
  nickname?: string | null;
  displayName: string;
  phone: string;
  isMonthlyMember: boolean;
  /** Nota de linha, 0 a 10. */
  lineRating: number;
  canPlayGoalkeeper: boolean;
  /** Nota de goleiro, 0 a 10; null = sem nota. */
  goalkeeperRating: number | null;
};

export type Group = {
  id: string;
  name: string;
  slug: string;
  timeZoneId: string;
  role: string;
};

export type MatchSummary = {
  id: string;
  groupId: string;
  title?: string | null;
  startsAt: string;
  location: string;
  playerLimit: number;
  status: string;
  notes?: string | null;
  confirmedCount: number;
  waitlistCount: number;
  spotsRemaining: number;
};

export type RegistrationPlayer = {
  registrationId: string;
  playerProfileId: string;
  displayName: string;
  status: string;
  priorityClass: string;
  waitlistPosition?: number | null;
  /** Só vem para administradores; jogadores recebem null. */
  skillRating: number | null;
};

export type MatchDetail = {
  match: MatchSummary;
  myStatus?: string | null;
  myWaitlistPosition?: number | null;
  confirmed: RegistrationPlayer[];
  waitlist: RegistrationPlayer[];
};

export type MonthlyMember = {
  id: string;
  playerProfileId: string;
  displayName: string;
  status: string;
  startedAt: string;
};

export type Payment = {
  id: string;
  monthlyMembershipId: string;
  playerName: string;
  billingPeriodId: string;
  periodLabel: string;
  amount: number;
  status: string;
  paidAt?: string | null;
  notes?: string | null;
};

export type BillingPeriod = { id: string; year: number; month: number; label: string };

/** Notas só chegam para administradores; para jogadores são null. */
export type DrawMember = {
  playerProfileId: string;
  displayName: string;
  /** "Goalkeeper" | "Line" */
  role: string;
  skillRating: number | null;
  goalkeeperRating: number | null;
};

export type DrawTeam = {
  id: string;
  name: string;
  /** "Vermelho" | "Preto" | "Verde"; null em sorteios antigos (Time A/B). */
  color: string | null;
  sortOrder: number;
  lineCount: number;
  lineSum: number | null;
  lineAverage: number | null;
  goalkeeperName: string | null;
  goalkeeperRating: number | null;
  members: DrawMember[];
  totalSkill: number | null;
};

export type Draw = {
  id: string;
  matchId: string;
  seed: string;
  /** "Draft" | "Confirmed" */
  status: string;
  teamCount: number;
  algorithmVersion: string | null;
  isLegacy: boolean;
  createdAt: string;
  confirmedAt: string | null;
  strengthDelta: number | null;
  lineAverageSpread: number | null;
  lineSumSpread: number | null;
  lineCountSpread: number;
  totalCountSpread: number;
  goalkeepersAssigned: number;
  goalkeeperSpread: number | null;
  warnings: string[];
  blockers: string[];
  rosterChanged: boolean;
  rosterChanges: string[];
  canConfirm: boolean;
  isLocked: boolean;
  teams: DrawTeam[];
};

export type RankingRow = {
  playerProfileId: string;
  displayName: string;
  matches: number;
  wins: number;
  losses: number;
  absences: number;
  manOfTheMatch: number;
  averageRating?: number | null;
  winRate: number;
};

/**
 * Mensagem legível de um erro da API: regra de negócio (detail), erro de campo
 * (ValidationProblemDetails.errors) ou título genérico.
 */
export function problemMessage(body: unknown, fallback: string): string {
  if (!body || typeof body !== "object") return fallback;
  const problem = body as { detail?: unknown; title?: unknown; errors?: unknown };
  if (typeof problem.detail === "string" && problem.detail) return problem.detail;
  if (problem.errors && typeof problem.errors === "object") {
    const fields = Object.entries(problem.errors as Record<string, unknown>)
      .filter(([, msgs]) => Array.isArray(msgs) && msgs.length > 0)
      .map(([field]) => field.replace(/^\$\.?/, "") || "corpo da requisição");
    if (fields.length > 0) return `Valor inválido em: ${fields.join(", ")}.`;
  }
  if (typeof problem.title === "string" && problem.title) return problem.title;
  return fallback;
}

async function parseJson<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = res.statusText || `Erro ${res.status}`;
    if (res.status === 409) detail = "A lista mudou ao mesmo tempo. Atualize e tente de novo.";
    try {
      detail = problemMessage(await res.json(), detail);
    } catch {
      /* corpo vazio ou não JSON */
    }
    throw new Error(detail);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

function url(path: string) {
  return `${API_BASE}${path}`;
}

const jsonHeaders = { "Content-Type": "application/json" };

export const api = {
  login(phone: string) {
    return fetch(url("/api/auth/login"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ phone }),
    }).then((r) => parseJson<User>(r));
  },
  register(phone: string, displayName: string) {
    return fetch(url("/api/auth/register"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ phone, displayName }),
    }).then((r) => parseJson<User>(r));
  },
  logout() {
    return fetch(url("/api/auth/logout"), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<void>(r),
    );
  },
  me() {
    return fetch(url("/api/auth/me"), { credentials: "include" }).then((r) => {
      if (r.status === 401) return null;
      return parseJson<User>(r);
    });
  },
  currentGroup() {
    return fetch(url("/api/groups/current"), { credentials: "include" }).then((r) => {
      if (r.status === 401 || r.status === 404) return null;
      return parseJson<Group>(r);
    });
  },
  upcomingMatch(groupId: string) {
    return fetch(url(`/api/matches/upcoming?groupId=${groupId}`), { credentials: "include" }).then((r) => {
      if (r.status === 404) return null;
      return parseJson<MatchDetail>(r);
    });
  },
  getMatch(matchId: string) {
    return fetch(url(`/api/matches/${matchId}`), { credentials: "include" }).then((r) => parseJson<MatchDetail>(r));
  },
  listMatches(groupId: string) {
    return fetch(url(`/api/matches?groupId=${groupId}`), { credentials: "include" }).then((r) =>
      parseJson<MatchSummary[]>(r),
    );
  },
  createMatch(body: {
    groupId: string;
    title?: string;
    startsAt: string;
    location: string;
    playerLimit: number;
    openRegistrations: boolean;
  }) {
    return fetch(url("/api/matches"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<MatchSummary>(r));
  },
  transitionMatch(matchId: string, status: string) {
    return fetch(url(`/api/matches/${matchId}/transition`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ status }),
    }).then((r) => parseJson<MatchSummary>(r));
  },
  joinMatch(matchId: string) {
    return fetch(url(`/api/matches/${matchId}/join`), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<MatchDetail>(r),
    );
  },
  leaveMatch(matchId: string) {
    return fetch(url(`/api/matches/${matchId}/leave`), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<MatchDetail>(r),
    );
  },
  removeRegistration(matchId: string, playerProfileId: string) {
    return fetch(url(`/api/matches/${matchId}/registrations/${playerProfileId}/remove`), {
      method: "POST",
      credentials: "include",
    }).then((r) => parseJson<MatchDetail>(r));
  },
  listPlayers(groupId: string) {
    return fetch(url(`/api/groups/${groupId}/players`), { credentials: "include" }).then((r) =>
      parseJson<GroupPlayer[]>(r),
    );
  },
  setNickname(groupId: string, playerProfileId: string, nickname: string | null) {
    return fetch(url(`/api/groups/${groupId}/players/${playerProfileId}/nickname`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ nickname }),
    }).then((r) => parseJson<GroupPlayer>(r));
  },
  setRatings(
    groupId: string,
    playerProfileId: string,
    body: { lineRating: number; canPlayGoalkeeper: boolean; goalkeeperRating: number | null },
  ) {
    return fetch(url(`/api/groups/${groupId}/players/${playerProfileId}/ratings`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<GroupPlayer>(r));
  },
  listMembers(groupId: string) {
    return fetch(url(`/api/groups/${groupId}/memberships`), { credentials: "include" }).then((r) =>
      parseJson<MonthlyMember[]>(r),
    );
  },
  addMember(groupId: string, playerProfileId: string) {
    return fetch(url(`/api/groups/${groupId}/memberships`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ playerProfileId }),
    }).then((r) => parseJson<MonthlyMember>(r));
  },
  setMemberStatus(groupId: string, membershipId: string, status: string) {
    return fetch(url(`/api/groups/${groupId}/memberships/${membershipId}/status`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ status }),
    }).then((r) => parseJson<MonthlyMember>(r));
  },
  ensurePeriod(groupId: string, year: number, month: number) {
    return fetch(url(`/api/groups/${groupId}/memberships/periods`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ year, month }),
    }).then((r) => parseJson<BillingPeriod>(r));
  },
  listPayments(groupId: string, year?: number, month?: number) {
    const qs = new URLSearchParams();
    if (year) qs.set("year", String(year));
    if (month) qs.set("month", String(month));
    return fetch(url(`/api/groups/${groupId}/memberships/payments?${qs}`), { credentials: "include" }).then((r) =>
      parseJson<Payment[]>(r),
    );
  },
  upsertPayment(groupId: string, membershipId: string, periodId: string, amount: number, status: string) {
    return fetch(url(`/api/groups/${groupId}/memberships/${membershipId}/payments/${periodId}`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ amount, status }),
    }).then((r) => parseJson<Payment>(r));
  },
  getDraw(matchId: string) {
    return fetch(url(`/api/matches/${matchId}/draw`), { credentials: "include" }).then((r) => {
      if (r.status === 404) return null;
      return parseJson<Draw>(r);
    });
  },
  createDraw(matchId: string, seed?: string) {
    return fetch(url(`/api/matches/${matchId}/draw`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ seed: seed ?? null }),
    }).then((r) => parseJson<Draw>(r));
  },
  confirmDraw(matchId: string, drawId: string) {
    return fetch(url(`/api/matches/${matchId}/draw/${drawId}/confirm`), {
      method: "POST",
      credentials: "include",
    }).then((r) => parseJson<Draw>(r));
  },
  swapPlayers(matchId: string, drawId: string, playerA: string, playerB: string) {
    return fetch(url(`/api/matches/${matchId}/draw/${drawId}/swap`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ playerA, playerB }),
    }).then((r) => parseJson<Draw>(r));
  },
  closeMatch(
    matchId: string,
    body: {
      homeScore: number;
      awayScore: number;
      winnerTeamId?: string | null;
      manOfTheMatchPlayerProfileId?: string | null;
      notes?: string;
      presence: { playerProfileId: string; present: boolean }[];
      ratings: { playerProfileId: string; score: number }[];
    },
  ) {
    return fetch(url(`/api/matches/${matchId}/result/close`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<unknown>(r));
  },
  rankings(groupId: string, sortBy = "wins") {
    return fetch(url(`/api/groups/${groupId}/rankings?sortBy=${sortBy}`), { credentials: "include" }).then((r) =>
      parseJson<RankingRow[]>(r),
    );
  },
  playerHistory(playerProfileId: string) {
    return fetch(url(`/api/players/${playerProfileId}/history`), { credentials: "include" }).then((r) =>
      parseJson<{
        playerProfileId: string;
        displayName: string;
        /** Só para administradores; null para jogadores. */
        skillRating: number | null;
        history: {
          matchId: string;
          title?: string;
          startsAt: string;
          location: string;
          status: string;
          rating?: number;
        }[];
      }>(r),
    );
  },
};
