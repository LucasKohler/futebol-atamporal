"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, GroupPlayer, MonthlyMember, Payment } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { useSession } from "@/lib/useSession";

export default function MensalistasPage() {
  const { group, loading, isAdmin } = useSession();
  const [players, setPlayers] = useState<GroupPlayer[]>([]);
  const [members, setMembers] = useState<MonthlyMember[]>([]);
  const [payments, setPayments] = useState<Payment[]>([]);
  const [periodId, setPeriodId] = useState<string | null>(null);
  const [nickDraft, setNickDraft] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState<string | null>(null);

  async function reload(groupId: string) {
    const now = new Date();
    const period = await api.ensurePeriod(groupId, now.getFullYear(), now.getMonth() + 1);
    setPeriodId(period.id);
    const [plist, mlist, plistPay] = await Promise.all([
      api.listPlayers(groupId),
      api.listMembers(groupId),
      api.listPayments(groupId, now.getFullYear(), now.getMonth() + 1),
    ]);
    setPlayers(plist);
    setMembers(mlist);
    setPayments(plistPay);
    setNickDraft(Object.fromEntries(plist.map((p) => [p.playerProfileId, p.nickname ?? ""])));
  }

  useEffect(() => {
    if (!group || !isAdmin) return;
    reload(group.id).catch((e) => setMsg(e.message));
  }, [group, isAdmin]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;
  if (!isAdmin) return <AppShell><p>Acesso admin.</p></AppShell>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">Mensalistas</h1>
      <p className="mt-1 text-body-md text-on-surface-variant">Jogadores, apelidos e pagamentos do mês</p>
      {msg ? <p className="mt-md text-error">{msg}</p> : null}

      <section className="mt-lg">
        <h2 className="text-headline-md">Jogadores</h2>
        <div className="mt-sm overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
          {players.map((p) => (
            <div key={p.playerProfileId} className="border-b border-outline-variant px-md py-sm last:border-0">
              <p className="font-semibold">{playerDisplayName(p.registeredName, p.nickname)}</p>
              <p className="text-label-md text-on-surface-variant">
                {p.registeredName} · {p.phone}
                {p.isMonthlyMember ? " · mensalista" : ""}
              </p>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <input
                  aria-label={`Apelido de ${p.registeredName}`}
                  className="min-w-[10rem] flex-1 rounded-lg border border-outline-variant px-2 py-1 text-body-md"
                  placeholder="Apelido fixo"
                  value={nickDraft[p.playerProfileId] ?? ""}
                  onChange={(e) => setNickDraft((d) => ({ ...d, [p.playerProfileId]: e.target.value }))}
                />
                <button
                  type="button"
                  className="text-label-md underline"
                  onClick={async () => {
                    try {
                      const nick = nickDraft[p.playerProfileId]?.trim() || null;
                      const updated = await api.setNickname(group!.id, p.playerProfileId, nick);
                      setPlayers((list) =>
                        list.map((row) => (row.playerProfileId === updated.playerProfileId ? updated : row)),
                      );
                      setNickDraft((d) => ({ ...d, [updated.playerProfileId]: updated.nickname ?? "" }));
                      await reload(group!.id);
                    } catch (e) {
                      setMsg(e instanceof Error ? e.message : "Erro ao salvar apelido");
                    }
                  }}
                >
                  Salvar apelido
                </button>
                {!p.isMonthlyMember ? (
                  <button
                    type="button"
                    className="text-label-md underline"
                    onClick={async () => {
                      await api.addMember(group!.id, p.playerProfileId);
                      await reload(group!.id);
                    }}
                  >
                    Tornar mensalista
                  </button>
                ) : null}
              </div>
            </div>
          ))}
          {players.length === 0 ? <p className="p-md text-on-surface-variant">Nenhum jogador.</p> : null}
        </div>
      </section>

      <section className="mt-lg">
        <h2 className="text-headline-md">Vínculos</h2>
        <div className="mt-sm overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
          {members.map((m) => {
            const linked = players.find((p) => p.playerProfileId === m.playerProfileId);
            return (
            <div key={m.id} className="flex items-center justify-between border-b border-outline-variant px-md py-sm last:border-0">
              <div>
                <p className="font-semibold">{playerDisplayName(linked?.registeredName ?? m.displayName, linked?.nickname)}</p>
                <p className="text-label-md text-on-surface-variant">{m.status}</p>
              </div>
              {m.status === "Active" ? (
                <button
                  type="button"
                  className="text-label-md underline"
                  onClick={async () => {
                    await api.setMemberStatus(group!.id, m.id, "Inactive");
                    await reload(group!.id);
                  }}
                >
                  Desativar
                </button>
              ) : (
                <button
                  type="button"
                  className="text-label-md underline"
                  onClick={async () => {
                    await api.setMemberStatus(group!.id, m.id, "Active");
                    await reload(group!.id);
                  }}
                >
                  Reativar
                </button>
              )}
            </div>
          );
          })}
          {members.length === 0 ? <p className="p-md text-on-surface-variant">Nenhum mensalista.</p> : null}
        </div>
      </section>

      <section className="mt-lg">
        <h2 className="text-headline-md">Pagamentos</h2>
        <div className="mt-sm overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
          {payments.map((p) => (
            <div key={p.id} className="flex flex-wrap items-center justify-between gap-2 border-b border-outline-variant px-md py-sm last:border-0">
              <div>
                <p className="font-semibold">{playerDisplayName(p.playerName)}</p>
                <p className="text-label-md text-on-surface-variant">
                  {p.periodLabel} · R$ {p.amount.toFixed(2)} · {p.status}
                </p>
              </div>
              {p.status !== "Paid" && periodId ? (
                <button
                  type="button"
                  className="rounded-full bg-pitch-green-container px-3 py-1.5 text-label-md text-on-primary"
                  onClick={async () => {
                    await api.upsertPayment(group!.id, p.monthlyMembershipId, p.billingPeriodId, p.amount, "Paid");
                    await reload(group!.id);
                  }}
                >
                  Marcar pago
                </button>
              ) : null}
            </div>
          ))}
          {payments.length === 0 ? <p className="p-md text-on-surface-variant">Sem lançamentos.</p> : null}
        </div>
      </section>
    </AppShell>
  );
}