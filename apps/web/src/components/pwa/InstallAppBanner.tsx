"use client";

import { useEffect, useState } from "react";
import { primaryButton, secondaryButton, focusRing } from "@/components/admin/ui";
import { hasInstallPrompt, onInstallPromptChange, promptInstall } from "@/lib/install";
import { dismissUntil, INSTALL_DISMISS_KEY, installBannerMode, isDismissed, type InstallMode } from "@/lib/notifications";
import { currentPlatform, isStandaloneDisplay } from "@/lib/push";

/**
 * Convite para instalar o app.
 * - `banner`: no topo das telas; dispensar esconde por 14 dias (preferência local, não é dado sensível).
 * - `card`: no Perfil; sempre mostra o estado, inclusive "já instalado".
 */
export function InstallAppBanner({ variant = "banner" }: { variant?: "banner" | "card" }) {
  const [mode, setMode] = useState<InstallMode | "installed" | null>(null);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    const compute = () => {
      const standalone = isStandaloneDisplay();
      if (standalone) return setMode(variant === "card" ? "installed" : "hidden");
      const dismissed = variant === "banner" && isDismissed(localStorage.getItem(INSTALL_DISMISS_KEY), Date.now());
      setMode(installBannerMode({ standalone, hasPrompt: hasInstallPrompt(), platform: currentPlatform(), dismissed }));
    };
    compute();
    return onInstallPromptChange(compute);
  }, [variant]);

  if (!mode || mode === "hidden") {
    if (variant === "card" && mode === "hidden") {
      return (
        <section aria-labelledby="instalar-titulo" className="rounded-xl border border-outline-variant bg-surface-container-lowest p-md">
          <h2 id="instalar-titulo" className="text-body-lg font-semibold">
            📲 App no celular
          </h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Abra este endereço no Chrome (Android) ou no Safari (iPhone) para instalar o app na tela inicial.
          </p>
        </section>
      );
    }
    return null;
  }

  if (mode === "installed") {
    return (
      <section className="rounded-xl border border-green-700 bg-green-50 p-md text-green-900">
        <p className="text-body-md font-semibold">✓ App instalado neste aparelho.</p>
      </section>
    );
  }

  const dismiss = () => {
    localStorage.setItem(INSTALL_DISMISS_KEY, String(dismissUntil(Date.now())));
    setMode("hidden");
  };

  return (
    <section
      aria-labelledby={`instalar-${variant}`}
      className={`flex flex-col gap-2 rounded-xl border-2 border-pitch-green-container bg-surface-container-lowest p-md ${variant === "banner" ? "mb-lg" : ""}`}
    >
      <div className="flex items-start justify-between gap-2">
        <h2 id={`instalar-${variant}`} className="text-body-lg font-semibold">
          📲 Instale o Futebol Atemporal
        </h2>
        {variant === "banner" ? (
          <button type="button" onClick={dismiss} className={`min-h-11 min-w-11 rounded-full text-headline-md ${focusRing}`} aria-label="Dispensar convite de instalação por 14 dias">
            ×
          </button>
        ) : null}
      </div>
      {mode === "prompt" ? (
        <>
          <p className="text-body-md text-on-surface-variant">Abra a pelada com 1 toque, em tela cheia, e receba os avisos da lista e do sorteio.</p>
          <button
            type="button"
            className={`${primaryButton} self-start`}
            onClick={async () => {
              const r = await promptInstall();
              setMsg(r === "accepted" ? "Instalando… o ícone aparece na tela inicial." : r === "dismissed" ? "Tudo bem, você pode instalar depois." : "Instalação indisponível agora.");
            }}
          >
            📲 Instalar App
          </button>
        </>
      ) : (
        <>
          <p className="text-body-md text-on-surface-variant">No iPhone, instale pelo Safari em 2 passos (necessário para receber avisos no celular):</p>
          <ol className="flex flex-col gap-1 text-body-md">
            <li className="flex items-center gap-2">
              <span aria-hidden="true" className="flex h-7 w-7 items-center justify-center rounded-full bg-pitch-green-container text-label-md text-on-primary">1</span>
              Toque em <strong>Compartilhar</strong>
              <svg aria-label="ícone Compartilhar" role="img" viewBox="0 0 24 24" className="h-5 w-5" fill="none" stroke="currentColor" strokeWidth="1.8">
                <path d="M12 3v12M8 7l4-4 4 4M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-7" />
              </svg>
              na barra do Safari.
            </li>
            <li className="flex items-center gap-2">
              <span aria-hidden="true" className="flex h-7 w-7 items-center justify-center rounded-full bg-pitch-green-container text-label-md text-on-primary">2</span>
              Escolha <strong>Adicionar à Tela de Início</strong> e confirme.
            </li>
          </ol>
          {variant === "banner" ? (
            <button type="button" onClick={dismiss} className={`${secondaryButton} self-start`}>
              Agora não
            </button>
          ) : null}
        </>
      )}
      {msg ? (
        <p role="status" className="text-body-md">
          {msg}
        </p>
      ) : null}
    </section>
  );
}
