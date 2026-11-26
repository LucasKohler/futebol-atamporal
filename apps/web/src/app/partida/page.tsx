"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { WhatsAppSummary } from "@/components/WhatsAppSummary";
import { PaymentsLink } from "@/components/pix/PaymentsLink";
import { PublicLiveDay } from "@/components/live/PublicLiveDay";
import { AdminConfirmButton } from "@/components/admin/AdminDrawer";
import { AdminSearch } from "@/components/admin/AdminSearch";
import { RoundStepper } from "@/components/admin/RoundStepper";
import { focusRing, inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import { filterRegistrations } from "@/lib/adminFilters";
import { api, Draw, MatchDetail, MatchSummary } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { formatRating } from "@/lib/ratings";
import { deriveRoundStep } from "@/lib/roundStep";
import { buildTeamsSummary, isGoalkeeper, isLineupFrozen, orderedMembers, teamStyle } from "@/lib/teams";
import { matchStatusLabel, registrationStatusLabel, useSession } from "@/lib/useSession";

/** A partir desse total de inscritos, o admin ganha busca na lista. */
const SEARCH_THRESHOLD = 8;

function formatWhen(iso: string, timeZone?: string) {
  return new Date(iso).toLocaleString("pt-BR", {
    weekday: "short",
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
    timeZone,
  });
}

export default function PartidaPage() {
  const { user, group, loading, error, isAdmin } = useSession();
  const [detail, setDetail] = useState<MatchDetail | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const [draw, setDraw] = useState<Draw | null>(null);
  /** Sorteio como está (prévia ou confirmado), só para o stepper do admin. */
  const [rawDraw, setRawDraw] = useState<Draw | null>(null);
  const [query, setQuery] = useState("");

  /** Só a escalação confirmada e ainda válida aparece na tela da partida. */
  const loadDraw = useCallback(async (matchId: string) => {
    const d = await api.getDraw(matchId).catch(() => null);
    setRawDraw(d);
    setDraw(d && d.status === "Confirmed" && !d.rosterChanged ? d : null);
  }, []);

  const reload = useCallback(
    async (groupId: string) => {
      const d = await api.upcomingMatch(groupId);
      setDetail(d);
      if (d) await loadDraw(d.match.id);
      else {
        setDraw(null);
        setRawDraw(null);
      }
      setLoaded(true);
    },
    [loadDraw],
  );

  useEffect(() => {
    if (!group) return;
    reload(group.id).catch((e) => {
      setMsg(e instanceof Error ? e.message : "Não foi possível carregar a lista.");
      setLoaded(true);
    });
  }, [group, reload]);

  /** Executa uma ação, mostra o estado persistido devolvido pela API e anuncia o resultado. */
  async function run(action: () => Promise<MatchDetail | void>, success: string) {
    setBusy(true);
    setMsg(null);
    setNotice(null);
    try {
      const result = await action();
      if (result) {
        setDetail(result);
        await loadDraw(result.match.id); // desistência/remoção pode invalidar a escalação
      } else if (group) await reload(group.id);
      setNotice(success);
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Erro");
      if (group) await reload(group.id).catch(() => undefined);
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (error) return <p className="p-lg text-error">{error}</p>;

  const match = detail?.match;
  const canJoin = !detail?.myStatus || detail.myStatus === "Withdrawn" || detail.myStatus === "Removed";
  // Mesma política da API: depois do início, ninguém sai nem é removido (elenco preservado).
  const frozen = match ? isLineupFrozen(match.status) : false;

  return (
    <AppShell>
      <div className="flex flex-col gap-lg">
        <div>
          <p className="text-label-sm uppercase text-on-surface-variant">Lista da partida</p>
          <h1 className="text-headline-lg">Próxima pelada</h1>
        </div>

        <div aria-live="polite" role="status" className="sr-only">
          {notice ?? ""}
        </div>

        {!loaded ? (
          <p className="text-body-md text-on-surface-variant">Carregando lista…</p>
        ) : !detail || !match ? (
          <div className="rounded-xl border border-dashed border-outline-variant bg-surface-container-low p-lg">
            <p className="text-body-md text-on-surface-variant">Nenhuma partida aberta no momento.</p>
            {msg ? (
              <p role="alert" className="mt-md rounded-lg bg-error-container p-3 text-on-error-container">
                {msg}
              </p>
            ) : null}
            {isAdmin && group ? (
              <CreateMatchForm
                groupId={group.id}
                onCreated={async () => {
                  await reload(group.id);
                  setNotice("Partida criada e lista aberta.");
                }}
              />
            ) : null}
          </div>
        ) : (
          <>
            <section aria-labelledby="partida-titulo" className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md sm:p-lg">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h2 id="partida-titulo" className="text-headline-md">
                    {match.title || "Partida"}
                  </h2>
                  <p className="mt-1 text-body-md text-on-surface-variant">{formatWhen(match.startsAt, group?.timeZoneId)}</p>
                  <p className="text-body-md text-on-surface-variant">{match.location}</p>
                </div>
                <span className="rounded-full bg-secondary-container px-3 py-1 text-label-md text-on-secondary-container">
                  {match.confirmedCount}/{match.playerLimit} vagas
                </span>
              </div>

              <dl className="mt-md grid grid-cols-3 gap-2 text-center">
                <Stat label="Confirmados" value={match.confirmedCount} />
                <Stat label="Vagas livres" value={match.spotsRemaining} />
                <Stat label="Na fila" value={match.waitlistCount} />
              </dl>

              <p className="mt-md text-label-md text-on-surface-variant">Status: {matchStatusLabel(match.status)}</p>
              {detail.myStatus ? (
                <p className="mt-1 text-body-md">
                  Seu status: <strong>{registrationStatusLabel(detail.myStatus)}</strong>
                  {detail.myWaitlistPosition ? <strong> · {detail.myWaitlistPosition}º na fila</strong> : null}
                </p>
              ) : (
                <p className="mt-1 text-body-md text-on-surface-variant">Você ainda não está na lista.</p>
              )}

              <div className="mt-lg flex flex-col gap-2 sm:flex-row sm:flex-wrap">
                {canJoin ? (
                  <button
                    type="button"
                    disabled={busy || match.status !== "RegistrationOpen"}
                    onClick={() => run(() => api.joinMatch(match.id), "Você entrou na lista.")}
                    className={primaryButton}
                  >
                    Entrar na lista
                  </button>
                ) : frozen ? (
                  <p className="text-body-md text-on-surface-variant">
                    A partida já começou: o elenco está fechado. Se não puder jogar, avise o admin; a ausência é registrada no
                    fechamento.
                  </p>
                ) : (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => run(() => api.leaveMatch(match.id), "Você saiu da lista.")}
                    className={secondaryButton}
                  >
                    Sair da lista
                  </button>
                )}
                <button
                  type="button"
                  disabled={busy}
                  className={secondaryButton}
                  onClick={() => run(async () => undefined, "Lista atualizada.")}
                >
                  Atualizar lista
                </button>
              </div>
              {msg ? (
                <p role="alert" className="mt-md rounded-lg bg-error-container p-3 text-on-error-container">
                  {msg}
                </p>
              ) : null}
              {notice ? <p className="mt-md text-label-md text-on-surface-variant">{notice}</p> : null}
            </section>

            {isAdmin ? (
              <AdminMatchPanel
                match={match}
                rawDraw={rawDraw}
                busy={busy}
                onTransition={(to, success) => run(async () => void (await api.transitionMatch(match.id, to)), success)}
                onUpdate={(body) => run(async () => void (await api.updateMatch(match.id, body)), "Dados da partida salvos.")}
              />
            ) : null}

            {["Drawn", "InProgress", "AwaitingClose"].includes(match.status) ? <PublicLiveDay matchId={match.id} matchStatus={match.status} /> : null}

            {draw ? <ConfirmedTeams draw={draw} /> : null}

            {isAdmin ? (
              <WhatsAppSummary
                detail={detail}
                teams={draw ? buildTeamsSummary(draw) : null}
                timeZone={group?.timeZoneId ?? "America/Sao_Paulo"}
              />
            ) : null}

            {detail.myStatus === "Confirmed" ? <PaymentsLink text="Avulso? Pague a diária desta pelada com Pix." /> : null}

            <section className="rounded-xl border-l-4 border-pitch-green-container bg-surface-container-low p-md">
              <h3 className="text-label-md">Ordem da fila</h3>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Mensalistas ativos (adimplentes ou pendentes) ficam à frente na fila; depois vale a ordem de chegada. Quem
                desiste libera a vaga para o 1º da fila.
              </p>
            </section>

            {isAdmin && detail.confirmed.length + detail.waitlist.length >= SEARCH_THRESHOLD ? (
              <AdminSearch
                label="Buscar na lista e na fila"
                placeholder="Nome ou apelido"
                value={query}
                onChange={setQuery}
                resultCount={filterRegistrations([...detail.confirmed, ...detail.waitlist], query).length}
              />
            ) : null}

            <PlayerList
              title="Confirmados"
              players={isAdmin ? filterRegistrations(detail.confirmed, query) : detail.confirmed}
              positionOf={(p) => detail.confirmed.findIndex((x) => x.registrationId === p.registrationId) + 1}
              isAdmin={isAdmin}
              canRemove={isAdmin && !frozen}
              busy={busy}
              onRemove={(p) =>
                run(() => api.removeRegistration(match.id, p.playerProfileId), `${playerDisplayName(p.displayName)} removido da lista.`)
              }
              emptyText="Ninguém confirmado ainda."
            />
            <PlayerList
              title="Fila de espera"
              players={isAdmin ? filterRegistrations(detail.waitlist, query) : detail.waitlist}
              isAdmin={isAdmin}
              canRemove={isAdmin && !frozen}
              busy={busy}
              onRemove={(p) =>
                run(() => api.removeRegistration(match.id, p.playerProfileId), `${playerDisplayName(p.displayName)} removido da fila.`)
              }
              emptyText="Fila vazia."
              waitlist
            />
          </>
        )}
        <p className="text-label-md text-on-surface-variant">Olá, {playerDisplayName(user?.registeredName, user?.nickname)}</p>
      </div>
    </AppShell>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-lg bg-surface-container-low px-2 py-2">
      <dt className="text-label-sm text-on-surface-variant">{label}</dt>
      <dd className="text-headline-md">{value}</dd>
    </div>
  );
}

/** Escalação confirmada: times por texto e cor, goleiro identificado, sem notas. */
function ConfirmedTeams({ draw }: { draw: Draw }) {
  const teams = [...draw.teams].sort((a, b) => a.sortOrder - b.sortOrder);
  return (
    <section aria-labelledby="times-titulo" className="flex flex-col gap-sm">
      <h2 id="times-titulo" className="text-headline-md">
        Times da rodada
      </h2>
      <div className="grid gap-sm sm:grid-cols-3">
        {teams.map((team) => {
          const style = teamStyle(team);
          return (
            <div key={team.id} className={`rounded-xl border-2 ${style.border} bg-surface-container-lowest p-md`}>
              <h3 className="flex items-center gap-2 text-label-md font-semibold">
                <span aria-hidden="true" className={`inline-block h-3 w-3 rounded-full ${style.swatch}`} />
                {style.label}
              </h3>
              <ul className="mt-1 text-body-md">
                {orderedMembers(team).map((m) => (
                  <li key={m.playerProfileId}>
                    {playerDisplayName(m.displayName)}
                    {isGoalkeeper(m) ? <span className="text-label-md text-on-surface-variant"> · goleiro</span> : null}
                  </li>
                ))}
              </ul>
              {!draw.isLegacy && !team.members.some(isGoalkeeper) ? (
                <p className="mt-1 text-label-md text-on-surface-variant">Gol em rodízio</p>
              ) : null}
            </div>
          );
        })}
      </div>
    </section>
  );
}

function PlayerList({
  title,
  players,
  isAdmin,
  canRemove,
  busy,
  onRemove,
  emptyText,
  waitlist,
  positionOf,
}: {
  title: string;
  players: MatchDetail["confirmed"];
  isAdmin: boolean;
  /** Admin com elenco ainda aberto (antes do início da partida). */
  canRemove: boolean;
  busy: boolean;
  onRemove: (p: MatchDetail["confirmed"][number]) => void;
  emptyText: string;
  waitlist?: boolean;
  /** Posição real na lista completa (a busca filtra sem renumerar). */
  positionOf?: (p: MatchDetail["confirmed"][number]) => number;
}) {
  return (
    <section aria-label={`${title} (${players.length})`}>
      <h2 className="mb-sm text-headline-md">
        {title} <span className="text-body-md text-on-surface-variant">({players.length})</span>
      </h2>
      <div
        className={`overflow-hidden rounded-xl border border-outline-variant ${waitlist ? "border-dashed bg-surface-variant/40" : "bg-surface-container-lowest"}`}
      >
        {players.length === 0 ? (
          <p className="p-md text-body-md text-on-surface-variant">{emptyText}</p>
        ) : (
          <ol>
            {players.map((p, i) => {
              const name = playerDisplayName(p.displayName);
              const position = waitlist ? (p.waitlistPosition ?? i + 1) : (positionOf?.(p) ?? i + 1);
              return (
                <li
                  key={p.registrationId}
                  className="flex flex-wrap items-center justify-between gap-2 border-b border-outline-variant px-md py-sm last:border-0"
                >
                  <div className="flex min-w-0 items-center gap-3">
                    <span className="w-7 shrink-0 text-label-md text-on-surface-variant">{waitlist ? `${position}º` : `${position}.`}</span>
                    <div className="min-w-0">
                      <p className="truncate text-body-md font-semibold">{name}</p>
                      {isAdmin && p.skillRating !== null ? (
                        <p className="text-label-md text-on-surface-variant">Linha {formatRating(p.skillRating)}</p>
                      ) : null}
                    </div>
                  </div>
                  <div className="flex shrink-0 flex-wrap items-center justify-end gap-2">
                    <span className="rounded-full bg-secondary-container px-2 py-0.5 text-label-md">
                      {p.priorityClass === "Monthly" ? "Mensalista" : "Avulso"}
                    </span>
                    {canRemove ? (
                      <AdminConfirmButton
                        label="Remover"
                        question={`Remover ${name}?`}
                        confirmLabel="Remover"
                        disabled={busy}
                        className={`min-h-11 px-2 text-label-md text-error underline disabled:opacity-50 ${focusRing}`}
                        onConfirm={() => onRemove(p)}
                      />
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ol>
        )}
      </div>
    </section>
  );
}

/** "2026-11-28T10:00" no fuso do aparelho, para o campo datetime-local (mesma regra do formulário de criação). */
function toLocalInput(iso: string) {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Controles do admin na tela da partida: etapa da rodada, transições da lista e edição dos dados. */
function AdminMatchPanel({
  match,
  rawDraw,
  busy,
  onTransition,
  onUpdate,
}: {
  match: MatchSummary;
  rawDraw: Draw | null;
  busy: boolean;
  onTransition: (to: string, success: string) => void;
  onUpdate: (body: { title: string | null; startsAt: string; location: string; playerLimit: number; notes: string | null }) => void;
}) {
  const step = deriveRoundStep({ status: match.status, confirmedCount: match.confirmedCount, draw: rawDraw });
  const editable = match.status !== "Completed" && match.status !== "Cancelled";
  const frozen = isLineupFrozen(match.status);
  const SUCCESS: Record<string, string> = {
    RegistrationOpen: "Inscrições abertas.",
    InProgress: "Partida iniciada. O elenco está congelado.",
    AwaitingClose: "Jogo encerrado. Falta lançar a súmula.",
  };

  return (
    <section aria-labelledby="admin-partida-titulo" className="flex flex-col gap-sm rounded-xl border-l-4 border-pitch-green-container bg-surface-container-low p-md">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="admin-partida-titulo" className="text-headline-md">
          Painel do admin
        </h2>
        <Link href="/admin" className={`inline-flex min-h-11 items-center text-label-md underline ${focusRing}`}>
          Central do admin
        </Link>
      </div>

      <RoundStepper state={step} busy={busy} hideCtaOn="/partida" onTransition={(to) => onTransition(to, SUCCESS[to] ?? "Partida atualizada.")} />

      <div className="flex flex-wrap gap-2">
        {match.status === "RegistrationOpen" ? (
          <AdminConfirmButton
            tone="neutral"
            label="Encerrar inscrições"
            question="Ninguém mais entra ou sai pela lista. Encerrar?"
            confirmLabel="Encerrar inscrições"
            disabled={busy}
            onConfirm={() => onTransition("RegistrationClosed", "Inscrições encerradas.")}
          />
        ) : null}
        {match.status === "RegistrationClosed" ? (
          <button type="button" disabled={busy} className={secondaryButton} onClick={() => onTransition("RegistrationOpen", "Inscrições reabertas.")}>
            Reabrir inscrições
          </button>
        ) : null}
        {match.status === "InProgress" ? (
          <button type="button" disabled={busy} className={secondaryButton} onClick={() => onTransition("AwaitingClose", SUCCESS.AwaitingClose)}>
            Encerrar jogo (aguardar súmula)
          </button>
        ) : null}
      </div>

      {editable ? <EditMatchDetails match={match} frozen={frozen} busy={busy} onSave={onUpdate} /> : null}
    </section>
  );
}

function EditMatchDetails({
  match,
  frozen,
  busy,
  onSave,
}: {
  match: MatchSummary;
  frozen: boolean;
  busy: boolean;
  onSave: (body: { title: string | null; startsAt: string; location: string; playerLimit: number; notes: string | null }) => void;
}) {
  const [title, setTitle] = useState(match.title ?? "");
  const [when, setWhen] = useState(() => toLocalInput(match.startsAt));
  const [location, setLocation] = useState(match.location);
  const [limit, setLimit] = useState(match.playerLimit);
  const [notes, setNotes] = useState(match.notes ?? "");

  return (
    <details className="rounded-lg border border-outline-variant bg-surface-container-lowest">
      <summary className={`flex min-h-11 cursor-pointer items-center px-md text-label-md ${focusRing}`}>Editar dados da partida</summary>
      <form
        className="flex flex-col gap-md px-md pb-md"
        onSubmit={(e) => {
          e.preventDefault();
          onSave({ title: title.trim() || null, startsAt: new Date(when).toISOString(), location: location.trim(), playerLimit: limit, notes: notes.trim() || null });
        }}
      >
        <label className="flex flex-col gap-1 text-label-md">
          Título
          <input className={inputClass} value={title} maxLength={120} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <div className="grid gap-md sm:grid-cols-2">
          <label className="flex flex-col gap-1 text-label-md">
            Data/hora
            <input type="datetime-local" required className={inputClass} value={when} onChange={(e) => setWhen(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1 text-label-md">
            Limite de jogadores
            <input
              type="number"
              min={2}
              max={60}
              required
              disabled={frozen}
              className={`${inputClass} disabled:opacity-50`}
              value={limit}
              onChange={(e) => setLimit(Number(e.target.value))}
            />
            {frozen ? <span className="font-normal text-on-surface-variant">Partida iniciada: o limite não muda mais.</span> : null}
          </label>
        </div>
        <label className="flex flex-col gap-1 text-label-md">
          Local
          <input required className={inputClass} value={location} maxLength={200} onChange={(e) => setLocation(e.target.value)} />
        </label>
        <label className="flex flex-col gap-1 text-label-md">
          Observações
          <textarea className={inputClass} rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </label>
        <button type="submit" disabled={busy} className={`${primaryButton} self-start`}>
          Salvar dados
        </button>
      </form>
    </details>
  );
}

function CreateMatchForm({ groupId, onCreated }: { groupId: string; onCreated: () => Promise<void> }) {
  const [location, setLocation] = useState("Arena Central");
  const [limit, setLimit] = useState(14);
  const [when, setWhen] = useState(() => {
    const d = new Date();
    d.setDate(d.getDate() + ((6 - d.getDay() + 7) % 7 || 7));
    d.setHours(10, 0, 0, 0);
    return d.toISOString().slice(0, 16);
  });
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  return (
    <form
      className="mt-lg flex flex-col gap-md"
      onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
        setFormError(null);
        try {
          await api.createMatch({
            groupId,
            title: "Pelada semanal",
            startsAt: new Date(when).toISOString(),
            location,
            playerLimit: limit,
            openRegistrations: true,
          });
          await onCreated();
        } catch (err) {
          setFormError(err instanceof Error ? err.message : "Erro ao criar partida");
        } finally {
          setBusy(false);
        }
      }}
    >
      <label className="text-label-md">
        Data/hora
        <input
          type="datetime-local"
          className={`mt-1 w-full rounded-lg border border-outline-variant px-3 py-2 ${focusRing}`}
          value={when}
          onChange={(e) => setWhen(e.target.value)}
        />
      </label>
      <label className="text-label-md">
        Local
        <input
          className={`mt-1 w-full rounded-lg border border-outline-variant px-3 py-2 ${focusRing}`}
          value={location}
          onChange={(e) => setLocation(e.target.value)}
        />
      </label>
      <label className="text-label-md">
        Limite
        <input
          type="number"
          min={2}
          className={`mt-1 w-full rounded-lg border border-outline-variant px-3 py-2 ${focusRing}`}
          value={limit}
          onChange={(e) => setLimit(Number(e.target.value))}
        />
      </label>
      {formError ? (
        <p role="alert" className="rounded-lg bg-error-container p-3 text-on-error-container">
          {formError}
        </p>
      ) : null}
      <button type="submit" disabled={busy} className={primaryButton}>
        Criar partida e abrir lista
      </button>
    </form>
  );
}
