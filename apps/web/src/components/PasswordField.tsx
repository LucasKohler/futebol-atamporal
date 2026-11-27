"use client";

import { forwardRef, useState } from "react";
import { focusRing, inputClass } from "@/components/admin/ui";
import { passwordChecks } from "@/lib/password";

/**
 * Campo de senha com mostrar/ocultar (botão com aria-pressed) e, opcionalmente, a lista de requisitos
 * da política atual ligada ao campo por aria-describedby.
 */
export const PasswordField = forwardRef<
  HTMLInputElement,
  {
    id: string;
    label: string;
    value: string;
    onChange: (value: string) => void;
    autoComplete: "current-password" | "new-password" | "one-time-code";
    showRules?: boolean;
    invalid?: boolean;
    disabled?: boolean;
  }
>(function PasswordField({ id, label, value, onChange, autoComplete, showRules, invalid, disabled }, ref) {
  const [visible, setVisible] = useState(false);
  const rulesId = `${id}-regras`;
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-label-md text-fg">
        {label}
      </label>
      <div className="flex items-stretch gap-2">
        <input
          ref={ref}
          id={id}
          type={visible ? "text" : "password"}
          autoComplete={autoComplete}
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          required
          disabled={disabled}
          aria-invalid={invalid || undefined}
          aria-describedby={showRules ? rulesId : undefined}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className={`${inputClass} min-w-0 flex-1 py-3`}
        />
        <button
          type="button"
          aria-pressed={visible}
          aria-controls={id}
          disabled={disabled}
          onClick={() => setVisible((v) => !v)}
          className={`min-h-touch shrink-0 rounded-lg border border-line-control bg-card px-3 text-label-md text-fg hover:bg-subtle disabled:opacity-50 ${focusRing}`}
        >
          {visible ? "Ocultar" : "Mostrar"}
          <span className="sr-only"> senha</span>
        </button>
      </div>
      {showRules ? (
        <ul id={rulesId} className="mt-1 flex flex-col gap-0.5 text-label-md" aria-label="Requisitos da senha">
          {passwordChecks(value).map((c) => (
            <li key={c.id} className={c.ok ? "text-success" : "text-fg-secondary"}>
              <span aria-hidden="true">{c.ok ? "✓ " : "• "}</span>
              {c.label}
              <span className="sr-only">{c.ok ? " (atendido)" : " (pendente)"}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
});
