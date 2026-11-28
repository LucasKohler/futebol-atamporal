"use client";

import { FormEvent, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { PasswordField } from "@/components/PasswordField";
import { inputClass, primaryButton } from "@/components/admin/ui";
import { api } from "@/lib/api";
import { passwordProblem, registerErrorMessage } from "@/lib/password";
import { tryNormalize } from "@/lib/phone";
import { useSession } from "@/lib/useSession";

type Field = "displayName" | "phone" | "password";

export default function RegisterPage() {
  const router = useRouter();
  const { applyUser } = useSession(false);
  const [displayName, setDisplayName] = useState("");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [field, setField] = useState<Field | null>(null);
  const [loading, setLoading] = useState(false);
  const refs = {
    displayName: useRef<HTMLInputElement>(null),
    phone: useRef<HTMLInputElement>(null),
    password: useRef<HTMLInputElement>(null),
  };

  function fail(message: string, f: Field) {
    setError(message);
    setField(f);
    refs[f].current?.focus();
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setField(null);
    if (displayName.trim().length < 2) return fail("Informe o nome.", "displayName");
    const normalized = tryNormalize(phone);
    if (!normalized.ok) return fail(normalized.error, "phone");
    const problem = passwordProblem(password);
    if (problem) return fail(problem, "password");
    setLoading(true);
    try {
      const user = await api.register(normalized.phone, displayName.trim(), password);
      await applyUser(user);
      router.replace("/");
    } catch (err) {
      setError(err instanceof Error ? registerErrorMessage(err.message, password) : "Não foi possível criar a conta. Tente de novo.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-page px-page-mobile py-lg">
      <div className="w-full max-w-md rounded-card border border-line-subtle bg-card p-lg shadow-elevation-1">
        <div className="mb-lg text-center">
          <p className="text-label-sm uppercase text-fg-brand">Futebol Atemporal</p>
          <h1 className="mt-2 text-headline-lg text-fg">Criar conta</h1>
        </div>
        <form onSubmit={onSubmit} className="flex flex-col gap-md" noValidate>
          <div>
            <label htmlFor="displayName" className="mb-1 block text-label-md text-fg">
              Nome
            </label>
            <input
              ref={refs.displayName}
              id="displayName"
              autoComplete="name"
              required
              minLength={2}
              aria-invalid={field === "displayName" || undefined}
              aria-describedby="displayName-ajuda"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className={`${inputClass} w-full py-3`}
            />
            <p id="displayName-ajuda" className="mt-1 text-label-md text-fg-secondary">
              O nome não pode ser alterado depois.
            </p>
          </div>
          <div>
            <label htmlFor="phone" className="mb-1 block text-label-md text-fg">
              Celular
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
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              className={`${inputClass} w-full py-3`}
            />
          </div>
          <PasswordField
            ref={refs.password}
            id="password"
            label="Senha"
            autoComplete="new-password"
            value={password}
            onChange={setPassword}
            invalid={field === "password"}
            showRules
          />
          {error ? (
            <p role="alert" className="rounded-lg bg-danger-bg px-3 py-2 text-body-md text-danger-strong">
              {error}
            </p>
          ) : null}
          <button type="submit" disabled={loading} className={`${primaryButton} w-full py-3`}>
            {loading ? "Criando…" : "Cadastrar"}
          </button>
        </form>
        <p className="mt-lg text-center text-body-md text-fg-secondary">
          Já tem conta?{" "}
          <Link href="/login" className="font-semibold text-fg-brand underline-offset-2 hover:underline">
            Entrar
          </Link>
        </p>
      </div>
    </div>
  );
}
