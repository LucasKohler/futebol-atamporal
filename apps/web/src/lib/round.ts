import type { Draw, RoundResult, RoundSheetPayload } from "./api";

/** Jogador escalado no sorteio confirmado. */
export type RosterPlayer = { playerProfileId: string; displayName: string; teamId: string; isGoalkeeper: boolean };
export type PresenceState = Record<string, { present: boolean; playedGoalkeeper: boolean }>;
export type GoalForm = { playerProfileId: string; ownGoal: boolean };
export type GameForm = { homeTeamId: string; awayTeamId: string; homeScore: number; awayScore: number; goals: GoalForm[] };

export type SheetForm = {
  presence: PresenceState;
  games: GameForm[];
  bestPlayerProfileId: string;
  bestGoalkeeperProfileId: string;
  notes: string;
  reason: string;
};

export function rosterFromDraw(draw: Draw): RosterPlayer[] {
  return draw.teams.flatMap((t) =>
    t.members.map((m) => ({
      playerProfileId: m.playerProfileId,
      displayName: m.displayName,
      teamId: t.id,
      isGoalkeeper: m.role === "Goalkeeper",
    })),
  );
}

export function emptySheet(roster: RosterPlayer[]): SheetForm {
  return {
    presence: Object.fromEntries(roster.map((p) => [p.playerProfileId, { present: true, playedGoalkeeper: p.isGoalkeeper }])),
    games: [],
    bestPlayerProfileId: "",
    bestGoalkeeperProfileId: "",
    notes: "",
    reason: "",
  };
}

/** Pré-preenche a correção com a revisão vigente. */
export function sheetFromRound(round: RoundResult): SheetForm {
  return {
    presence: Object.fromEntries(
      round.participants.map((p) => [p.playerProfileId, { present: p.present, playedGoalkeeper: p.playedGoalkeeper }]),
    ),
    games: round.games.map((g) => ({
      homeTeamId: g.homeTeamId,
      awayTeamId: g.awayTeamId,
      homeScore: g.homeScore,
      awayScore: g.awayScore,
      goals: g.goals.map((x) => ({ playerProfileId: x.playerProfileId, ownGoal: x.ownGoal })),
    })),
    bestPlayerProfileId: round.bestPlayerProfileId ?? "",
    bestGoalkeeperProfileId: round.bestGoalkeeperProfileId ?? "",
    notes: round.notes ?? "",
    reason: "",
  };
}

/** Gols por lado: gols normais do próprio time + gols contra do adversário (mesma regra da API). */
export function creditedScore(game: GameForm, roster: RosterPlayer[]): { home: number; away: number } {
  const teamOf = new Map(roster.map((p) => [p.playerProfileId, p.teamId]));
  let home = 0;
  let away = 0;
  for (const goal of game.goals) {
    const team = teamOf.get(goal.playerProfileId);
    const toHome = goal.ownGoal ? team === game.awayTeamId : team === game.homeTeamId;
    if (toHome) home++;
    else away++;
  }
  return { home, away };
}

/** Problemas de um confronto antes de enviar (a API valida de novo). */
export function gameIssues(game: GameForm, roster: RosterPlayer[], presence: PresenceState): string[] {
  const issues: string[] = [];
  if (!game.homeTeamId || !game.awayTeamId) issues.push("Escolha as duas cores.");
  else if (game.homeTeamId === game.awayTeamId) issues.push("Escolha duas cores diferentes.");
  const teamOf = new Map(roster.map((p) => [p.playerProfileId, p.teamId]));
  for (const goal of game.goals) {
    if (!goal.playerProfileId) {
      issues.push("Escolha o autor de cada gol.");
      continue;
    }
    if (presence[goal.playerProfileId] && !presence[goal.playerProfileId].present) issues.push("Gol de jogador marcado ausente.");
    const team = teamOf.get(goal.playerProfileId);
    if (team !== game.homeTeamId && team !== game.awayTeamId) issues.push("Autor do gol não joga nesse confronto.");
  }
  const { home, away } = creditedScore(game, roster);
  if (home !== game.homeScore || away !== game.awayScore)
    issues.push(`Gols lançados (${home} x ${away}) não fecham o placar (${game.homeScore} x ${game.awayScore}).`);
  return [...new Set(issues)];
}

export type StandingPreview = { teamId: string; played: number; wins: number; draws: number; losses: number; goalsFor: number; goalsAgainst: number; points: number; isRoundWinner: boolean };

/** Classificação da rodada: 3 por vitória, 1 por empate; desempate por saldo e gols pró; empate persistente divide. */
export function previewStandings(games: GameForm[], teamIds: string[]): StandingPreview[] {
  const rows = new Map(teamIds.map((t) => [t, { teamId: t, played: 0, wins: 0, draws: 0, losses: 0, goalsFor: 0, goalsAgainst: 0, points: 0, isRoundWinner: false }]));
  for (const g of games) {
    const h = rows.get(g.homeTeamId);
    const a = rows.get(g.awayTeamId);
    if (!h || !a || h === a) continue;
    h.played++;
    a.played++;
    h.goalsFor += g.homeScore;
    h.goalsAgainst += g.awayScore;
    a.goalsFor += g.awayScore;
    a.goalsAgainst += g.homeScore;
    if (g.homeScore > g.awayScore) {
      h.wins++;
      a.losses++;
    } else if (g.homeScore < g.awayScore) {
      a.wins++;
      h.losses++;
    } else {
      h.draws++;
      a.draws++;
    }
  }
  const list = [...rows.values()].map((r) => ({ ...r, points: r.wins * 3 + r.draws }));
  const key = (r: StandingPreview) => [r.points, r.goalsFor - r.goalsAgainst, r.goalsFor];
  const best = [...list].sort((x, y) => {
    const [a, b] = [key(x), key(y)];
    return b[0] - a[0] || b[1] - a[1] || b[2] - a[2];
  })[0];
  return list.map((r) => ({
    ...r,
    isRoundWinner: !!best && r.played > 0 && key(r).every((v, i) => v === key(best)[i]),
  }));
}

