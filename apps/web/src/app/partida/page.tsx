"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, MatchDetail } from "@/lib/api";
import { matchStatusLabel, registrationStatusLabel, useSession } from "@/lib/useSession";

function formatWhen(iso: string) {
  return new Date(iso).toLocaleString("pt-BR", {
    weekday: "short",
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export default function PartidaPage() {
  const { user, group, loading, error, isAdmin } = useSession();
  const [detail, setDetail] = useState<MatchDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  async function reload(groupId: string) {
    const d = await api.upcomingMatch(groupId);
    setDetail(d);
  }

  useEffect(() => {
    if (!group) return;
    reload(group.id).catch((e) => setMsg(e.message));
  }, [group]);

  async function join() {
    if (!detail) return;
    setBusy(true);
    setMsg(null);
    try {
      setDetail(await api.joinMatch(detail.match.id));
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Erro");
    } finally {
      setBusy(false);
    }
  }

  async function leave() {
    if (!detail) return;
    setBusy(true);
    setMsg(null);
    try {
      setDetail(await api.leaveMatch(detail.match.id));
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Erro");
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (error) return <p className="p-lg text-error">{error}</p>;

  return (
    <AppShell>
      <div className="flex flex-col gap-lg">
        <div>
          <p className="text-label-sm uppercase text-on-surface-variant">Lista da partida</p>
          <h1 className="text-headline-lg">Próxima pelada</h1>
        </div>

        {!detail ? (
          <div className="rounded-xl border border-dashed border-outline-variant bg-surface-container-low p-lg">
            <p className="text-body-md text-on-surface-variant">Nenhuma partida aberta no momento.</p>
            {isAdmin && group ? (
              <CreateMatchForm
                groupId={group.id}
                onCreated={async () => {
                  await reload(group.id);
                }}
              />
            ) : null}
          </div>
        ) : (
          <>
            <section className="rounded-xl border border-outline-variant bg-surface-container-lowest p-lg">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h2 className="text-headline-md">{detail.match.title || "Partida"}</h2>
                  <p className="mt-1 text-body-md text-on-surface-variant">{formatWhen(detail.match.startsAt)}</p>
                  <p className="text-body-md text-on-surface-variant">{detail.match.location}</p>
                </div>
                <span className="rounded-full bg-secondary-container px-3 py-1 text-label-md text-on-secondary-container">
                  {detail.match.confirmedCount}/{detail.match.playerLimit} vagas
                </span>
              </div>
              <p className="mt-md text-label-md text-on-surface-variant">Status: {matchStatusLabel(detail.match.status)}</p>
              {detail.myStatus ? (
                <p className="mt-1 text-body-md">
                  Seu status: <strong>{registrationStatusLabel(detail.myStatus)}</strong>
                  {detail.myWaitlistPosition ? ` (posição ${detail.myWaitlistPosition})` : null}
                </p>
              ) : (
                <p className="mt-1 text-body-md text-on-surface-variant">Você ainda não está na lista.</p>
              )}

              <div className="mt-lg flex flex-col gap-2 sm:flex-row">
                {!detail.myStatus || detail.myStatus === "Withdrawn" || detail.myStatus === "Removed" ? (
                  <button
                    type="button"
                    disabled={busy || detail.match.status !== "RegistrationOpen"}
                    onClick={join}
                    className="rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary disabled:opacity-50"
                  >
                    Entrar na lista
                  </button>
                ) : (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={leave}
                    className="rounded-full border border-outline px-4 py-3 text-label-md"
                  >
                    Sair da lista
                  </button>
                )}
                {isAdmin && detail.match.status === "RegistrationOpen" ? (
                  <button
                    type="button"
                    className="rounded-full border border-outline px-4 py-3 text-label-md"
                    onClick={async () => {
                      await api.transitionMatch(detail.match.id, "RegistrationClosed");
                      await reload(group!.id);
                    }}
                  >
                    Encerrar inscrições
                  </button>
                ) : null}
                {isAdmin && detail.match.status === "Draft" ? (
                  <button
                    type="button"
                    className="rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary"
                    onClick={async () => {
                      await api.transitionMatch(detail.match.id, "RegistrationOpen");
                      await reload(group!.id);
                    }}
                  >
                    Abrir inscrições
                  </button>
                ) : null}
              </div>
              {msg ? (
                <p role="alert" className="mt-md rounded-lg bg-error-container p-3 text-on-error-container">
                  {msg}
                </p>
              ) : null}
            </section>

            <section className="rounded-xl border-l-4 border-pitch-green-container bg-surface-container-low p-md">
              <h3 className="text-label-md">Mensalistas</h3>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Mensalistas ativos (adimplentes ou pendentes) têm prioridade na promoção da fila de espera.
              </p>
            </section>

            <PlayerList
              title="Confirmados"
              players={detail.confirmed}
              isAdmin={isAdmin}
              matchId={detail.match.id}
              onChanged={setDetail}
            />
            <PlayerList
              title="Fila de espera"
              players={detail.waitlist}
              isAdmin={isAdmin}
              matchId={detail.match.id}
              onChanged={setDetail}
              waitlist
            />
          </>
        )}
        <p className="text-label-md text-on-surface-variant">Olá, {user?.displayName}</p>
      </div>
    </AppShell>
  );
}

function PlayerList({
  title,
  players,
  isAdmin,
  matchId,
  onChanged,
  waitlist,
}: {
  title: string;
  players: MatchDetail["confirmed"];
  isAdmin: boolean;
  matchId: string;
  onChanged: (d: MatchDetail) => void;
  waitlist?: boolean;
}) {
  return (
    <section>
      <h2 className="mb-sm text-headline-md">{title}</h2>
      <div className={`overflow-hidden rounded-xl border border-outline-variant ${waitlist ? "border-dashed bg-surface-variant/40" : "bg-surface-container-lowest"}`}>
        {players.length === 0 ? (
          <p className="p-md text-body-md text-on-surface-variant">Nenhum jogador.</p>
        ) : (
          players.map((p) => (
            <div key={p.registrationId} className="flex items-center justify-between border-b border-outline-variant px-md py-sm last:border-0">
              <div className="flex items-center gap-3">
                {waitlist ? <span className="w-6 text-label-md text-on-surface-variant">{p.waitlistPosition}º</span> : null}
                <div>
                  <p className="text-body-md font-semibold">{p.displayName}</p>
                  <p className="text-label-md text-on-surface-variant">Nível {p.skillRating.toFixed(1)}</p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <span className="rounded-full bg-secondary-container px-2 py-0.5 text-label-md">
                  {p.priorityClass === "Monthly" ? "Mensalista" : "Avulso"}
                </span>
                {isAdmin ? (
                  <button
                    type="button"
                    className="text-label-md text-error underline"
                    onClick={async () => onChanged(await api.removeRegistration(matchId, p.playerProfileId))}
                  >
                    Remover
                  </button>
                ) : null}
              </div>
            </div>
          ))
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

  return (
    <form
      className="mt-lg flex flex-col gap-md"
      onSubmit={async (e) => {
        e.preventDefault();
        setBusy(true);
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
        } finally {
          setBusy(false);
        }
      }}
    >
      <label className="text-label-md">
        Data/hora
        <input type="datetime-local" className="mt-1 w-full rounded-lg border border-outline-variant px-3 py-2" value={when} onChange={(e) => setWhen(e.target.value)} />
      </label>
      <label className="text-label-md">
        Local
        <input className="mt-1 w-full rounded-lg border border-outline-variant px-3 py-2" value={location} onChange={(e) => setLocation(e.target.value)} />
      </label>
      <label className="text-label-md">
        Limite
        <input type="number" min={2} className="mt-1 w-full rounded-lg border border-outline-variant px-3 py-2" value={limit} onChange={(e) => setLimit(Number(e.target.value))} />
      </label>
      <button type="submit" disabled={busy} className="rounded-full bg-pitch-green-container py-3 text-label-md text-on-primary">
        Criar partida e abrir lista
      </button>
    </form>
  );
}
