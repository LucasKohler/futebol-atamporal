"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { api } from "@/lib/api";
import { tryNormalize } from "@/lib/phone";
import { useSession } from "@/lib/useSession";

export default function LoginPage() {
  const router = useRouter();
  const { applyUser } = useSession(false);
  const [phone, setPhone] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const normalized = tryNormalize(phone);
    if (!normalized.ok) {
      setError(normalized.error);
      return;
    }
    setLoading(true);
    try {
      const user = await api.login(normalized.phone);
      await applyUser(user);
      router.replace("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Falha no login");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-background px-page-mobile">
      <div className="w-full max-w-md rounded-xl border border-outline-variant bg-surface-container-lowest p-lg shadow-sm">
        <div className="mb-lg text-center">
          <p className="text-label-sm uppercase text-pitch-green-container">Futebol Atemporal</p>
          <h1 className="mt-2 text-headline-lg text-on-background">Entrar</h1>
        </div>
        <form onSubmit={onSubmit} className="flex flex-col gap-md" noValidate>
          <div>
            <label htmlFor="phone" className="mb-1 block text-label-md text-on-surface">
              Celular
            </label>
            <input
              id="phone"
              type="tel"
              inputMode="numeric"
              autoComplete="tel"
              required
              placeholder="11 99999-9999"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              className="w-full rounded-lg border border-outline-variant bg-surface px-3 py-3 text-body-md"
            />
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
            {loading ? "Entrando…" : "Entrar"}
          </button>
        </form>
        <p className="mt-lg text-center text-body-md text-on-surface-variant">
          Não tem conta?{" "}
          <Link href="/cadastro" className="font-semibold text-primary underline-offset-2 hover:underline">
            Cadastre-se
          </Link>
        </p>
      </div>
    </div>
  );
}