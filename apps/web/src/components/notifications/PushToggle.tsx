"use client";

import { useCallback, useEffect, useState } from "react";
import { primaryButton, secondaryButton } from "@/components/admin/ui";
import { pushState, revocationMessage, type PushState } from "@/lib/notifications";
import { useSession } from "@/lib/useSession";
import {
  canCoordinatePush,
  currentPermission,
  currentPlatform,
  disablePush,
  enablePush,
  getSubscription,
  isServerRevoked,
  isStandaloneDisplay,
  pushCapabilities,
} from "@/lib/push";

const HELP: Record<PushState, string> = {
  unsupported: "Este navegador não recebe notificações push. Os avisos continuam aparecendo no sininho do app.",
  "ios-needs-install":
    "No iPhone, os avisos no celular só funcionam com o app instalado na Tela de Início (iOS 16.4 ou mais novo). Instale e abra pelo ícone para ativar.",
  "server-disabled": "O envio de push está desligado no servidor agora. Os avisos continuam no sininho do app.",
  denied:
    "As notificações foram bloqueadas neste navegador. Para reativar: toque no cadeado ao lado do endereço (ou nas Configurações do app/navegador) → Notificações → Permitir, e volte aqui.",
  "needs-update":
    "Este navegador é antigo demais para ativar os avisos no celular com segurança. Atualize o navegador (no iPhone, iOS 16.4 ou mais novo) para ativar. Os avisos continuam no sininho do app.",
  off: "Receba no celular os avisos de lista aberta, promoção da fila, sorteio e fim da pelada.",
  on: "Avisos ativados neste aparelho.",
};

/** Liga/desliga o push deste aparelho. A permissão só é pedida no toque em "Ativar". */
export function PushToggle({ serverMode, onChange }: { serverMode: string | null; onChange?: () => void }) {
  // Usuário da sessão no momento do toque: a ativação fica vinculada a ele (troca de conta no meio = descarta).
  const { user } = useSession(false);
  const [state, setState] = useState<PushState | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [msgIsError, setMsgIsError] = useState(false);
  // Falha ao ler o service worker/inscrição: o estado real é desconhecido (nunca mostrar "Desativado").
  const [readFailed, setReadFailed] = useState(false);

  const refresh = useCallback(async () => {
    let sub: PushSubscription | null;
    try {
      sub = await getSubscription();
    } catch {
      setReadFailed(true);
      setState((s) => s ?? "on");
      return;
    }
    setReadFailed(false);
    const serverRevoked = !!sub && (await isServerRevoked(sub.endpoint));
    setState(
      pushState({
        ...pushCapabilities(),
        platform: currentPlatform(),
        standalone: isStandaloneDisplay(),
        permission: currentPermission(),
        serverMode,
        subscribed: !!sub,
        // "Ativar" com inscrição local restante só se a remoção no servidor foi confirmada; resultado
        // incerto ou falho mantém "Desativar" para tentar de novo.
        serverRevoked,
        hasLocks: canCoordinatePush(),
      }),
    );
  }, [serverMode]);

  useEffect(() => {
    refresh().catch(() => setState("unsupported"));
  }, [refresh]);

  async function enable() {
    setBusy(true);
    setMsg(null);
    setMsgIsError(false);
    const r = await enablePush(user?.id); // primeira chamada do toque: pede a permissão antes de qualquer espera
    if (!r.ok) {
      setMsg(
        r.reason === "denied"
          ? "Permissão negada. Veja abaixo como reativar."
          : r.reason === "dismissed"
            ? "Você fechou o pedido de permissão. Toque em Ativar de novo quando quiser."
            : r.reason === "storage"
              ? "Não foi possível ativar: o armazenamento do navegador está indisponível. Libere espaço ou saia do modo privado e tente de novo."
              : r.reason === "cancelled"
              ? "Os avisos foram desligados enquanto você ativava. Toque em Ativar de novo se quiser recebê-los."
              : r.reason === "needs-update"
              ? "Não é possível ativar neste navegador. Atualize-o para receber os avisos no celular."
              : (r.message ?? "Não foi possível ativar neste aparelho."),
      );
    } else setMsg("Pronto! Este aparelho vai receber os avisos.");
    await refresh();
    setBusy(false);
    onChange?.();
  }

  async function disable() {
    setBusy(true);
    setMsg(null);
    setMsgIsError(false);
    const r = await disablePush(user?.id); // conta do toque: o DELETE só vale para ela
    // Só anuncia sucesso com a revogação confirmada; a falha mantém o estado real ("Ativado").
    setMsgIsError(r.status === "failed");
    setMsg(revocationMessage(r, "toggle"));
    await refresh().catch(() => undefined);
    setBusy(false);
    onChange?.();
  }

  if (!state) return null;
  if (readFailed) {
    return (
      <div className="flex flex-col gap-2 rounded-xl border border-outline-variant bg-surface-container-low p-md">
        <p className="text-body-md font-semibold">🔔 Avisos no celular</p>
        <p role="alert" className="rounded-lg bg-danger-bg p-2 text-body-md text-danger-strong">
          Não foi possível verificar os avisos deste aparelho. Eles podem continuar ativos.
        </p>
        <div className="flex flex-wrap gap-2">
          <button type="button" disabled={busy} className={secondaryButton} onClick={() => refresh().catch(() => undefined)}>
            Verificar de novo
          </button>
          <button type="button" disabled={busy} className={secondaryButton} onClick={disable}>
            {busy ? "Desativando…" : "Desativar neste aparelho"}
          </button>
        </div>
        {msg ? (
          <p role={msgIsError ? "alert" : "status"} className="text-body-md">
            {msg}
          </p>
        ) : null}
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-2 rounded-xl border border-outline-variant bg-surface-container-low p-md">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-body-md font-semibold">🔔 Avisos no celular</p>
        <span className={`rounded-full px-2 py-0.5 text-label-md ${state === "on" ? "bg-green-100 text-green-900" : "bg-surface-container text-on-surface-variant"}`}>
          {state === "on" ? "Ativado" : "Desativado"}
        </span>
      </div>
      <p className="text-body-md text-on-surface-variant">{HELP[state]}</p>
      {state === "off" ? (
        <button type="button" disabled={busy} className={`${primaryButton} self-start`} onClick={enable}>
          {busy ? "Ativando…" : "Ativar avisos neste aparelho"}
        </button>
      ) : null}
      {state === "on" ? (
        <button type="button" disabled={busy} className={`${secondaryButton} self-start`} onClick={disable}>
          {busy ? "Desativando…" : "Desativar neste aparelho"}
        </button>
      ) : null}
      {msg ? (
        <p
          role={msgIsError ? "alert" : "status"}
          className={msgIsError ? "rounded-lg bg-danger-bg p-2 text-body-md text-danger-strong" : "text-body-md"}
        >
          {msg}
        </p>
      ) : null}
    </div>
  );
}
