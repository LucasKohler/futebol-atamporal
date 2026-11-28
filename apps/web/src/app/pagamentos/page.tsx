"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { AdminEmpty, AdminError, AdminLoading, StatusBadge, type BadgeTone } from "@/components/admin/AdminCard";
import { focusRing, primaryButton } from "@/components/admin/ui";
import { PixCheckout } from "@/components/pix/PixCheckout";
import { Sheet } from "@/components/Sheet";
import { usePixFocusReturn } from "@/components/pix/usePixFocusReturn";
import { formatMoney } from "@/lib/adminFilters";
import { api, MyMatchDayPayment, MyPayments, PixCharge, type PaymentSource } from "@/lib/api";
import { dueAtText, isLaunchedOpenFee, isSettled, matchFeeBadge } from "@/lib/pix";
import { createCheckoutSessions } from "@/lib/pixCheckoutSession";
import { monthName } from "@/lib/season";
import { useSession } from "@/lib/useSession";

const OBLIGATION: Record<string, { label: string; tone: BadgeTone }> = {
  Pending: { label: "Pendente", tone: "warning" },
  Overdue: { label: "Atrasada", tone: "danger" },
  Paid: { label: "Paga", tone: "success" },
  Exempt: { label: "Isenta", tone: "info" },
  Cancelled: { label: "Cancelada", tone: "neutral" },
};

function sourceText(source: PaymentSource, paidAt: string | null) {
  const when = paidAt ? ` em ${new Date(paidAt).toLocaleDateString("pt-BR")}` : "";
  return source === "Pix" ? `Pago via Pix${when}` : source === "Manual" ? `Baixa registrada pelo admin${when}` : "";
}

type Open = { key: string; charge: PixCharge; regenerate: () => Promise<void> };

/** Atrasadas primeiro, depois as lançadas do dia; o resto mantém a ordem da API. */
function feeRank(d: MyMatchDayPayment) {
  if (d.status === "Overdue") return 0;
  if (isLaunchedOpenFee(d)) return 1;
  return 2;
}

/** Texto de apoio da diária conforme o ciclo (lançamento no fechamento, vencimento, devolução). */
function feeNote(d: MyMatchDayPayment, timeZone: string | undefined): string | null {
  if (d.status === "Overdue") return d.dueAt ? `Venceu ${dueAtText(d.dueAt, timeZone).replace("até", "às")}.` : "Diária vencida.";
  if (d.status === "RefundPending") return "Sua presença foi corrigida para ausente. O admin vai devolver o valor pago.";
  if (d.status === "Refunded") return "Valor devolvido pelo admin.";
  if (d.status === "Cancelled") return "Cobrança removida pelo admin.";
  if (d.status === "Pending" && d.dueAt) return `Pague ${dueAtText(d.dueAt, timeZone)}.`;
  if (d.status === "Pending" || d.status === "NotCharged") return "A cobrança é lançada quando o admin fecha a rodada.";
  return null;
}

