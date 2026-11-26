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
  /** Pix: campos aditivos do contrato 2026-11-26 (ausentes em APIs antigas). */
  playerProfileId?: string;
  paymentSource?: PaymentSource;
  latestPix?: PixChargeSummary | null;
};

// ---------- Pix Mercado Pago (contrato 2026-11-26-contrato-api-pix.md) ----------

/** "Live" | "Simulator" | "Unconfigured". Nunca traz segredos. */
export type GatewayStatus = {
  mode: string;
  isConfigured: boolean;
  isSimulated: boolean;
  webhookSecretConfigured: boolean;
  defaultMonthlyFee: number;
  defaultMatchDayFee: number;
  pixExpirationMinutes: number;
  webhookPath: string;
};

export type PaymentSource = "None" | "Pix" | "Manual";

/** status: "Pending" | "Approved" | "Cancelled" | "Expired". */
export type PixCharge = {
  id: string;
  groupId: string;
  playerProfileId: string;
  playerName: string;
  /** "Membership" | "MatchDay" */
  kind: string;
  membershipPaymentId: string | null;
  matchFeePaymentId: string | null;
  matchId: string | null;
  amount: number;
  description: string;
  mpPaymentId: string | null;
  /** Copia e Cola. Null na listagem do admin. */
  qrCode: string | null;
  qrCodeBase64: string | null;
  expiresAt: string;
  status: string;
  /** "Pending" | "Paid" | "Overdue" | "Exempt" | "Cancelled" */
  obligationStatus: string;
  isSimulated: boolean;
  reused: boolean;
  createdAt: string;
  approvedAt: string | null;
};

export type PixChargeSummary = {
  id: string;
  mpPaymentId: string | null;
  status: string;
  amount: number;
  expiresAt: string;
  createdAt: string;
  approvedAt: string | null;
  isSimulated: boolean;
};

export type MyMembershipPayment = {
  paymentId: string;
  monthlyMembershipId: string;
  billingPeriodId: string;
  periodLabel: string;
  year: number;
  month: number;
  amount: number;
  status: string;
  paymentSource: PaymentSource;
  paidAt: string | null;
  activePix: PixCharge | null;
};

export type MyMatchDayPayment = {
  matchFeeId: string | null;
  matchId: string;
  matchTitle: string | null;
  startsAt: string;
  location: string;
  amount: number;
  status: string;
  paymentSource: PaymentSource;
  paidAt: string | null;
  activePix: PixCharge | null;
};

export type MyPayments = {
  gateway: GatewayStatus;
  isMonthlyMember: boolean;
  currentPeriod: { year: number; month: number; label: string };
  memberships: MyMembershipPayment[];
  matchDays: MyMatchDayPayment[];
};

export type PixVerifyResult = { charge: PixCharge; obligationStatus: string; wasUpdated: boolean; message: string };

export type AdminMatchFee = {
  matchFeeId: string | null;
  matchId: string;
  playerProfileId: string;
  playerName: string;
  priorityClass: string;
  amount: number;
  status: string;
  paymentSource: PaymentSource;
  paidAt: string | null;
  notes: string | null;
  latestPix: PixChargeSummary | null;
};

export type PixAdminKpis = {
  pendingCount: number;
  approvedCount: number;
  expiredCount: number;
  cancelledCount: number;
  activeCount: number;
  paidByPixCount: number;
  paidManualCount: number;
  exemptCount: number;
  receivedPix: number;
  receivedManual: number;
  pendingAmount: number;
};

export type AdminPixOverview = { gateway: GatewayStatus; kpis: PixAdminKpis; charges: PixCharge[] };

export type PixReconcileResult = { checked: number; approved: number; expired: number; cancelled: number; stillPending: number; errors: number };

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
  /** Pênaltis defendidos (partidas ao vivo, contrato 2026-11-26). */
  penaltySaves?: number;
  bestGoalkeeper?: number;
};

// ---------- Pós-jogo de três cores (súmula) ----------

