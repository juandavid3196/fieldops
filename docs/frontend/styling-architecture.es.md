[English](styling-architecture.md) | **Español**

# Arquitectura de estilos del frontend

Reglas para estilos globales, tokens y estilos de componentes en `frontend/`.
Fuente del tema: `frontend/src/app/core/config/primeng.config.ts`.

## 1. Principios de estilos

- PrimeNG (preset Aura) define el lenguaje visual; la app agrega solo lo que PrimeNG no tiene.
- Primero custom properties de CSS; funciones de SCSS en tiempo de compilación solo donde las variables CSS no alcanzan.
- El CSS global se mantiene mínimo: tokens, base del documento, fallback de foco, movimiento reducido.
- Los estilos de componente están junto al componente y encapsulados.
- La accesibilidad (WCAG AA) es un requisito, no un pulido posterior.

## 2. Carpetas y responsabilidades de archivos

```text
frontend/src/
├── styles.scss          # entrada: comentario de cabecera + solo @use, sin reglas
└── styles/
    ├── _fonts.scss      # emite CSS: @font-face de Inter autoalojada (400/500/600/700)
    ├── _tokens.scss     # emite CSS: tokens :root --fo-*
    ├── _base.scss       # emite CSS: html/body, fallback de focus-visible, movimiento reducido
    └── _breakpoints.scss # no emite CSS: mapa de breakpoints y mixin up()
```

| Archivo                    | Salida     | Responsabilidad                                                          |
| -------------------------- | ---------- | ------------------------------------------------------------------------ |
| `styles.scss`              | CSS global | Orden de carga: `styles/fonts`, `styles/tokens`, `styles/base`           |
| `styles/_fonts.scss`       | Emite CSS  | `@font-face` de Inter (`font-display: swap`) desde `public/fonts/inter/` |
| `styles/_tokens.scss`      | Emite CSS  | Único lugar donde se declara `--fo-*`                                    |
| `styles/_base.scss`        | Emite CSS  | Base del documento (familia Inter, cuerpo de 14px) y reglas globales de accesibilidad |
| `styles/_breakpoints.scss` | Sin CSS    | Mapa `md`/`lg`/`admin`/`dock` y mixin `up($name)`; los componentes lo importan con `@use` |
| `name.scss` del componente | CSS con alcance | Estilos de ese componente únicamente                               |

- PrimeIcons 7 (dependencia `primeicons`) se carga mediante `styles` en `angular.json`
  (`node_modules/primeicons/primeicons.css`); usa `<i class="pi pi-*" aria-hidden="true">`.
- Inter (SIL OFL 1.1, licencia en `public/fonts/inter/OFL.txt`) está autoalojada; sin CDN.

- `_tokens` y `_base` emiten CSS. Los componentes **nunca** deben hacer `@use` de `_tokens` ni `_base`: duplica
  su CSS en cada bundle de componente. Los componentes consumen tokens con `var()`.
- Los parciales de tiempo de compilación (p. ej. `_breakpoints.scss`) no deben emitir CSS; los componentes
  pueden hacer `@use` de ellos.
- `angular.json` no tiene `includePaths`: usa rutas relativas en `@use`
  (p. ej. `@use '../../../styles/breakpoints' as bp;`).
- Solo `@use`/`@forward`; `@import` está prohibido.

## 3. Propiedad de tokens: PrimeNG vs FieldOps

| Dueño                                   | Posee                                                                            |
| --------------------------------------- | -------------------------------------------------------------------------------- |
| Preset en `primeng.config.ts` (`--p-*`) | Paleta, primary, surface, anillo de foco, radios, tipografía, apariencia de los componentes de PrimeNG |
| `_tokens.scss` (`--fo-*`)               | Significado propio de la app que PrimeNG no tiene como token, construido a partir de `--p-*` |
| SCSS en tiempo de compilación           | Solo lo que las variables CSS no pueden hacer (p. ej. valores de media queries)  |

- Nunca redeclares `--p-*` en SCSS; cambia el preset.
- Preset: Aura, primary mapeado a `teal` 50..950; `primary.color` =
  `light-dark({primary.700}, {primary.400})`; hover/active un paso más oscuro (claro) o
  más claro (oscuro). `contrastColor` es el valor por defecto de Aura.
- El nombre de la variable CSS de PrimeNG = `--p-` + ruta del token en kebab-case (`focusRing.width` → `--p-focus-ring-width`).

Regla de admisión para un nuevo token `--fo-*` (deben cumplirse todas):

1. Ningún token `--p-*` existente lo expresa.
2. Lo usa una regla global o al menos dos componentes.
3. Su valor se deriva de `--p-*`, o de una spec/decisión de diseño aprobada.
4. Se declara solo en `_tokens.scss`.

## 4. Nombres de tokens

Patrón: `--fo-<category>-<role>[-<variant>]`, category ∈ `color | space | size | radius | shadow`.

