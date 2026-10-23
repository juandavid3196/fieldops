**English** | [Español](styling-architecture.es.md)

# Frontend styling architecture

Rules for global styles, tokens and component styling in `frontend/`.
Theme source: `frontend/src/app/core/config/primeng.config.ts`.

## 1. Styling principles

- PrimeNG (Aura preset) owns the visual language; the app adds only what PrimeNG lacks.
- CSS custom properties first; SCSS compile-time features only where CSS vars cannot work.
- Global CSS stays minimal: tokens, document base, focus fallback, reduced motion.
- Component styles are co-located and encapsulated.
- Accessibility (WCAG AA) is a requirement, not a later polish step.

## 2. Folder and file responsibilities

```text
frontend/src/
├── styles.scss          # entry: header comment + @use only, no rules
└── styles/
    ├── _fonts.scss      # emits CSS: self-hosted Inter @font-face (400/500/600/700)
    ├── _tokens.scss     # emits CSS: :root --fo-* tokens
    ├── _base.scss       # emits CSS: html/body, focus-visible fallback, reduced motion
    └── _breakpoints.scss # no CSS: breakpoint map and up() mixin
```

| File                       | Output     | Responsibility                                                           |
| -------------------------- | ---------- | ------------------------------------------------------------------------ |
| `styles.scss`              | Global CSS | Load order: `styles/fonts`, `styles/tokens`, `styles/base`               |
| `styles/_fonts.scss`       | Emits CSS  | Inter `@font-face` (`font-display: swap`) from `public/fonts/inter/`     |
| `styles/_tokens.scss`      | Emits CSS  | Only place `--fo-*` is declared                                          |
| `styles/_base.scss`        | Emits CSS  | Document base (Inter family, 14px body) and global a11y rules            |
| `styles/_breakpoints.scss` | No CSS     | `md`/`lg`/`admin`/`dock` map and `up($name)` mixin; components `@use` it |
| Component `name.scss`      | Scoped CSS | Styles for that component only                                           |

- PrimeIcons 7 (`primeicons` dependency) is loaded through `angular.json` `styles`
  (`node_modules/primeicons/primeicons.css`); use `<i class="pi pi-*" aria-hidden="true">`.
- Inter (SIL OFL 1.1, license in `public/fonts/inter/OFL.txt`) is self-hosted; no CDN.

- `_tokens` and `_base` emit CSS. Components must **never** `@use` `_tokens` or `_base`: it
  duplicates their CSS into every component bundle. Components consume tokens with `var()`.
- Compile-time partials (e.g. `_breakpoints.scss`) must emit no CSS; components
  may `@use` those.
- `angular.json` has no `includePaths`: use relative `@use` paths
  (e.g. `@use '../../../styles/breakpoints' as bp;`).
- `@use`/`@forward` only; `@import` is prohibited.

## 3. PrimeNG vs FieldOps token ownership

| Owner                                   | Owns                                                                             |
| --------------------------------------- | -------------------------------------------------------------------------------- |
| Preset in `primeng.config.ts` (`--p-*`) | Palette, primary, surface, focus ring, radii, typography, PrimeNG component look |
| `_tokens.scss` (`--fo-*`)               | App meaning PrimeNG has no token for, built from `--p-*`                         |
| SCSS compile-time                       | Only what CSS vars cannot do (e.g. media query values)                           |

- Never redeclare `--p-*` in SCSS; change the preset instead.
- Preset: Aura, primary mapped to `teal` 50..950; `primary.color` =
  `light-dark({primary.700}, {primary.400})`; hover/active one step darker (light) or
  lighter (dark). `contrastColor` is Aura's default.
- PrimeNG CSS var name = `--p-` + dash-cased token path (`focusRing.width` → `--p-focus-ring-width`).

Admission rule for a new `--fo-*` token (all must hold):

1. No existing `--p-*` token expresses it.
2. Used by a global rule or by at least two components.
3. Value derived from `--p-*`, or from an approved spec/design decision.
4. Declared only in `_tokens.scss`.

## 4. Token naming

Pattern: `--fo-<category>-<role>[-<variant>]`, category ∈ `color | space | size | radius | shadow`.

