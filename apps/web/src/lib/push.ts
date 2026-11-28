"use client";

import { api, ApiError } from "./api";
import {
  arrayBufferToBase64Url,
  canLogoutAfter,
  detectPlatform,
  deviceLabel,
  revocationOutcome,
  urlBase64ToUint8Array,
  type PushRevocation,
} from "./notifications";

/**
 * Push no navegador (contrato D2/D4/D7). Permissão só é pedida em `enablePush`, chamado por um toque do usuário.
 * A inscrição é do usuário e do aparelho; no logout, `disablePush` cancela antes de sair.
 *
 * Coordenação de gravações no servidor (ligar, sincronizar, desligar), na mesma aba e entre abas:
 * - toda gravação roda sob o mesmo bloqueio exclusivo da Web Locks API (`fa.push.write`), que vale
 *   para todas as abas da origem. Uma gravação já em voo termina antes de o desligar começar;
 * - desligar grava a marca persistente `fa.push.optOut` ANTES de pedir o bloqueio e de novo dentro dele;
 *   toda gravação automática confere a marca dentro do bloqueio, logo depois de obtê-lo e antes de
 *   cada escrita. Nenhuma sincronização grava depois do DELETE, mesmo se o navegador não cancelar a inscrição;
 * - sem Web Locks (navegador antigo), nada liga ou grava no servidor automaticamente nem por "Ativar"
 *   (fallback seguro); só o desligar funciona, serializado na aba. Assim não há POST depois de um DELETE.
 */

const KEY_ID_STORAGE = "fa.push.keyId";
/** Marca persistente: o usuário desligou o push neste aparelho; a sincronização automática não pode religar. */
const OPT_OUT_STORAGE = "fa.push.optOut";
/** Sequência de revogações (cresce a cada desligar): invalida ativações iniciadas antes, em qualquer aba. */
const REVOKE_SEQ_STORAGE = "fa.push.revokeSeq";
/**
 * Resumo SHA-256 (hex) do endpoint cuja remoção no servidor foi CONFIRMADA; a URL da inscrição nunca é gravada.
 * Só com ele a tela mostra "Ativar" com a inscrição local restante.
 */
const SERVER_REVOKED_STORAGE = "fa.push.serverRevokedDigest";
/** Chave antiga (versão de desenvolvimento) que guardava a URL bruta: apagada sempre que possível. */
const LEGACY_SERVER_REVOKED_STORAGE = "fa.push.serverRevokedEndpoint";
/**
 * Prefixo dos bloqueios de "intenção de ativar". Cada ativação segura um bloqueio próprio desde ANTES de pedir
 * permissão até terminar. O desligar consulta esses bloqueios via Web Locks (que vale entre abas e não depende
 * de localStorage) para saber se há ativação pendente que ele não conseguiu invalidar.
 */
export const PUSH_INTENT_LOCK_PREFIX = "fa.push.intent:";
/**
 * Prefixo dos marcadores de revogação: cada desligar segura um bloqueio próprio, sem liberar, até a aba fechar.
 * Uma ativação compara os marcadores existentes no início e de novo no momento de gravar: marcador novo =
 * houve revogação no meio, então ela é descartada. Funciona entre abas sem depender de localStorage.
 */
export const PUSH_REVOKED_LOCK_PREFIX = "fa.push.revoked:";
/** Nome do bloqueio exclusivo compartilhado por todas as abas. */
export const PUSH_WRITE_LOCK = "fa.push.write";
/** Tempo máximo esperando gravações de outras abas antes de desistir do desligar (o logout fica bloqueado). */
export const DISABLE_LOCK_TIMEOUT_MS = 15_000;
/** Canal entre abas para cancelar ativações pendentes (com confirmação), sem depender de localStorage. */
const COORD_CHANNEL = "fa.push.coord";
/** Tempo máximo esperando as outras abas confirmarem que cancelaram suas ativações pendentes. */
export const INTENT_ACK_TIMEOUT_MS = 2_000;

export function pushCapabilities() {
  const w = typeof window !== "undefined" ? window : undefined;
  return {
    hasNotification: !!w && "Notification" in w,
    hasServiceWorker: !!w && "serviceWorker" in navigator,
    hasPushManager: !!w && "PushManager" in w,
  };
}

export function currentPermission(): NotificationPermission | "unsupported" {
  return typeof window !== "undefined" && "Notification" in window ? Notification.permission : "unsupported";
}

export function isStandaloneDisplay(): boolean {
  if (typeof window === "undefined") return false;
  const nav = navigator as Navigator & { standalone?: boolean };
  return window.matchMedia?.("(display-mode: standalone)").matches === true || nav.standalone === true;
}

export function currentPlatform() {
  return detectPlatform(typeof navigator !== "undefined" ? navigator.userAgent : "", typeof navigator !== "undefined" ? navigator.maxTouchPoints : 0);
}

let registration: Promise<ServiceWorkerRegistration | null> | null = null;

