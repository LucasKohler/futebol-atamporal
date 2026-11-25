import type { MatchDetail } from "./api";
import { playerDisplayName } from "./playerDisplayName";

/** Rótulo curto do estado da lista para o texto do grupo. */
const LIST_STATE: Record<string, string> = {
  Draft: "lista ainda não aberta",
  RegistrationOpen: "lista aberta",
  RegistrationClosed: "lista encerrada",
  Drawn: "times sorteados",
  InProgress: "em andamento",
  AwaitingClose: "aguardando resultado",
  Completed: "encerrada",
  Cancelled: "cancelada",
};

export type SummaryOptions = {
  /** Link da tela da partida (ex.: https://app/partida). */
  link: string;
  /** Momento da geração do texto. */
  now: Date;
  /** Fuso do grupo, para data e horário iguais para todos. */
  timeZone: string;
  /** Bloco opcional da escalação confirmada (ver buildTeamsSummary), inserido antes do link. */
  teams?: string | null;
};

function formatDate(date: Date, timeZone: string) {
  const day = date.toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "2-digit", timeZone });
  const time = date.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone });
  return `${day} às ${time}`;
}

/**
 * Texto da lista para o admin colar no WhatsApp (PR-2).
 * Usa só nomes de exibição na ordem persistida pela API.
 * Nunca inclui telefone, nota ou situação de pagamento.
 */
export function buildWhatsAppSummary(detail: MatchDetail, options: SummaryOptions): string {
  const { match, confirmed, waitlist } = detail;
  const name = (p: { displayName: string }) => playerDisplayName(p.displayName);
  const state = LIST_STATE[match.status] ?? match.status;

  const lines: string[] = [
    `⚽ *${match.title?.trim() || "Pelada"}*`,
    `📅 ${formatDate(new Date(match.startsAt), options.timeZone)}`,
    `📍 ${match.location}`,
    `Vagas: ${match.confirmedCount}/${match.playerLimit} · ${state}`,
    "",
    `✅ Confirmados (${confirmed.length})`,
  ];

  if (confirmed.length === 0) lines.push("—");
  confirmed.forEach((p, i) => lines.push(`${i + 1}. ${name(p)}`));

  lines.push("", `⏳ Fila de espera (${waitlist.length})`);
  if (waitlist.length === 0) lines.push("—");
  waitlist.forEach((p, i) => lines.push(`${p.waitlistPosition ?? i + 1}. ${name(p)}`));

  if (options.teams) lines.push(options.teams);

  lines.push(
    "",
    `Confirme ou desista pelo app: ${options.link}`,
    `Atualizado em ${formatDate(options.now, options.timeZone)}`,
  );

  return lines.join("\n");
}
