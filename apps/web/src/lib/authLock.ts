/**
 * Serializa entre abas toda requisição que grava ou apaga o cookie de sessão (login, cadastro, configuração e
 * recuperação de admin, logout). Sem isso, a resposta de um logout de A pode chegar DEPOIS da resposta do login
 * de B em outra aba e expirar o cookie que B acabou de receber (o navegador aplica os Set-Cookie na ordem de
 * chegada). Com o bloqueio, a requisição seguinte só sai depois de a anterior ter sido respondida e aplicada.
 *
 * - Web Locks (vale para todas as abas da origem): bloqueio exclusivo `fa.auth.cookie`.
 * - Bloqueio ocupado além do prazo: desiste SEM enviar a requisição (AuthBusyError); a tela pede nova tentativa.
 * - Falha ao pedir o bloqueio: a requisição também não sai (erro repassado).
 * - Navegador sem Web Locks: só serializa na própria aba. Bloquear o logout nesse caso deixaria a sessão aberta
 *   num aparelho compartilhado, risco maior que a corrida entre abas (que só pode desconectar a outra conta).
 */
export const AUTH_COOKIE_LOCK = "fa.auth.cookie";
export const AUTH_LOCK_TIMEOUT_MS = 15_000;

export class AuthBusyError extends Error {
  constructor() {
    super("Outra aba está entrando ou saindo da conta. Aguarde um instante e tente de novo.");
    this.name = "AuthBusyError";
  }
}

function webLocks(): LockManager | null {
  try {
    const locks = typeof navigator !== "undefined" ? (navigator as Navigator & { locks?: LockManager }).locks : undefined;
    return locks && typeof locks.request === "function" ? locks : null;
  } catch {
    return null;
  }
}

let localChain: Promise<unknown> = Promise.resolve();

export function withAuthCookieLock<T>(fn: () => Promise<T>, timeoutMs: number = AUTH_LOCK_TIMEOUT_MS): Promise<T> {
  const locks = webLocks();
  if (!locks) {
    const run = localChain.then(fn, fn);
    localChain = run.catch(() => undefined);
    return run;
  }
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  return (
    locks.request(AUTH_COOKIE_LOCK, { mode: "exclusive", signal: controller.signal }, () => {
      clearTimeout(timer);
      return fn();
    }) as Promise<T>
  ).catch((e: unknown) => {
    clearTimeout(timer);
    if (controller.signal.aborted) throw new AuthBusyError();
    throw e;
  });
}
