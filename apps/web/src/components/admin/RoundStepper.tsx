"use client";

import Link from "next/link";
import type { RoundStepView } from "@/lib/roundStep";
import { AdminIcon } from "./AdminIcon";
import { primaryButton } from "./ui";

/**
 * Stepper da rodada semanal. Em telas da própria etapa, `hideCtaOn` evita um CTA que leva para a página atual.
 * `onTransition` (opcional) executa a transição no lugar; sem ele, o CTA leva para a tela que tem a ação.
 */
export function RoundStepper({
  state,
  onTransition,
  busy,
  hideCtaOn,
  compact,
}: {
  state: RoundStepView;
  onTransition?: (to: string) => void;
  busy?: boolean;
  hideCtaOn?: string;
  compact?: boolean;
}) {
  const cta = state.cta;
  const showCta = cta && !(hideCtaOn && cta.href === hideCtaOn && cta.kind === "link");

  return (
    <nav aria-label="Etapas da rodada semanal" className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
      <ol className="grid grid-cols-4 gap-1">
        {state.steps.map((s) => {
          const current = s.status === "current";
          const done = s.status === "done";
          return (
            <li key={s.step} aria-current={current ? "step" : undefined} className="flex min-w-0 flex-col items-center gap-1 text-center">
              <span
                className={`flex h-9 w-9 items-center justify-center rounded-full border-2 text-label-md ${
                  done
                    ? "border-pitch-green-container bg-pitch-green-container text-on-primary"
                    : current
                      ? "border-pitch-green-container bg-surface-container-lowest text-pitch-green-container"
                      : "border-outline-variant text-on-surface-variant"
                }`}
              >
                {done ? <AdminIcon name="check" className="h-4 w-4" /> : s.step}
              </span>
              <span className={`w-full text-label-sm uppercase sm:text-label-md sm:normal-case ${current ? "text-on-surface" : "text-on-surface-variant"}`}>
                <span className="sm:hidden">{s.short}</span>
                <span className="hidden sm:inline">{s.label}</span>
                <span className="sr-only">{done ? " (concluída)" : current ? " (etapa atual)" : " (a seguir)"}</span>
              </span>
            </li>
          );
        })}
      </ol>
      {!compact || state.blocker || showCta ? (
        <div className="mt-md flex flex-col gap-2 border-t border-outline-variant pt-md sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-body-md font-semibold">{state.headline}</p>
            {state.blocker ? (
              <p className="mt-1 flex items-start gap-1 text-body-md text-on-error-container">
                <AdminIcon name="alert" className="mt-0.5 h-4 w-4 shrink-0" />
                {state.blocker}
              </p>
            ) : null}
          </div>
          {showCta && cta ? (
            cta.kind === "transition" && onTransition ? (
              <button type="button" disabled={busy} onClick={() => onTransition(cta.to)} className={primaryButton}>
                {cta.label}
                <AdminIcon name="arrow" className="h-4 w-4" />
              </button>
            ) : (
              <Link href={cta.href} className={primaryButton}>
                {cta.label}
                <AdminIcon name="arrow" className="h-4 w-4" />
              </Link>
            )
          ) : null}
        </div>
      ) : null}
    </nav>
  );
}
