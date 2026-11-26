"use client";

import { useEffect, useState } from "react";
import { api, type LiveDay } from "@/lib/api";
import { currentGame } from "@/lib/live";
import { teamStyle } from "@/lib/teams";
import { colorResolver, DayGamesList, DayStandings } from "./DayBoard";

const POLL_MS = 15_000;

/** Leitura pública do dia (membro ativo): jogo em andamento, partidas com pênaltis e 🧤, classificação. */
export function PublicLiveDay({ matchId, matchStatus }: { matchId: string; matchStatus: string }) {
  const [day, setDay] = useState<LiveDay | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = () =>
      api
        .getLive(matchId)
        .then((d) => {
          if (!cancelled) {
            setDay(d);
            setError(null);
          }
        })
        .catch((e) => !cancelled && setError(e instanceof Error ? e.message : "Erro"));
    load();
    // Atualiza sozinho enquanto a pelada está rolando e a aba está visível.
    const live = matchStatus === "InProgress" || matchStatus === "AwaitingClose" || matchStatus === "Drawn";
    const t = live
      ? window.setInterval(() => {
          if (document.visibilityState !== "hidden") load();
        }, POLL_MS)
      : undefined;
    return () => {
      cancelled = true;
      if (t) window.clearInterval(t);
    };
  }, [matchId, matchStatus]);

  if (error) return null; // sem partidas lançadas ou dia sem sorteio: a tela da lista segue normal
  if (!day || day.games.length === 0) return null;

  const colorOf = colorResolver(day);
  const now = currentGame(day);
  return (
    <section aria-labelledby="ao-vivo-publico" className="flex flex-col gap-sm rounded-xl border border-outline-variant bg-surface-container-low p-md">
      <h2 id="ao-vivo-publico" className="text-headline-md">
        {day.matchStatus === "Completed" ? "Resultado do dia" : "Pelada ao vivo"}
      </h2>
      {now ? (
        <div role="status" aria-live="polite" className="rounded-xl border-2 border-pitch-green-container bg-surface-container-lowest p-md text-center">
          <p className="text-label-md text-on-surface-variant">Partida #{now.order} em andamento</p>
          <p className="sr-only">
            {colorOf(now.homeTeamId)} {now.homeScore} x {now.awayScore} {colorOf(now.awayTeamId)}
          </p>
          <div aria-hidden="true" className="mt-1 grid grid-cols-[1fr_auto_1fr] items-center gap-2">
            {[now.homeTeamId, "x", now.awayTeamId].map((id, i) => {
              if (id === "x") return <span key="x" className="text-headline-md text-on-surface-variant">x</span>;
              const t = day.teams.find((x) => x.id === id);
              return (
                <div key={id} className="flex flex-col items-center">
                  <span className="flex items-center gap-1 text-body-md font-semibold">
                    {t ? <span className={`inline-block h-3 w-3 rounded-full ${teamStyle(t).swatch}`} /> : null}
                    {colorOf(id)}
                  </span>
                  <span className="text-display tabular-nums">{i === 0 ? now.homeScore : now.awayScore}</span>
                </div>
              );
            })}
          </div>
        </div>
      ) : null}
      <DayStandings day={day} caption="Cores no dia" />
      <details>
        <summary className="flex min-h-11 cursor-pointer items-center text-label-md">Partidas do dia ({day.games.length})</summary>
        <div className="mt-sm">
          <DayGamesList day={day} />
        </div>
      </details>
    </section>
  );
}
