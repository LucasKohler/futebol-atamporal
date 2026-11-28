"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { AppShell } from "@/components/AppShell";
import { PaymentsLink } from "@/components/pix/PaymentsLink";
import { PushToggle } from "@/components/notifications/PushToggle";
import { InstallAppBanner } from "@/components/pwa/InstallAppBanner";
import { api, ApiError, SeasonStats } from "@/lib/api";
import { canLogoutAfter, revocationMessage } from "@/lib/notifications";
import { disablePushAndLogout } from "@/lib/push";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { groupPeriod, monthName, myStats } from "@/lib/season";
import { formatBr } from "@/lib/phone";
import { formatRating } from "@/lib/ratings";
import { registrationStatusLabel, useSession } from "@/lib/useSession";

type History = Awaited<ReturnType<typeof api.playerHistory>>;

export default function PerfilPage() {
  const router = useRouter();
  const { user, group, loading, refresh } = useSession();
  const [history, setHistory] = useState<History | null>(null);
  const [season, setSeason] = useState<SeasonStats | null>(null);

  useEffect(() => {
    if (!user?.playerProfileId) return;
    api.playerHistory(user.playerProfileId).then(setHistory).catch(console.error);
  }, [user]);

  useEffect(() => {
    if (!group) return;
    // Mês/ano no fuso do grupo (mesma regra da API), não no fuso do aparelho.
    const { year, month } = groupPeriod(new Date(), group.timeZoneId);
    api.season(group.id, year, month).then(setSeason).catch(console.error);
  }, [group]);

  // Push desligado no servidor (503 push_not_configured) → o controle explica em vez de oferecer "Ativar".
  const [pushMode, setPushMode] = useState<string | null>(null);
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutError, setLogoutError] = useState<string | null>(null);
  // Remonta o controle de push depois de uma tentativa de saída, para mostrar o estado real do aparelho.
  const [pushKey, setPushKey] = useState(0);
  useEffect(() => {
    api
      .vapidPublicKey()
      .then(() => setPushMode("Enabled"))
      .catch((e) => setPushMode(e instanceof ApiError && e.code === "push_not_configured" ? "Disabled" : null));
  }, []);

  const mine = myStats(season, user?.playerProfileId);

  if (loading) return <div className="flex min-h-dvh items-center justify-center">Carregando…</div>;

  return (
    <AppShell>
      <h1 className="text-headline-lg">
        {playerDisplayName(user?.registeredName ?? history?.displayName, user?.nickname)}
      </h1>
      <p className="text-body-md text-on-surface-variant">{user?.phone ? formatBr(user.phone) : null}</p>
      {user?.nickname ? (
        <p className="text-label-md text-on-surface-variant">Nome cadastrado: {user.registeredName}</p>
      ) : null}
      {/* A API só envia a nota para administradores. */}
      {history?.skillRating != null ? (
        <p className="mt-2 text-label-md">Nota de linha: {formatRating(history.skillRating)}</p>
      ) : null}

      <div className="mt-md">
        <PaymentsLink />
      </div>

      <section aria-label="App e avisos" className="mt-md flex flex-col gap-sm">
        <InstallAppBanner variant="card" />
        <PushToggle key={pushKey} serverMode={pushMode} />
      </section>

      <div className="mt-lg grid grid-cols-2 gap-md">
        <div className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
          <p className="text-label-sm uppercase text-on-surface-variant">Partidas</p>
          <p className="mt-1 text-headline-lg text-primary">{history?.history.length ?? 0}</p>
        </div>
        <div className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
          <p className="text-label-sm uppercase text-on-surface-variant">Última nota</p>
          <p className="mt-1 text-headline-lg text-primary">{history?.history[0]?.rating != null ? formatRating(history.history[0].rating) : "—"}</p>
        </div>
      </div>

      {mine && season ? (
        <section aria-labelledby="temporada-titulo" className="mt-lg">
          <h2 id="temporada-titulo" className="text-headline-md">
            Gols e prêmios
          </h2>
          <p className="text-label-md text-on-surface-variant">
            {monthName(season.month ?? 1)} de {season.year}, no fuso do grupo ({season.timeZoneId}). Outros meses em Rankings.
          </p>
          <dl className="mt-sm grid grid-cols-2 gap-md sm:grid-cols-4">
            <Stat label={`Gols em ${monthName(season.month ?? 1)}`} value={mine.monthGoals} />
            <Stat label={`Gols em ${season.year}`} value={mine.yearGoals} />
            <Stat label="Melhor Jogador (mês)" value={mine.bestPlayer} />
            <Stat label="Melhor Goleiro (mês)" value={mine.bestGoalkeeper} />
            <Stat label="🧤 Pênaltis defendidos (mês)" value={mine.penaltySaves} />
            <Stat label="Vitórias nos pênaltis (mês)" value={mine.gamesWonOnPenalties} />
          </dl>
        </section>
      ) : null}

      <section className="mt-xl">
        <h2 className="text-headline-md">Histórico</h2>
        <ul className="mt-sm divide-y divide-outline-variant rounded-xl border border-outline-variant bg-surface-container-lowest">
          {(history?.history ?? []).map((h) => (
            <li key={h.matchId} className="px-md py-sm">
              <p className="font-semibold">{h.title || "Partida"}</p>
              <p className="text-label-md text-on-surface-variant">
                {new Date(h.startsAt).toLocaleDateString("pt-BR")} · {registrationStatusLabel(h.status)}
                {h.rating != null ? ` · nota ${formatRating(h.rating)}` : ""}
                {h.playedGoalkeeper ? " · no gol" : ""}
                {h.penaltySaves ? ` · 🧤 ${h.penaltySaves}` : ""}
              </p>
            </li>
          ))}
          {!history?.history?.length ? <li className="p-md text-on-surface-variant">Sem histórico.</li> : null}
        </ul>
      </section>

      {logoutError ? (
        <p id="logout-erro" role="alert" className="mt-lg rounded-lg bg-danger-bg p-3 text-body-md text-danger-strong">
          {logoutError}
        </p>
      ) : null}
      <button
        type="button"
        disabled={loggingOut}
        aria-describedby={logoutError ? "logout-erro" : undefined}
        className="mt-lg w-full rounded-full border border-outline px-4 py-3 text-label-md disabled:opacity-50"
        onClick={async () => {
          // Conta da ação capturada no toque: DELETE do push e logout só valem para ela.
          const expectedUserId = user?.id;
          setLoggingOut(true);
          setLogoutError(null);
          try {
            // D2: cancela o push ANTES de sair. Se nenhuma ponta confirmar a revogação, não sai:
            // o aparelho poderia continuar recebendo avisos desta conta depois do logout.
            // O logout acontece com o bloqueio de gravação do push na mão: nenhuma ativação grava no intervalo.
            const { revocation, loggedOut, sessionChanged, sessionEnded } = await disablePushAndLogout(
              () => api.logout(expectedUserId),
              expectedUserId,
            );
            if (sessionChanged) {
              // Outra conta assumiu o cookie: não é logout desta ação; mantém o estado local e a sessão vigente.
              setLogoutError(revocationMessage({ status: "failed", reason: "session" }, "logout"));
              setPushKey((k) => k + 1);
              return;
            }
            if (sessionEnded) {
              // 401: a sessão já não existe no servidor (logout em outra aba, expiração de 28 dias ou cookie antigo).
              // O logout não apaga o cookie (204 sem Set-Cookie): a sessão é conferida em /me e a tela vai ao login.
              await refresh();
              router.replace("/login");
              return;
            }
            if (!loggedOut || !canLogoutAfter(revocation)) {
              setLogoutError(revocationMessage(revocation, "logout"));
              setPushKey((k) => k + 1);
              return;
            }
            await refresh();
            router.replace("/login");
          } catch (e) {
            setLogoutError(e instanceof Error ? `Não foi possível sair: ${e.message}` : "Não foi possível sair. Tente de novo.");
          } finally {
            setLoggingOut(false);
          }
        }}
      >
        {loggingOut ? "Saindo…" : "Sair"}
      </button>
    </AppShell>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
      <dt className="text-label-sm uppercase text-on-surface-variant">{label}</dt>
      <dd className="mt-1 text-headline-lg text-primary">{value}</dd>
    </div>
  );
}