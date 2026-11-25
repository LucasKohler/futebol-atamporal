"use client";

import { useEffect, useMemo, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, Draw, MatchDetail } from "@/lib/api";
import { useSession } from "@/lib/useSession";

export default function RegistroPage() {
  const { group, loading, isAdmin } = useSession();
  const [match, setMatch] = useState<MatchDetail | null>(null);
  const [draw, setDraw] = useState<Draw | null>(null);
  const [home, setHome] = useState(0);
  const [away, setAway] = useState(0);
  const [motm, setMotm] = useState("");
  const [presence, setPresence] = useState<Record<string, boolean>>({});
  const [ratings, setRatings] = useState<Record<string, number>>({});
  const [msg, setMsg] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!group) return;
    (async () => {
      const m = await api.upcomingMatch(group.id);
      setMatch(m);
      if (!m) return;
      const d = await api.getDraw(m.match.id);
      setDraw(d);
      const map: Record<string, boolean> = {};
      const rates: Record<string, number> = {};
      m.confirmed.forEach((p) => {
        map[p.playerProfileId] = true;
        // Página só de admin: a API envia a nota de linha; 5 é o padrão histórico se faltar.
        rates[p.playerProfileId] = p.skillRating ?? 5;
      });
      setPresence(map);
      setRatings(rates);
    })().catch((e) => setMsg(e.message));
  }, [group]);

  const teams = useMemo(() => draw?.teams ?? [], [draw]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (!isAdmin) return <AppShell><p>Acesso admin.</p></AppShell>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">Registro pós-jogo</h1>
      {!match ? (
        <p className="mt-md text-on-surface-variant">Sem partida para encerrar.</p>
      ) : done ? (
        <p className="mt-md rounded-xl bg-surface-container-low p-lg">Partida encerrada com sucesso.</p>
      ) : (
        <form
          className="mt-lg flex max-w-xl flex-col gap-lg"
          onSubmit={async (e) => {
            e.preventDefault();
            setMsg(null);
            try {
              const winner =
                home === away ? null : home > away ? teams[0]?.id : teams[1]?.id;
              await api.closeMatch(match.match.id, {
                homeScore: home,
                awayScore: away,
                winnerTeamId: winner,
                manOfTheMatchPlayerProfileId: motm || null,
                presence: Object.entries(presence).map(([playerProfileId, present]) => ({ playerProfileId, present })),
                ratings: Object.entries(ratings).map(([playerProfileId, score]) => ({ playerProfileId, score })),
              });
              setDone(true);
            } catch (err) {
              setMsg(err instanceof Error ? err.message : "Erro");
            }
          }}
        >
          <div className="grid grid-cols-2 gap-md">
            <label className="text-label-md">
              Placar {teams[0]?.name ?? "A"}
              <input type="number" min={0} className="mt-1 w-full rounded-lg border border-outline-variant px-3 py-2" value={home} onChange={(e) => setHome(Number(e.target.value))} />
            </label>
            <label className="text-label-md">
              Placar {teams[1]?.name ?? "B"}
              <input type="number" min={0} className="mt-1 w-full rounded-lg border border-outline-variant px-3 py-2" value={away} onChange={(e) => setAway(Number(e.target.value))} />
            </label>
          </div>

          <section>
            <h2 className="text-headline-md">Presença</h2>
            <ul className="mt-sm space-y-2">
              {match.confirmed.map((p) => (
                <li key={p.playerProfileId} className="flex items-center justify-between rounded-lg border border-outline-variant px-md py-sm">
                  <span>{p.displayName}</span>
                  <input
                    type="checkbox"
                    checked={presence[p.playerProfileId] ?? false}
                    onChange={(e) => setPresence((s) => ({ ...s, [p.playerProfileId]: e.target.checked }))}
                    aria-label={`Presente ${p.displayName}`}
                  />
                </li>
              ))}
            </ul>
          </section>

          <label className="text-label-md">
            Craque do dia
            <select className="mt-1 w-full rounded-lg border border-outline-variant px-3 py-2" value={motm} onChange={(e) => setMotm(e.target.value)}>
              <option value="">—</option>
              {match.confirmed.map((p) => (
                <option key={p.playerProfileId} value={p.playerProfileId}>
                  {p.displayName}
                </option>
              ))}
            </select>
          </label>

          <section>
            <h2 className="text-headline-md">Notas</h2>
            <ul className="mt-sm space-y-2">
              {match.confirmed.map((p) => (
                <li key={p.playerProfileId} className="flex items-center justify-between gap-3">
                  <span className="text-body-md">{p.displayName}</span>
                  <input
                    type="number"
                    min={0}
                    max={10}
                    step={0.1}
                    className="w-24 rounded-lg border border-outline-variant px-2 py-1"
                    value={ratings[p.playerProfileId] ?? 5}
                    onChange={(e) => setRatings((s) => ({ ...s, [p.playerProfileId]: Number(e.target.value) }))}
                  />
                </li>
              ))}
            </ul>
          </section>

          {msg ? <p className="text-error">{msg}</p> : null}
          <button type="submit" className="rounded-full bg-pitch-green-container py-3 text-label-md text-on-primary">
            Concluir partida
          </button>
        </form>
      )}
    </AppShell>
  );
}
