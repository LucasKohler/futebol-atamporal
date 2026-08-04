"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { AppShell } from "@/components/AppShell";
import { api, MatchDetail } from "@/lib/api";
import { matchStatusLabel, registrationStatusLabel, useSession } from "@/lib/useSession";

export default function HomePage() {
  const { user, group, loading, error, isAdmin } = useSession();
  const [upcoming, setUpcoming] = useState<MatchDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    if (!group) return;
    api.upcomingMatch(group.id).then(setUpcoming).catch(() => setUpcoming(null));
  }, [group]);

  if (loading) {
    return <div className="flex min-h-dvh items-center justify-center text-on-surface-variant">Carregando…</div>;
  }
  if (error) {
    return <p className="p-lg text-error">{error}</p>;
  }

  async function joinOrLeave() {
    if (!upcoming) return;
    setBusy(true);
    setMsg(null);
    try {
      const active = upcoming.myStatus === "Confirmed" || upcoming.myStatus === "Waitlisted";
      setUpcoming(active ? await api.leaveMatch(upcoming.match.id) : await api.joinMatch(upcoming.match.id));
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Erro");
    } finally {
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-lg">
        <section>
          <p className="text-label-sm uppercase text-on-surface-variant">Olá</p>
          <h1 className="text-headline-lg">{user?.displayName}</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">{group?.name}</p>
        </section>

        <section className="rounded-xl border border-outline-variant bg-surface-container-lowest p-lg">
          <h2 className="text-headline-md">Próxima partida</h2>
          {upcoming ? (
            <div className="mt-md space-y-3">
              <p className="text-display text-primary">
                {upcoming.match.confirmedCount}/{upcoming.match.playerLimit}
              </p>
              <p className="text-body-md">{upcoming.match.location}</p>
              <p className="text-body-md text-on-surface-variant">
                {new Date(upcoming.match.startsAt).toLocaleString("pt-BR")}
              </p>
              <p className="text-label-md">
                {matchStatusLabel(upcoming.match.status)} · Você: {registrationStatusLabel(upcoming.myStatus)}
                {upcoming.myWaitlistPosition ? ` (#${upcoming.myWaitlistPosition})` : ""}
              </p>
              {msg ? <p className="text-error">{msg}</p> : null}
              <div className="flex flex-wrap gap-2">
                {upcoming.match.status === "RegistrationOpen" ? (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={joinOrLeave}
                    className="rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary disabled:opacity-50"
                  >
                    {upcoming.myStatus === "Confirmed" || upcoming.myStatus === "Waitlisted"
                      ? "Sair da lista"
                      : "Entrar na lista"}
                  </button>
                ) : null}
                <Link href="/partida" className="rounded-full border border-outline px-4 py-3 text-label-md">
                  Ver detalhes
                </Link>
              </div>
            </div>
          ) : (
            <div className="mt-md space-y-3">
              <p className="text-body-md text-on-surface-variant">Nenhuma partida aberta.</p>
              {isAdmin ? (
                <Link href="/partida" className="inline-flex rounded-full bg-pitch-green-container px-4 py-2 text-label-md text-on-primary">
                  Criar partida
                </Link>
              ) : null}
            </div>
          )}
        </section>

        {isAdmin ? (
          <section className="rounded-xl border border-outline-variant bg-surface-container-low p-md">
            <h2 className="text-label-md uppercase text-on-surface-variant">Admin</h2>
            <div className="mt-sm flex flex-wrap gap-2">
              <Link href="/admin/sorteio" className="rounded-full border border-outline px-3 py-2 text-label-md">
                Sorteio
              </Link>
              <Link href="/admin/registro" className="rounded-full border border-outline px-3 py-2 text-label-md">
                Pós-jogo
              </Link>
              <Link href="/admin/mensalistas" className="rounded-full border border-outline px-3 py-2 text-label-md">
                Mensalistas
              </Link>
            </div>
          </section>
        ) : null}
      </div>
    </AppShell>
  );
}
