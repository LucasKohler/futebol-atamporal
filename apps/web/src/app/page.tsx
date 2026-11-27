"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { AppShell } from "@/components/AppShell";
import { AdminLoading, StatusBadge } from "@/components/admin/AdminCard";
import { primaryButton, secondaryButton } from "@/components/admin/ui";
import { PaymentsLink } from "@/components/pix/PaymentsLink";
import { formatMoney } from "@/lib/adminFilters";
import { api, MatchDetail, MyMatchDayPayment } from "@/lib/api";
import { homePlan, spotsText, whenText, type MatchStage } from "@/lib/homeStage";
import { dueAtText } from "@/lib/pix";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { matchStatusLabel, registrationStatusLabel, useSession } from "@/lib/useSession";

const cardClass = "rounded-card border border-line-subtle bg-card p-md sm:p-lg";

export default function HomePage() {
  const { user, group, loading, error, isAdmin } = useSession();
  const [upcoming, setUpcoming] = useState<MatchDetail | null>(null);
  const [upcomingLoaded, setUpcomingLoaded] = useState(false);
  const [upcomingError, setUpcomingError] = useState<string | null>(null);
  const [fees, setFees] = useState<MyMatchDayPayment[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!group) return;
    api
      .upcomingMatch(group.id)
      .then((d) => {
        setUpcoming(d);
        setUpcomingError(null);
      })
      .catch((e) => {
        setUpcoming(null);
        setUpcomingError(e instanceof Error ? e.message : "Não foi possível carregar a próxima partida.");
      })
      .finally(() => setUpcomingLoaded(true));
    // A cobrança é complementar: se falhar, a Home segue só com a etapa da rodada.
    api
      .myPayments(group.id)
      .then((p) => setFees(p.matchDays))
      .catch(() => setFees(null));
  }, [group]);

  if (loading) {
    return (
      <div role="status" className="flex min-h-dvh items-center justify-center text-fg-secondary">
        Carregando…
      </div>
    );
  }
  if (error) {
    return (
      <p role="alert" className="p-lg text-danger">
        {error}
      </p>
    );
  }

  async function joinOrLeave(joined: boolean) {
    if (!upcoming) return;
    setBusy(true);
    setMsg(null);
    setNotice(null);
    try {
      setUpcoming(joined ? await api.leaveMatch(upcoming.match.id) : await api.joinMatch(upcoming.match.id));
      setNotice(joined ? "Você saiu da lista." : "Presença confirmada.");
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Não foi possível atualizar sua presença.");
    } finally {
      setBusy(false);
    }
  }

  const plan = homePlan(upcoming, fees);
  const tz = group?.timeZoneId;
  // Um botão primário por tela: diária atrasada > ação da etapa > diária pendente do dia.
  const stagePrimary = !plan.overdueFee && (plan.stage.kind === "play" || (plan.stage.kind === "confirm" && !plan.stage.joined));
  const pendingPrimary = !plan.overdueFee && !stagePrimary;

  return (
    <AppShell>
      <div className="flex flex-col gap-lg">
        <section>
          <p className="text-label-sm uppercase text-fg-secondary">Olá</p>
          <h1 className="text-headline-lg">{playerDisplayName(user?.registeredName, user?.nickname)}</h1>
          <p className="mt-1 text-body-md text-fg-secondary">{group?.name}</p>
        </section>

        <div aria-live="polite" role="status" className="sr-only">
          {notice ?? ""}
        </div>

        <h2 className="sr-only">Sua próxima ação</h2>

        {plan.overdueFee ? <FeeCard fee={plan.overdueFee} extra={plan.overdueCount - 1} primary timeZone={tz} /> : null}

        {!upcomingLoaded ? (
          <AdminLoading label="Carregando a próxima partida…" />
        ) : (
          <StageCard
            stage={plan.stage}
            primary={stagePrimary}
            busy={busy}
            msg={msg ?? upcomingError}
            timeZone={tz}
            isAdmin={isAdmin}
            onToggle={joinOrLeave}
          />
        )}

        {plan.pendingFee ? <FeeCard fee={plan.pendingFee} primary={pendingPrimary} timeZone={tz} /> : null}

        <PaymentsLink />

        {isAdmin ? (
          <section className="rounded-card border border-line-subtle bg-subtle p-md">
            <h2 className="text-label-md uppercase text-fg-secondary">Admin</h2>
            <p className="mt-1 text-body-md text-fg-secondary">Rodada da semana, sorteio, súmula, elenco e mensalidades ficam na central do admin.</p>
            <Link href="/admin" className={`${secondaryButton} mt-sm`}>
              Abrir central do admin
            </Link>
          </section>
        ) : null}
      </div>
    </AppShell>
  );
}

