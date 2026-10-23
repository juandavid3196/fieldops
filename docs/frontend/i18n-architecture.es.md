[English](i18n-architecture.md) | **Español**

# Internacionalización (inglés y español)

Spec: `specs/app-i18n-en-es/spec.md` (reglas BR-01 a BR-20). Esta página registra
la arquitectura y las convenciones; no repite la spec.

## Idiomas soportados

`src/app/core/i18n/languages.config.ts` es la lista única (BR-01): `en`
(`en-US`, base y fallback) y `es` (`es-CO`). Agregar un idioma implica agregar
su entrada, su código en el backend y sus archivos de traducción; los nombres de las opciones siempre
se muestran en su propio idioma.

## Piezas de ejecución (`src/app/core/i18n/`)

| Pieza | Responsabilidad |
| ----- | --------------- |
| `provideI18n()` | Se registra una sola vez en `app.config.ts`. Transloco (`en` por defecto y fallback), el manejador de claves faltantes, `TranslatedTitleStrategy` y el inicializador que resuelve y aplica el primer idioma antes de la primera pantalla. |
| `LanguageService` | Dueño del idioma activo: resolución (sesión, local, navegador, `en`), la clave de `localStorage`, `<html lang>`, el locale de Intl, los textos de PrimeNG y `apply(code)`, que carga el scope raíz y cada scope de feature activado antes de cambiar. |
| `LanguagePreferenceService` | Opciones del selector. Las superficies con sesión guardan mediante `PUT /account/language` o `PUT /portal/account/language`; cualquier fallo restaura el idioma anterior y lanza una señal de toast (BR-09). |
| `SessionService`, `PortalSessionService` | Llaman a `applyPreference` cuando se carga, crea o cambia una sesión. |
| `I18n`, `t()`, `localized()`, `localizedList()` | Traducción desde TypeScript. `t(key, params)` lee el signal del idioma, de modo que los computed y las plantillas se reevalúan al cambiar. `localized*` envuelven constantes (objetos, arreglos) que contienen llamadas a `t()`: se construyen de forma lazy y se reconstruyen una vez por idioma. |
| `LocalizedRefPipe`, `CodeLabelPipe` | Los bindings `[options]`/`[model]` de listas constantes reciben una referencia nueva por idioma; `CodeLabelPipe` muestra etiquetas de roles, acciones de auditoría y enums a partir de códigos estables. |
| `FieldErrorCatalog`, `ApiErrorService` | Los mensajes por campo del backend y los códigos `403`/`404`/`409` se asignan a texto localizado; los desconocidos recurren a un texto localizado genérico. El `title` y el `detail` del backend nunca se muestran. |
| `withI18n(route, ...folders)` | Agrega un scope de feature a una ruta: `provideTranslocoScope` más un guard que carga el scope (y los scopes de `scope-dependencies.ts`) para el idioma activo. |

## Recursos

- Scope raíz: `core/i18n/{en,es}.json` (shell, navegación, común, títulos,
  errores, enums, roles, acciones de auditoría, selector, textos de PrimeNG), cargado en
  el arranque.
- Scopes de feature: `features/<feature>/i18n/{en,es}.json`, cargados cuando se activa una ruta de
  la feature (el build emite un chunk por archivo). Una carga fallida
  recurre al inglés.
- Claves: `<scope>.<surface>.<element>[.state]` en camelCase (`customers.customerList.title`);
  palabras compartidas en `common.*`; los parámetros son `{{name}}`.
- Plurales: claves separadas (`...One`/`...Many` o `<word>` y `<word>s`) elegidas con
  `Intl.PluralRules` (`core/i18n/plural.ts`); sin dependencia de ICU.
- Los títulos de ruta son claves (`titles.<name>`); `TranslatedTitleStrategy` las resuelve
  y vuelve a ejecutar las funciones de título al cambiar de idioma.

## Formato

Los helpers puros formatean con `activeIntlLocale()` (signal que devuelve `en-US` o `es-CO`)
al renderizar; nada de lo formateado se almacena ni se envía. Los nombres de meses y días y los
textos de PrimeNG siguen el idioma mediante `PrimeNG.setTranslation`.

## Selectores

`shared/components/language-options`, `language-menu-item` (menú de usuario del shell principal,
panel **More** del técnico, menú de usuario del portal), `language-footer-selector`
(páginas anónimas) y `language-toast` (toast de fallo de los shells). Las opciones son
`menuitemradio` con `aria-checked`, un icono de verificación y soporte de teclado.

## Verificaciones y pruebas

- `npm run i18n:check` (`scripts/i18n-check`): paridad de claves, claves JSON duplicadas,
  valores vacíos, paridad de interpolaciones y texto o atributos escritos directamente en plantillas
  que no estén en `i18n-allowlist.json` (solo marcas, símbolos y unidades). Corre en
  el CI del frontend después del lint; `npm run i18n:check:test` cubre el script.
- Las pruebas unitarias parten en inglés con los archivos reales: `src/test-setup.ts` registra
  `provideI18nTesting()` (cada scope fusionado en la traducción raíz y cargado de forma
  síncrona) y un traductor sin DI para utilidades puras. La lista de features de
  `core/i18n/testing/i18n-testing.ts` debe incluir cada carpeta `features/<f>/i18n`.

## Agregar texto o un idioma

1. Agrega la clave a `en.json` y `es.json` del scope correspondiente (glosario BR-16 para
   el español) y usa `{{ 'key' | transloco }}` en plantillas o `t('key')` en código.
2. Para un nuevo scope de feature, envuelve sus rutas con `withI18n`, lista los scopes que
   renderiza de otras features en `scope-dependencies.ts` y agrega la carpeta a
   `I18N_FEATURE_FOLDERS`.
3. Ejecuta `npm run i18n:check`.
