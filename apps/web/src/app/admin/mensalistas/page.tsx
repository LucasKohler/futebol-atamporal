"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { AdminEmpty, AdminError, AdminKpiStrip, AdminLoading, AdminNotice, StatusBadge, type BadgeTone } from "@/components/admin/AdminCard";
import { AdminConfirmButton } from "@/components/admin/AdminDrawer";
import { AdminFilterChips, AdminSearch } from "@/components/admin/AdminSearch";
import { AdminShell } from "@/components/admin/AdminShell";
import { AdminTabs } from "@/components/admin/AdminTabs";
import { inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import {
  filterMembers,
  filterPayments,
  formatMoney,
  MEMBER_FILTERS,
  MEMBER_STATUS_LABEL,
  PAYMENT_FILTERS,
  PAYMENT_STATUS_LABEL,
  recentPeriods,
  summarizePayments,
  type MemberFilter,
  type PaymentFilter,
} from "@/lib/adminFilters";
import { AdminPixPanel, GatewayBadge, GatewaySettingsCard, MatchFeesPanel, PixTransactionsPanel } from "@/components/admin/pix/PixAdmin";
import { api, GatewayStatus, MonthlyMember, Payment, PixCharge } from "@/lib/api";
import { usePixFocusReturn } from "@/components/pix/usePixFocusReturn";
import { obligationActions, paidBySource, PAYMENT_SOURCE_LABEL, pixSummaryText } from "@/lib/pix";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { groupPeriod, monthName, type Period } from "@/lib/season";
import { useSession } from "@/lib/useSession";

type Tab = "cobrancas" | "diarias" | "pix" | "vinculos";

const PAYMENT_TONE: Record<string, BadgeTone> = { Paid: "success", Pending: "warning", Overdue: "danger", Exempt: "info", Cancelled: "neutral" };

const samePeriod = (a: Period, b: Period) => a.year === b.year && a.month === b.month;
const periodKey = (p: Period) => `${p.year}-${p.month}`;

/**
 * Mensalidades por competência (mês/ano no fuso do grupo) e vínculos de mensalistas.
 * Rota mantida em /admin/mensalistas por compatibilidade; notas e apelidos foram para Elenco & Notas.
 */
export default function MensalidadesPage() {
  const { group, isAdmin } = useSession();
  const current = useMemo(() => (group ? groupPeriod(new Date(), group.timeZoneId) : null), [group]);
  const [period, setPeriod] = useState<Period | null>(null);
  const [tab, setTab] = useState<Tab>("cobrancas");
  const [payments, setPayments] = useState<Payment[] | null>(null);
  const [members, setMembers] = useState<MonthlyMember[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [payQuery, setPayQuery] = useState("");
  const [payFilter, setPayFilter] = useState<PaymentFilter>("all");
  const [memberQuery, setMemberQuery] = useState("");
  const [memberFilter, setMemberFilter] = useState<MemberFilter>("active");
  const [gateway, setGateway] = useState<GatewayStatus | null>(null);
  const [pixOpen, setPixOpen] = useState<{ paymentId: string; name: string; charge: PixCharge } | null>(null);
  const { remember, restore } = usePixFocusReturn();

  useEffect(() => {
    if (current && !period) setPeriod(current);
  }, [current, period]);

  useEffect(() => {
    if (!group || !isAdmin) return;
    // Falha aqui não bloqueia a baixa manual: o selo fica em "verificando".
    api.paymentSettings(group.id).then(setGateway).catch(() => undefined);
  }, [group, isAdmin]);

  const loadPayments = useCallback(
    async (p: Period) => {
      if (!group || !current) return;
      // Mês corrente: abre a competência como antes (gera as cobranças pendentes dos ativos, idempotente).
      if (samePeriod(p, current)) await api.ensurePeriod(group.id, p.year, p.month);
      setPayments(await api.listPayments(group.id, p.year, p.month));
    },
    [group, current],
  );

  const loadMembers = useCallback(async () => {
    if (!group) return;
    setMembers(await api.listMembers(group.id));
  }, [group]);

  useEffect(() => {
    if (!group || !isAdmin || !period) return;
    setPayments(null);
    setError(null);
    loadPayments(period).catch((e) => setError(e instanceof Error ? e.message : "Não foi possível carregar as mensalidades."));
  }, [group, isAdmin, period, loadPayments]);

  useEffect(() => {
    if (!group || !isAdmin) return;
    loadMembers().catch((e) => setError(e instanceof Error ? e.message : "Não foi possível carregar os vínculos."));
  }, [group, isAdmin, loadMembers]);

  async function act(id: string, action: () => Promise<unknown>, success: string, reload: () => Promise<void>) {
    setBusyId(id);
    setError(null);
    setNotice(null);
    try {
      await action();
      await reload();
      setNotice(success);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao salvar.");
    } finally {
      setBusyId(null);
    }
  }

  const options = useMemo(() => (current ? recentPeriods(current, 12) : []), [current]);
  const summary = useMemo(() => (payments ? summarizePayments(payments) : null), [payments]);
  const bySource = useMemo(() => paidBySource(payments ?? []), [payments]);

  /** Admin gera/vê o Pix da mensalidade para enviar ao jogador (mesma rota do jogador; a API autoriza admin). */
  async function openPix(paymentId: string, name: string) {
    setBusyId(paymentId);
    setError(null);
    try {
      setPixOpen({ paymentId, name, charge: await api.createMembershipPix(paymentId) });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível gerar o Pix.");
      if (period) await loadPayments(period).catch(() => undefined);
    } finally {
      setBusyId(null);
    }
  }
  const visiblePayments = useMemo(() => (payments ? filterPayments(payments, payQuery, payFilter) : []), [payments, payQuery, payFilter]);
  const visibleMembers = useMemo(() => (members ? filterMembers(members, memberQuery, memberFilter) : []), [members, memberQuery, memberFilter]);
  const label = period ? `${monthName(period.month)} de ${period.year}` : "";

  return (
    <AdminShell
      moduleId="mensalidades"
      title="Mensalidades & Pix"
      description="Mensalidades por competência, diárias dos avulsos e cobranças Pix do Mercado Pago. Apelidos e notas ficam em Elenco & Notas."
      actions={<GatewayBadge gateway={gateway} />}
    >
      {error ? <AdminError>{error}</AdminError> : null}
      {notice ? <AdminNotice>{notice}</AdminNotice> : null}

      <AdminTabs
        label="Mensalidades e Pix"
        value={tab}
        onChange={setTab}
        tabs={[
          { id: "cobrancas", label: "Cobranças mensais" },
          { id: "diarias", label: "Diárias da rodada" },
          { id: "pix", label: "Transações Pix" },
          { id: "vinculos", label: "Vínculos & Config", badge: members ? `(${members.filter((m) => m.status === "Active").length})` : undefined },
        ]}
      >
        {tab === "cobrancas" ? (
          <div className="flex flex-col gap-md">
            <label className="flex flex-col gap-1 text-label-md sm:max-w-xs">
              Competência
              <select
                className={inputClass}
                value={period ? periodKey(period) : ""}
                onChange={(e) => {
                  const [y, m] = e.target.value.split("-").map(Number);
                  setPeriod({ year: y, month: m });
                }}
              >
                {options.map((o) => (
                  <option key={periodKey(o)} value={periodKey(o)}>
                    {monthName(o.month)} de {o.year}
                    {current && samePeriod(o, current) ? " (atual)" : ""}
                  </option>
                ))}
              </select>
            </label>

            {payments === null || !summary ? (
              error ? null : <AdminLoading label="Carregando cobranças…" />
            ) : summary.total === 0 ? (
              <AdminEmpty
                action={
                  period && current && !samePeriod(period, current) ? (
                    <AdminConfirmButton
                      tone="neutral"
                      label={`Abrir competência de ${label}`}
                      question="Gera uma cobrança pendente para cada mensalista ativo hoje. Abrir?"
                      confirmLabel="Abrir competência"
                      disabled={busyId === "period"}
                      className={primaryButton}
                      onConfirm={() =>
                        act(
                          "period",
                          () => api.ensurePeriod(group!.id, period.year, period.month),
                          `Competência de ${label} aberta.`,
                          () => loadPayments(period),
                        )
                      }
                    />
                  ) : null
                }
              >
                Nenhuma cobrança em {label}.{" "}
                {period && current && samePeriod(period, current)
                  ? "Não há mensalistas ativos: torne jogadores mensalistas em Elenco & Notas."
                  : "A competência ainda não foi aberta."}
              </AdminEmpty>
            ) : (
              <>
                <AdminKpiStrip
                  label={`Resumo de ${label}`}
                  items={[
                    {
                      label: "Pagos",
                      value: summary.paid,
                      hint: `${bySource.pixCount} via Pix · ${bySource.manualCount} baixa manual (de ${summary.total})`,
                    },
                    {
                      label: "Pendentes",
                      value: summary.open,
                      tone: summary.open > 0 ? "warning" : undefined,
                      hint: summary.overdue > 0 ? `${summary.overdue} atrasados` : undefined,
                    },
                    { label: "Isentos", value: summary.exempt, hint: summary.cancelled > 0 ? `${summary.cancelled} cancelados` : undefined },
                    { label: "Recebido", value: formatMoney(summary.received), hint: `de ${formatMoney(summary.expected)} previstos` },
                  ]}
                />
                <div className="flex flex-col gap-sm">
                  <AdminSearch label="Buscar mensalista" placeholder="Nome ou apelido" value={payQuery} onChange={setPayQuery} resultCount={visiblePayments.length} />
                  <AdminFilterChips
                    label="Filtrar cobranças"
                    options={PAYMENT_FILTERS}
                    value={payFilter}
                    onChange={setPayFilter}
                    counts={{ all: summary.total, open: summary.open, paid: summary.paid, exempt: summary.exempt }}
                  />
                </div>
                {pixOpen ? (
                  <AdminPixPanel
                    title={`Pix da mensalidade · ${pixOpen.name}`}
                    charge={pixOpen.charge}
                    onClose={() => {
                      const key = `m:${pixOpen.paymentId}`;
                      setPixOpen(null);
                      restore(key);
                      loadPayments(period!).catch(() => undefined); // botão passa a "Ver QR Code Pix"
                    }}
                    onSettled={() => loadPayments(period!).catch(() => undefined)}
                    onRegenerate={() => openPix(pixOpen.paymentId, pixOpen.name)}
                  />
                ) : null}
                {visiblePayments.length === 0 ? (
                  <AdminEmpty>Nenhuma cobrança com esses critérios.</AdminEmpty>
                ) : (
                  <ul className="overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
                    {visiblePayments.map((p) => (
                      <PaymentRow
                        key={p.id}
                        payment={p}
                        timeZone={group?.timeZoneId}
                        onPix={() => {
                          remember(); // gatilho "Gerar/Ver Pix"; volta a ele ao fechar
                          openPix(p.id, playerDisplayName(p.playerName));
                        }}
                        onSync={() =>
                          p.latestPix &&
                          act(p.id, () => api.verifyPix(p.latestPix!.id), `Pix de ${playerDisplayName(p.playerName)} sincronizado com o Mercado Pago.`, () =>
                            loadPayments(period!),
                          )
                        }
                        busy={busyId === p.id}
                        onSet={(status, success) =>
                          act(
                            p.id,
                            () => api.upsertPayment(group!.id, p.monthlyMembershipId, p.billingPeriodId, p.amount, status),
                            success,
                            () => loadPayments(period!),
                          )
                        }
                      />
                    ))}
                  </ul>
                )}
              </>
            )}
          </div>
        ) : tab === "diarias" ? (
          group ? <MatchFeesPanel groupId={group.id} timeZone={group.timeZoneId} /> : null
        ) : tab === "pix" ? (
          group ? <PixTransactionsPanel groupId={group.id} onGateway={setGateway} /> : null
        ) : (
          <div className="flex flex-col gap-md">
            {group ? <GatewaySettingsCard groupId={group.id} gateway={gateway} onSaved={setGateway} /> : null}
            <h2 className="text-headline-md">Vínculos de mensalistas</h2>
            <div className="flex flex-col gap-sm">
              <AdminSearch label="Buscar vínculo" placeholder="Nome ou apelido" value={memberQuery} onChange={setMemberQuery} resultCount={visibleMembers.length} />
              <AdminFilterChips
                label="Filtrar vínculos"
                options={MEMBER_FILTERS}
                value={memberFilter}
                onChange={setMemberFilter}
                counts={
                  members
                    ? { all: members.length, active: members.filter((m) => m.status === "Active").length, inactive: members.filter((m) => m.status !== "Active").length }
                    : undefined
                }
              />
            </div>
            {members === null ? (
              <AdminLoading label="Carregando vínculos…" />
            ) : visibleMembers.length === 0 ? (
              <AdminEmpty
                action={
                  <Link href="/admin/elenco" className={secondaryButton}>
                    Tornar jogadores mensalistas em Elenco & Notas
                  </Link>
                }
              >
                {members.length === 0 ? "Nenhum mensalista cadastrado." : "Nenhum vínculo com esses critérios."}
              </AdminEmpty>
            ) : (
              <ul className="overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
                {visibleMembers.map((m) => {
                  const name = playerDisplayName(m.displayName);
                  const active = m.status === "Active";
                  return (
                    <li key={m.id} className="flex flex-wrap items-center justify-between gap-2 border-b border-outline-variant px-md py-sm last:border-0">
                      <div className="min-w-0">
                        <p className="truncate font-semibold">{name}</p>
                        <p className="text-label-md text-on-surface-variant">Desde {new Date(m.startedAt).toLocaleDateString("pt-BR")}</p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <StatusBadge tone={active ? "success" : "neutral"}>{MEMBER_STATUS_LABEL[m.status] ?? m.status}</StatusBadge>
                        {active ? (
                          <AdminConfirmButton
                            label="Desativar"
                            question={`Desativar ${name}?`}
                            confirmLabel="Desativar"
                            disabled={busyId === m.id}
                            onConfirm={() =>
                              act(m.id, () => api.setMemberStatus(group!.id, m.id, "Inactive"), `${name} desativado.`, async () => {
                                await loadMembers();
                              })
                            }
                          />
                        ) : (
                          <button
                            type="button"
                            disabled={busyId === m.id}
                            className={secondaryButton}
                            onClick={() =>
                              act(m.id, () => api.setMemberStatus(group!.id, m.id, "Active"), `${name} reativado.`, async () => {
                                await loadMembers();
                                if (period) await loadPayments(period);
                              })
                            }
                          >
                            Reativar
                          </button>
                        )}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        )}
      </AdminTabs>
    </AdminShell>
  );
}

function PaymentRow({
  payment: p,
  busy,
  onSet,
  onPix,
  onSync,
  timeZone,
}: {
  payment: Payment;
  busy: boolean;
  onSet: (status: string, success: string) => void;
  onPix: () => void;
  onSync: () => void;
  timeZone?: string;
}) {
  const name = playerDisplayName(p.playerName);
  const actions = obligationActions(p.status, p.paymentSource, p.latestPix, Date.now());
  return (
    <li data-pix-item={`m:${p.id}`} tabIndex={-1} className="flex flex-col gap-2 border-b border-outline-variant px-md py-sm last:border-0">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="truncate font-semibold">{name}</p>
          <p className="text-label-md text-on-surface-variant">
            {formatMoney(p.amount)}
            {p.paidAt ? ` · pago em ${new Date(p.paidAt).toLocaleDateString("pt-BR")}` : ""}
            {p.status === "Paid" && p.paymentSource && p.paymentSource !== "None" ? ` · ${PAYMENT_SOURCE_LABEL[p.paymentSource]}` : ""}
          </p>
          {p.paymentSource !== undefined ? (
            <p className="text-label-md text-on-surface-variant">{pixSummaryText(p.latestPix, p.paymentSource, Date.now(), timeZone)}</p>
          ) : null}
        </div>
        <StatusBadge tone={PAYMENT_TONE[p.status] ?? "neutral"}>{PAYMENT_STATUS_LABEL[p.status] ?? p.status}</StatusBadge>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        {actions.pix ? (
          <button type="button" data-pix-trigger={`m:${p.id}`} disabled={busy} className={secondaryButton} onClick={onPix}>
            {actions.sync ? "Ver QR Code Pix" : "Gerar Pix"}
          </button>
        ) : null}
        {actions.sync ? (
          <button type="button" disabled={busy} className={secondaryButton} onClick={onSync}>
            Sincronizar com MP
          </button>
        ) : null}
        {actions.markPaid ? (
          <button type="button" disabled={busy} className={primaryButton} onClick={() => onSet("Paid", `Pagamento de ${name} registrado.`)}>
            Marcar pago
          </button>
        ) : null}
        {/* Pago via Pix é definitivo na tela (o dinheiro entrou no Mercado Pago); a API também recusa com 409. */}
        {actions.revertPending ? (
          <button type="button" disabled={busy} className={secondaryButton} onClick={() => onSet("Pending", `${name} voltou para pendente.`)}>
            Voltar a pendente
          </button>
        ) : null}
        {actions.exempt ? (
          <button type="button" disabled={busy} className={secondaryButton} onClick={() => onSet("Exempt", `${name} isento em ${p.periodLabel}.`)}>
            Isentar
          </button>
        ) : null}
      </div>
    </li>
  );
}
