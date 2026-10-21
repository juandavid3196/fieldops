# Application internationalization (English and Spanish)

| Field    | Value            |
| -------- | ---------------- |
| Feature  | `app-i18n-en-es` |
| Type     | Full-stack       |
| Status   | APPROVED         |
| Created  | 2026-10-21       |
| Updated  | 2026-10-21       |
| Approved | 2026-10-21       |

## Context and objective

Every FieldOps screen is English-only, with hardcoded copy and `en-US`
formatting, so Spanish-speaking teams and customers in Colombia and Latin
America cannot work in their language. This feature adds runtime language
switching between English (`en`, base and fallback) and Latin American Spanish
(`es`) with Transloco, translates every user-visible text of the application,
localizes formatting (`en-US` / `es-CO`) and persists the choice per user and
per browser. The infrastructure is reusable for a later Portuguese spec.
Success means a user can switch language from any shell or public page without
reload or loss of work, the choice survives sign-in on any device, and no screen
shows English copy in Spanish, unresolved keys or backend technical text.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Signed-in internal user, any role | Switch the UI language from the main shell user menu or the technician shell **More** panel; persist their own `preferredLanguage` through `PUT /account/language` | Change another user's language; supply a user or organization identifier; set an unsupported code |
| Signed-in portal user | Switch language from the portal shell user menu; persist their own `preferredLanguage` through `PUT /portal/account/language` | Same as above; use the internal endpoint with a portal session or the reverse |
| Anonymous visitor | Switch language from the compact footer selector on sign-in, registration, password recovery, invitation acceptance, public portal pages and Privacy/Terms; the choice is stored only in the browser | Call either language endpoint (`401`) |
| System | Resolve the user and organization only from the validated session; write audit rows; store the active language at account creation | Trust client user/organization identifiers; translate user-written or historical content |

## Scope

- Transloco installation (official package compatible with Angular 22),
  registered from `app.config.ts`, with English as base and fallback and
  translation resources organized per feature and loaded lazily.
- Language resolution, local persistence, authenticated persistence and the
  `lang` attribute of the document.
- Language selector: **Language** sublist in the main shell user menu, the
  technician shell **More** panel and the portal shell user menu; compact footer
  selector on anonymous pages.
- Schema amendment SA-24 (`users.preferred_language`), migration generation
  (never applied), session contracts, two self-service endpoints, account
  creation language and the `user.language_updated` audit event.
- Translation of every visible text in the surfaces of the Translation coverage
  table, including dialogs, drawers, context menus, confirmations, loading /
  empty / filtered-empty / error states, toasts, validation messages, tooltips,
  placeholders, page titles, breadcrumbs, ARIA text and displayed enums.
- Code-based translation of role names, audit actions and notification
  templates; frontend translation of backend field and error messages.
- Locale-aware dates, numbers, currencies, percentages, relative times and
  PrimeNG components.
- `npm run i18n:check` (key parity, duplicates, interpolation parity and
  hardcoded template text with a versioned allowlist), added to the frontend CI.
- `docs/frontend` updated with the i18n architecture and conventions.

## Non-goals

- Portuguese or any third language.
- Dark mode.
- Automatic translation of user-entered content.
- Changing emails, PDFs or CSV files already generated, or their generators.
- Translating historical records (audit snapshots, stored notification subjects
  or payload text, stored labels).
- Per-organization language administration or organization default language.
- A dedicated language page; the future Preferences page is a separate spec.
- A custom ESLint rule for hardcoded TypeScript strings.
- Backend-side localization: API messages, codes and field keys stay unchanged.
- Professional legal review of the Spanish Privacy and Terms text.

## User flow

1. A visitor opens Sign in for the first time with browser language `es-CO`; the
   page renders in Spanish and `<html lang="es">`.
2. They sign in; the session carries `preferredLanguage`, which wins over the
   local choice; the UI switches if different and the local value is updated.
3. In the user menu they open **Idioma** and choose **English**; the UI changes
   immediately, the URL, form values and scroll position stay, and
   `PUT /account/language` stores `en`.
