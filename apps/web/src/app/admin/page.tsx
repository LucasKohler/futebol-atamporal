"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

/** Dashboard antigo redireciona para o hub (evita nav duplicada). */
export default function AdminRedirect() {
  const router = useRouter();
  useEffect(() => {
    router.replace("/");
  }, [router]);
  return <div className="flex min-h-dvh items-center justify-center text-on-surface-variant">Redirecionando…</div>;
}
