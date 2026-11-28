"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { PasswordField } from "@/components/PasswordField";
import { focusRing, inputClass, primaryButton } from "@/components/admin/ui";
import { api, ApiError } from "@/lib/api";
import { adminRecoveryError, adminSetupError, passwordProblem } from "@/lib/password";
import { tryNormalize } from "@/lib/phone";
import { useSession } from "@/lib/useSession";

type Field = "setupCode" | "phone" | "password";
type Mode = "setup" | "recovery";

const COPY: Record<Mode, { title: string; codeLabel: string; passwordLabel: string; submit: string; busy: string; missingCode: string }> = {
  setup: {
    title: "Configuração inicial do admin",
    codeLabel: "Código de configuração",
    passwordLabel: "Senha do admin",
    submit: "Criar acesso do admin",
    busy: "Configurando…",
    missingCode: "Informe o código de configuração.",
  },
  recovery: {
    title: "Recuperar acesso do admin",
    codeLabel: "Código de recuperação",
    passwordLabel: "Nova senha do admin",
    submit: "Trocar senha do admin",
    busy: "Trocando…",
    missingCode: "Informe o código de recuperação.",
  },
};

/**
 * Acesso do único admin, sem depender de outro admin (decisões do Lucas):
 * - Configuração inicial: código de configuração de uso único; depois de usado, nunca mais vale.
 * - Recuperação: código temporário de uso único que quem opera o servidor define quando o admin esquece a senha
 *   (vale no máximo 10 minutos). Não reativa a configuração inicial.
 * Os códigos vivem só no servidor; aqui existem apenas no campo enquanto a tela está aberta. Nada é guardado no navegador.
 */
