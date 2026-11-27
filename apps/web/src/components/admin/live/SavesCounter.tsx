"use client";

import type { LivePlayer } from "@/lib/api";
import { MAX_SAVES_PER_GAME } from "@/lib/live";
import { playerDisplayName } from "@/lib/playerDisplayName";
import { focusRing } from "@/components/admin/ui";

/** 🧤 Defesas de pênalti por goleiro do jogo, com − / + de 44 px. */
export function SavesCounter({
  keepers,
  saves,
  onChange,
  onAdjust,
  disabled,
  colorOf,
  clamp,
}: {
  keepers: LivePlayer[];
  saves: Record<string, number>;
  /** Rascunho local (edição): recebe um atualizador, para toques rápidos seguidos somarem todos. */
  onChange?: (update: (prev: Record<string, number>) => Record<string, number>) => void;
  /** Ao vivo: cada toque grava no servidor na hora (o valor exibido vem da súmula salva). */
  onAdjust?: (goalkeeperProfileId: string, delta: number) => void;
  disabled?: boolean;
  colorOf: (teamId: string) => string;
  clamp: (n: number) => number;
}) {
  const adjust = (id: string, delta: number) => {
    if (onAdjust) onAdjust(id, delta);
    else onChange?.((prev) => ({ ...prev, [id]: clamp((prev[id] ?? 0) + delta) }));
  };
  const btn = `flex h-14 w-14 items-center justify-center rounded-full border-2 border-outline text-headline-md disabled:opacity-40 ${focusRing}`;
  return (
    <fieldset className="rounded-lg border border-outline-variant p-sm">
      <legend className="px-1 text-label-md font-semibold">🧤 Goleiro defendeu pênalti?</legend>
      {keepers.length === 0 ? (
        <p className="text-body-md text-on-surface-variant">Nenhum goleiro marcado nessas cores. Ajuste “atuou no gol” na presença.</p>
      ) : (
        <ul className="flex flex-col gap-1">
          {keepers.map((k) => {
            const n = saves[k.playerProfileId] ?? 0;
            const name = playerDisplayName(k.displayName);
            return (
              <li key={k.playerProfileId} className="flex items-center justify-between gap-2">
                <span className="text-body-md">
                  {name} <span className="text-label-md text-on-surface-variant">({colorOf(k.teamId)})</span>
                </span>
                <span className="flex items-center gap-1">
                  <button type="button" className={btn} aria-label={`Tirar uma defesa de pênalti de ${name}`} disabled={disabled || n <= 0} onClick={() => adjust(k.playerProfileId, -1)}>
                    −
                  </button>
                  <span className="w-8 text-center text-headline-md tabular-nums" aria-live="polite" aria-label={`${n} defesas de ${name}`}>
                    {n}
                  </span>
                  <button
                    type="button"
                    className={`${btn} border-pitch-green-container text-pitch-green-container`}
                    aria-label={`Mais uma defesa de pênalti de ${name}`}
                    disabled={disabled || n >= MAX_SAVES_PER_GAME}
                    onClick={() => adjust(k.playerProfileId, 1)}
                  >
                    +
                  </button>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </fieldset>
  );
}
