/** Classes base do painel admin (uma fonte só, em vez de cópias por página). Alvos de toque ≥ 44 px (min-h-11). */
export const focusRing =
  "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-pitch-green-container";

export const primaryButton = `inline-flex min-h-11 items-center justify-center gap-2 rounded-full bg-pitch-green-container px-4 py-2 text-label-md text-on-primary disabled:opacity-50 ${focusRing}`;

export const secondaryButton = `inline-flex min-h-11 items-center justify-center gap-2 rounded-full border border-outline bg-surface-container-lowest px-4 py-2 text-label-md text-on-surface disabled:opacity-50 ${focusRing}`;

export const dangerButton = `inline-flex min-h-11 items-center justify-center gap-2 rounded-full border border-error px-4 py-2 text-label-md text-error disabled:opacity-50 ${focusRing}`;

export const textButton = `inline-flex min-h-11 items-center px-2 text-label-md underline disabled:opacity-50 ${focusRing}`;

export const inputClass = `min-h-11 rounded-lg border border-outline-variant bg-surface-container-lowest px-3 py-2 text-body-md ${focusRing}`;
