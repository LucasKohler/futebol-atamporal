"use client";

import { useState } from "react";
import { api, GroupPlayer, MonthlyMember } from "@/lib/api";
import { formatRating, validateRatings } from "@/lib/ratings";
import { AdminConfirmButton } from "./AdminDrawer";
import { inputClass, primaryButton, secondaryButton } from "./ui";

type Status = { error: boolean; text: string } | null;

function StatusLine({ status }: { status: Status }) {
  if (!status) return null;
  return (
    <p role={status.error ? "alert" : "status"} className={`text-label-md ${status.error ? "text-error" : "text-green-900"}`}>
      {status.text}
    </p>
  );
}

/**
 * Edição de um jogador (aberta sob demanda): apelido, notas de linha/goleiro e vínculo de mensalista.
 * Rascunhos ficam locais: "Cancelar" fecha sem salvar e descarta as alterações.
 */
export function PlayerEditor({
  groupId,
  player,
  membership,
  onPlayerSaved,
  onMembershipChanged,
  onClose,
}: {
  groupId: string;
  player: GroupPlayer;
  membership: MonthlyMember | undefined;
  onPlayerSaved: (updated: GroupPlayer) => void;
  onMembershipChanged: () => Promise<void>;
  onClose: () => void;
}) {
  const id = player.playerProfileId;
  const [nick, setNick] = useState(player.nickname ?? "");
  const [line, setLine] = useState(String(player.lineRating));
  const [canGk, setCanGk] = useState(player.canPlayGoalkeeper);
  const [gk, setGk] = useState(player.goalkeeperRating === null ? "" : String(player.goalkeeperRating));
  const [status, setStatus] = useState<Status>(null);
  const [busy, setBusy] = useState(false);

  const nickChanged = (nick.trim() || null) !== (player.nickname ?? null);
  const ratingsChanged =
    line !== String(player.lineRating) ||
    canGk !== player.canPlayGoalkeeper ||
    gk !== (player.goalkeeperRating === null ? "" : String(player.goalkeeperRating));

  async function save() {
    const ratings = validateRatings({ lineRating: line, canPlayGoalkeeper: canGk, goalkeeperRating: gk });
    if (!ratings.ok) {
      setStatus({ error: true, text: ratings.error });
      return;
    }
    if (!nickChanged && !ratingsChanged) {
      setStatus({ error: false, text: "Nada para salvar." });
      return;
    }
    setBusy(true);
    setStatus(null);
    try {
      let updated = player;
      if (nickChanged) updated = await api.setNickname(groupId, id, nick.trim() || null);
      if (ratingsChanged) updated = await api.setRatings(groupId, id, ratings.value);
      onPlayerSaved(updated);
      setStatus({
        error: false,
        text: `Salvo: linha ${formatRating(updated.lineRating)}, goleiro ${formatRating(updated.goalkeeperRating)}${updated.nickname ? `, apelido "${updated.nickname}"` : ""}.`,
      });
    } catch (e) {
      setStatus({ error: true, text: e instanceof Error ? e.message : "Erro ao salvar." });
    } finally {
      setBusy(false);
    }
  }

  async function membershipAction(action: () => Promise<unknown>, success: string) {
    setBusy(true);
    setStatus(null);
    try {
      await action();
      await onMembershipChanged();
      setStatus({ error: false, text: success });
    } catch (e) {
      setStatus({ error: true, text: e instanceof Error ? e.message : "Erro ao atualizar o vínculo." });
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-md">
      <form
        aria-label={`Editar ${player.registeredName}`}
        className="flex flex-col gap-md"
        onSubmit={(e) => {
          e.preventDefault();
          save();
        }}
      >
        <label className="flex flex-col gap-1 text-label-md" htmlFor={`apelido-${id}`}>
          Apelido fixo (opcional)
          <input
            id={`apelido-${id}`}
            className={inputClass}
            placeholder="Sem apelido: usa o nome cadastrado"
            maxLength={40}
            value={nick}
            onChange={(e) => setNick(e.target.value)}
          />
        </label>

        <fieldset className="flex flex-wrap items-end gap-md">
          <legend className="mb-1 text-label-md">Notas (0 a 10)</legend>
          <label className="flex flex-col gap-1 text-label-md" htmlFor={`linha-${id}`}>
            Linha
            <input id={`linha-${id}`} inputMode="decimal" className={`${inputClass} w-24`} value={line} onChange={(e) => setLine(e.target.value)} />
          </label>
          <label className="flex min-h-11 items-center gap-2 text-label-md">
            <input
              type="checkbox"
              className="h-5 w-5"
              checked={canGk}
              onChange={(e) => {
                setCanGk(e.target.checked);
                if (!e.target.checked) setGk("");
              }}
            />
            Apto para o gol
          </label>
          <label className="flex flex-col gap-1 text-label-md" htmlFor={`gol-${id}`}>
            Goleiro
            <input
              id={`gol-${id}`}
              inputMode="decimal"
              placeholder="sem nota"
              disabled={!canGk}
              className={`${inputClass} w-24 disabled:opacity-50`}
              value={gk}
              onChange={(e) => setGk(e.target.value)}
            />
          </label>
        </fieldset>

        <div className="flex flex-wrap gap-2">
          <button type="submit" disabled={busy} className={primaryButton}>
            Salvar alterações
          </button>
          <button type="button" onClick={onClose} className={secondaryButton}>
            {nickChanged || ratingsChanged ? "Cancelar" : "Fechar"}
          </button>
        </div>
      </form>

      <div className="flex flex-col gap-2 border-t border-outline-variant pt-md">
        <p className="text-label-md">Vínculo de mensalista</p>
        {player.isMonthlyMember && membership ? (
          <AdminConfirmButton
            label="Desativar mensalista"
            question={`Desativar o vínculo de ${player.registeredName}?`}
            confirmLabel="Desativar"
            disabled={busy}
            onConfirm={() =>
              membershipAction(() => api.setMemberStatus(groupId, membership.id, "Inactive"), "Vínculo desativado. A prioridade na fila deixa de valer.")
            }
          />
        ) : player.isMonthlyMember ? (
          <p className="text-body-md text-on-surface-variant">Mensalista ativo.</p>
        ) : (
          <button
            type="button"
            disabled={busy}
            className={secondaryButton}
            onClick={() =>
              membershipAction(
                () => (membership ? api.setMemberStatus(groupId, membership.id, "Active") : api.addMember(groupId, id)),
                "Agora é mensalista. Confira a cobrança em Mensalidades.",
              )
            }
          >
            {membership ? "Reativar mensalista" : "Tornar mensalista"}
          </button>
        )}
      </div>

      <StatusLine status={status} />
    </div>
  );
}
