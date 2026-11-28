import { withAuthCookieLock } from "./authLock";

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
  /** "Pending" | "Paid" | "Overdue" | "Exempt" | "Cancelled" | "RefundPending" | "Refunded" | "NotCharged" (diária: rodada ainda não fechada) */
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
  /**
   * Vencimento (meia-noite seguinte ao fechamento da rodada, fuso do grupo). Null = cobrança ainda
   * não lançada no fechamento (ou diária anterior ao contrato de 2026-11-27). Aditivo; ausente em APIs antigas.
   */
  dueAt?: string | null;
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
  /** Vencimento da diária lançada no fechamento (aditivo, contrato 2026-11-27). */
  dueAt?: string | null;
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
  /** 409 reminder_cooldown: segundos até poder repetir o lembrete. */
  retryAfterSeconds: number | null;
  constructor(message: string, status: number, code: string | null, currentVersion: number | null, retryAfterSeconds: number | null = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
    this.currentVersion = currentVersion;
    this.retryAfterSeconds = retryAfterSeconds;
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
    let retryAfterSeconds: number | null = null;
    try {
      const body = await res.json();
      detail = problemMessage(body, detail);
      if (body && typeof body === "object") {
        const b = body as { code?: unknown; currentVersion?: unknown; retryAfterSeconds?: unknown };
        if (typeof b.code === "string") code = b.code;
        if (typeof b.currentVersion === "number") currentVersion = b.currentVersion;
        if (typeof b.retryAfterSeconds === "number") retryAfterSeconds = b.retryAfterSeconds;
      }
    } catch {
      /* corpo vazio ou não JSON */
    }
    // ApiError estende Error: quem só lê `message` continua funcionando.
    throw new ApiError(detail, res.status, code, currentVersion, retryAfterSeconds);
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
  /** Telefone + senha (contrato backend 2026-11-27). Erro genérico: não revela se o celular existe. */
  login(phone: string, password: string) {
    // Grava o cookie de sessão: serializado entre abas com o logout (ver authLock).
    return withAuthCookieLock(() => fetch(url("/api/auth/login"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ phone, password }),
    }).then((r) => parseJson<User>(r)));
  },
  register(phone: string, displayName: string, password: string) {
    // Grava o cookie de sessão: serializado entre abas com o logout (ver authLock).
    return withAuthCookieLock(() => fetch(url("/api/auth/register"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ phone, displayName, password }),
    }).then((r) => parseJson<User>(r)));
  },
  /**
   * Configuração inicial do primeiro admin (uso único): código guardado só no servidor + celular do admin + senha.
   * 200 abre a sessão; depois disso o código deixa de valer (409 setup_already_done).
   */
  adminSetup(setupCode: string, phone: string, password: string) {
    // Grava o cookie de sessão: serializado entre abas com o logout (ver authLock).
    return withAuthCookieLock(() => fetch(url("/api/auth/admin-setup"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ setupCode, phone, password }),
    }).then((r) => parseJson<User>(r)));
  },
  /**
   * Recuperação do único admin (decisão do Lucas): código temporário de uso único definido pelo operador no servidor,
   * válido por no máximo 10 minutos. 200 abre a sessão e encerra as sessões antigas do admin.
   */
  adminRecovery(recoveryCode: string, phone: string, newPassword: string) {
    // Grava o cookie de sessão: serializado entre abas com o logout (ver authLock).
    return withAuthCookieLock(() => fetch(url("/api/auth/admin-recovery"), {
      method: "POST",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ recoveryCode, phone, newPassword }),
    }).then((r) => parseJson<User>(r)));
  },
  /** Recuperação de senha: o admin do grupo define a nova senha do jogador (204, sem corpo). */
  resetPlayerPassword(groupId: string, playerProfileId: string, newPassword: string) {
    return fetch(url(`/api/groups/${groupId}/players/${playerProfileId}/password`), {
      method: "PUT",
      headers: jsonHeaders,
      credentials: "include",
      body: JSON.stringify({ newPassword }),
    }).then((r) => parseJson<void>(r));
  },
  /**
   * Encerra a sessão. Com `expectedUserId`, a API só sai se o cookie ainda for dessa conta; senão responde
   * 409 `session_changed` e mantém a sessão (outra conta entrou em outra aba).
   */
  logout(expectedUserId?: string) {
    // Serializado entre abas com login/cadastro: a expiração do cookie não pode chegar depois do login de outra conta.
    return withAuthCookieLock(() =>
      fetch(url("/api/auth/logout"), {
        method: "POST",
        credentials: "include",
        ...(expectedUserId ? { headers: jsonHeaders, body: JSON.stringify({ expectedUserId }) } : {}),
      }).then((r) => parseJson<void>(r)),
    );
  },
  /** Sessão atual. Sem cache: a resposta precisa refletir logout/login recentes (usada para vincular o push à conta). */
  me() {
    return fetch(url("/api/auth/me"), { credentials: "include", cache: "no-store" }).then((r) => {
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

  // ---------- Notificações e push (contrato 2026-11-26-contrato-api-pwa-notificacoes.md) ----------
  /** Anônima. 503 push_not_configured quando o push está desligado no servidor. */
  vapidPublicKey() {
    return fetch(url("/api/notifications/vapid-public-key"), { credentials: "include" }).then((r) => parseJson<VapidPublicKey>(r));
  },
  /** Insere ou atualiza pelo endpoint; chamar a cada início do app com permissão concedida. */
  /**
   * Registra a inscrição para o usuário logado. `expectedUserId` é a conta cuja intenção iniciou a gravação:
   * a API exige que seja a mesma do cookie e, se não for, responde 409 `session_changed` sem gravar nada.
   */
  subscribePush(body: {
    endpoint: string;
    keys: { p256dh: string; auth: string };
    userAgent?: string;
    deviceLabel?: string;
    expectedUserId: string;
  }) {
    return send<{ id: string; isActive: boolean; keyId: string }>("POST", "/api/notifications/subscriptions", body);
  },
  /** 204 mesmo se não existir. */
  /**
   * Remove a inscrição do usuário logado. `expectedUserId` é obrigatório: se não for a conta do cookie, a API
   * responde 409 `session_changed` sem remover nada. 204 também quando não havia inscrição dessa conta.
   */
  async unsubscribePush(endpoint: string, expectedUserId: string): Promise<void> {
    const r = await fetch(url("/api/notifications/subscriptions"), {
      method: "DELETE",
      credentials: "include",
      headers: jsonHeaders,
      body: JSON.stringify({ endpoint, expectedUserId }),
    });
    // Só 204 confirma a remoção. Erros viram ApiError; qualquer outro sucesso (200, 202...) também não confirma.
    if (r.status === 204) return;
    await parseJson<unknown>(r);
    throw new ApiError("O servidor não confirmou a remoção da inscrição.", r.status, "unexpected_status", null);
  },
  myNotifications(groupId: string, opts: { before?: string | null; limit?: number } = {}) {
    const qs = new URLSearchParams({ groupId });
    if (opts.before) qs.set("before", opts.before);
    if (opts.limit) qs.set("limit", String(opts.limit));
    return fetch(url(`/api/notifications/me?${qs}`), { credentials: "include" }).then((r) => parseJson<MyNotifications>(r));
  },
  readNotification(id: string) {
    return send<void>("POST", `/api/notifications/${id}/read`);
  },
  readAllNotifications(groupId: string) {
    return send<{ updated: number }>("POST", "/api/notifications/read-all", { groupId });
  },
  adminNotificationSummary(groupId: string) {
    return fetch(url(`/api/groups/${groupId}/admin/notifications/summary`), { credentials: "include" }).then((r) =>
      parseJson<AdminNotificationSummary>(r),
    );
  },
  /** Não grava nada: só conta destinatários e aparelhos. */
  adminNotificationPreview(groupId: string, body: { audience?: string; quick?: QuickReminderKind }) {
    return send<{ recipients: number; pushDevices: number; matchId: string | null }>("POST", `/api/groups/${groupId}/admin/notifications/preview`, body);
  },
  /** Idempotency-Key: o mesmo valor em reenvios (clique duplo) cria uma campanha só. */
  adminQuickReminder(groupId: string, body: { kind: QuickReminderKind; audience?: "NotResponded" | "AllMembers" }, idempotencyKey: string) {
    return sendWithKey<NotificationCampaign>("POST", `/api/groups/${groupId}/admin/notifications/quick`, body, idempotencyKey);
  },
  adminBroadcast(
    groupId: string,
    body: { audience: BroadcastAudience; title: string; body: string; actionUrl: NotificationActionUrl },
    idempotencyKey: string,
  ) {
    return sendWithKey<NotificationCampaign>("POST", `/api/groups/${groupId}/admin/notifications/broadcast`, body, idempotencyKey);
  },
  adminCampaigns(groupId: string, page = 1, pageSize = 20) {
    return fetch(url(`/api/groups/${groupId}/admin/notifications/campaigns?page=${page}&pageSize=${pageSize}`), { credentials: "include" }).then((r) =>
      parseJson<{ items: NotificationCampaign[]; total: number }>(r),
    );
  },
};

function sendWithKey<T>(method: string, path: string, body: unknown, idempotencyKey: string) {
  return fetch(url(path), {
    method,
    credentials: "include",
    headers: { ...jsonHeaders, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(body),
  }).then((r) => parseJson<T>(r));
}

// ---------- Tipos de notificação ----------

export type NotificationActionUrl = "/" | "/partida" | "/pagamentos" | "/rankings" | "/perfil";

/** category: MatchOpen | WaitlistPromoted | DrawPublished | MatchCompleted | RegistrationReminder | PaymentReminder | AdminBroadcast */
export type AppNotification = {
  id: string;
  groupId: string;
  category: string;
  title: string;
  body: string;
  actionUrl: NotificationActionUrl;
  matchId: string | null;
  createdAt: string;
  readAt: string | null;
};

export type PushDevice = { id: string; deviceLabel: string | null; createdAt: string; lastSuccessAt: string | null; isActive: boolean; stale: boolean };

export type MyNotifications = {
  items: AppNotification[];
  unreadCount: number;
  nextCursor: string | null;
  /** mode: "Live" | "Simulator" | "Disabled" */
  push: { mode: string; keyId: string | null };
  devices: PushDevice[];
};

export type VapidPublicKey = { publicKey: string; keyId: string };

export type QuickReminderKind = "RegistrationReminder" | "PaymentReminder" | "MatchAvailable";
export type BroadcastAudience = "AllMembers" | "MatchConfirmed" | "PaymentPending";

export type AdminNotificationSummary = {
  membersActive: number;
  membersWithPush: number;
  activeDevices: number;
  sentLast30d: number;
  pushMode: string;
  lastCampaignAt: string | null;
};

export type NotificationCampaign = {
  id: string;
  /** "Automatic" | "AdminQuick" | "AdminBroadcast" */
  source: string;
  category: string;
  audience: string;
  title: string;
  actionUrl: string;
  createdAt: string;
  createdByName: string | null;
  recipientCount: number;
  pushDeviceCount: number;
  /** Aceitos pelo serviço de push ("enviados"); não há confirmação de entrega no aparelho. */
  pushSentCount: number;
  pushFailedCount: number;
};
