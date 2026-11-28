"use client";

import { useId, type ReactNode } from "react";
import { TabRail } from "@/components/TabRail";

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
  return (
    <div className="flex flex-col gap-md">
      {/* Uma fileira só; rola na horizontal com sinal na borda; abas com a mesma altura (regra 27/11). */}
      <TabRail label={label} tabs={tabs} value={value} onChange={onChange} idFor={(id) => `${base}-tab-${id}`} controls={`${base}-panel`} />
      <div id={`${base}-panel`} role="tabpanel" aria-labelledby={`${base}-tab-${value}`} tabIndex={0} className="focus-visible:outline-none">
        {children}
      </div>
    </div>
  );
}
