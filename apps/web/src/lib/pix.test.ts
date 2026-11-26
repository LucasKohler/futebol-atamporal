import { describe, expect, it } from "vitest";
import {
  canVerify,
  checkoutPhase,
  formatCountdown,
  isActivePix,
  isSettled,
  paidBySource,
  pixSummaryText,
  remainingMs,
  shouldPoll,
} from "./pix";

const NOW = Date.parse("2026-11-26T12:00:00Z");
const charge = (status: string, expiresAt: string, obligationStatus = "Pending") => ({ status, expiresAt, obligationStatus });

describe("checkout Pix", () => {
  it("contador regressivo", () => {
    expect(remainingMs("2026-11-26T12:28:45Z", NOW)).toBe(28 * 60_000 + 45_000);
    expect(formatCountdown(28 * 60_000 + 45_000)).toBe("28m 45s");
    expect(formatCountdown(65 * 60_000 + 5_000)).toBe("1h 05m 05s");
    expect(formatCountdown(0)).toBe("Expirado");
    expect(remainingMs("inválido", NOW)).toBe(0);
  });

  it("fase: pago por aprovação ou obrigação quitada; expirado pelo prazo mesmo antes do servidor marcar", () => {
    expect(checkoutPhase(charge("Approved", "2026-11-26T12:30:00Z"), NOW)).toBe("paid");
    expect(checkoutPhase(charge("Pending", "2026-11-26T12:30:00Z", "Paid"), NOW)).toBe("paid");
    expect(checkoutPhase(charge("Pending", "2026-11-26T12:30:00Z"), NOW)).toBe("pending");
    expect(checkoutPhase(charge("Pending", "2026-11-26T11:59:59Z"), NOW)).toBe("expired");
    expect(checkoutPhase(charge("Expired", "2026-11-26T12:30:00Z"), NOW)).toBe("expired");
    expect(checkoutPhase(charge("Cancelled", "2026-11-26T12:30:00Z"), NOW)).toBe("cancelled");
  });

  it("polling só pendente e com a aba visível", () => {
    expect(shouldPoll("pending", true)).toBe(true);
    expect(shouldPoll("pending", false)).toBe(false);
    expect(shouldPoll("paid", true)).toBe(false);
    expect(shouldPoll("expired", true)).toBe(false);
  });

  it("verificar respeita 10 s entre consultas", () => {
    expect(canVerify(null, NOW)).toBe(true);
    expect(canVerify(NOW - 9_000, NOW)).toBe(false);
    expect(canVerify(NOW - 10_000, NOW)).toBe(true);
  });

  it("obrigação quitada", () => {
    expect(isSettled("Paid")).toBe(true);
    expect(isSettled("Exempt")).toBe(true);
    expect(isSettled("Pending")).toBe(false);
    expect(isSettled("Overdue")).toBe(false);
  });
});

describe("status Pix no admin", () => {
  const pix = (status: string, expiresAt = "2026-11-26T12:30:00Z") => ({
    id: "1",
    mpPaymentId: "123",
    status,
    amount: 150,
    expiresAt,
    createdAt: "2026-11-26T11:30:00Z",
    approvedAt: null,
    isSimulated: false,
  });

  it("textos por situação", () => {
    expect(pixSummaryText(null, "None", NOW)).toBe("Sem Pix gerado");
    expect(pixSummaryText(pix("Pending"), "None", NOW, "UTC")).toBe("Pix gerado · expira às 12:30");
    expect(pixSummaryText(pix("Pending", "2026-11-26T11:00:00Z"), "None", NOW)).toBe("Pix expirado");
    expect(pixSummaryText(pix("Approved"), "Pix", NOW)).toBe("Aprovado via Mercado Pago #123");
    expect(pixSummaryText(pix("Pending"), "Manual", NOW)).toBe("Baixa manual");
  });

  it("Pix ativo e pagos por fonte", () => {
    expect(isActivePix(pix("Pending"), NOW)).toBe(true);
    expect(isActivePix(pix("Pending", "2026-11-26T11:00:00Z"), NOW)).toBe(false);
    expect(isActivePix(null, NOW)).toBe(false);
    expect(
      paidBySource([
        { status: "Paid", paymentSource: "Pix", amount: 150 },
        { status: "Paid", paymentSource: "Manual", amount: 150 },
        { status: "Paid", amount: 100 },
        { status: "Pending", paymentSource: "None", amount: 150 },
      ]),
    ).toEqual({ pixCount: 1, manualCount: 2, pixAmount: 150, manualAmount: 250 });
  });
});
