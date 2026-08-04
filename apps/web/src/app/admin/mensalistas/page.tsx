"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { api, MonthlyMember, Payment } from "@/lib/api";
import { useSession } from "@/lib/useSession";

export default function MensalistasPage() {
  const { group, loading, isAdmin } = useSession();
  const [members, setMembers] = useState<MonthlyMember[]>([]);
  const [payments, setPayments] = useState<Payment[]>([]);
  const [periodId, setPeriodId] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  async function reload(groupId: string) {
    const now = new Date();
    const period = await api.ensurePeriod(groupId, now.getFullYear(), now.getMonth() + 1);
    setPeriodId(period.id);
    setMembers(await api.listMembers(groupId));
    setPayments(await api.listPayments(groupId, now.getFullYear(), now.getMonth() + 1));
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
      <p className="mt-1 text-body-md text-on-surface-variant">Vínculos e pagamentos do mês corrente</p>
      {msg ? <p className="mt-md text-error">{msg}</p> : null}

      <section className="mt-lg">
        <h2 className="text-headline-md">Membros</h2>
        <div className="mt-sm overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
          {members.map((m) => (
            <div key={m.id} className="flex items-center justify-between border-b border-outline-variant px-md py-sm last:border-0">
              <div>
                <p className="font-semibold">{m.displayName}</p>
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
          ))}
          {members.length === 0 ? <p className="p-md text-on-surface-variant">Nenhum mensalista.</p> : null}
        </div>
      </section>

      <section className="mt-lg">
        <h2 className="text-headline-md">Pagamentos</h2>
        <div className="mt-sm overflow-hidden rounded-xl border border-outline-variant bg-surface-container-lowest">
          {payments.map((p) => (
            <div key={p.id} className="flex flex-wrap items-center justify-between gap-2 border-b border-outline-variant px-md py-sm last:border-0">
              <div>
                <p className="font-semibold">{p.playerName}</p>
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
