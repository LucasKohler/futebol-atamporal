import { describe, expect, it } from "vitest";
import { pixReturnSelectors, resolveReturnTarget } from "./focus";
import { obligationActions } from "./pix";

const NOW = Date.parse("2026-11-26T12:00:00Z");
const activePix = { status: "Pending", expiresAt: "2026-11-26T12:30:00Z" };

describe("ações do admin por obrigação (QA/revisor: Isentar)", () => {
  it("paga via Pix: sem Isentar, baixa manual ou volta a pendente", () => {
    expect(obligationActions("Paid", "Pix", { status: "Approved", expiresAt: "2026-11-26T12:30:00Z" }, NOW)).toEqual({
      pix: false,
      sync: false,
      markPaid: false,
      revertPending: false,
      exempt: false,
    });
  });

  it("paga manualmente: só desfazer baixa", () => {
    expect(obligationActions("Paid", "Manual", null, NOW)).toMatchObject({ exempt: false, markPaid: false, revertPending: true });
    // API antiga sem paymentSource conta como baixa manual
    expect(obligationActions("Paid", undefined, null, NOW).revertPending).toBe(true);
  });

  it("pendente ou atrasada: Pix, baixa manual e Isentar; sincronizar só com Pix ativo", () => {
    expect(obligationActions("Pending", "None", null, NOW)).toEqual({ pix: true, sync: false, markPaid: true, revertPending: false, exempt: true });
    expect(obligationActions("Overdue", "None", activePix, NOW)).toMatchObject({ exempt: true, sync: true });
  });

  it("isenta: desfazer isenção, sem isentar de novo", () => {
    expect(obligationActions("Exempt", "None", null, NOW)).toMatchObject({ exempt: false, revertPending: true, pix: false });
  });

  it("cancelada: sem Isentar nem Pix", () => {
    expect(obligationActions("Cancelled", "None", null, NOW)).toMatchObject({ exempt: false, pix: false });
  });
});

describe("retorno de foco do checkout Pix (QA: foco perdido ao abrir/fechar por teclado)", () => {
  const el = (connected: boolean, name: string) => ({ isConnected: connected, name, focus: () => undefined });

  it("volta ao gatilho original quando ele ainda está na página", () => {
    const trigger = el(true, "gatilho");
    expect(resolveReturnTarget(trigger, [el(true, "outro")])?.name).toBe("gatilho");
  });

  it("gatilho desmontado: usa o botão re-renderizado; se o item foi pago, o cartão do item", () => {
    const gone = el(false, "antigo");
    expect(resolveReturnTarget(gone, [el(true, "botão novo"), el(true, "cartão")])?.name).toBe("botão novo");
    expect(resolveReturnTarget(gone, [null, el(true, "cartão")])?.name).toBe("cartão");
    expect(resolveReturnTarget(null, [null, null])).toBeNull();
  });

  it("seletores do item e sem aspas injetadas na chave", () => {
    expect(pixReturnSelectors("m:abc")).toEqual(['[data-pix-trigger="m:abc"]', '[data-pix-item="m:abc"]']);
    expect(pixReturnSelectors('x"]')[0]).toBe('[data-pix-trigger="x]"]');
  });
});