| Familia de tokens                                                          | Significado                                             |
| -------------------------------------------------------------------------- | ------------------------------------------------------- |
| `--fo-color-app-background`, `-surface*`, `-text*`, `-border*`             | Fondo del handoff, tarjetas, texto y bordes (claro/oscuro) |
| `--fo-color-primary*`, `-success-*`, `-info-*`, `-warning-*`, `-danger-*`  | Teal del handoff y colores de estado (claro/oscuro)     |
| `--fo-color-navy*`, `--fo-color-on-navy`                                   | Sidebar navy (oscuro en ambos modos)                    |
| `--fo-size-touch-target`, `-control`, `-topbar`, `-sidebar*`, `-drawer`    | Objetivo de 44px, control de 40px, barra superior de 64px, 256/72/224/420 |
| `--fo-radius-control/card/panel/pill`, `--fo-shadow-*`, `--fo-font-family` | Radios (6/10/8/999), elevación y pila tipográfica de Inter |

## 5. Global vs local al componente

- Lo global (`src/styles/`) se limita a: tokens, `html`/`body`, fallback de focus-visible,
  movimiento reducido. Sin selectores de clase, de feature ni `.p-*`.
- Estilos de componente: `name.scss` junto al componente, encapsulación emulada, `:host` para el elemento anfitrión.
- Los estilos específicos de una feature se quedan dentro de la feature; pasan a `shared/` solo con un segundo uso.
- SCSS de componentes: sin colores hex y sin `px` directos para color, radio o espaciado. Usa
  `var(--p-*)`/`var(--fo-*)` y `rem`.
- Los estilos de componente se mantienen dentro del presupuesto de 4 kB (advertencia) / 8 kB (error).

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

- Bloque = concepto del componente; `__elemento`, `--modificador`.
- Anidación máxima: bloque + 1 nivel.

## 7. Responsive y modo oscuro

Responsive:

- Mobile-first: estilos base para pantallas pequeñas, consultas `min-width` hacia arriba.
- Los breakpoints viven en `src/styles/_breakpoints.scss`: mapa de tiempo de compilación `md: 48rem` (768px),
  `lg: 64rem`, `admin: 68.75rem` (1100px) y `dock: 90rem` (1440px), más un mixin `up($name)`
  que lanza `@error` ante nombres desconocidos (no emite CSS). Usa `@include bp.up(md) { ... }` después de
  `@use '<relative>/styles/breakpoints' as bp;`.
- Los espejos en script de `md`, `admin` y `dock` están en `app/core/config/breakpoints.ts`; mantén ambos sincronizados.
- Modos de layout del shell: por debajo de `md` un drawer, de `md` hasta por debajo de `admin` el rail de 72px, desde `admin` el
  sidebar de 256px (columna de Administration de 224px). El drawer de sucursales se acopla (420px) desde `dock`,
  se superpone bajo la barra superior desde `md` y es de pantalla completa por debajo.
- Las media queries usan `rem`.
- Objetivos táctiles ≥ 2.75rem (44px) en móvil.

Modo oscuro (app-dark-mode):

- `.app-dark` en `<html>` (`darkModeSelector`) se define solo en modo oscuro; `style.colorScheme` y
  `<meta name="theme-color">` (`#0f9d9a` claro, `#0b1620` oscuro) siempre equivalen al modo efectivo.
- Preferencia `system` | `light` | `dark` (por defecto `system`), definida una sola vez en
  `core/theme/theme.config.ts`. Resolución (`theme-resolver.ts`): el `preferredTheme` del usuario con
  sesión, luego el valor local `fieldops.theme`, luego `system`; `system` sigue
  `prefers-color-scheme` y recurre a claro cuando `matchMedia` no está disponible. Un valor
  no soportado se ignora.
- Antes del arranque: el script en línea de `src/index.html` aplica el valor local y el modo del dispositivo
  antes del bundle (sin parpadeo). `core/theme/theme.service.spec.ts` ejecuta ese script y el
  resolver sobre una misma tabla, para que no se desfasen.
- `ThemeService` (`core/theme`) es dueño del estado: `apply()` solo toca el DOM (vistas previas), `commit()` y
  `applyPreference()` también escriben el valor local, y un único listener de `matchMedia` existe solo
  mientras la preferencia es `system`. `ThemePreferenceService` guarda mediante `PUT /account/theme` o
  `PUT /portal/account/theme` (con el `source`) y restaura si falla. Los servicios de sesión aplican
  `preferredTheme` junto a `preferredLanguage`.
- Puntos de entrada: `/account/preferences` (solo shell principal) y el ítem **Appearance**
  (`shared/components/appearance-menu-item`) en los menús principal, de técnico y del portal.