export type RoundSheetPayload = {
  presence: { playerProfileId: string; present: boolean; playedGoalkeeper?: boolean | null }[];
  games: { homeTeamId: string; awayTeamId: string; homeScore: number; awayScore: number }[];
  /** game = número do confronto (1, 2, 3...). */
  goals: { game: number; playerProfileId: string; ownGoal: boolean }[];
  bestPlayerProfileId?: string | null;
  bestGoalkeeperProfileId?: string | null;
  notes?: string | null;
  /** Obrigatório na correção. */
  reason?: string | null;
};

export type RoundTeam = {
  teamId: string;
  name: string;
  color: string | null;
  played: number;
  wins: number;
  draws: number;
  losses: number;
  goalsFor: number;
  goalsAgainst: number;
  points: number;
  isRoundWinner: boolean;
  winsRegular?: number;
  winsPenalties?: number;
};

export type RoundResult = {
  matchId: string;
  isLegacy: boolean;
  revision: number | null;
  isCurrentRevision: boolean;
  closedAt: string;
  localDate: string;
  teams: RoundTeam[];
  games: {
    order: number;
    homeTeamId: string;
    awayTeamId: string;
    homeScore: number;
    awayScore: number;
    outcome: string;
    goals: { playerProfileId: string; displayName: string; teamId: string; ownGoal: boolean; id?: string; teamCreditedId?: string }[];
    id?: string;
    status?: string;
    decidedByPenalties?: boolean;
    penaltyWinnerTeamId?: string | null;
    winnerTeamId?: string | null;
    penaltySaves?: PenaltySave[] | null;
  }[];
  participants: {
    playerProfileId: string;
    displayName: string;
    teamId: string;
    present: boolean;
    playedGoalkeeper: boolean;
    goals: number;
    ownGoals: number;
    gamesWon?: number;
    gamesWonOnPenalties?: number;
    penaltySaves?: number;
  }[];
  /** 1 = regra antiga (3/1 com empate); 2 = partidas ao vivo com pênaltis alternados. */
  rulesVersion?: number;
  bestPlayerProfileId: string | null;
  bestPlayerName: string | null;
  bestGoalkeeperProfileId: string | null;
  bestGoalkeeperName: string | null;
  notes: string | null;
  legacyHomeScore: number | null;
  legacyAwayScore: number | null;
  legacyWinnerTeamId: string | null;
  revisions: { revision: number; isCurrent: boolean; createdAt: string; reason: string | null }[];
};

export type Scorer = { playerProfileId: string; displayName: string; goals: number; position: number; isLeader: boolean };

export type SeasonStats = {
  year: number;
  month: number | null;
  timeZoneId: string;
  monthScorers: Scorer[];
  yearScorers: Scorer[];
  colors: {
    color: string;
    roundsWon: number;
    wins: number;
    draws: number;
    losses: number;
    goalsFor: number;
    goalsAgainst: number;
    winsRegular?: number;
    winsPenalties?: number;
  }[];
  players: {
    playerProfileId: string;
    displayName: string;
    roundsPlayed: number;
    roundsWon: number;
    gamesWon: number;
    goals: number;
    ownGoals: number;
    bestPlayer: number;
    bestGoalkeeper: number;
    gamesWonOnPenalties?: number;
    penaltySaves?: number;
  }[];
  rounds: { matchId: string; title: string | null; localDate: string; isLegacy: boolean; winnerColors: string[]; games: string[] }[];
};

// ---------- Partidas ao vivo (contrato 2026-11-26-contrato-api-partidas-ao-vivo.md, congelado) ----------

export type PenaltySave = { goalkeeperProfileId: string; displayName: string; count: number };

export type LiveGoal = { id: string; playerProfileId: string; displayName: string; ownGoal: boolean; teamCreditedId: string };

