---
name: PitchPerfect Elite
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
  display:
    fontFamily: Inter
    fontSize: 36px
    fontWeight: '700'
    lineHeight: 44px
    letterSpacing: -0.02em
  headline-lg:
    fontFamily: Inter
    fontSize: 28px
    fontWeight: '600'
    lineHeight: 36px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  label-md:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.05em
  label-sm:
    fontFamily: Inter
    fontSize: 10px
    fontWeight: '800'
    lineHeight: 12px
    letterSpacing: 0.05em
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
PitchPerfect Elite is a high-performance sports management platform that balances athletic energy with professional administrative precision. The brand personality is **authoritative, organized, and energetic**.

The design style follows a **Corporate Modern** aesthetic with a specific focus on **Tonal Density**. It utilizes a deep athletic green palette to evoke the feeling of a pitch, contrasted with a crisp, multi-layered "blue-tinted" surface system that keeps the interface feeling light and systematic. The UI is intentionally flat and structured, using subtle outlines rather than heavy shadows to maintain a clean, data-focused environment suitable for tournament organizers and league administrators.

## Colors
The palette is rooted in **Deep Pitch Green** (#004532), used for primary branding and high-importance actions. 

- **Primary & Containers:** The primary green is supported by a "Container" variant (#065f46) for active states in navigation, ensuring high legibility.
- **Surface System:** We use a sophisticated blue-grey neutral scale for surfaces. The background is a cool `surface-ice` (#f8f9ff). Container levels (Low to Highest) use progressively deeper blue tints to establish hierarchy without relying on shadows.
- **Functional Accents:** `Tertiary` reds and earthy tones are used for financial indicators (pending payments), while a specific `waiting-list-amber` (#d97706) is reserved for scheduling status updates.

## Typography
The system uses **Inter** exclusively to emphasize its utilitarian, SaaS-first nature. 

Hierarchy is established through tight letter-spacing on larger display sizes and a strict adherence to a semi-bold/bold weight for interactive labels. **Display** type is used for key metrics (e.g., player counts), while **Label-SM** (10px, All-Caps) is utilized for secondary metadata to maximize information density without clutter.

## Layout & Spacing
The layout follows a **Fixed-Width Grid** within a max-width container of 1280px for desktop, centered on the screen. 

- **Navigation:** A fixed 256px (w-64) sidebar handles primary app-level navigation.
- **Content Rhythm:** A 24px gutter is used between cards in the Bento-style dashboard grid. 
- **Page Margins:** Main canvas areas use 40px (xl) padding on desktop, scaling down to 24px (lg) on mobile.
- **Responsive Behavior:** The 4-column metric grid collapses to 2 columns on tablets and 1 column on mobile devices.

## Elevation & Depth
Depth is expressed through **Tonal Layers** and **Low-Contrast Outlines** rather than traditional shadows. 

1.  **Level 0 (Base):** `background` (#f8f9ff).
2.  **Level 1 (Cards/Tables):** `surface` with a 1px border of `outline-variant` (#bec9c2).
3.  **Level 2 (In-set elements):** `surface-container` tiers are used for button backgrounds and table headers to create a recessed look.
4.  **Shadows:** Only used sparingly (e.g., `shadow-sm`) on floating buttons or active card states to provide a subtle "lift" during interaction.

## Shapes
The shape language is **hybrid**. 

- **Structural Elements:** Dashboard cards and player list containers use a `rounded-xl` (12px-24px) corner radius to feel modern and accessible.
- **Interactive Elements:** Primary buttons and navigation pills use **Fully Rounded (Pill)** geometry. This distinction helps users immediately identify "clickable" versus "informational" areas.
- **Avatars:** Strictly circular to contrast with the rectangular grid of the dashboard.

## Components
- **Buttons:** Primary buttons are pill-shaped with high-contrast text. Secondary buttons use a `border-outline` with no background fill until hover.
- **Chips/Badges:** Small, high-radius (rounded-full) badges with low-opacity background tints (e.g., secondary-container) to denote player categories like "Mensalista."
- **Data Tables:** Feature a `surface-container-lowest` header with `outline-variant` dividers. Rows should include a transition-enabled hover state (`surface-container-low`).
- **Metric Cards:** Use a "Bento" style with a consistent 24px padding, featuring an icon in a circular container in the top-right and large `display` typography for the primary value.
- **Navigation Rails:** Active states use a `rounded-r-full` background to create a clear "pointing" visual toward the main content area.