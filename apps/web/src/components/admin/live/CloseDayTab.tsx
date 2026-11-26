"use client";

import { useState } from "react";
import { AdminConfirmButton } from "@/components/admin/AdminDrawer";
import { AdminEmpty, StatusBadge } from "@/components/admin/AdminCard";
import { inputClass, primaryButton } from "@/components/admin/ui";
import { colorResolver, DayStandings } from "@/components/live/DayBoard";
import { api, type LiveDay, type LivePlayer, type RoundResult } from "@/lib/api";
import { closeDayIssue } from "@/lib/live";
import { playerDisplayName } from "@/lib/playerDisplayName";
import type { Mutate } from "./LiveTab";

export type CloseDayBody = Parameters<typeof api.closeLiveDay>[1];

function statLine(p: LivePlayer) {
  const parts = [`${p.goals} gol${p.goals === 1 ? "" : "s"}`, `${p.gamesWon} vitória${p.gamesWon === 1 ? "" : "s"}${p.gamesWonOnPenalties ? ` (${p.gamesWonOnPenalties} nos pênaltis)` : ""}`];
  if (p.playedGoalkeeper) parts.push(`🧤 ${p.penaltySaves}`);
  if (p.playedGoalkeeper && p.soleGoalkeeperForTeam && p.teamGoalsConceded !== null) parts.push(`${p.teamGoalsConceded} sofridos`);
  return parts.join(" · ");
}

