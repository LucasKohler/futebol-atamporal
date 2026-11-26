import type { AdminModuleIcon } from "@/lib/adminModules";

type IconName = AdminModuleIcon | "admin" | "check" | "alert" | "chevron" | "search" | "arrow";

/** Ícones SVG locais (sem fonte externa), decorativos: o texto ao lado sempre nomeia a ação. */
export function AdminIcon({ name, className = "h-5 w-5" }: { name: IconName; className?: string }) {
  const common = {
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 1.8,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true,
    viewBox: "0 0 24 24",
    className,
  };
  switch (name) {
    case "admin":
      return <svg {...common}><path d="M12 3 4 6v6c0 4.5 3.4 8.3 8 9 4.6-.7 8-4.5 8-9V6z" /><path d="m9 12 2 2 4-4" /></svg>;
    case "list":
      return <svg {...common}><path d="M9 6h11M9 12h11M9 18h11" /><path d="m4 6 1 1 2-2M4 12l1 1 2-2M4 18l1 1 2-2" /></svg>;
    case "shuffle":
      return <svg {...common}><path d="M16 3h5v5M4 20 21 3M21 16v5h-5M15 15l6 6M4 4l5 5" /></svg>;
    case "scoreboard":
      return <svg {...common}><rect x="3" y="5" width="18" height="14" rx="2" /><path d="M12 5v14M7 10v4M16 10h2v4h-2" /></svg>;
    case "people":
      return <svg {...common}><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 7M18 14a6 6 0 0 1 3.5 6" /></svg>;
    case "wallet":
      return <svg {...common}><path d="M4 7h15a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h12v3" /><path d="M16 13.5h.01" /></svg>;
    case "settings":
      return <svg {...common}><circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7 7M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1" /></svg>;
    case "check":
      return <svg {...common}><path d="m5 12 5 5 9-10" /></svg>;
    case "alert":
      return <svg {...common}><path d="M12 3 2 20h20z" /><path d="M12 10v4M12 17h.01" /></svg>;
    case "chevron":
      return <svg {...common}><path d="m6 9 6 6 6-6" /></svg>;
    case "search":
      return <svg {...common}><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></svg>;
    case "arrow":
      return <svg {...common}><path d="M5 12h14M13 6l6 6-6 6" /></svg>;
    default:
      return null;
  }
}
