import { describe, expect, it } from "vitest";
import type { Draw } from "./api";
import { shouldRestoreFocus } from "./focus";
import { nextSwapSelection, swapPreview } from "./teams";

// Dados fictícios: sorteio de três cores.
const draw = (status: string): Draw =>
  ({
    status,
    teams: [
      { id: "R", name: "Time 1", color: "Vermelho", sortOrder: 1, members: [
        { playerProfileId: "r1", displayName: "Ana", role: "Goalkeeper", skillRating: 6, goalkeeperRating: 7 },
        { playerProfileId: "r2", displayName: "Beto", role: "Line", skillRating: 5, goalkeeperRating: null },
      ] },
      { id: "B", name: "Time 2", color: "Preto", sortOrder: 2, members: [
        { playerProfileId: "b2", displayName: "Duda", role: "Line", skillRating: 6, goalkeeperRating: null },
      ] },
    ],
  }) as unknown as Draw;

describe("troca no sorteio exige prévia e confirmação", () => {
  it("segundo toque só devolve o par; a prévia descreve a mudança", () => {
    const first = nextSwapSelection(null, { playerProfileId: "r2", teamId: "R", role: "Line" });
    const second = nextSwapSelection(first.selected, { playerProfileId: "b2", teamId: "B", role: "Line" });
    expect(second.swap).toEqual(["r2", "b2"]);
    const preview = swapPreview(draw("Draft"), "r2", "b2");
    expect(preview?.moves).toEqual([
      "Beto sai do Vermelho (linha) e vai para o Preto (linha)",
      "Duda sai do Preto (linha) e vai para o Vermelho (linha)",
    ]);
    expect(preview?.invalidatesConfirmation).toBe(false);
  });

  it("em sorteio confirmado avisa que a confirmação será desfeita", () => {
    expect(swapPreview(draw("Confirmed"), "r1", "r2")).toMatchObject({
      invalidatesConfirmation: true,
      moves: ["Ana sai do Vermelho (gol) e vai para o Vermelho (linha)", "Beto sai do Vermelho (linha) e vai para o Vermelho (gol)"],
    });
  });

  it("jogador fora do sorteio não gera prévia", () => {
    expect(swapPreview(draw("Draft"), "r2", "x")).toBeNull();
  });
});

describe("retorno de foco ao fechar o painel", () => {
  const doc: { body?: unknown } = {};
  const body = { ownerDocument: doc };
  doc.body = body;
  const inside = { ownerDocument: doc };
  const outside = { ownerDocument: doc };
  const root = { contains: (n: unknown) => n === inside };

  it("restaura quando o foco caiu no body ou ficou dentro do painel", () => {
    expect(shouldRestoreFocus(body, root)).toBe(true);
    expect(shouldRestoreFocus(null, root)).toBe(true);
    expect(shouldRestoreFocus(inside, root)).toBe(true);
  });

  it("não rouba o foco quando o usuário já focou outro elemento", () => {
    expect(shouldRestoreFocus(outside, root)).toBe(false);
  });
});
