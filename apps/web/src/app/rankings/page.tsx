"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, RankingRow, Scorer, SeasonStats } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { groupPeriod, leaderText, monthName, plural, type Period } from "@/lib/season";
import { teamStyle } from "@/lib/teams";
import { useSession } from "@/lib/useSession";

const focusRing =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-pitch-green-container";
type Tab = "artilharia" | "cores" | "jogadores" | "geral";

export default function RankingsPage() {
  const { group, loading } = useSession();
  const [tab, setTab] = useState<Tab>("artilharia");
  // Período inicial no fuso do GRUPO (mesma regra da API), nunca no fuso do aparelho.
  const [period, setPeriod] = useState<Period | null>(null);
  const [season, setSeason] = useState<SeasonStats | null>(null);
  const [rows, setRows] = useState<RankingRow[]>([]);
  const [sortBy, setSortBy] = useState("wins");
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    if (!group) return;
    setPeriod((current) => current ?? groupPeriod(new Date(), group.timeZoneId));
  }, [group]);

  const year = period?.year ?? 0;
  const month = period?.month ?? 1;

  useEffect(() => {
    if (!group || !period) return;
    api.season(group.id, period.year, period.month).then(setSeason).catch((e) => setMsg(e instanceof Error ? e.message : "Erro"));
  }, [group, period]);

  useEffect(() => {
    if (!group || tab !== "geral") return;
    api.rankings(group.id, sortBy).then(setRows).catch((e) => setMsg(e instanceof Error ? e.message : "Erro"));
  }, [group, sortBy, tab]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">Rankings</h1>

      <div className="mt-md flex flex-wrap items-end gap-2">
        <label className="text-label-md">
          Mês
          <select
            className={`mt-1 block rounded-lg border border-outline-variant px-2 py-2 ${focusRing}`}
            value={month}
            disabled={!period}
            onChange={(e) => setPeriod((p) => (p ? { ...p, month: Number(e.target.value) } : p))}
          >
            {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
              <option key={m} value={m}>
                {monthName(m)}
              </option>
            ))}
          </select>
        </label>
        <label className="text-label-md">
          Ano
          <input
            type="number"
            className={`mt-1 block w-24 rounded-lg border border-outline-variant px-2 py-2 ${focusRing}`}
            value={period ? year : ""}
            disabled={!period}
            onChange={(e) => setPeriod((p) => (p ? { ...p, year: Number(e.target.value) || p.year } : p))}
          />
        </label>
      </div>

      <div role="tablist" aria-label="Rankings" className="mt-md flex flex-wrap gap-2">
        {(
          [
            ["artilharia", "Artilharia"],
            ["cores", "Cores e rodadas"],
            ["jogadores", "Jogadores e prêmios"],
            ["geral", "Geral (histórico)"],
          ] as [Tab, string][]
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={tab === key}
            onClick={() => setTab(key)}
            className={`min-h-11 rounded-full px-3 py-1.5 text-label-md ${focusRing} ${tab === key ? "bg-pitch-green-container text-on-primary" : "border border-outline"}`}
          >
            {label}
          </button>
        ))}
      </div>

      {msg ? (
        <p role="alert" className="mt-md rounded-lg bg-error-container p-3 text-on-error-container">
          {msg}
        </p>
      ) : null}

      {season && tab === "artilharia" ? (
        <div className="mt-lg grid gap-lg md:grid-cols-2">
          <ScorerList title={`Artilheiro de ${monthName(month)}`} scorers={season.monthScorers} />
          <ScorerList title={`Artilheiro de ${year}`} scorers={season.yearScorers} />
          <p className="text-label-md text-on-surface-variant md:col-span-2">
            Pela data do jogo no fuso do grupo ({season.timeZoneId}). Gol contra não entra. Empate em gols divide a liderança. Rodadas antigas
            (Time A/B) não tinham gols registrados.
          </p>
        </div>
      ) : null}

      {season && tab === "cores" ? (
        <div className="mt-lg flex flex-col gap-lg">
          <section aria-labelledby="cores-titulo">
            <h2 id="cores-titulo" className="text-headline-md">
              Vitórias por cor · {monthName(month)} de {year}
            </h2>
            <table className="mt-sm w-full max-w-xl text-left text-body-md">
              <thead className="text-label-md text-on-surface-variant">
                <tr>
                  <th scope="col">Cor</th>
                  <th scope="col">Rodadas vencidas</th>
                  <th scope="col">V</th>
                  <th scope="col">
                    <abbr title="Vitórias nos pênaltis alternados">V pên.</abbr>
                  </th>
                  <th scope="col">E</th>
                  <th scope="col">D</th>
                  <th scope="col">Gols</th>
                </tr>
              </thead>
              <tbody>
                {season.colors.map((c) => {
                  const style = teamStyle({ color: c.color, name: c.color });
                  return (
                    <tr key={c.color}>
                      <th scope="row" className="py-1">
                        <span aria-hidden="true" className={`mr-1 inline-block h-3 w-3 rounded-full ${style.swatch}`} />
                        {style.label}
                      </th>
                      <td>{c.roundsWon}</td>
                      <td>{c.wins}</td>
                      <td>{c.winsPenalties ?? 0}</td>
                      <td>{c.draws}</td>
                      <td>{c.losses}</td>
                      <td>
                        {c.goalsFor}:{c.goalsAgainst}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </section>
          <section aria-labelledby="rodadas-titulo">
            <h2 id="rodadas-titulo" className="text-headline-md">
              Rodadas
            </h2>
            {season.rounds.length === 0 ? <p className="text-body-md text-on-surface-variant">Nenhuma rodada encerrada no mês.</p> : null}
            <ul className="mt-sm flex flex-col gap-sm">
              {season.rounds.map((r) => (
                <li key={r.matchId} className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
                  <p className="font-semibold">
                    {new Date(`${r.localDate}T12:00:00`).toLocaleDateString("pt-BR")} · {r.title || "Partida"}
                  </p>
                  {r.isLegacy ? (
                    <p className="text-label-md text-on-surface-variant">Rodada antiga (Time A/B): cores e gols indisponíveis.</p>
                  ) : (
                    <>
                      <p className="text-body-md">Venceu: {r.winnerColors.join(" e ") || "—"}</p>
                      <p className="text-label-md text-on-surface-variant">{r.games.join(" · ")}</p>
                    </>
                  )}
                </li>
              ))}
            </ul>
          </section>
        </div>
      ) : null}

      {season && tab === "jogadores" ? (
        <section className="mt-lg" aria-labelledby="jogadores-titulo">
          <h2 id="jogadores-titulo" className="text-headline-md">
            Jogadores · {monthName(month)} de {year}
          </h2>
          <ol className="mt-sm space-y-2">
            {season.players.map((p) => (
              <li key={p.playerProfileId} className="rounded-xl border border-outline-variant bg-surface-container-lowest px-md py-sm">
                <p className="font-semibold">{playerDisplayName(p.displayName)}</p>
                <p className="text-label-md text-on-surface-variant">
                  {plural(p.goals, "gol", "gols")} · {plural(p.roundsPlayed, "rodada", "rodadas")} · {plural(p.roundsWon, "rodada vencida", "rodadas vencidas")} ·{" "}
                  {plural(p.gamesWon, "jogo vencido", "jogos vencidos")}
                  {p.gamesWonOnPenalties ? ` (${p.gamesWonOnPenalties} nos pênaltis)` : ""} · Melhor Jogador {p.bestPlayer}× · Melhor Goleiro {p.bestGoalkeeper}×
                  {p.penaltySaves ? ` · 🧤 ${plural(p.penaltySaves, "pênalti defendido", "pênaltis defendidos")}` : ""}
                </p>
              </li>
            ))}
          </ol>
          {season.players.length === 0 ? <p className="text-body-md text-on-surface-variant">Sem rodadas no mês.</p> : null}
        </section>
      ) : null}

      {tab === "geral" ? (
        <section className="mt-lg">
          <div className="flex flex-wrap gap-2">
            {[
              ["wins", "Vitórias"],
              ["winrate", "Aproveitamento"],
              ["rating", "Nota"],
              ["motm", "Craques"],
              ["penaltySaves", "🧤 Pênaltis defendidos"],
            ].map(([key, label]) => (
              <button
                key={key}
                type="button"
                onClick={() => setSortBy(key)}
                className={`min-h-11 rounded-full px-3 py-1.5 text-label-md ${focusRing} ${sortBy === key ? "bg-pitch-green-container text-on-primary" : "border border-outline"}`}
              >
                {label}
              </button>
            ))}
          </div>
          <ol className="mt-md space-y-2">
            {rows.map((r, i) => (
              <li
                key={r.playerProfileId}
                className="flex items-center justify-between rounded-xl border border-outline-variant bg-surface-container-lowest px-md py-sm"
              >
                <div className="flex items-center gap-3">
                  <span className="w-6 text-label-md text-on-surface-variant">{i + 1}</span>
                  <div>
                    <p className="font-semibold">{playerDisplayName(r.displayName)}</p>
                    <p className="text-label-md text-on-surface-variant">
                      {r.matches} jogos · {r.wins}V {r.losses}D · {(r.winRate * 100).toFixed(0)}%
                      {r.penaltySaves ? ` · 🧤 ${r.penaltySaves}` : ""}
                    </p>
                  </div>
                </div>
                <div className="text-right text-label-md">
                  {sortBy === "motm" ? `${r.manOfTheMatch}×` : null}
                  {sortBy === "rating" ? r.averageRating?.toFixed(1) ?? "—" : null}
                  {sortBy === "wins" ? `${r.wins}V` : null}
                  {sortBy === "winrate" ? `${(r.winRate * 100).toFixed(0)}%` : null}
                  {sortBy === "penaltySaves" ? <span aria-label={`${r.penaltySaves ?? 0} pênaltis defendidos`}>🧤 {r.penaltySaves ?? 0}</span> : null}
                </div>
              </li>
            ))}
          </ol>
          {rows.length === 0 ? <p className="mt-md text-on-surface-variant">Sem dados: encerre partidas para popular o ranking.</p> : null}
        </section>
      ) : null}
    </AppShell>
  );
}

function ScorerList({ title, scorers }: { title: string; scorers: Scorer[] }) {
  return (
    <section aria-label={title} className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
      <h2 className="text-headline-md">{title}</h2>
      <p className="mt-1 text-body-md">{leaderText(scorers)}</p>
      <ol className="mt-sm space-y-1">
        {scorers.map((s) => (
          <li key={s.playerProfileId} className="flex justify-between text-body-md">
            <span>
              <span className="mr-2 inline-block w-6 text-label-md text-on-surface-variant">{s.position}º</span>
              {playerDisplayName(s.displayName)}
              {s.isLeader ? " 🥇" : ""}
            </span>
            <span className="font-semibold">{s.goals}</span>
          </li>
        ))}
      </ol>
      {scorers.length === 0 ? <p className="text-body-md text-on-surface-variant">Nenhum gol registrado.</p> : null}
    </section>
  );
}
