import type { Draw, DrawMember, DrawTeam } from "./api";
import { playerDisplayName } from "./playerDisplayName";

export type TeamStyle = {
  /** Rótulo sempre em texto: a cor nunca é a única pista. */
  label: string;
  emoji: string;
  /** Classes literais para o Tailwind detectar. */
  swatch: string;
  border: string;
  /** Cor do time como variável CSS (pulso da borda no gol). */
  cssColor: string;
};

/** Tokens do DS `team-*` (mesmos valores de antes). */
const STYLES: Record<string, TeamStyle> = {
  Vermelho: { label: "Vermelho", emoji: "🔴", swatch: "bg-team-red", border: "border-team-red", cssColor: "var(--team-red)" },
  Preto: { label: "Preto", emoji: "⚫", swatch: "bg-team-black", border: "border-team-black", cssColor: "var(--team-black)" },
  Verde: { label: "Verde", emoji: "🟢", swatch: "bg-team-green", border: "border-team-green", cssColor: "var(--team-green)" },
};

/** Estilo do time; sorteios antigos (Time A/B, sem cor) usam estilo neutro com o nome original. */
export function teamStyle(team: Pick<DrawTeam, "color" | "name">): TeamStyle {
  return (team.color && STYLES[team.color]) || { label: team.name, emoji: "⚪", swatch: "bg-outline", border: "border-outline", cssColor: "var(--border-control)" };
}

/** Espelha RegistrationPolicy.IsLineupFrozen da API: partida iniciada ou aguardando fechamento. */
export function isLineupFrozen(matchStatus: string): boolean {
  return matchStatus === "InProgress" || matchStatus === "AwaitingClose";
}

export function isGoalkeeper(member: Pick<DrawMember, "role">) {
  return member.role === "Goalkeeper";
}

/** Goleiro primeiro, depois linha. */
export function orderedMembers(team: DrawTeam): DrawMember[] {
  return [...team.members].sort((a, b) => Number(isGoalkeeper(b)) - Number(isGoalkeeper(a)));
}

/** Menor e maior nota de linha do time (amplitude); null quando não há notas (jogador comum ou time sem linha). */
export function lineRange(team: Pick<DrawTeam, "members">): { min: number; max: number } | null {
  const ratings = team.members.filter((m) => !isGoalkeeper(m) && m.skillRating !== null).map((m) => m.skillRating as number);
  if (ratings.length === 0) return null;
  return { min: Math.min(...ratings), max: Math.max(...ratings) };
}

/** Largura da barra (0–100%) numa escala FIXA de 0 a 10, para não exagerar diferenças pequenas. */
export function ratingBarPercent(value: number | null | undefined): number {
  if (value === null || value === undefined || !Number.isFinite(value)) return 0;
  return Math.max(0, Math.min(100, (value / 10) * 100));
}

export function formatScore(value: number | null | undefined, digits = 1): string {
  return value === null || value === undefined ? "—" : value.toLocaleString("pt-BR", { minimumFractionDigits: digits, maximumFractionDigits: 2 });
}

/**
 * Escalação para colar no WhatsApp (só sorteio confirmado).
 * Nomes e funções apenas: sem notas, somas ou avisos.
 */
export function buildTeamsSummary(draw: Draw): string | null {
  if (draw.status !== "Confirmed" || draw.rosterChanged) return null;
  const lines = ["", "🎽 Times"];
  for (const team of [...draw.teams].sort((a, b) => a.sortOrder - b.sortOrder)) {
    const style = teamStyle(team);
    const members = orderedMembers(team).map((m) => `${playerDisplayName(m.displayName)}${isGoalkeeper(m) ? " (gol)" : ""}`);
    const keeperNote = team.members.some(isGoalkeeper) || draw.isLegacy ? "" : " · gol em rodízio";
    lines.push(`${style.emoji} ${style.label}: ${members.join(", ")}${keeperNote}`);
  }
  return lines.join("\n");
}

/**
 * Seleção de troca em dois toques: o primeiro escolhe, o segundo (de outro time ou outra função) troca.
 * Retorna o novo estado e, quando houver, o par a trocar.
 */
export function nextSwapSelection(
  selected: { playerProfileId: string; teamId: string; role: string } | null,
  clicked: { playerProfileId: string; teamId: string; role: string },
): { selected: typeof selected; swap: [string, string] | null } {
  if (!selected) return { selected: clicked, swap: null };
  if (selected.playerProfileId === clicked.playerProfileId) return { selected: null, swap: null };
  if (selected.teamId === clicked.teamId && selected.role === clicked.role) return { selected: clicked, swap: null };
  return { selected: null, swap: [selected.playerProfileId, clicked.playerProfileId] };
}

export type SwapPreview = {
  playerA: string;
  playerB: string;
  /** Frases do que muda, ex.: "Ana sai do Vermelho (linha) e vai para o Preto (gol)". */
  moves: string[];
  /** Escalação confirmada volta a prévia e precisa de nova confirmação. */
  invalidatesConfirmation: boolean;
};

/**
 * Prévia da troca antes de gravar (o segundo toque só propõe; a gravação exige confirmação explícita).
 * Null quando algum jogador não está no sorteio.
 */
export function swapPreview(draw: Draw, playerA: string, playerB: string): SwapPreview | null {
  const find = (id: string) => {
    for (const team of draw.teams) {
      const member = team.members.find((m) => m.playerProfileId === id);
      if (member) return { team, member };
    }
    return null;
  };
  const a = find(playerA);
  const b = find(playerB);
  if (!a || !b) return null;
  const role = (m: DrawMember) => (isGoalkeeper(m) ? "gol" : "linha");
  const move = (from: typeof a, to: typeof a) =>
    `${playerDisplayName(from.member.displayName)} sai do ${teamStyle(from.team).label} (${role(from.member)}) e vai para o ${teamStyle(to.team).label} (${role(to.member)})`;
  return {
    playerA,
    playerB,
    moves: [move(a, b), move(b, a)],
    invalidatesConfirmation: draw.status === "Confirmed",
  };
}
