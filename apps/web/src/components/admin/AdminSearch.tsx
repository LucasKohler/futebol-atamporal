"use client";

import { useId } from "react";
import { AdminIcon } from "./AdminIcon";
import { focusRing } from "./ui";

/** Campo de busca com rótulo visível e botão de limpar. */
export function AdminSearch({
  label,
  value,
  onChange,
  placeholder,
  resultCount,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  /** Quando informado, anuncia a quantidade de resultados. */
  resultCount?: number;
}) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-label-md">
        {label}
      </label>
      <div className="relative">
        <AdminIcon name="search" className="pointer-events-none absolute left-3 top-1/2 h-5 w-5 -translate-y-1/2 text-on-surface-variant" />
        <input
          id={id}
          type="search"
          value={value}
          placeholder={placeholder}
          autoComplete="off"
          onChange={(e) => onChange(e.target.value)}
          className={`min-h-11 w-full rounded-full border border-outline-variant bg-surface-container-lowest py-2 pl-10 pr-24 text-body-md ${focusRing}`}
        />
        {value ? (
          <button
            type="button"
            onClick={() => onChange("")}
            className={`absolute right-1 top-1/2 min-h-11 -translate-y-1/2 rounded-full px-3 text-label-md underline ${focusRing}`}
          >
            Limpar
          </button>
        ) : null}
      </div>
      {resultCount !== undefined ? (
        <p aria-live="polite" className="text-label-md text-on-surface-variant">
          {resultCount === 1 ? "1 resultado" : `${resultCount} resultados`}
        </p>
      ) : null}
    </div>
  );
}

/** Chips de filtro de escolha única (botões com aria-pressed). */
export function AdminFilterChips<T extends string>({
  label,
  options,
  value,
  onChange,
  counts,
}: {
  label: string;
  options: { id: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
  counts?: Partial<Record<T, number>>;
}) {
  return (
    <div role="group" aria-label={label} className="flex gap-2 overflow-x-auto pb-1">
      {options.map((o) => {
        const active = o.id === value;
        const count = counts?.[o.id];
        return (
          <button
            key={o.id}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(o.id)}
            className={`min-h-11 shrink-0 rounded-full border px-4 text-label-md ${
              active ? "border-pitch-green-container bg-pitch-green-container text-on-primary" : "border-outline-variant bg-surface-container-lowest"
            } ${focusRing}`}
          >
            {o.label}
            {count !== undefined ? <span className="ml-1 opacity-80">({count})</span> : null}
          </button>
        );
      })}
    </div>
  );
}
