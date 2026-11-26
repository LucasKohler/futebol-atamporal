"use client";

import { useEffect, useState } from "react";
import { primaryButton, secondaryButton, focusRing } from "@/components/admin/ui";
import type { MatchDetail } from "@/lib/api";
import { buildWhatsAppSummary } from "@/lib/matchSummary";

/**
 * Admin copia o texto da lista (e da escalação confirmada, quando houver) e compartilha pela própria conta
 * do WhatsApp: o app não envia nada. Sem telefone, nota ou pagamento. Clipboard negado mostra o texto para cópia manual.
 */
export function WhatsAppSummary({
  detail,
  teams,
  timeZone,
  title = "Resumo para o WhatsApp",
  description = "Data, local, vagas, confirmados e fila na ordem da lista. Sem telefone, nota ou pagamento.",
}: {
  detail: MatchDetail;
  teams: string | null;
  timeZone: string;
  title?: string;
  description?: string;
}) {
  const [copied, setCopied] = useState<string | null>(null);
  const [showText, setShowText] = useState(false);
  const [text, setText] = useState("");

  function currentText() {
    const link = `${window.location.origin}/partida`;
    const value = buildWhatsAppSummary(detail, { link, now: new Date(), timeZone, teams });
    setText(value);
    return value;
  }

  // Texto aberto acompanha a lista e a escalação: após entrar, sair, remover ou confirmar, regenera.
  useEffect(() => {
    if (!showText) return;
    setText(buildWhatsAppSummary(detail, { link: `${window.location.origin}/partida`, now: new Date(), timeZone, teams }));
  }, [detail, showText, timeZone, teams]);

  async function copy() {
    const value = currentText();
    try {
      await navigator.clipboard.writeText(value);
      setCopied("Resumo copiado. Cole no grupo do WhatsApp.");
    } catch {
      setShowText(true);
      setCopied("Não foi possível copiar automaticamente. Selecione o texto abaixo e copie.");
    }
  }

  return (
    <section aria-labelledby="resumo-titulo" className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
      <h2 id="resumo-titulo" className="text-headline-md">
        {title}
      </h2>
      <p className="mt-1 text-body-md text-on-surface-variant">{description}</p>
      <div className="mt-md flex flex-col gap-2 sm:flex-row">
        <button type="button" onClick={copy} className={primaryButton}>
          {teams ? "Copiar resumo e escalação" : "Copiar resumo"}
        </button>
        <button
          type="button"
          aria-expanded={showText}
          aria-controls="resumo-texto"
          onClick={() => {
            if (!showText) currentText();
            setShowText((v) => !v);
          }}
          className={secondaryButton}
        >
          {showText ? "Ocultar texto" : "Ver texto"}
        </button>
      </div>
      <p aria-live="polite" className="mt-2 text-label-md text-on-surface-variant">
        {copied ?? ""}
      </p>
      {showText ? (
        <textarea
          id="resumo-texto"
          readOnly
          aria-label="Texto do resumo da lista"
          value={text}
          rows={Math.min(24, text.split("\n").length + 1)}
          onFocus={(e) => e.currentTarget.select()}
          className={`mt-2 w-full rounded-lg border border-outline-variant bg-surface-container-low p-3 font-mono text-label-md ${focusRing}`}
        />
      ) : null}
    </section>
  );
}