export function buildPayload(form: SheetForm, roster: RosterPlayer[]): RoundSheetPayload {
  return {
    presence: roster.map((p) => ({
      playerProfileId: p.playerProfileId,
      present: form.presence[p.playerProfileId]?.present ?? true,
      playedGoalkeeper: form.presence[p.playerProfileId]?.playedGoalkeeper ?? p.isGoalkeeper,
    })),
    games: form.games.map((g) => ({ homeTeamId: g.homeTeamId, awayTeamId: g.awayTeamId, homeScore: g.homeScore, awayScore: g.awayScore })),
    goals: form.games.flatMap((g, i) => g.goals.map((x) => ({ game: i + 1, playerProfileId: x.playerProfileId, ownGoal: x.ownGoal }))),
    bestPlayerProfileId: form.bestPlayerProfileId || null,
    bestGoalkeeperProfileId: form.bestGoalkeeperProfileId || null,
    notes: form.notes.trim() || null,
    reason: form.reason.trim() || null,
  };
}

/** Candidatos a Melhor Jogador (presentes) e Melhor Goleiro (presentes que atuaram no gol). */
export function awardCandidates(roster: RosterPlayer[], presence: PresenceState) {
  const present = roster.filter((p) => presence[p.playerProfileId]?.present ?? true);
  return {
    bestPlayer: present,
    bestGoalkeeper: present.filter((p) => presence[p.playerProfileId]?.playedGoalkeeper ?? p.isGoalkeeper),
  };
}

/** "Vermelho 2 x 1 Preto" */
export function gameLabel(game: Pick<GameForm, "homeTeamId" | "awayTeamId" | "homeScore" | "awayScore">, colorOf: (teamId: string) => string) {
  return `${colorOf(game.homeTeamId)} ${game.homeScore} x ${game.awayScore} ${colorOf(game.awayTeamId)}`;
}

export const MAX_SCORE = 99;

/** Placar limitado a 0..99 (mesmo limite do campo numérico). */
export function clampScore(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(MAX_SCORE, Math.trunc(value)));
}

export type SideProgress = { credited: number; score: number; missing: number; extra: number; ok: boolean };

/** Gols atribuídos x placar, por lado do confronto (para o indicador "Gols atribuídos: X/Y"). */
export function goalProgress(game: GameForm, roster: RosterPlayer[]): { home: SideProgress; away: SideProgress; ok: boolean } {
  const credited = creditedScore(game, roster);
  const side = (c: number, score: number): SideProgress => ({
    credited: c,
    score,
    missing: Math.max(0, score - c),
    extra: Math.max(0, c - score),
    ok: c === score,
  });
  const home = side(credited.home, game.homeScore);
  const away = side(credited.away, game.awayScore);
  return { home, away, ok: home.ok && away.ok };
}

/**
 * Gol rápido por chip: adiciona o gol e, se o placar daquele lado ficaria menor que os gols atribuídos,
 * sobe o placar junto (um toque = um gol). Gol contra conta para o adversário de quem fez.
 */
export function addQuickGoal(game: GameForm, roster: RosterPlayer[], playerProfileId: string, ownGoal: boolean): GameForm {
  const next: GameForm = { ...game, goals: [...game.goals, { playerProfileId, ownGoal }] };
  const credited = creditedScore(next, roster);
  return {
    ...next,
    homeScore: clampScore(Math.max(next.homeScore, credited.home)),
    awayScore: clampScore(Math.max(next.awayScore, credited.away)),
  };
}

/** Marca todos (ou só os de um time) como presentes, mantendo quem atuou no gol. */
export function markAllPresent(presence: PresenceState, roster: RosterPlayer[], teamId?: string): PresenceState {
  const next = { ...presence };
  for (const p of roster) {
    if (teamId && p.teamId !== teamId) continue;
    next[p.playerProfileId] = { playedGoalkeeper: presence[p.playerProfileId]?.playedGoalkeeper ?? p.isGoalkeeper, present: true };
  }
  return next;
}

/** Remove prêmios que deixaram de ser elegíveis (ex.: premiado marcado ausente ou fora do gol). */
export function pruneAwards(form: SheetForm, roster: RosterPlayer[]): SheetForm {
  const c = awardCandidates(roster, form.presence);
  const ok = (id: string, list: RosterPlayer[]) => !id || list.some((p) => p.playerProfileId === id);
  return {
    ...form,
    bestPlayerProfileId: ok(form.bestPlayerProfileId, c.bestPlayer) ? form.bestPlayerProfileId : "",
    bestGoalkeeperProfileId: ok(form.bestGoalkeeperProfileId, c.bestGoalkeeper) ? form.bestGoalkeeperProfileId : "",
  };
}
