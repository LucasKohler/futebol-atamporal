"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { AdminEmpty, AdminError, AdminLoading, StatusBadge } from "@/components/admin/AdminCard";
import { AdminDrawer } from "@/components/admin/AdminDrawer";
import { AdminFilterChips, AdminSearch } from "@/components/admin/AdminSearch";
import { AdminShell } from "@/components/admin/AdminShell";
import { PlayerEditor } from "@/components/admin/PlayerEditor";
import { countPlayers, filterPlayers, PLAYER_FILTERS, type PlayerFilter } from "@/lib/adminFilters";
import { api, GroupPlayer, MonthlyMember } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { formatRating } from "@/lib/ratings";
import { useSession } from "@/lib/useSession";

/** Elenco & Notas: busca, filtros e edição de um jogador por vez. */
export default function ElencoPage() {
  const { group, isAdmin } = useSession();
  const [players, setPlayers] = useState<GroupPlayer[] | null>(null);
  const [members, setMembers] = useState<MonthlyMember[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<PlayerFilter>("all");
  const [editingId, setEditingId] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!group) return;
    setError(null);
    const [p, m] = await Promise.all([api.listPlayers(group.id), api.listMembers(group.id)]);
    setPlayers(p);
    setMembers(m);
  }, [group]);

  useEffect(() => {
    if (!group || !isAdmin) return;
    load().catch((e) => setError(e instanceof Error ? e.message : "Não foi possível carregar o elenco."));
  }, [group, isAdmin, load]);

  const visible = useMemo(() => (players ? filterPlayers(players, query, filter) : []), [players, query, filter]);
  const counts = useMemo(() => (players ? countPlayers(players) : undefined), [players]);

  return (
    <AdminShell moduleId="elenco" title="Elenco & Notas" description="Busque um jogador e abra a edição só dele: apelido, notas de linha e goleiro e vínculo.">
      {error ? <AdminError onRetry={() => load().catch(() => undefined)}>{error}</AdminError> : null}
      {players === null ? (
        error ? null : <AdminLoading label="Carregando elenco…" />
      ) : (
        <div className="flex flex-col gap-md">
          <div className="flex flex-col gap-sm">
            <AdminSearch
              label="Buscar jogador"
              placeholder="Nome, apelido ou celular"
              value={query}
              onChange={setQuery}
              resultCount={visible.length}
            />
            <AdminFilterChips label="Filtrar elenco" options={PLAYER_FILTERS} value={filter} onChange={setFilter} counts={counts} />
          </div>

          {players.length === 0 ? (
            <AdminEmpty>Nenhum jogador no grupo ainda. Jogadores aparecem aqui depois do primeiro cadastro.</AdminEmpty>
          ) : visible.length === 0 ? (
            <AdminEmpty
              action={
                <button type="button" className="min-h-11 text-label-md underline" onClick={() => { setQuery(""); setFilter("all"); }}>
                  Limpar busca e filtros
                </button>
              }
            >
              Nenhum jogador encontrado com esses critérios.
            </AdminEmpty>
          ) : (
            <ul className="flex flex-col gap-sm">
              {visible.map((p) => {
                const name = playerDisplayName(p.registeredName, p.nickname);
                const open = editingId === p.playerProfileId;
                return (
                  <li key={p.playerProfileId}>
                    <AdminDrawer
                      open={open}
                      onToggle={() => setEditingId(open ? null : p.playerProfileId)}
                      toggleLabel={{ open: "Fechar", closed: "Editar" }}
                      summary={
                        <div className="min-w-0">
                          <p className="truncate text-body-lg font-semibold">{name}</p>
                          <p className="truncate text-label-md text-on-surface-variant">
                            {p.nickname ? `${p.registeredName} · ` : ""}
                            {p.phone}
                          </p>
                          <div className="mt-1 flex flex-wrap gap-1">
                            <StatusBadge tone="info">Linha {formatRating(p.lineRating)}</StatusBadge>
                            {p.canPlayGoalkeeper ? (
                              <StatusBadge tone="success">Gol {formatRating(p.goalkeeperRating)}</StatusBadge>
                            ) : (
                              <StatusBadge>Sem gol</StatusBadge>
                            )}
                            <StatusBadge tone={p.isMonthlyMember ? "success" : "neutral"}>{p.isMonthlyMember ? "Mensalista" : "Avulso"}</StatusBadge>
                          </div>
                        </div>
                      }
                    >
                      <PlayerEditor
                        groupId={group!.id}
                        player={p}
                        membership={members.find((m) => m.playerProfileId === p.playerProfileId)}
                        onPlayerSaved={(updated) =>
                          setPlayers((list) => (list ?? []).map((row) => (row.playerProfileId === updated.playerProfileId ? updated : row)))
                        }
                        onMembershipChanged={load}
                        onClose={() => setEditingId(null)}
                      />
                    </AdminDrawer>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}
    </AdminShell>
  );
}
