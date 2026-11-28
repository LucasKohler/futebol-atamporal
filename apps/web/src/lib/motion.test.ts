import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { BASE_MS, SUMULA_MAX_MS, scoreDirection, staggerDelayMs, vibrateGoal } from "./motion";

const css = readFileSync(join(__dirname, "../app/globals.css"), "utf8");
const tw = readFileSync(join(__dirname, "../../tailwind.config.ts"), "utf8");

describe("tokens da revisão 27/11", () => {
  it.each([
    ["--scrim", "rgba(11, 28, 48, 0.48)"],
    ["--elevation-0", "none"],
    ["--duration-instant", "80ms"],
    ["--duration-fast", "120ms"],
    ["--duration-base", "200ms"],
    ["--duration-slow", "280ms"],
    ["--duration-exit", "160ms"],
    ["--duration-celebrate", "480ms"],
    ["--ease-standard", "cubic-bezier(0.2, 0, 0, 1)"],
    ["--ease-enter", "cubic-bezier(0, 0, 0, 1)"],
    ["--ease-exit", "cubic-bezier(0.3, 0, 1, 1)"],
    ["--ease-emphasis", "cubic-bezier(0.34, 1.56, 0.64, 1)"],
  ])("%s = %s", (name, value) => {
    expect(css).toContain(`${name}: ${value};`);
  });

  it("sombras existem como variável e utilitário", () => {
    for (const n of ["elevation-1", "elevation-2", "elevation-3", "elevation-sheet", "elevation-bar", "elevation-bar-top"]) {
      expect(css).toMatch(new RegExp(`--${n}: `));
      expect(tw).toContain(`"${n}": v("${n}")`);
    }
  });

  it("menos movimento: preserva e amplia o bloco existente (duração, repetição, atraso e escala do toque)", () => {
    const block = css.slice(css.indexOf("@media (prefers-reduced-motion: reduce)"));
    for (const rule of ["animation-duration", "animation-iteration-count: 1", "animation-delay: 0ms", "transition-duration", "transition-delay: 0ms", "scroll-behavior: auto"]) {
      expect(block).toContain(rule);
    }
    expect(block).toMatch(/\.press:active[^{]*\{\s*transform: none;/);
  });

  it("nenhuma animação da Súmula passa de 300 ms (pulso encurtado; placar = duration-base)", () => {
    expect(tw).toContain('"team-pulse": "fa-team-pulse 280ms');
    expect(tw).toContain('"score-in": "fa-score-in var(--duration-base)');
    expect(280).toBeLessThanOrEqual(SUMULA_MAX_MS);
    expect(BASE_MS).toBeLessThanOrEqual(SUMULA_MAX_MS);
  });
});

describe("motion", () => {
  it("direção do placar", () => {
    expect(scoreDirection(undefined, 1)).toBeNull();
    expect(scoreDirection(1, 1)).toBeNull();
    expect(scoreDirection(1, 2)).toBe("up");
    expect(scoreDirection(2, 1)).toBe("down");
  });

  it("cascata cabe no teto de 300 ms com qualquer número de itens", () => {
    expect(staggerDelayMs(0)).toBe(0);
    expect(staggerDelayMs(2)).toBe(80);
    for (let i = 0; i < 30; i++) expect(staggerDelayMs(i) + BASE_MS).toBeLessThanOrEqual(SUMULA_MAX_MS);
  });

  it("vibração é opcional: sem suporte ou com erro não quebra nem conta como confirmação", () => {
    expect(vibrateGoal(undefined)).toBe(false);
    expect(vibrateGoal({})).toBe(false);
    expect(
      vibrateGoal({
        vibrate: () => {
          throw new Error("bloqueado");
        },
      }),
    ).toBe(false);
    const calls: Array<number | number[]> = [];
    expect(vibrateGoal({ vibrate: (p) => (calls.push(p), true) })).toBe(true);
    expect(calls).toEqual([10]);
  });
});