/**
 * Registra /sw.js (só push e clique; sem cache). Idempotente.
 * Falha de registro NÃO vira "sem service worker": a promessa rejeita e o cache é descartado,
 * para a próxima chamada tentar de novo.
 */
export function registerServiceWorker(): Promise<ServiceWorkerRegistration | null> {
  if (!pushCapabilities().hasServiceWorker) return Promise.resolve(null);
  if (!registration) {
    registration = navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch((e: unknown) => {
      registration = null;
      throw e;
    });
  }
  return registration;
}

/**
 * Inscrição atual deste aparelho, sem registrar nada novo: lê o registro já existente do service worker.
 * `null` só quando comprovadamente não há registro ou inscrição; erro de leitura rejeita (não é "ausência").
 */
export async function getSubscription(): Promise<PushSubscription | null> {
  if (!pushCapabilities().hasServiceWorker) return null;
  const reg = await navigator.serviceWorker.getRegistration("/");
  if (!reg || !("pushManager" in reg)) return null;
  return reg.pushManager.getSubscription();
}

// ---- Coordenação ----

/** Na dúvida (armazenamento inacessível), trata como desligado: a sincronização automática não grava. */
function isOptedOut(): boolean {
  try {
    return localStorage.getItem(OPT_OUT_STORAGE) === "1";
  } catch {
    return true;
  }
}

/**
 * Sinal de "desligar em andamento", SEPARADO da marca confirmada de desligado. Cada desligar registra um token
 * antes de esperar o bloqueio (sincronizações já na fila desistem) e remove só o próprio token ao terminar.
 * Nunca é usado para restaurar `fa.push.optOut`: a marca confirmada só é gravada sob o bloqueio, depois do DELETE
 * 204, e nenhuma falha a apaga. Tokens de abas que fecharam no meio expiram sozinhos.
 */
const DISABLING_STORAGE = "fa.push.disabling";
const DISABLING_TTL_MS = 60_000;

function readDisabling(): Record<string, number> {
  const raw = localStorage.getItem(DISABLING_STORAGE);
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object") return {};
    const out: Record<string, number> = {};
    const now = Date.now();
    for (const [k, v] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof v === "number" && Number.isFinite(v) && now - v < DISABLING_TTL_MS) out[k] = v;
    }
    return out;
  } catch {
    return {};
  }
}

function writeDisabling(tokens: Record<string, number>) {
  if (Object.keys(tokens).length === 0) localStorage.removeItem(DISABLING_STORAGE);
  else localStorage.setItem(DISABLING_STORAGE, JSON.stringify(tokens));
}

/** Registra um desligar em andamento; o token só serve para removê-lo depois. */
function beginDisabling(): string {
  const token = newIntentId();
  try {
    const tokens = readDisabling();
    tokens[token] = Date.now();
    writeDisabling(tokens);
  } catch {
    /* sem armazenamento: só otimização; a segurança vem do bloqueio e da marca confirmada */
  }
  return token;
}

/** Remove só o próprio token. Não toca a marca confirmada de desligado. */
function endDisabling(token: string) {
  try {
    const tokens = readDisabling();
    delete tokens[token];
    writeDisabling(tokens);
  } catch {
    /* sem armazenamento */
  }
}

/** Há desligar em andamento (nesta ou em outra aba)? Armazenamento ilegível = sim (falha segura). */
function disablePending(): boolean {
  try {
    return Object.keys(readDisabling()).length > 0;
  } catch {
    return true;
  }
}

/** A sincronização automática deve desistir: desligado confirmado ou desligar em andamento. */
function syncBlocked(): boolean {
  return isOptedOut() || disablePending();
}

/**
 * Grava a marca de desligado (preferência do aparelho) e confirma lendo de volta.
 * Retorna false se não deu para gravar ou confirmar (ex.: cota cheia: leitura funciona, gravação não).
 */
function markOptedOut(): boolean {
  try {
    localStorage.setItem(OPT_OUT_STORAGE, "1");
    return localStorage.getItem(OPT_OUT_STORAGE) === "1";
  } catch {
    return false;
  }
}

/** Chave da sequência de revogações DESTA conta: a revogação de uma conta não invalida ativações de outra. */
function revokeSeqKey(userId: string): string {
  return `${REVOKE_SEQ_STORAGE}:${encodeURIComponent(userId)}`;
}

/** Avança a sequência de revogações da conta e confirma lendo de volta. */
function bumpRevokeSeq(userId: string): boolean {
  try {
    const next = String(readRevokeSeq(userId) + 1);
    localStorage.setItem(revokeSeqKey(userId), next);
    return localStorage.getItem(revokeSeqKey(userId)) === next;
  } catch {
    return false;
  }
}

function readRevokeSeq(userId: string): number {
  try {
    const n = Number(localStorage.getItem(revokeSeqKey(userId)) ?? "0");
    return Number.isFinite(n) ? n : 0;
  } catch {
    return Number.NaN; // ilegível: nunca coincide, então ativações em curso são descartadas
  }
}

