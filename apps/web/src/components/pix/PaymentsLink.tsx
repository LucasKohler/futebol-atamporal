import Link from "next/link";
import { focusRing } from "@/components/admin/ui";

/** Atalho para /pagamentos (Home, Partida e Perfil). */
export function PaymentsLink({ text = "Mensalidade e diárias da pelada. Pague com Pix pelo app." }: { text?: string }) {
  return (
    <Link
      href="/pagamentos"
      className={`flex min-h-11 items-center justify-between gap-3 rounded-xl border border-outline-variant bg-surface-container-lowest p-md hover:border-pitch-green-container ${focusRing}`}
    >
      <span>
        <span className="block text-body-lg font-semibold">Pagamentos & Pix</span>
        <span className="block text-body-md text-on-surface-variant">{text}</span>
      </span>
      <span aria-hidden="true" className="text-headline-md text-pitch-green-container">
        →
      </span>
    </Link>
  );
}
