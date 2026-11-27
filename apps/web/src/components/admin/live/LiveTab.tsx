"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AdminEmpty } from "@/components/admin/AdminCard";
import { focusRing, gameButton, inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import { colorResolver, DayStandings } from "@/components/live/DayBoard";
import { api, type LiveDay } from "@/lib/api";
import {
  applySaveDelta,
  clampSaves,
  currentGame,
  inProgressSavesBody,
  savesFromGame,
  defaultMatchup,
  finishIssue,
  goalkeepersForGame,
  lastGoalForSide,
  newGoalId,
  penaltyWinnerFor,
  savesToInput,
  waitingTeamId,
} from "@/lib/live";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { teamStyle } from "@/lib/teams";
import { SavesCounter } from "./SavesCounter";

/**
 * Grava uma alteração com a versão lida. `fn` recebe também a súmula atual (ou a recarregada, na repetição após
 * conflito), para calcular a mudança sobre o estado mais novo do servidor.
 */
export type Mutate = (fn: (version: number, day: LiveDay) => Promise<LiveDay>, success?: string, opts?: { retryOnConflict?: boolean }) => Promise<boolean>;

const LIVE_STATUSES = ["Drawn", "InProgress", "AwaitingClose"];

/** Aba 1 · Partida ao vivo (beira de campo): iniciar, gols com 1 toque, pênaltis, 🧤 e finalizar. */
export function LiveTab({ day, busy, mutate, onGoToCheck }: { day: LiveDay; busy: boolean; mutate: Mutate; onGoToCheck: () => void }) {
  const colorOf = colorResolver(day);
  const game = currentGame(day);
  const nextOrder = day.games.length + 1;
  const suggested = defaultMatchup(day);
  const [home, setHome] = useState(suggested.homeTeamId);
  const [away, setAway] = useState(suggested.awayTeamId);
  const [ownGoal, setOwnGoal] = useState(false);
  // Só o vencedor nos pênaltis fica no aparelho até finalizar (a API o aceita apenas no encerramento).
  // As defesas 🧤 vêm da súmula salva no servidor: cada toque grava na hora e sobrevive a recarga.
  const [penWinner, setPenWinner] = useState<string | null>(null);

  // Nova sugestão a cada partida encerrada ("quem ganha fica").
  useEffect(() => {
    setHome(suggested.homeTeamId);
    setAway(suggested.awayTeamId);
  }, [suggested.homeTeamId, suggested.awayTeamId, day.games.length]);

  // Estado local de pênaltis/🧤 vale só para o jogo em andamento.
  useEffect(() => {
    setPenWinner(null);
    setOwnGoal(false);
  }, [game?.id]);

  if (day.matchStatus === "Completed") {
    return (
      <AdminEmpty action={<button type="button" className={secondaryButton} onClick={onGoToCheck}>Abrir checagem do dia</button>}>
        Este dia já foi concluído. Correções ficam na Checagem do Dia, com motivo.
      </AdminEmpty>
    );
  }
  if (!LIVE_STATUSES.includes(day.matchStatus)) {
    return (
      <AdminEmpty action={<Link href="/admin/sorteio" className={primaryButton}>Ir para o sorteio</Link>}>
        Confirme o sorteio de Vermelho, Preto e Verde para começar as partidas do dia.
      </AdminEmpty>
    );
  }

  if (!game) {
    const waiting = home && away && home !== away ? waitingTeamId(day, home, away) : null;
    const lastWinner = [...day.games].sort((a, b) => b.order - a.order)[0]?.winnerTeamId;
    return (
      <section aria-labelledby="iniciar-titulo" className="flex flex-col gap-md">
        <h2 id="iniciar-titulo" className="text-headline-md">
          Partida #{nextOrder}
        </h2>
        {lastWinner ? (
          <p className="text-body-md text-on-surface-variant">
            Quem ganha fica: {colorOf(lastWinner)} venceu a anterior{waiting ? ` e enfrenta quem estava fora.` : "."}
          </p>
        ) : null}
        <div className="grid grid-cols-2 gap-sm">
          <label className="flex flex-col gap-1 text-label-md">
            Cor 1
            <select className={inputClass} value={home} onChange={(e) => setHome(e.target.value)}>
              {day.teams.map((t) => (
                <option key={t.id} value={t.id}>
                  {teamStyle(t).label}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1 text-label-md">
            Cor 2
            <select className={inputClass} value={away} onChange={(e) => setAway(e.target.value)}>
              {day.teams.map((t) => (
                <option key={t.id} value={t.id}>
                  {teamStyle(t).label}
                </option>
              ))}
            </select>
          </label>
        </div>
        {waiting ? <p className="text-body-md">Fica de fora: {colorOf(waiting)}</p> : null}
        <button
          type="button"
          disabled={busy || !home || !away || home === away}
          className={`${primaryButton} min-h-14 text-body-lg`}
          onClick={() =>
            mutate((v) => api.startLiveGame(day.matchId, { homeTeamId: home, awayTeamId: away, expectedVersion: v }), `Partida #${nextOrder} iniciada.`)
          }
        >
          Iniciar Partida #{nextOrder}
        </button>
        {home === away ? <p role="alert" className="text-body-md text-on-error-container">Escolha duas cores diferentes.</p> : null}
        {day.games.length > 0 ? <DayStandings day={day} caption="Até agora" /> : null}
      </section>
    );
  }

  const sides = [game.homeTeamId, game.awayTeamId];
  const score: Record<string, number> = { [game.homeTeamId]: game.homeScore, [game.awayTeamId]: game.awayScore };
  const tied = game.homeScore === game.awayScore;
  const issue = finishIssue(game.homeScore, game.awayScore, game, penWinner);
  const keepers = goalkeepersForGame(day, game);
  const other = (teamId: string) => (teamId === game.homeTeamId ? game.awayTeamId : game.homeTeamId);

  function addGoal(playerProfileId: string, name: string, playerTeam: string) {
    const credited = ownGoal ? other(playerTeam) : playerTeam;
    const goalId = newGoalId(); // mesmo id na repetição após conflito: sem gol em dobro
    mutate(
      (v) => api.addLiveGoal(day.matchId, game!.id, { goalId, playerProfileId, ownGoal, expectedVersion: v }),
      `${ownGoal ? "Gol contra de" : "Gol de"} ${name}: ${colorOf(credited)} marcou.`,
      { retryOnConflict: true },
    );
    setOwnGoal(false);
  }

  return (
    <section aria-labelledby="ao-vivo-titulo" className="flex flex-col gap-md">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="ao-vivo-titulo" className="text-headline-md">
          Partida #{game.order} · ao vivo
        </h2>
        <span className="text-label-md text-on-surface-variant">Fica de fora: {colorOf(waitingTeamId(day, game.homeTeamId, game.awayTeamId) ?? "")}</span>
      </div>

      <div className="grid grid-cols-2 gap-sm">
        {sides.map((teamId) => {
          const t = day.teams.find((x) => x.id === teamId)!;
          const style = teamStyle(t);
          const last = lastGoalForSide(game, teamId);
          return (
            <div key={teamId} className={`flex flex-col items-center gap-1 rounded-xl border-2 ${style.border} bg-surface-container-lowest p-sm`}>
              <p className="flex items-center gap-1 text-body-lg font-semibold">
                <span aria-hidden="true" className={`inline-block h-3 w-3 rounded-full ${style.swatch}`} />
                {style.label}
              </p>
              <p className="text-display tabular-nums" aria-live="polite" aria-label={`${style.label} ${score[teamId]} gols`}>
                {score[teamId]}
              </p>
              <p className="min-h-4 text-caption text-fg-secondary">
                {last ? `Último: ${playerDisplayName(last.displayName)}${last.ownGoal ? " (contra)" : ""}` : "Sem gols"}
              </p>
              {/* Desfazer explícito: remove o último gol da cor e a autoria juntos (placar + artilharia). */}
              <button
                type="button"
                disabled={busy || !last}
                className={`${gameButton} w-full border border-line-control bg-card text-body-md text-fg hover:bg-subtle`}
                aria-label={last ? `Desfazer o último gol do ${style.label}, de ${playerDisplayName(last.displayName)}` : `Sem gol do ${style.label} para desfazer`}
                onClick={() =>
                  last &&
                  mutate(
                    (v) => api.removeLiveGoal(day.matchId, game.id, last.id, v),
                    `Gol de ${playerDisplayName(last.displayName)} desfeito: placar e artilharia corrigidos.`,
                  )
                }
              >
                <span aria-hidden="true">↶</span> Desfazer gol
              </button>
            </div>
          );
        })}
      </div>

      <div className="flex flex-col gap-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="text-label-md">+ Gol com 1 toque em quem marcou</p>
          <button
            type="button"
            aria-pressed={ownGoal}
            onClick={() => setOwnGoal((v) => !v)}
            className={`min-h-touch-game rounded-full border px-4 text-body-md font-semibold ${ownGoal ? "border-danger bg-danger-bg text-danger-strong" : "border-line-control bg-card"} ${focusRing}`}
          >
            {ownGoal ? "Próximo toque: gol contra" : "Gol contra"}
          </button>
        </div>
        {sides.map((teamId) => {
          const players = day.players.filter((p) => p.teamId === teamId && p.present);
          return (
            <div key={teamId} role="group" aria-label={`Jogadores do ${colorOf(teamId)}`} className="grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
              <span className="col-span-2 w-full text-label-md text-fg-secondary">{colorOf(teamId)}</span>
              {players.map((p) => {
                const name = playerDisplayName(p.displayName);
                return (
                  <button
                    key={p.playerProfileId}
                    type="button"
                    disabled={busy}
                    onClick={() => addGoal(p.playerProfileId, name, teamId)}
                    aria-label={`${ownGoal ? "Gol contra de" : "Gol de"} ${name} (${colorOf(teamId)})`}
                    className={`${gameButton} min-w-0 border ${ownGoal ? "border-danger bg-card" : "border-line-control bg-subtle"} text-fg`}
                  >
                    {ownGoal ? "GC " : "+ "}
                    {name}
                  </button>
                );
              })}
              {players.length === 0 ? <span className="col-span-2 text-label-md text-fg-secondary">Ninguém presente.</span> : null}
            </div>
          );
        })}
      </div>

      {tied ? (
        <fieldset className="rounded-xl border-2 border-warning bg-warning-subtle p-md">
          <legend className="px-1 text-body-lg font-semibold text-warning-strong">Decisão nos pênaltis alternados</legend>
          <p className="text-body-md text-warning-strong">Empatou: quem ganhou nos pênaltis? (não conta no placar nem na artilharia)</p>
          <div className="mt-sm grid grid-cols-2 gap-2">
            {sides.map((id) => (
              <label
                key={id}
                className={`flex min-h-touch-game cursor-pointer items-center justify-center gap-2 rounded-full border-2 px-3 text-body-lg has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-pitch-green-container ${
                  penWinner === id ? "border-pitch-green-container bg-pitch-green-container text-on-primary" : "border-outline-variant bg-surface-container-lowest"
                }`}
              >
                <input type="radio" name={`pen-${game.id}`} className="sr-only" checked={penWinner === id} onChange={() => setPenWinner(id)} />
                {penWinner === id ? "✓ " : ""}
                {colorOf(id)}
              </label>
            ))}
          </div>
        </fieldset>
      ) : null}

      <SavesCounter
        keepers={keepers}
        saves={savesFromGame(game)}
        disabled={busy}
        onAdjust={(goalkeeperProfileId, delta) => {
          const keeper = keepers.find((k) => k.playerProfileId === goalkeeperProfileId);
          const name = keeper ? playerDisplayName(keeper.displayName) : "goleiro";
          mutate(
            (v, latest) => {
              // Recalcula sobre a súmula mais nova (inclusive após conflito com outro aparelho).
              const fresh = latest.games.find((g) => g.id === game.id);
              if (!fresh || fresh.status !== "InProgress") throw new Error(`A Partida #${game.order} não está mais em andamento.`);
              return api.updateLiveGame(day.matchId, game.id, inProgressSavesBody(fresh, applySaveDelta(fresh, goalkeeperProfileId, delta), v));
            },
            delta > 0 ? `🧤 Defesa de pênalti de ${name} salva.` : `🧤 Defesa de pênalti de ${name} removida.`,
            { retryOnConflict: true },
          );
        }}
        colorOf={colorOf}
        clamp={clampSaves}
      />

      {issue ? (
        <p role="status" className="text-body-md text-on-error-container">
          {issue}
        </p>
      ) : null}
      <button
        type="button"
        disabled={busy || !!issue}
        className={`${primaryButton} min-h-14 text-body-lg`}
        onClick={() =>
          mutate(
            (v) =>
              api.finishLiveGame(day.matchId, game.id, {
                penaltyWinnerTeamId: penaltyWinnerFor(game.homeScore, game.awayScore, penWinner),
                // Defesas já gravadas ao vivo: reenvia o valor salvo para o fechamento ficar igual ao servidor.
                penaltySaves: savesToInput(savesFromGame(game)),
                expectedVersion: v,
              }),
            `Partida #${game.order} encerrada. Prepare a #${game.order + 1}.`,
          )
        }
      >
        Finalizar Partida #{game.order} e preparar #{game.order + 1}
      </button>
    </section>
  );
}