/** Prefixo dos nomes de bloqueio (intenção ou marcador) de uma conta. */
function userScope(userId: string): string {
  return `${encodeURIComponent(userId)}:`;
}

/** Resumo SHA-256 (hex) não reversível do endpoint; null se o navegador não oferecer Web Crypto. */
export async function endpointDigest(endpoint: string): Promise<string | null> {
  try {
    const subtle = globalThis.crypto?.subtle;
    if (!subtle) return null;
    const bytes = new Uint8Array(await subtle.digest("SHA-256", new TextEncoder().encode(endpoint)));
    return Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  } catch {
    return null;
  }
}

/** A remoção desta inscrição no servidor foi confirmada por um desligar anterior? (usado pela tela) */
export async function isServerRevoked(endpoint: string): Promise<boolean> {
  const digest = await endpointDigest(endpoint);
  if (!digest) return false; // incerto: a tela continua oferecendo "Desativar"
  try {
    return localStorage.getItem(SERVER_REVOKED_STORAGE) === digest;
  } catch {
    return false;
  }
}

async function rememberServerRevocation(endpoint: string, confirmed: boolean) {
  try {
    localStorage.removeItem(LEGACY_SERVER_REVOKED_STORAGE);
    const digest = confirmed ? await endpointDigest(endpoint) : null;
    if (digest) localStorage.setItem(SERVER_REVOKED_STORAGE, digest);
    else localStorage.removeItem(SERVER_REVOKED_STORAGE);
  } catch {
    /* sem armazenamento: a tela fica em "Desativar" (seguro) */
  }
}

function newIntentId(): string {
  try {
    return globalThis.crypto.randomUUID();
  } catch {
    return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  }
}

/** Ativações pendentes DESTA aba (id -> cancelada?). O cancelamento fica em memória: sobrevive ao fechamento de outras abas. */
const localIntents = new Map<string, { cancelled: boolean; userId: string }>();

/** Cancela só as ativações locais da conta revogada; as de outra conta seguem intactas. */
function cancelLocalIntents(userId: string): string[] {
  const ids: string[] = [];
  for (const [id, state] of localIntents) {
    if (state.userId !== userId) continue;
    state.cancelled = true;
    ids.push(id);
  }
  return ids;
}

type CoordMessage = { type: "revoke"; revokeId: string; userId: string } | { type: "ack"; revokeId: string; intents: string[] };

let channel: BroadcastChannel | null | undefined;

/** Canal de coordenação da aba. Ao receber "revoke", cancela as ativações locais e confirma quais eram. */
function coordChannel(): BroadcastChannel | null {
  if (channel !== undefined) return channel;
  try {
    if (typeof BroadcastChannel !== "function") return (channel = null);
    const ch = new BroadcastChannel(COORD_CHANNEL);
    ch.addEventListener("message", (ev: MessageEvent) => {
      const msg = ev.data as CoordMessage | undefined;
      if (msg?.type !== "revoke" || !msg.revokeId || typeof msg.userId !== "string" || !msg.userId) return;
      const intents = cancelLocalIntents(msg.userId);
      ch.postMessage({ type: "ack", revokeId: msg.revokeId, intents } satisfies CoordMessage);
    });
    return (channel = ch);
  } catch {
    return (channel = null);
  }
}

/**
 * Segura o bloqueio de intenção `fa.push.intent:<id>` até `release()`. Resolve quando concedido (nome único:
 * concessão imediata). Sem Web Locks, devolve null (ativar já é recusado nesse modo).
 */
async function holdIntentLock(id: string, userId: string): Promise<{ release: () => void } | null> {
  const locks = webLocks();
  if (!locks) return null;
  let release!: () => void;
  const held = new Promise<void>((r) => (release = r));
  await new Promise<void>((granted, failed) => {
    locks
      .request(`${PUSH_INTENT_LOCK_PREFIX}${userScope(userId)}${id}`, { mode: "exclusive" }, () => {
        granted();
        return held;
      })
      .catch(failed);
  });
  return { release };
}

/** Cria um marcador de revogação da conta (bloqueio mantido até a aba fechar). false se não for possível. */
async function holdRevocationMarker(userId: string): Promise<boolean> {
  const locks = webLocks();
  if (!locks) return false;
  try {
    await new Promise<void>((granted, failed) => {
      locks
        .request(`${PUSH_REVOKED_LOCK_PREFIX}${userScope(userId)}${newIntentId()}`, { mode: "exclusive" }, () => {
          granted();
          return new Promise<void>(() => undefined); // nunca libera: vale enquanto a aba existir
        })
        .catch(failed);
    });
    return true;
  } catch {
    return false;
  }
}

/** Marcadores de revogação DESTA conta ativos em todas as abas; null se não for possível consultar. */
async function revocationMarkers(userId: string): Promise<Set<string> | null> {
  const locks = webLocks();
  if (!locks || typeof locks.query !== "function") return null;
  try {
    const snap = await locks.query();
    return new Set((snap.held ?? []).map((l) => l.name ?? "").filter((n) => n.startsWith(`${PUSH_REVOKED_LOCK_PREFIX}${userScope(userId)}`)));
  } catch {
    return null;
  }
}

