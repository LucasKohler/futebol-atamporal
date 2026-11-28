"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { focusRing, secondaryButton } from "@/components/admin/ui";
import { api, type AppNotification, type MyNotifications } from "@/lib/api";
import { BELL_POLL_MS, CATEGORY_ICON, relativeTime, startsWithEmoji } from "@/lib/notifications";
import { registerServiceWorker, syncPushOnStart } from "@/lib/push";
import { useSession } from "@/lib/useSession";
import { PushToggle } from "./PushToggle";

/**
 * Sininho do AppShell: contador de não lidas (atualiza a cada 60 s só com a aba visível e ao voltar o foco),
 * drawer com foco preso e retorno do foco ao sininho, marcar como lida e ativar/desativar o push do aparelho.
 */
export function NotificationBell({ groupId }: { groupId: string }) {
  const router = useRouter();
  const [data, setData] = useState<MyNotifications | null>(null);
  const [open, setOpen] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const bellRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const synced = useRef(false);
  // Conta da sessão no momento em que a carga começa: a sincronização automática fica vinculada a ela.
  const { user } = useSession(false);
  const sessionUserId = user?.id ?? null;

  const load = useCallback(async () => {
    const expectedUserId = sessionUserId; // capturado antes de qualquer espera
    try {
      const d = await api.myNotifications(groupId, { limit: 20 });
      setData((prev) => (prev && open ? { ...d, items: mergeItems(d.items, prev.items) } : d));
      setError(null);
      // Uma vez por início do app: renova a inscrição (e refaz se a chave do servidor mudou). Nunca pede permissão.
      // Sem identidade capturada, não sincroniza nem inscreve (tenta de novo numa próxima carga com sessão).
      if (!synced.current && expectedUserId) {
        synced.current = true;
        registerServiceWorker().then(() => syncPushOnStart(d.push.keyId, expectedUserId)).catch(() => undefined);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao carregar avisos.");
    }
  }, [groupId, open, sessionUserId]);

  useEffect(() => {
    load();
    const tick = () => {
      if (document.visibilityState === "visible") load();
    };
    const t = window.setInterval(tick, BELL_POLL_MS);
    const onVisible = () => tick();
    document.addEventListener("visibilitychange", onVisible);
    window.addEventListener("focus", onVisible);
    return () => {
      window.clearInterval(t);
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("focus", onVisible);
    };
  }, [load]);

  // Foco preso no drawer; Esc fecha; ao fechar, o foco volta ao sininho.
  useEffect(() => {
    if (!open) return;
    const panel = panelRef.current;
    panel?.querySelector<HTMLElement>("[data-autofocus]")?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        close();
        return;
      }
      if (e.key !== "Tab" || !panel) return;
      const items = [...panel.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])')];
      if (items.length === 0) return;
      const first = items[0];
      const last = items[items.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  function close() {
    setOpen(false);
    window.setTimeout(() => bellRef.current?.focus(), 0);
  }

  async function openItem(n: AppNotification) {
    if (!n.readAt) {
      setData((d) => (d ? { ...d, unreadCount: Math.max(0, d.unreadCount - 1), items: d.items.map((x) => (x.id === n.id ? { ...x, readAt: new Date().toISOString() } : x)) } : d));
      api.readNotification(n.id).catch(() => undefined);
    }
    setOpen(false);
    router.push(n.actionUrl);
  }

  async function readAll() {
    try {
      await api.readAllNotifications(groupId);
      setData((d) => (d ? { ...d, unreadCount: 0, items: d.items.map((x) => ({ ...x, readAt: x.readAt ?? new Date().toISOString() })) } : d));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro.");
    }
  }

  async function more() {
    if (!data?.nextCursor) return;
    setLoadingMore(true);
    try {
      const d = await api.myNotifications(groupId, { before: data.nextCursor, limit: 20 });
      setData((prev) => (prev ? { ...prev, items: mergeItems(prev.items, d.items), nextCursor: d.nextCursor } : d));
    } finally {
      setLoadingMore(false);
    }
  }

  const unread = data?.unreadCount ?? 0;
  const now = Date.now();
  return (
    <>
      <button
        ref={bellRef}
        type="button"
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={unread > 0 ? `Avisos: ${unread} não lido${unread > 1 ? "s" : ""}` : "Avisos"}
        className={`relative flex h-11 w-11 items-center justify-center rounded-full text-on-surface ${focusRing}`}
      >
        <svg aria-hidden="true" viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
          <path d="M6 8a6 6 0 1 1 12 0c0 7 3 8 3 8H3s3-1 3-8M10.3 20a2 2 0 0 0 3.4 0" />
        </svg>
        {unread > 0 ? (
          <span aria-hidden="true" className="absolute -right-0.5 -top-0.5 min-w-5 rounded-full bg-error px-1 text-center text-label-sm leading-5 text-white">
            {unread > 99 ? "99+" : unread}
          </span>
        ) : null}
      </button>

      {open && typeof document !== "undefined"
        ? createPortal(
        <div className="fixed inset-0 z-50 flex justify-end bg-black/40" onClick={(e) => e.target === e.currentTarget && close()}>
          <div
            ref={panelRef}
            role="dialog"
            aria-modal="true"
            aria-labelledby="avisos-titulo"
            className="flex h-full w-full max-w-md flex-col gap-md overflow-y-auto bg-background p-md shadow-xl"
          >
            <div className="flex items-center justify-between gap-2">
              <h2 id="avisos-titulo" className="text-headline-md">
                Avisos
              </h2>
              <button type="button" data-autofocus onClick={close} className={`min-h-11 min-w-11 rounded-full text-headline-md ${focusRing}`} aria-label="Fechar avisos">
                ×
              </button>
            </div>

            <PushToggle serverMode={data?.push.mode ?? null} />

            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-label-md text-on-surface-variant">{unread > 0 ? `${unread} não lido${unread > 1 ? "s" : ""}` : "Tudo lido"}</p>
              <button type="button" disabled={unread === 0} onClick={readAll} className={secondaryButton}>
                Marcar todas como lidas
              </button>
            </div>

            {error ? (
              <p role="alert" className="text-body-md text-on-error-container">
                {error}
              </p>
            ) : null}

            {!data ? (
              <p className="text-body-md text-on-surface-variant">Carregando…</p>
            ) : data.items.length === 0 ? (
              <p className="rounded-xl border border-dashed border-outline-variant p-md text-body-md text-on-surface-variant">
                Nenhum aviso ainda. Você verá aqui a lista aberta, promoções da fila, sorteios e o fim da pelada.
              </p>
            ) : (
              <ul className="flex flex-col gap-1">
                {data.items.map((n) => (
                  <li key={n.id}>
                    <button
                      type="button"
                      onClick={() => openItem(n)}
                      className={`flex w-full items-start gap-3 rounded-xl border p-sm text-left ${n.readAt ? "border-outline-variant bg-surface-container-lowest" : "border-pitch-green-container bg-surface-container-low"} ${focusRing}`}
                    >
                      <span aria-hidden="true" className="w-7 text-headline-md">
                        {iconFor(n)}
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center justify-between gap-2">
                          <span className={`text-body-md ${n.readAt ? "" : "font-semibold"}`}>{n.title}</span>
                          <span className="shrink-0 text-label-md text-on-surface-variant">{relativeTime(n.createdAt, now)}</span>
                        </span>
                        <span className="block whitespace-pre-line break-words text-body-md text-on-surface-variant">{n.body}</span>
                        {!n.readAt ? <span className="sr-only">(não lido)</span> : null}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
            {data?.nextCursor ? (
              <button type="button" disabled={loadingMore} onClick={more} className={`${secondaryButton} self-center`}>
                {loadingMore ? "Carregando…" : "Ver mais antigos"}
              </button>
            ) : null}
          </div>
        </div>,
        // Portal no body: fora do contexto de empilhamento do header (a nav inferior não cobre o drawer).
        document.body,
      )
        : null}
    </>
  );
}

/** Ícone da categoria, sem repetir quando o título do servidor já começa com ele. */
function iconFor(n: AppNotification) {
  return startsWithEmoji(n.title) ? "" : (CATEGORY_ICON[n.category] ?? "🔔");
}

function mergeItems(a: AppNotification[], b: AppNotification[]) {
  const seen = new Set<string>();
  return [...a, ...b].filter((n) => (seen.has(n.id) ? false : (seen.add(n.id), true)));
}
