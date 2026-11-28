"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { AdminEmpty, AdminError, AdminLoading, AdminNotice } from "@/components/admin/AdminCard";
import { AdminShell } from "@/components/admin/AdminShell";
import { AdminTabs } from "@/components/admin/AdminTabs";
import { RoundStepper } from "@/components/admin/RoundStepper";
import { inputClass } from "@/components/admin/ui";
import { CloseDayTab, type CloseDayBody } from "@/components/admin/live/CloseDayTab";
import { DayCheckTab } from "@/components/admin/live/DayCheckTab";
import { LiveTab, type Mutate } from "@/components/admin/live/LiveTab";
import { api, type LiveDay, type MatchSummary, type RoundResult, type RoundSheetV2 } from "@/lib/api";
import { currentGame, isVersionConflict } from "@/lib/live";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { deriveRoundStep } from "@/lib/roundStep";
import { matchStatusLabel, useSession } from "@/lib/useSession";

const DAY_STATUSES = ["RegistrationClosed", "Drawn", "InProgress", "AwaitingClose", "Completed"];
const SYNC_MS = 10_000;

type Tab = "live" | "check" | "close";

/**
 * Súmula do dia da pelada em 3 momentos: partida ao vivo (beira de campo), checagem do dia inteiro
 * e fim da pelada com Melhor Jogador e Melhor Goleiro. Cada alteração grava no servidor com a versão lida;
 * se outro aparelho mudou antes, a tela recarrega e avisa.
 */