/** Ids das ativações pendentes DESTA conta em todas as abas (pelos bloqueios de intenção); null se não der para saber. */
async function pendingIntentIds(userId: string): Promise<string[] | null> {
  const locks = webLocks();
  if (!locks || typeof locks.query !== "function") return null;
  try {
    const snap = await locks.query();
    const names = [...(snap.held ?? []), ...(snap.pending ?? [])].map((l) => l.name ?? "");
    const prefix = `${PUSH_INTENT_LOCK_PREFIX}${userScope(userId)}`;
    return [...new Set(names.filter((n) => n.startsWith(prefix)).map((n) => n.slice(prefix.length)))];
  } catch {
    return null;
  }
}

/**
 * Cancela as ativações pendentes DA CONTA revogada, em qualquer aba, e só devolve true com confirmação de cada uma
 * (ativações de outra conta não são tocadas):
 * as locais são marcadas direto; as de outras abas, por mensagem com confirmação (ack). O cancelamento fica
 * na memória da aba que ativava, então não depende de localStorage nem de a aba que desligou continuar aberta.
 * false = havia ativação que não confirmou a tempo (ou não deu para saber): a revogação não é final.
 */
async function invalidatePendingActivations(userId: string): Promise<boolean> {
  // Sem Web Locks com consulta, enablePush recusa antes de começar (needs-update): não existe ativação pendente.
  const locks = webLocks();
  if (!locks || typeof locks.query !== "function") {
    cancelLocalIntents(userId);
    return true;
  }
  const ids = await pendingIntentIds(userId);
  if (ids === null) return false;
  const local = new Set(cancelLocalIntents(userId));
  const remote = ids.filter((id) => !local.has(id));
  if (remote.length === 0) return true;
  const ch = coordChannel();
  if (!ch) return false;
  const revokeId = newIntentId();
  const acked = new Set<string>();
  return await new Promise<boolean>((resolve) => {
    const onMessage = (ev: MessageEvent) => {
      const msg = ev.data as CoordMessage | undefined;
      if (msg?.type !== "ack" || msg.revokeId !== revokeId) return;
      for (const id of msg.intents ?? []) acked.add(id);
      if (remote.every((id) => acked.has(id))) finish(true);
    };
    const timer = setTimeout(() => finish(false), INTENT_ACK_TIMEOUT_MS);
    function finish(result: boolean) {
      clearTimeout(timer);
      ch!.removeEventListener("message", onMessage);
      resolve(result);
    }
    ch.addEventListener("message", onMessage);
    ch.postMessage({ type: "revoke", revokeId, userId } satisfies CoordMessage);
  });
}

function webLocks(): LockManager | null {
  try {
    const locks = typeof navigator !== "undefined" ? (navigator as Navigator & { locks?: LockManager }).locks : undefined;
    return locks && typeof locks.request === "function" ? locks : null;
  } catch {
    return null;
  }
}

/** Fallback sem Web Locks: serializa as gravações desta aba (ligar/desligar). */
let localChain: Promise<unknown> = Promise.resolve();
function withLocalLock<T>(fn: () => Promise<T>): Promise<T> {
  const run = localChain.then(fn, fn);
  localChain = run.catch(() => undefined);
  return run;
}

class LockTimeoutError extends Error {
  constructor() {
    super("Tempo esgotado esperando outras abas.");
    this.name = "LockTimeoutError";
  }
}

/**
 * Executa `fn` com o bloqueio exclusivo entre abas. Com `timeoutMs`, desiste se o bloqueio não vier a tempo
 * (rejeita com LockTimeoutError sem executar `fn`). Sem Web Locks, usa a fila local da aba.
 */
function withWriteLock<T>(fn: () => Promise<T>, timeoutMs?: number): Promise<T> {
  const locks = webLocks();
  if (!locks) return withLocalLock(fn);
  if (!timeoutMs) return locks.request(PUSH_WRITE_LOCK, { mode: "exclusive" }, () => fn()) as Promise<T>;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  return (
    locks.request(PUSH_WRITE_LOCK, { mode: "exclusive", signal: controller.signal }, () => {
      clearTimeout(timer);
      return fn();
    }) as Promise<T>
  ).catch((e: unknown) => {
      clearTimeout(timer);
      if (controller.signal.aborted) throw new LockTimeoutError();
      throw e;
    });
}

/** 409 do contrato de troca de sessão (e só ele): a API não alterou nada porque o cookie é de outra conta. */
function isSessionChanged(e: unknown): boolean {
  return e instanceof ApiError && e.status === 409 && e.code === "session_changed";
}

/** A API recusou porque a conta do cookie não é a esperada (409 com `code: "session_changed"`): nada foi gravado. */
export class SessionChangedError extends Error {
  constructor() {
    super("A sessão mudou antes de ativar as notificações.");
    this.name = "SessionChangedError";
  }
}

