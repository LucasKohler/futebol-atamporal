"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { AppShell } from "@/components/AppShell";
import { api } from "@/lib/api";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { useSession } from "@/lib/useSession";

type History = Awaited<ReturnType<typeof api.playerHistory>>;

export default function PerfilPage() {
  const router = useRouter();
  const { user, loading, refresh } = useSession();
  const [history, setHistory] = useState<History | null>(null);

  useEffect(() => {
    if (!user?.playerProfileId) return;
    api.playerHistory(user.playerProfileId).then(setHistory).catch(console.error);
  }, [user]);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">
        {playerDisplayName(user?.registeredName ?? history?.displayName, user?.nickname)}
      </h1>
      <p className="text-body-md text-on-surface-variant">{user?.phone}</p>
      {user?.nickname ? (
        <p className="text-label-md text-on-surface-variant">Nome cadastrado: {user.registeredName}</p>
      ) : null}
      {/* A API só envia a nota para administradores. */}
      {history?.skillRating != null ? (
        <p className="mt-2 text-label-md">Nota de linha: {history.skillRating.toFixed(1)}</p>
      ) : null}

      <div className="mt-lg grid grid-cols-2 gap-md">
        <div className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
          <p className="text-label-sm uppercase text-on-surface-variant">Partidas</p>
          <p className="mt-1 text-headline-lg text-primary">{history?.history.length ?? 0}</p>
        </div>
        <div className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
          <p className="text-label-sm uppercase text-on-surface-variant">Última nota</p>
          <p className="mt-1 text-headline-lg text-primary">{history?.history[0]?.rating?.toFixed(1) ?? "—"}</p>
        </div>
      </div>

      <section className="mt-xl">
        <h2 className="text-headline-md">Histórico</h2>
        <ul className="mt-sm divide-y divide-outline-variant rounded-xl border border-outline-variant bg-surface-container-lowest">
          {(history?.history ?? []).map((h) => (
            <li key={h.matchId} className="px-md py-sm">
              <p className="font-semibold">{h.title || "Partida"}</p>
              <p className="text-label-md text-on-surface-variant">
                {new Date(h.startsAt).toLocaleDateString("pt-BR")} · {h.status}
                {h.rating != null ? ` · nota ${h.rating}` : ""}
              </p>
            </li>
          ))}
          {!history?.history?.length ? <li className="p-md text-on-surface-variant">Sem histórico.</li> : null}
        </ul>
      </section>

      <button
        type="button"
        className="mt-lg w-full rounded-full border border-outline px-4 py-3 text-label-md"
        onClick={async () => {
          await api.logout();
          await refresh();
          router.replace("/login");
        }}
      >
        Sair
      </button>
    </AppShell>
  );
}