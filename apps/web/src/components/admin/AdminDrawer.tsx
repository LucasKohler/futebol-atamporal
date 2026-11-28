"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { shouldRestoreFocus } from "@/lib/focus";
import { AdminIcon } from "./AdminIcon";
import { dangerButton, focusRing, secondaryButton } from "./ui";

/**
 * Painel expansível sob demanda (edição de um item por vez sem abrir dezenas de formulários).
 * Controlado: o pai decide qual painel está aberto. Ao abrir, o foco vai para o conteúdo.
 */
export function AdminDrawer({
  summary,
  open,
  onToggle,
  toggleLabel,
  children,
}: {
  summary: ReactNode;
  open: boolean;
  onToggle: () => void;
  /** Texto do botão (ex.: "Editar Fulano"). */
  toggleLabel: { open: string; closed: string };
  children: ReactNode;
}) {
  const id = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(open);
  useEffect(() => {
    if (open) panelRef.current?.focus();
    else if (wasOpen.current && shouldRestoreFocus(document.activeElement, rootRef.current)) {
      // Fechou (botão interno, Cancelar ou salvar): o foco volta ao gatilho "Editar", não ao topo da página.
      triggerRef.current?.focus();
    }
    wasOpen.current = open;
  }, [open]);

  return (
    <div
      ref={rootRef}
      className={`rounded-xl border ${open ? "border-pitch-green-container bg-surface-container-lowest shadow-elevation-1" : "border-outline-variant bg-surface-container-lowest"}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2 px-md py-sm">
        <div className="min-w-0 flex-1">{summary}</div>
        <button ref={triggerRef} type="button" aria-expanded={open} aria-controls={id} onClick={onToggle} className={secondaryButton}>
          {open ? toggleLabel.open : toggleLabel.closed}
          <AdminIcon name="chevron" className={`h-4 w-4 transition-transform ${open ? "rotate-180" : ""}`} />
        </button>
      </div>
      {open ? (
        <div id={id} ref={panelRef} tabIndex={-1} className={`border-t border-outline-variant px-md py-md ${focusRing}`}>
          {children}
        </div>
      ) : null}
    </div>
  );
}

/**
 * Ação que exige confirmação em dois passos, sem diálogo nativo:
 * o primeiro toque mostra "Confirmar"/"Cancelar" no lugar; Esc cancela.
 */
export function AdminConfirmButton({
  label,
  confirmLabel,
  question,
  onConfirm,
  disabled,
  tone = "danger",
  className,
}: {
  label: string;
  confirmLabel: string;
  question: string;
  onConfirm: () => void;
  disabled?: boolean;
  tone?: "danger" | "neutral";
  className?: string;
}) {
  const [asking, setAsking] = useState(false);
  const confirmRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (asking) confirmRef.current?.focus();
  }, [asking]);

  if (!asking) {
    return (
      <button type="button" disabled={disabled} onClick={() => setAsking(true)} className={className ?? (tone === "danger" ? dangerButton : secondaryButton)}>
        {label}
      </button>
    );
  }
  return (
    <span
      role="group"
      aria-label={question}
      className="inline-flex flex-wrap items-center gap-2"
      onKeyDown={(e) => {
        if (e.key === "Escape") setAsking(false);
      }}
    >
      <span className="text-label-md">{question}</span>
      <button
        ref={confirmRef}
        type="button"
        disabled={disabled}
        onClick={() => {
          setAsking(false);
          onConfirm();
        }}
        className={tone === "danger" ? `${dangerButton} bg-error-container` : secondaryButton}
      >
        {confirmLabel}
      </button>
      <button type="button" onClick={() => setAsking(false)} className={secondaryButton}>
        Cancelar
      </button>
    </span>
  );
}
