"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { AdminCard, AdminEmpty, AdminError, AdminKpiStrip, AdminLoading, AdminNotice, StatusBadge, type BadgeTone } from "@/components/admin/AdminCard";
import { AdminFilterChips } from "@/components/admin/AdminSearch";
import { inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import { PixCheckout } from "@/components/pix/PixCheckout";
import { formatMoney, PAYMENT_STATUS_LABEL } from "@/lib/adminFilters";
import { api, AdminMatchFee, AdminPixOverview, GatewayStatus, MatchSummary, PixCharge } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { usePixFocusReturn } from "@/components/pix/usePixFocusReturn";
import { GATEWAY_MODE_LABEL, isActivePix, obligationActions, PAYMENT_SOURCE_LABEL, PIX_STATUS_LABEL, pixSummaryText } from "@/lib/pix";

const OBLIGATION_TONE: Record<string, BadgeTone> = { Paid: "success", Pending: "warning", Overdue: "danger", Exempt: "info", Cancelled: "neutral" };
const PIX_TONE: Record<string, BadgeTone> = { Approved: "success", Pending: "warning", Expired: "neutral", Cancelled: "neutral" };

export function GatewayBadge({ gateway }: { gateway: GatewayStatus | null }) {
  if (!gateway) return <StatusBadge>Gateway: verificando…</StatusBadge>;
  const tone: BadgeTone = gateway.mode === "Live" ? "success" : gateway.mode === "Simulator" ? "warning" : "danger";
  return <StatusBadge tone={tone}>{GATEWAY_MODE_LABEL[gateway.mode] ?? gateway.mode}</StatusBadge>;
}

/** Abre o checkout Pix para o admin mostrar/enviar ao jogador (com simulação em modo de teste). */
export function AdminPixPanel({
  title,
  charge,
  onClose,
  onSettled,
  onRegenerate,
}: {
  title: string;
  charge: PixCharge;
  onClose: () => void;
  onSettled: () => void;
  onRegenerate?: () => Promise<void>;
}) {
  return (
    <div className="flex flex-col gap-sm rounded-xl border border-outline-variant bg-surface-container-low p-md">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-body-lg font-semibold">{title}</p>
        <button type="button" className={secondaryButton} onClick={onClose}>
          Fechar Pix
        </button>
      </div>
      <PixCheckout key={charge.id} initialCharge={charge} allowSimulate onSettled={onSettled} onRegenerate={onRegenerate} />
    </div>
  );
}

// ---------- Configuração e status do gateway ----------

export function GatewaySettingsCard({ groupId, gateway, onSaved }: { groupId: string; gateway: GatewayStatus | null; onSaved: (g: GatewayStatus) => void }) {
  const [monthly, setMonthly] = useState("");
  const [daily, setDaily] = useState("");
  const [minutes, setMinutes] = useState("");
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<{ error: boolean; text: string } | null>(null);

  useEffect(() => {
    if (!gateway) return;
    setMonthly(String(gateway.defaultMonthlyFee));
    setDaily(String(gateway.defaultMatchDayFee));
    setMinutes(String(gateway.pixExpirationMinutes));
  }, [gateway]);

  async function save() {
    const m = Number(monthly.replace(",", "."));
    const d = Number(daily.replace(",", "."));
    const min = Number(minutes);
    if (!(m >= 0) || !(d >= 0)) return setStatus({ error: true, text: "Valores devem ser números positivos." });
    if (!Number.isInteger(min) || min < 30 || min > 1440) return setStatus({ error: true, text: "Validade do Pix entre 30 e 1440 minutos." });
    setBusy(true);
    setStatus(null);
    try {
      onSaved(await api.updatePaymentSettings(groupId, { defaultMonthlyFee: m, defaultMatchDayFee: d, pixExpirationMinutes: min }));
      setStatus({ error: false, text: "Configuração salva. Vale para as próximas cobranças." });
    } catch (e) {
      setStatus({ error: true, text: e instanceof Error ? e.message : "Erro ao salvar." });
    } finally {
      setBusy(false);
    }
  }

  return (
    <AdminCard title="Gateway Mercado Pago e valores" titleId="gateway-titulo" actions={<GatewayBadge gateway={gateway} />}>
      {!gateway ? (
        <AdminLoading label="Carregando configuração…" />
      ) : (
        <div className="flex flex-col gap-md">
          <ul className="text-body-md text-on-surface-variant">
            <li>Webhook: {gateway.webhookSecretConfigured ? "assinatura configurada" : "segredo do webhook não configurado"}</li>
            <li>
              Endereço do webhook: <code className="break-all">{gateway.webhookPath}</code>
            </li>
            {gateway.mode === "Unconfigured" ? <li className="text-on-error-container">Sem chave do Mercado Pago: jogadores não conseguem pagar por Pix.</li> : null}
          </ul>
          <form
            className="grid gap-md sm:grid-cols-3"
            onSubmit={(e) => {
              e.preventDefault();
              save();
            }}
          >
            <label className="flex flex-col gap-1 text-label-md">
              Mensalidade (R$)
              <input inputMode="decimal" className={inputClass} value={monthly} onChange={(e) => setMonthly(e.target.value)} />
            </label>
            <label className="flex flex-col gap-1 text-label-md">
              Diária avulsa (R$)
              <input inputMode="decimal" className={inputClass} value={daily} onChange={(e) => setDaily(e.target.value)} />
            </label>
            <label className="flex flex-col gap-1 text-label-md">
              Validade do Pix (minutos)
              <input inputMode="numeric" className={inputClass} value={minutes} onChange={(e) => setMinutes(e.target.value)} />
            </label>
            <button type="submit" disabled={busy} className={`${primaryButton} sm:col-span-3 sm:justify-self-start`}>
              Salvar configuração
            </button>
          </form>
          {status ? (
            <p role={status.error ? "alert" : "status"} className={status.error ? "text-on-error-container" : "text-green-900"}>
              {status.text}
            </p>
          ) : null}
        </div>
      )}
    </AdminCard>
  );
}

// ---------- Diárias da rodada ----------

export function MatchFeesPanel({ groupId, timeZone }: { groupId: string; timeZone?: string }) {
  const [matches, setMatches] = useState<MatchSummary[] | null>(null);
  const [matchId, setMatchId] = useState("");
  const [fees, setFees] = useState<AdminMatchFee[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [pix, setPix] = useState<{ playerProfileId: string; name: string; charge: PixCharge } | null>(null);
  const { remember, restore } = usePixFocusReturn();

  useEffect(() => {
    api
      .listMatches(groupId)
      .then((list) => {
        const relevant = list.filter((m) => m.status !== "Draft" && m.status !== "Cancelled").sort((a, b) => b.startsAt.localeCompare(a.startsAt));
        setMatches(relevant);
        setMatchId((cur) => cur || relevant[0]?.id || "");
      })
      .catch((e) => setError(e instanceof Error ? e.message : "Erro ao carregar partidas."));
  }, [groupId]);

  const load = useCallback(async () => {
    if (!matchId) return;
    setFees(await api.matchFees(groupId, matchId));
  }, [groupId, matchId]);

  useEffect(() => {
    setFees(null);
    setPix(null);
    load().catch((e) => setError(e instanceof Error ? e.message : "Erro ao carregar diárias."));
  }, [load]);

  async function act(id: string, action: () => Promise<unknown>, success: string) {
    setBusyId(id);
    setError(null);
    setNotice(null);
    try {
      await action();
      await load();
      setNotice(success);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro.");
      await load().catch(() => undefined);
    } finally {
      setBusyId(null);
    }
  }

  async function openPix(f: AdminMatchFee) {
    const create = async () => {
      const charge = await api.createMatchDayPix(f.matchId, { playerProfileId: f.playerProfileId });
      setPix({ playerProfileId: f.playerProfileId, name: playerDisplayName(f.playerName), charge });
    };
    setBusyId(f.playerProfileId);
    setError(null);
    try {
      await create();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível gerar o Pix.");
    } finally {
      setBusyId(null);
    }
  }

  const summary = useMemo(() => {
    const list = fees ?? [];
    const paid = list.filter((f) => f.status === "Paid");
    return {
      total: list.length,
      paidPix: paid.filter((f) => f.paymentSource === "Pix").length,
      paidManual: paid.filter((f) => f.paymentSource !== "Pix").length,
      open: list.filter((f) => f.status === "Pending" || f.status === "Overdue").length,
      received: paid.reduce((s, f) => s + f.amount, 0),
    };
  }, [fees]);

  return (
    <div className="flex flex-col gap-md">
      <label className="flex flex-col gap-1 text-label-md sm:max-w-md">
        Partida
        <select className={inputClass} value={matchId} onChange={(e) => setMatchId(e.target.value)}>
          {matches?.length === 0 ? <option value="">Nenhuma partida</option> : null}
          {(matches ?? []).map((m) => (
            <option key={m.id} value={m.id}>
              {new Date(m.startsAt).toLocaleDateString("pt-BR", { timeZone })} · {m.title || "Partida"}
            </option>
          ))}
        </select>
      </label>
      {error ? <AdminError>{error}</AdminError> : null}
      {notice ? <AdminNotice>{notice}</AdminNotice> : null}
      {matches === null || (matchId && fees === null) ? (
        <AdminLoading label="Carregando diárias…" />
      ) : !matchId ? (
        <AdminEmpty>Nenhuma partida com lista aberta ou encerrada.</AdminEmpty>
      ) : fees && fees.length === 0 ? (
        <AdminEmpty>Nenhum avulso nesta partida. Mensalistas ativos não pagam diária.</AdminEmpty>
      ) : (
        <>
          <AdminKpiStrip
            label="Resumo das diárias"
            items={[
              { label: "Pagas via Pix", value: summary.paidPix },
              { label: "Baixa manual", value: summary.paidManual },
              { label: "Pendentes", value: summary.open, tone: summary.open > 0 ? "warning" : undefined },
              { label: "Recebido", value: formatMoney(summary.received), hint: `${summary.total} avulsos` },
            ]}
          />
          {pix ? (
            <AdminPixPanel
              title={`Pix da diária · ${pix.name}`}
              charge={pix.charge}
              onClose={() => {
                const key = `d:${pix.playerProfileId}`;
                setPix(null);
                restore(key);
                load().catch(() => undefined); // botão passa a "Ver QR Code Pix"
              }}
              onSettled={() => load().catch(() => undefined)}
              onRegenerate={async () => {
                const f = fees?.find((x) => x.playerProfileId === pix.playerProfileId);
                if (f) await openPix(f);
              }}
            />
          ) : null}
          <ul className="overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
            {(fees ?? []).map((f) => {
              const name = playerDisplayName(f.playerName);
              const actions = obligationActions(f.status, f.paymentSource, f.latestPix, Date.now());
              const busy = busyId === f.playerProfileId;
              return (
                <li
                  key={f.playerProfileId}
                  data-pix-item={`d:${f.playerProfileId}`}
                  tabIndex={-1}
                  className="flex flex-col gap-2 border-b border-outline-variant px-md py-sm last:border-0"
                >
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="truncate font-semibold">{name}</p>
                      <p className="text-label-md text-on-surface-variant">
                        {formatMoney(f.amount)} · {pixSummaryText(f.latestPix, f.paymentSource, Date.now(), timeZone)}
                      </p>
                    </div>
                    <StatusBadge tone={OBLIGATION_TONE[f.status] ?? "neutral"}>{PAYMENT_STATUS_LABEL[f.status] ?? f.status}</StatusBadge>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {actions.pix ? (
                      <button
                        type="button"
                        data-pix-trigger={`d:${f.playerProfileId}`}
                        disabled={busy}
                        className={secondaryButton}
                        onClick={() => {
                          remember();
                          openPix(f);
                        }}
                      >
                        {actions.sync ? "Ver QR Code Pix" : "Gerar Pix"}
                      </button>
                    ) : null}
                    {actions.sync && f.latestPix ? (
                      <button
                        type="button"
                        disabled={busy}
                        className={secondaryButton}
                        onClick={() => act(f.playerProfileId, () => api.verifyPix(f.latestPix!.id), `Pix de ${name} sincronizado com o Mercado Pago.`)}
                      >
                        Sincronizar com MP
                      </button>
                    ) : null}
                    {actions.markPaid ? (
                      <button
                        type="button"
                        disabled={busy}
                        className={primaryButton}
                        onClick={() => act(f.playerProfileId, () => api.updateMatchFee(groupId, f.matchId, f.playerProfileId, { amount: f.amount, status: "Paid" }), `Baixa manual da diária de ${name}.`)}
                      >
                        Baixa manual
                      </button>
                    ) : null}
                    {actions.exempt ? (
                      <button
                        type="button"
                        disabled={busy}
                        className={secondaryButton}
                        onClick={() => act(f.playerProfileId, () => api.updateMatchFee(groupId, f.matchId, f.playerProfileId, { amount: f.amount, status: "Exempt" }), `${name} isento da diária.`)}
                      >
                        Isentar
                      </button>
                    ) : null}
                    {actions.revertPending ? (
                      <button
                        type="button"
                        disabled={busy}
                        className={secondaryButton}
                        onClick={() => act(f.playerProfileId, () => api.updateMatchFee(groupId, f.matchId, f.playerProfileId, { amount: f.amount, status: "Pending" }), `Diária de ${name} voltou a pendente.`)}
                      >
                        {f.status === "Exempt" ? "Desfazer isenção" : "Desfazer baixa"}
                      </button>
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ul>
        </>
      )}
    </div>
  );
}

// ---------- Transações Pix ----------

type ChargeFilter = "all" | "Pending" | "Approved" | "Expired";
const CHARGE_FILTERS: { id: ChargeFilter; label: string }[] = [
  { id: "all", label: "Todas" },
  { id: "Pending", label: "Ativas" },
  { id: "Approved", label: "Aprovadas" },
  { id: "Expired", label: "Expiradas" },
];

/** `onGateway` precisa ser estável (ex.: o setter de um useState), pois entra nas dependências do carregamento. */
export function PixTransactionsPanel({ groupId, onGateway }: { groupId: string; onGateway?: (g: GatewayStatus) => void }) {
  const [filter, setFilter] = useState<ChargeFilter>("all");
  const [data, setData] = useState<AdminPixOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    const d = await api.pixOverview(groupId, filter === "all" ? {} : { status: filter });
    setData(d);
    onGateway?.(d.gateway);
  }, [groupId, filter, onGateway]);

  useEffect(() => {
    setData(null);
    setError(null);
    load().catch((e) => setError(e instanceof Error ? e.message : "Erro ao carregar transações."));
  }, [load]);

  async function act(id: string, action: () => Promise<string>) {
    setBusy(id);
    setError(null);
    setNotice(null);
    try {
      const msg = await action();
      await load();
      setNotice(msg);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro.");
    } finally {
      setBusy(null);
    }
  }

  const k = data?.kpis;
  return (
    <div className="flex flex-col gap-md">
      {error ? <AdminError onRetry={() => load().catch(() => undefined)}>{error}</AdminError> : null}
      {notice ? <AdminNotice>{notice}</AdminNotice> : null}
      <div className="flex flex-wrap items-center justify-between gap-2">
        <GatewayBadge gateway={data?.gateway ?? null} />
        <button
          type="button"
          disabled={busy === "reconcile"}
          className={secondaryButton}
          onClick={() =>
            act("reconcile", async () => {
              const r = await api.reconcilePix(groupId);
              return `Conferidas ${r.checked}: ${r.approved} aprovadas, ${r.expired} expiradas, ${r.cancelled} canceladas, ${r.stillPending} pendentes${r.errors ? `, ${r.errors} com erro` : ""}.`;
            })
          }
        >
          {busy === "reconcile" ? "Sincronizando…" : "Sincronizar pendentes com o Mercado Pago"}
        </button>
      </div>
      {k ? (
        <AdminKpiStrip
          label="Indicadores Pix"
          items={[
            { label: "Recebido via Pix", value: formatMoney(k.receivedPix), hint: `${k.paidByPixCount} pagamentos automáticos` },
            { label: "Baixa manual", value: formatMoney(k.receivedManual), hint: `${k.paidManualCount} lançamentos · ${k.exemptCount} isentos` },
            { label: "Pendente", value: formatMoney(k.pendingAmount), tone: k.pendingAmount > 0 ? "warning" : undefined },
            { label: "Cobranças Pix", value: `${k.activeCount} ativas`, hint: `${k.expiredCount} expiradas · ${k.approvedCount} aprovadas` },
          ]}
        />
      ) : null}
      <AdminFilterChips label="Filtrar transações" options={CHARGE_FILTERS} value={filter} onChange={setFilter} />
      {!data ? (
        error ? null : <AdminLoading label="Carregando transações…" />
      ) : data.charges.length === 0 ? (
        <AdminEmpty>Nenhuma cobrança Pix com esse filtro.</AdminEmpty>
      ) : (
        <ul className="overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
          {data.charges.map((c) => {
            const active = isActivePix(c, Date.now());
            return (
              <li key={c.id} className="flex flex-col gap-2 border-b border-outline-variant px-md py-sm last:border-0 sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0">
                  <p className="truncate font-semibold">{playerDisplayName(c.playerName)}</p>
                  <p className="text-label-md text-on-surface-variant">
                    {c.kind === "MatchDay" ? "Diária" : "Mensalidade"} · {formatMoney(c.amount)} · {new Date(c.createdAt).toLocaleString("pt-BR")}
                    {c.mpPaymentId ? ` · MP #${c.mpPaymentId}` : ""}
                    {c.isSimulated ? " · simulado" : ""}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <StatusBadge tone={PIX_TONE[c.status] ?? "neutral"}>{PIX_STATUS_LABEL[c.status] ?? c.status}</StatusBadge>
                  {active ? (
                    <button
                      type="button"
                      disabled={busy === c.id}
                      className={secondaryButton}
                      onClick={() => act(c.id, async () => (await api.verifyPix(c.id)).message)}
                    >
                      Sincronizar com MP
                    </button>
                  ) : null}
                  {active && c.isSimulated && data.gateway.mode === "Simulator" ? (
                    <button
                      type="button"
                      disabled={busy === c.id}
                      className={secondaryButton}
                      onClick={() => act(c.id, async () => (await api.simulateApprovePix(c.id)).message)}
                    >
                      Simular aprovação
                    </button>
                  ) : null}
                </div>
              </li>
            );
          })}
        </ul>
      )}
      <p className="text-label-md text-on-surface-variant">
        Fontes de baixa: {PAYMENT_SOURCE_LABEL.Pix} (webhook ou verificação) e {PAYMENT_SOURCE_LABEL.Manual}. “Sincronizar” consulta o Mercado Pago e dá baixa se o
        pagamento estiver aprovado.
      </p>
    </div>
  );
}
