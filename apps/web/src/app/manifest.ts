import type { MetadataRoute } from "next";

/** Manifesto PWA (contrato 2026-11-26, seção 7): instalável em modo standalone, ícones locais. */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Futebol Atemporal",
    short_name: "FutAtemporal",
    description: "Lista da pelada, sorteio, súmula ao vivo, pagamentos e avisos do seu grupo.",
    start_url: "/",
    scope: "/",
    display: "standalone",
    orientation: "portrait",
    lang: "pt-BR",
    theme_color: "#065f46",
    background_color: "#002d1f",
    icons: [
      { src: "/icons/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
