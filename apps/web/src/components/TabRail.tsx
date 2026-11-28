"use client";

import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { focusRing } from "@/components/admin/ui";
import { overflowMask, useOverflowHint } from "./useOverflowHint";

export type RailTab<T extends string> = { id: T; label: string; badge?: ReactNode };

/**
 * Trilho de abas (regra 27/11): sempre UMA fileira; com muitas abas rola na horizontal e o degradê na borda
 * mostra que há mais. O texto pode quebrar em duas linhas, mas todas as abas têm a mesma altura e o texto fica
 * centralizado. O fundo da aba ativa (elevation-1) desliza até a nova em duration-base.
 * Teclado WAI-ARIA: setas, Home e End movem o foco e ativam a aba.
 */
export function TabRail<T extends string>({
  label,
  tabs,
  value,
  onChange,
  idFor,
  controls,
}: {
  label: string;
  tabs: RailTab<T>[];
  value: T;
  onChange: (value: T) => void;
  /** id de cada aba (para aria-labelledby do painel). */
  idFor?: (id: T) => string;
  /** id do painel controlado. */
  controls?: string;
}) {
  const refs = useRef<Record<string, HTMLButtonElement | null>>({});
  const { ref, hint, update } = useOverflowHint<HTMLDivElement>();
  const [indicator, setIndicator] = useState<{ left: number; width: number; top: number; height: number } | null>(null);

  // Mede a aba ativa para posicionar o fundo deslizante; mantém a aba ativa visível no trilho.
  useLayoutEffect(() => {
    const el = refs.current[value];
    if (!el) return;
    setIndicator({ left: el.offsetLeft, width: el.offsetWidth, top: el.offsetTop, height: el.offsetHeight });
    el.scrollIntoView?.({ block: "nearest", inline: "nearest" });
    update();
  }, [value, tabs.length, update]);

  // Remede quando o trilho ou as abas mudam de tamanho (fonte maior, rotação, rótulo com contador novo).
  useEffect(() => {
    const rail = ref.current;
    if (!rail || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(() => {
      const el = refs.current[value];
      if (!el) return;
      setIndicator((cur) => {
        const next = { left: el.offsetLeft, width: el.offsetWidth, top: el.offsetTop, height: el.offsetHeight };
        return cur && cur.left === next.left && cur.width === next.width && cur.top === next.top && cur.height === next.height ? cur : next;
      });
    });
    ro.observe(rail);
    for (const el of Object.values(refs.current)) if (el) ro.observe(el);
    return () => ro.disconnect();
  }, [value, tabs.length, ref]);

  function onKeyDown(e: KeyboardEvent<HTMLButtonElement>, index: number) {
    let next = -1;
    if (e.key === "ArrowRight") next = (index + 1) % tabs.length;
    else if (e.key === "ArrowLeft") next = (index - 1 + tabs.length) % tabs.length;
    else if (e.key === "Home") next = 0;
    else if (e.key === "End") next = tabs.length - 1;
    if (next < 0) return;
    e.preventDefault();
    onChange(tabs[next].id);
    refs.current[tabs[next].id]?.focus();
  }

  const mask = overflowMask(hint);
  return (
    <div
      ref={ref}
      role="tablist"
      aria-label={label}
      className="relative flex items-stretch gap-1 overflow-x-auto rounded-full bg-surface-container p-1 [scrollbar-width:none]"
      style={mask ? { maskImage: mask, WebkitMaskImage: mask } : undefined}
    >
      {indicator ? (
        <span
          aria-hidden="true"
          className="pointer-events-none absolute rounded-full bg-card shadow-elevation-1 transition-[left,width,top,height] duration-base ease-standard"
          style={indicator}
        />
      ) : null}
      {tabs.map((t, i) => {
        const active = t.id === value;
        return (
          <button
            key={t.id}
            ref={(el) => {
              refs.current[t.id] = el;
            }}
            id={idFor?.(t.id)}
            role="tab"
            type="button"
            aria-selected={active}
            aria-controls={controls}
            tabIndex={active ? 0 : -1}
            onClick={() => onChange(t.id)}
            onKeyDown={(e) => onKeyDown(e, i)}
            className={`relative z-10 flex min-h-11 min-w-[6.5rem] flex-1 shrink-0 basis-0 items-center justify-center rounded-full px-3 py-1 text-center text-label-md leading-tight transition-colors duration-base ease-standard ${
              active ? "text-on-surface" : "text-on-surface-variant"
            } ${indicator ? "" : active ? "bg-card shadow-elevation-1" : ""} ${focusRing}`}
          >
            <span>
              {t.label}
              {t.badge !== undefined ? <span className="ml-1">{t.badge}</span> : null}
            </span>
          </button>
        );
      })}
    </div>
  );
}
