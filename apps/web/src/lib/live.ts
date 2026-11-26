import { ApiError, type LiveDay, type LiveGame, type LiveGoalInput, type PenaltySaveInput, type RoundResult, type RoundSheetV2 } from "./api";

/** Defesas de pênalti por goleiro num jogo: 1 a 10 (regra D4). */
export const MAX_SAVES_PER_GAME = 10;

export function currentGame(day: LiveDay): LiveGame | null {
  return day.games.find((g) => g.status === "InProgress") ?? null;
}

export function finishedGames(day: LiveDay): LiveGame[] {
  return [...day.games].filter((g) => g.status === "Finished").sort((a, b) => a.order - b.order);
}

/** Próximo confronto: sugestão do servidor ("quem ganha fica") ou as duas primeiras cores. */
export function defaultMatchup(day: LiveDay): { homeTeamId: string; awayTeamId: string } {
  if (day.suggestedNext) return day.suggestedNext;
  return { homeTeamId: day.teams[0]?.id ?? "", awayTeamId: day.teams[1]?.id ?? "" };
}

/** Cor que fica de fora do confronto (3 cores). */
export function waitingTeamId(day: LiveDay, homeTeamId: string, awayTeamId: string): string | null {
  return day.teams.find((t) => t.id !== homeTeamId && t.id !== awayTeamId)?.id ?? null;
}

/** Último gol creditado a um lado (o "−" remove esse gol). */
export function lastGoalForSide(game: LiveGame, teamId: string) {
  for (let i = game.goals.length - 1; i >= 0; i--) if (game.goals[i].teamCreditedId === teamId) return game.goals[i];
  return null;
}

/** Goleiros elegíveis a defesa de pênalti no jogo: presentes, que atuaram no gol, de uma das duas cores. */
export function goalkeepersForGame(day: LiveDay, game: Pick<LiveGame, "homeTeamId" | "awayTeamId">) {
  return day.players.filter((p) => p.present && p.playedGoalkeeper && (p.teamId === game.homeTeamId || p.teamId === game.awayTeamId));
}

export function clampSaves(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(MAX_SAVES_PER_GAME, Math.trunc(n)));
}

/** Mapa local {goleiro: defesas} → corpo da API (só quem tem 1+). */
export function savesToInput(saves: Record<string, number>): PenaltySaveInput[] {
  return Object.entries(saves)
    .map(([goalkeeperProfileId, count]) => ({ goalkeeperProfileId, count: clampSaves(count) }))
    .filter((s) => s.count > 0);
}

/**
 * 🧤 ao vivo: aplica +1/−1 sobre o valor salvo no servidor (não sobre um rascunho local), para que o toque
 * grave na hora e sobreviva a recarga; recalcular a partir da súmula mais nova evita perder a defesa de outro aparelho.
 */
export function applySaveDelta(game: Pick<LiveGame, "penaltySaves">, goalkeeperProfileId: string, delta: number): PenaltySaveInput[] {
  const saves = savesFromGame(game);
  saves[goalkeeperProfileId] = clampSaves((saves[goalkeeperProfileId] ?? 0) + delta);
  return savesToInput(saves);
}

/** Corpo do PUT que regrava o jogo em andamento só mudando as defesas (gols e cores preservados pelos ids). */
export function inProgressSavesBody(game: LiveGame, penaltySaves: PenaltySaveInput[], expectedVersion: number) {
  return {
    homeTeamId: game.homeTeamId,
    awayTeamId: game.awayTeamId,
    status: "InProgress",
    goals: game.goals.map((g) => ({ goalId: g.id, playerProfileId: g.playerProfileId, ownGoal: g.ownGoal })),
    // A API só aceita vencedor nos pênaltis ao encerrar.
    penaltyWinnerTeamId: null,
    penaltySaves,
    expectedVersion,
  };
}

export function savesFromGame(game: Pick<LiveGame, "penaltySaves">): Record<string, number> {
  return Object.fromEntries(game.penaltySaves.map((s) => [s.goalkeeperProfileId, s.count]));
}

