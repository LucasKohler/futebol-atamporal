import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { overflowMask, scrollEdges, SHEET_DRAG_CLOSE_PX, shouldCloseOnDrag } from "./layoutRules";
import { countElevation2, homeElevation } from "./elevation";
import { pixEndTone } from "./pix";
import { teamStyle } from "./teams";

const src = (p: string) => readFileSync(join(__dirname, "..", p), "utf8");

describe("sombras (revisão 27/11)", () => {
  it("Home: só o card primário em elevation-2; demais em elevation-1", () => {
    expect(homeElevation(true)).toBe("shadow-elevation-2");
    expect(homeElevation(false)).toBe("shadow-elevation-1");
  });

  it("Home: as regras de primário nunca geram dois elevation-2", () => {
    // Espelha page.tsx: atrasada > etapa (jogar/confirmar) > diária pendente.
    for (const overdue of [true, false])
      for (const stageWantsPrimary of [true, false])
        for (const pending of [true, false]) {
          const stagePrimary = !overdue && stageWantsPrimary;
          const pendingPrimary = pending && !overdue && !stagePrimary;
          expect(countElevation2([overdue, stagePrimary, pendingPrimary])).toBeLessThanOrEqual(1);
        }
  });

  it("Súmula: o placar é um único elevation-2 e cada cor mantém a borda do time", () => {
    const live = src("components/admin/live/LiveTab.tsx");
    expect(live.match(/shadow-elevation-2/g)?.length).toBe(1);
    expect(live).toContain("rounded-xl border-2 ${style.border} bg-card");
  });

  it("cards elevados mantêm a borda sutil (decisão do Lucas)", () => {
    const home = src("app/page.tsx");
    expect(home).toContain('const cardClass = "rounded-card border border-line-subtle');
    expect(src("components/pix/PaymentsLink.tsx")).toMatch(/border border-outline-variant[^"]*shadow-elevation-1/);
  });

  it("barras fixas só ganham sombra com conteúdo rolando por baixo", () => {
    expect(scrollEdges(0, 800, 800)).toEqual({ scrolled: false, contentBelow: false });
    expect(scrollEdges(0, 800, 2000)).toEqual({ scrolled: false, contentBelow: true });
    expect(scrollEdges(1200, 800, 2000)).toEqual({ scrolled: true, contentBelow: false });
  });
});

describe("Súmula: cor do time em cada botão", () => {
  it("cores vêm dos tokens team-*", () => {
    expect(teamStyle({ color: "Preto", name: "Preto" })).toMatchObject({ swatch: "bg-team-black", cssColor: "var(--team-black)" });
    expect(teamStyle({ color: "Verde", name: "Verde" })).toMatchObject({ swatch: "bg-team-green", border: "border-team-green" });
  });

  it("cada botão de gol tem o ponto de 10 px da cor do time", () => {
    const live = src("components/admin/live/LiveTab.tsx");
    expect(live).toContain('className={`inline-block h-2.5 w-2.5 shrink-0 rounded-full ${style?.swatch ?? "bg-outline"}`}');
  });
});

describe("abas e tabelas", () => {
  it("degradê só na borda onde há mais conteúdo", () => {
    expect(overflowMask({ start: false, end: false })).toBeUndefined();
    expect(overflowMask({ start: false, end: true })).toContain("transparent 100%");
    expect(overflowMask({ start: true, end: false })).toContain("transparent 0");
  });

  it("trilho de abas: uma fileira (sem flex-wrap), mesma altura, texto centralizado", () => {
    const rail = src("components/TabRail.tsx");
    expect(rail).not.toContain("flex-wrap");
    expect(rail).toContain("items-stretch");
    expect(rail).toContain("overflow-x-auto");
    expect(rail).toContain("text-center");
    expect(src("app/rankings/page.tsx")).toContain("<TabRail");
    expect(src("components/admin/AdminTabs.tsx")).toContain("<TabRail");
  });

  it("tabela do dia: primeira coluna fixa e troféu na mesma linha do nome", () => {
    const board = src("components/live/DayBoard.tsx");
    expect(board).toContain("sticky left-0");
    expect(board).toContain('className={`whitespace-nowrap py-2 ${sticky}`}');
  });
});

describe("Pix (07d/07e)", () => {
  it("Cancelado neutro, Expirou âmbar", () => {
    expect(pixEndTone("cancelled")).toBe("neutral");
    expect(pixEndTone("expired")).toBe("warning");
  });

  it("Cancelado mantém 'Gerar novo código Pix' e 'Verificar' (decisão do Lucas)", () => {
    const pix = src("components/pix/PixCheckout.tsx");
    const block = pix.slice(pix.indexOf("data-phase={phase}"));
    expect(block).toContain("Gerar novo código Pix");
    expect(block).toContain("Já paguei / Verificar");
    expect(block).not.toMatch(/phase === "expired" \?[^:]*Gerar/); // não condiciona as ações à expiração
  });

  it("folha só fecha por ação do usuário (sem fechamento automático)", () => {
    const sheet = src("components/Sheet.tsx");
    expect(sheet).not.toMatch(/setTimeout|setInterval/);
    expect(shouldCloseOnDrag(SHEET_DRAG_CLOSE_PX)).toBe(true);
    expect(shouldCloseOnDrag(SHEET_DRAG_CLOSE_PX - 1)).toBe(false);
    expect(shouldCloseOnDrag(-200)).toBe(false);
    expect(sheet).toContain("shadow-elevation-sheet");
    expect(sheet).toContain("bg-scrim");
  });

  it("Pagamentos abre o checkout na folha", () => {
    const page = src("app/pagamentos/page.tsx");
    expect(page).toContain('<Sheet title="Pagar com Pix"');
    expect(page).not.toContain("checkoutFor(");
  });
});