export default function AdminSetupPage() {
  const router = useRouter();
  const { applyUser } = useSession(false);
  const [mode, setMode] = useState<Mode>("setup");
  const [code, setCode] = useState("");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [field, setField] = useState<Field | null>(null);
  const [closed, setClosed] = useState<{ mode: Mode; message: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const refs = {
    setupCode: useRef<HTMLInputElement>(null),
    phone: useRef<HTMLInputElement>(null),
    password: useRef<HTMLInputElement>(null),
  };
  const copy = COPY[mode];

  // Foco no campo com problema depois que os campos voltam a ficar habilitados (durante o envio ficam desabilitados).
  const [focusTick, setFocusTick] = useState(0);
  useEffect(() => {
    if (!loading && field) refs[field].current?.focus();
  }, [focusTick, loading]); // eslint-disable-line react-hooks/exhaustive-deps

  // Link direto para a recuperação: /configuracao-inicial?modo=recuperacao
  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("modo") === "recuperacao") setMode("recovery");
  }, []);

  function fail(message: string, f: Field | null) {
    setError(message);
    setField(f);
    setFocusTick((t) => t + 1);
  }

  function switchMode(next: Mode) {
    setMode(next);
    setCode("");
    setPassword("");
    setError(null);
    setField(null);
    setClosed(null);
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setField(null);
    if (!code.trim()) return fail(copy.missingCode, "setupCode");
    const normalized = tryNormalize(phone);
    if (!normalized.ok) return fail(normalized.error, "phone");
    const problem = passwordProblem(password);
    if (problem) return fail(problem, "password");
    setLoading(true);
    try {
      const user =
        mode === "setup"
          ? await api.adminSetup(code.trim(), normalized.phone, password)
          : await api.adminRecovery(code.trim(), normalized.phone, password);
      setCode("");
      setPassword("");
      await applyUser(user);
      router.replace("/admin");
    } catch (err) {
      const apiCode = err instanceof ApiError ? err.code : null;
      const fallback = err instanceof Error ? err.message : "Não foi possível concluir. Tente de novo.";
      const r = mode === "setup" ? adminSetupError(apiCode, fallback, password) : adminRecoveryError(apiCode, fallback, password);
      if (r.final) {
        setCode("");
        setPassword("");
        setClosed({ mode, message: r.message });
      } else if ("setupPending" in r && r.setupPending) {
        // Recuperação pedida antes de existir admin: leva para a configuração inicial, sem reaproveitar o código.
        switchMode("setup");
        setError(r.message);
      } else {
        // Código errado é limpo e recebe o foco; nos demais erros o código fica só no campo para corrigir o resto.
        if (r.field === "setupCode") setCode("");
        fail(r.message, r.field);
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-page px-page-mobile py-lg">
      <div className="w-full max-w-md rounded-card border border-line-subtle bg-card p-lg shadow-elevation-1">
        <div className="mb-md text-center">
          <p className="text-label-sm uppercase text-fg-brand">Futebol Atemporal</p>
          <h1 className="mt-2 text-headline-lg text-fg">{copy.title}</h1>
        </div>

        <div role="group" aria-label="O que você precisa fazer" className="mb-md grid grid-cols-2 gap-1 rounded-full bg-selected p-1">
          {(["setup", "recovery"] as Mode[]).map((m) => (
            <button
              key={m}
              type="button"
              aria-pressed={mode === m}
              disabled={loading}
              onClick={() => switchMode(m)}
              className={`min-h-touch rounded-full px-3 text-label-md ${mode === m ? "bg-action text-fg-on-brand" : "text-fg"} ${focusRing}`}
            >
              {m === "setup" ? "Primeiro acesso" : "Esqueci a senha"}
            </button>
          ))}
        </div>

        {closed && closed.mode === mode ? (
          <div className="flex flex-col gap-md">
            <p role="alert" className="rounded-lg bg-warning-subtle px-3 py-2 text-body-md text-warning-strong">
              {closed.message}
            </p>
            <Link href="/login" className={`${primaryButton} w-full py-3`}>
              Ir para o login
            </Link>
          </div>
        ) : (
          <>
            <div className="mb-md flex flex-col gap-1 rounded-lg bg-subtle p-3 text-body-md text-fg-secondary">
              {mode === "setup" ? (
                <>
                  <p>Use uma única vez, para criar a senha do primeiro admin.</p>
                  <p>
                    O código de configuração é fornecido por quem opera o servidor. Depois que o admin for criado, o código deixa de
                    valer e esta opção fica desativada.
                  </p>
                </>
              ) : (
                <>
                  <p>Para o admin que esqueceu a senha. Jogadores pedem a redefinição ao admin.</p>
                  <p>
                    Peça a quem opera o servidor um código temporário: ele vale uma única vez e por poucos minutos. Ao trocar a
                    senha, as sessões abertas do admin são encerradas.
                  </p>
                </>
              )}
            </div>
            <form onSubmit={onSubmit} className="flex flex-col gap-md" noValidate>
              <PasswordField
                ref={refs.setupCode}
                id="setupCode"
                label={copy.codeLabel}
                autoComplete="one-time-code"
                value={code}
                onChange={setCode}
                invalid={field === "setupCode"}
                disabled={loading}
              />
              <div>
                <label htmlFor="phone" className="mb-1 block text-label-md text-fg">
                  Celular do admin
                </label>
                <input
                  ref={refs.phone}
                  id="phone"
                  type="tel"
                  inputMode="numeric"
                  autoComplete="tel"
                  required
                  placeholder="11 99999-9999"
                  aria-invalid={field === "phone" || undefined}
                  disabled={loading}
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  className={`${inputClass} w-full py-3`}
                />
              </div>
              <PasswordField
                ref={refs.password}
                id="password"
                label={copy.passwordLabel}
                autoComplete="new-password"
                value={password}
                onChange={setPassword}
                invalid={field === "password"}
                disabled={loading}
                showRules
              />
              {error ? (
                <p role="alert" className="rounded-lg bg-danger-bg px-3 py-2 text-body-md text-danger-strong">
                  {error}
                </p>
              ) : null}
              <button type="submit" disabled={loading} className={`${primaryButton} w-full py-3`}>
                {loading ? copy.busy : copy.submit}
              </button>
            </form>
            <p className="mt-lg text-center text-body-md text-fg-secondary">
              Lembrou a senha?{" "}
              <Link href="/login" className="font-semibold text-fg-brand underline-offset-2 hover:underline">
                Entrar
              </Link>
            </p>
          </>
        )}
      </div>
    </div>
  );
}
