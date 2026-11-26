import type { Scorer, SeasonStats } from "./api";
import { playerDisplayName } from "./playerDisplayName";

const MONTHS = ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

export type Period = { year: number; month: number };

function periodIn(now: Date, timeZone: string): Period {
  const parts = new Intl.DateTimeFormat("en-US", { timeZone, year: "numeric", month: "numeric" }).formatToParts(now);
  return {
    year: Number(parts.find((p) => p.type === "year")?.value),
    month: Number(parts.find((p) => p.type === "month")?.value),
  };
}

/**
 * Mês e ano "de agora" no fuso do GRUPO, a mesma regra da API (GroupClock): a artilharia usa a data local do jogo
 * no fuso do grupo, não a do aparelho. Ex.: 01/01/2027 01:30 UTC ainda é dezembro/2026 em America/Sao_Paulo.
 * Fuso ausente ou inválido cai em UTC, como a API.
 */
export function groupPeriod(now: Date, timeZone: string | null | undefined): Period {
  try {
    return periodIn(now, timeZone || "UTC");
  } catch {
    return periodIn(now, "UTC");
  }
}

/** "1 gol", "2 gols". */
export function plural(n: number, singular: string, pluralForm: string): string {
  return `${n} ${n === 1 ? singular : pluralForm}`;
}

export function monthName(month: number): string {
  return MONTHS[month - 1] ?? String(month);
}

/** "Fulano, 3 gols" · empate: "Fulano e Beltrano dividem a liderança, 2 gols". */
export function leaderText(scorers: Scorer[]): string {
  const leaders = scorers.filter((s) => s.isLeader);
  if (leaders.length === 0) return "Sem artilheiro ainda.";
  const goals = leaders[0].goals;
  const unit = goals === 1 ? "gol" : "gols";
  const names = leaders.map((s) => playerDisplayName(s.displayName));
  if (names.length === 1) return `${names[0]}, ${goals} ${unit}`;
  const list = `${names.slice(0, -1).join(", ")} e ${names[names.length - 1]}`;
  return `${list} dividem a liderança, ${goals} ${unit}`;
}

/** Linha do próprio jogador na temporada (perfil). */
export function myStats(season: SeasonStats | null, playerProfileId: string | null | undefined) {
  if (!season || !playerProfileId) return null;
  const row = season.players.find((p) => p.playerProfileId === playerProfileId);
  const monthGoals = season.monthScorers.find((s) => s.playerProfileId === playerProfileId)?.goals ?? 0;
  const yearGoals = season.yearScorers.find((s) => s.playerProfileId === playerProfileId)?.goals ?? 0;
  return {
    monthGoals,
    yearGoals,
    roundsPlayed: row?.roundsPlayed ?? 0,
    roundsWon: row?.roundsWon ?? 0,
    bestPlayer: row?.bestPlayer ?? 0,
    bestGoalkeeper: row?.bestGoalkeeper ?? 0,
    gamesWonOnPenalties: row?.gamesWonOnPenalties ?? 0,
    /** Pênaltis defendidos no período (0 em APIs anteriores ao contrato ao vivo). */
    penaltySaves: row?.penaltySaves ?? 0,
  };
}
