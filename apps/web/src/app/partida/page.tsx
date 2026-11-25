"use client";

import { useCallback, useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, Draw, MatchDetail } from "@/lib/api";
import { buildWhatsAppSummary } from "@/lib/matchSummary";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { formatRating } from "@/lib/ratings";
import { buildTeamsSummary, isGoalkeeper, isLineupFrozen, orderedMembers, teamStyle } from "@/lib/teams";
import { matchStatusLabel, registrationStatusLabel, useSession } from "@/lib/useSession";

const focusRing =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-pitch-green-container";
const primaryButton = `min-h-11 rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary disabled:opacity-50 ${focusRing}`;
const secondaryButton = `min-h-11 rounded-full border border-outline px-4 py-3 text-label-md disabled:opacity-50 ${focusRing}`;

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

  /** Só a escalação confirmada e ainda válida aparece na tela da partida. */
  const loadDraw = useCallback(async (matchId: string) => {
    const d = await api.getDraw(matchId).catch(() => null);
    setDraw(d && d.status === "Confirmed" && !d.rosterChanged ? d : null);
  }, []);

  const reload = useCallback(
    async (groupId: string) => {
      const d = await api.upcomingMatch(groupId);
      setDetail(d);
      if (d) await loadDraw(d.match.id);
      else setDraw(null);
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
                {isAdmin && match.status === "Draft" ? (
                  <button
                    type="button"
                    disabled={busy}
                    className={primaryButton}
                    onClick={() => run(async () => void (await api.transitionMatch(match.id, "RegistrationOpen")), "Inscrições abertas.")}
                  >
                    Abrir inscrições
                  </button>
                ) : null}
                {isAdmin && match.status === "RegistrationOpen" ? (
                  <button
                    type="button"
                    disabled={busy}
                    className={secondaryButton}
                    onClick={() =>
                      run(async () => void (await api.transitionMatch(match.id, "RegistrationClosed")), "Inscrições encerradas.")
                    }
                  >
                    Encerrar inscrições
                  </button>
                ) : null}
                {isAdmin && match.status === "RegistrationClosed" ? (
                  <button
                    type="button"
                    disabled={busy}
                    className={secondaryButton}
                    onClick={() => run(async () => void (await api.transitionMatch(match.id, "RegistrationOpen")), "Inscrições reabertas.")}
                  >
                    Reabrir inscrições
                  </button>
                ) : null}
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

            {draw ? <ConfirmedTeams draw={draw} /> : null}

            {isAdmin ? (
              <WhatsAppSummary
                detail={detail}
                teams={draw ? buildTeamsSummary(draw) : null}
                timeZone={group?.timeZoneId ?? "America/Sao_Paulo"}
              />
            ) : null}

            <section className="rounded-xl border-l-4 border-pitch-green-container bg-surface-container-low p-md">
              <h3 className="text-label-md">Ordem da fila</h3>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Mensalistas ativos (adimplentes ou pendentes) ficam à frente na fila; depois vale a ordem de chegada. Quem
                desiste libera a vaga para o 1º da fila.
              </p>
            </section>

            <PlayerList
              title="Confirmados"
              players={detail.confirmed}
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
              players={detail.waitlist}
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

/** Admin copia o texto da lista e compartilha pela própria conta do WhatsApp (o app não envia nada). */
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

function WhatsAppSummary({ detail, teams, timeZone }: { detail: MatchDetail; teams: string | null; timeZone: string }) {
  const [copied, setCopied] = useState<string | null>(null);
  const [showText, setShowText] = useState(false);
  const [text, setText] = useState("");

  function currentText() {
    const link = `${window.location.origin}/partida`;
    const value = buildWhatsAppSummary(detail, { link, now: new Date(), timeZone, teams });
    setText(value);
    return value;
  }

  // Texto aberto acompanha a lista e a escalação: após entrar, sair, remover ou confirmar, regenera.
  useEffect(() => {
    if (!showText) return;
    setText(buildWhatsAppSummary(detail, { link: `${window.location.origin}/partida`, now: new Date(), timeZone, teams }));
  }, [detail, showText, timeZone, teams]);

  async function copy() {
    const value = currentText();
    try {
      await navigator.clipboard.writeText(value);
      setCopied("Resumo copiado. Cole no grupo do WhatsApp.");
    } catch {
      setShowText(true);
      setCopied("Não foi possível copiar automaticamente. Selecione o texto abaixo e copie.");
    }
  }

  return (
    <section aria-labelledby="resumo-titulo" className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
      <h2 id="resumo-titulo" className="text-headline-md">
        Resumo para o WhatsApp
      </h2>
      <p className="mt-1 text-body-md text-on-surface-variant">
        Data, local, vagas, confirmados e fila na ordem da lista. Sem telefone, nota ou pagamento.
      </p>
      <div className="mt-md flex flex-col gap-2 sm:flex-row">
        <button type="button" onClick={copy} className={primaryButton}>
          Copiar resumo
        </button>
        <button
          type="button"
          aria-expanded={showText}
          aria-controls="resumo-texto"
          onClick={() => {
            if (!showText) currentText();
            setShowText((v) => !v);
          }}
          className={secondaryButton}
        >
          {showText ? "Ocultar texto" : "Ver texto"}
        </button>
      </div>
      <p aria-live="polite" className="mt-2 text-label-md text-on-surface-variant">
        {copied ?? ""}
      </p>
      {showText ? (
        <textarea
          id="resumo-texto"
          readOnly
          aria-label="Texto do resumo da lista"
          value={text}
          rows={Math.min(24, text.split("\n").length + 1)}
          onFocus={(e) => e.currentTarget.select()}
          className={`mt-2 w-full rounded-lg border border-outline-variant bg-surface-container-low p-3 font-mono text-label-md ${focusRing}`}
        />
      ) : null}
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
              return (
                <li
                  key={p.registrationId}
                  className="flex items-center justify-between gap-2 border-b border-outline-variant px-md py-sm last:border-0"
                >
                  <div className="flex min-w-0 items-center gap-3">
                    <span className="w-7 shrink-0 text-label-md text-on-surface-variant">
                      {waitlist ? `${p.waitlistPosition ?? i + 1}º` : `${i + 1}.`}
                    </span>
                    <div className="min-w-0">
                      <p className="truncate text-body-md font-semibold">{name}</p>
                      {isAdmin && p.skillRating !== null ? (
                        <p className="text-label-md text-on-surface-variant">Linha {formatRating(p.skillRating)}</p>
                      ) : null}
                    </div>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <span className="rounded-full bg-secondary-container px-2 py-0.5 text-label-md">
                      {p.priorityClass === "Monthly" ? "Mensalista" : "Avulso"}
                    </span>
                    {canRemove ? (
                      <button
                        type="button"
                        disabled={busy}
                        aria-label={`Remover ${name} da lista`}
                        className={`min-h-11 px-2 text-label-md text-error underline disabled:opacity-50 ${focusRing}`}
                        onClick={() => onRemove(p)}
                      >
                        Remover
                      </button>
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
