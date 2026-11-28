"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect } from "react";
import { NotificationBell } from "@/components/notifications/NotificationBell";
import { InstallAppBanner } from "@/components/pwa/InstallAppBanner";
import { isNavActive } from "@/lib/adminModules";
import { api } from "@/lib/api";
import { takeNotificationParam } from "@/lib/notifications";
import { useSession } from "@/lib/useSession";

const links = [
  { href: "/", label: "Início", icon: "home" },
  { href: "/partida", label: "Partida", icon: "sports_soccer" },
  { href: "/rankings", label: "Rankings", icon: "leaderboard" },
  { href: "/perfil", label: "Perfil", icon: "person" },
];

function NavIcon({ name }: { name: string }) {
  const common = { fill: "none", stroke: "currentColor", strokeWidth: 1.8, strokeLinecap: "round" as const, strokeLinejoin: "round" as const };

  switch (name) {
    case "home":
      return <svg aria-hidden="true" viewBox="0 0 24 24" className="h-[22px] w-[22px]" {...common}><path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1z" /></svg>;
    case "sports_soccer":
      return <svg aria-hidden="true" viewBox="0 0 24 24" className="h-[22px] w-[22px]" {...common}><circle cx="12" cy="12" r="9" /><path d="m12 7 3 2-1 4h-4L9 9zm-3 2-3-1m8 2 3-1m-3 5 2 3m-7-3-2 3m3-4-4 1m9-1 4 1" /></svg>;
    case "leaderboard":
      return <svg aria-hidden="true" viewBox="0 0 24 24" className="h-[22px] w-[22px]" {...common}><path d="M4 19V5m0 14h17M8 15h3V9H8zm6 0h3V5h-3zm6 0h3v-8h-3z" /></svg>;
    case "person":
      return <svg aria-hidden="true" viewBox="0 0 24 24" className="h-[22px] w-[22px]" {...common}><circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" /></svg>;
    default:
      return null;
  }
}

export function AppShell({
  children,
  brand = "Futebol Atemporal",
}: {
  children: React.ReactNode;
  brand?: string;
  /** @deprecated nav unificada — mantido só por compat */
  isAdmin?: boolean;
}) {
  const pathname = usePathname();
  // A barra inferior é a mesma para todos (decisão de design); o admin chega à Central pela Home e por "Gerenciar rodada".
  const { user, group } = useSession(false);
  const navLinks = links;

  // Aberto pelo toque num push: ?n={id} marca o aviso como lido e sai da URL (contrato D7).
  useEffect(() => {
    if (!user) return;
    const { id, cleanUrl } = takeNotificationParam(window.location.href);
    if (!id) return;
    window.history.replaceState(null, "", cleanUrl);
    api.readNotification(id).catch(() => undefined);
  }, [user, pathname]);

  return (
    <div className="min-h-dvh bg-background text-on-background">
      <header className="sticky top-0 z-30 border-b border-outline-variant bg-background">
        <div className="mx-auto flex max-w-container items-center justify-between px-page-mobile py-2 md:px-page-desktop">
          <div className="flex items-center gap-2 text-primary">
            <NavIcon name="sports_soccer" />
            <span className="text-headline-md font-semibold">{brand}</span>
          </div>
          {user && group ? <NotificationBell groupId={group.id} /> : null}
        </div>
      </header>

      {/* A navegação inferior é fixa em todas as larguras: a folga evita que ela cubra o último botão da página. */}
      <main className="mx-auto max-w-container px-page-mobile pb-28 pt-lg md:px-page-desktop">
        {user && pathname !== "/perfil" ? <InstallAppBanner /> : null}
        {children}
      </main>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 border-t border-outline-variant bg-surface-container-lowest"
        aria-label="Principal"
      >
        <ul className="mx-auto flex max-w-lg items-stretch justify-around px-2 py-2">
          {navLinks.map((link) => {
            const active = isNavActive(pathname, link.href);
            return (
              <li key={link.href}>
                <Link
                  href={link.href}
                  aria-current={active ? "page" : undefined}
                  className={`flex min-h-11 min-w-[3.75rem] flex-col items-center gap-1 rounded-full px-2 py-2 text-label-sm uppercase focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-pitch-green-container sm:min-w-[4.5rem] sm:px-3 ${
                    active ? "bg-pitch-green-container text-on-primary" : "text-on-surface-variant"
                  }`}
                >
                  <NavIcon name={link.icon} />
                  <span>{link.label}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
    </div>
  );
}