| Token family                                                               | Meaning                                                 |
| -------------------------------------------------------------------------- | ------------------------------------------------------- |
| `--fo-color-app-background`, `-surface*`, `-text*`, `-border*`             | Handoff ground, cards, text and borders (light/dark)    |
| `--fo-color-primary*`, `-success-*`, `-info-*`, `-warning-*`, `-danger-*`  | Handoff teal and status colors (light/dark)             |
| `--fo-color-navy*`, `--fo-color-on-navy`                                   | Navy sidebar (dark in both modes)                       |
| `--fo-size-touch-target`, `-control`, `-topbar`, `-sidebar*`, `-drawer`    | 44px target, 40px control, 64px top bar, 256/72/224/420 |
| `--fo-radius-control/card/panel/pill`, `--fo-shadow-*`, `--fo-font-family` | Radii (6/10/8/999), elevation and Inter stack           |

## 5. Global vs component-local

- Global (`src/styles/`) is limited to: tokens, `html`/`body`, focus-visible fallback,
  reduced motion. No class, feature or `.p-*` selectors.
- Component styles: co-located `name.scss`, emulated encapsulation, `:host` for the host element.
- Feature-specific styles stay inside the feature; move to `shared/` only on a second reuse.
- Component SCSS: no hex colors and no raw `px` for color, radius or spacing. Use
  `var(--p-*)`/`var(--fo-*)` and `rem`.
- Component styles stay within the 4 kB warning / 8 kB error budget.

## 6. BEM

```scss
.member-card {
  padding: 1rem;

  &__header {
    display: flex;
  }

  &--selected {
    border-color: var(--p-primary-color);
  }
}
```

- Block = component concept; `__element`, `--modifier`.
- Maximum nesting: block + 1 level.

## 7. Responsive and dark mode

Responsive:

- Mobile-first: base styles for small screens, `min-width` queries upward.
- Breakpoints live in `src/styles/_breakpoints.scss`: compile-time map `md: 48rem` (768px),
  `lg: 64rem`, `admin: 68.75rem` (1100px) and `dock: 90rem` (1440px), plus an `up($name)` mixin
  that raises `@error` for unknown names (emits no CSS). Use `@include bp.up(md) { ... }` after
  `@use '<relative>/styles/breakpoints' as bp;`.
- Script mirrors of `md`, `admin` and `dock` are in `app/core/config/breakpoints.ts`; keep both in sync.
- Shell layout modes: below `md` a drawer, `md` to below `admin` the 72px rail, from `admin` the
  256px sidebar (Administration column 224px). The branch drawer docks (420px) from `dock`,
  overlays below the top bar from `md` and is full screen below.
- Media queries use `rem`.
- Touch targets ≥ 2.75rem (44px) on mobile.

Dark mode (app-dark-mode):

- `.app-dark` on `<html>` (`darkModeSelector`) is set only in dark mode; `style.colorScheme` and
  `<meta name="theme-color">` (`#0f9d9a` light, `#0b1620` dark) always equal the effective mode.
- Preference `system` | `light` | `dark` (default `system`), defined once in
  `core/theme/theme.config.ts`. Resolution (`theme-resolver.ts`): the signed-in user's
  `preferredTheme`, then the `fieldops.theme` local value, then `system`; `system` follows
  `prefers-color-scheme` and falls back to light when `matchMedia` is unavailable. An unsupported
  value is ignored.
- Pre-bootstrap: the inline script in `src/index.html` applies the local value and the device mode
  before the bundle (no flash). `core/theme/theme.service.spec.ts` runs that script and the
  resolver over one table, so the two cannot drift.
- `ThemeService` (`core/theme`) owns the state: `apply()` is DOM-only (previews), `commit()` and
  `applyPreference()` also write the local value, and a single `matchMedia` listener exists only
  while the preference is `system`. `ThemePreferenceService` saves through `PUT /account/theme` or
  `PUT /portal/account/theme` (with the `source`) and restores on failure. Session services apply
  `preferredTheme` beside `preferredLanguage`.
- Entry points: `/account/preferences` (main shell only) and the **Appearance** item
  (`shared/components/appearance-menu-item`) in the main, technician and portal menus.
- PrimeNG sets `color-scheme` per mode, so `light-dark()` follows `.app-dark`.
- Every `--fo-*` color token is a `light-dark()` pair unless it is a documented mode-invariant
  (brand, status solid fills, navy sidebar). Component styles use tokens only; palettes that used
  to live in component SCSS are tokens in `_tokens.scss`, light values unchanged.
