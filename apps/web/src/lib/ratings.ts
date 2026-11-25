/** Validação no cliente das notas de linha e goleiro (SO-1). A API valida de novo. */

export type RatingsInput = {
  lineRating: string;
  canPlayGoalkeeper: boolean;
  goalkeeperRating: string;
};

export type RatingsPayload = {
  lineRating: number;
  canPlayGoalkeeper: boolean;
  /** null = sem nota de goleiro (diferente de zero). */
  goalkeeperRating: number | null;
};

export type RatingsResult = { ok: true; value: RatingsPayload } | { ok: false; error: string };

function parse(raw: string): number | null | "invalid" {
  const text = raw.trim().replace(",", ".");
  if (text === "") return null;
  if (!/^\d+(\.\d{1,2})?$/.test(text)) return "invalid";
  const value = Number(text);
  return value >= 0 && value <= 10 ? value : "invalid";
}

export function validateRatings(input: RatingsInput): RatingsResult {
  const line = parse(input.lineRating);
  if (line === null) return { ok: false, error: "Informe a nota de linha (0 a 10)." };
  if (line === "invalid") return { ok: false, error: "Nota de linha deve ser um número de 0 a 10, com até duas casas." };

  const gk = parse(input.goalkeeperRating);
  if (gk === "invalid") return { ok: false, error: "Nota de goleiro deve ser um número de 0 a 10, com até duas casas." };
  if (gk !== null && !input.canPlayGoalkeeper)
    return { ok: false, error: "Marque “Apto para o gol” para registrar nota de goleiro." };

  return { ok: true, value: { lineRating: line, canPlayGoalkeeper: input.canPlayGoalkeeper, goalkeeperRating: gk } };
}

export function formatRating(value: number | null | undefined): string {
  return value === null || value === undefined ? "sem nota" : value.toLocaleString("pt-BR", { maximumFractionDigits: 2 });
}
