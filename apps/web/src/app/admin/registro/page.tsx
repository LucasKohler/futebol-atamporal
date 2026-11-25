"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, Draw, MatchSummary, RoundResult } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import {
  awardCandidates,
  buildPayload,
  emptySheet,
  gameIssues,
  gameLabel,
  previewStandings,
  rosterFromDraw,
  sheetFromRound,
  type GameForm,
  type RosterPlayer,
  type SheetForm,
} from "@/lib/round";
import { teamStyle } from "@/lib/teams";
import { matchStatusLabel, useSession } from "@/lib/useSession";

const focusRing =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-pitch-green-container";
const input = `rounded-lg border border-outline-variant px-2 py-2 text-body-md ${focusRing}`;
const primaryButton = `min-h-11 rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary disabled:opacity-50 ${focusRing}`;
const secondaryButton = `min-h-11 rounded-full border border-outline px-4 py-2 text-label-md disabled:opacity-50 ${focusRing}`;
const CLOSABLE = ["RegistrationClosed", "Drawn", "InProgress", "AwaitingClose"];

export default function RegistroPage() {
  const { group, loading, isAdmin } = useSession();
  const [matches, setMatches] = useState<MatchSummary[]>([]);
  const [matchId, setMatchId] = useState("");
  const [draw, setDraw] = useState<Draw | null>(null);
  const [round, setRound] = useState<RoundResult | null>(null);
  const [form, setForm] = useState<SheetForm | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!group || !isAdmin) return;
    api
      .listMatches(group.id)
      .then((list) => {
        const relevant = list.filter((m) => CLOSABLE.includes(m.status) || m.status === "Completed");
        setMatches(relevant);
        const open = relevant.find((m) => CLOSABLE.includes(m.status));
        setMatchId((current) => current || open?.id || relevant[0]?.id || "");
      })
      .catch((e) => setMsg(e instanceof Error ? e.message : "Erro"));
  }, [group, isAdmin]);

  const load = useCallback(async (id: string) => {
    setMsg(null);
    const [d, r] = await Promise.all([api.getDraw(id), api.getRound(id)]);
    setDraw(d);
    setRound(r);
    if (r && !r.isLegacy) setForm(sheetFromRound(r));
    else if (d && d.status === "Confirmed" && !d.isLegacy) setForm(emptySheet(rosterFromDraw(d)));
    else setForm(null);
  }, []);

  useEffect(() => {
    if (!matchId) return;
    load(matchId).catch((e) => setMsg(e instanceof Error ? e.message : "Erro"));
  }, [matchId, load]);

  const roster = useMemo(() => (draw ? rosterFromDraw(draw) : []), [draw]);
  const teams = useMemo(() => (draw ? [...draw.teams].sort((a, b) => a.sortOrder - b.sortOrder) : []), [draw]);
  const colorOf = useCallback((teamId: string) => {
    const t = teams.find((x) => x.id === teamId);
    return t ? teamStyle(t).label : "?";
  }, [teams]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (!isAdmin) return <AppShell><p>Acesso admin.</p></AppShell>;

  const selected = matches.find((m) => m.id === matchId);
  const correcting = !!round && !round.isLegacy;

  async function submit() {
    if (!form || !selected) return;
    setBusy(true);
    setMsg(null);
    setNotice(null);
    try {
      const payload = buildPayload(form, roster);
      const saved = correcting ? await api.correctRound(selected.id, payload) : await api.closeRound(selected.id, payload);
      setNotice(correcting ? `Correção salva (revisão ${saved.revision}).` : "Rodada encerrada e registrada.");
      await load(selected.id);
      if (group) setMatches((list) => list.map((m) => (m.id === selected.id ? { ...m, status: "Completed" } : m)));
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Erro");
    } finally {
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <h1 className="text-headline-lg">Registro pós-jogo</h1>
      <p className="mt-1 text-body-md text-on-surface-variant">
        Confrontos entre as cores, gols (gol contra fica fora da artilharia), presença e prêmios da rodada.
      </p>
      <div aria-live="polite" role="status" className="sr-only">
        {notice ?? ""}
      </div>

      <label className="mt-lg block text-label-md">
        Partida
        <select className={`mt-1 block w-full max-w-md ${input}`} value={matchId} onChange={(e) => setMatchId(e.target.value)}>
          {matches.length === 0 ? <option value="">Nenhuma partida para registrar</option> : null}
          {matches.map((m) => (
            <option key={m.id} value={m.id}>
              {new Date(m.startsAt).toLocaleDateString("pt-BR")} · {m.title || "Partida"} · {matchStatusLabel(m.status)}
            </option>
          ))}
        </select>
      </label>

      {msg ? (
        <p role="alert" className="mt-md rounded-lg bg-error-container p-3 text-on-error-container">
          {msg}
        </p>
      ) : null}
      {notice ? <p className="mt-md text-label-md text-on-surface-variant">{notice}</p> : null}

      {round?.isLegacy ? (
        <section className="mt-lg rounded-xl bg-surface-container-low p-md">
          <h2 className="text-headline-md">Rodada antiga (Time A/B)</h2>
          <p className="mt-1 text-body-md">
            Placar {round.legacyHomeScore} x {round.legacyAwayScore}. Cores, goleiros e gols não eram registrados e ficam como
            indisponíveis.
          </p>
          {round.bestPlayerName ? <p className="text-body-md">Craque: {playerDisplayName(round.bestPlayerName)}</p> : null}
        </section>
      ) : !form || !selected ? (
        selected ? (
          <p className="mt-lg text-body-md text-on-surface-variant">
            Confirme o sorteio de Vermelho, Preto e Verde antes de registrar o resultado.
          </p>
        ) : null
      ) : (
        <form
          className="mt-lg flex flex-col gap-lg"
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
        >
          {correcting ? (
            <section className="rounded-xl border border-outline-variant bg-surface-container-low p-md">
              <h2 className="text-headline-md">Correção da rodada encerrada</h2>
              <p className="text-body-md text-on-surface-variant">
                Revisão vigente: {round?.revision}. A versão anterior fica guardada e os totais são recalculados sem contar duas vezes.
              </p>
              <ul className="mt-1 text-label-md text-on-surface-variant">
                {round?.revisions.map((r) => (
                  <li key={r.revision}>
                    Revisão {r.revision} · {new Date(r.createdAt).toLocaleString("pt-BR")}
                    {r.reason ? ` · motivo: ${r.reason}` : " · fechamento original"}
                    {r.isCurrent ? " (vigente)" : ""}
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          <PresenceSection teams={teams} roster={roster} form={form} setForm={setForm} />
          <GamesSection teams={teams} roster={roster} form={form} setForm={setForm} colorOf={colorOf} />
          <Preview teams={teams} games={form.games} />
          <AwardsSection roster={roster} form={form} setForm={setForm} />

          <label className="text-label-md">
            Observações (opcional)
            <textarea
              className={`mt-1 block w-full ${input}`}
              rows={2}
              maxLength={1000}
              value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })}
            />
          </label>
          {correcting ? (
            <label className="text-label-md">
              Motivo da correção (obrigatório)
              <input
                className={`mt-1 block w-full ${input}`}
                required
                minLength={5}
                maxLength={500}
                value={form.reason}
                onChange={(e) => setForm({ ...form, reason: e.target.value })}
                placeholder="Ex.: autor do 2º gol lançado errado"
              />
            </label>
          ) : null}

          <button type="submit" disabled={busy} className={primaryButton}>
            {correcting ? "Salvar correção" : "Encerrar rodada"}
          </button>
        </form>
      )}
    </AppShell>
  );
}

type FormProps = { roster: RosterPlayer[]; form: SheetForm; setForm: (f: SheetForm) => void };
type TeamLite = Draw["teams"][number];

function PresenceSection({ teams, roster, form, setForm }: FormProps & { teams: TeamLite[] }) {
  return (
    <section aria-labelledby="presenca-titulo">
      <h2 id="presenca-titulo" className="text-headline-md">
        Presença
      </h2>
      <div className="mt-sm grid gap-sm md:grid-cols-3">
        {teams.map((t) => {
          const style = teamStyle(t);
          return (
            <fieldset key={t.id} className={`rounded-xl border-2 ${style.border} p-md`}>
              <legend className="px-1 text-label-md font-semibold">{style.label}</legend>
              {roster
                .filter((p) => p.teamId === t.id)
                .map((p) => {
                  const state = form.presence[p.playerProfileId] ?? { present: true, playedGoalkeeper: p.isGoalkeeper };
                  const set = (patch: Partial<typeof state>) =>
                    setForm({ ...form, presence: { ...form.presence, [p.playerProfileId]: { ...state, ...patch } } });
                  return (
                    <div key={p.playerProfileId} className="flex flex-wrap items-center justify-between gap-2 py-1">
                      <label className="flex min-h-11 items-center gap-2 text-body-md">
                        <input type="checkbox" checked={state.present} onChange={(e) => set({ present: e.target.checked })} />
                        {playerDisplayName(p.displayName)}
                      </label>
                      <label className="flex items-center gap-1 text-label-md text-on-surface-variant">
                        <input
                          type="checkbox"
                          checked={state.playedGoalkeeper}
                          onChange={(e) => set({ playedGoalkeeper: e.target.checked })}
                        />
                        atuou no gol
                      </label>
                    </div>
                  );
                })}
            </fieldset>
          );
        })}
      </div>
    </section>
  );
}

function GamesSection({
  teams,
  roster,
  form,
  setForm,
  colorOf,
}: FormProps & { teams: TeamLite[]; colorOf: (teamId: string) => string }) {
  const updateGame = (i: number, patch: Partial<GameForm>) =>
    setForm({ ...form, games: form.games.map((g, j) => (j === i ? { ...g, ...patch } : g)) });
  const defaultGame = (): GameForm => {
    const last = form.games[form.games.length - 1];
    const home = teams[0]?.id ?? "";
    const away = teams[1]?.id ?? "";
    return { homeTeamId: last ? last.awayTeamId : home, awayTeamId: last ? (teams.find((t) => t.id !== last.awayTeamId && t.id !== last.homeTeamId)?.id ?? away) : away, homeScore: 0, awayScore: 0, goals: [] };
  };

  return (
    <section aria-labelledby="confrontos-titulo">
      <h2 id="confrontos-titulo" className="text-headline-md">
        Confrontos
      </h2>
      {form.games.length === 0 ? <p className="text-body-md text-on-surface-variant">Nenhum confronto lançado.</p> : null}
      <ol className="mt-sm flex flex-col gap-md">
        {form.games.map((g, i) => {
          const issues = gameIssues(g, roster, form.presence);
          const scorers = roster.filter(
            (p) => (p.teamId === g.homeTeamId || p.teamId === g.awayTeamId) && (form.presence[p.playerProfileId]?.present ?? true),
          );
          return (
            <li key={i} className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
              <div className="flex flex-wrap items-end gap-2">
                <span className="text-label-md font-semibold">Jogo {i + 1}</span>
                <TeamSelect label="Time da casa" ariaLabel={`Jogo ${i + 1}: time da casa`} teams={teams} value={g.homeTeamId} onChange={(v) => updateGame(i, { homeTeamId: v })} />
                <ScoreInput label={`Gols ${colorOf(g.homeTeamId)}`} ariaLabel={`Jogo ${i + 1}: gols ${colorOf(g.homeTeamId)}`} value={g.homeScore} onChange={(v) => updateGame(i, { homeScore: v })} />
                <span aria-hidden="true">x</span>
                <ScoreInput label={`Gols ${colorOf(g.awayTeamId)}`} ariaLabel={`Jogo ${i + 1}: gols ${colorOf(g.awayTeamId)}`} value={g.awayScore} onChange={(v) => updateGame(i, { awayScore: v })} />
                <TeamSelect label="Visitante" ariaLabel={`Jogo ${i + 1}: visitante`} teams={teams} value={g.awayTeamId} onChange={(v) => updateGame(i, { awayTeamId: v })} />
                <button
                  type="button"
                  className={secondaryButton}
                  onClick={() => setForm({ ...form, games: form.games.filter((_, j) => j !== i) })}
                  aria-label={`Remover jogo ${i + 1}`}
                >
                  Remover jogo
                </button>
              </div>

              <div className="mt-sm">
                <p className="text-label-md">Gols de {gameLabel(g, colorOf)}</p>
                <ul className="mt-1 flex flex-col gap-1">
                  {g.goals.map((goal, k) => (
                    <li key={k} className="flex flex-wrap items-center gap-2">
                      <label className="text-label-md">
                        <span className="sr-only">Jogo {i + 1}: autor do gol {k + 1}</span>
                        <select
                          className={input}
                          value={goal.playerProfileId}
                          onChange={(e) =>
                            updateGame(i, { goals: g.goals.map((x, j) => (j === k ? { ...x, playerProfileId: e.target.value } : x)) })
                          }
                        >
                          <option value="">Autor…</option>
                          {scorers.map((p) => (
                            <option key={p.playerProfileId} value={p.playerProfileId}>
                              {playerDisplayName(p.displayName)} ({colorOf(p.teamId)})
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="flex items-center gap-1 text-label-md">
                        <input
                          type="checkbox"
                          checked={goal.ownGoal}
                          onChange={(e) =>
                            updateGame(i, { goals: g.goals.map((x, j) => (j === k ? { ...x, ownGoal: e.target.checked } : x)) })
                          }
                        />
                        gol contra
                      </label>
                      <button
                        type="button"
                        className="min-h-11 px-2 text-label-md text-error underline"
                        onClick={() => updateGame(i, { goals: g.goals.filter((_, j) => j !== k) })}
                        aria-label={`Remover gol ${k + 1} do jogo ${i + 1}`}
                      >
                        Remover
                      </button>
                    </li>
                  ))}
                </ul>
                <button
                  type="button"
                  className={`mt-1 ${secondaryButton}`}
                  onClick={() => updateGame(i, { goals: [...g.goals, { playerProfileId: "", ownGoal: false }] })}
                >
                  Adicionar gol
                </button>
              </div>

              {issues.length > 0 ? (
                <ul className="mt-sm text-label-md text-error">
                  {issues.map((m) => (
                    <li key={m}>⚠️ {m}</li>
                  ))}
                </ul>
              ) : (
                <p className="mt-sm text-label-md text-on-surface-variant">✓ Gols fecham o placar.</p>
              )}
            </li>
          );
        })}
      </ol>
      <button type="button" className={`mt-sm ${secondaryButton}`} onClick={() => setForm({ ...form, games: [...form.games, defaultGame()] })}>
        Adicionar confronto
      </button>
    </section>
  );
}

function Preview({ teams, games }: { teams: TeamLite[]; games: GameForm[] }) {
  const standings = previewStandings(games, teams.map((t) => t.id));
  return (
    <section aria-labelledby="previa-titulo">
      <h2 id="previa-titulo" className="text-headline-md">
        Classificação da rodada
      </h2>
      <p className="text-label-md text-on-surface-variant">3 pontos por vitória e 1 por empate; desempate por saldo e gols pró.</p>
      <table className="mt-sm w-full max-w-xl text-left text-body-md">
        <thead className="text-label-md text-on-surface-variant">
          <tr>
            <th scope="col">Cor</th>
            <th scope="col">J</th>
            <th scope="col">V</th>
            <th scope="col">E</th>
            <th scope="col">D</th>
            <th scope="col">Gols</th>
            <th scope="col">Pts</th>
          </tr>
        </thead>
        <tbody>
          {teams.map((t) => {
            const s = standings.find((x) => x.teamId === t.id)!;
            const style = teamStyle(t);
            return (
              <tr key={t.id} className={s.isRoundWinner ? "font-semibold" : ""}>
                <th scope="row" className="py-1">
                  <span aria-hidden="true" className={`mr-1 inline-block h-3 w-3 rounded-full ${style.swatch}`} />
                  {style.label}
                  {s.isRoundWinner ? " 🏆" : ""}
                </th>
                <td>{s.played}</td>
                <td>{s.wins}</td>
                <td>{s.draws}</td>
                <td>{s.losses}</td>
                <td>
                  {s.goalsFor}:{s.goalsAgainst}
                </td>
                <td>{s.points}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </section>
  );
}

function AwardsSection({ roster, form, setForm }: FormProps) {
  const candidates = awardCandidates(roster, form.presence);
  return (
    <section aria-labelledby="premios-titulo" className="flex flex-col gap-sm">
      <h2 id="premios-titulo" className="text-headline-md">
        Prêmios da partida
      </h2>
      <label className="text-label-md">
        Melhor Jogador (entre os presentes)
        <select
          className={`mt-1 block w-full max-w-md ${input}`}
          value={form.bestPlayerProfileId}
          onChange={(e) => setForm({ ...form, bestPlayerProfileId: e.target.value })}
        >
          <option value="">Sem prêmio</option>
          {candidates.bestPlayer.map((p) => (
            <option key={p.playerProfileId} value={p.playerProfileId}>
              {playerDisplayName(p.displayName)}
            </option>
          ))}
        </select>
      </label>
      <label className="text-label-md">
        Melhor Goleiro (entre quem atuou no gol)
        <select
          className={`mt-1 block w-full max-w-md ${input}`}
          value={form.bestGoalkeeperProfileId}
          onChange={(e) => setForm({ ...form, bestGoalkeeperProfileId: e.target.value })}
        >
          <option value="">Sem prêmio</option>
          {candidates.bestGoalkeeper.map((p) => (
            <option key={p.playerProfileId} value={p.playerProfileId}>
              {playerDisplayName(p.displayName)}
            </option>
          ))}
        </select>
      </label>
    </section>
  );
}

function TeamSelect({ label, ariaLabel, teams, value, onChange }: { label: string; ariaLabel: string; teams: TeamLite[]; value: string; onChange: (v: string) => void }) {
  return (
    <label className="text-label-md">
      {label}
      <select aria-label={ariaLabel} className={`mt-1 block ${input}`} value={value} onChange={(e) => onChange(e.target.value)}>
        {teams.map((t) => (
          <option key={t.id} value={t.id}>
            {teamStyle(t).label}
          </option>
        ))}
      </select>
    </label>
  );
}

function ScoreInput({ label, ariaLabel, value, onChange }: { label: string; ariaLabel: string; value: number; onChange: (v: number) => void }) {
  return (
    <label className="text-label-md">
      {label}
      <input
        type="number"
        aria-label={ariaLabel}
        min={0}
        max={99}
        inputMode="numeric"
        className={`mt-1 block w-16 ${input}`}
        value={value}
        onChange={(e) => onChange(Math.max(0, Math.min(99, Number(e.target.value) || 0)))}
      />
    </label>
  );
}
