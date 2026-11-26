"use client";

import { useId } from "react";
import { clampScore, MAX_SCORE } from "@/lib/round";
import { focusRing } from "./ui";

/** Placar com botões grandes − / + (≥ 44 px) ao lado do campo numérico; teclado: setas no campo também ajustam. */
export function ScoreStepper({
  label,
  ariaContext,
  value,
  onChange,
  disabled,
}: {
  /** Rótulo visível (ex.: "Vermelho"). */
  label: string;
  /** Contexto para leitor de tela (ex.: "jogo 1, gols do Vermelho"). */
  ariaContext: string;
  value: number;
  onChange: (value: number) => void;
  disabled?: boolean;
}) {
  const id = useId();
  const btn = `flex h-12 w-12 shrink-0 items-center justify-center rounded-full border-2 border-outline text-headline-md disabled:opacity-40 ${focusRing}`;
  return (
    <div className="flex flex-col items-center gap-1">
      <label htmlFor={id} className="text-label-md">
        {label}
      </label>
      <div className="flex items-center gap-1">
        <button
          type="button"
          aria-label={`Diminuir ${ariaContext}`}
          disabled={disabled || value <= 0}
          onClick={() => onChange(clampScore(value - 1))}
          className={btn}
        >
          −
        </button>
        <input
          id={id}
          type="number"
          min={0}
          max={MAX_SCORE}
          inputMode="numeric"
          aria-label={ariaContext}
          disabled={disabled}
          value={value}
          onChange={(e) => onChange(clampScore(Number(e.target.value)))}
          className={`h-12 w-14 rounded-lg border border-outline-variant text-center text-headline-md ${focusRing}`}
        />
        <button
          type="button"
          aria-label={`Aumentar ${ariaContext}`}
          disabled={disabled || value >= MAX_SCORE}
          onClick={() => onChange(clampScore(value + 1))}
          className={`${btn} border-pitch-green-container text-pitch-green-container`}
        >
          +
        </button>
      </div>
    </div>
  );
}
