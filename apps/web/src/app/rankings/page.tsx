"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, RankingRow } from "@/lib/api";
import { useSession } from "@/lib/useSession";

export default function RankingsPage() {
  const { group, loading } = useSession();
  const [rows, setRows] = useState<RankingRow[]>([]);
  const [sortBy, setSortBy] = useState("wins");

  useEffect(() => {
    if (!group) return;
    api.rankings(group.id, sortBy).then(setRows).catch(console.error);
  }, [group, sortBy]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">Rankings</h1>
      <div className="mt-md flex flex-wrap gap-2">
        {[
          ["wins", "Vitórias"],
          ["winrate", "Aproveitamento"],
          ["rating", "Nota"],
          ["motm", "Craques"],
        ].map(([key, label]) => (
          <button
            key={key}
            type="button"
            onClick={() => setSortBy(key)}
            className={`rounded-full px-3 py-1.5 text-label-md ${sortBy === key ? "bg-pitch-green-container text-on-primary" : "border border-outline"}`}
          >
            {label}
          </button>
        ))}
      </div>

      <ol className="mt-lg space-y-2">
        {rows.map((r, i) => (
          <li
            key={r.playerProfileId}
            className="flex items-center justify-between rounded-xl border border-outline-variant bg-surface-container-lowest px-md py-sm"
          >
            <div className="flex items-center gap-3">
              <span className="w-6 text-label-md text-on-surface-variant">{i + 1}</span>
              <div>
                <p className="font-semibold">{r.displayName}</p>
                <p className="text-label-md text-on-surface-variant">
                  {r.matches} jogos · {r.wins}V {r.losses}D · {(r.winRate * 100).toFixed(0)}%
                </p>
              </div>
            </div>
            <div className="text-right text-label-md">
              {sortBy === "motm" ? `${r.manOfTheMatch}×` : null}
              {sortBy === "rating" ? r.averageRating?.toFixed(1) ?? "—" : null}
              {sortBy === "wins" ? `${r.wins}V` : null}
              {sortBy === "winrate" ? `${(r.winRate * 100).toFixed(0)}%` : null}
            </div>
          </li>
        ))}
      </ol>
      {rows.length === 0 ? (
        <p className="mt-md text-on-surface-variant">Sem dados — encerre partidas para popular o ranking.</p>
      ) : null}
    </AppShell>
  );
}
