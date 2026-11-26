import { describe, expect, it } from "vitest";
import { deriveRoundStep } from "./roundStep";

const okDraft = { status: "Draft", rosterChanged: false, canConfirm: true };

describe("deriveRoundStep (stepper da rodada)", () => {
  it("sem partida: etapa 1 e CTA de criar partida", () => {
    const s = deriveRoundStep({ status: null, confirmedCount: 0, draw: null });
    expect(s.current).toBe(1);
    expect(s.cta).toEqual({ kind: "link", label: "Criar partida", href: "/partida" });
    expect(s.steps.map((x) => x.status)).toEqual(["current", "upcoming", "upcoming", "upcoming"]);
  });

  it("rascunho: transição para abrir inscrições", () => {
    const s = deriveRoundStep({ status: "Draft", confirmedCount: 0, draw: null });
    expect(s.cta).toMatchObject({ kind: "transition", to: "RegistrationOpen" });
  });

  it("inscrições abertas: bloqueia sorteio com menos de 3 confirmados", () => {
    const few = deriveRoundStep({ status: "RegistrationOpen", confirmedCount: 2, draw: null });
    expect(few.current).toBe(1);
    expect(few.blocker).toMatch(/ao menos 3/);
    const ok = deriveRoundStep({ status: "RegistrationOpen", confirmedCount: 12, draw: null });
    expect(ok.blocker).toBeNull();
    expect(ok.cta).toMatchObject({ href: "/admin/sorteio", label: "Sortear times" });
  });

  it("lista encerrada: sorteio pendente, prévia com lista alterada, bloqueada ou pronta", () => {
    expect(deriveRoundStep({ status: "RegistrationClosed", confirmedCount: 12, draw: null }).headline).toMatch(/sorteio pendente/);
    const changed = deriveRoundStep({ status: "RegistrationClosed", confirmedCount: 12, draw: { ...okDraft, rosterChanged: true } });
    expect(changed.current).toBe(2);
    expect(changed.cta?.label).toBe("Refazer sorteio");
    expect(changed.blocker).not.toBeNull();
    const blocked = deriveRoundStep({ status: "RegistrationClosed", confirmedCount: 12, draw: { ...okDraft, canConfirm: false } });
    expect(blocked.blocker).toMatch(/bloqueios/);
    const ready = deriveRoundStep({ status: "RegistrationClosed", confirmedCount: 12, draw: okDraft });
    expect(ready.cta?.label).toBe("Revisar e confirmar escalação");
    expect(ready.steps.map((x) => x.status)).toEqual(["done", "current", "upcoming", "upcoming"]);
  });

  it("sorteado: etapa 3 com transição para iniciar; lista alterada volta ao sorteio", () => {
    const drawn = deriveRoundStep({ status: "Drawn", confirmedCount: 12, draw: { ...okDraft, status: "Confirmed" } });
    expect(drawn.current).toBe(3);
    expect(drawn.cta).toMatchObject({ kind: "transition", to: "InProgress" });
    const invalid = deriveRoundStep({ status: "Drawn", confirmedCount: 12, draw: { ...okDraft, status: "Confirmed", rosterChanged: true } });
    expect(invalid.current).toBe(2);
  });

  it("em jogo e aguardando fechamento levam à súmula; concluída marca todas as etapas", () => {
    expect(deriveRoundStep({ status: "InProgress", confirmedCount: 12, draw: null })).toMatchObject({ current: 3, cta: { href: "/admin/registro" } });
    expect(deriveRoundStep({ status: "AwaitingClose", confirmedCount: 12, draw: null })).toMatchObject({ current: 4, cta: { href: "/admin/registro" } });
    const done = deriveRoundStep({ status: "Completed", confirmedCount: 12, draw: null });
    expect(done.finished).toBe(true);
    expect(done.steps.every((x) => x.status === "done")).toBe(true);
  });
});
