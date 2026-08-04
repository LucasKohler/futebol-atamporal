"use client";

import { createContext, useContext, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { api, Group, User } from "@/lib/api";

type Session = {
  user: User | null;
  group: Group | null;
  loading: boolean;
  error: string | null;
  isAdmin: boolean;
  refresh: () => Promise<void>;
};

const SessionContext = createContext<Session | null>(null);

export function SessionProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [group, setGroup] = useState<Group | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    const me = await api.me();
    if (!me) {
      setUser(null);
      setGroup(null);
      return;
    }
    setUser(me);
    setGroup(await api.currentGroup());
  }

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const me = await api.me();
        if (!me) {
          if (!cancelled) setLoading(false);
          return;
        }
        const g = await api.currentGroup();
        if (!cancelled) {
          setUser(me);
          setGroup(g);
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : "Erro de sessão");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const isAdmin = user?.memberships.some((m) => m.role === "Admin") ?? false;

  return (
    <SessionContext.Provider value={{ user, group, loading, error, isAdmin, refresh }}>
      {children}
    </SessionContext.Provider>
  );
}

export function useSession(requireAuth = true) {
  const ctx = useContext(SessionContext);
  const router = useRouter();
  if (!ctx) throw new Error("useSession fora do SessionProvider");

  useEffect(() => {
    if (requireAuth && !ctx.loading && !ctx.user) {
      router.replace("/login");
    }
  }, [requireAuth, ctx.loading, ctx.user, router]);

  return ctx;
}

export function matchStatusLabel(status: string) {
  const map: Record<string, string> = {
    Draft: "Rascunho",
    RegistrationOpen: "Inscrições abertas",
    RegistrationClosed: "Inscrições encerradas",
    Drawn: "Times sorteados",
    InProgress: "Em andamento",
    AwaitingClose: "Aguardando fechamento",
    Completed: "Concluída",
    Cancelled: "Cancelada",
  };
  return map[status] ?? status;
}

export function registrationStatusLabel(status: string | null | undefined) {
  if (!status) return "Não inscrito";
  const map: Record<string, string> = {
    Confirmed: "Confirmado",
    Waitlisted: "Na fila",
    Withdrawn: "Desistiu",
    Removed: "Removido",
    Absent: "Ausente",
    Participated: "Participou",
  };
  return map[status] ?? status;
}
