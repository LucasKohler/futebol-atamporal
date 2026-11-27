"use client";

import { useState } from "react";
import type { MatchSummary } from "@/lib/api";
import { isLineupFrozen } from "@/lib/teams";
import { AdminConfirmButton } from "./AdminDrawer";
import { focusRing, inputClass, primaryButton, secondaryButton } from "./ui";

function toLocalInput(iso: string) {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/**
 * Controles da partida na Central do admin (antes ficavam na tela Partida).
 * A etapa e o próximo passo seguem no RoundStepper da Central; aqui ficam as transições de apoio e a edição dos dados.
 */
export function MatchControls({
  match,
  busy,
  onTransition,
  onUpdate,
}: {
  match: MatchSummary;
  busy: boolean;
  onTransition: (to: string, success: string) => void;
  onUpdate: (body: { title: string | null; startsAt: string; location: string; playerLimit: number; notes: string | null }) => void;
}) {
  const editable = match.status !== "Completed" && match.status !== "Cancelled";
  const frozen = isLineupFrozen(match.status);
  const hasTransitions = match.status === "RegistrationOpen" || match.status === "RegistrationClosed" || match.status === "InProgress";
  if (!editable && !hasTransitions) return null;

  return (
    <section aria-labelledby="controles-partida-titulo" className="flex flex-col gap-sm">
      <h3 id="controles-partida-titulo" className="text-subtitle">
        Controles da partida
      </h3>
      {hasTransitions ? (
        <div className="flex flex-wrap gap-2">
          {match.status === "RegistrationOpen" ? (
            <AdminConfirmButton
              tone="neutral"
              label="Encerrar inscrições"
              question="Ninguém mais entra ou sai pela lista. Encerrar?"
              confirmLabel="Encerrar inscrições"
              disabled={busy}
              onConfirm={() => onTransition("RegistrationClosed", "Inscrições encerradas.")}
            />
          ) : null}
          {match.status === "RegistrationClosed" ? (
            <button type="button" disabled={busy} className={secondaryButton} onClick={() => onTransition("RegistrationOpen", "Inscrições reabertas.")}>
              Reabrir inscrições
            </button>
          ) : null}
          {match.status === "InProgress" ? (
            <button type="button" disabled={busy} className={secondaryButton} onClick={() => onTransition("AwaitingClose", "Jogo encerrado. Falta lançar a súmula.")}>
              Encerrar jogo (aguardar súmula)
            </button>
          ) : null}
        </div>
      ) : null}
      {editable ? <EditMatchDetails key={match.id} match={match} frozen={frozen} busy={busy} onSave={onUpdate} /> : null}
    </section>
  );
}

function EditMatchDetails({
  match,
  frozen,
  busy,
  onSave,
}: {
  match: MatchSummary;
  frozen: boolean;
  busy: boolean;
  onSave: (body: { title: string | null; startsAt: string; location: string; playerLimit: number; notes: string | null }) => void;
}) {
  const [title, setTitle] = useState(match.title ?? "");
  const [when, setWhen] = useState(() => toLocalInput(match.startsAt));
  const [location, setLocation] = useState(match.location);
  const [limit, setLimit] = useState(match.playerLimit);
  const [notes, setNotes] = useState(match.notes ?? "");

  return (
    <details className="rounded-lg border border-line-subtle bg-card">
      <summary className={`flex min-h-11 cursor-pointer items-center px-md text-label-md ${focusRing}`}>Editar dados da partida</summary>
      <form
        className="flex flex-col gap-md px-md pb-md"
        onSubmit={(e) => {
          e.preventDefault();
          onSave({ title: title.trim() || null, startsAt: new Date(when).toISOString(), location: location.trim(), playerLimit: limit, notes: notes.trim() || null });
        }}
      >
        <label className="flex flex-col gap-1 text-label-md">
          Título
          <input className={inputClass} value={title} maxLength={120} onChange={(e) => setTitle(e.target.value)} />
        </label>
        <div className="grid gap-md sm:grid-cols-2">
          <label className="flex flex-col gap-1 text-label-md">
            Data/hora
            <input type="datetime-local" required className={inputClass} value={when} onChange={(e) => setWhen(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1 text-label-md">
            Limite de jogadores
            <input
              type="number"
              min={2}
              max={60}
              required
              disabled={frozen}
              className={`${inputClass} disabled:opacity-50`}
              value={limit}
              onChange={(e) => setLimit(Number(e.target.value))}
            />
            {frozen ? <span className="font-normal text-on-surface-variant">Partida iniciada: o limite não muda mais.</span> : null}
          </label>
        </div>
        <label className="flex flex-col gap-1 text-label-md">
          Local
          <input required className={inputClass} value={location} maxLength={200} onChange={(e) => setLocation(e.target.value)} />
        </label>
        <label className="flex flex-col gap-1 text-label-md">
          Observações
          <textarea className={inputClass} rows={2} maxLength={1000} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </label>
        <button type="submit" disabled={busy} className={`${primaryButton} self-start`}>
          Salvar dados
        </button>
      </form>
    </details>
  );
}
