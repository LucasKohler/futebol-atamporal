"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/**
 * Diz se um contêiner rolável na horizontal tem conteúdo escondido à esquerda/direita, para mostrar o sinal
 * de "tem mais" na borda (regra de abas e tabelas da revisão 27/11). Atualiza ao rolar e ao redimensionar.
 */
export function useOverflowHint<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [hint, setHint] = useState({ start: false, end: false });

  const update = useCallback(() => {
    const el = ref.current;
    if (!el) return;
    const max = el.scrollWidth - el.clientWidth;
    const next = { start: el.scrollLeft > 1, end: max - el.scrollLeft > 1 };
    setHint((h) => (h.start === next.start && h.end === next.end ? h : next));
  }, []);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    update();
    el.addEventListener("scroll", update, { passive: true });
    const ro = typeof ResizeObserver !== "undefined" ? new ResizeObserver(update) : null;
    ro?.observe(el);
    window.addEventListener("resize", update);
    return () => {
      el.removeEventListener("scroll", update);
      ro?.disconnect();
      window.removeEventListener("resize", update);
    };
  }, [update]);

  return { ref, hint, update };
}

export { overflowMask } from "@/lib/layoutRules";
