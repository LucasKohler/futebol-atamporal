"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import { useSession } from "@/lib/useSession";

export default function RegisterPage() {
  const router = useRouter();
  const { applyUser } = useSession(false);
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const user = await api.register(email, password, displayName);
      await applyUser(user);
      router.replace("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Falha no cadastro");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-background px-page-mobile">
      <div className="w-full max-w-md rounded-xl border border-outline-variant bg-surface-container-lowest p-lg shadow-sm">
        <div className="mb-lg text-center">
          <p className="text-label-sm uppercase text-pitch-green-container">Futebol Atemporal</p>
          <h1 className="mt-2 text-headline-lg text-on-background">Criar conta</h1>
        </div>
        <form onSubmit={onSubmit} className="flex flex-col gap-md" noValidate>
          <div>
            <label htmlFor="displayName" className="mb-1 block text-label-md text-on-surface">
              Nome
            </label>
            <input
              id="displayName"
              required
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className="w-full rounded-lg border border-outline-variant bg-surface px-3 py-3 text-body-md"
            />
          </div>
          <div>
            <label htmlFor="email" className="mb-1 block text-label-md text-on-surface">
              Email
            </label>
            <input
              id="email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-lg border border-outline-variant bg-surface px-3 py-3 text-body-md"
            />
          </div>
          <div>
            <label htmlFor="password" className="mb-1 block text-label-md text-on-surface">
              Senha
            </label>
            <input
              id="password"
              type="password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-lg border border-outline-variant bg-surface px-3 py-3 text-body-md"
            />
            <p className="mt-1 text-label-md text-on-surface-variant">Mínimo 8 caracteres, com maiúscula e número.</p>
          </div>
          {error ? (
            <p role="alert" className="rounded-lg bg-error-container px-3 py-2 text-body-md text-on-error-container">
              {error}
            </p>
          ) : null}
          <button
            type="submit"
            disabled={loading}
            className="rounded-full bg-pitch-green-container px-4 py-3 text-label-md text-on-primary disabled:opacity-60"
          >
            {loading ? "Criando…" : "Cadastrar"}
          </button>
        </form>
        <p className="mt-lg text-center text-body-md text-on-surface-variant">
          Já tem conta?{" "}
          <Link href="/login" className="font-semibold text-primary underline-offset-2 hover:underline">
            Entrar
          </Link>
        </p>
      </div>
    </div>
  );
}