/** Pode encerrar? Empate exige vencedor nos pênaltis entre as duas cores; jogo sem empate não pode ter. */
export function finishIssue(homeScore: number, awayScore: number, game: { homeTeamId: string; awayTeamId: string }, penaltyWinnerTeamId: string | null): string | null {
  if (homeScore === awayScore) {
    if (!penaltyWinnerTeamId) return "Empate: escolha quem ganhou nos pênaltis.";
    if (penaltyWinnerTeamId !== game.homeTeamId && penaltyWinnerTeamId !== game.awayTeamId) return "O vencedor nos pênaltis precisa ser uma das duas cores.";
  }
  return null;
}

/** Vencedor nos pênaltis só vai para a API quando há empate. */
export function penaltyWinnerFor(homeScore: number, awayScore: number, chosen: string | null): string | null {
  return homeScore === awayScore ? chosen : null;
}

/** Placar a partir dos gols (D3): gol normal conta para a cor do autor; gol contra, para a adversária. */
export function scoreFromGoals(goals: LiveGoalInput[], homeTeamId: string, awayTeamId: string, teamOf: (playerProfileId: string) => string | undefined) {
  let home = 0;
  let away = 0;
  for (const g of goals) {
    const team = teamOf(g.playerProfileId);
    if (!team) continue;
    const credited = g.ownGoal ? (team === homeTeamId ? awayTeamId : team === awayTeamId ? homeTeamId : null) : team;
    if (credited === homeTeamId) home++;
    else if (credited === awayTeamId) away++;
  }
  return { home, away };
}

/** Gols que não valem no confronto (autor de outra cor ou sem autor). */
export function invalidGoals(goals: LiveGoalInput[], homeTeamId: string, awayTeamId: string, teamOf: (id: string) => string | undefined) {
  return goals.filter((g) => {
    const t = teamOf(g.playerProfileId);
    return !g.playerProfileId || (t !== homeTeamId && t !== awayTeamId);
  });
}

export type GameDraft = {
  homeTeamId: string;
  awayTeamId: string;
  goals: LiveGoalInput[];
  penaltyWinnerTeamId: string | null;
  saves: Record<string, number>;
};

export function draftFromGame(game: LiveGame): GameDraft {
  return {
    homeTeamId: game.homeTeamId,
    awayTeamId: game.awayTeamId,
    goals: game.goals.map((g) => ({ goalId: g.id, playerProfileId: g.playerProfileId, ownGoal: g.ownGoal })),
    penaltyWinnerTeamId: game.penaltyWinnerTeamId,
    saves: savesFromGame(game),
  };
}

/** Problemas de um rascunho de jogo encerrado antes de enviar (a API valida de novo). */
export function draftIssues(draft: GameDraft, day: LiveDay): string[] {
  const issues: string[] = [];
  if (!draft.homeTeamId || !draft.awayTeamId || draft.homeTeamId === draft.awayTeamId) issues.push("Escolha duas cores diferentes.");
  const teamOf = teamLookup(day);
  if (invalidGoals(draft.goals, draft.homeTeamId, draft.awayTeamId, teamOf).length > 0) issues.push("Todo gol precisa de autor de uma das duas cores.");
  const { home, away } = scoreFromGoals(draft.goals, draft.homeTeamId, draft.awayTeamId, teamOf);
  const pen = finishIssue(home, away, draft, draft.penaltyWinnerTeamId);
  if (pen) issues.push(pen);
  const keepers = new Set(goalkeepersForGame(day, draft).map((p) => p.playerProfileId));
  if (savesToInput(draft.saves).some((s) => !keepers.has(s.goalkeeperProfileId))) issues.push("Defesa de pênalti só para quem atuou no gol nessas cores.");
  return issues;
}

export function teamLookup(day: LiveDay) {
  const map = new Map(day.players.map((p) => [p.playerProfileId, p.teamId]));
  return (id: string) => map.get(id);
}

/**
 * Dia fechado: monta a súmula v2 completa para a correção (PUT /result/round), aplicando a edição local
 * de um jogo (ou exclusão) sobre o retrato atual. Placar sempre derivado dos gols.
 */