/**
 * Único ponto que grava a inscrição no servidor. Sempre leva `expectedUserId`, a conta capturada no início da
 * intenção (toque ou início do app), nunca derivada depois de esperas. 409 vira SessionChangedError, sem repetir.
 */
async function sendToServer(sub: PushSubscription, expectedUserId: string) {
  const json = sub.toJSON();
  const p256dh = json.keys?.p256dh ?? arrayBufferToBase64Url(sub.getKey("p256dh"));
  const auth = json.keys?.auth ?? arrayBufferToBase64Url(sub.getKey("auth"));
  let res: { id: string; isActive: boolean; keyId: string };
  try {
    res = await api.subscribePush({
      endpoint: sub.endpoint,
      keys: { p256dh, auth },
      userAgent: navigator.userAgent.slice(0, 256),
      deviceLabel: deviceLabel(navigator.userAgent),
      expectedUserId,
    });
  } catch (e) {
    // Só o 409 do contrato de troca de sessão vira cancelamento; outro 409 segue como erro normal.
    if (isSessionChanged(e)) throw new SessionChangedError();
    throw e;
  }
  localStorage.setItem(KEY_ID_STORAGE, res.keyId);
  return res;
}

async function subscribeWithServerKey(reg: ServiceWorkerRegistration) {
  const { publicKey } = await api.vapidPublicKey();
  return reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) as BufferSource });
}

export type EnableResult =
  | { ok: true }
  | { ok: false; reason: "denied" | "dismissed" | "unsupported" | "needs-update" | "cancelled" | "storage" | "error"; message?: string };

/** Marca de desligado deste aparelho (para a tela não mostrar "Ativado" com a inscrição local restante). */
export function isPushOptedOut(): boolean {
  return isOptedOut();
}

/** Sem Web Locks (com consulta) e BroadcastChannel não há coordenação segura entre abas: ativar fica indisponível. */
export function canCoordinatePush(): boolean {
  const locks = webLocks();
  return locks !== null && typeof locks.query === "function" && coordChannel() !== null;
}

/**
 * Liga o push para a conta `expectedUserId` (o usuário da sessão que a tela mostra no momento do toque).
 * Precisa ser chamado direto do toque: o pedido de permissão é feito ANTES de qualquer `await`, porque o
 * iOS/WebKit (e o Chrome) só mostram o pedido dentro do gesto do usuário.
 * A intenção fica vinculada a esse usuário: na hora de gravar, a sessão atual é conferida sem cache e,
 * se não houver sessão, a resposta falhar ou o usuário for outro (logout de A e login de B no meio),
 * a ativação é descartada sem criar inscrição nem gravar.
 */
export async function enablePush(expectedUserId: string | null | undefined): Promise<EnableResult> {
  const caps = pushCapabilities();
  if (!caps.hasNotification || !caps.hasServiceWorker || !caps.hasPushManager) return { ok: false, reason: "unsupported" };
  // Sem coordenação entre abas (Web Locks com consulta + BroadcastChannel), um ligar poderia gravar depois do
  // DELETE de outra aba. Recusa antes de pedir permissão ou gravar.
  if (!canCoordinatePush()) return { ok: false, reason: "needs-update" };
  // Sem usuário conhecido no toque, não há a quem vincular a intenção: não ativa.
  if (!expectedUserId) return { ok: false, reason: "cancelled" };
  // Armazenamento ilegível: sem como detectar uma revogação no meio, não ativa (falha segura).
  const seqAtStart = readRevokeSeq(expectedUserId);
  if (!Number.isFinite(seqAtStart)) return { ok: false, reason: "storage" };

  // Pedido de permissão ainda dentro do gesto (sem await antes desta linha).
  let permissionRequest: Promise<NotificationPermission>;
  try {
    permissionRequest = Promise.resolve(Notification.requestPermission());
  } catch (e) {
    return { ok: false, reason: "error", message: e instanceof Error ? e.message : "Não foi possível pedir permissão." };
  }
  // A partir daqui pode esperar: a permissão já foi pedida.
  // Intenção visível para o desligar de qualquer aba, independente de localStorage (ver disablePush).
  const intentId = newIntentId();
  const intentState = { cancelled: false, userId: expectedUserId };
  localIntents.set(intentId, intentState);
  let intent: { release: () => void } | null;
  try {
    intent = await holdIntentLock(intentId, expectedUserId);
  } catch {
    localIntents.delete(intentId);
    return { ok: false, reason: "error", message: "Não foi possível coordenar com as outras abas." };
  }
  if (!intent) {
    localIntents.delete(intentId);
    return { ok: false, reason: "needs-update" };
  }
  // Marcadores de revogação existentes agora; um novo antes da gravação invalida esta ativação.
  const markersAtStart = await revocationMarkers(expectedUserId);
  if (!markersAtStart) {
    intent.release();
    localIntents.delete(intentId);
    return { ok: false, reason: "needs-update" };
  }
  try {
    const permission = await permissionRequest;
    if (permission === "denied") return { ok: false, reason: "denied" };
    if (permission !== "granted") return { ok: false, reason: "dismissed" };
    const reg = await registerServiceWorker();
    if (!reg) return { ok: false, reason: "unsupported" };
    const done = await withWriteLock(async () => {
      // Cancelada por um desligar (desta ou de outra aba, com confirmação): não grava.
      if (intentState.cancelled) return false;
      const current = readRevokeSeq(expectedUserId);
      if (!Number.isFinite(current) || current !== seqAtStart) return false; // desligado (ou ilegível) depois do início
      // Mesma conta que iniciou? Sessão conferida agora, sem cache; ausente, com erro ou de outro usuário: desiste.
      if (!(await sessionStillBelongsTo(expectedUserId))) return false;
      // Independe do armazenamento: houve revogação (nesta ou em outra aba) desde o início desta ativação?
      const markersNow = await revocationMarkers(expectedUserId);
      if (!markersNow || [...markersNow].some((m) => !markersAtStart.has(m))) return false;
      const existing = await reg.pushManager.getSubscription();
      const sub = existing ?? (await subscribeWithServerKey(reg));
      // Última conferência imediatamente antes do POST (a criação da inscrição local é assíncrona).
      if (intentState.cancelled || !(await sessionStillBelongsTo(expectedUserId))) {
        if (!existing) await sub.unsubscribe().catch(() => false); // não deixa inscrição nova sem dono
        return false;
      }
      // O POST carrega a conta esperada; a API recusa (409) se o cookie mudou depois da conferência acima.
      try {
        await sendToServer(sub, expectedUserId);
      } catch (e) {
        if (e instanceof SessionChangedError) {
          if (!existing) await sub.unsubscribe().catch(() => false); // inscrição local recém-criada sem dono
          return false;
        }
        throw e;
      }
      localStorage.removeItem(OPT_OUT_STORAGE); // o usuário religou explicitamente
      localStorage.removeItem(SERVER_REVOKED_STORAGE);
      localStorage.removeItem(LEGACY_SERVER_REVOKED_STORAGE);
      return true;
    });
    return done ? { ok: true } : { ok: false, reason: "cancelled" };
  } catch (e) {
    return { ok: false, reason: "error", message: e instanceof Error ? e.message : "Não foi possível ativar." };
  } finally {
    intent.release();
    localIntents.delete(intentId);
  }
}

