"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, Draw, DrawTeam, MatchDetail } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { formatScore, isGoalkeeper, nextSwapSelection, orderedMembers, teamStyle } from "@/lib/teams";
import { matchStatusLabel, useSession } from "@/lib/useSession";

const focusRing =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-pitch-green-container";
const primaryButton = `min-h-11 rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary disabled:opacity-50 ${focusRing}`;
const secondaryButton = `min-h-11 rounded-full border border-outline px-4 py-3 text-label-md disabled:opacity-50 ${focusRing}`;

type Selected = { playerProfileId: string; teamId: string; role: string } | null;

export default function SorteioPage() {
  const { group, loading, isAdmin } = useSession();
  const [match, setMatch] = useState<MatchDetail | null>(null);
  const [draw, setDraw] = useState<Draw | null>(null);
  const [seed, setSeed] = useState("");
  const [selected, setSelected] = useState<Selected>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function reload(groupId: string) {
    const m = await api.upcomingMatch(groupId);
    setMatch(m);
    setDraw(m ? await api.getDraw(m.match.id) : null);
  }

  useEffect(() => {
    if (!group || !isAdmin) return;
    reload(group.id).catch((e) => setMsg(e instanceof Error ? e.message : "Erro"));
  }, [group, isAdmin]);

  async function run(action: () => Promise<Draw | null>, success: string) {
    setBusy(true);
    setMsg(null);
    setNotice(null);
    try {
      const result = await action();
      if (result) setDraw(result);
      if (match) setMatch(await api.getMatch(match.match.id));
      setNotice(success);
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Erro");
      if (group) await reload(group.id).catch(() => undefined);
    } finally {
      setSelected(null);
      setBusy(false);
    }
  }

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (!isAdmin) return <AppShell><p>Acesso admin.</p></AppShell>;

  const locked = draw?.isLocked ?? false;

  function pick(team: DrawTeam, playerProfileId: string, role: string) {
    if (!draw || !match || locked || busy) return;
    const next = nextSwapSelection(selected, { playerProfileId, teamId: team.id, role });
    setSelected(next.selected);
    if (next.swap) {
      const [a, b] = next.swap;
      run(() => api.swapPlayers(match.match.id, draw.id, a, b), "Troca feita. Revise as métricas e confirme de novo.");
    }
  }

  return (
    <AppShell>
      <h1 className="text-headline-lg">Sorteio de times</h1>
      <p className="mt-1 text-body-md text-on-surface-variant">
        Vermelho, Preto e Verde. Primeiro equilibra a linha; depois, os goleiros. Goleiro forte não compensa linha fraca.
      </p>
      <div aria-live="polite" role="status" className="sr-only">
        {notice ?? ""}
      </div>

      {!match ? (
        <p className="mt-md text-on-surface-variant">Nenhuma partida disponível.</p>
      ) : (
        <div className="mt-lg flex flex-col gap-lg">
          <p className="text-body-md text-on-surface-variant">
            {match.confirmed.length} confirmados · {matchStatusLabel(match.match.status)}
          </p>

          {locked ? (
            <p className="rounded-lg bg-surface-container-low p-3 text-body-md">
              Partida em andamento ou encerrada: o elenco sorteado está preservado e não pode mais ser alterado.
            </p>
          ) : (
            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-end">
              <label className="text-label-md">
                Semente (opcional, para repetir um sorteio)
                <input
                  className={`mt-1 block w-full rounded-lg border border-outline-variant px-3 py-2 sm:w-56 ${focusRing}`}
                  value={seed}
                  maxLength={80}
                  onChange={(e) => setSeed(e.target.value)}
                  placeholder="gerada automaticamente"
                />
              </label>
              <button
                type="button"
                disabled={busy || match.confirmed.length < 3}
                className={primaryButton}
                onClick={() =>
                  run(() => api.createDraw(match.match.id, seed.trim() || undefined), "Sorteio gerado. Revise antes de confirmar.")
                }
              >
                {draw ? "Refazer sorteio" : "Sortear times"}
              </button>
              {draw && draw.status !== "Confirmed" ? (
                <button
                  type="button"
                  disabled={busy || !draw.canConfirm}
                  className={secondaryButton}
                  onClick={() => run(() => api.confirmDraw(match.match.id, draw.id), "Escalação confirmada.")}
                >
                  Confirmar escalação
                </button>
              ) : null}
            </div>
          )}
          {match.confirmed.length < 3 ? (
            <p className="text-body-md text-on-surface-variant">São necessários ao menos 3 confirmados para sortear.</p>
          ) : null}

          {msg ? (
            <p role="alert" className="rounded-lg bg-error-container p-3 text-on-error-container">
              {msg}
            </p>
          ) : null}
          {notice ? <p className="text-label-md text-on-surface-variant">{notice}</p> : null}

          {draw ? <DrawReview draw={draw} selected={selected} locked={locked || busy} onPick={pick} /> : null}
        </div>
      )}
    </AppShell>
  );
}

