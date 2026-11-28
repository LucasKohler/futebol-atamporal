"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { focusRing, secondaryButton } from "@/components/admin/ui";
import { shouldCloseOnDrag } from "@/lib/layoutRules";

/**
 * Folha inferior (07d): `elevation-sheet` sobre o `scrim`. Sobe do rodapé (duration-slow + ease-enter) e o véu
 * entra em fade. Fecha só por ação do usuário: botão Fechar, Esc, toque no véu ou arrasto para baixo pela alça.
 * Nunca fecha sozinha (decisão 27/11: a confirmação do Pix fica aberta até a pessoa agir).
 * Diálogo modal: foco preso dentro e devolvido pelo chamador em `onClose`.
 */
export function Sheet({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const panelRef = useRef<HTMLDivElement>(null);
  const dragStart = useRef<number | null>(null);
  const [dragY, setDragY] = useState(0);
  const [mounted, setMounted] = useState(false);
  // Chamadores passam funções novas a cada render: o efeito usa a última sem reexecutar (não rouba o foco).
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;
  useEffect(() => setMounted(true), []);

  // Esc fecha; Tab fica preso dentro da folha; rolagem da página travada enquanto aberta.
  useEffect(() => {
    if (!mounted) return;
    const panel = panelRef.current;
    panel?.focus();
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onCloseRef.current();
        return;
      }
      if (e.key !== "Tab" || !panel) return;
      const items = [...panel.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], textarea, input, select, [tabindex]:not([tabindex="-1"])')];
      if (items.length === 0) return;
      const first = items[0];
      const last = items[items.length - 1];
      if (e.shiftKey && (document.activeElement === first || document.activeElement === panel)) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = prevOverflow;
    };
  }, [mounted]);

  if (!mounted) return null;
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center">
      <div aria-hidden="true" className="absolute inset-0 animate-scrim-in bg-scrim" onClick={onClose} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        className={`relative flex max-h-[92dvh] w-full max-w-lg animate-sheet-in flex-col rounded-t-card bg-card shadow-elevation-sheet ${focusRing}`}
        style={dragY > 0 ? { transform: `translateY(${dragY}px)` } : undefined}
      >
        {/* Alça: arrastar para baixo fecha. */}
        <div
          className="flex touch-none justify-center pb-1 pt-2"
          onPointerDown={(e) => {
            dragStart.current = e.clientY;
            e.currentTarget.setPointerCapture?.(e.pointerId);
          }}
          onPointerMove={(e) => {
            if (dragStart.current !== null) setDragY(Math.max(0, e.clientY - dragStart.current));
          }}
          onPointerUp={(e) => {
            const delta = dragStart.current === null ? 0 : e.clientY - dragStart.current;
            dragStart.current = null;
            setDragY(0);
            if (shouldCloseOnDrag(delta)) onClose();
          }}
          onPointerCancel={() => {
            dragStart.current = null;
            setDragY(0);
          }}
        >
          <span aria-hidden="true" className="h-1 w-10 rounded-full bg-line-control" />
        </div>
        <div className="flex items-center justify-between gap-2 px-md pb-sm">
          <h2 className="text-subtitle">{title}</h2>
          <button type="button" className={secondaryButton} onClick={onClose}>
            Fechar
          </button>
        </div>
        <div className="overflow-y-auto px-md pb-[max(16px,env(safe-area-inset-bottom))]">{children}</div>
      </div>
    </div>,
    document.body,
  );
}
