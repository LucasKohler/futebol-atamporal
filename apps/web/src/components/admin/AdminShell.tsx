"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { AppShell } from "@/components/AppShell";
import { ADMIN_MODULES, getAdminModule, sortAdminModules } from "@/lib/adminModules";
import { useSession } from "@/lib/useSession";
import { AdminLoading } from "./AdminCard";
import { AdminIcon } from "./AdminIcon";
import { focusRing } from "./ui";

/**
 * Moldura de toda tela administrativa: verifica se a pessoa é admin do grupo ativo, mostra trilha
 * (Admin › módulo), título, ações e o menu de módulos gerado pelo registro.
 * É só experiência de uso: cada chamada continua autorizada pela API.
 */
export function AdminShell({
  moduleId,
  title,
  description,
  actions,
  children,
}: {
  /** Id do módulo em ADMIN_MODULES; ausente = hub. */
  moduleId?: string;
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
}) {
  const { loading, isAdmin, user } = useSession();
  const current = moduleId ? getAdminModule(moduleId) : undefined;

  if (loading || !user) {
    return (
      <AppShell>
        <AdminLoading />
      </AppShell>
    );
  }

  if (!isAdmin) {
    return (
      <AppShell>
        <div role="alert" className="rounded-xl border border-outline-variant bg-surface-container-low p-lg">
          <h1 className="text-headline-md">Área restrita</h1>
          <p className="mt-1 text-body-md text-on-surface-variant">Só administradores do grupo acessam o painel de admin.</p>
          <Link href="/" className={`mt-md inline-flex min-h-11 items-center text-label-md underline ${focusRing}`}>
            Voltar ao início
          </Link>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-lg">
        <header className="flex flex-col gap-sm">
          <nav aria-label="Trilha">
            <ol className="flex items-center gap-1 text-label-md text-on-surface-variant">
              <li>
                {current ? (
                  <Link href="/admin" className={`inline-flex min-h-11 items-center gap-1 underline ${focusRing}`}>
                    <AdminIcon name="admin" className="h-4 w-4" />
                    Admin
                  </Link>
                ) : (
                  <span aria-current="page" className="inline-flex min-h-11 items-center gap-1">
                    <AdminIcon name="admin" className="h-4 w-4" />
                    Admin
                  </span>
                )}
              </li>
              {current ? (
                <>
                  <li aria-hidden="true">›</li>
                  <li aria-current="page">{current.title}</li>
                </>
              ) : null}
            </ol>
          </nav>
          <div className="flex flex-wrap items-end justify-between gap-2">
            <div className="min-w-0">
              <h1 className="text-headline-lg">{title}</h1>
              {description ? <p className="mt-1 max-w-2xl text-body-md text-on-surface-variant">{description}</p> : null}
            </div>
            {actions ? <div className="flex flex-wrap gap-2">{actions}</div> : null}
          </div>
          <AdminModuleNav currentId={moduleId} />
        </header>
        {children}
      </div>
    </AppShell>
  );
}

/** Menu horizontal de módulos (rolável no celular), gerado do registro. */
export function AdminModuleNav({ currentId }: { currentId?: string }) {
  const modules = sortAdminModules(ADMIN_MODULES);
  return (
    <nav aria-label="Módulos do admin" className="-mx-1 overflow-x-auto px-1 pb-1">
      <ul className="flex gap-2">
        <li>
          <Link
            href="/admin"
            aria-current={!currentId ? "page" : undefined}
            className={`inline-flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap rounded-full border px-3 text-label-md ${
              !currentId ? "border-pitch-green-container bg-pitch-green-container text-on-primary" : "border-outline-variant bg-surface-container-lowest"
            } ${focusRing}`}
          >
            <AdminIcon name="admin" className="h-4 w-4" />
            Central
          </Link>
        </li>
        {modules.map((m) => {
          const active = m.id === currentId;
          return (
            <li key={m.id}>
              <Link
                href={m.href}
                aria-current={active ? "page" : undefined}
                className={`inline-flex min-h-11 shrink-0 items-center gap-2 whitespace-nowrap rounded-full border px-3 text-label-md ${
                  active ? "border-pitch-green-container bg-pitch-green-container text-on-primary" : "border-outline-variant bg-surface-container-lowest"
                } ${focusRing}`}
              >
                <AdminIcon name={m.icon} className="h-4 w-4" />
                {m.title}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
