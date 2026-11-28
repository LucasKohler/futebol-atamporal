import type { PixCharge } from "./api";

export type CheckoutOpen = { key: string; charge: PixCharge; regenerate: () => Promise<void> };

/**
 * Sessões do checkout Pix na folha de Pagamentos. Cada abertura cria uma sessão; fechar a folha ou abrir outro
 * checkout a invalida. Uma resposta tardia de "gerar código" (inclusive "Gerar novo código Pix" dentro da folha)
 * só é aplicada se a sessão que a pediu ainda é a atual: assim ela nunca reabre uma folha fechada, nunca troca um
 * checkout aberto depois e nunca rouba o foco que o usuário deixou no gatilho.
 */
export function createCheckoutSessions(deps: {
  /** Abre/atualiza a folha. */
  open: (o: CheckoutOpen) => void;
  /** Marca/desmarca o botão ocupado (null = nenhum). */
  setBusy: (update: (current: string | null) => string | null) => void;
  /** Mensagem de erro da ação (null limpa). */
  setError: (msg: string | null) => void;
  /** Recarrega a lista (ex.: 409 já pago). */
  reload: () => Promise<void>;
  now?: () => number;
}) {
  let current = 0;
  const now = deps.now ?? Date.now;

  /** Abre o checkout de `key`: reusa o Pix pendente válido ou gera um novo. */
  async function start(key: string, create: () => Promise<PixCharge>, existing: PixCharge | null) {
    const session = ++current; // abrir outro checkout invalida qualquer operação anterior
    deps.setError(null);
    const regenerate = async () => {
      const charge = await create();
      if (session !== current) return; // folha fechada ou substituída enquanto esperava: descarta
      deps.open({ key, charge, regenerate });
    };
    if (existing && existing.status === "Pending" && Date.parse(existing.expiresAt) > now()) {
      deps.open({ key, charge: existing, regenerate });
      return;
    }
    deps.setBusy(() => key);
    try {
      await regenerate();
    } catch (e) {
      if (session !== current) return; // erro de uma operação descartada não aparece na tela
      deps.setError(e instanceof Error ? e.message : "Não foi possível gerar o Pix.");
      await deps.reload().catch(() => undefined);
    } finally {
      deps.setBusy((b) => (b === key ? null : b));
    }
  }

  /** Fechou a folha: invalida toda operação pendente desta sessão. */
  function close() {
    current++;
  }

  return { start, close };
}
