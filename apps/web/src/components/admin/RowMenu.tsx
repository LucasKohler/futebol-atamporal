"use client";

import { ReactNode, useEffect, useId, useRef, useState } from "react";
import { focusRing } from "./ui";

/**
 * Menu "⋯" da ListRow (DS): ações secundárias e destrutivas da linha ficam aqui, fora da vista.
 * Disclosure simples (botão + lista), operável por teclado; Esc fecha e devolve o foco ao botão.
 * Ações destrutivas dentro do menu continuam usando AdminConfirmButton (confirmação).
 */
export function RowMenu({ label, children, disabled }: { label: string; children: ReactNode; disabled?: boolean }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  return (
    <div
      ref={rootRef}
      className="relative"
      onKeyDown={(e) => {
        if (e.key === "Escape" && open) {
          e.stopPropagation();
          setOpen(false);
          buttonRef.current?.focus();
        }
      }}
    >
      <button
        ref={buttonRef}
        type="button"
        disabled={disabled}
        aria-expanded={open}
        aria-controls={id}
        aria-label={label}
        onClick={() => setOpen((v) => !v)}
        className={`inline-flex min-h-touch min-w-touch items-center justify-center rounded-full border border-line-control bg-card text-headline-md leading-none text-fg hover:bg-subtle disabled:opacity-50 ${focusRing}`}
      >
        <span aria-hidden="true">⋯</span>
      </button>
      {open ? (
        <div
          id={id}
          className="absolute right-0 z-20 mt-1 flex min-w-[14rem] flex-col gap-1 rounded-card border border-line-subtle bg-card p-sm shadow-lg [&>*]:w-full [&_button]:w-full [&_button]:justify-start"
        >
          {children}
        </div>
      ) : null}
    </div>
  );
}