export type LiveGame = {
  id: string;
  order: number;
  /** "InProgress" | "Finished" */
  status: string;
  homeTeamId: string;
  awayTeamId: string;
  homeScore: number;
  awayScore: number;
  decidedByPenalties: boolean;
  goals: LiveGoal[];
  penaltyWinnerTeamId: string | null;
  /** Null só enquanto InProgress. */
  winnerTeamId: string | null;
  penaltySaves: PenaltySave[];
};

export type LiveStanding = {
  teamId: string;
  played: number;
  wins: number;
  winsRegular: number;
  winsPenalties: number;
  losses: number;
  goalsFor: number;
  goalsAgainst: number;
  isDayLeader: boolean;
};

export type LivePlayer = {
  playerProfileId: string;
  displayName: string;
  teamId: string;
  present: boolean;
  playedGoalkeeper: boolean;
  goals: number;
  ownGoals: number;
  gamesWon: number;
  gamesWonOnPenalties: number;
  penaltySaves: number;
  teamGoalsConceded: number | null;
  soleGoalkeeperForTeam: boolean;
};

export type LiveDay = {
  matchId: string;
  localDate: string;
  matchStatus: string;
  version: number;
  teams: { id: string; name: string; color: string | null }[];
  games: LiveGame[];
  standings: LiveStanding[];
  players: LivePlayer[];
  suggestedNext: { homeTeamId: string; awayTeamId: string } | null;
};

export type LiveGoalInput = { goalId: string; playerProfileId: string; ownGoal: boolean };
export type PenaltySaveInput = { goalkeeperProfileId: string; count: number };

/** Corpo da correção de dia fechado (PUT /result/round, súmula v2). */
export type RoundSheetV2 = {
  presence: { playerProfileId: string; present: boolean; playedGoalkeeper?: boolean | null }[];
  games: {
    id?: string;
    homeTeamId: string;
    awayTeamId: string;
    homeScore: number;
    awayScore: number;
    penaltyWinnerTeamId?: string | null;
    penaltySaves?: PenaltySaveInput[];
  }[];
  goals: { game: number; playerProfileId: string; ownGoal: boolean; id?: string }[];
  bestPlayerProfileId?: string | null;
  bestGoalkeeperProfileId?: string | null;
  notes?: string | null;
  reason?: string | null;
};

/** Erro da API com status e `code` do ProblemDetails (ex.: live_version_conflict, game_in_progress). */
export class ApiError extends Error {
  status: number;
  code: string | null;
  currentVersion: number | null;
  constructor(message: string, status: number, code: string | null, currentVersion: number | null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.currentVersion = currentVersion;
  }
}

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
    let code: string | null = null;
    let currentVersion: number | null = null;
    try {
      const body = await res.json();
      detail = problemMessage(body, detail);
      if (body && typeof body === "object") {
        const b = body as { code?: unknown; currentVersion?: unknown };
        if (typeof b.code === "string") code = b.code;
        if (typeof b.currentVersion === "number") currentVersion = b.currentVersion;
      }
    } catch {
      /* corpo vazio ou não JSON */
    }
    // ApiError estende Error: quem só lê `message` continua funcionando.
    throw new ApiError(detail, res.status, code, currentVersion);
  }
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

function url(path: string) {
  return `${API_BASE}${path}`;
}

const jsonHeaders = { "Content-Type": "application/json" };

