"use client";

import { useCallback, useRef } from "react";
import { pixReturnSelectors, resolveReturnTarget } from "@/lib/focus";

/**
 * Foco do checkout Pix como num drawer: `remember()` ao abrir guarda o gatilho;
 * `restore(key)` ao fechar devolve o foco a ele (ou ao botão/cartão re-renderizado com data-pix-trigger/data-pix-item).
 */
export function usePixFocusReturn() {
  const saved = useRef<HTMLElement | null>(null);

  const remember = useCallback(() => {
    const active = document.activeElement;
    saved.current = active instanceof HTMLElement && active !== document.body ? active : null;
  }, []);

  const restore = useCallback((key: string) => {
    // Espera o React aplicar o fechamento (o gatilho pode reaparecer só depois do render).
    window.setTimeout(() => {
      const candidates = pixReturnSelectors(key).map((s) => document.querySelector<HTMLElement>(s));
      resolveReturnTarget(saved.current, candidates)?.focus();
      saved.current = null;
    }, 0);
  }, []);

  return { remember, restore };
}
