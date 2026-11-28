"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { focusRing, primaryButton, secondaryButton } from "@/components/admin/ui";
import { formatMoney } from "@/lib/adminFilters";
import { api, PixCharge } from "@/lib/api";
import { canVerify, checkoutPhase, formatCountdown, PIX_POLL_MS, pixEndTone, PIX_VERIFY_COOLDOWN_MS, remainingMs, shouldPoll } from "@/lib/pix";

/**
 * Checkout transparente Pix: copia e cola em destaque, QR Code, contador até expirar,
 * polling local a cada 5 s com a tela aberta e "Já paguei / Verificar" (consulta o Mercado Pago).
 * O estado vem sempre do servidor; o cliente nunca marca pago sozinho.
 */
export function PixCheckout({
  initialCharge,
  onSettled,
  onRegenerate,
  allowSimulate = false,
  autoFocus = true,
}: {
  /** Move o foco para o checkout ao montar (padrão). */
  autoFocus?: boolean;
  initialCharge: PixCharge;
  /** Chamado uma vez quando o servidor confirma o pagamento. */
  onSettled?: (charge: PixCharge) => void;
  /** Gera um novo código depois de expirar (reusa a rota de geração). */
  onRegenerate?: () => Promise<void>;
  /** Admin em modo simulador: aprova a cobrança de teste. */
  allowSimulate?: boolean;
}) {
  const [charge, setCharge] = useState(initialCharge);
  const [now, setNow] = useState(() => Date.now());
  const [visible, setVisible] = useState(true);
  const [copied, setCopied] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [lastVerifyAt, setLastVerifyAt] = useState<number | null>(null);
  const settledRef = useRef(false);
  const codeRef = useRef<HTMLTextAreaElement>(null);
  const rootRef = useRef<HTMLElement | null>(null);
  const cooldownTimer = useRef<number | null>(null);
  useEffect(
    () => () => {
      if (cooldownTimer.current) window.clearTimeout(cooldownTimer.current);
    },
    [],
  );

  // Ao abrir (inclusive por teclado), o foco entra no checkout em vez de cair no body quando o gatilho some.
  useEffect(() => {
    if (autoFocus) rootRef.current?.focus();
  }, [autoFocus]);

  useEffect(() => setCharge(initialCharge), [initialCharge]);

  const phase = checkoutPhase(charge, now);

  // Quando o conteúdo troca (ex.: pendente → pago), o botão focado some: o foco segue para o novo bloco.
  useEffect(() => {
    if (document.activeElement === document.body || !document.activeElement) rootRef.current?.focus();
  }, [phase]);

  // Relógio do contador (1 s) só enquanto pendente.
  useEffect(() => {
    if (phase !== "pending") return;
    const t = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(t);
  }, [phase]);

  // Pausa o polling com a aba em segundo plano.
  useEffect(() => {
    const onVis = () => setVisible(document.visibilityState !== "hidden");
    onVis();
    document.addEventListener("visibilitychange", onVis);
    return () => document.removeEventListener("visibilitychange", onVis);
  }, []);

  const poll = useCallback(async () => {
    try {
      setCharge(await api.getPixCharge(charge.id));
      setError(null);
    } catch {
      setError("Sem conexão com o servidor. Tentando de novo…");
    }
  }, [charge.id]);

  useEffect(() => {
    if (!shouldPoll(phase, visible)) return;
    const t = window.setInterval(poll, PIX_POLL_MS);
    return () => window.clearInterval(t);
  }, [phase, visible, poll]);

  useEffect(() => {
    if (phase === "paid" && !settledRef.current) {
      settledRef.current = true;
      onSettled?.(charge);
    }
  }, [phase, charge, onSettled]);

  async function copy() {
    if (!charge.qrCode) return;
    try {
      await navigator.clipboard.writeText(charge.qrCode);
      setCopied("Código Pix copiado! Cole no app do seu banco, em Pix Copia e Cola.");
    } catch {
      codeRef.current?.focus();
      codeRef.current?.select();
      setCopied("Não foi possível copiar automaticamente. O código está selecionado abaixo: copie manualmente.");
    }
  }

  async function verify() {
    const t = Date.now();
    if (!canVerify(lastVerifyAt, t)) return;
    setLastVerifyAt(t);
    setBusy(true);
    setError(null);
    setInfo(null);
    try {
      const result = await api.verifyPix(charge.id);
      setCharge({ ...result.charge, obligationStatus: result.obligationStatus });
      setInfo(result.message || (result.wasUpdated ? "Pagamento confirmado!" : "Ainda não recebemos a confirmação do banco."));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível verificar agora.");
    } finally {
      setBusy(false);
      setNow(Date.now());
      // Libera o botão ao fim do intervalo mesmo sem o relógio do contador (ex.: código expirado).
      if (cooldownTimer.current) window.clearTimeout(cooldownTimer.current);
      cooldownTimer.current = window.setTimeout(() => setNow(Date.now()), PIX_VERIFY_COOLDOWN_MS + 50);
    }
  }

  async function simulate() {
    setBusy(true);
    setError(null);
    try {
      const result = await api.simulateApprovePix(charge.id);
      setCharge({ ...result.charge, obligationStatus: result.obligationStatus });
      setInfo(result.message);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível simular.");
    } finally {
      setBusy(false);
    }
  }

  async function regenerate() {
    if (!onRegenerate) return;
    setBusy(true);
    setError(null);
    try {
      await onRegenerate();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível gerar um novo código.");
    } finally {
      setBusy(false);
    }
  }

  const verifyLocked = !canVerify(lastVerifyAt, now);
  const left = remainingMs(charge.expiresAt, now);

  if (phase === "paid") {
    return (
      <div
        ref={(el) => {
          rootRef.current = el;
        }}
        tabIndex={-1}
        role="status"
        aria-live="polite"
        className={`rounded-xl border-2 border-success bg-success-bg p-md text-success transition-colors duration-celebrate ease-standard ${focusRing}`}
      >
        {/* 07e: o check se desenha (duration-celebrate). Fica aberto até a pessoa agir (decisão 27/11). */}
        <p className="flex items-center gap-2 text-headline-md">
          <svg aria-hidden="true" viewBox="0 0 24 24" className="h-7 w-7 shrink-0" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12.5l4.5 4.5L19 7.5" strokeDasharray="24" className="animate-check-draw" />
          </svg>
          Pagamento confirmado!
        </p>
        <p className="mt-1 text-body-md">
          {formatMoney(charge.amount)} · {charge.description}
          {charge.approvedAt ? ` · ${new Date(charge.approvedAt).toLocaleString("pt-BR")}` : ""}
        </p>
      </div>
    );
  }

  return (
    <section
      ref={(el) => {
        rootRef.current = el;
      }}
      tabIndex={-1}
      aria-label={`Pagamento Pix: ${charge.description}`}
      className={`flex flex-col gap-md rounded-xl border-2 border-pitch-green-container bg-surface-container-lowest p-md ${focusRing}`}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-headline-md">{formatMoney(charge.amount)}</p>
        <p className="text-body-md text-on-surface-variant">{charge.description}</p>
      </div>
      {charge.isSimulated ? (
        <p className="rounded-lg border border-warning bg-warning-subtle p-2 text-label-md text-warning-strong">Modo de teste: este Pix é simulado e não cobra dinheiro.</p>
      ) : null}

      {phase === "pending" ? (
        <>
          <p className="text-body-lg font-semibold" aria-live="off">
            Expira em <span className="tabular-nums">{formatCountdown(left)}</span>
          </p>
          <button type="button" onClick={copy} disabled={!charge.qrCode} className={`${primaryButton} min-h-14 w-full text-body-lg`}>
            Copiar código Pix (Copia e Cola)
          </button>
          <p aria-live="polite" className="min-h-5 text-label-md text-success">
            {copied ?? ""}
          </p>
          <label className="flex flex-col gap-1 text-label-md">
            Código Pix Copia e Cola
            <textarea
              ref={codeRef}
              readOnly
              rows={3}
              value={charge.qrCode ?? ""}
              onFocus={(e) => e.currentTarget.select()}
              className={`w-full break-all rounded-lg border border-outline-variant bg-surface-container-low p-2 font-mono text-label-md ${focusRing}`}
            />
          </label>
          {charge.qrCodeBase64 ? (
            <div className="flex flex-col items-center gap-1">
              {/* eslint-disable-next-line @next/next/no-img-element -- imagem data: gerada pela API, sem otimização remota */}
              <img
                src={`data:image/png;base64,${charge.qrCodeBase64}`}
                alt="QR Code Pix para escanear com o app do banco"
                width={220}
                height={220}
                className="h-56 w-56 rounded-lg border border-outline-variant bg-white p-2 [image-rendering:pixelated]"
              />
              <p className="text-label-md text-on-surface-variant">Ou escaneie o QR Code com outro aparelho.</p>
            </div>
          ) : null}
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={verify} disabled={busy || verifyLocked} className={secondaryButton}>
              {busy ? <VerifyingDot /> : null}
              {verifyLocked ? "Verificando… aguarde alguns segundos" : "Já paguei / Verificar agora"}
            </button>
            {allowSimulate && charge.isSimulated ? (
              <button type="button" onClick={simulate} disabled={busy} className={secondaryButton}>
                Simular aprovação (teste)
              </button>
            ) : null}
          </div>
          <p className="text-label-md text-on-surface-variant">
            A confirmação aparece sozinha nesta tela (conferimos a cada {PIX_POLL_MS / 1000} s). O botão consulta o Mercado Pago no máximo a cada{" "}
            {PIX_VERIFY_COOLDOWN_MS / 1000} s.
          </p>
        </>
      ) : (
        // 07e: "Cancelado" em tom neutro; "Expirou" continua âmbar. Os dois mantêm "Gerar novo código" e "Verificar"
        // (decisão do Lucas 27/11), para quem pagou antes do cancelamento/expiração.
        <div
          role="alert"
          data-phase={phase}
          className={`flex flex-col gap-2 rounded-lg border p-3 ${
            pixEndTone(phase === "cancelled" ? "cancelled" : "expired") === "neutral"
              ? "border-line-control bg-subtle text-fg"
              : "border-warning bg-warning-subtle text-warning-strong"
          }`}
        >
          <p className="flex items-center gap-2 font-semibold">
            <span aria-hidden="true">{phase === "cancelled" ? "⊘" : "⏱"}</span>
            {phase === "cancelled" ? "Este código Pix foi cancelado." : "Este código Pix expirou."}
          </p>
          <p className="text-body-md">Se você já pagou, toque em verificar. Senão, gere um novo código.</p>
          <div className="flex flex-wrap gap-2">
            {onRegenerate ? (
              <button type="button" onClick={regenerate} disabled={busy} className={primaryButton}>
                Gerar novo código Pix
              </button>
            ) : null}
            <button type="button" onClick={verify} disabled={busy || verifyLocked} className={secondaryButton}>
              {busy ? <VerifyingDot /> : null}
              Já paguei / Verificar
            </button>
          </div>
        </div>
      )}

      {info ? (
        <p role="status" className="text-body-md">
          {info}
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="text-body-md text-on-error-container">
          {error}
        </p>
      ) : null}
    </section>
  );
}

/** Ponto pulsando só enquanto verifica (1 s por ciclo). Com menos movimento fica parado; o texto segue visível. */
function VerifyingDot() {
  return <span aria-hidden="true" className="inline-block h-2 w-2 animate-dot-pulse rounded-full bg-current" />;
}
