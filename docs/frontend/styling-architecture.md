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
    ├── _tokens.scss     # emits CSS: :root --fo-* tokens
    └── _base.scss       # emits CSS: html/body, focus-visible fallback, reduced motion
```

| File                  | Output     | Responsibility                             |
| --------------------- | ---------- | ------------------------------------------ |
| `styles.scss`         | Global CSS | Load order: `styles/tokens`, `styles/base` |
| `styles/_tokens.scss` | Emits CSS  | Only place `--fo-*` is declared            |
| `styles/_base.scss`   | Emits CSS  | Document base and global a11y rules        |
| Component `name.scss` | Scoped CSS | Styles for that component only             |

- Both partials emit CSS. Components must **never** `@use` `_tokens` or `_base`: it
  duplicates their CSS into every component bundle. Components consume tokens with `var()`.
- Future compile-time partials (e.g. `_breakpoints.scss`) must emit no CSS; components
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

| Token                           | Status   |
| ------------------------------- | -------- |
| `--fo-color-app-background`     | Existing |
| `--fo-color-sidebar-background` | Example  |
| `--fo-size-touch-target`        | Example  |

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
- Breakpoints are deferred. The first responsive screen creates `src/styles/_breakpoints.scss`:
  compile-time map `md: 48rem`, `lg: 64rem` and an `up($name)` mixin (emits no CSS).
- Shell-only widths (1100px / 1440px from the handoff) stay local to the shell component.
- Media queries use `rem`.
- Touch targets ≥ 2.75rem (44px) on mobile.

Dark mode:

- `.app-dark` on `<html>` (`darkModeSelector`); no toggle exists yet.
- PrimeNG sets `color-scheme` per mode, so `light-dark()` follows `.app-dark`.
- Every `--fo-*` color uses `light-dark()`.
- Mockups are light-only; dark values derive from `--p-*` dark values.

## 8. PrimeNG override rules

Use the first option that works:

1. Preset tokens in `primeng.config.ts` (global look).
2. Component `dt` input (design tokens for one instance).
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
| `grep -rnE "#[0-9a-fA-F]{3,8}\b" --include=*.scss frontend/src/app` | No matches                 |

## Deferred

Need a separate approved decision before use:

- Inter font.
- Navy color.
- Type scale.
- Card radius 10.
- Spacing scale (`--fo-space-*`).
- Status colors.
- Lavender accent.
- PrimeIcons.

Breakpoints are not a pending decision: the convention in section 7 is approved and
`_breakpoints.scss` is created with its first consumer.
