import type { PaymentSource, PixCharge, PixChargeSummary } from "./api";

/** Intervalo do polling local enquanto o checkout está aberto. */
export const PIX_POLL_MS = 5_000;
/** "Já paguei / Verificar" consulta o Mercado Pago no máximo uma vez nesse intervalo (ADR do design). */
export const PIX_VERIFY_COOLDOWN_MS = 10_000;

export const PIX_STATUS_LABEL: Record<string, string> = {
  Pending: "Aguardando pagamento",
  Approved: "Pago via Pix",
  Expired: "Expirado",
  Cancelled: "Cancelado",
};

export const PAYMENT_SOURCE_LABEL: Record<PaymentSource, string> = {
  None: "—",
  Pix: "Pix automático",
  Manual: "Baixa manual",
};

export const GATEWAY_MODE_LABEL: Record<string, string> = {
  Live: "Mercado Pago conectado",
  Simulator: "Modo simulador (teste)",
  Unconfigured: "Mercado Pago não configurado",
};

/** Obrigação quitada (por Pix ou baixa manual) ou isenta: nada a pagar. */
export function isSettled(obligationStatus: string): boolean {
  return obligationStatus === "Paid" || obligationStatus === "Exempt" || obligationStatus === "Cancelled";
}

export function remainingMs(expiresAt: string, now: number): number {
  const t = Date.parse(expiresAt);
  return Number.isFinite(t) ? Math.max(0, t - now) : 0;
}

/** "Expira em 28m 45s" · "1h 05m" · "Expirado". */
export function formatCountdown(ms: number): string {
  if (ms <= 0) return "Expirado";
  const total = Math.ceil(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n: number) => String(n).padStart(2, "0");
  return h > 0 ? `${h}h ${pad(m)}m ${pad(s)}s` : `${m}m ${pad(s)}s`;
}

export type CheckoutPhase = "paid" | "pending" | "expired" | "cancelled";

/**
 * Fase do checkout derivada só do servidor + relógio:
 * pago quando a cobrança foi aprovada ou a obrigação já está quitada; expirado quando passou do prazo.
 */
export function checkoutPhase(charge: Pick<PixCharge, "status" | "obligationStatus" | "expiresAt">, now: number): CheckoutPhase {
  if (charge.status === "Approved" || charge.obligationStatus === "Paid") return "paid";
  if (charge.status === "Cancelled") return "cancelled";
  if (charge.status === "Expired" || remainingMs(charge.expiresAt, now) <= 0) return "expired";
  return "pending";
}

/** Polling só com o checkout aberto, cobrança pendente, dentro do prazo e aba visível. */
export function shouldPoll(phase: CheckoutPhase, visible: boolean): boolean {
  return phase === "pending" && visible;
}

export function canVerify(lastVerifyAt: number | null, now: number): boolean {
  return lastVerifyAt === null || now - lastVerifyAt >= PIX_VERIFY_COOLDOWN_MS;
}

/** Texto curto do último Pix para listas do admin. */
export function pixSummaryText(pix: PixChargeSummary | null | undefined, source: PaymentSource | undefined, now: number, timeZone?: string): string {
  if (source === "Manual") return "Baixa manual";
  if (!pix) return "Sem Pix gerado";
  if (pix.status === "Approved") return `Aprovado via Mercado Pago${pix.mpPaymentId ? ` #${pix.mpPaymentId}` : ""}${pix.isSimulated ? " (simulado)" : ""}`;
  if (pix.status === "Pending" && remainingMs(pix.expiresAt, now) > 0) {
    const time = new Date(pix.expiresAt).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone });
    return `Pix gerado · expira às ${time}`;
  }
  if (pix.status === "Cancelled") return "Pix cancelado";
  return "Pix expirado";
}

/** Pix pendente ainda válido: o admin pode sincronizar com o Mercado Pago. */
export function isActivePix(pix: Pick<PixChargeSummary, "status" | "expiresAt"> | null | undefined, now: number): boolean {
  return !!pix && pix.status === "Pending" && remainingMs(pix.expiresAt, now) > 0;
}

export type ObligationActions = { pix: boolean; sync: boolean; markPaid: boolean; revertPending: boolean; exempt: boolean };

/**
 * Ações do admin numa mensalidade ou diária (mesma regra nas duas telas).
 * Pago via Pix é definitivo na tela: sem baixa manual, isenção ou volta a pendente (a API também recusa com 409).
 * Isentar só vale para o que está em aberto (pendente ou atrasado).
 */
export function obligationActions(
  status: string,
  source: PaymentSource | undefined,
  latestPix: Pick<PixChargeSummary, "status" | "expiresAt"> | null | undefined,
  now: number,
): ObligationActions {
  const open = status === "Pending" || status === "Overdue";
  const paidByPix = status === "Paid" && source === "Pix";
  return {
    pix: open,
    sync: isActivePix(latestPix, now),
    markPaid: status !== "Paid",
    revertPending: (status === "Paid" && !paidByPix) || status === "Exempt",
    exempt: open,
  };
}

/** Pagos por fonte a partir das mensalidades da competência (Pix automático x baixa manual). */
export function paidBySource(payments: { status: string; paymentSource?: PaymentSource; amount: number }[]) {
  const r = { pixCount: 0, manualCount: 0, pixAmount: 0, manualAmount: 0 };
  for (const p of payments) {
    if (p.status !== "Paid") continue;
    if (p.paymentSource === "Pix") {
      r.pixCount++;
      r.pixAmount += p.amount;
    } else {
      // Sem paymentSource (API antiga) ou "Manual"/"None": baixa feita à mão.
      r.manualCount++;
      r.manualAmount += p.amount;
    }
  }
  return r;
}
