"use client";

import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError, type GroupPlayer, type MatchDetail, type WaitlistAuditEvent, type WaitlistPlacement } from "@/lib/api";
import { formatRegistrationTime, windowInputValue, windowInstant } from "@/lib/registrationWindows";
import { inputClass, primaryButton, secondaryButton } from "./ui";

type Action = "windows" | "move" | "status" | "enroll";
const titles = {
  windows: "Configurar aberturas",
  move: "Reordenar fila",
  status: "Mover entre lista e fila",
  enroll: "Inscrever outro membro por exceção",
};
const eventLabels: Record<string, string> = {
  AdminEarlySelfJoin: "Autoinscrição antecipada",
  MatchRegistrationWindowsUpdated: "Aberturas alteradas",
  AdminEnrolledOutsideWindow: "Inscrição por exceção",
  WaitlistReordered: "Fila reordenada",
  RegistrationStatusAdjusted: "Situação da inscrição alterada",
};

export function RegistrationReasonDialog({ title, children, onCancel, onSubmit }: {
  title: string;
  children?: ReactNode;
  onCancel: () => void;
  onSubmit: (reason: string, data: FormData) => Promise<void>;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const element = dialog.current;
    const trigger = document.activeElement;
    element?.showModal();
    return () => {
      element?.close();
      if (trigger instanceof HTMLElement && trigger.isConnected) trigger.focus();
    };
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const data = new FormData(event.currentTarget);
    const reason = String(data.get("reason")).trim();
    if (reason.length < 5) {
      setError("Informe um motivo com pelo menos 5 caracteres.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await onSubmit(reason, data);
    } catch (error) {
      setError(error instanceof Error ? error.message : "Não foi possível salvar.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <dialog ref={dialog} aria-labelledby={titleId} className="m-auto max-h-[90dvh] w-[min(92vw,32rem)] overflow-auto rounded-xl border border-line-control bg-card p-lg text-fg backdrop:bg-black/50"
      onCancel={(event) => { event.preventDefault(); if (!busy) onCancel(); }}>
      <h2 id={titleId} className="text-headline-md">{title}</h2>
      <form onSubmit={submit} className="mt-md flex flex-col gap-md">
        {children}
        <label className="flex flex-col gap-1">
          Motivo (5 a 300 caracteres)
          <textarea name="reason" required minLength={5} maxLength={300} rows={3} className={inputClass} />
        </label>
        {error ? <p role="alert" className="text-danger">{error}</p> : null}
        <div className="flex flex-wrap gap-2">
          <button type="submit" className={primaryButton} disabled={busy}>{busy ? "Salvando…" : "Confirmar"}</button>
          <button type="button" className={secondaryButton} onClick={onCancel} disabled={busy}>Cancelar</button>
        </div>
      </form>
    </dialog>
  );
}

function PositionInput({ label, max, required = false }: { label: string; max: number; required?: boolean }) {
  return <label className="flex flex-col gap-1">{label}
    <input className={inputClass} name="position" type="number" min={1} max={max} required={required} />
  </label>;
}

function PlayerSelect({ players, name = "player", label, onChange }: {
  players: Pick<GroupPlayer, "playerProfileId" | "displayName">[];
  name?: string;
  label: string;
  onChange?: (id: string) => void;
}) {
  return <label className="flex flex-col gap-1">{label}
    <select name={name} required className={inputClass} defaultValue="" onChange={(event) => onChange?.(event.target.value)}>
      <option value="" disabled>Selecione</option>
      {players.map((player) => <option key={player.playerProfileId} value={player.playerProfileId}>{player.displayName}</option>)}
    </select>
  </label>;
}

export function RegistrationControls({ detail, ownId, locked, onChanged }: {
  detail: MatchDetail;
  ownId?: string | null;
  locked: boolean;
  onChanged: (detail: MatchDetail) => void;
}) {
  const [draft, setDraft] = useState<{ action: Action; version: number } | null>(null);
  const action = draft?.action;
  const [selection, setSelection] = useState("");
  const [placement, setPlacement] = useState<WaitlistPlacement>("Auto");
  const [players, setPlayers] = useState<GroupPlayer[]>([]);
  const [auditOpen, setAuditOpen] = useState(false);
  const [audit, setAudit] = useState<{ events: WaitlistAuditEvent[]; offset: number } | null>(null);
  const [auditBusy, setAuditBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [error, setError] = useState("");
  const { match, version, registrationWindows, groupTimeZoneId } = detail;
  const zone = groupTimeZoneId ?? "UTC";
  if (version === undefined || !registrationWindows) return null;

  const roster = [...detail.confirmed, ...detail.waitlist];
  const active = new Set(roster.map((r) => r.playerProfileId));
  const eligiblePlayers = players.filter((p) => p.playerProfileId !== ownId && !active.has(p.playerProfileId));
  const selected = roster.find((p) => p.playerProfileId === selection);
  const full = match.spotsRemaining === 0;
  const pageSize = 20;

  async function loadAudit(offset: number) {
    setAuditBusy(true);
    try {
      setAudit({ events: await api.waitlistAudit(match.id, pageSize, offset), offset });
    } catch (error) {
      setError(error instanceof Error ? error.message : "Não foi possível carregar o histórico.");
    } finally {
      setAuditBusy(false);
    }
  }

  async function open(action: Action) {
    if (version === undefined) return;
    setError("");
    setNotice("");
    setSelection("");
    setPlacement("Auto");
    try {
      if (action === "enroll") setPlayers(await api.listPlayers(match.groupId));
      setDraft({ action, version });
    } catch (error) {
      setError(error instanceof Error ? error.message : "Não foi possível carregar os membros.");
    }
  }

  async function save(reason: string, data: FormData) {
    if (!draft) return;
    const base = { reason, expectedVersion: draft.version };
    const playerProfileId = String(data.get("player") ?? "");
    const pos = data.get("position");
    const position = pos ? Number(pos) : undefined;
    const demotePlayerProfileId = String(data.get("demote") || "") || undefined;
    try {
      let updated: MatchDetail;
      switch (action) {
        case "windows":
          updated = await api.updateRegistrationWindows(match.id, {
            ...base,
            monthlyOpensAt: windowInstant(String(data.get("monthly")), zone),
            casualOpensAt: windowInstant(String(data.get("casual")), zone),
          });
          break;
        case "move":
          updated = await api.moveWaitlist(match.id, { ...base, playerProfileId, targetPosition: Number(pos) });
          break;
        case "status":
          updated = await api.adjustRegistrationStatus(match.id, playerProfileId, {
            ...base, targetStatus: selected?.status === "Confirmed" ? "Waitlisted" : "Confirmed",
            targetWaitlistPosition: selected?.status === "Confirmed" ? position : undefined,
            promoteNext: data.has("promoteNext"), demotePlayerProfileId,
            demoteToPosition: selected?.status === "Waitlisted" ? position : undefined,
          });
          break;
        case "enroll":
          updated = await api.adminEnroll(match.id, { ...base, playerProfileId, placement, targetWaitlistPosition: position, demotePlayerProfileId });
          break;
        default:
          return;
      }
      onChanged(updated);
      setDraft(null);
      setNotice("Alteração registrada no histórico.");
      if (auditOpen) await loadAudit(0);
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 409) throw error;
      const fresh = await api.getMatch(match.id);
      onChanged(fresh);
      setDraft(null);
      setError("A lista mudou. Revise os dados atualizados e abra a ação novamente para confirmar.");
    }
  }

  return (
    <section aria-label="Administração das inscrições" className="rounded-xl border border-line-subtle bg-card p-md">
      <h3 className="text-headline-sm">Administração das inscrições</h3>
      {!locked ? <div className="mt-sm flex flex-wrap gap-2">
        <button type="button" className={secondaryButton} onClick={() => void open("windows")}>{titles.windows}</button>
        <button type="button" className={secondaryButton} disabled={detail.waitlist.length < 2} onClick={() => void open("move")}>{titles.move}</button>
        <button type="button" className={secondaryButton} disabled={!roster.length} onClick={() => void open("status")}>{titles.status}</button>
        <button type="button" className={secondaryButton} onClick={() => void open("enroll")}>{titles.enroll}</button>
      </div> : null}
      <button type="button" className={`${secondaryButton} mt-sm`} aria-expanded={auditOpen} onClick={() => {
        setAuditOpen(!auditOpen);
        if (!auditOpen) void loadAudit(0);
      }}>{auditOpen ? "Ocultar histórico" : "Ver histórico de ajustes"}</button>
      {error ? <p role="alert" className="mt-sm text-danger">{error}</p> : null}
      {notice ? <p role="status" className="mt-sm">{notice}</p> : null}
      {auditOpen ? <div className="mt-sm" aria-busy={auditBusy}>
        {auditBusy ? <p role="status">Carregando histórico…</p> : null}
        {audit && audit.events.length === 0 ? <p>Nenhum ajuste registrado nesta página.</p> : null}
        {audit?.events.map((event) => <AuditLine key={event.id} event={event} zone={zone} names={[...players, ...roster]} />)}
        {audit && (audit.offset > 0 || audit.events.length === pageSize) ? <div className="mt-sm flex gap-2">
          <button type="button" className={secondaryButton} disabled={auditBusy || audit.offset === 0} onClick={() => void loadAudit(audit.offset - pageSize)}>Anterior</button>
          <button type="button" className={secondaryButton} disabled={auditBusy || audit.events.length < pageSize} onClick={() => void loadAudit(audit.offset + pageSize)}>Próxima</button>
        </div> : null}
      </div> : null}
      {action ? <RegistrationReasonDialog title={titles[action]} onCancel={() => setDraft(null)} onSubmit={save}>
        {action === "windows" ? <>
          <p>Horário do grupo ({zone}). Deixe os dois campos vazios para desativar as janelas.</p>
          <label className="flex flex-col gap-1">Abertura para mensalistas
            <input className={inputClass} type="datetime-local" name="monthly" step={1} defaultValue={windowInputValue(registrationWindows.monthlyOpensAt, zone)} />
          </label>
          <label className="flex flex-col gap-1">Abertura geral
            <input className={inputClass} type="datetime-local" name="casual" step={1} defaultValue={windowInputValue(registrationWindows.casualOpensAt, zone)} />
          </label>
        </> : null}
        {action === "move" ? <>
          <PlayerSelect players={detail.waitlist.map((player) => ({ ...player, displayName: `${player.waitlistPosition}º · ${player.displayName}` }))} label="Pessoa na fila" />
          <PositionInput label="Nova posição" max={detail.waitlist.length} required />
        </> : null}
        {action === "status" ? <>
          <PlayerSelect players={roster.map((player) => ({ ...player, displayName: `${player.displayName} · ${player.status === "Confirmed" ? "Confirmado" : "Na fila"}` }))}
            label="Inscrição" onChange={setSelection} />
          {selected?.status === "Confirmed" ? <>
            <p>Mover para a fila de espera.</p>
            <PositionInput label="Posição na fila (vazio: fim)" max={detail.waitlist.length + 1} />
            <label><input name="promoteNext" type="checkbox" defaultChecked /> Promover a próxima pessoa da fila</label>
          </> : null}
          {selected?.status === "Waitlisted" ? <p>Promover para a lista de confirmados.</p> : null}
          {selected?.status === "Waitlisted" && full ? <>
            <PlayerSelect players={detail.confirmed} name="demote" label="Confirmado que irá para a fila" />
            <PositionInput label="Posição da pessoa na fila (vazio: fim)" max={detail.waitlist.length} />
          </> : null}
        </> : null}
        {action === "enroll" ? <>
          {eligiblePlayers.length === 0 ? <p>Todos os outros membros ativos já estão inscritos.</p> : null}
          <PlayerSelect players={eligiblePlayers} label="Membro do grupo" />
          <label className="flex flex-col gap-1">Colocação
            <select className={inputClass} value={placement} onChange={(event) => setPlacement(event.target.value as WaitlistPlacement)}>
              <option value="Auto">Automática</option><option value="Waitlist">Fila de espera</option><option value="Confirmed">Confirmado</option>
            </select>
          </label>
          {placement === "Waitlist" || placement === "Auto" && full
            ? <PositionInput label="Posição na fila (vazio: fim)" max={detail.waitlist.length + 1} /> : null}
          {placement === "Confirmed" && full ? <PlayerSelect players={detail.confirmed} name="demote" label="Confirmado que irá para a fila" /> : null}
        </> : null}
      </RegistrationReasonDialog> : null}
    </section>
  );
}

function AuditLine({ event, zone, names }: {
  event: WaitlistAuditEvent;
  zone: string;
  names: Pick<GroupPlayer, "playerProfileId" | "displayName">[];
}) {
  const name = (id: string) => names.find((player) => player.playerProfileId === id)?.displayName ?? id;
  const time = (value: string | null) => value ? formatRegistrationTime(value, zone) : "Sem janela";
  return <details className="border-b border-line-subtle py-2">
    <summary className="cursor-pointer">{eventLabels[event.action] ?? event.action} · {formatRegistrationTime(event.createdAt, zone)}</summary>
    <p>Motivo: {event.reason}</p>
    <p className="break-all">Admin: {event.actorUserId} · versão {event.version}</p>
    {[event.before, event.after].map((state, index) => <div key={index} className="mt-2 break-words">
      <p className="font-semibold">{index === 0 ? "Antes" : "Depois"}</p>
      <p>Mensalistas: {time(state.monthlyRegistrationOpensAt)} · Geral: {time(state.casualRegistrationOpensAt)}</p>
      <p>Confirmados: {state.confirmed.map(name).join(", ") || "Nenhum"}</p>
      <p>Fila: {state.waitlist.map((entry) => `${entry.waitlistPosition}º ${name(entry.playerProfileId)}`).join(", ") || "Vazia"}</p>
    </div>)}
  </details>;
}
