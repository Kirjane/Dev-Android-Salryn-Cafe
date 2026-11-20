# `@salryn/cafe-ui` — Salryn Cafe Design System & Brand Foundation (Issue `#10` UX-A)

Standalone, accessible UI primitive package and interactive component catalog for **Salryn Cafe**, built directly on canonical brand assets in `assets/brand/` (`6b266249`).

## Brand & Accessibility Guarantees
- **Canonical Palette (`assets/brand/colors.css` & `colors.json`):** Espresso `#3B1204`, Cream `#FFF4E1`, Copper `#CC7436`, Dark Copper `#934719`, Latte `#E9D5BB`, Mocha `#735447` — zero modification to `assets/brand/*`.
- **WCAG 2.1 AA Contrast Verified:** Programmatically verified (`>= 4.5:1` normal text, `>= 3.0:1` large/decorative) across all brand and warm semantic feedback tokens (`success`, `warning`, `danger`, `info`, `neutral`, `copper`).
- **Aspect-Ratio Preserved Brand Artwork:** `BrandMark` renders `salryn-transparent-logo.png`, `salryn-app-icon.png`, and `salryn-banner.png` with `object-fit: contain` and light-surface framing for the transparent logo.
- **Touch & Keyboard Ergonomics:** Minimum `44px × 44px` touch targets (`48px` POS primary actions), responsive across `1024×768`, tablet, desktop, and mobile.
- **Accessible Primitives:**
  - `CafeShell` (skip-to-content link, `aria-current="page"`, collapsible sidebar + mobile drawer, sign-out confirmation)
  - `Dialog` / `ConfirmDialog` (`Escape` close, `Tab`/`Shift+Tab` focus trap, initial focus placement, focus restoration)
  - `FormField` / `TextInput` / `SelectInput` / `TextareaInput` (`<label htmlFor>`, `aria-invalid`, `aria-describedby`, `role="alert"`)
  - `DataTable`, `Button`, `StatusBadge`, `Card`, `MetricCard`, `Alert`, `ToastProvider`, `EmptyState`, `LoadingState`, `ErrorBoundary`

## Commands
```bash
npm install
npm test        # Vitest + React Testing Library + WCAG contrast + axe-core
npm run build   # tsc --noEmit + vite build (Catalog bundle)
npm run dev     # Run interactive Design System Catalog
```