4. If the save fails, the UI returns to Spanish and a localized toast explains
   the failure.
5. On another device they sign in and see English.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must resolve the initial language per BR-02 and BR-03 before the first screen renders, set `<html lang>` and keep the local value per BR-04. |
| FR-02 | When a session is established or revalidated (internal or portal) with a supported `preferredLanguage`, the frontend must apply it per BR-02 and update the local value. |
| FR-03 | The main shell user menu, the technician shell **More** panel and the portal shell user menu must contain a **Language** item that opens a sublist with **English** and **Español** per BR-18; anonymous pages listed in BR-18 must show the compact footer selector. |
| FR-04 | Choosing a language must apply it immediately without reload or navigation, preserving the current route, query parameters, form values and dirty state, open drawers/dialogs and scroll position, and must update `<html lang>`, the document title and all formatting. |
| FR-05 | For a signed-in user, choosing a different language must call the matching endpoint (BR-09); on failure the previous language is restored with the BR-09 toast. Anonymous choices only update the local value. |
| FR-06 | `PUT /account/language` (internal session) and `PUT /portal/account/language` (portal session) must validate per BR-05, update only the session user's `preferred_language` per BR-06 and audit per BR-07. |
| FR-07 | `GET /sessions/current`, `POST /sessions`, `GET /portal/sessions/current`, `POST /portal/sessions` and `POST /portal/sessions/current/account` must include `user.preferredLanguage`. |
| FR-08 | Organization registration, internal invitation acceptance and portal invitation acceptance must store the language active in the UI per BR-08. |
| FR-09 | Schema amendment SA-24 must add `users.preferred_language` with existing users set to `en`; the migration is generated and never applied. |
| FR-10 | Every user-visible text of the surfaces in the Translation coverage table must come from translation resources in both languages, using the BR-16 glossary for Spanish, while BR-15 content is shown unchanged. |
| FR-11 | Role names, audit actions, notification templates and displayed enum/status values must be translated from their stable codes per BR-14. |
| FR-12 | Backend validation and error responses must be shown as localized text per BR-13; backend technical text is never displayed. |
| FR-13 | Dates, numbers, currencies, percentages, relative times and PrimeNG components must be formatted per BR-10. |
| FR-14 | Missing translations must behave per BR-11, and no production screen may show an unresolved key. |
| FR-15 | `npm run i18n:check` must enforce BR-12 and run in the frontend CI workflow. |
| FR-16 | Help & support articles, Privacy and Terms must be fully translated, and the Spanish Privacy and Terms pages must show the BR-17 note. |
| FR-17 | Translation resources must be split into a root scope and per-feature scopes per BR-20, loading a feature scope only when one of its routes is activated. |
| FR-18 | The selectors and all translated ARIA text must meet the accessibility rules of BR-18 and UI behavior. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Supported languages: `en` → English, locale `en-US`; `es` → Español, locale `es-CO`. The list lives in one frontend configuration and one backend configuration (code, not database), so a later language is added in code only. Option names are always shown in their own language ("English", "Español") regardless of the active language. `en` is the base and global fallback. | Both |
| BR-02 | Resolution order: 1. signed-in user's `preferredLanguage` when supported; 2. local value when supported; 3. browser language (BR-03); 4. `en`. The session preference wins even over a choice made anonymously on Sign in before signing in. An unsupported `preferredLanguage` from the server is ignored and the next source applies. | Frontend |
| BR-03 | Browser language: the first entry of `navigator.languages` (or `navigator.language` when the list is empty); if its value starts with `es` ignoring case → `es`; anything else, or no value → `en`. Used only when no session preference and no valid local value exist. | Frontend |
| BR-04 | Local value: one dedicated `localStorage` key holding a supported code, written on every effective language change (anonymous choice, authenticated choice, applied session preference, failure restore). A missing, unsupported or unreadable value is ignored; storage errors never break rendering. Sign-out does not clear it. | Frontend |
| BR-05 | Request validation of both endpoints: JSON body ≤4 KB (`413` above, `415` non-JSON, keyless `400` for malformed/empty/`null` body); `language` required string, exact, case-sensitive and untrimmed match of a supported code; otherwise `400` key `language` "Choose a supported language.". Unknown body properties (`userId`, `organizationId`, `email`, …) are ignored. | Backend |
| BR-06 | Save semantics: when `language` equals the stored value → `200` with no write (no `updated_at` change, no audit). Otherwise, in one transaction: `preferred_language = language`, `updated_at = now`, audit row (BR-07) → `200 { preferredLanguage }`. Responses carry `Cache-Control: no-store`. | Backend |
| BR-07 | Audit: action `user.language_updated`, entity `user` = session user id, actor = session user, metadata exactly `{ "from": "<old code>", "to": "<new code>" }`, client IP as in account-settings BR-07. Organization: internal → session organization; portal → organization of the current portal account. Catalog label "Language updated" (translated in the frontend per BR-14). No names, email or other user data. | Backend |
| BR-08 | Account creation: `POST /organization-registrations`, `POST /invitations/accept` and `POST /portal/invitations/accept` accept an optional `preferredLanguage`; a supported code is stored, anything else (absent, `null`, non-string, unsupported) stores `en` without error. The frontend always sends the active language. `accept-existing` endpoints never change an existing user's preference. | Both |
| BR-09 | Authenticated switch: the new language is applied at once, then saved; while the save is in flight both options are `aria-disabled` and repeat choices send nothing. Any failure (`400`, `401`, `413`, `415`, `5xx`, network) restores the previous language and local value and shows the toast "We couldn't save your language. Try again." / "No pudimos guardar tu idioma. Inténtalo de nuevo." in the restored language; a `401` then follows the existing expired-session behavior. Choosing the active language sends no request. | Frontend |
| BR-10 | Formatting: `en` → `en-US`, `es` → `es-CO` for dates, times, numbers, currencies, percentages and relative times; values are formatted at render time from raw data and re-rendered on switch; nothing formatted is stored or sent. Time zone stays the organization or branch zone each feature already uses; currency stays the organization currency. PrimeNG month/day names, date-picker texts, paginator, filters, empty messages and ARIA texts follow the active language; first day of week follows CLDR week data for the locale (Sunday for both `en-US` and `es-CO`). | Frontend |
| BR-11 | Missing key: development → the key itself is rendered and one console warning per key and language is logged; production → the English value is rendered with no warning to the user. BR-12 parity guarantees an English value exists. | Frontend |
| BR-12 | `npm run i18n:check` fails (non-zero exit, listing file and key/line) when: a key exists in one language file of a scope and not in the other; a JSON object contains a duplicate key; a value is empty; interpolation parameters of a key differ between languages; a `.html` template has a static text node containing letters, or a static `aria-label`, `aria-description`, `placeholder`, `title`, `alt` or `label` attribute value, that is not in the versioned allowlist file. Bindings, translation pipes/directives, icons-only markup and allowlisted entries (brand name "FieldOps", symbols, units) pass. | Frontend |
| BR-13 | Backend messages: a field error is translated through a frontend catalog keyed by field key and the exact approved English message; no match → the generic localized field message "Check this value." / "Revisa este valor."; general failures use the existing `ApiError.kind` messages, localized; known `code` values (403/404/409) map to localized messages, unknown codes fall back to the kind message. Backend `title`, `detail` and unknown messages are never shown. The correlation (`traceId`) is preserved and shown wherever it is shown today. | Frontend |
| BR-14 | Code-based labels: role names by `role.code`, audit actions by `action`, notification titles/bodies by `template_code` with values from `payload`, statuses and enums by their code. Unknown role or audit code → the server-provided name; unknown notification template → the stored subject. Stored snapshots and payload values are shown unchanged. | Frontend |
| BR-15 | Never translated: names of people, organizations, branches and customers; addresses; notes, descriptions and any user-entered content; request, quote, job, invoice and visit numbers; audit `before_data`/`after_data`/metadata values; internal codes. | Frontend |
| BR-16 | Spanish glossary (mandatory, Latin American usage, `tú` form): see the Glossary table. | Frontend |
| BR-17 | Spanish Privacy and Terms show, above the content in secondary text, "Esta traducción está pendiente de revisión jurídica profesional."; English pages show no note. | Frontend |
| BR-18 | Selector. Menu item label "Language" / "Idioma" with the active option name as secondary text; Enter, Space or Right Arrow opens the sublist, Up/Down move, Escape or Left Arrow closes the sublist returning focus to **Language**, a second Escape keeps the existing menu behavior. Options are `menuitemradio` with `aria-checked`, a check icon and visible selected styling on the active one. Choosing closes the menu and returns focus to the menu button. Footer selector (Sign in, organization registration, password recovery request/reset, internal and portal invitation acceptance, portal sign-in and recovery, public service request, public quote approval, Privacy, Terms): a button with a language icon, the active option name and accessible name "Language: English" / "Idioma: Español", opening the same two options with the same keyboard rules and Escape returning focus to the button. | Frontend |
| BR-19 | `<html lang>` equals the active code at all times; document titles and breadcrumbs are translated and update on switch. | Frontend |
| BR-20 | Resources: a root scope (shells, navigation, common actions, validation, errors, enums, glossary terms, selector) is loaded at bootstrap for the active language; each feature has its own scope with `en` and `es` files, loaded when one of its routes activates and reloaded for the new language on switch; the English scope is loaded as fallback only when needed (BR-11). | Frontend |

