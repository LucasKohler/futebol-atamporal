"use client";

import type { ReactNode } from "react";
import type { LiveDay, LiveGame } from "@/lib/api";
import { gameResultText, savesText, sortedStandings } from "@/lib/live";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { teamStyle } from "@/lib/teams";

export function colorResolver(day: LiveDay) {
  return (teamId: string) => {
    const t = day.teams.find((x) => x.id === teamId);
    return t ? teamStyle(t).label : "?";
  };
}

function Swatch({ day, teamId }: { day: LiveDay; teamId: string }) {
  const t = day.teams.find((x) => x.id === teamId);
  if (!t) return null;
  return <span aria-hidden="true" className={`mr-1 inline-block h-3 w-3 rounded-full ${teamStyle(t).swatch}`} />;
}

/** Classificação do dia (regra A): vitórias totais com detalhe das vitórias nos pênaltis, saldo e gols. */
export function DayStandings({ day, caption = "Classificação do dia" }: { day: LiveDay; caption?: string }) {
  const colorOf = colorResolver(day);
  const rows = sortedStandings(day);
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[20rem] text-left text-body-md [&_td]:px-2 [&_td]:text-center [&_td]:tabular-nums [&_thead_th]:px-2 [&_thead_th:not(:first-child)]:text-center">
        <caption className="mb-1 text-left text-label-md text-on-surface-variant">{caption}: vitória no tempo normal ou nos pênaltis vale 1.</caption>
        <thead className="text-label-md text-on-surface-variant">
          <tr>
            <th scope="col" className="py-1">Cor</th>
            <th scope="col">J</th>
            <th scope="col">V</th>
            <th scope="col">
              <abbr title="Vitórias nos pênaltis">V pên.</abbr>
            </th>
            <th scope="col">D</th>
            <th scope="col">Saldo</th>
            <th scope="col">Gols</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s) => (
            <tr key={s.teamId} className={`border-t border-outline-variant ${s.isDayLeader ? "font-semibold" : ""}`}>
              <th scope="row" className="py-2">
                <Swatch day={day} teamId={s.teamId} />
                {colorOf(s.teamId)}
                {s.isDayLeader && s.played > 0 ? " 🏆" : ""}
              </th>
              <td>{s.played}</td>
              <td>{s.wins}</td>
              <td>{s.winsPenalties}</td>
              <td>{s.losses}</td>
              <td>{s.goalsFor - s.goalsAgainst}</td>
              <td>
                {s.goalsFor}:{s.goalsAgainst}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Lista das partidas do dia: placar, vencedor nos pênaltis, 🧤 e autores. `action` adiciona botões (admin). */
export function DayGamesList({ day, action }: { day: LiveDay; action?: (game: LiveGame) => ReactNode }) {
  const colorOf = colorResolver(day);
  const games = [...day.games].sort((a, b) => a.order - b.order);
  if (games.length === 0) return <p className="text-body-md text-on-surface-variant">Nenhuma partida lançada ainda.</p>;
  return (
    <ol className="flex flex-col gap-sm">
      {games.map((g) => {
        const scorers = g.goals.map((x) => `${playerDisplayName(x.displayName)}${x.ownGoal ? " (contra)" : ""}`);
        const saves = savesText(g, playerDisplayName);
        return (
          <li key={g.id} className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="text-label-md text-on-surface-variant">
                  Partida #{g.order}
                  {g.status === "InProgress" ? " · em andamento" : ""}
                </p>
                <p className="text-body-lg font-semibold">
                  <Swatch day={day} teamId={g.homeTeamId} />
                  {gameResultText(g, colorOf)}
                </p>
                {g.status === "Finished" && g.winnerTeamId ? (
                  <p className="text-label-md text-on-surface-variant">
                    Vencedor: {colorOf(g.winnerTeamId)}
                    {g.decidedByPenalties ? " (pênaltis alternados)" : ""}
                  </p>
                ) : null}
                {scorers.length > 0 ? <p className="text-body-md">⚽ {scorers.join(", ")}</p> : null}
                {saves ? <p className="text-body-md">{saves} pênalti(s) defendido(s)</p> : null}
              </div>
              {action ? <div className="flex flex-wrap gap-2">{action(g)}</div> : null}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