- PrimeNG define `color-scheme` por modo, así que `light-dark()` sigue a `.app-dark`.
- Todo token de color `--fo-*` es un par `light-dark()` salvo que sea un invariante de modo documentado
  (marca, rellenos sólidos de estado, sidebar navy). Los estilos de componente usan solo tokens; las paletas que
  vivían en el SCSS de componentes ahora son tokens en `_tokens.scss`, con los valores claros sin cambios.
- El texto sobre rellenos que se invierten en oscuro (azul de acción, texto secundario) usa `--fo-color-on-strong`.

`npm run theme:check` (`scripts/theme-check`, pruebas con `npm run theme:check:test`) falla indicando
archivo y línea cuando un archivo `.scss`/`.css` distinto de `_tokens.scss`, una plantilla (`style`, `fill`,
`stroke`, `color`, `[style.*]` y texto) o un archivo `.ts` de componente contiene un hex, un color `rgb()`/`hsl()`
o un color con nombre (asignado a una propiedad de color o a una cadena CSS), o cuando un token de color en
`_tokens.scss` no es un par `light-dark()`. `transparent`, `currentColor`, `inherit` y las referencias a
tokens pasan. Las excepciones viven en `scripts/theme-check/allowlist.json` como
`{ "file", "value", "reason" }` (para un token, `value` es el nombre del token); mantén los motivos específicos
(arte de marca, rellenos sólidos, `theme-color`, sidebar navy). Corre en el CI del frontend después de la
verificación de i18n.

## 8. Reglas para sobrescribir PrimeNG

Usa la primera opción que funcione:

1. Tokens del preset en `primeng.config.ts` (apariencia global).
2. Input `dt` del componente (design tokens para una instancia). No en elementos que también llevan
   `pFocusTrap` (ambos exponen `dt`): define las variables `--p-<component>-*` del componente mediante
   su input `[style]` (usado por los drawers de navegación y de sucursales).
3. `class`/`styleClass` del host, con estilos en el SCSS del componente dueño.

- Nunca `::ng-deep`; nunca sobrescrituras globales de `.p-*`.
- `cssLayer` es `false`. Reconsidéralo solo si una necesidad aprobada exige que el CSS de la app
  sobrescriba PrimeNG de forma fiable sin trabajo de especificidad (requeriría un orden de capas documentado).

## 9. Accesibilidad

- `primary.color` = teal.700 (#0f766e) en modo claro: texto blanco 5.47:1 (AA). El teal del handoff
  #0F9D9A / #0B8583 no cumple AA con texto blanco y no se usa.
- Foco: `_base.scss` tiene un fallback de focus-visible con `:where()` de especificidad cero que usa
  `--p-focus-ring-*`; los estilos de foco de los componentes de PrimeNG prevalecen.
- Nunca `outline: none` sin un reemplazo visible.
- Movimiento reducido: una regla global acorta animaciones/transiciones a 0.01ms (mantiene
  el disparo de `animationend`/`transitionend`); el ripple se desactiva en el arranque cuando
  `prefers-reduced-motion: reduce`.
- Objetivos táctiles ≥ 44px en móvil.
- Tamaño de fuente raíz de 16px; tamaños en `rem` para que el escalado de texto funcione.
- El color nunca es el único indicador de estado; acompáñalo con texto o icono.

## 10. Permitido vs prohibido

```scss
// Permitido
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
// Prohibido
@import '../../styles/tokens'; // @import
@use '../../../styles/tokens'; // duplica CSS global en el componente
::ng-deep .p-button { ... }    // ::ng-deep / sobrescritura global de .p-*
.card { color: #0f9d9a; }      // color hex en SCSS de componente
.card { border-radius: 10px; } // radio en px directo
:root { --p-primary-color: red; } // redeclarar --p-*
.btn { color: red !important; }   // !important sin documentar
```

## Validación

Desde `frontend/`:

```bash
npm run format:check
npm run lint
npm run theme:check
npm run theme:check:test
npm run test -- --watch=false
npm run build -- --configuration production
```

Verificaciones con grep (desde la raíz del repositorio):

| Verificación                                                        | Esperado                   |
| ------------------------------------------------------------------- | -------------------------- |
| `grep -rnE "@import\|::ng-deep" frontend/src`                       | Sin coincidencias          |
| `grep -rn "!important" frontend/src`                                | Solo `styles/_base.scss`   |
| `grep -rnE "^\s*--fo-[a-z-]+:" frontend/src`                        | Solo `styles/_tokens.scss` |
| `grep -rnE "@use .*styles/(tokens\|base)" frontend/src/app`         | Sin coincidencias          |
| `npm run theme:check` (colores fijos, allowlist y pares de tokens)  | Pasa                       |

## Diferido

Requieren una decisión aprobada por separado antes de usarse:

- Escala de espaciado (`--fo-space-*`).
- Acento lavanda.

Aprobado e implementado (design-17 company setup completion): Inter, navy, colores de estado,
escala tipográfica, radio de tarjeta 10, PrimeIcons, los tokens `--fo-*` del handoff y los breakpoints
de 1100px/1440px.
