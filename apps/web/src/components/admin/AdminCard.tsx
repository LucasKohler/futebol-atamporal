import type { ReactNode } from "react";
import { AdminIcon } from "./AdminIcon";

/** Cartão-base das seções do admin: título opcional, ações à direita e conteúdo. */
export function AdminCard({
  title,
  titleId,
  description,
  actions,
  children,
  tone = "default",
  className = "",
}: {
  title?: ReactNode;
  titleId?: string;
  description?: ReactNode;
  actions?: ReactNode;
  children?: ReactNode;
  tone?: "default" | "muted" | "accent";
  className?: string;
}) {
  const toneClass =
    tone === "muted"
      ? "bg-surface-container-low border-outline-variant"
      : tone === "accent"
        ? "bg-surface-container-lowest border-pitch-green-container border-l-4"
        : "bg-surface-container-lowest border-outline-variant";
  return (
    <section aria-labelledby={title && titleId ? titleId : undefined} className={`rounded-xl border p-md sm:p-lg ${toneClass} ${className}`}>
      {title || actions ? (
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0">
            {title ? (
              <h2 id={titleId} className="text-headline-md">
                {title}
              </h2>
            ) : null}
            {description ? <p className="mt-1 text-body-md text-on-surface-variant">{description}</p> : null}
          </div>
          {actions ? <div className="flex flex-wrap gap-2">{actions}</div> : null}
        </div>
      ) : null}
      {children ? <div className={title || actions ? "mt-md" : ""}>{children}</div> : null}
    </section>
  );
}

export type BadgeTone = "success" | "warning" | "danger" | "neutral" | "info";

/** Tons do StatusBadge = pares de tokens do DS (texto/fundo); borda reforça o tom. */
const BADGE: Record<BadgeTone, string> = {
  success: "bg-success-bg text-success border-success",
  warning: "bg-warning-bg text-warning-strong border-warning",
  danger: "bg-danger-bg text-danger border-danger",
  neutral: "bg-subtle text-fg-secondary border-line-subtle",
  info: "bg-info-bg text-info border-info",
};

/** Selo de estado: sempre com texto (a cor não é a única pista). */
export function StatusBadge({ tone = "neutral", children }: { tone?: BadgeTone; children: ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full border px-2 py-0.5 text-label-md ${BADGE[tone]}`}>
      {children}
    </span>
  );
}

export type Kpi = { label: string; value: ReactNode; hint?: ReactNode; tone?: BadgeTone };

/** Faixa de indicadores rápidos (2 colunas no celular, até 4 no desktop). */
export function AdminKpiStrip({ items, label }: { items: Kpi[]; label: string }) {
  return (
    <dl aria-label={label} className="grid grid-cols-2 gap-2 lg:grid-cols-4">
      {items.map((k) => (
        <div
          key={k.label}
          className={`rounded-lg border px-3 py-2 ${k.tone === "danger" ? "border-danger bg-danger-bg" : k.tone === "warning" ? "border-warning bg-warning-subtle" : "border-outline-variant bg-surface-container-low"}`}
        >
          <dt className="text-label-md text-on-surface-variant">{k.label}</dt>
          <dd className="text-headline-md">{k.value}</dd>
          {k.hint ? <dd className="text-label-md text-on-surface-variant">{k.hint}</dd> : null}
        </div>
      ))}
    </dl>
  );
}

export function AdminLoading({ label = "Carregando…" }: { label?: string }) {
  return (
    <p role="status" className="flex items-center gap-2 rounded-xl bg-surface-container-low p-md text-body-md text-on-surface-variant">
      <span aria-hidden="true" className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-outline border-t-transparent" />
      {label}
    </p>
  );
}

export function AdminEmpty({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-outline-variant bg-surface-container-low p-md text-body-md text-on-surface-variant">
      <p>{children}</p>
      {action ? <div className="mt-sm">{action}</div> : null}
    </div>
  );
}

export function AdminError({ children, onRetry }: { children: ReactNode; onRetry?: () => void }) {
  return (
    <div role="alert" className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-danger-bg p-3 text-danger-strong">
      <p className="flex items-center gap-2">
        <AdminIcon name="alert" className="h-5 w-5 shrink-0" />
        {children}
      </p>
      {onRetry ? (
        <button type="button" onClick={onRetry} className="min-h-11 px-2 text-label-md underline">
          Tentar de novo
        </button>
      ) : null}
    </div>
  );
}

/** Aviso de sucesso visível + anúncio para leitor de tela. */
export function AdminNotice({ children }: { children: ReactNode }) {
  return (
    <p role="status" aria-live="polite" className="flex items-center gap-2 rounded-lg bg-success-bg p-3 text-body-md text-success">
      {children ? <AdminIcon name="check" className="h-5 w-5 shrink-0" /> : null}
      {children}
    </p>
  );
}