/** A sessão atual (consultada sem cache) é do usuário esperado? Sessão ausente, erro ou outro usuário = não. */
async function sessionStillBelongsTo(expectedUserId: string): Promise<boolean> {
  try {
    const me = await api.me();
    return !!me && typeof me.id === "string" && me.id === expectedUserId;
  } catch {
    return false;
  }
}

/**
 * Desliga o push deste aparelho: apaga no servidor e cancela no navegador. Usado também no logout.
 * Não esconde falhas: devolve o que de fato foi revogado (ver `revocationOutcome`), para a tela não
 * anunciar sucesso nem deixar sair da conta com o push pessoal ainda ativo.
 * Antes de esperar o bloqueio só registra "desligar em andamento" (sincronizações na fila desistem). O DELETE, a
 * decisão e a marca confirmada de desligado acontecem com o bloqueio na mão; nada é restaurado depois de soltá-lo.
 * Sem 204 do DELETE nada muda: a inscrição local fica, as ativações de qualquer conta seguem e a marca confirmada
 * (inclusive a gravada por outro desligar) permanece como estava.
 */
export async function disablePush(expectedUserId: string | null | undefined): Promise<PushRevocation> {
  // Conta da ação capturada no toque. Sem ela nada é tocado; a API confere se o cookie ainda é dela (409 senão).
  if (!expectedUserId) return { status: "failed", reason: "session" };
  const token = beginDisabling();
  try {
    return await withWriteLock(async () => {
      try {
        return await revokeUnderLock(expectedUserId);
      } finally {
        endDisabling(token); // ainda sob o bloqueio
      }
    }, DISABLE_LOCK_TIMEOUT_MS);
  } catch (e) {
    endDisabling(token); // bloqueio não obtido: só o próprio token sai; a marca confirmada não é tocada
    if (e instanceof LockTimeoutError) return { status: "failed", reason: "busy" };
    throw e;
  }
}

/**
 * `sessionEnded`: a API respondeu 401 (sessão já revogada no servidor, expirada ou cookie antigo sem sessão).
 * Não há sessão a encerrar; a tela deve tratar como saída já feita e levar ao login.
 */
export type LogoutResult = { revocation: PushRevocation; loggedOut: boolean; sessionChanged?: boolean; sessionEnded?: boolean };

/** 401: o cookie não corresponde a uma sessão válida no servidor (revogada, expirada ou sem `fa_sid`). */
function isUnauthenticated(e: unknown): boolean {
  return e instanceof ApiError && e.status === 401;
}

