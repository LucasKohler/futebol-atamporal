import type { NotificationActionUrl } from "./api";

/** Destinos permitidos (D8): comparação exata, depois de tirar espaços. */
export const ACTION_URLS: { url: NotificationActionUrl; label: string }[] = [
  { url: "/", label: "Início" },
  { url: "/partida", label: "Partida" },
  { url: "/pagamentos", label: "Pagamentos" },
  { url: "/rankings", label: "Rankings" },
  { url: "/perfil", label: "Perfil" },
];

export const TITLE_MAX = 120;
export const BODY_MAX = 500;
export const BELL_POLL_MS = 60_000;
export const INSTALL_DISMISS_DAYS = 14;
export const INSTALL_DISMISS_KEY = "fa.install.dismissedUntil";

export function isAllowedActionUrl(url: string): url is NotificationActionUrl {
  return ACTION_URLS.some((a) => a.url === url.trim());
}

/** Chave pública VAPID (Base64Url) → bytes para `applicationServerKey`. */
export function urlBase64ToUint8Array(base64Url: string): Uint8Array {
  const padding = "=".repeat((4 - (base64Url.length % 4)) % 4);
  const base64 = (base64Url + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = typeof atob === "function" ? atob(base64) : Buffer.from(base64, "base64").toString("binary");
  const out = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
  return out;
}

/** Bytes de uma chave da inscrição → Base64Url (o que a API espera em p256dh/auth). */
export function arrayBufferToBase64Url(buf: ArrayBuffer | null): string {
  if (!buf) return "";
  const bytes = new Uint8Array(buf);
  let bin = "";
  for (const b of bytes) bin += String.fromCharCode(b);
  const b64 = typeof btoa === "function" ? btoa(bin) : Buffer.from(bin, "binary").toString("base64");
  return b64.replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export type Platform = { isIos: boolean; isAndroid: boolean; isIosSafari: boolean };

/** iPadOS se apresenta como Mac: toque múltiplo denuncia. Safari iOS = não é Chrome/Firefox/Edge iOS. */
export function detectPlatform(userAgent: string, maxTouchPoints = 0): Platform {
  const ua = userAgent || "";
  const isIos = /iPhone|iPad|iPod/i.test(ua) || (/Macintosh/i.test(ua) && maxTouchPoints > 1);
  const isAndroid = /Android/i.test(ua);
  const isIosSafari = isIos && /Safari/i.test(ua) && !/CriOS|FxiOS|EdgiOS|OPiOS|GSA/i.test(ua);
  return { isIos, isAndroid, isIosSafari };
}

export type InstallMode = "hidden" | "prompt" | "ios-guide";

/** Banner de instalação: some no modo instalado; botão de 1 toque quando há beforeinstallprompt; guia no Safari iOS. */
export function installBannerMode(s: { standalone: boolean; hasPrompt: boolean; platform: Platform; dismissed: boolean }): InstallMode {
  if (s.standalone || s.dismissed) return "hidden";
  if (s.hasPrompt) return "prompt";
  if (s.platform.isIosSafari) return "ios-guide";
  return "hidden";
}

export function dismissUntil(now: number, days = INSTALL_DISMISS_DAYS): number {
  return now + days * 24 * 60 * 60 * 1000;
}

export function isDismissed(stored: string | null, now: number): boolean {
  const until = stored ? Number(stored) : NaN;
  return Number.isFinite(until) && until > now;
}

export type PushState = "unsupported" | "ios-needs-install" | "server-disabled" | "denied" | "needs-update" | "off" | "on";

/**
 * Estado do push neste aparelho, na ordem de precedência que a UI explica:
 * sem suporte → iPhone fora da Tela de Início → servidor sem push → permissão negada → desligado/ligado.
 * - `serverRevoked`: a remoção no servidor foi confirmada. Mesmo que a inscrição local tenha ficado
 *   (o navegador não cancelou), o aparelho está desligado para a conta: "off" ("Ativar").
 *   Resultado incerto ou falho (só a marca de desligado, sem confirmação) continua "on", com "Desativar".
 * - `hasLocks`: sem Web Locks não há como coordenar abas com segurança; ativar fica indisponível
 *   ("needs-update"). Se ainda estiver ligado, continua "on" para permitir desligar.
 */
export function pushState(s: {
  hasNotification: boolean;
  hasServiceWorker: boolean;
  hasPushManager: boolean;
  platform: Platform;
  standalone: boolean;
  permission: NotificationPermission | "unsupported";
  serverMode: string | null;
  subscribed: boolean;
  /** A remoção desta inscrição no servidor foi confirmada (a marca de desligado sozinha não basta). */
  serverRevoked?: boolean;
  hasLocks?: boolean;
}): PushState {
  if (s.platform.isIos && !s.standalone) return "ios-needs-install";
  if (!s.hasNotification || !s.hasServiceWorker || !s.hasPushManager) return "unsupported";
  if (s.serverMode === "Disabled") return "server-disabled";
  if (s.permission === "denied") return "denied";
  const active = s.subscribed && !s.serverRevoked && s.permission === "granted";
  if (active) return "on";
  return s.hasLocks === false ? "needs-update" : "off";
}

/** Lê e remove `?n={id}` da URL (o app marca a notificação como lida e limpa o endereço). */
export function takeNotificationParam(href: string): { id: string | null; cleanUrl: string } {
  const u = new URL(href);
  const id = u.searchParams.get("n");
  u.searchParams.delete("n");
  const valid = id && /^[0-9a-fA-F-]{36}$/.test(id) ? id : null;
  return { id: valid, cleanUrl: u.pathname + (u.search ? u.search : "") + u.hash };
}

export const CATEGORY_ICON: Record<string, string> = {
  MatchOpen: "⚽",
  WaitlistPromoted: "⚡",
  DrawPublished: "🎨",
  MatchCompleted: "🏆",
  RegistrationReminder: "📝",
  PaymentReminder: "💳",
  AdminBroadcast: "📣",
};

export const CATEGORY_LABEL: Record<string, string> = {
  MatchOpen: "Lista aberta",
  WaitlistPromoted: "Promoção da fila",
  DrawPublished: "Times sorteados",
  MatchCompleted: "Pelada encerrada",
  RegistrationReminder: "Lembrete de presença",
  PaymentReminder: "Lembrete de pagamento",
  AdminBroadcast: "Comunicado",
  MatchAvailable: "Partida disponível",
};

export const AUDIENCE_LABEL: Record<string, string> = {
  AllMembers: "Todos os membros ativos",
  MatchConfirmed: "Confirmados na partida atual",
  PaymentPending: "Com pagamento pendente",
  NotResponded: "Quem ainda não respondeu",
  Direct: "Destinatários do evento",
};

export const SOURCE_LABEL: Record<string, string> = {
  Automatic: "Automático",
  AdminQuick: "Lembrete rápido",
  AdminBroadcast: "Comunicado",
};

/** O título do servidor já vem com emoji (ex.: "⚡ Vaga confirmada!"): evita ícone duplicado na lista. */
export function startsWithEmoji(title: string): boolean {
  return /^\s*\p{Extended_Pictographic}/u.test(title);
}

/** "agora", "há 5 min", "há 3 h", "ontem", "12/11". */
export function relativeTime(iso: string, now: number): string {
  const t = Date.parse(iso);
  if (!Number.isFinite(t)) return "";
  const diff = Math.max(0, now - t);
  const min = Math.floor(diff / 60_000);
  if (min < 1) return "agora";
  if (min < 60) return `há ${min} min`;
  const h = Math.floor(min / 60);
  if (h < 24) return `há ${h} h`;
  if (h < 48) return "ontem";
  return new Date(t).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" });
}

/** Problemas do comunicado antes de enviar (a API valida de novo). */
export function broadcastIssues(title: string, body: string, actionUrl: string): string[] {
  const issues: string[] = [];
  if (!title.trim()) issues.push("Escreva um título.");
  if (title.length > TITLE_MAX) issues.push(`Título com no máximo ${TITLE_MAX} caracteres.`);
  if (!body.trim()) issues.push("Escreva a mensagem.");
  if (body.length > BODY_MAX) issues.push(`Mensagem com no máximo ${BODY_MAX} caracteres.`);
  if (!isAllowedActionUrl(actionUrl)) issues.push("Escolha um destino da lista.");
  return issues;
}

/** "Disponível de novo em 12 min." para 409 reminder_cooldown. */
export function cooldownText(retryAfterSeconds: number | null): string {
  if (!retryAfterSeconds || retryAfterSeconds <= 0) return "Este lembrete foi enviado há pouco. Tente mais tarde.";
  const min = Math.ceil(retryAfterSeconds / 60);
  return `Este lembrete foi enviado há pouco. Disponível de novo em ${min} min.`;
}

export function newIdempotencyKey(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) return crypto.randomUUID();
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}

/** Rótulo curto do aparelho para a lista de inscrições (sem dados sensíveis). */
export function deviceLabel(userAgent: string): string {
  const p = detectPlatform(userAgent);
  if (p.isIos) return "iPhone/iPad";
  if (p.isAndroid) return "Android";
  if (/Windows/i.test(userAgent)) return "Windows";
  if (/Macintosh/i.test(userAgent)) return "Mac";
  if (/Linux/i.test(userAgent)) return "Linux";
  return "Navegador";
}

/**
 * Resultado de desligar o push deste aparelho (servidor + navegador).
 * - `none`: não havia inscrição neste aparelho.
 * - `revoked`: as duas pontas confirmaram.
 * - `partial`: só uma ponta confirmou; já basta para o aparelho não receber avisos desta conta
 *   (sem inscrição no navegador nada chega; sem registro no servidor nada é enviado para esta conta).
 * - `failed`: nenhuma ponta confirmou (ou não deu para ler a inscrição): o push pessoal pode continuar ativo.
 */
export type PushRevocation =
  | { status: "none" }
  | { status: "revoked" }
  | { status: "partial"; serverRevoked: boolean; browserRevoked: boolean }
  | { status: "failed"; reason: "read" | "both" | "busy" | "storage" | "pending" | "session" | "server" | "unauthenticated" };

export function revocationOutcome(s: { readFailed: boolean; subscriptionFound: boolean; serverRevoked: boolean; browserRevoked: boolean }): PushRevocation {
  if (s.readFailed) return { status: "failed", reason: "read" };
  if (!s.subscriptionFound) return { status: "none" };
  if (s.serverRevoked && s.browserRevoked) return { status: "revoked" };
  if (s.serverRevoked || s.browserRevoked) return { status: "partial", serverRevoked: s.serverRevoked, browserRevoked: s.browserRevoked };
  return { status: "failed", reason: "both" };
}

/** Só sair da conta quando o push pessoal deste aparelho estiver comprovadamente desligado. */
export function canLogoutAfter(r: PushRevocation): boolean {
  return r.status !== "failed";
}

export function revocationMessage(r: PushRevocation, context: "toggle" | "logout"): string | null {
  if (r.status === "failed" && r.reason === "session") {
    return context === "logout"
      ? "A conta deste navegador mudou (por exemplo, outra conta entrou em outra aba). Nada foi alterado e a sessão atual continua. Atualize a página para continuar."
      : "A conta deste navegador mudou (por exemplo, outra conta entrou em outra aba). Nada foi alterado. Atualize a página e tente de novo.";
  }
  if (r.status === "failed" && r.reason === "unauthenticated") {
    return context === "logout"
      ? "Sua sessão já tinha terminado neste navegador. Entre de novo se quiser desligar os avisos deste aparelho."
      : "Sua sessão terminou. Entre de novo para desligar os avisos deste aparelho.";
  }
  if (r.status === "failed" && r.reason === "pending") {
    return context === "logout"
      ? "Há uma ativação de avisos em andamento neste aparelho (por exemplo, um pedido de permissão aberto em outra aba). Responda ou feche esse pedido e toque em Sair de novo; até lá você continua conectado."
      : "Há uma ativação de avisos em andamento em outra aba. Termine ou cancele essa ativação e tente desativar de novo.";
  }
  if (r.status === "failed") {
    return context === "logout"
      ? "Não conseguimos desligar os avisos deste aparelho, então você continua conectado para que ele não receba avisos da sua conta depois da saída. Verifique a conexão e toque em Sair de novo."
      : "Não conseguimos desligar os avisos neste aparelho; eles continuam ativos. Verifique a conexão e tente de novo.";
  }
  if (context === "logout") return null;
  if (r.status === "partial") {
    return r.browserRevoked
      ? "Avisos desligados neste aparelho. O servidor não confirmou a remoção agora, mas este aparelho não recebe mais avisos."
      : "Avisos desligados para esta conta neste aparelho. O navegador não removeu a inscrição; se quiser, bloqueie as notificações do site nas configurações.";
  }
  return "Avisos no celular desativados neste aparelho. Eles continuam no sininho do app.";
}