- Text on fills that invert in dark (action blue, secondary text) uses `--fo-color-on-strong`.

`npm run theme:check` (`scripts/theme-check`, tests with `npm run theme:check:test`) fails with
file and line when a `.scss`/`.css` file other than `_tokens.scss`, a template (`style`, `fill`,
`stroke`, `color`, `[style.*]` and text) or a component `.ts` file contains a hex, `rgb()`/`hsl()`
color or a named color (assigned to a color property or CSS string), or when a color token in
`_tokens.scss` is not a `light-dark()` pair. `transparent`, `currentColor`, `inherit` and token
references pass. Exceptions live in `scripts/theme-check/allowlist.json` as
`{ "file", "value", "reason" }` (for a token, `value` is the token name); keep reasons specific
(brand artwork, solid fills, `theme-color`, navy sidebar). It runs in the frontend CI after the
i18n check.

## 8. PrimeNG override rules

Use the first option that works:

1. Preset tokens in `primeng.config.ts` (global look).
2. Component `dt` input (design tokens for one instance). Not on elements that also carry
   `pFocusTrap` (both expose `dt`): set the component's `--p-<component>-*` variables through
   its `[style]` input instead (used by the navigation and branch drawers).
3. Host `class`/`styleClass`, styled in the owning component's SCSS.

- Never `::ng-deep`; never global `.p-*` overrides.
- `cssLayer` is `false`. Revisit only if an approved need requires app CSS to reliably
  override PrimeNG without specificity work (would need a documented layer order).

## 9. Accessibility

- `primary.color` = teal.700 (#0f766e) in light mode: white text 5.47:1 (AA). Handoff
  teal #0F9D9A / #0B8583 fails AA with white text and is not used.
- Focus: `_base.scss` has a zero-specificity `:where()` focus-visible fallback using
  `--p-focus-ring-*`; PrimeNG component focus styles win.
- Never `outline: none` without a visible replacement.
- Reduced motion: global rule shortens animations/transitions to 0.01ms (keeps
  `animationend`/`transitionend` firing); ripple is disabled at bootstrap when
  `prefers-reduced-motion: reduce`.
- Touch targets ≥ 44px on mobile.
- 16px root font size; sizes in `rem` so text scaling works.
- Color is never the only indicator of state; pair with text or icon.

## 10. Allowed vs prohibited

```scss
// Allowed
:host {
  display: block;
}
.job-list__item {
  padding: 0.75rem 1rem;
  border-radius: var(--p-content-border-radius);
  color: var(--p-text-color);
  background: var(--fo-color-app-background);
}
```

```scss
// Prohibited
@import '../../styles/tokens'; // @import
@use '../../../styles/tokens'; // duplicates global CSS into the component
::ng-deep .p-button { ... }    // ::ng-deep / global .p-* override
.card { color: #0f9d9a; }      // hex color in component SCSS
.card { border-radius: 10px; } // raw px radius
:root { --p-primary-color: red; } // redeclaring --p-*
.btn { color: red !important; }   // undocumented !important
```

## Validation

From `frontend/`:

```bash
npm run format:check
npm run lint
npm run theme:check
npm run theme:check:test
npm run test -- --watch=false
npm run build -- --configuration production
```

Grep checks (from repo root):

| Check                                                               | Expected                   |
| ------------------------------------------------------------------- | -------------------------- |
| `grep -rnE "@import\|::ng-deep" frontend/src`                       | No matches                 |
| `grep -rn "!important" frontend/src`                                | Only `styles/_base.scss`   |
| `grep -rnE "^\s*--fo-[a-z-]+:" frontend/src`                        | Only `styles/_tokens.scss` |
| `grep -rnE "@use .*styles/(tokens\|base)" frontend/src/app`         | No matches                 |
| `npm run theme:check` (hardcoded colors, allowlist and token pairs) | Passes                     |

## Deferred

Need a separate approved decision before use:

- Spacing scale (`--fo-space-*`).
- Lavender accent.

Approved and implemented (design-17 company setup completion): Inter, navy, status colors,
type scale, card radius 10, PrimeIcons, the `--fo-*` handoff tokens and the 1100px/1440px
breakpoints.
