/**
 * Classes base (Button, campo, selo) do Design System — uma fonte só para todas as telas.
 * Alvos de toque ≥ 44 px (min-h-touch); modo jogo da súmula usa 56 px (min-h-touch-game).
 */
export const focusRing =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus";

const disabled = "disabled:cursor-not-allowed disabled:opacity-50";

/** Primário: a próxima ação da tela (no máximo um visível por tela/card). */
export const primaryButton = `inline-flex min-h-touch items-center justify-center gap-2 rounded-full bg-action px-4 py-2 text-label-md text-fg-on-brand hover:bg-action-pressed active:bg-action-pressed ${disabled} ${focusRing}`;

/** Secundário: apoio ao lado do primário (borda border-control, 3:1). */
export const secondaryButton = `inline-flex min-h-touch items-center justify-center gap-2 rounded-full border border-line-control bg-card px-4 py-2 text-label-md text-fg hover:bg-subtle ${disabled} ${focusRing}`;

/** Perigo: só dentro de confirmação. */
export const dangerButton = `inline-flex min-h-touch items-center justify-center gap-2 rounded-full border border-action-danger px-4 py-2 text-label-md text-action-danger hover:bg-danger-bg ${disabled} ${focusRing}`;

/** Fantasma: links de ajuda e ações de baixa importância. */
export const textButton = `inline-flex min-h-touch items-center px-2 text-label-md underline ${disabled} ${focusRing}`;

/** Modo jogo (súmula): 56 px e texto 16 px. */
export const gameButton = `inline-flex min-h-touch-game items-center justify-center gap-2 rounded-full px-4 py-2 text-body-lg font-semibold ${disabled} ${focusRing}`;

export const inputClass = `min-h-touch rounded-lg border border-line-control bg-card px-3 py-2 text-body-md placeholder:text-fg-placeholder ${focusRing}`;
