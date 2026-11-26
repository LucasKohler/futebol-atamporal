"use client";

import { useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { focusRing } from "./ui";

export type AdminTab<T extends string> = { id: T; label: string; badge?: ReactNode };

/**
 * Abas acessíveis (padrão WAI-ARIA): setas esquerda/direita, Home e End movem o foco e ativam a aba.
 * Renderiza só o painel ativo.
 */
export function AdminTabs<T extends string>({
  label,
  tabs,
  value,
  onChange,
  children,
}: {
  label: string;
  tabs: AdminTab<T>[];
  value: T;
  onChange: (value: T) => void;
  children: ReactNode;
}) {
  const base = useId();
  const refs = useRef<Record<string, HTMLButtonElement | null>>({});

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

  return (
    <div className="flex flex-col gap-md">
      <div role="tablist" aria-label={label} className="flex gap-1 overflow-x-auto rounded-full bg-surface-container p-1">
        {tabs.map((t, i) => {
          const active = t.id === value;
          return (
            <button
              key={t.id}
              ref={(el) => {
                refs.current[t.id] = el;
              }}
              id={`${base}-tab-${t.id}`}
              role="tab"
              type="button"
              aria-selected={active}
              aria-controls={`${base}-panel`}
              tabIndex={active ? 0 : -1}
              onClick={() => onChange(t.id)}
              onKeyDown={(e) => onKeyDown(e, i)}
              className={`min-h-11 flex-1 shrink-0 whitespace-nowrap rounded-full px-4 text-label-md ${
                active ? "bg-surface-container-lowest text-on-surface shadow-sm" : "text-on-surface-variant"
              } ${focusRing}`}
            >
              {t.label}
              {t.badge !== undefined ? <span className="ml-1">{t.badge}</span> : null}
            </button>
          );
        })}
      </div>
      <div id={`${base}-panel`} role="tabpanel" aria-labelledby={`${base}-tab-${value}`} tabIndex={0} className="focus-visible:outline-none">
        {children}
      </div>
    </div>
  );
}
