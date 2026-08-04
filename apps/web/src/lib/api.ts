const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5080";

export type Membership = {
  groupId: string;
  groupName: string;
  groupSlug: string;
  role: string;
  status: string;
};

export type User = {
  id: string;
  email: string;
  displayName: string;
  playerProfileId?: string | null;
  memberships: Membership[];
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
  skillRating: number;
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

export type DrawTeam = {
  id: string;
  name: string;
  sortOrder: number;
  totalSkill: number;
  members: { playerProfileId: string; displayName: string; skillRating: number }[];
};

export type Draw = {
  id: string;
  matchId: string;
  seed: string;
  status: string;
  teamCount: number;
  strengthDelta: number;
  createdAt: string;
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

async function parseJson<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = res.statusText;
    try {
      const body = await res.json();
      detail = body.detail ?? body.title ?? detail;
    } catch {
      /* ignore */
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
  login(email: string, password: string) {
    return fetch(url("/api/auth/login"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ email, password }),
    }).then((r) => parseJson<User>(r));
  },
  register(email: string, password: string, displayName: string) {
    return fetch(url("/api/auth/register"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ email, password, displayName }),
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
        skillRating: number;
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