export default function RegistroPage() {
  const { group, isAdmin } = useSession();
  const [matches, setMatches] = useState<MatchSummary[] | null>(null);
  const [matchId, setMatchId] = useState("");
  const [day, setDay] = useState<LiveDay | null>(null);
  const [round, setRound] = useState<RoundResult | null>(null);
  const [loadingDay, setLoadingDay] = useState(false);
  const [dayError, setDayError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [tab, setTab] = useState<Tab>("live");
  const dayRef = useRef<LiveDay | null>(null);
  const busyRef = useRef(false);
  dayRef.current = day;
  busyRef.current = busy;

  useEffect(() => {
    if (!group || !isAdmin) return;
    api
      .listMatches(group.id)
      .then((list) => {
        const relevant = list.filter((m) => DAY_STATUSES.includes(m.status)).sort((a, b) => b.startsAt.localeCompare(a.startsAt));
        setMatches(relevant);
        const active = relevant.find((m) => m.status !== "Completed");
        setMatchId((cur) => cur || active?.id || relevant[0]?.id || "");
      })
      .catch((e) => {
        setError(e instanceof Error ? e.message : "Erro ao carregar partidas.");
        setMatches([]);
      });
  }, [group, isAdmin]);

  const load = useCallback(async (id: string, keepTab = false) => {
    setLoadingDay(true);
    setDayError(null);
    try {
      const [d, r] = await Promise.all([api.getLive(id).catch((e: unknown) => e), api.getRound(id).catch(() => null)]);
      setRound(r);
      if (d instanceof Error) {
        setDay(null);
        setDayError(d.message);
      } else {
        const live = d as LiveDay;
        setDay(live);
        if (!keepTab) setTab(live.matchStatus === "Completed" ? "check" : "live");
      }
    } finally {
      setLoadingDay(false);
    }
  }, []);

  useEffect(() => {
    if (!matchId) return;
    setDay(null);
    setNotice(null);
    setError(null);
    load(matchId).catch(() => undefined);
  }, [matchId, load]);

  // Sincroniza com outro aparelho a cada 10 s (dia em andamento, aba visível, sem gravação em curso).
  useEffect(() => {
    if (!matchId || !day || day.matchStatus === "Completed") return;
    const t = window.setInterval(async () => {
      if (busyRef.current || document.visibilityState === "hidden") return;
      try {
        const fresh = await api.getLive(matchId);
        if (fresh.version !== dayRef.current?.version || fresh.matchStatus !== dayRef.current?.matchStatus) setDay(fresh);
      } catch {
        /* rede instável: tenta no próximo ciclo */
      }
    }, SYNC_MS);
    return () => window.clearInterval(t);
  }, [matchId, day?.matchStatus, day]);

  const mutate: Mutate = useCallback(
    async (fn, success, opts) => {
      const current = dayRef.current;
      if (!current) return false;
      setBusy(true);
      setError(null);
      setNotice(null);
      try {
        setDay(await fn(current.version, current));
        if (success) setNotice(success);
        return true;
      } catch (e) {
        if (isVersionConflict(e)) {
          const fresh = await api.getLive(current.matchId).catch(() => null);
          if (fresh) setDay(fresh);
          if (fresh && opts?.retryOnConflict) {
            try {
              setDay(await fn(fresh.version, fresh));
              if (success) setNotice(success);
              return true;
            } catch (e2) {
              setError(e2 instanceof Error ? e2.message : "Erro ao salvar.");
              return false;
            }
          }
          setError("Outro aparelho alterou a súmula ao mesmo tempo. Atualizamos a tela: confira e repita a ação.");
        } else {
          setError(e instanceof Error ? e.message : "Erro ao salvar.");
          const fresh = await api.getLive(current.matchId).catch(() => null);
          if (fresh) setDay(fresh);
        }
        return false;
      } finally {
        setBusy(false);
      }
    },
    [],
  );

  async function closeDay(body: CloseDayBody) {
    if (!day) return false;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await api.closeLiveDay(day.matchId, body);
      setMatches((list) => (list ?? []).map((m) => (m.id === day.matchId ? { ...m, status: "Completed" } : m)));
      await load(day.matchId, true);
      setNotice("Pelada do dia concluída. Rankings e perfis já contam este dia.");
      return true;
    } catch (e) {
      setError(isVersionConflict(e) ? "A súmula mudou em outro aparelho. Atualizamos a tela: confira e conclua de novo." : e instanceof Error ? e.message : "Erro ao concluir.");
      await load(day.matchId, true).catch(() => undefined);
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function correct(sheet: RoundSheetV2) {
    if (!day) return false;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const saved = await api.correctRoundV2(day.matchId, sheet);
      await load(day.matchId, true);
      setNotice(`Correção salva (revisão ${saved.revision}). A anterior fica no histórico.`);
      return true;
    } catch (e) {
      setError(e instanceof Error ? e.message : "Erro ao salvar a correção.");
      return false;
    } finally {
      setBusy(false);
    }
  }

  const selected = matches?.find((m) => m.id === matchId);
  const step = selected ? deriveRoundStep({ status: day?.matchStatus ?? selected.status, confirmedCount: day?.players.length ?? 0, draw: null }) : null;
  const live = day ? currentGame(day) : null;

  return (
    <AdminShell
      moduleId="registro"
      title="Súmula do dia"
      description="Partidas ao vivo jogo a jogo, checagem do dia inteiro e, no fim da pelada, Melhor Jogador e Melhor Goleiro."
    >
      {/* Sem CTA aqui: nesta tela a ação é a própria aba Ao vivo (iniciar a #1 já põe o dia em andamento). */}
      {step ? <RoundStepper state={{ ...step, cta: null }} compact /> : null}

      {matches === null ? (
        <AdminLoading label="Carregando dias de pelada…" />
      ) : (
        <label className="flex flex-col gap-1 text-label-md">
          Dia da pelada
          <select className={`${inputClass} w-full max-w-md`} value={matchId} onChange={(e) => setMatchId(e.target.value)}>
            {matches.length === 0 ? <option value="">Nenhum dia com sorteio</option> : null}
            {matches.map((m) => (
              <option key={m.id} value={m.id}>
                {new Date(m.startsAt).toLocaleDateString("pt-BR", { timeZone: group?.timeZoneId })} · {m.title || "Partida"} · {matchStatusLabel(m.status)}
              </option>
            ))}
          </select>
        </label>
      )}

      {error ? <AdminError>{error}</AdminError> : null}
      {notice ? <AdminNotice>{notice}</AdminNotice> : null}

      {matches && matches.length === 0 ? (
        <AdminEmpty>Nenhum dia de pelada com sorteio confirmado ainda.</AdminEmpty>
      ) : loadingDay && !day ? (
        <AdminLoading label="Carregando a súmula do dia…" />
      ) : round?.isLegacy ? (
        <section className="rounded-xl bg-surface-container-low p-md">
          <h2 className="text-headline-md">Rodada antiga (Time A/B)</h2>
          <p className="mt-1 text-body-md">
            Placar {round.legacyHomeScore} x {round.legacyAwayScore}. Cores, goleiros e gols não eram registrados e ficam como indisponíveis.
          </p>
          {round.bestPlayerName ? <p className="text-body-md">Craque: {playerDisplayName(round.bestPlayerName)}</p> : null}
        </section>
      ) : !day ? (
        dayError ? (
          <AdminEmpty action={<button type="button" className="min-h-11 text-label-md underline" onClick={() => load(matchId)}>Tentar de novo</button>}>
            {dayError}
          </AdminEmpty>
        ) : null
      ) : (
        <AdminTabs
          label="Momentos do dia"
          value={tab}
          onChange={setTab}
          tabs={[
            { id: "live", label: "Ao vivo", badge: live ? `(#${live.order})` : undefined },
            { id: "check", label: "Checagem", badge: `(${day.games.length})` },
            { id: "close", label: "Fim & melhores" },
          ]}
        >
          {tab === "live" ? (
            <LiveTab day={day} busy={busy} mutate={mutate} onGoToCheck={() => setTab("check")} />
          ) : tab === "check" ? (
            <DayCheckTab key={`${day.matchId}-${round?.revision ?? 0}`} day={day} round={round} busy={busy} mutate={mutate} onCorrect={correct} />
          ) : (
            <CloseDayTab day={day} round={round} busy={busy} mutate={mutate} onClose={closeDay} />
          )}
        </AdminTabs>
      )}

      {round && !round.isLegacy && round.revisions.length > 0 ? (
        <details className="rounded-lg border border-outline-variant bg-surface-container-lowest">
          <summary className="flex min-h-11 cursor-pointer items-center px-md text-label-md">Histórico de revisões ({round.revisions.length})</summary>
          <ol className="flex flex-col gap-1 px-md pb-md text-body-md">
            {[...round.revisions]
              .sort((a, b) => b.revision - a.revision)
              .map((r) => (
                <li key={r.revision} className="border-b border-outline-variant py-2 last:border-0">
                  <strong>Revisão {r.revision}</strong>
                  {r.isCurrent ? " (vigente)" : ""} · {new Date(r.createdAt).toLocaleString("pt-BR")} · {r.reason ? `Motivo: ${r.reason}` : "Fechamento original"}
                </li>
              ))}
          </ol>
        </details>
      ) : null}
    </AdminShell>
  );
}
