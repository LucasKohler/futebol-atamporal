type FocusNode = { ownerDocument?: { body?: unknown } | null };
type FocusRoot = { contains: (node: never) => boolean };

/**
 * Ao fechar um painel, devolve o foco ao gatilho só se o foco se perdeu (foi para o body porque o conteúdo
 * focado desmontou) ou ainda está dentro do painel. Se o usuário já focou outra coisa (ex.: abriu outro jogador),
 * não rouba o foco.
 */
type Focusable = { isConnected?: boolean; focus: () => void };

/**
 * Para onde devolver o foco ao fechar um painel (checkout Pix): o gatilho original se ainda está na página;
 * senão o primeiro candidato existente (ex.: o mesmo botão re-renderizado, ou o cartão do item quando ele foi pago
 * e o botão sumiu). Null se nada existir.
 */
export function resolveReturnTarget<T extends Focusable>(saved: T | null, candidates: (T | null | undefined)[]): T | null {
  if (saved && saved.isConnected) return saved;
  return candidates.find((c): c is T => !!c && c.isConnected !== false) ?? null;
}

/** Seletores dos alvos de retorno de um item com checkout Pix. */
export function pixReturnSelectors(key: string): string[] {
  const safe = key.replace(/["\\]/g, "");
  return [`[data-pix-trigger="${safe}"]`, `[data-pix-item="${safe}"]`];
}

export function shouldRestoreFocus(active: FocusNode | null, root: FocusRoot | null): boolean {
  if (!active || active === active.ownerDocument?.body) return true;
  return !!root && root.contains(active as never);
}