/**
 * Desliga o push e, se isso for seguro, faz o logout AINDA COM o bloqueio de gravação na mão.
 * Uma ativação que comece ou continue nesse intervalo (nesta ou em outra aba) espera o bloqueio e, ao obtê-lo,
 * encontra a sessão encerrada e desiste sem gravar. Assim não existe janela entre revogar e sair.
 * `expectedUserId` é a conta da ação (capturada no toque). Se o cookie já for de outra conta, o DELETE e o logout
 * recebem 409 `session_changed`: nada é removido nem cancelado e a sessão vigente continua (`sessionChanged`).
 * Sem 204 do DELETE (404, 5xx, outro 409, rede) o logout não é feito e nada local muda (nova tentativa).
 * O logout revoga a sessão no servidor e responde 204 SEM Set-Cookie: nada aqui depende de o cookie ser apagado;
 * a tela confirma a saída consultando `/me` (401 = sem sessão).
 * 401 no DELETE ou no logout = a sessão já não existe no servidor (`sessionEnded`): não há o que encerrar.
 * Outros erros do `logout` são repassados (com o push já desligado).
 */
export async function disablePushAndLogout(
  logout: () => Promise<void>,
  expectedUserId: string | null | undefined,
): Promise<LogoutResult> {
  const sessionFailure: LogoutResult = { revocation: { status: "failed", reason: "session" }, loggedOut: false, sessionChanged: true };
  if (!expectedUserId) return sessionFailure;
  const token = beginDisabling();
  try {
    return await withWriteLock(async () => {
      let revocation: PushRevocation;
      try {
        revocation = await revokeUnderLock(expectedUserId);
      } finally {
        endDisabling(token); // ainda sob o bloqueio
      }
      if (revocation.status === "failed" && revocation.reason === "session") return sessionFailure;
      // Sessão já encerrada no servidor: o logout também daria 401. Nada local muda (sem 204 não há confirmação).
      if (revocation.status === "failed" && revocation.reason === "unauthenticated") {
        return { revocation, loggedOut: false, sessionEnded: true };
      }
      if (!canLogoutAfter(revocation)) return { revocation, loggedOut: false };
      try {
        await logout();
      } catch (e) {
        // A conta mudou entre o DELETE e o logout: a sessão vigente (de outra conta) não é encerrada.
        if (isSessionChanged(e)) return { revocation, loggedOut: false, sessionChanged: true };
        // Sessão já revogada (ex.: logout em outra aba) ou expirada: não há o que encerrar; equivale a ter saído.
        if (isUnauthenticated(e)) return { revocation, loggedOut: true, sessionEnded: true };
        throw e;
      }
      return { revocation, loggedOut: true };
    }, DISABLE_LOCK_TIMEOUT_MS);
  } catch (e) {
    endDisabling(token); // idempotente; só o próprio token
    if (e instanceof LockTimeoutError) return { revocation: { status: "failed", reason: "busy" }, loggedOut: false };
    throw e;
  }
}

/**
 * Núcleo da revogação; precisa ser chamado com o bloqueio de gravação (serializa com toda gravação, em todas as abas).
 * Ordem: 1) lê a inscrição; 2) DELETE com a conta da ação, que só segue com 204; 3) só então revoga localmente:
 * marca, sequência e marcador DA CONTA, cancelamento confirmado das ativações pendentes DA CONTA e, por fim,
 * cancelamento da inscrição no navegador. Uma revogação recusada (outra conta, erro) não afeta nada local.
 * Nenhuma ativação grava entre o DELETE e o cancelamento: todas esperam este mesmo bloqueio.
 */
async function revokeUnderLock(expectedUserId: string): Promise<PushRevocation> {
  let sub: PushSubscription | null;
  try {
    sub = await getSubscription();
  } catch {
    // Não deu para ler o service worker ou a inscrição: o push pode continuar ativo.
    return revocationOutcome({ readFailed: true, subscriptionFound: false, serverRevoked: false, browserRevoked: false });
  }
  if (sub) {
    try {
      await api.unsubscribePush(sub.endpoint, expectedUserId); // resolve só com 204
    } catch (e) {
      // Cookie de outra conta: a API não removeu nada. Para aqui, sem tocar a inscrição local (pode ser da outra conta).
      if (isSessionChanged(e)) return { status: "failed", reason: "session" };
      // Sessão já encerrada no servidor (revogada, expirada, cookie sem sessão): não confirmou. Nada local muda.
      if (isUnauthenticated(e)) return { status: "failed", reason: "unauthenticated" };
      // Qualquer outro status ou erro (404, 5xx, outro 409, rede): remoção não confirmada. Nada local muda.
      return { status: "failed", reason: "server" };
    }
  }
  // Conta confirmada pelo servidor (ou nada a apagar). Regrava a marca dentro do bloqueio (um ligar concluído
  // enquanto esperávamos pode tê-la removido) e avança a sequência da conta.
  const flagDurable = markOptedOut() && bumpRevokeSeq(expectedUserId);
  // Marcador de revogação da conta: invalida ativações dela já iniciadas enquanto esta aba existir.
  await holdRevocationMarker(expectedUserId);
  // Cancela, com confirmação, as ativações pendentes da conta em qualquer aba. Não depende de localStorage nem de
  // esta aba continuar aberta (o cancelamento fica na aba que ativava).
  const allCancelled = await invalidatePendingActivations(expectedUserId);
  const outcome = sub ? await cancelLocally(sub) : revocationOutcome({ readFailed: false, subscriptionFound: false, serverRevoked: false, browserRevoked: false });
  // Falha segura: ativação pendente que não confirmou o cancelamento (ou sem como saber) poderia gravar depois
  // do DELETE. A revogação não é final e o logout fica bloqueado.
  if (!allCancelled) return { status: "failed", reason: "pending" };
  return requireDurableFlag(outcome, flagDurable);
}