### Glossary

| English | Spanish |
| ------- | ------- |
| Service request | Solicitud de servicio |
| Quote | Cotización |
| Job | Trabajo |
| Work order | Orden de trabajo |
| Visit | Visita |
| Dispatch | Programación |
| Technician | Técnico |
| Customer | Cliente |
| Invoice | Factura |
| Branch | Sucursal |
| Team | Equipo |
| Skill | Habilidad |
| Availability | Disponibilidad |
| Time off | Ausencia |
| Business hours | Horario comercial |

### Translation coverage

| Surface | Includes |
| ------- | -------- |
| Main shell | Navigation, top bar, global search, user menu, breadcrumbs, page titles, coming-soon pages |
| Authentication | Sign in, organization registration and onboarding wizard, password recovery, invitation acceptance |
| Operations | Dashboard, business overview, requests, quotes and quote builder, jobs and visits, completed-jobs review, dispatch/schedule, messages |
| Customers | Customers, customer detail and properties, portal invitations |
| Team and administration | Team, technicians, skills, availability, time off, business hours, users and permissions, company settings, branches, products and services, catalog categories |
| Finance | Invoices, payments, billing review, bank details |
| Insight | Reports, audit log, notifications |
| Account and support | Account settings, Help & support (including articles) |
| Technician shell | Shell, **More** panel, today's jobs, schedule, active visits, job details, progress, completion, time summary |
| Customer portal | Portal shell, sign-in, recovery, invitations, dashboard, requests, quotes, appointments, invoices, payments, account, messages |
| Public pages | Public service request, public quote approval, Privacy, Terms |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Active `x` | Active `y` | Selector choice | Any visitor or user | `y` supported, `y ≠ x` |
| Active `y` (unsaved) | Active `y` (saved) | Endpoint `200` | Signed-in user | BR-05 |
| Active `y` (unsaved) | Active `x` | Endpoint failure | Signed-in user | BR-09 |
| `preferred_language = x` | `preferred_language = y` | `PUT …/language` | Session user | BR-05, BR-06 |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`): `users`
  (`preferred_language` new, `updated_at`); `audit_logs` through the existing
  audit writer; portal links (`customer_contacts.portal_user_id`) read-only to
  resolve the current portal account organization.
- Schema amendment SA-24 (approved by the user 2026-10-21, OD-03):
  `ALTER TABLE users ADD COLUMN preferred_language varchar(10) NOT NULL DEFAULT 'en';`
  No CHECK constraint (later languages are added in code). Existing rows get
  `en`. The implementation adds SA-24 to the schema document beside the `users`
  table. Migration generation requires `--generate-migration`; it is never
  applied by agents.
- `users` is shared by every organization and portal link of a user: the
  preference applies everywhere the user signs in.

## Tenant isolation and authorization

- Organization context: user, organization and (portal) current account are
  resolved only from the validated internal or portal session cookie. Neither
  endpoint accepts a user, organization, contact or customer identifier.
- Client-provided identifiers: ignored (BR-05); they never change which user is
  written or where the audit row is stored.
- Another user's language: unreachable by construction.
- Permission: `PUT /account/language` any valid internal session (`[Authorize]`,
  no role policy); `PUT /portal/account/language` any valid portal session. A
  missing, revoked or wrong-type session → `401`.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| PUT | `/account/language` | JSON `{ language }` | `200 { preferredLanguage }` | `400` key `language` (keyless for malformed body) · `401` · `413` · `415` | Internal session |
| PUT | `/portal/account/language` | JSON `{ language }` | `200 { preferredLanguage }` | Same as above | Portal session |
| GET | `/sessions/current` (changed) | — | Existing body + `user.preferredLanguage` | Unchanged | Internal session |
| POST | `/sessions` (changed) | Unchanged | Existing body + `user.preferredLanguage` | Unchanged | Anonymous |
| GET | `/portal/sessions/current` (changed) | — | Existing body + `user.preferredLanguage` | Unchanged | Portal session |
| POST | `/portal/sessions` (changed) | Unchanged | Existing body + `user.preferredLanguage` | Unchanged | Anonymous |
| POST | `/portal/sessions/current/account` (changed) | Unchanged | Existing body + `user.preferredLanguage` | Unchanged | Portal session |
| POST | `/organization-registrations` (changed) | Existing body + optional `preferredLanguage` | Unchanged | Unchanged (BR-08: no new error) | Anonymous |
| POST | `/invitations/accept` (changed) | Existing body + optional `preferredLanguage` | Unchanged | Unchanged | Anonymous |
| POST | `/portal/invitations/accept` (changed) | Existing body + optional `preferredLanguage` | Unchanged | Unchanged | Anonymous |

All other API codes, messages and field keys are unchanged.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Language sublist (three shells) | Options `aria-disabled` while saving | Not applicable | BR-09 restore + toast | Every signed-in user | UI in the new language, menu closed, focus on menu button |
| Footer selector | Not applicable | Not applicable | Not applicable: local only | Anonymous pages | UI in the new language |
| Feature scope switch | Current texts stay until the new scope arrives; no flash of keys | Not applicable | Scope load failure → English fallback (BR-11) | Not applicable | Feature in the new language |
| Every translated screen | Its existing states, translated | Existing empty/filtered-empty, translated | Existing errors, translated (BR-13) | Existing, translated | Existing, translated |

- Mockups: None. The Language item follows the existing user-menu item pattern;
  the footer selector follows existing tokens and the authentication footer.
- Responsive: the footer selector stays inside the page width at 320px; Spanish
  copy (typically longer) must not overflow buttons, tabs, chips, table
  headers or navigation at 320, 768 and 1280px — text wraps or truncates with
  the full text available as a tooltip only where truncation already exists.
- Accessibility: BR-18 keyboard and ARIA; visible focus; existing menu/dialog
  focus trapping, Escape and focus return unchanged; all `aria-label`,
  `aria-description`, live-region and visually hidden texts translated; option
  names carry `lang="en"` / `lang="es"` when shown in the other language.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| No valid session on a language endpoint | `401`; frontend restores previous language, toast, then expired-session flow | None |
| Unsupported, missing or wrong-case `language` | `400` `language` "Choose a supported language."; frontend restores + toast | None |
| Malformed body / non-JSON / oversize | Keyless `400` / `415` / `413`; frontend restores + toast | None |
| Save fails (`5xx`, network) | BR-09 restore + toast | None (transaction rolled back) |
| Same language saved | `200` | None, no audit |
| Translation scope fails to load | English fallback for that scope | None |
| Unknown server field message or code | BR-13 generic localized text; `traceId` preserved | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An anonymous visitor (parameterized: local `es` + browser `en-US`; local `en` + browser `es-CO`; no local + browser `es`, `es-419`, `ES-mx`; no local + browser `pt-BR`, `fr`, none; local `xx` + browser `es`) | Sign in loads | The page renders in the BR-02/BR-03 language from first paint, `<html lang>` matches and the local value is set accordingly |
| AC-02 | Local value `en`, user with `preferredLanguage` `es` (internal and portal, parameterized) | They sign in, or reload with a valid session | The UI ends in Spanish, `<html lang="es">`, the local value becomes `es`, and no language request is sent; an unsupported server value (`pt`) leaves the local language |
| AC-03 | Each shell (main user menu, technician **More**, portal user menu; parameterized) | The user opens **Language** with the keyboard and moves through it | The item shows the active option name; the sublist lists **English** and **Español**; the active one has a check icon, selected styling and `aria-checked="true"`; Escape/Left closes the sublist with focus on **Language**; choosing closes the menu with focus on the menu button |
| AC-04 | A user on a page with a dirty form, query parameters, an open drawer and a scrolled list | They switch language | No reload or navigation occurs; URL, form values, dirty state, drawer and scroll are unchanged; texts, document title, breadcrumbs, `<html lang>` and formatted values change; no discard-changes dialog appears |
| AC-05 | A signed-in internal user with `en` | They choose **Español** | `PUT /account/language` `{ language: "es" }` returns `200 { preferredLanguage: "es" }`; `preferred_language` and `updated_at` change; one `user.language_updated` row exists in the session organization with metadata exactly `{ from: "en", to: "es" }` and the client IP; the next `GET /sessions/current` returns `es` |
| AC-06 | A user whose stored value is `es` | The API receives `{ language: "es" }`; separately the user chooses the active option | `200`; `updated_at` unchanged; no audit row; choosing the active option in the UI sends no request |
| AC-07 | Invalid bodies (parameterized: `pt`, `ES`, `" es"`, `""`, `null`, number, missing; malformed JSON; non-JSON; >4 KB) sent directly to both endpoints | The request is processed | `400` `language` "Choose a supported language." or keyless `400` / `415` / `413` as BR-05; nothing stored; no audit |
| AC-08 | Users A (organization 1) and B (organization 2); no session | A sends `{ language: "es", userId: B, organizationId: 2 }`; an anonymous client calls both endpoints; a portal session calls `/account/language` and an internal session calls `/portal/account/language` | Only A changes; B unchanged; the audit row exists only in organization 1; the anonymous and wrong-session-type calls return `401` and change nothing |
| AC-09 | A portal user with two linked accounts in different organizations, current account in organization 2 | They choose **Español** in the portal menu | `PUT /portal/account/language` returns `200`; the preference is `es`; one `user.language_updated` row exists in organization 2 only; `GET /portal/sessions/current` and switching account return `preferredLanguage` `es` |
| AC-10 | A signed-in user in English | Saving **Español** fails (parameterized: `500`, network, `400`, `401`) | The UI shows Spanish while saving with options `aria-disabled`, then returns to English, the local value returns to `en`, the toast "We couldn't save your language. Try again." is shown; for `401` the existing expired-session flow follows |
| AC-11 | An anonymous visitor on each BR-18 footer page (parameterized) | They switch language with the footer selector using only the keyboard | The page changes language without reload or request, form input is kept, the local value is updated, the button's accessible name follows BR-18 and Escape returns focus to the button |
| AC-12 | Registration, internal invitation acceptance and portal invitation acceptance (parameterized) with the UI in Spanish; and API calls with `preferredLanguage` absent, `pt`, `null` or `5` | The account is created | UI flows store `es`; the API cases store `en` without a new error; `accept-existing` leaves an existing user's preference unchanged |
| AC-13 | The generated migration and an existing database snapshot | The persistence diff is reviewed | `users.preferred_language varchar(10) NOT NULL DEFAULT 'en'` is added without CHECK, existing rows read `en`, SA-24 appears in the schema document, and the migration is not applied |
| AC-14 | The en and es resources of every scope | `npm run i18n:check` runs on the current tree, then on fixtures with a missing key, an extra key, a duplicate JSON key, an empty value, a mismatched interpolation parameter and a non-allowlisted template literal/attribute (parameterized) | The current tree passes; each fixture fails with a non-zero exit naming the file and key or line; the frontend CI workflow runs the command |
| AC-15 | A key present only in the code path of a build (parameterized: development, production) | The screen renders | Development shows the key and logs one warning; production shows the English value; a production smoke pass of every shell and surface shows no unresolved key |
| AC-16 | A record with a date, amount in the organization currency, percentage, count and relative time in an organization time zone different from the browser's | It is shown in `en` then in `es` | Values use `en-US` then `es-CO` formatting (decimal/thousand separators, month names, currency symbol position) with the same time zone and currency; PrimeNG date picker month/day names, texts and first day of week follow BR-10; no formatted value is sent to the API |
| AC-17 | Backend `400` responses with a known field message, an unknown field message, a known `409` code, an unknown code and a `500` with `traceId` (parameterized) | The user is in Spanish | Known messages show the catalog Spanish text under the field; unknown messages show "Revisa este valor."; the known code shows its Spanish message; the unknown code and `500` show the localized kind message with the correlation ID where it is shown today; no backend English text is displayed |
| AC-18 | A role, an audit action, a notification template and statuses/enums with known and unknown codes (parameterized) | Shown in Spanish | Known codes show Spanish labels and notification text rendered from the payload; unknown role/audit codes show the server name; an unknown template shows the stored subject; audit snapshots and payload values are unchanged |
| AC-19 | Records with person, organization, branch and customer names, an address, notes, numbers and audit snapshot values | Shown in Spanish | Those values are displayed exactly as stored |
| AC-20 | Spanish active, each shell and surface of the Translation coverage table (parameterized smoke: main shell, technician shell, portal shell, authentication, one representative page per surface) | Each renders its loading, empty, filtered-empty, error and success states and opens its dialogs, drawers, menus, toasts, tooltips and validation | Every visible and ARIA text is Spanish from resources with the BR-16 glossary; no English copy, unresolved key or overflow appears at 320, 768 and 1280px |
| AC-21 | Help & support, Privacy and Terms | Shown in English then Spanish | Full content is translated; the Spanish Privacy and Terms show the BR-17 note; English shows none |
| AC-22 | A user on a feature route whose scope is not loaded | They navigate to it, then switch language | The feature scope loads only on activation for the active language; on switch the new language scope loads and texts change without a key flash or reload; a failed scope load shows English |

## Testing requirements

Parameterized cases count as one method. The frontend count exceeds the
default range because the feature is cross-cutting (resolution, persistence,
selector in three shells, formatting, error translation and the check script are
distinct risks).

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Language code validation against the supported list, only if it lives outside the endpoint pipeline. ≤1 method | AC-07 |
| Backend integration | Internal endpoint success/unchanged/invalid with audit and `updated_at`; portal endpoint with current-account organization audit; session bodies include `preferredLanguage`; account creation language for the three flows; ignored identifiers, anonymous and wrong-session-type `401`. ≤6 methods | AC-02, AC-05 to AC-09, AC-12 |
| Authorization/tenant isolation | One representative test: identifiers of another user/organization ignored, audit only in the session organization; plus the wrong-session-type `401`s and portal organization resolution | AC-08, AC-09 |
| Persistence review | `database-reviewer` on the SA-24 mapping and generated migration | AC-13 |
| Frontend service/component | Resolution order and browser detection; session preference application; runtime switch preserving form/route state; authenticated save and failure restore; selector keyboard/ARIA in one shell plus item presence in the others; footer selector; formatting `en-US`/`es-CO` and PrimeNG locale; error catalog and code-based labels; missing-key behavior; lazy scope loading. ≤10 methods | AC-01 to AC-04, AC-10, AC-11, AC-15 to AC-18, AC-22 |
| Script | `i18n:check` against the current tree and failing fixtures | AC-14 |
| User visual QA | Spanish smoke of every surface, glossary, overflow at 320/768/1280px, legal note, user-entered content untouched, keyboard and focus in the selectors | AC-03, AC-11, AC-19, AC-20, AC-21 |

## Dependencies

- `specs/authenticated-app-shell/spec.md`, `specs/account-settings/spec.md`
  (user menu; account-settings OD-08 hid Language until this spec — FR-03 here
  adds it), `specs/technician-todays-jobs/spec.md` (**More** panel),
  `specs/customer-portal-dashboard/spec.md` (portal shell and sessions).
- `specs/sign-in/spec.md`, `specs/organization-onboarding/spec.md`,
  `specs/invitation-acceptance/spec.md`, `specs/password-recovery/spec.md`:
  session and account-creation contracts extended additively.
- `specs/audit-trail/spec.md`: audit writer and catalog; one new event.
- `specs/notifications-center/spec.md`: `template_code`/`payload` rendering.
- Every feature spec of the Translation coverage table: copy is translated,
  behavior unchanged.
- New dependency: Transloco (authorized by the user 2026-10-21).
- Implementation classification for `spec-impl`: Backend TARGETED (SA-24,
  two endpoints, session contracts, account creation); Frontend FULL
  (new i18n infrastructure in `app.config.ts`, shared selector, every feature,
  CI step, `docs/frontend`). `--generate-migration` required.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Spanish copy uses informal `tú` and neutral Latin American vocabulary. |
| AS-02 | One shared language service and selector are reused by the three shells, the footer and the future Preferences page. |
| AS-03 | Approved English copy of existing specs stays the English source; this spec does not rewrite English text. |
| AS-04 | The error catalog of BR-13 covers the field messages currently defined in approved specs; new specs add their entries. |
| AS-05 | CLDR week data gives Sunday as the first day for `es-CO`; PrimeNG takes the value from the locale, so it equals `en-US`. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Portal user persistence and selector location | Persist + portal menu / local only + footer | Yes | Persist; `PUT /portal/account/language`; selector in portal user menu (2026-10-21) |
| OD-02 | Internal endpoint contract | Proposed | Yes | `PUT /account/language` `{ language }` → `{ preferredLanguage }`, code-validated `en`/`es`; `POST /sessions` returns the preference (2026-10-21) |
| OD-03 | Language of new accounts | Store active UI language / always `en` | Yes | Store active language in registration, internal and portal invitation acceptance; existing users `en`; SA-24 approved (2026-10-21) |
| OD-04 | Backend field messages without codes | Catalog by field + exact message / client only / backend codes | Yes | Catalog; unmatched → generic localized message (2026-10-21) |
| OD-05 | Backend labels with stable codes | Translate by code / show backend text | Yes | Translate roles, audit actions, notifications by code; snapshots unchanged (2026-10-21) |
| OD-06 | Hardcoded text enforcement | `i18n:check` templates + allowlist / plus ESLint rule | Yes | `i18n:check` with versioned allowlist; no ESLint rule (2026-10-21) |
| OD-07 | Help & support, Privacy, Terms | Translate fully / partially | Yes | Fully; Spanish legal note BR-17 (2026-10-21) |
| OD-08 | Spec split | One FULL spec / two specs | Yes | One spec, ACs grouped by surface (2026-10-21) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02 |
| FR-03 | AC-03, AC-11 |
| FR-04 | AC-04, AC-11 |
| FR-05 | AC-05, AC-06, AC-10 |
| FR-06 | AC-05, AC-06, AC-07, AC-08, AC-09 |
| FR-07 | AC-02, AC-05, AC-09 |
| FR-08 | AC-12 |
| FR-09 | AC-13 |
| FR-10 | AC-19, AC-20 |
| FR-11 | AC-18 |
| FR-12 | AC-17 |
| FR-13 | AC-16 |
| FR-14 | AC-15, AC-20 |
| FR-15 | AC-14 |
| FR-16 | AC-21 |
| FR-17 | AC-22 |
| FR-18 | AC-03, AC-11, AC-20 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-21 | — → DRAFT     | Created |
| 2026-10-21 | DRAFT → APPROVED | Approved by user via /spec approve |
