"use client";

import { useEffect, useRef, useState } from "react";
import { scoreDirection } from "@/lib/motion";

/**
 * Acompanha a troca do placar: devolve a direção da última mudança e um contador que muda a cada troca
 * (usado como `key` para reiniciar a animação). O número em si muda na hora; a animação só acompanha.
 */
export function useScoreChange(value: number) {
  const prev = useRef<number | undefined>(undefined);
  const [change, setChange] = useState<{ dir: "up" | "down" | null; tick: number }>({ dir: null, tick: 0 });
  useEffect(() => {
    const dir = scoreDirection(prev.current, value);
    prev.current = value;
    if (dir) setChange((c) => ({ dir, tick: c.tick + 1 }));
  }, [value]);
  return change;
}

/**
 * Número do placar: o novo entra de baixo no gol e de cima quando o gol é desfeito
 * (duration-base + ease-emphasis, ≤ 300 ms). Com menos movimento, o número só troca.
 */
export function AnimatedScore({ value, dir, tick, label }: { value: number; dir: "up" | "down" | null; tick: number; label: string }) {
  return (
    <p className="overflow-hidden text-display tabular-nums" aria-live="polite" aria-label={label}>
      <span key={tick} className={`inline-block ${dir === "up" ? "animate-score-in" : dir === "down" ? "animate-score-in-down" : ""}`}>
        {value}
      </span>
    </p>
  );
}