/**
 * Revogação só no servidor (inscrição local restante) depende da marca de desligado para não ser desfeita
 * por uma sincronização posterior. Se a marca não pôde ser gravada e confirmada, o resultado não é final: falha.
 * Com a inscrição cancelada no navegador, nenhuma aba consegue regravar e a marca é dispensável.
 */
function requireDurableFlag(outcome: PushRevocation, flagDurable: boolean): PushRevocation {
  if (flagDurable) return outcome;
  if (outcome.status === "partial" && !outcome.browserRevoked) return { status: "failed", reason: "storage" };
  return outcome;
}

/** Cancela a inscrição no navegador; só é chamado depois do DELETE confirmado (204). */
async function cancelLocally(sub: PushSubscription): Promise<PushRevocation> {
  let browserRevoked: boolean;
  try {
    browserRevoked = await sub.unsubscribe();
  } catch {
    browserRevoked = false;
  }
  try {
    if (browserRevoked) localStorage.removeItem(KEY_ID_STORAGE);
  } catch {
    /* sem armazenamento */
  }
  // Remoção confirmada no servidor: a tela pode mostrar "Ativar" com a inscrição local restante.
  await rememberServerRevocation(sub.endpoint, true);
  return revocationOutcome({ readFailed: false, subscriptionFound: true, serverRevoked: true, browserRevoked });
}

export type SyncResult = "synced" | "resubscribed" | "skipped" | "cancelled";

/**
 * A cada início do app (permissão já concedida): renova a inscrição no servidor; se a chave do servidor mudou
 * (inscrição "stale"), refaz a inscrição sozinho com a chave nova. Nunca pede permissão.
 * Grava só com o bloqueio entre abas e com a marca de desligado ausente; sem Web Locks, não grava.
 * `expectedUserId` é a conta da sessão capturada antes de qualquer espera; sem ela, não sincroniza.
 * Se a API responder 409 (cookie de outra conta), termina "cancelled" sem gravar para essa conta.
 */
export async function syncPushOnStart(serverKeyId: string | null, expectedUserId: string | null | undefined): Promise<SyncResult> {
  if (!expectedUserId) return "skipped"; // sem identidade capturada: não sincroniza nem inscreve
  const userId = expectedUserId;
  if (currentPermission() !== "granted" || syncBlocked()) return "skipped";
  if (!webLocks()) return "skipped"; // fallback seguro: sem coordenação entre abas, nada de gravação automática
  const reg = await registerServiceWorker();
  if (!reg) return "skipped";
  return withWriteLock(async (): Promise<SyncResult> => {
    if (syncBlocked()) return "cancelled"; // desligado (ou desligando) enquanto esperávamos o bloqueio
    let sub = await reg.pushManager.getSubscription();
    if (!sub) return "skipped"; // sem inscrição neste aparelho: respeitar
    if (syncBlocked()) return "cancelled";
    const stored = localStorage.getItem(KEY_ID_STORAGE);
    if (serverKeyId && stored && stored !== serverKeyId) {
      // DELETE com a conta original; só cancela a inscrição local depois de o servidor confirmar (204).
      try {
        await api.unsubscribePush(sub.endpoint, userId);
      } catch (e) {
        // Cookie de outra conta: nada foi removido. Para sem tocar a inscrição local nem o estado remoto.
        if (isSessionChanged(e)) return "cancelled";
        throw e;
      }
      if (!(await sub.unsubscribe())) throw new Error("Não foi possível trocar a inscrição antiga neste aparelho.");
      if (syncBlocked()) return "cancelled";
      const fresh = await subscribeWithServerKey(reg);
      sub = fresh;
      if (syncBlocked()) {
        await fresh.unsubscribe().catch(() => false); // desligado no meio: não deixa inscrição nova no navegador
        return "cancelled";
      }
      try {
        await sendToServer(fresh, userId);
      } catch (e) {
        if (e instanceof SessionChangedError) {
          await fresh.unsubscribe().catch(() => false); // conta mudou: a inscrição nova não fica sem dono
          return "cancelled";
        }
        throw e;
      }
      return "resubscribed";
    }
    try {
      await sendToServer(sub, userId);
    } catch (e) {
      if (e instanceof SessionChangedError) return "cancelled"; // outra conta no cookie: não grava para ela
      throw e;
    }
    return "synced";
  });
}

export function isIosPlatform() {
  return currentPlatform().isIos;
}