/** Pagamentos do jogador: mensalidade da competência e diárias, com checkout Pix. */
export default function PagamentosPage() {
  const { group, loading } = useSession();
  const [data, setData] = useState<MyPayments | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [open, setOpen] = useState<Open | null>(null);
  const { remember, restore } = usePixFocusReturn();

  const load = useCallback(async () => {
    if (!group) return;
    setError(null);
    setData(await api.myPayments(group.id));
  }, [group]);

  useEffect(() => {
    load().catch((e) => setError(e instanceof Error ? e.message : "Não foi possível carregar seus pagamentos."));
  }, [load]);

  // Vindo da Home ("Pagar R$ X com Pix"): abre o checkout da diária pedida uma única vez.
  const autoOpened = useRef(false);
  useEffect(() => {
    if (!data || autoOpened.current) return;
    autoOpened.current = true;
    const matchId = new URLSearchParams(window.location.search).get("diaria");
    if (!matchId) return;
    window.history.replaceState(null, "", window.location.pathname);
    const d = data.matchDays.find((x) => x.matchId === matchId);
    const off = !data.gateway.isConfigured || data.gateway.mode === "Unconfigured";
    if (d && isLaunchedOpenFee(d) && !off) start(`d:${d.matchId}`, () => api.createMatchDayPix(d.matchId), d.activePix);
  }, [data]); // eslint-disable-line react-hooks/exhaustive-deps

  // Sessões do checkout: fechar ou abrir outro invalida respostas tardias (ver pixCheckoutSession).
  const loadRef = useRef(load);
  loadRef.current = load;
  const sessions = useRef<ReturnType<typeof createCheckoutSessions> | null>(null);
  sessions.current ??= createCheckoutSessions({
    open: (o) => setOpen(o),
    setBusy: (update) => setBusyKey(update),
    setError: (msg) => setActionError(msg),
    reload: () => loadRef.current(),
  });

  function start(key: string, create: () => Promise<PixCharge>, existing: PixCharge | null) {
    remember(); // gatilho atual; volta a ele ao fechar o checkout
    return sessions.current!.start(key, create, existing);
  }

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;

  const pixOff = data && (!data.gateway.isConfigured || data.gateway.mode === "Unconfigured");
  const nothing = data && data.memberships.length === 0 && data.matchDays.length === 0;
  const matchDays = data ? [...data.matchDays].sort((a, b) => feeRank(a) - feeRank(b)) : [];

  function closeCheckout() {
    if (!open) return;
    const key = open.key;
    sessions.current?.close(); // "Gerar novo código" ainda pendente não reabre a folha
    setOpen(null);
    restore(key);
    // Atualiza status e o Pix ativo (o botão passa a "Continuar pagamento Pix").
    load().catch(() => undefined);
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-lg">
        <div>
          <p className="text-label-sm uppercase text-on-surface-variant">Pagamentos & Pix</p>
          <h1 className="text-headline-lg">Meus pagamentos</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">Mensalidade e diárias da pelada. Pague com Pix e a confirmação aparece aqui sozinha.</p>
        </div>

        {error ? <AdminError onRetry={() => load().catch(() => undefined)}>{error}</AdminError> : null}
        {actionError ? <AdminError>{actionError}</AdminError> : null}
        {pixOff ? (
          <p role="status" className="rounded-lg border border-warning bg-warning-subtle p-3 text-body-md text-warning-strong">
            O pagamento por Pix está indisponível no momento. Fale com o admin para pagar de outra forma.
          </p>
        ) : null}
        {data?.gateway.isSimulated ? (
          <p className="rounded-lg border border-warning bg-warning-subtle p-2 text-label-md text-warning-strong">Ambiente de teste: os Pix gerados aqui são simulados.</p>
        ) : null}

        {!data ? (
          error ? null : <AdminLoading label="Carregando seus pagamentos…" />
        ) : nothing ? (
          <AdminEmpty>Nada a pagar agora. Mensalidades e diárias aparecem aqui quando forem lançadas.</AdminEmpty>
        ) : (
          <>
            {data.memberships.length > 0 ? (
              <section aria-labelledby="mensalidades-titulo" className="flex flex-col gap-sm">
                <h2 id="mensalidades-titulo" className="text-headline-md">
                  Mensalidade
                </h2>
                <ul className="flex flex-col gap-sm">
                  {data.memberships.map((m) => {
                    const key = `m:${m.paymentId}`;
                    const ob = OBLIGATION[m.status] ?? { label: m.status, tone: "neutral" as BadgeTone };
                    const settled = isSettled(m.status);
                    const hasActive = !!m.activePix && m.activePix.status === "Pending";
                    return (
                      <li key={key} data-pix-item={key} tabIndex={-1} className={`rounded-xl border border-outline-variant bg-surface-container-lowest p-md shadow-elevation-1 ${focusRing}`}>
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div>
                            <p className="text-body-lg font-semibold">
                              {monthName(m.month)} de {m.year}
                            </p>
                            <p className="text-headline-md">{formatMoney(m.amount)}</p>
                            {settled ? <p className="text-label-md text-on-surface-variant">{sourceText(m.paymentSource, m.paidAt)}</p> : null}
                          </div>
                          <StatusBadge tone={ob.tone}>{m.status === "Paid" ? "Mensalidade paga" : ob.label}</StatusBadge>
                        </div>
                        {!settled && !pixOff && open?.key !== key ? (
                          <button
                            type="button"
                            disabled={busyKey === key}
                            className={`${primaryButton} mt-md w-full sm:w-auto`}
                            data-pix-trigger={key}
                            onClick={() => start(key, () => api.createMembershipPix(m.paymentId), m.activePix)}
                          >
                            {busyKey === key ? "Gerando Pix…" : hasActive ? "Continuar pagamento Pix" : `Pagar ${formatMoney(m.amount)} com Pix`}
                          </button>
                        ) : null}
                      </li>
                    );
                  })}
                </ul>
              </section>
            ) : null}

            {matchDays.length > 0 ? (
              <section aria-labelledby="diarias-titulo" className="flex flex-col gap-sm">
                <h2 id="diarias-titulo" className="text-headline-md">
                  Diárias
                </h2>
                <ul className="flex flex-col gap-sm">
                  {matchDays.map((d) => {
                    const key = `d:${d.matchId}`;
                    const ob = matchFeeBadge(d.status, d.dueAt);
                    const note = feeNote(d, group?.timeZoneId);
                    // Decisão do Lucas: diária só existe depois que o admin fecha a rodada/dia (dueAt). Sem dueAt, sem Pix.
                    const payable = isLaunchedOpenFee(d);
                    const notLaunched = d.status === "NotCharged" || (d.status === "Pending" && !d.dueAt);
                    const hasActive = !!d.activePix && d.activePix.status === "Pending";
                    return (
                      <li
                        key={key}
                        data-pix-item={key}
                        tabIndex={-1}
                        // 07c: cards de diária em elevation-1; Atrasada mantém a borda vermelha (estado) e ganha a sombra.
                        className={`rounded-xl border bg-surface-container-lowest p-md shadow-elevation-1 ${d.status === "Overdue" ? "border-danger" : "border-outline-variant"} ${focusRing}`}
                      >
                        <div className="flex flex-wrap items-start justify-between gap-2">
                          <div className="min-w-0">
                            <p className="text-body-lg font-semibold">{d.matchTitle || "Pelada"}</p>
                            <p className="text-label-md text-on-surface-variant">
                              {new Date(d.startsAt).toLocaleString("pt-BR", {
                                weekday: "short",
                                day: "2-digit",
                                month: "short",
                                hour: "2-digit",
                                minute: "2-digit",
                                timeZone: group?.timeZoneId,
                              })}{" "}
                              · {d.location}
                            </p>
                            {notLaunched ? null : <p className="text-headline-md">{formatMoney(d.amount)}</p>}
                            {d.status === "Paid" ? <p className="text-label-md text-on-surface-variant">{sourceText(d.paymentSource, d.paidAt)}</p> : null}
                            {note ? <p className={`text-body-md ${d.status === "Overdue" ? "text-danger-strong" : "text-fg-secondary"}`}>{note}</p> : null}
                          </div>
                          <StatusBadge tone={ob.tone}>{d.status === "Paid" ? "Diária paga" : ob.label}</StatusBadge>
                        </div>
                        {payable && !pixOff && open?.key !== key ? (
                          <button
                            type="button"
                            disabled={busyKey === key}
                            className={`${primaryButton} mt-md w-full sm:w-auto`}
                            data-pix-trigger={key}
                            onClick={() => start(key, () => api.createMatchDayPix(d.matchId), d.activePix)}
                          >
                            {busyKey === key
                              ? "Gerando Pix…"
                              : hasActive
                                ? "Continuar pagamento Pix"
                                : `Pagar ${formatMoney(d.amount)} com Pix`}
                          </button>
                        ) : null}
                      </li>
                    );
                  })}
                </ul>
              </section>
            ) : null}
          </>
        )}

      </div>
      {open ? (
        <Sheet title="Pagar com Pix" onClose={closeCheckout}>
          <PixCheckout key={open.charge.id} initialCharge={open.charge} onRegenerate={open.regenerate} onSettled={() => load().catch(() => undefined)} />
        </Sheet>
      ) : null}
    </AppShell>
  );
}
