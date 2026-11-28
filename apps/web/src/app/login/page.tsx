"use client";

import { FormEvent, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { PasswordField } from "@/components/PasswordField";
import { inputClass, primaryButton } from "@/components/admin/ui";
import { api, ApiError } from "@/lib/api";
import { loginErrorMessage } from "@/lib/password";
import { tryNormalize } from "@/lib/phone";
import { useSession } from "@/lib/useSession";

export default function LoginPage() {
  const router = useRouter();
  const { applyUser } = useSession(false);
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [field, setField] = useState<"phone" | "password" | null>(null);
  const [loading, setLoading] = useState(false);
  const phoneRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setField(null);
    const normalized = tryNormalize(phone);
    if (!normalized.ok) {
      setError(normalized.error);
      setField("phone");
      phoneRef.current?.focus();
      return;
    }
    if (!password) {
      setError("Informe a senha.");
      setField("password");
      passwordRef.current?.focus();
      return;
    }
    setLoading(true);
    try {
      const user = await api.login(normalized.phone, password);
      await applyUser(user);
      router.replace("/");
    } catch (err) {
      // 401 com code: invalid_credentials (genérico, não revela se o celular existe), locked_out, invalid_phone.
      const fallback = err instanceof Error ? err.message : "Não foi possível entrar. Tente de novo.";
      setError(loginErrorMessage(err instanceof ApiError ? err.code : null, fallback));
      setPassword("");
      passwordRef.current?.focus();
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-page px-page-mobile">
      <div className="w-full max-w-md rounded-card border border-line-subtle bg-card p-lg shadow-elevation-1">
        <div className="mb-lg text-center">
          <p className="text-label-sm uppercase text-fg-brand">Futebol Atemporal</p>
          <h1 className="mt-2 text-headline-lg text-fg">Entrar</h1>
        </div>
        <form onSubmit={onSubmit} className="flex flex-col gap-md" noValidate aria-describedby={error ? "login-erro" : undefined}>
          <div>
            <label htmlFor="phone" className="mb-1 block text-label-md text-fg">
              Celular
            </label>
            <input
              ref={phoneRef}
              id="phone"
              type="tel"
              inputMode="numeric"
              autoComplete="tel"
              required
              placeholder="11 99999-9999"
              aria-invalid={field === "phone" || undefined}
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              className={`${inputClass} w-full py-3`}
            />
          </div>
          <PasswordField
            ref={passwordRef}
            id="password"
            label="Senha"
            autoComplete="current-password"
            value={password}
            onChange={setPassword}
            invalid={field === "password"}
          />
          {error ? (
            <p id="login-erro" role="alert" className="rounded-lg bg-danger-bg px-3 py-2 text-body-md text-danger-strong">
              {error}
            </p>
          ) : null}
          <button type="submit" disabled={loading} className={`${primaryButton} w-full py-3`}>
            {loading ? "Entrando…" : "Entrar"}
          </button>
          <p className="text-body-md text-fg-secondary">Esqueceu a senha? Peça ao admin do grupo para redefinir.</p>
        </form>
        <p className="mt-lg text-center text-body-md text-fg-secondary">
          Não tem conta?{" "}
          <Link href="/cadastro" className="font-semibold text-fg-brand underline-offset-2 hover:underline">
            Cadastre-se
          </Link>
        </p>
      </div>
    </div>
  );
}
