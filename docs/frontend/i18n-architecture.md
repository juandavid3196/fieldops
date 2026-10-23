**English** | [Español](i18n-architecture.es.md)

# Internationalization (English and Spanish)

Spec: `specs/app-i18n-en-es/spec.md` (rules BR-01 to BR-20). This page records
the architecture and the conventions; it does not repeat the spec.

## Supported languages

`src/app/core/i18n/languages.config.ts` is the single list (BR-01): `en`
(`en-US`, base and fallback) and `es` (`es-CO`). Adding a language means adding
its entry, its backend code and its translation files; option names are always
shown in their own language.

## Runtime pieces (`src/app/core/i18n/`)

| Piece | Responsibility |
| ----- | -------------- |
| `provideI18n()` | Registered once in `app.config.ts`. Transloco (`en` default and fallback), the missing-key handler, `TranslatedTitleStrategy` and the initializer that resolves and applies the first language before the first screen. |
| `LanguageService` | Owns the active language: resolution (session, local, browser, `en`), the `localStorage` key, `<html lang>`, the Intl locale, PrimeNG texts and `apply(code)`, which loads the root scope and every activated feature scope before switching. |
| `LanguagePreferenceService` | Selector choices. Signed-in surfaces save through `PUT /account/language` or `PUT /portal/account/language`; any failure restores the previous language and raises a toast signal (BR-09). |
| `SessionService`, `PortalSessionService` | Call `applyPreference` when a session is loaded, created or switched. |
| `I18n`, `t()`, `localized()`, `localizedList()` | TypeScript translation. `t(key, params)` reads the language signal, so computeds and templates re-evaluate on a switch. `localized*` wrap constants (objects, arrays) that contain `t()` calls: they are built lazily and rebuilt once per language. |
| `LocalizedRefPipe`, `CodeLabelPipe` | `[options]`/`[model]` bindings of constant lists get a new reference per language; `CodeLabelPipe` shows role, audit action and enum labels from stable codes. |
| `FieldErrorCatalog`, `ApiErrorService` | Backend field messages and `403`/`404`/`409` codes map to localized text; unknown ones fall back to a generic localized text. Backend `title` and `detail` are never shown. |
| `withI18n(route, ...folders)` | Adds a feature scope to a route: `provideTranslocoScope` plus a guard that loads the scope (and the scopes in `scope-dependencies.ts`) for the active language. |

## Resources

- Root scope: `core/i18n/{en,es}.json` (shell, navigation, common, titles,
  errors, enums, roles, audit actions, selector, PrimeNG texts), loaded at
  bootstrap.
- Feature scopes: `features/<feature>/i18n/{en,es}.json`, loaded when a route of
  the feature activates (the build emits one chunk per file). A failed load
  falls back to English.
- Keys: `<scope>.<surface>.<element>[.state]` in camelCase (`customers.customerList.title`);
  shared words in `common.*`; parameters are `{{name}}`.
- Plurals: separate keys (`...One`/`...Many` or `<word>` and `<word>s`) chosen with
  `Intl.PluralRules` (`core/i18n/plural.ts`); no ICU dependency.
- Route titles are keys (`titles.<name>`); `TranslatedTitleStrategy` resolves
  them and re-runs title functions on a switch.

## Formatting

Pure helpers format with `activeIntlLocale()` (signal-backed `en-US` or `es-CO`)
at render time; nothing formatted is stored or sent. PrimeNG month/day names and
texts follow the language through `PrimeNG.setTranslation`.

## Selectors

`shared/components/language-options`, `language-menu-item` (main shell user menu,
technician **More** panel, portal user menu), `language-footer-selector`
(anonymous pages) and `language-toast` (failure toast of the shells). Options are
`menuitemradio` with `aria-checked`, a check icon and keyboard support.

## Checks and tests

- `npm run i18n:check` (`scripts/i18n-check`): key parity, duplicate JSON keys,
  empty values, interpolation parity and hardcoded template text or attributes
  not listed in `i18n-allowlist.json` (brand, symbols and units only). It runs in
  the frontend CI after lint; `npm run i18n:check:test` covers the script.
- Unit tests start in English with the real files: `src/test-setup.ts` registers
  `provideI18nTesting()` (every scope merged into the root translation and loaded
  synchronously) and a DI-free translator for pure utilities. The feature list of
  `core/i18n/testing/i18n-testing.ts` must include every `features/<f>/i18n` folder.

## Adding text or a language

1. Add the key to `en.json` and `es.json` of the owning scope (glossary BR-16 for
   Spanish) and use `{{ 'key' | transloco }}` in templates or `t('key')` in code.
2. For a new feature scope, wrap its routes with `withI18n`, list the scopes it
   renders from other features in `scope-dependencies.ts` and add the folder to
   `I18N_FEATURE_FOLDERS`.
3. Run `npm run i18n:check`.
