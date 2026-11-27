"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { AdminCard, AdminError, AdminKpiStrip, AdminLoading, AdminNotice, type Kpi } from "@/components/admin/AdminCard";
import { AdminIcon } from "@/components/admin/AdminIcon";
import { AdminShell } from "@/components/admin/AdminShell";
import { MatchControls } from "@/components/admin/MatchControls";
import { RoundStepper } from "@/components/admin/RoundStepper";
import { WhatsAppSummary } from "@/components/WhatsAppSummary";
import { focusRing } from "@/components/admin/ui";
import { formatMoney, summarizePayments } from "@/lib/adminFilters";
import { ADMIN_MODULES, groupAdminModules } from "@/lib/adminModules";
import { AdminPixOverview, api, Draw, GroupPlayer, MatchDetail, Payment } from "@/lib/api";
import { GATEWAY_MODE_LABEL } from "@/lib/pix";
import { deriveRoundStep } from "@/lib/roundStep";
import { groupPeriod, monthName } from "@/lib/season";
import { buildTeamsSummary } from "@/lib/teams";
import { matchStatusLabel, useSession } from "@/lib/useSession";

type HubData = {
  match: MatchDetail | null;
  draw: Draw | null;
  players: GroupPlayer[] | null;
  payments: Payment[] | null;
  pix: AdminPixOverview | null;
  period: { year: number; month: number };
};

const TRANSITION_NOTICE: Record<string, string> = {
  RegistrationOpen: "Inscrições abertas.",
  InProgress: "Partida iniciada. O elenco está congelado.",
  AwaitingClose: "Partida aguardando a súmula.",
};

/** Central de comando do admin: etapa da rodada com o próximo passo, indicadores e módulos. */
export default function AdminHubPage() {
  const { group, isAdmin } = useSession();
  const [data, setData] = useState<HubData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!group) return;
    setError(null);
    const period = groupPeriod(new Date(), group.timeZoneId);
    const match = await api.upcomingMatch(group.id);
    // Indicadores secundários não derrubam o hub: falha vira "indisponível".
    const [draw, players, payments, pix] = await Promise.all([
      match ? api.getDraw(match.match.id).catch(() => null) : Promise.resolve(null),
      api.listPlayers(group.id).catch(() => null),
      // Só leitura: não abre a competência (abrir gera cobranças; isso fica na tela de Mensalidades).
      api.listPayments(group.id, period.year, period.month).catch(() => null),
      api.pixOverview(group.id).catch(() => null),
    ]);
    setData({ match, draw, players, payments, pix, period });
  }, [group]);

  useEffect(() => {
    if (!group || !isAdmin) return;
    load().catch((e) => setError(e instanceof Error ? e.message : "Não foi possível carregar a central."));
  }, [group, isAdmin, load]);

  async function transition(to: string, success?: string) {
    if (!data?.match) return;
    const id = data.match.match.id;
    await runMatch(() => api.transitionMatch(id, to), success ?? TRANSITION_NOTICE[to] ?? "Partida atualizada.");
  }

  async function runMatch(action: () => Promise<unknown>, success: string) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
      await load();
      setNotice(success);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao atualizar a partida.");
      await load().catch(() => undefined);
    } finally {
      setBusy(false);
    }
  }

  const step = data
    ? deriveRoundStep({ status: data.match?.match.status ?? null, confirmedCount: data.match?.match.confirmedCount ?? 0, draw: data.draw })
    : null;

  return (
    <AdminShell title="Central do admin" description="O próximo passo da rodada, os números da semana e todos os módulos do grupo.">
      {error ? <AdminError onRetry={() => load().catch(() => undefined)}>{error}</AdminError> : null}
      {notice ? <AdminNotice>{notice}</AdminNotice> : null}

      {!data ? (
        error ? null : <AdminLoading label="Carregando a rodada…" />
      ) : (
        <>
          <section aria-labelledby="rodada-titulo" className="flex flex-col gap-sm">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 id="rodada-titulo" className="text-headline-md">
                Rodada da semana
              </h2>
              {data.match ? (
                <p className="text-label-md text-on-surface-variant">
                  {data.match.match.title || "Partida"} ·{" "}
                  {new Date(data.match.match.startsAt).toLocaleString("pt-BR", {
                    weekday: "short",
                    day: "2-digit",
                    month: "short",
                    hour: "2-digit",
                    minute: "2-digit",
                    timeZone: group?.timeZoneId,
                  })}{" "}
                  · {matchStatusLabel(data.match.match.status)}
                </p>
              ) : null}
            </div>
            {step ? <RoundStepper state={step} onTransition={(to) => transition(to)} busy={busy} /> : null}
            {data.match ? (
              <MatchControls
                match={data.match.match}
                busy={busy}
                onTransition={(to, success) => transition(to, success)}
                onUpdate={(body) => {
                  const id = data.match!.match.id;
                  return runMatch(() => api.updateMatch(id, body), "Dados da partida salvos.");
                }}
              />
            ) : null}
            {data.match ? (
              <WhatsAppSummary
                detail={data.match}
                teams={data.draw && data.draw.status === "Confirmed" && !data.draw.rosterChanged ? buildTeamsSummary(data.draw) : null}
                timeZone={group?.timeZoneId ?? "America/Sao_Paulo"}
              />
            ) : null}
          </section>

          <section aria-labelledby="kpis-titulo" className="flex flex-col gap-sm">
            <h2 id="kpis-titulo" className="text-headline-md">
              Resumo
            </h2>
            <AdminKpiStrip label="Indicadores da semana e do mês" items={kpis(data)} />
          </section>

          <ModuleGrid />
        </>
      )}
    </AdminShell>
  );
}

