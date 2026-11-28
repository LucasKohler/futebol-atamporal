import type { Config } from "tailwindcss";

/**
 * Tema = tokens do Design System (variáveis CSS em src/app/globals.css).
 * Nomes semânticos novos: page, card, subtle, selected, fg-*, line-*, action-*, focus,
 * success/warning/danger/info-*, team-*. Os nomes Material 3 antigos seguem como aliases
 * apontando para o token equivalente, para não exigir refatoração de todas as telas.
 */
const v = (name: string) => `var(--${name})`;

const config: Config = {
  content: ["./src/components/**/*.{js,ts,jsx,tsx,mdx}", "./src/app/**/*.{js,ts,jsx,tsx,mdx}", "./src/lib/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // Semânticos (DS)
        page: v("bg-page"),
        card: v("surface"),
        subtle: v("surface-subtle"),
        selected: v("surface-selected"),
        fg: {
          DEFAULT: v("text-primary"),
          secondary: v("text-secondary"),
          muted: v("text-muted"),
          placeholder: v("text-placeholder"),
          brand: v("text-brand"),
          "on-brand": v("text-on-brand"),
        },
        line: { subtle: v("border-subtle"), control: v("border-control") },
        focus: v("focus-ring"),
        action: { DEFAULT: v("action-primary"), pressed: v("action-primary-pressed"), danger: v("action-danger") },
        success: { DEFAULT: v("success-text"), bg: v("success-bg") },
        warning: { DEFAULT: v("warning-text"), strong: v("warning-text-strong"), bg: v("warning-bg"), subtle: v("warning-subtle") },
        danger: { DEFAULT: v("danger-text"), strong: v("danger-text-strong"), bg: v("danger-bg") },
        info: { DEFAULT: v("info-text"), bg: v("info-bg") },
        team: { red: v("team-red"), black: v("team-black"), green: v("team-green"), "green-text": v("team-green-text") },
        scrim: v("scrim"),

        // Aliases Material 3 (compatibilidade) → tokens do DS
        background: v("bg-page"),
        "on-background": v("text-primary"),
        surface: v("bg-page"),
        "surface-container-lowest": v("surface"),
        "surface-container-low": v("surface-subtle"),
        "surface-container": v("info-bg"),
        "surface-variant": "#d3e4fe",
        "on-surface": v("text-primary"),
        "on-surface-variant": v("text-secondary"),
        primary: v("action-primary-pressed"),
        "on-primary": v("text-on-brand"),
        "pitch-green-container": v("action-primary"),
        secondary: v("text-muted"),
        "secondary-container": v("surface-selected"),
        "on-secondary-container": v("text-muted"),
        error: v("danger-text"),
        "error-container": "#ffdad6",
        "on-error-container": v("danger-text-strong"),
        outline: "#707974",
        "outline-variant": v("border-subtle"),
      },
      fontFamily: {
        sans: ["var(--font-inter)", "system-ui", "sans-serif"],
      },
      fontSize: {
        display: ["36px", { lineHeight: "44px", letterSpacing: "-0.02em", fontWeight: "700" }],
        "headline-lg": ["28px", { lineHeight: "36px", letterSpacing: "-0.01em", fontWeight: "600" }],
        "headline-md": ["20px", { lineHeight: "28px", fontWeight: "600" }],
        subtitle: ["16px", { lineHeight: "24px", fontWeight: "600" }],
        "body-lg": ["16px", { lineHeight: "24px", fontWeight: "400" }],
        "body-md": ["14px", { lineHeight: "20px", fontWeight: "400" }],
        "label-md": ["12px", { lineHeight: "16px", letterSpacing: "0.05em", fontWeight: "600" }],
        // DS: o sobretítulo 10/12 era pequeno demais; agora 12/16 bold.
        "label-sm": ["12px", { lineHeight: "16px", letterSpacing: "0.05em", fontWeight: "700" }],
        caption: ["12px", { lineHeight: "16px", fontWeight: "400" }],
      },
      maxWidth: { container: "1280px" },
      minHeight: { touch: v("touch-min"), "touch-game": v("touch-game") },
      minWidth: { touch: v("touch-min"), "touch-game": v("touch-game") },
      spacing: {
        xs: "4px",
        sm: "8px",
        md: "16px",
        lg: "24px",
        xl: "40px",
        "page-mobile": "24px",
        "page-desktop": "40px",
      },
      borderRadius: {
        // Legado: rounded-xl foi sobrescrito para 24px; telas novas usam badge/control/card.
        xl: "1.5rem",
        badge: v("radius-sm"),
        control: v("radius-md"),
        card: v("radius-lg"),
      },
      // Revisão 27/11: sombras, duração e curvas como tokens (variáveis em globals.css).
      boxShadow: {
        "elevation-0": v("elevation-0"),
        "elevation-1": v("elevation-1"),
        "elevation-2": v("elevation-2"),
        "elevation-3": v("elevation-3"),
        "elevation-sheet": v("elevation-sheet"),
        "elevation-bar": v("elevation-bar"),
        "elevation-bar-top": v("elevation-bar-top"),
      },
      transitionDuration: {
        instant: v("duration-instant"),
        fast: v("duration-fast"),
        base: v("duration-base"),
        slow: v("duration-slow"),
        exit: v("duration-exit"),
        celebrate: v("duration-celebrate"),
      },
      transitionTimingFunction: {
        standard: v("ease-standard"),
        enter: v("ease-enter"),
        exit: v("ease-exit"),
        emphasis: v("ease-emphasis"),
      },
      animation: {
        "score-in": "fa-score-in var(--duration-base) var(--ease-emphasis) both",
        "score-in-down": "fa-score-in-down var(--duration-base) var(--ease-emphasis) both",
        "team-pulse": "fa-team-pulse 280ms var(--ease-standard) 1",
        "sheet-in": "fa-sheet-in var(--duration-slow) var(--ease-enter) both",
        "scrim-in": "fa-fade-in var(--duration-slow) var(--ease-enter) both",
        "rise-in": "fa-rise-in var(--duration-base) var(--ease-enter) both",
        "toast-in": "fa-toast-in var(--duration-base) var(--ease-enter) both",
        "menu-in": "fa-menu-in var(--duration-fast) var(--ease-enter) both",
        "check-draw": "fa-check-draw var(--duration-celebrate) var(--ease-emphasis) both",
        "dot-pulse": "fa-dot-pulse 1s linear infinite",
      },
    },
  },
  plugins: [],
};

export default config;