function send<T>(method: string, path: string, body?: unknown) {
  return fetch(url(path), {
    method,
    credentials: "include",
    headers: body === undefined ? undefined : jsonHeaders,
    body: body === undefined ? undefined : JSON.stringify(body),
  }).then((r) => parseJson<T>(r));
}

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
  /** PUT /api/matches/{id} (contrato existente: UpdateMatchRequest). Só admin; partida encerrada não edita. */
  updateMatch(
    matchId: string,
    body: { title?: string | null; startsAt: string; location: string; playerLimit: number; notes?: string | null },
  ) {
    return fetch(url(`/api/matches/${matchId}`), {
      method: "PUT",
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
  // ---------- Partidas ao vivo ----------
  /** Leitura para membro ativo; dia Completed devolve o retrato final. */
  getLive(matchId: string) {
    return fetch(url(`/api/matches/${matchId}/live`), { credentials: "include" }).then((r) => parseJson<LiveDay>(r));
  },
  /** Sem insertAt: inicia jogo InProgress no fim. Com insertAt: insere jogo já encerrado. */
  startLiveGame(
    matchId: string,
    body: {
      homeTeamId: string;
      awayTeamId: string;
      expectedVersion: number;
      insertAt?: number;
      goals?: LiveGoalInput[];
      penaltyWinnerTeamId?: string | null;
      penaltySaves?: PenaltySaveInput[];
    },
  ) {
    return send<LiveDay>("POST", `/api/matches/${matchId}/live/games`, body);
  },
  /** Idempotente por goalId: repetir o mesmo goalId não duplica o gol. */
  addLiveGoal(matchId: string, gameId: string, body: LiveGoalInput & { expectedVersion: number }) {
    return send<LiveDay>("POST", `/api/matches/${matchId}/live/games/${gameId}/goals`, body);
  },
  removeLiveGoal(matchId: string, gameId: string, goalId: string, expectedVersion: number) {
    return send<LiveDay>("DELETE", `/api/matches/${matchId}/live/games/${gameId}/goals/${goalId}?expectedVersion=${expectedVersion}`);
  },
  finishLiveGame(matchId: string, gameId: string, body: { penaltyWinnerTeamId: string | null; penaltySaves: PenaltySaveInput[]; expectedVersion: number }) {
    return send<LiveDay>("POST", `/api/matches/${matchId}/live/games/${gameId}/finish`, body);
  },
  updateLiveGame(
    matchId: string,
    gameId: string,
    body: {
      homeTeamId: string;
      awayTeamId: string;
      status: string;
      goals: LiveGoalInput[];
      penaltyWinnerTeamId: string | null;
      penaltySaves: PenaltySaveInput[];
      expectedVersion: number;
    },
  ) {
    return send<LiveDay>("PUT", `/api/matches/${matchId}/live/games/${gameId}`, body);
  },
  deleteLiveGame(matchId: string, gameId: string, expectedVersion: number) {
    return send<LiveDay>("DELETE", `/api/matches/${matchId}/live/games/${gameId}?expectedVersion=${expectedVersion}`);
  },
  updateLivePresence(
    matchId: string,
    body: { expectedVersion: number; presence: { playerProfileId: string; present: boolean; playedGoalkeeper?: boolean | null }[] },
  ) {
    return send<LiveDay>("PUT", `/api/matches/${matchId}/live/presence`, body);
  },
  /** Fecha o dia a partir do rascunho ao vivo no servidor. */
  closeLiveDay(
    matchId: string,
    body: {
      expectedLiveVersion: number;
      presence?: { playerProfileId: string; present: boolean; playedGoalkeeper?: boolean | null }[];
      bestPlayerProfileId?: string | null;
      bestGoalkeeperProfileId?: string | null;
      notes?: string | null;
    },
  ) {
    return send<RoundResult>("POST", `/api/matches/${matchId}/result/round`, body);
  },
  /** Correção de dia fechado: súmula v2 completa + motivo (nova revisão). */
  correctRoundV2(matchId: string, body: RoundSheetV2) {
    return send<RoundResult>("PUT", `/api/matches/${matchId}/result/round`, body);
  },
  getRound(matchId: string, revision?: number) {
    const qs = revision ? `?revision=${revision}` : "";
    return fetch(url(`/api/matches/${matchId}/result/round${qs}`), { credentials: "include" }).then((r) => {
      if (r.status === 404) return null;
      return parseJson<RoundResult>(r);
    });
  },
  closeRound(matchId: string, body: RoundSheetPayload) {
    return fetch(url(`/api/matches/${matchId}/result/round`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<RoundResult>(r));
  },
  correctRound(matchId: string, body: RoundSheetPayload) {
    return fetch(url(`/api/matches/${matchId}/result/round`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<RoundResult>(r));
  },
  season(groupId: string, year?: number, month?: number | null) {
    const qs = new URLSearchParams();
    if (year) qs.set("year", String(year));
    if (month) qs.set("month", String(month));
    return fetch(url(`/api/groups/${groupId}/season?${qs}`), { credentials: "include" }).then((r) => parseJson<SeasonStats>(r));
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
          /** Partidas ao vivo: pênaltis defendidos no dia e se atuou no gol. */
          penaltySaves?: number;
          playedGoalkeeper?: boolean;
        }[];
      }>(r),
    );
  },

  // ---------- Pix Mercado Pago ----------
  myPayments(groupId: string) {
    return fetch(url(`/api/groups/${groupId}/my-payments`), { credentials: "include" }).then((r) => parseJson<MyPayments>(r));
  },
  /** Gera ou reaproveita o Pix da mensalidade (409 se já paga). */
  createMembershipPix(paymentId: string) {
    return fetch(url(`/api/payments/mensalidades/${paymentId}/pix`), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<PixCharge>(r),
    );
  },
  /** Gera ou reaproveita o Pix da diária. playerProfileId/amount só valem para admin. */
  createMatchDayPix(matchId: string, body: { playerProfileId?: string | null; amount?: number | null } = {}) {
    return fetch(url(`/api/payments/diarias/${matchId}/pix`), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<PixCharge>(r));
  },
  /** Polling: consulta só o banco local. */
  getPixCharge(pixChargeId: string) {
    return fetch(url(`/api/payments/pix/${pixChargeId}`), { credentials: "include" }).then((r) => parseJson<PixCharge>(r));
  },
  /** "Já paguei / Verificar": consulta o Mercado Pago na hora. */
  verifyPix(pixChargeId: string) {
    return fetch(url(`/api/payments/pix/${pixChargeId}/verify`), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<PixVerifyResult>(r),
    );
  },
  /** Só admin e só em modo Simulator (400 em Live). */
  simulateApprovePix(pixChargeId: string) {
    return fetch(url(`/api/payments/pix/${pixChargeId}/simulate-approve`), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<PixVerifyResult>(r),
    );
  },
  paymentSettings(groupId: string) {
    return fetch(url(`/api/groups/${groupId}/payments/settings`), { credentials: "include" }).then((r) => parseJson<GatewayStatus>(r));
  },
  updatePaymentSettings(groupId: string, body: { defaultMonthlyFee: number; defaultMatchDayFee: number; pixExpirationMinutes: number }) {
    return fetch(url(`/api/groups/${groupId}/payments/settings`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<GatewayStatus>(r));
  },
  matchFees(groupId: string, matchId: string) {
    return fetch(url(`/api/groups/${groupId}/matches/${matchId}/fees`), { credentials: "include" }).then((r) =>
      parseJson<AdminMatchFee[]>(r),
    );
  },
  updateMatchFee(groupId: string, matchId: string, playerProfileId: string, body: { amount: number; status: string; notes?: string | null }) {
    return fetch(url(`/api/groups/${groupId}/matches/${matchId}/fees/${playerProfileId}`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    }).then((r) => parseJson<AdminMatchFee>(r));
  },
  pixOverview(groupId: string, filters: { status?: string; kind?: string; year?: number; month?: number; matchId?: string } = {}) {
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(filters)) if (v !== undefined && v !== "") qs.set(k, String(v));
    return fetch(url(`/api/groups/${groupId}/payments/pix?${qs}`), { credentials: "include" }).then((r) => parseJson<AdminPixOverview>(r));
  },
  reconcilePix(groupId: string) {
    return fetch(url(`/api/groups/${groupId}/payments/pix/reconcile`), { method: "POST", credentials: "include" }).then((r) =>
      parseJson<PixReconcileResult>(r),
    );
  },
};
