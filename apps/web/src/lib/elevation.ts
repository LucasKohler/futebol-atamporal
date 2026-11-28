/**
 * Onde aplicar as sombras (revisão 27/11, decisões do Lucas):
 * - no máximo um `elevation-2` por tela: só o card com a ação primária ("Sua próxima ação") ou o placar ao vivo;
 * - demais cards soltos em `elevation-1`;
 * - cards elevados MANTÊM a borda sutil; card de estado (Atrasada, Confirmado) mantém a borda colorida e ganha a sombra.
 */
export function homeElevation(primary: boolean): string {
  return primary ? "shadow-elevation-2" : "shadow-elevation-1";
}

/** Quantos cards ficariam em elevation-2 com esses estados de "primário" (deve ser ≤ 1 por tela). */
export function countElevation2(primaries: boolean[]): number {
  return primaries.filter(Boolean).length;
}
