"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { WhatsAppSummary } from "@/components/WhatsAppSummary";
import { AdminEmpty, AdminError, AdminLoading, AdminNotice, StatusBadge } from "@/components/admin/AdminCard";
import { AdminShell } from "@/components/admin/AdminShell";
import { RoundStepper } from "@/components/admin/RoundStepper";
import { focusRing, inputClass, primaryButton, secondaryButton } from "@/components/admin/ui";
import { api, Draw, DrawTeam, MatchDetail } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { deriveRoundStep, MIN_PLAYERS_TO_DRAW } from "@/lib/roundStep";
import {
  buildTeamsSummary,
  formatScore,
  isGoalkeeper,
  lineRange,
  nextSwapSelection,
  orderedMembers,
  ratingBarPercent,
  swapPreview,
  teamStyle,
  type SwapPreview,
} from "@/lib/teams";
import { matchStatusLabel, useSession } from "@/lib/useSession";

type Selected = { playerProfileId: string; teamId: string; role: string; name: string; color: string } | null;

export default function SorteioPage() {
  const { group, isAdmin } = useSession();
  const [match, setMatch] = useState<MatchDetail | null>(null);
  const [draw, setDraw] = useState<Draw | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [seed, setSeed] = useState("");
  const [selected, setSelected] = useState<Selected>(null);
  /** Troca proposta pelo segundo toque: só grava após "Confirmar troca". */
  const [pending, setPending] = useState<SwapPreview | null>(null);
  const confirmSwapRef = useRef<HTMLButtonElement>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async (groupId: string) => {
    const m = await api.upcomingMatch(groupId);
    setMatch(m);
    setDraw(m ? await api.getDraw(m.match.id) : null);
    setLoaded(true);
  }, []);

  useEffect(() => {
    if (!group || !isAdmin) return;
    reload(group.id).catch((e) => {
      setMsg(e instanceof Error ? e.message : "Erro");
      setLoaded(true);
    });
  }, [group, isAdmin, reload]);

  // Esc cancela a seleção ou a troca proposta.
  useEffect(() => {
    if (!selected && !pending) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setSelected(null);
        setPending(null);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [selected, pending]);

  // A prévia da troca recebe o foco para o teclado e o leitor de tela.
  useEffect(() => {
    if (pending) confirmSwapRef.current?.focus();
  }, [pending]);

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
      setPending(null);
      setBusy(false);
    }
  }

  const locked = draw?.isLocked ?? false;

  function pick(team: DrawTeam, playerProfileId: string, role: string, name: string) {
    if (!draw || !match || locked || busy || pending) return;
    const next = nextSwapSelection(selected, { playerProfileId, teamId: team.id, role });
    setSelected(next.selected ? { ...next.selected, name, color: teamStyle(team).label } : null);
    // O segundo toque só propõe a troca; nada é gravado nem invalidado sem confirmação explícita.
    if (next.swap) setPending(swapPreview(draw, next.swap[0], next.swap[1]));
  }

  function confirmSwap() {
    if (!draw || !match || !pending) return;
    const { playerA, playerB, invalidatesConfirmation } = pending;
    run(
      () => api.swapPlayers(match.match.id, draw.id, playerA, playerB),
      invalidatesConfirmation
        ? "Troca feita. A escalação voltou a prévia: revise o equilíbrio e confirme de novo."
        : "Troca feita. Revise o equilíbrio antes de confirmar.",
    );
  }

  const step = match
    ? deriveRoundStep({ status: match.match.status, confirmedCount: match.match.confirmedCount, draw })
    : deriveRoundStep({ status: null, confirmedCount: 0, draw: null });
  const teamsText = draw ? buildTeamsSummary(draw) : null;

  return (
    <AdminShell
      moduleId="sorteio"
      title="Sorteio 3 Cores"
      description="Vermelho, Preto e Verde. Primeiro equilibra a linha; depois, os goleiros. Goleiro forte não compensa linha fraca."
    >
      {!loaded ? (
        <AdminLoading label="Carregando sorteio…" />
      ) : (
        <>
          <RoundStepper
            state={step}
            busy={busy}
            hideCtaOn="/admin/sorteio"
            onTransition={(to) =>
              match &&
              run(async () => {
                await api.transitionMatch(match.match.id, to);
                return null;
              }, "Partida iniciada. O elenco está congelado.")
            }
          />
          {msg ? <AdminError>{msg}</AdminError> : null}
          {notice ? <AdminNotice>{notice}</AdminNotice> : null}

          {!match ? (
            <AdminEmpty
              action={
                <Link href="/partida" className={primaryButton}>
                  Criar partida
                </Link>
              }
            >
              Nenhuma partida disponível para sortear.
            </AdminEmpty>
          ) : (
            <div className="flex flex-col gap-lg">
              <section aria-labelledby="controle-titulo" className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 id="controle-titulo" className="text-headline-md">
                    {match.match.title || "Partida"}
                  </h2>
                  <StatusBadge tone="info">
                    {match.confirmed.length} confirmados · {matchStatusLabel(match.match.status)}
                  </StatusBadge>
                </div>
                {locked ? (
                  <p className="mt-sm text-body-md">Partida em andamento ou encerrada: o elenco sorteado está preservado e não pode mais ser alterado.</p>
                ) : (
                  <div className="mt-md flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-end">
                    <label className="flex flex-col gap-1 text-label-md">
                      Semente (opcional, para repetir um sorteio)
                      <input
                        className={`${inputClass} w-full sm:w-56`}
                        value={seed}
                        maxLength={80}
                        onChange={(e) => setSeed(e.target.value)}
                        placeholder="gerada automaticamente"
                      />
                    </label>
                    <button
                      type="button"
                      disabled={busy || match.confirmed.length < MIN_PLAYERS_TO_DRAW}
                      className={draw ? secondaryButton : primaryButton}
                      onClick={() => run(() => api.createDraw(match.match.id, seed.trim() || undefined), "Sorteio gerado. Revise antes de confirmar.")}
                    >
                      {draw ? "Refazer sorteio" : "Sortear times"}
                    </button>
                    {draw && draw.status !== "Confirmed" ? (
                      <button
                        type="button"
                        disabled={busy || !draw.canConfirm}
                        className={primaryButton}
                        onClick={() => run(() => api.confirmDraw(match.match.id, draw.id), "Escalação confirmada.")}
                      >
                        Confirmar escalação
                      </button>
                    ) : null}
                  </div>
                )}
                {match.confirmed.length < MIN_PLAYERS_TO_DRAW ? (
                  <p className="mt-sm text-body-md text-on-surface-variant">São necessários ao menos {MIN_PLAYERS_TO_DRAW} confirmados para sortear.</p>
                ) : null}
              </section>

              {!draw ? (
                <AdminEmpty>Nenhum sorteio ainda. Toque em “Sortear times” para gerar a prévia.</AdminEmpty>
              ) : (
                <DrawReview draw={draw} selected={selected} locked={locked || busy} onPick={pick} />
              )}

              {draw && teamsText ? (
                <WhatsAppSummary
                  detail={match}
                  teams={teamsText}
                  timeZone={group?.timeZoneId ?? "America/Sao_Paulo"}
                  title="Escalação para o WhatsApp"
                  description="Lista e times confirmados, só com nomes e quem vai no gol. Sem telefone, nota ou pagamento."
                />
              ) : draw ? (
                <p className="text-body-md text-on-surface-variant">O resumo com a escalação fica disponível depois de confirmar o sorteio.</p>
              ) : null}
            </div>
          )}

          {pending ? (
            <div
              role="dialog"
              aria-modal="false"
              aria-labelledby="troca-titulo"
              aria-describedby="troca-detalhe"
              className="fixed inset-x-0 top-16 z-40 mx-auto flex w-[calc(100%-2rem)] max-w-xl flex-col gap-2 rounded-xl border-2 border-pitch-green-container bg-surface-container-lowest p-3 shadow-lg"
            >
              <p id="troca-titulo" className="text-body-md font-semibold">
                Prévia da troca
              </p>
              <ul id="troca-detalhe" className="list-disc pl-5 text-body-md">
                {pending.moves.map((m) => (
                  <li key={m}>{m}.</li>
                ))}
              </ul>
              {pending.invalidatesConfirmation ? (
                <p role="alert" className="rounded-lg border border-amber-700 bg-amber-50 p-2 text-body-md text-amber-900">
                  A escalação está confirmada. Ao trocar, ela volta a prévia e precisa ser confirmada de novo antes de iniciar a partida.
                </p>
              ) : null}
              <div className="flex flex-wrap gap-2">
                <button ref={confirmSwapRef} type="button" disabled={busy} onClick={confirmSwap} className={primaryButton}>
                  Confirmar troca
                </button>
                <button type="button" onClick={() => setPending(null)} className={secondaryButton}>
                  Cancelar
                </button>
              </div>
            </div>
          ) : selected ? (
            <div
              role="status"
              aria-live="polite"
              className="fixed inset-x-0 top-16 z-40 mx-auto flex w-[calc(100%-2rem)] max-w-xl flex-wrap items-center justify-between gap-2 rounded-xl border-2 border-pitch-green-container bg-surface-container-lowest p-3 shadow-lg"
            >
              <p className="text-body-md">
                Trocando <strong>{playerDisplayName(selected.name)}</strong> ({selected.color}). Toque no jogador de destino para ver a prévia da troca.
              </p>
              <button type="button" onClick={() => setSelected(null)} className={secondaryButton}>
                Cancelar
              </button>
            </div>
          ) : null}
        </>
      )}
    </AdminShell>
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
  onPick: (team: DrawTeam, playerProfileId: string, role: string, name: string) => void;
}) {
  const teams = [...draw.teams].sort((a, b) => a.sortOrder - b.sortOrder);
  return (
    <section aria-labelledby="escalacao-titulo" className="flex flex-col gap-md">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="escalacao-titulo" className="flex items-center gap-2 text-headline-md">
          {draw.status === "Confirmed" ? "Escalação confirmada" : "Prévia do sorteio"}
          <StatusBadge tone={draw.status === "Confirmed" && !draw.rosterChanged ? "success" : "warning"}>
            {draw.status === "Confirmed" && !draw.rosterChanged ? "Confirmado" : "Não confirmado"}
          </StatusBadge>
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
        <ul className="rounded-lg border border-amber-700 bg-amber-50 p-3 text-body-md text-amber-900">
          {draw.warnings.map((w) => (
            <li key={w}>Atenção: {w}</li>
          ))}
        </ul>
      ) : null}

      <BalanceComparator draw={draw} teams={teams} />

      {!locked ? (
        <p className="text-body-md text-on-surface-variant">
          Troca: toque em um jogador e depois em outro de outro time (ou no goleiro do mesmo time); confira a prévia e toque em
          “Confirmar troca”. Só quem é apto ao gol pode assumir o gol. Esc ou “Cancelar” desfaz.
        </p>
      ) : null}

      <div className="grid gap-md md:grid-cols-3">
        {teams.map((team) => {
          const style = teamStyle(team);
          return (
            <section key={team.id} aria-label={`Time ${style.label}`} className={`rounded-xl border-2 ${style.border} bg-surface-container-lowest p-md`}>
              <h3 className="flex items-center gap-2 text-headline-md">
                <span aria-hidden="true" className={`inline-block h-4 w-4 rounded-full ${style.swatch}`} />
                {style.label}
              </h3>
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
                        onClick={() => onPick(team, m.playerProfileId, m.role, m.displayName)}
                        className={`flex min-h-11 w-full items-center justify-between rounded-lg border px-3 py-2 text-left text-body-md ${
                          isSelected ? "border-2 border-pitch-green-container bg-secondary-container font-semibold" : "border-outline-variant"
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

/** Comparador de equilíbrio: linha (qtd, soma, média, amplitude) separada do gol, barras em escala fixa 0–10. */
function BalanceComparator({ draw, teams }: { draw: Draw; teams: DrawTeam[] }) {
  return (
    <section aria-labelledby="equilibrio-titulo" className="flex flex-col gap-sm">
      <h3 id="equilibrio-titulo" className="text-headline-md">
        Equilíbrio
      </h3>
      <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Metric label="Linha: diferença das médias" value={formatScore(draw.lineAverageSpread, 2)} />
        <Metric label="Linha: diferença das somas" value={formatScore(draw.lineSumSpread)} />
        <Metric label="Goleiros escalados" value={`${draw.goalkeepersAssigned} de ${draw.teams.length}`} />
        <Metric label="Goleiros: diferença das notas" value={formatScore(draw.goalkeeperSpread)} />
      </dl>
      <div className="grid gap-sm md:grid-cols-3">
        {teams.map((team) => {
          const style = teamStyle(team);
          const range = lineRange(team);
          return (
            <div key={team.id} className={`rounded-xl border-2 border-t-8 ${style.border} bg-surface-container-lowest p-md`}>
              <p className="flex items-center gap-2 text-body-lg font-semibold">
                <span aria-hidden="true" className={`inline-block h-3 w-3 rounded-full ${style.swatch}`} />
                {style.label}
              </p>
              <p className="mt-sm text-label-md uppercase text-on-surface-variant">Linha</p>
              <div className="mt-1 flex items-baseline justify-between gap-2">
                <span className="text-headline-md">{formatScore(team.lineAverage, 2)}</span>
                <span className="text-label-md text-on-surface-variant">média</span>
              </div>
              <div
                role="meter"
                aria-label={`Média de linha do ${style.label}`}
                aria-valuemin={0}
                aria-valuemax={10}
                aria-valuenow={team.lineAverage ?? 0}
                className="mt-1 h-2 w-full rounded-full bg-surface-container"
              >
                <div className={`h-2 rounded-full ${style.swatch}`} style={{ width: `${ratingBarPercent(team.lineAverage)}%` }} />
              </div>
              <p className="mt-1 text-label-md text-on-surface-variant">
                {team.lineCount} na linha · soma {formatScore(team.lineSum)}
                {range ? ` · de ${formatScore(range.min)} a ${formatScore(range.max)}` : ""}
              </p>
              <p className="mt-sm text-label-md uppercase text-on-surface-variant">Gol</p>
              <p className="text-body-md">
                {team.goalkeeperName ? (
                  <>
                    {playerDisplayName(team.goalkeeperName)} · nota {formatScore(team.goalkeeperRating)}
                  </>
                ) : (
                  "Sem goleiro (rodízio)"
                )}
              </p>
            </div>
          );
        })}
      </div>
      <p className="text-label-md text-on-surface-variant">Barras em escala fixa de 0 a 10. Diferenças pequenas são normais; notas são estimativas.</p>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg bg-surface-container-low px-3 py-2">
      <dt className="text-label-md text-on-surface-variant">{label}</dt>
      <dd className="text-headline-md">{value}</dd>
    </div>
  );
}
