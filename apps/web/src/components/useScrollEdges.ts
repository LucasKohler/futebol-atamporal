"use client";

import { useEffect, useState } from "react";
import { scrollEdges } from "@/lib/layoutRules";

/** Acompanha a rolagem da página: `scrolled` (há conteúdo sob a barra do topo) e `contentBelow` (sob a do rodapé). */
export function useScrollEdges() {
  const [edges, setEdges] = useState({ scrolled: false, contentBelow: false });
  useEffect(() => {
    const update = () => {
      const next = scrollEdges(window.scrollY, window.innerHeight, document.documentElement.scrollHeight);
      setEdges((e) => (e.scrolled === next.scrolled && e.contentBelow === next.contentBelow ? e : next));
    };
    update();
    window.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    const ro = typeof ResizeObserver !== "undefined" ? new ResizeObserver(update) : null;
    ro?.observe(document.body);
    return () => {
      window.removeEventListener("scroll", update);
      window.removeEventListener("resize", update);
      ro?.disconnect();
    };
  }, []);
  return edges;
}
