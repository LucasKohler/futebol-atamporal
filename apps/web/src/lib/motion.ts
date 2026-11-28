/**
 * Regras de movimento da revisão de 27/11 (decisões do Lucas):
 * - o dado muda na hora; a animação só acompanha;
 * - nada na Súmula passa de 300 ms (pulso e cascata foram encurtados para caber);
 * - vibração após o gol é opcional e nunca a única confirmação (o aviso "Gol de X" continua);
 * - com menos movimento, o CSS zera durações e atrasos; o estado muda direto.
 */

/** Teto de qualquer animação da Súmula e do jogo (ms). */
export const SUMULA_MAX_MS = 300;
/** Duração de cada item (= duration-base). */
export const BASE_MS = 200;
/** Intervalo entre itens da cascata (times sorteados). */
export const STAGGER_STEP_MS = 40;

/** Direção da troca do placar: o número novo entra de baixo (gol) ou de cima (gol desfeito). */
export function scoreDirection(prev: number | undefined, next: number): "up" | "down" | null {
  if (prev === undefined || prev === next) return null;
  return next > prev ? "up" : "down";
}

/**
 * Atraso do item `index` na cascata. Limitado para que atraso + duração caiba no teto de 300 ms,
 * mesmo com muitos jogadores.
 */
export function staggerDelayMs(index: number, step = STAGGER_STEP_MS, durationMs = BASE_MS, maxTotalMs = SUMULA_MAX_MS): number {
  const cap = Math.max(0, maxTotalMs - durationMs);
  return Math.min(Math.max(0, index) * step, cap);
}

type VibratingNavigator = { vibrate?: (pattern: number | number[]) => boolean };

/**
 * Vibração curta (10 ms) depois do gol, só onde existir. Opcional e silenciosa: nunca lança e não substitui
 * a confirmação visual. Retorna se o navegador aceitou.
 */
export function vibrateGoal(nav: VibratingNavigator | undefined = typeof navigator !== "undefined" ? (navigator as VibratingNavigator) : undefined): boolean {
  try {
    if (!nav || typeof nav.vibrate !== "function") return false;
    return nav.vibrate(10) === true;
  } catch {
    return false;
  }
}