export function buildCorrectionSheet(
  day: LiveDay,
  edits: Record<string, GameDraft | "deleted">,
  round: Pick<RoundResult, "bestPlayerProfileId" | "bestGoalkeeperProfileId" | "notes"> | null,
  reason: string,
): RoundSheetV2 {
  const teamOf = teamLookup(day);
  const games = finishedGames(day)
    .filter((g) => edits[g.id] !== "deleted")
    .map((g) => {
      const e = edits[g.id];
      return { id: g.id, draft: e && e !== "deleted" ? e : draftFromGame(g) };
    });
  return {
    presence: day.players.map((p) => ({ playerProfileId: p.playerProfileId, present: p.present, playedGoalkeeper: p.playedGoalkeeper })),
    games: games.map(({ id, draft }) => {
      const { home, away } = scoreFromGoals(draft.goals, draft.homeTeamId, draft.awayTeamId, teamOf);
      return {
        id,
        homeTeamId: draft.homeTeamId,
        awayTeamId: draft.awayTeamId,
        homeScore: home,
        awayScore: away,
        penaltyWinnerTeamId: penaltyWinnerFor(home, away, draft.penaltyWinnerTeamId),
        penaltySaves: savesToInput(draft.saves),
      };
    }),
    goals: games.flatMap(({ draft }, i) => draft.goals.map((g) => ({ game: i + 1, playerProfileId: g.playerProfileId, ownGoal: g.ownGoal, id: g.goalId }))),
    bestPlayerProfileId: round?.bestPlayerProfileId ?? null,
    bestGoalkeeperProfileId: round?.bestGoalkeeperProfileId ?? null,
    notes: round?.notes ?? null,
    reason: reason.trim(),
  };
}

/** Classificação do dia na ordem da regra A (vitórias, vitórias no tempo normal, saldo, gols pró). */
export function sortedStandings(day: LiveDay) {
  return [...day.standings].sort(
    (a, b) =>
      b.wins - a.wins ||
      b.winsRegular - a.winsRegular ||
      b.goalsFor - b.goalsAgainst - (a.goalsFor - a.goalsAgainst) ||
      b.goalsFor - a.goalsFor,
  );
}

/** Por que ainda não dá para concluir o dia (null = pode). */
export function closeDayIssue(day: LiveDay): string | null {
  if (day.matchStatus === "Completed") return "O dia já foi concluído. Para corrigir, use a Checagem do Dia com motivo.";
  if (finishedGames(day).length === 0) return "Lance ao menos uma partida antes de concluir o dia.";
  const open = currentGame(day);
  if (open) return `Finalize a Partida #${open.order} antes de concluir o dia.`;
  return null;
}

export function isVersionConflict(e: unknown): boolean {
  return e instanceof ApiError && e.code === "live_version_conflict";
}

/** Id do gol gerado no aparelho: tocar duas vezes não duplica (idempotência por goalId). */
export function newGoalId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) return crypto.randomUUID();
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}

/** Texto do resultado: "Vermelho 2 x 2 Preto · pênaltis: Vermelho". */
export function gameResultText(game: Pick<LiveGame, "homeTeamId" | "awayTeamId" | "homeScore" | "awayScore" | "penaltyWinnerTeamId">, colorOf: (id: string) => string) {
  const base = `${colorOf(game.homeTeamId)} ${game.homeScore} x ${game.awayScore} ${colorOf(game.awayTeamId)}`;
  return game.penaltyWinnerTeamId ? `${base} · pênaltis: ${colorOf(game.penaltyWinnerTeamId)}` : base;
}

/** "🧤 Ana (2), Caio (1)" ou "" */
export function savesText(game: Pick<LiveGame, "penaltySaves">, nameOf: (name: string) => string = (n) => n): string {
  const list = game.penaltySaves.filter((s) => s.count > 0);
  return list.length === 0 ? "" : `🧤 ${list.map((s) => `${nameOf(s.displayName)} (${s.count})`).join(", ")}`;
}
