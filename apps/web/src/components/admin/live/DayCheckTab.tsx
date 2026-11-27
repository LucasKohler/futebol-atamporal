"use client";

import { useState } from "react";
import { AdminConfirmButton } from "@/components/admin/AdminDrawer";
import { RowMenu } from "@/components/admin/RowMenu";
import { StatusBadge } from "@/components/admin/AdminCard";
import { inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import { colorResolver, DayGamesList, DayStandings } from "@/components/live/DayBoard";
import { api, type LiveDay, type RoundResult } from "@/lib/api";
import { buildCorrectionSheet, defaultMatchup, draftFromGame, finishedGames, savesToInput, scoreFromGoals, teamLookup, penaltyWinnerFor, type GameDraft } from "@/lib/live";
import { GameEditor } from "./GameEditor";
import type { Mutate } from "./LiveTab";

/**
 * Aba 2 · Checagem do dia inteiro: classificação das cores, lista das partidas e edição manual.
 * Dia em andamento: cada edição grava na hora (PUT/DELETE /live). Dia concluído: as edições viram uma correção
 * auditada (nova revisão com motivo), enviada de uma vez.
 */
export function DayCheckTab({
  day,
  round,
  busy,
  mutate,
  onCorrect,
}: {
  day: LiveDay;
  round: RoundResult | null;
  busy: boolean;
  mutate: Mutate;
  onCorrect: (sheet: ReturnType<typeof buildCorrectionSheet>) => Promise<boolean>;
}) {
  const colorOf = colorResolver(day);
  const completed = day.matchStatus === "Completed";
  const [editing, setEditing] = useState<string | null>(null);
  const [insertAt, setInsertAt] = useState<number>(finishedGames(day).length + 1);
  const [edits, setEdits] = useState<Record<string, GameDraft | "deleted">>({});
  const [reason, setReason] = useState("");
  const teamOf = teamLookup(day);
  const pending = Object.keys(edits).length;

  async function saveLive(gameId: string, d: GameDraft) {
    const { home, away } = scoreFromGoals(d.goals, d.homeTeamId, d.awayTeamId, teamOf);
    const ok = await mutate(
      (v) =>
        api.updateLiveGame(day.matchId, gameId, {
          homeTeamId: d.homeTeamId,
          awayTeamId: d.awayTeamId,
          status: "Finished",
          goals: d.goals,
          penaltyWinnerTeamId: penaltyWinnerFor(home, away, d.penaltyWinnerTeamId),
          penaltySaves: savesToInput(d.saves),
          expectedVersion: v,
        }),
      "Partida corrigida.",
    );
    if (ok) setEditing(null);
  }

  async function insertLive(d: GameDraft) {
    const { home, away } = scoreFromGoals(d.goals, d.homeTeamId, d.awayTeamId, teamOf);
    const ok = await mutate(
      (v) =>
        api.startLiveGame(day.matchId, {
          homeTeamId: d.homeTeamId,
          awayTeamId: d.awayTeamId,
          insertAt,
          goals: d.goals,
          penaltyWinnerTeamId: penaltyWinnerFor(home, away, d.penaltyWinnerTeamId),
          penaltySaves: savesToInput(d.saves),
          expectedVersion: v,
        }),
      `Partida inserida na posição #${insertAt}.`,
    );
    if (ok) setEditing(null);
  }

  const editingGame = day.games.find((g) => g.id === editing);
  const blankDraft = (): GameDraft => ({ ...defaultMatchup(day), goals: [], penaltyWinnerTeamId: null, saves: {} });

  return (
    <section aria-labelledby="checagem-titulo" className="flex flex-col gap-md">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="checagem-titulo" className="text-headline-md">
          Dia {new Date(`${day.localDate}T12:00:00`).toLocaleDateString("pt-BR")}
        </h2>
        <StatusBadge tone={completed ? "success" : "warning"}>{completed ? "Dia concluído" : "Dia em andamento"}</StatusBadge>
      </div>

      <DayStandings day={day} />

      {completed ? (
        <p className="rounded-lg border border-amber-700 bg-amber-50 p-3 text-body-md text-amber-900">
          Dia concluído: as edições abaixo ficam pendentes e viram uma nova revisão quando você salvar a correção com motivo. A revisão anterior fica guardada.
        </p>
      ) : null}

      {editing === "new" ? (
        <div className="flex flex-col gap-sm">
          <label className="flex flex-col gap-1 text-label-md sm:max-w-xs">
            Posição da partida esquecida
            <select className={inputClass} value={insertAt} onChange={(e) => setInsertAt(Number(e.target.value))}>
              {Array.from({ length: finishedGames(day).length + 1 }, (_, i) => i + 1).map((n) => (
                <option key={n} value={n}>
                  #{n}
                </option>
              ))}
            </select>
          </label>
          <GameEditor day={day} initial={blankDraft()} title="Nova partida (já jogada)" saveLabel="Inserir partida" busy={busy} onSave={insertLive} onCancel={() => setEditing(null)} />
        </div>
      ) : editingGame ? (
        <GameEditor
          key={editingGame.id}
          day={day}
          initial={edits[editingGame.id] && edits[editingGame.id] !== "deleted" ? (edits[editingGame.id] as GameDraft) : draftFromGame(editingGame)}
          title={`Editar Partida #${editingGame.order}`}
          saveLabel={completed ? "Guardar na correção" : "Salvar partida"}
          busy={busy}
          onSave={(d) => {
            if (completed) {
              setEdits((e) => ({ ...e, [editingGame.id]: d }));
              setEditing(null);
            } else saveLive(editingGame.id, d);
          }}
          onCancel={() => setEditing(null)}
        />
      ) : null}

      <DayGamesList
        day={day}
        action={(g) =>
          g.status !== "Finished" ? (
            <StatusBadge tone="warning">Em andamento</StatusBadge>
          ) : edits[g.id] === "deleted" ? (
            <>
              <StatusBadge tone="danger">Será removida</StatusBadge>
              <button type="button" className={secondaryButton} onClick={() =>
                  setEdits((e) => {
                    const next = { ...e };
                    delete next[g.id];
                    return next;
                  })
                }>
                Desfazer
              </button>
            </>
          ) : (
            <>
              {edits[g.id] ? <StatusBadge tone="info">Alterada</StatusBadge> : null}
              <button type="button" disabled={busy} className={secondaryButton} onClick={() => setEditing(g.id)} aria-label={`Editar partida ${g.order}: ${colorOf(g.homeTeamId)} x ${colorOf(g.awayTeamId)}`}>
                Editar
              </button>
              {/* Ação destrutiva fora da vista (DS ListRow): menu "⋯" com confirmação. */}
              <RowMenu label={`Mais ações da partida ${g.order}`} disabled={busy}>
                <AdminConfirmButton
                  tone="danger"
                  label="Excluir partida"
                  question={`Excluir a partida #${g.order}?`}
                  confirmLabel="Excluir"
                  disabled={busy}
                  onConfirm={() => {
                    if (completed) setEdits((e) => ({ ...e, [g.id]: "deleted" }));
                    else mutate((v) => api.deleteLiveGame(day.matchId, g.id, v), `Partida #${g.order} excluída; as seguintes foram renumeradas.`);
                  }}
                />
              </RowMenu>
            </>
          )
        }
      />

      {!completed && editing === null ? (
        <button type="button" className={`${secondaryButton} self-start`} disabled={busy} onClick={() => { setInsertAt(finishedGames(day).length + 1); setEditing("new"); }}>
          Adicionar partida esquecida
        </button>
      ) : null}

      {completed ? (
        <form
          className="flex flex-col gap-sm rounded-xl border border-outline-variant bg-surface-container-low p-md"
          onSubmit={async (e) => {
            e.preventDefault();
            const ok = await onCorrect(buildCorrectionSheet(day, edits, round, reason));
            if (ok) {
              setEdits({});
              setReason("");
            }
          }}
        >
          <p className="text-body-md">
            {pending === 0 ? "Nenhuma alteração pendente." : `${pending} partida(s) com alteração pendente.`} Revisão vigente: {round?.revision ?? "—"}.
          </p>
          <label className="flex flex-col gap-1 text-label-md">
            Motivo da correção (obrigatório)
            <input
              className={inputClass}
              required
              minLength={5}
              maxLength={500}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Ex.: gol da partida #3 lançado para o autor errado"
            />
          </label>
          <button type="submit" disabled={busy || pending === 0 || reason.trim().length < 5} className={`${primaryButton} self-start`}>
            Salvar correção (nova revisão)
          </button>
        </form>
      ) : null}
    </section>
  );
}