/** Aba 3 · Fim da pelada: resumo do dia, presença/goleiros, Melhor Jogador e Melhor Goleiro e conclusão do dia. */
export function CloseDayTab({
  day,
  round,
  busy,
  mutate,
  onClose,
}: {
  day: LiveDay;
  round: RoundResult | null;
  busy: boolean;
  mutate: Mutate;
  onClose: (body: CloseDayBody) => Promise<boolean>;
}) {
  const colorOf = colorResolver(day);
  const completed = day.matchStatus === "Completed";
  const [best, setBest] = useState("");
  const [bestGk, setBestGk] = useState("");
  const [notes, setNotes] = useState("");
  const issue = closeDayIssue(day);
  const ranked = [...day.players].sort((a, b) => Number(b.present) - Number(a.present) || b.gamesWon - a.gamesWon || b.goals - a.goals || b.penaltySaves - a.penaltySaves);
  const presentPlayers = day.players.filter((p) => p.present);
  const presentKeepers = presentPlayers.filter((p) => p.playedGoalkeeper);
  const bestOk = !best || presentPlayers.some((p) => p.playerProfileId === best);
  const bestGkOk = !bestGk || presentKeepers.some((p) => p.playerProfileId === bestGk);

  function presence(patch: (p: LivePlayer) => Partial<LivePlayer>) {
    return day.players.map((p) => {
      const n = { ...p, ...patch(p) };
      return { playerProfileId: n.playerProfileId, present: n.present, playedGoalkeeper: n.playedGoalkeeper };
    });
  }
  function setPresence(id: string, patch: Partial<Pick<LivePlayer, "present" | "playedGoalkeeper">>, msg: string) {
    mutate((v) => api.updateLivePresence(day.matchId, { expectedVersion: v, presence: presence((p) => (p.playerProfileId === id ? patch : {})) }), msg);
  }

  return (
    <section aria-labelledby="fim-titulo" className="flex flex-col gap-md">
      <h2 id="fim-titulo" className="text-headline-md">
        Fim da pelada & Melhores do dia
      </h2>
      <DayStandings day={day} />

      <div className="overflow-x-auto">
        <table className="w-full min-w-[20rem] text-left text-body-md [&_td]:px-2 [&_td]:text-center [&_td]:tabular-nums [&_thead_th]:px-2 [&_thead_th:not(:first-child)]:text-center">
          <caption className="mb-1 text-left text-label-md text-on-surface-variant">Desempenho do dia (base para escolher os melhores)</caption>
          <thead className="text-label-md text-on-surface-variant">
            <tr>
              <th scope="col" className="py-1">Jogador</th>
              <th scope="col">Gols</th>
              <th scope="col">V</th>
              <th scope="col">
                <abbr title="Pênaltis defendidos">🧤</abbr>
              </th>
              {!completed ? <th scope="col">Presença</th> : null}
            </tr>
          </thead>
          <tbody>
            {ranked.map((p) => {
              const name = playerDisplayName(p.displayName);
              return (
                <tr key={p.playerProfileId} className={`border-t border-outline-variant ${p.present ? "" : "text-on-surface-variant line-through"}`}>
                  <th scope="row" className="py-2 font-normal">
                    {name} <span className="text-label-md text-on-surface-variant">({colorOf(p.teamId)}{p.playedGoalkeeper ? " · gol" : ""})</span>
                  </th>
                  <td>{p.goals}</td>
                  <td>
                    {p.gamesWon}
                    {p.gamesWonOnPenalties ? <span className="text-label-md text-on-surface-variant"> ({p.gamesWonOnPenalties} pên.)</span> : null}
                  </td>
                  <td>{p.playedGoalkeeper ? p.penaltySaves : "—"}</td>
                  {!completed ? (
                    <td className="whitespace-nowrap">
                      <label className="mr-2 inline-flex min-h-11 items-center gap-1 text-label-md">
                        <input
                          type="checkbox"
                          className="h-5 w-5"
                          checked={p.present}
                          disabled={busy}
                          onChange={(e) => setPresence(p.playerProfileId, { present: e.target.checked }, `${name}: ${e.target.checked ? "presente" : "ausente"}.`)}
                          aria-label={`${name} presente`}
                        />
                        veio
                      </label>
                      <label className="inline-flex min-h-11 items-center gap-1 text-label-md">
                        <input
                          type="checkbox"
                          className="h-5 w-5"
                          checked={p.playedGoalkeeper}
                          disabled={busy || !p.present}
                          onChange={(e) => setPresence(p.playerProfileId, { playedGoalkeeper: e.target.checked }, `${name}: ${e.target.checked ? "atuou no gol" : "não atuou no gol"}.`)}
                          aria-label={`${name} atuou no gol`}
                        />
                        gol
                      </label>
                    </td>
                  ) : null}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {completed ? (
        <div className="flex flex-col gap-1 rounded-xl border border-green-700 bg-green-50 p-md text-green-900">
          <p className="text-body-lg font-semibold">Pelada do dia concluída.</p>
          <p>Melhor Jogador da Pelada: {round?.bestPlayerName ? playerDisplayName(round.bestPlayerName) : "sem prêmio"}</p>
          <p>Melhor Goleiro da Pelada: {round?.bestGoalkeeperName ? playerDisplayName(round.bestGoalkeeperName) : "sem prêmio"}</p>
        </div>
      ) : (
        <form
          className="flex flex-col gap-md"
          onSubmit={(e) => {
            e.preventDefault();
          }}
        >
          <AwardChips legend="Melhor Jogador da Pelada (entre os presentes)" name="best-player" candidates={presentPlayers} value={best} onChange={setBest} colorOf={colorOf} />
          <AwardChips
            legend="Melhor Goleiro da Pelada (entre quem atuou no gol)"
            name="best-gk"
            candidates={presentKeepers}
            value={bestGk}
            onChange={setBestGk}
            colorOf={colorOf}
            empty="Ninguém marcado como goleiro: ajuste a coluna “gol” acima."
          />
          <label className="flex flex-col gap-1 text-label-md">
            Observações (opcional, só admins veem)
            <textarea className={inputClass} rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
          {issue ? (
            <p role="status" className="text-body-md text-on-error-container">
              {issue}
            </p>
          ) : null}
          <div>
            <AdminConfirmButton
              tone="neutral"
              className={primaryButton}
              label="Concluir a pelada do dia"
              question="Concluir o dia? Depois, mudanças só por correção com motivo."
              confirmLabel="Concluir dia"
              disabled={busy || !!issue || !bestOk || !bestGkOk}
              onConfirm={() =>
                onClose({
                  expectedLiveVersion: day.version,
                  presence: day.players.map((p) => ({ playerProfileId: p.playerProfileId, present: p.present, playedGoalkeeper: p.playedGoalkeeper })),
                  bestPlayerProfileId: best || null,
                  bestGoalkeeperProfileId: bestGk || null,
                  notes: notes.trim() || null,
                })
              }
            />
          </div>
        </form>
      )}
    </section>
  );
}

function AwardChips({
  legend,
  name,
  candidates,
  value,
  onChange,
  colorOf,
  empty = "Ninguém presente.",
}: {
  legend: string;
  name: string;
  candidates: LivePlayer[];
  value: string;
  onChange: (v: string) => void;
  colorOf: (id: string) => string;
  empty?: string;
}) {
  const options = [{ id: "", label: "Sem prêmio", sub: "" }, ...candidates.map((p) => ({ id: p.playerProfileId, label: playerDisplayName(p.displayName), sub: `${colorOf(p.teamId)} · ${statLine(p)}` }))];
  return (
    <fieldset>
      <legend className="mb-1 text-label-md">{legend}</legend>
      {candidates.length === 0 ? <AdminEmpty>{empty}</AdminEmpty> : null}
      <div className="flex flex-col gap-1 sm:flex-row sm:flex-wrap">
        {options.map((o) => {
          const checked = value === o.id;
          return (
            <label
              key={o.id || "none"}
              className={`flex min-h-11 cursor-pointer flex-col justify-center rounded-xl border px-3 py-1 has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-pitch-green-container ${
                checked ? "border-pitch-green-container bg-pitch-green-container text-on-primary" : "border-outline-variant bg-surface-container-lowest"
              }`}
            >
              <input type="radio" className="sr-only" name={name} checked={checked} onChange={() => onChange(o.id)} />
              <span className="text-body-md font-semibold">
                {checked && o.id ? "★ " : ""}
                {o.label}
              </span>
              {o.sub ? <span className={`text-label-md ${checked ? "" : "text-on-surface-variant"}`}>{o.sub}</span> : null}
            </label>
          );
        })}
      </div>
      {value ? <StatusBadge tone="success">Escolhido</StatusBadge> : null}
    </fieldset>
  );
}