/** Cobrança da diária lançada no fechamento da rodada. Atrasada vem antes de tudo. */
function FeeCard({ fee, primary, extra = 0, timeZone }: { fee: MyMatchDayPayment; primary: boolean; extra?: number; timeZone?: string }) {
  const overdue = fee.status === "Overdue";
  const amount = formatMoney(fee.amount);
  const when = new Date(fee.startsAt).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit", timeZone });
  return (
    <section
      aria-labelledby={`fee-${fee.matchId}`}
      className={`rounded-card border p-md sm:p-lg ${overdue ? "border-danger bg-danger-bg" : "border-line-subtle bg-card"}`}
    >
      <div className="flex items-start justify-between gap-2">
        <p className="text-label-sm uppercase text-fg-secondary">{overdue ? "Diária em atraso" : "Pelada encerrada"}</p>
        <StatusBadge tone={overdue ? "danger" : "warning"}>{overdue ? "Atrasada" : "Pendente"}</StatusBadge>
      </div>
      <h3 id={`fee-${fee.matchId}`} className="mt-1 text-headline-md">
        Diária de {when} · {amount}
      </h3>
      <p className="mt-1 text-body-md text-fg-secondary">
        {fee.matchTitle || "Pelada"} · {fee.location}
        {!overdue && fee.dueAt ? ` · pague ${dueAtText(fee.dueAt, timeZone)}` : ""}
      </p>
      {extra > 0 ? (
        <p className="mt-1 text-body-md text-danger-strong">
          E mais {extra} {extra === 1 ? "diária atrasada" : "diárias atrasadas"} em Pagamentos.
        </p>
      ) : null}
      <Link href={`/pagamentos?diaria=${encodeURIComponent(fee.matchId)}`} className={`${primary ? primaryButton : secondaryButton} mt-md w-full sm:w-auto`}>
        Pagar {amount} com Pix
      </Link>
    </section>
  );
}

function StageCard({
  stage,
  primary,
  busy,
  msg,
  timeZone,
  isAdmin,
  onToggle,
}: {
  stage: MatchStage;
  primary: boolean;
  busy: boolean;
  msg: string | null;
  timeZone?: string;
  isAdmin: boolean;
  onToggle: (joined: boolean) => void;
}) {
  const error = msg ? (
    <p role="alert" className="mt-md rounded-lg bg-danger-bg p-3 text-body-md text-danger-strong">
      {msg}
    </p>
  ) : null;

  if (stage.kind === "none") {
    return (
      <section className={cardClass}>
        <h3 className="text-headline-md">Próxima partida</h3>
        <p className="mt-1 text-body-md text-fg-secondary">Nenhuma partida aberta. Você recebe um aviso quando a lista abrir.</p>
        {isAdmin ? (
          <Link href="/partida" className={`${secondaryButton} mt-md`}>
            Criar partida
          </Link>
        ) : null}
        {error}
      </section>
    );
  }

  const { match, myStatus, myWaitlistPosition } = stage.detail;
  const header = (
    <>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <p className="text-label-sm uppercase text-fg-secondary">{stageEyebrow(stage)}</p>
        <StatusBadge tone={match.spotsRemaining > 0 ? "success" : "neutral"}>{spotsText(match)}</StatusBadge>
      </div>
      <h3 className="mt-1 text-headline-md">{match.title || "Próxima pelada"}</h3>
      <p className="mt-1 text-body-md text-fg-secondary">
        {whenText(match.startsAt, timeZone)} · {match.location}
      </p>
    </>
  );
  const btn = primary ? primaryButton : secondaryButton;

  if (stage.kind === "confirm") {
    return (
      <section className={cardClass}>
        {header}
        <p className="mt-sm text-body-md">
          {stage.joined ? (
            <>
              Você está <strong>{registrationStatusLabel(myStatus)}</strong>
              {myWaitlistPosition ? <strong> · {myWaitlistPosition}º na fila</strong> : null}.
            </>
          ) : (
            "Vai jogar? Confirme sua presença."
          )}
        </p>
        <div className="mt-md flex flex-wrap gap-2">
          <button type="button" disabled={busy} onClick={() => onToggle(stage.joined)} className={stage.joined ? secondaryButton : `${btn} w-full sm:w-auto`}>
            {busy ? "Salvando…" : stage.joined ? "Sair da lista" : "Confirmar presença"}
          </button>
          <Link href="/partida" className={secondaryButton}>
            Ver lista
          </Link>
        </div>
        {error}
      </section>
    );
  }

  if (stage.kind === "play") {
    return (
      <section className={cardClass}>
        {header}
        <p className="mt-sm text-body-md">
          {match.status === "InProgress" ? "A pelada está rolando." : match.status === "AwaitingClose" ? "Jogo encerrado, aguardando a súmula." : "Os times foram sorteados."}
          {myStatus ? <> Você: <strong>{registrationStatusLabel(myStatus)}</strong>.</> : null}
        </p>
        <Link href="/partida" className={`${btn} mt-md w-full sm:w-auto`}>
          {match.status === "Drawn" ? "Ver meu time" : "Ver time e placar"}
        </Link>
        {error}
      </section>
    );
  }

  return (
    <section className={cardClass}>
      {header}
      <p className="mt-sm text-body-md">
        {matchStatusLabel(match.status)}
        {myStatus ? <> · Você: <strong>{registrationStatusLabel(myStatus)}</strong></> : null}
      </p>
      <Link href="/partida" className={`${secondaryButton} mt-md`}>
        Ver detalhes
      </Link>
      {error}
    </section>
  );
}

function stageEyebrow(stage: MatchStage): string {
  switch (stage.kind) {
    case "confirm":
      return "Inscrições abertas";
    case "closed":
      return "Inscrições encerradas";
    case "play":
      return "Dia de jogo";
    default:
      return "Próxima partida";
  }
}
