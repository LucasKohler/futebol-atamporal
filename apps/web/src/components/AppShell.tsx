"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/", label: "Início", icon: "home" },
  { href: "/partida", label: "Partida", icon: "sports_soccer" },
  { href: "/rankings", label: "Rankings", icon: "leaderboard" },
  { href: "/perfil", label: "Perfil", icon: "person" },
];

function NavIcon({ name }: { name: string }) {
  return (
    <span className="material-symbols-outlined text-[22px]" aria-hidden>
      {name}
    </span>
  );
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

  return (
    <div className="min-h-dvh bg-background text-on-background">
      <header className="sticky top-0 z-30 border-b border-outline-variant bg-background">
        <div className="mx-auto flex max-w-container items-center justify-between px-page-mobile py-3 md:px-page-desktop">
          <div className="flex items-center gap-2 text-primary">
            <NavIcon name="sports_soccer" />
            <span className="text-headline-md font-semibold">{brand}</span>
          </div>
        </div>
      </header>

      {/* A navegação inferior é fixa em todas as larguras: a folga evita que ela cubra o último botão da página. */}
      <main className="mx-auto max-w-container px-page-mobile pb-28 pt-lg md:px-page-desktop">
        {children}
      </main>

      <nav
        className="fixed inset-x-0 bottom-0 z-30 border-t border-outline-variant bg-surface-container-lowest"
        aria-label="Principal"
      >
        <ul className="mx-auto flex max-w-lg items-stretch justify-around px-2 py-2">
          {links.map((link) => {
            const active = pathname === link.href;
            return (
              <li key={link.href}>
                <Link
                  href={link.href}
                  className={`flex min-w-[4.5rem] flex-col items-center gap-1 rounded-full px-3 py-2 text-label-sm uppercase ${
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
