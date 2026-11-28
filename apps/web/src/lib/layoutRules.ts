/**
 * Regras puras de layout da revisão 27/11 (testáveis sem DOM).
 */

/** Máscara de degradê na borda onde há mais conteúdo (o corte mostra que a fileira continua). */
export function overflowMask(hint: { start: boolean; end: boolean }): string | undefined {
  if (!hint.start && !hint.end) return undefined;
  const left = hint.start ? "transparent 0, #000 24px" : "#000 0";
  const right = hint.end ? "#000 calc(100% - 24px), transparent 100%" : "#000 100%";
  return `linear-gradient(to right, ${left}, ${right})`;
}

/** Barras fixas só ganham sombra quando o conteúdo rola por baixo delas. */
export function scrollEdges(scrollY: number, viewportHeight: number, documentHeight: number) {
  return { scrolled: scrollY > 0, contentBelow: documentHeight - (scrollY + viewportHeight) > 1 };
}

/** Distância (px) arrastando para baixo que fecha a folha. */
export const SHEET_DRAG_CLOSE_PX = 96;

/** Decide se um arrasto vertical fecha a folha (só para baixo e além do limite). */
export function shouldCloseOnDrag(deltaY: number, threshold = SHEET_DRAG_CLOSE_PX): boolean {
  return deltaY >= threshold;
}
