import { describe, expect, it } from "vitest";
import type { PixCharge } from "./api";
import { createCheckoutSessions, type CheckoutOpen } from "./pixCheckoutSession";

function deferred<T>() {
  let resolve!: (v: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => ((resolve = res), (reject = rej)));
  return { promise, resolve, reject };
}

const charge = (id: string, status = "Pending", expiresAt = "2099-01-01T00:00:00Z") => ({ id, status, expiresAt }) as unknown as PixCharge;

function setup() {
  const opened: CheckoutOpen[] = [];
  let busy: string | null = null;
  const errors: Array<string | null> = [];
  let reloads = 0;
  // Estado da tela: o que a folha mostra agora (null = fechada), como em pagamentos/page.tsx.
  let sheet: CheckoutOpen | null = null;
  const s = createCheckoutSessions({
    open: (o) => {
      opened.push(o);
      sheet = o;
    },
    setBusy: (u) => (busy = u(busy)),
    setError: (m) => errors.push(m),
    reload: async () => void reloads++,
    now: () => Date.parse("2026-11-27T12:00:00Z"),
  });
  return {
    s,
    opened,
    errors,
    get busy() {
      return busy;
    },
    get reloads() {
      return reloads;
    },
    get sheet() {
      return sheet;
    },
    close() {
      s.close();
      sheet = null;
    },
  };
}

describe("checkout Pix: respostas tardias não reabrem nem substituem a folha", () => {
  it("'Gerar novo código Pix' atrasado + folha fechada: a resposta chega e a folha continua fechada", async () => {
    const t = setup();
    const gen = deferred<PixCharge>();
    let calls = 0;
    const create = () => (++calls === 1 ? Promise.resolve(charge("c1", "Expired")) : gen.promise);
    // Abre com código expirado na lista: gera um e abre a folha.
    await t.s.start("d:1", create, null);
    expect(t.sheet?.charge.id).toBe("c1");
    // Dentro da folha: "Gerar novo código Pix" (resposta lenta)…
    const regen = t.sheet!.regenerate();
    // …e o usuário fecha a folha (Esc/véu/arrasto/Fechar) antes da resposta.
    t.close();
    gen.resolve(charge("c2"));
    await regen;
    expect(t.sheet).toBeNull(); // não reabriu (nada de foco roubado do gatilho)
    expect(t.opened.map((o) => o.charge.id)).toEqual(["c1"]);
  });

  it("resposta atrasada do checkout A não substitui o checkout B aberto depois", async () => {
    const t = setup();
    const genA = deferred<PixCharge>();
    const startA = t.s.start("d:A", () => genA.promise, null); // A gerando (lento)
    expect(t.busy).toBe("d:A");
    await t.s.start("m:B", () => Promise.resolve(charge("b1")), null); // usuário abre B
    expect(t.sheet?.key).toBe("m:B");
    genA.resolve(charge("a1")); // A responde depois
    await startA;
    expect(t.sheet?.key).toBe("m:B");
    expect(t.sheet?.charge.id).toBe("b1");
    expect(t.opened.map((o) => o.key)).toEqual(["m:B"]);
    expect(t.busy).toBeNull();
  });

  it("'Gerar novo código' de A dentro da folha, A substituída por B: B continua", async () => {
    const t = setup();
    const genA = deferred<PixCharge>();
    await t.s.start("d:A", () => genA.promise, charge("a0")); // reusa o Pix pendente de A
    expect(t.sheet?.charge.id).toBe("a0");
    const pending = t.sheet!.regenerate(); // "Gerar novo código Pix" em A (lento)
    await t.s.start("m:B", () => Promise.resolve(charge("b1")), null); // usuário abre B
    genA.resolve(charge("a2"));
    await pending;
    expect(t.sheet?.key).toBe("m:B");
    expect(t.opened.map((o) => o.charge.id)).toEqual(["a0", "b1"]);
  });

  it("erro tardio de uma operação descartada não aparece nem recarrega", async () => {
    const t = setup();
    const gen = deferred<PixCharge>();
    const p = t.s.start("d:1", () => gen.promise, null);
    t.close();
    gen.reject(new Error("409 já pago"));
    await p;
    expect(t.errors).toEqual([null]);
    expect(t.reloads).toBe(0);
    expect(t.sheet).toBeNull();
  });

  it("fluxo aprovado continua: reusa Pix pendente válido, gera quando não há, regenera na mesma folha e mostra erro atual", async () => {
    const t = setup();
    await t.s.start("d:1", () => Promise.resolve(charge("novo")), charge("ativo"));
    expect(t.sheet?.charge.id).toBe("ativo");
    await t.sheet!.regenerate();
    expect(t.sheet?.charge.id).toBe("novo");
    await t.s.start("d:2", () => Promise.reject(new Error("falhou")), null);
    expect(t.errors.at(-1)).toBe("falhou");
    expect(t.reloads).toBe(1);
    expect(t.busy).toBeNull();
  });
});
