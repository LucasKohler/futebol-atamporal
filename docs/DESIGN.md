# Design System — Futebol Atemporal

Fonte canônica de tokens: exportação Stitch (`pitch_performance/DESIGN.md`), adaptada ao produto **Futebol Atemporal** (marca de UI; o design system original chama-se PitchPerfect Elite).

---

name: Futebol Atemporal
colors:
  surface: '#f8f9ff'
  surface-dim: '#cbdbf5'
  surface-bright: '#f8f9ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#eff4ff'
  surface-container: '#e5eeff'
  surface-container-high: '#dce9ff'
  surface-container-highest: '#d3e4fe'
  on-surface: '#0b1c30'
  on-surface-variant: '#404944'
  inverse-surface: '#213145'
  inverse-on-surface: '#eaf1ff'
  outline: '#707974'
  outline-variant: '#bfc9c2'
  surface-tint: '#2f6953'
  primary: '#002d1f'
  on-primary: '#ffffff'
  primary-container: '#004532'
  on-primary-container: '#78b299'
  inverse-primary: '#97d3b9'
  secondary: '#545f73'
  on-secondary: '#ffffff'
  secondary-container: '#d5e0f8'
  on-secondary-container: '#586377'
  tertiary: '#491411'
  on-tertiary: '#ffffff'
  tertiary-container: '#652925'
  on-tertiary-container: '#e49088'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#b3efd4'
  primary-fixed-dim: '#97d3b9'
  on-primary-fixed: '#002116'
  on-primary-fixed-variant: '#12503c'
  secondary-fixed: '#d8e3fb'
  secondary-fixed-dim: '#bcc7de'
  on-secondary-fixed: '#111c2d'
  on-secondary-fixed-variant: '#3c475a'
  tertiary-fixed: '#ffdad6'
  tertiary-fixed-dim: '#ffb3ac'
  on-tertiary-fixed: '#3b0908'
  on-tertiary-fixed-variant: '#73342f'
  background: '#f8f9ff'
  on-background: '#0b1c30'
  surface-variant: '#d3e4fe'
  pitch-green-container: '#065f46'
  waiting-list-amber: '#d97706'
  surface-ice: '#f8f9ff'
  surface-border: '#bec9c2'
  error-soft: '#ffdad6'
typography:
  display: { fontFamily: Inter, fontSize: 36px, fontWeight: '700', lineHeight: 44px, letterSpacing: -0.02em }
  headline-lg: { fontFamily: Inter, fontSize: 28px, fontWeight: '600', lineHeight: 36px, letterSpacing: -0.01em }
  headline-md: { fontFamily: Inter, fontSize: 20px, fontWeight: '600', lineHeight: 28px }
  body-lg: { fontFamily: Inter, fontSize: 16px, fontWeight: '400', lineHeight: 24px }
  body-md: { fontFamily: Inter, fontSize: 14px, fontWeight: '400', lineHeight: 20px }
  label-md: { fontFamily: Inter, fontSize: 12px, fontWeight: '600', lineHeight: 16px, letterSpacing: 0.05em }
  label-sm: { fontFamily: Inter, fontSize: 10px, fontWeight: '800', lineHeight: 12px, letterSpacing: 0.05em }
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 40px
  gutter: 24px
  container-max: 1280px

---

## Brand & Style

Personalidade **autoritativa, organizada e energética**. Estética Corporate Modern com **Tonal Density**: verde profundo de gramado + superfícies azuladas em camadas. UI plana, outlines sutis, poucas sombras.

Nome de produto na interface: **Futebol Atemporal** (não “PitchPerfect”).

## Layout

- Container max 1280px
- Sidebar desktop 256px; bottom nav no mobile
- Gutter 24px; padding desktop 40px / mobile 24px
- Grid de métricas 4 → 2 → 1 colunas

## Elevation

1. Base: `background`
2. Cards/tabelas: `surface` + borda `outline-variant`
3. Recuos: `surface-container*`
4. Sombras só em FAB / estados ativos (`shadow-sm`)

## Components

- Botões primários em pill; secundários com outline
- Chips para Mensalista / Avulso / Pendente / Aguardando
- Tabelas com header `surface-container-lowest` e hover `surface-container-low`
- Metric cards estilo bento
- Nav ativa com `rounded-r-full` (sidebar) ou pill preenchida (bottom)

## Responsividade e a11y

- Mobile-first; ações essenciais sem depender de hover
- Foco visível, labels, contraste, estados não só por cor
- Sem molduras de “app nativo”; web responsiva real