function kpis(data: HubData): Kpi[] {
  const m = data.match?.match;
  const drawLabel = !data.draw
    ? "Não sorteado"
    : data.draw.rosterChanged
      ? "Refazer"
      : data.draw.status === "Confirmed"
        ? "Confirmado"
        : "Prévia";
  const pay = data.payments ? summarizePayments(data.payments) : null;
  const players = data.players;
  const competence = `${monthName(data.period.month)}/${data.period.year}`;
  return [
    {
      label: "Vagas preenchidas",
      value: m ? `${m.confirmedCount}/${m.playerLimit}` : "—",
      hint: m ? `${m.waitlistCount} na fila` : "Sem partida aberta",
    },
    {
      label: "Sorteio",
      value: m ? drawLabel : "—",
      tone: data.draw?.rosterChanged ? "warning" : undefined,
      hint: data.draw && !data.draw.isLegacy ? "Vermelho, Preto e Verde" : undefined,
    },
    {
      label: `Mensalidades (${competence})`,
      value: pay === null ? "Indisponível" : pay.total === 0 ? "Não aberta" : `${pay.paid} pagos`,
      tone: pay && pay.open > 0 ? "warning" : undefined,
      hint: pay && pay.total > 0 ? `${pay.open} pendentes · ${formatMoney(pay.received)} de ${formatMoney(pay.expected)}` : undefined,
    },
    {
      label: "Elenco",
      value: players === null ? "Indisponível" : `${players.length} jogadores`,
      hint: players
        ? `${players.filter((p) => p.canPlayGoalkeeper).length} aptos ao gol · ${players.filter((p) => p.isMonthlyMember).length} mensalistas`
        : undefined,
    },
    {
      label: "Pix Mercado Pago",
      value: !data.pix ? "Indisponível" : formatMoney(data.pix.kpis.receivedPix),
      tone: data.pix?.gateway.mode === "Unconfigured" ? "danger" : undefined,
      hint: data.pix
        ? `${GATEWAY_MODE_LABEL[data.pix.gateway.mode] ?? data.pix.gateway.mode} · ${data.pix.kpis.activeCount} ativas · ${formatMoney(data.pix.kpis.pendingAmount)} pendente`
        : undefined,
    },
  ];
}

function ModuleGrid() {
  const sections = groupAdminModules(ADMIN_MODULES);
  return (
    <section aria-labelledby="modulos-titulo" className="flex flex-col gap-md">
      <h2 id="modulos-titulo" className="text-headline-md">
        Módulos
      </h2>
      {sections.map((s) => (
        <div key={s.group} className="flex flex-col gap-sm">
          <h3 className="text-label-md uppercase text-on-surface-variant">{s.title}</h3>
          <ul className="grid gap-sm sm:grid-cols-2 lg:grid-cols-3">
            {s.modules.map((m) => (
              <li key={m.id}>
                <Link
                  href={m.href}
                  className={`flex h-full min-h-11 items-start gap-3 rounded-xl border border-outline-variant bg-surface-container-lowest p-md hover:border-pitch-green-container ${focusRing}`}
                >
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-surface-container text-pitch-green-container">
                    <AdminIcon name={m.icon} />
                  </span>
                  <span className="min-w-0">
                    <span className="block text-body-lg font-semibold">
                      {m.roundStep ? <span className="text-on-surface-variant">{m.roundStep}. </span> : null}
                      {m.title}
                    </span>
                    <span className="block text-body-md text-on-surface-variant">{m.description}</span>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ))}
      <AdminCard tone="muted">
        <p className="text-body-md text-on-surface-variant">
          Novas funções do grupo entram como módulos no registro do painel e aparecem aqui e no menu automaticamente.
        </p>
      </AdminCard>
    </section>
  );
}