function DrawReview({
  draw,
  selected,
  locked,
  onPick,
}: {
  draw: Draw;
  selected: Selected;
  locked: boolean;
  onPick: (team: DrawTeam, playerProfileId: string, role: string) => void;
}) {
  const teams = [...draw.teams].sort((a, b) => a.sortOrder - b.sortOrder);
  return (
    <section aria-labelledby="escalacao-titulo" className="flex flex-col gap-md">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="escalacao-titulo" className="text-headline-md">
          {draw.status === "Confirmed" ? "Escalação confirmada" : "Prévia do sorteio"}
        </h2>
        <p className="text-label-md text-on-surface-variant">
          Semente {draw.seed} · {draw.algorithmVersion ?? "sorteio antigo"}
        </p>
      </div>

      {draw.rosterChanged ? (
        <div role="alert" className="rounded-lg border border-error bg-error-container p-3 text-on-error-container">
          <p className="font-semibold">A lista mudou depois do sorteio. Refaça o sorteio antes de confirmar.</p>
          <ul className="mt-1 list-disc pl-5">
            {draw.rosterChanges.map((c) => (
              <li key={c}>{c}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {draw.blockers.length > 0 ? (
        <div role="alert" className="rounded-lg bg-error-container p-3 text-on-error-container">
          <p className="font-semibold">Não é possível confirmar:</p>
          <ul className="mt-1 list-disc pl-5">
            {draw.blockers.map((b) => (
              <li key={b}>{b}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {draw.warnings.length > 0 ? (
        <ul className="rounded-lg bg-surface-container-low p-3 text-body-md">
          {draw.warnings.map((w) => (
            <li key={w}>⚠️ {w}</li>
          ))}
        </ul>
      ) : null}

      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Metric label="Linha: diferença das médias" value={formatScore(draw.lineAverageSpread, 2)} />
        <Metric label="Linha: diferença das somas" value={formatScore(draw.lineSumSpread)} />
        <Metric label="Goleiros escalados" value={`${draw.goalkeepersAssigned} de ${draw.teams.length}`} />
        <Metric label="Goleiros: diferença das notas" value={formatScore(draw.goalkeeperSpread)} />
      </dl>

      {!locked ? (
        <p className="text-body-md text-on-surface-variant">
          Para trocar, toque em um jogador e depois em outro de outro time (ou no goleiro do mesmo time). Só quem é apto ao gol
          pode assumir o gol.
        </p>
      ) : null}

      <div className="grid gap-md md:grid-cols-3">
        {teams.map((team) => {
          const style = teamStyle(team);
          return (
            <section
              key={team.id}
              aria-label={`Time ${style.label}`}
              className={`rounded-xl border-2 ${style.border} bg-surface-container-lowest p-md`}
            >
              <h3 className="flex items-center gap-2 text-headline-md">
                <span aria-hidden="true" className={`inline-block h-4 w-4 rounded-full ${style.swatch}`} />
                {style.label}
              </h3>
              <p className="mt-1 text-label-md text-on-surface-variant">
                Linha: {team.lineCount} jogadores · soma {formatScore(team.lineSum)} · média {formatScore(team.lineAverage, 2)}
              </p>
              <p className="text-label-md text-on-surface-variant">
                Goleiro: {team.goalkeeperName ? `${playerDisplayName(team.goalkeeperName)} · nota ${formatScore(team.goalkeeperRating)}` : "sem goleiro (rodízio)"}
              </p>
              <ul className="mt-sm flex flex-col gap-1">
                {orderedMembers(team).map((m) => {
                  const isSelected = selected?.playerProfileId === m.playerProfileId;
                  const name = playerDisplayName(m.displayName);
                  return (
                    <li key={m.playerProfileId}>
                      <button
                        type="button"
                        disabled={locked}
                        aria-pressed={isSelected}
                        aria-label={`${name}, ${isGoalkeeper(m) ? "goleiro" : "linha"} do ${style.label}${isSelected ? ", selecionado para troca" : ""}`}
                        onClick={() => onPick(team, m.playerProfileId, m.role)}
                        className={`flex min-h-11 w-full items-center justify-between rounded-lg border px-3 py-2 text-left text-body-md ${
                          isSelected ? "border-pitch-green-container bg-secondary-container" : "border-outline-variant"
                        } disabled:cursor-default ${focusRing}`}
                      >
                        <span>
                          {isGoalkeeper(m) ? <strong className="mr-1 text-label-md">GOL</strong> : null}
                          {name}
                        </span>
                        <span className="text-label-md text-on-surface-variant">
                          {isGoalkeeper(m) ? `gol ${formatScore(m.goalkeeperRating)}` : `linha ${formatScore(m.skillRating)}`}
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            </section>
          );
        })}
      </div>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg bg-surface-container-low px-3 py-2">
      <dt className="text-label-sm text-on-surface-variant">{label}</dt>
      <dd className="text-headline-md">{value}</dd>
    </div>
  );
}
