"use client";

import { useEffect, useRef, useState } from "react";
import { focusRing, inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import type { LiveDay } from "@/lib/api";
import { clampSaves, draftIssues, goalkeepersForGame, newGoalId, scoreFromGoals, teamLookup, type GameDraft } from "@/lib/live";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { teamStyle } from "@/lib/teams";
import { SavesCounter } from "./SavesCounter";

/** Edição completa de uma partida encerrada: cores, gols (autor/gol contra), vencedor nos pênaltis e 🧤. */
export function GameEditor({
  day,
  initial,
  title,
  saveLabel,
  busy,
  onSave,
  onCancel,
}: {
  day: LiveDay;
  initial: GameDraft;
  title: string;
  saveLabel: string;
  busy: boolean;
  onSave: (draft: GameDraft) => void;
  onCancel: () => void;
}) {
  const [draft, setDraft] = useState<GameDraft>(initial);
  const rootRef = useRef<HTMLFieldSetElement>(null);
  useEffect(() => rootRef.current?.focus(), []);

  const teamOf = teamLookup(day);
  const colorOf = (id: string) => {
    const t = day.teams.find((x) => x.id === id);
    return t ? teamStyle(t).label : "?";
  };
  const { home, away } = scoreFromGoals(draft.goals, draft.homeTeamId, draft.awayTeamId, teamOf);
  const tied = home === away;
  const issues = draftIssues(draft, day);
  const authors = day.players.filter((p) => p.teamId === draft.homeTeamId || p.teamId === draft.awayTeamId);
  const keepers = goalkeepersForGame(day, draft);
  const set = (patch: Partial<GameDraft>) => setDraft((d) => ({ ...d, ...patch }));

  return (
    <fieldset ref={rootRef} tabIndex={-1} className={`flex flex-col gap-md rounded-xl border-2 border-pitch-green-container bg-surface-container-lowest p-md ${focusRing}`}>
      <legend className="px-1 text-body-lg font-semibold">{title}</legend>

      <div className="grid grid-cols-2 gap-sm">
        {(["homeTeamId", "awayTeamId"] as const).map((side) => (
          <label key={side} className="flex flex-col gap-1 text-label-md">
            {side === "homeTeamId" ? "Cor 1" : "Cor 2"}
            <select className={inputClass} value={draft[side]} onChange={(e) => set({ [side]: e.target.value } as Partial<GameDraft>)}>
              {day.teams.map((t) => (
                <option key={t.id} value={t.id}>
                  {teamStyle(t).label}
                </option>
              ))}
            </select>
          </label>
        ))}
      </div>

      <p className="text-headline-md" aria-live="polite">
        Placar: {colorOf(draft.homeTeamId)} {home} x {away} {colorOf(draft.awayTeamId)}
      </p>
      <p className="-mt-sm text-label-md text-on-surface-variant">O placar sai dos gols lançados abaixo.</p>

      <div className="flex flex-col gap-1">
        <p className="text-label-md">Gols ({draft.goals.length})</p>
        {draft.goals.map((g, i) => (
          <div key={g.goalId} className="flex flex-wrap items-center gap-2">
            <label className="text-label-md">
              <span className="sr-only">Autor do gol {i + 1}</span>
              <select
                className={inputClass}
                value={g.playerProfileId}
                onChange={(e) => set({ goals: draft.goals.map((x, j) => (j === i ? { ...x, playerProfileId: e.target.value } : x)) })}
              >
                <option value="">Autor…</option>
                {authors.map((p) => (
                  <option key={p.playerProfileId} value={p.playerProfileId}>
                    {playerDisplayName(p.displayName)} ({colorOf(p.teamId)})
                  </option>
                ))}
              </select>
            </label>
            <label className="flex min-h-11 items-center gap-1 text-label-md">
              <input
                type="checkbox"
                className="h-5 w-5"
                checked={g.ownGoal}
                onChange={(e) => set({ goals: draft.goals.map((x, j) => (j === i ? { ...x, ownGoal: e.target.checked } : x)) })}
              />
              gol contra
            </label>
            <button
              type="button"
              className={`min-h-11 px-2 text-label-md text-error underline ${focusRing}`}
              aria-label={`Remover gol ${i + 1}`}
              onClick={() => set({ goals: draft.goals.filter((_, j) => j !== i) })}
            >
              Remover
            </button>
          </div>
        ))}
        <button
          type="button"
          className={`${secondaryButton} self-start`}
          onClick={() => set({ goals: [...draft.goals, { goalId: newGoalId(), playerProfileId: "", ownGoal: false }] })}
        >
          Adicionar gol
        </button>
      </div>

      {tied ? (
        <fieldset className="rounded-lg border border-amber-700 bg-amber-50 p-sm">
          <legend className="px-1 text-label-md font-semibold">Empate: quem ganhou nos pênaltis alternados?</legend>
          <div className="flex flex-wrap gap-2">
            {[draft.homeTeamId, draft.awayTeamId].map((id) => (
              <label key={id} className="flex min-h-11 items-center gap-2 rounded-full border border-outline-variant bg-surface-container-lowest px-3 text-body-md">
                <input type="radio" name="pen-editor" checked={draft.penaltyWinnerTeamId === id} onChange={() => set({ penaltyWinnerTeamId: id })} />
                {colorOf(id)}
              </label>
            ))}
          </div>
        </fieldset>
      ) : null}

      <SavesCounter keepers={keepers} saves={draft.saves} onChange={(update) => setDraft((d) => ({ ...d, saves: update(d.saves) }))} colorOf={colorOf} clamp={clampSaves} />

      {issues.length > 0 ? (
        <ul role="alert" className="rounded-lg bg-error-container p-2 text-label-md text-on-error-container">
          {issues.map((m) => (
            <li key={m}>! {m}</li>
          ))}
        </ul>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <button type="button" disabled={busy || issues.length > 0} className={primaryButton} onClick={() => onSave(draft)}>
          {saveLabel}
        </button>
        <button type="button" className={secondaryButton} onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </fieldset>
  );
}
