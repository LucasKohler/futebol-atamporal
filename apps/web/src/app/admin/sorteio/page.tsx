"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, Draw, MatchDetail } from "@/lib/api";
import { useSession } from "@/lib/useSession";

export default function SorteioPage() {
  const { group, loading, isAdmin } = useSession();
  const [match, setMatch] = useState<MatchDetail | null>(null);
  const [draw, setDraw] = useState<Draw | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!group) return;
    (async () => {
      const m = await api.upcomingMatch(group.id);
      setMatch(m);
      if (m) setDraw(await api.getDraw(m.match.id));
    })().catch((e) => setMsg(e.message));
  }, [group]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (!isAdmin) return <AppShell><p>Acesso admin.</p></AppShell>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">Sorteio de times</h1>
      {!match ? (
        <p className="mt-md text-on-surface-variant">Nenhuma partida disponível.</p>
      ) : (
        <div className="mt-lg flex flex-col gap-lg">
          <p className="text-body-md text-on-surface-variant">
            {match.confirmed.length} confirmados · status {match.match.status}
          </p>
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              disabled={busy}
              className="rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary"
              onClick={async () => {
                setBusy(true);
                setMsg(null);
                try {
                  if (match.match.status === "RegistrationOpen") {
                    await api.transitionMatch(match.match.id, "RegistrationClosed");
                  }
                  setDraw(await api.createDraw(match.match.id));
                  setMatch(await api.getMatch(match.match.id));
                } catch (e) {
                  setMsg(e instanceof Error ? e.message : "Erro");
                } finally {
                  setBusy(false);
                }
              }}
            >
              {draw ? "Refazer sorteio" : "Realizar sorteio"}
            </button>
            {draw && draw.status !== "Confirmed" ? (
              <button
                type="button"
                disabled={busy}
                className="rounded-full border border-outline px-4 py-3 text-label-md"
                onClick={async () => {
                  setBusy(true);
                  try {
                    setDraw(await api.confirmDraw(match.match.id, draw.id));
                    setMatch(await api.getMatch(match.match.id));
                  } catch (e) {
                    setMsg(e instanceof Error ? e.message : "Erro");
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                Confirmar sorteio
              </button>
            ) : null}
          </div>
          {msg ? <p className="text-error">{msg}</p> : null}
          {draw ? (
            <>
              <p className="text-label-md text-on-surface-variant">
                Diferença de força {draw.strengthDelta.toFixed(1)} · {draw.status === "Confirmed" ? "Confirmado" : "Rascunho"}
              </p>
              <div className="grid gap-md md:grid-cols-2">
                {draw.teams.map((t) => (
                  <div key={t.id} className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
                    <div className="flex justify-between">
                      <h2 className="text-headline-md">{t.name}</h2>
                      <span className="text-label-md">{t.totalSkill.toFixed(1)}</span>
                    </div>
                    <ul className="mt-sm space-y-2">
                      {t.members.map((m) => (
                        <li key={m.playerProfileId} className="flex justify-between text-body-md">
                          <span>{m.displayName}</span>
                          <span className="rounded-full bg-pitch-green-container px-2 py-0.5 text-label-md text-on-primary">
                            {m.skillRating.toFixed(1)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            </>
          ) : (
            <p className="text-on-surface-variant">Sorteio ainda não realizado.</p>
          )}
        </div>
      )}
    </AppShell>
  );
}
