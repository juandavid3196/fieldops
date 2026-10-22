# Application dark mode and account preferences

| Field    | Value           |
| -------- | --------------- |
| Feature  | `app-dark-mode` |
| Type     | Full-stack      |
| Status   | APPROVED        |
| Created  | 2026-10-21      |
| Updated  | 2026-10-21      |
| Approved | 2026-10-21      |

## Context and objective

FieldOps renders only in light mode, although the tokens and the PrimeNG preset
already declare dark values, and the Preferences section of Account settings is
hidden (account-settings OD-08). This feature adds a per-user appearance
preference (**System**, **Light**, **Dark**; default **System**) applied across
every shell, feature and public page, persisted per user and per browser, and
activates `/account/preferences` with Language and Appearance. Success means a
user can change appearance from Preferences or any shell menu without reload or
loss of work, the choice survives sign-in on any device, the first paint already
uses the correct theme, and no screen shows light-only colors in dark mode.
This spec supersedes account-settings OD-08 only for the Preferences section and
the Dark mode menu item; My profile and Security are unchanged.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Signed-in internal user, any role (including unknown codes), any branch scope | Open `/account/preferences`; preview and save their own language and appearance; change appearance from the main shell user menu or the technician shell **More** panel; persist their own `preferredTheme` through `PUT /account/theme` | Change another user's preferences; supply a user or organization identifier; set an unsupported theme or source |
| Signed-in portal user | Change appearance from the portal shell user menu; persist their own `preferredTheme` through `PUT /portal/account/theme` | Same as above; use the internal endpoint with a portal session or the reverse; open `/account/preferences` |
| Anonymous visitor | See authentication and public pages in the theme resolved from the local value or the device (BR-02) | Choose a theme (no selector); call either theme endpoint (`401`) |
| System | Resolve user and organization only from the validated session; write audit rows | Trust client user/organization identifiers; record device information |

## Scope

- Theme resolution, local persistence, authenticated persistence and document
  theme state, applied before Angular bootstraps.
- Live following of the device color scheme while the preference is **System**.
- `/account/preferences` with Language and Appearance, Save preferences, Reset
  and the discard-changes dialog.
- **Appearance** item in the main shell user menu, the technician shell **More**
  panel and the portal shell user menu.
- Schema amendment SA-25 (`users.preferred_theme`), migration generation (never
  applied), session contracts, two self-service endpoints and the
  `user.theme_updated` audit event.
- Dark variants through the centralized tokens and the PrimeNG preset for every
  surface of the Theme coverage table, including charts and overlays.
- `npm run theme:check` (hardcoded colors, versioned allowlist, token dark
  variants), added to the frontend CI.
- English and Spanish copy for every new text; `docs/frontend` styling
  architecture updated with the theme resolution and `theme:check`.

## Non-goals

- Per-organization palettes, brand-color customization or additional themes.
- A theme selector on anonymous pages.
- Changes to PDFs, CSV files or emails.
- Altering uploaded images, photos, logos or evidence.
- Changing My profile or Security behavior, or showing Help & support.
- A Preferences page in the customer portal.
- Storing the local theme at account creation (new accounts get `system`).
- Changing the language endpoints, their validation or their audit.

## User flow

1. A user whose last local value is `dark` opens FieldOps; the first paint is
   already dark.
2. They sign in; the session carries `preferredTheme` `system`, which wins; the
   device is light, so the UI becomes light and the local value becomes `system`.
3. In **Account settings → Preferences** they select **Dark** and **Español**;
   both are previewed at once; **Save preferences** and **Reset** become enabled.
4. They select **Save preferences**; the language and theme are saved through
   their endpoints and "Preferencias guardadas" is shown.
5. Later, from the user menu, they choose **Appearance → System**; the UI
   follows the device and the choice is saved immediately.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must resolve the theme per BR-02 and apply the BR-04 document state before Angular bootstraps, so the first paint uses the resolved theme. |
| FR-02 | When an internal or portal session is established or revalidated with a supported `preferredTheme`, the frontend must apply it per BR-02 and update the local value. |
| FR-03 | While the effective preference is `system`, the frontend must follow device color-scheme changes live per BR-05. |
| FR-04 | Every theme change must be applied without reload, navigation or session re-request, preserving route, query parameters, form values and dirty state, open overlays and scroll position. |
| FR-05 | The frontend must provide `/account/preferences` inside the main shell for every signed-in internal role and branch scope, and the account section navigation must list **My profile**, **Preferences** and **Security** in that order per BR-09; `/account` still opens My profile and the menus' Account settings targets are unchanged. |
| FR-06 | Preferences must show the Language and Appearance sections, the info note and the actions of the approved design with the BR-09 copy. |
| FR-07 | Selecting a language or appearance on Preferences must preview it immediately per BR-10; **Reset** restores the last saved values; **Save preferences** and **Reset** are disabled while no value differs from the saved one. |
| FR-08 | **Save preferences** must save each changed preference through its own endpoint per BR-11, keeping successes and restoring only failures with the BR-12 toast. |
| FR-09 | Leaving Preferences with unsaved changes must show the existing discard-changes dialog per BR-13, and a menu change must update the page's saved state per BR-13. |
| FR-10 | The main shell user menu, the technician shell **More** panel and the portal shell user menu must contain an **Appearance** item opening **System**, **Light** and **Dark** per BR-14. |
| FR-11 | Choosing an appearance from a menu must apply it and save it immediately per BR-15. |
| FR-12 | `PUT /account/theme` (internal session) and `PUT /portal/account/theme` (portal session) must validate per BR-06, update only the session user's `preferred_theme` per BR-07 and audit per BR-08. |
| FR-13 | Every session body that includes `user.preferredLanguage` must also include `user.preferredTheme`. |
| FR-14 | Schema amendment SA-25 must add `users.preferred_theme` with existing users set to `system`; the migration is generated and never applied. |
| FR-15 | Every surface of the Theme coverage table must render in light and dark through the centralized tokens and the PrimeNG preset per BR-16 and BR-17. |
| FR-16 | `npm run theme:check` must enforce BR-18 and run in the frontend CI workflow. |
| FR-17 | Both themes must meet the accessibility rules of BR-19. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Supported preferences: `system`, `light`, `dark` (exact, lowercase), defined once in frontend code and once in backend code, not in the database. Default `system`. Effective modes: `light`, `dark`. Option labels: System / Sistema, Light / Claro, Dark / Oscuro. | Both |
| BR-02 | Resolution. Preference: 1. signed-in user's `preferredTheme` when supported; 2. local value when supported; 3. `system`. Effective mode: `light` / `dark` as chosen; `system` → `dark` when the device reports `prefers-color-scheme: dark`, `light` when it reports light; when the media query is unavailable → `light`. An unsupported server value is ignored and the next source applies. | Frontend |
| BR-03 | Local value: one dedicated `localStorage` key `fieldops.theme` holding a supported preference, written on every persisted or applied session change (applied session preference, successful save, failure restore). Preferences page previews do not write it (BR-10). A missing, unsupported or unreadable value is ignored; storage errors never break rendering. Sign-out does not clear it. | Frontend |
| BR-04 | Document state, always equal to the effective mode: the stable class `app-dark` on `<html>` only in dark; document `color-scheme` `light` or `dark`; `<meta name="theme-color">` `#0f9d9a` in light, `#0b1620` in dark. A small script in `index.html`, executed before the application bundle, applies BR-02 steps 2–3 so the first paint matches; the application takes over after bootstrap without a visible change when the session agrees. | Frontend |
| BR-05 | System following: while the effective preference is `system`, a device color-scheme change updates the BR-04 state at once, without reload or request. With `light` or `dark`, device changes are ignored. Listening stops when the preference leaves `system`. | Frontend |
| BR-06 | Request validation of both endpoints: JSON body ≤4 KB (`413` above, `415` non-JSON, keyless `400` for malformed/empty/`null` body). `theme` required string, exact, case-sensitive and untrimmed match of BR-01, otherwise `400` key `theme` "Choose a supported appearance.". `source` required string, exact match of the endpoint's list — internal: `preferences_page`, `user_menu`, `technician_menu`; portal: `portal_menu` — otherwise `400` key `source` "Choose a supported source.". Both keys are reported when both are invalid. Unknown body properties (`userId`, `organizationId`, `email`, …) are ignored. | Backend |
| BR-07 | Save semantics: after validation, when `theme` equals the stored value → `200 { preferredTheme }` with no write (no `updated_at` change, no audit). Otherwise, in one transaction: `preferred_theme = theme`, `updated_at = now`, audit row (BR-08) → `200 { preferredTheme }`. Responses carry `Cache-Control: no-store`. | Backend |
| BR-08 | Audit: action `user.theme_updated`, entity `user` = session user id, actor = session user, metadata exactly `{ "previousTheme": "<old>", "newTheme": "<new>", "source": "<source>" }`, client IP as in account-settings BR-07. Organization: internal → session organization; portal → organization of the current portal account. No user agent, device, OS or browser data, names or email. Catalog label "Appearance updated" / "Apariencia actualizada". | Backend |
| BR-09 | Preferences copy (en / es). Breadcrumb "Account / Preferences" / "Cuenta / Preferencias"; nav item "Preferences" / "Preferencias"; layout subtitle for every account section "Manage your profile, preferences and security settings." / "Administra tu perfil, preferencias y configuración de seguridad."; section "Language" / "Idioma" with select label "Display language" / "Idioma de visualización", options from the supported language list (each name in its own language) and helper "Changes the language used across your account." / "Cambia el idioma usado en toda tu cuenta."; section "Appearance" / "Apariencia" with cards System "Match your device settings for a seamless experience." / "Usa la configuración de tu dispositivo para una experiencia uniforme.", Light "Always use the light theme." / "Usa siempre el tema claro.", Dark "Always use the dark theme." / "Usa siempre el tema oscuro."; info note "These preferences apply only to your user account." / "Estas preferencias se aplican solo a tu cuenta de usuario."; buttons **Save preferences** / **Guardar preferencias** ("Saving…" / "Guardando…") and **Reset** / **Restablecer**; success toast "Preferences saved" / "Preferencias guardadas". | Frontend |
| BR-10 | Preview: the saved values are the session's `preferredLanguage` and `preferredTheme`. Selecting a value applies it at once (language through the existing language service, theme through BR-04/BR-05) without request and without writing either local value. The page is dirty when any selected value differs from its saved value; returning to the saved values makes it pristine. **Reset** applies the saved values and makes the page pristine. Save and Reset are disabled while pristine and while saving. | Frontend |
| BR-11 | Save: sends only the changed preferences, concurrently — language through `PUT /account/language` (app-i18n-en-es contract unchanged), theme through `PUT /account/theme` with `source` `preferences_page`. While saving, the button shows "Saving…", controls and actions are disabled and repeat clicks send nothing. Each `200` makes that value the saved value and writes its local value. All succeed → BR-09 success toast, page pristine. | Frontend |
| BR-12 | Save failure (`400`, `401`, `413`, `415`, `5xx`, network), per preference: a failed preference returns to its saved value (UI and local value) while a successful one stays saved. Toast, in the language active after restore: only language failed → "We couldn't save your language. Try again." / "No pudimos guardar tu idioma. Inténtalo de nuevo."; only appearance failed → "We couldn't save your appearance. Try again." / "No pudimos guardar tu apariencia. Inténtalo de nuevo."; both failed → "We couldn't save your preferences. Try again." / "No pudimos guardar tus preferencias. Inténtalo de nuevo.". No success toast when any failed. A `401` then follows the existing expired-session behavior. | Frontend |
| BR-13 | Unsaved changes: navigating away while dirty shows the existing discard-changes dialog; **Keep editing** keeps the page and previews; **Discard** applies the saved values, then navigates. The dialog is bypassed once the session has been cleared (sign-out). A successful language or appearance change from a shell menu while Preferences is open becomes that preference's saved and selected value on the page; the other preference keeps its pending change. | Frontend |
| BR-14 | Menu item: label "Appearance" / "Apariencia", secondary text = active option label, placed directly after **Language** in each of the three menus. Sublist options System, Light, Dark as `menuitemradio` with `aria-checked`, a check icon and visible selected styling on the active one. Desktop opens a side flyout and mobile an inline compact sublist, with the same breakpoint, keyboard rules (Enter/Space/Right Arrow open, Up/Down move, Escape/Left Arrow close returning focus to **Appearance**) and focus return as the app-i18n-en-es BR-18 Language item. Choosing closes the menu and returns focus to the menu button. | Frontend |
| BR-15 | Menu save: choosing a different option applies it at once, then calls `PUT /account/theme` with `source` `user_menu` (main shell) or `technician_menu` (technician shell), or `PUT /portal/account/theme` with `portal_menu` (portal shell). While in flight the options are `aria-disabled` and repeat choices send nothing. `200` writes the local value. Any failure restores the previous preference and local value and shows "We couldn't save your appearance. Try again." / "No pudimos guardar tu apariencia. Inténtalo de nuevo."; a `401` then follows the expired-session behavior. Choosing the active option sends nothing. | Frontend |
| BR-16 | Theming: colors come only from the `--fo-*` tokens in `src/styles/_tokens.scss` and the PrimeNG preset (dark selector `.app-dark`); no duplicated stylesheets or components. Every color token has a light and a dark value covering: page background; surfaces and cards; header; primary, secondary and muted text; headings; borders and dividers; inputs and placeholders; hover, selected, focus and disabled; shadows, masks and overlays; success, warning, danger, info and neutral statuses; skeletons and loading states; chart series, axes, gridlines, legends and tooltips. The navy sidebar stays dark in both themes. Uploaded images, photos, logos and evidence are rendered unchanged. | Frontend |
| BR-17 | Status meaning: each status keeps the same hue family in both themes and is always conveyed by text or icon as well as color. Chart series keep their meaning; dark series colors reach ≥3:1 against the chart surface. | Frontend |
| BR-18 | `npm run theme:check` fails (non-zero exit, listing file and line) when, outside the versioned allowlist file: a `.scss`/`.css` file other than `_tokens.scss`, a `.html` template (text, `style`, `fill`, `stroke`, `color` and `[style.*]` static values) or a component `.ts` file (inline styles and color string literals) contains a hex, `rgb()`/`rgba()`, `hsl()`/`hsla()` color or a CSS named color (`white`, `black`, …); or a color token in `_tokens.scss` is not a `light-dark()` pair and is not allowlisted as mode-invariant. Allowlist entries name file, value and reason (brand, status solid fills, the `theme-color` values and the BR-04 script, navy sidebar tokens). `transparent`, `currentColor`, `inherit` and token references pass. | Frontend |
| BR-19 | Accessibility in both themes: text contrast ≥4.5:1 (≥3:1 for large text) and non-text UI and focus indicators ≥3:1 against adjacent colors (WCAG 2.2 AA); visible focus ring on every interactive element; selected, disabled, hover and error states distinguishable by more than color; full keyboard operation. Appearance cards are a labelled `radiogroup` ("Appearance") of three `radio` items with `aria-checked`: one Tab stop on the selected card, arrow keys move and select, Space selects; the selected card shows a filled radio indicator and a primary border. | Frontend |

### Theme coverage

| Surface | Includes |
| ------- | -------- |
| Shells | Main shell (sidebar, header, global search, user menu, breadcrumbs), technician shell and **More** panel, customer portal shell, coming-soon pages |
| Authentication and public | Sign in, registration and onboarding, password recovery, invitation acceptance, portal sign-in/recovery/invitations, public service request, public quote approval, public invoice, Privacy, Terms |
| Operations | Dashboard, business overview, requests, quotes and builder, jobs and visits, completed-jobs review, schedule/dispatch, messages, technician pages |
| Customers, team and administration | Customers and properties, team, technicians, skills, availability, time off, business hours, users and permissions, company settings, branches, products and services, catalog |
| Finance | Invoices, payments, billing review, bank details |
| Insight | Reports and charts, audit log, notifications |
| Account and support | Account settings (all sections), Help & support |
| Overlays and components | Menus, dialogs, drawers, popovers, tooltips, toasts, confirmations; PrimeNG DatePicker/Calendar, Select, Table, Paginator, inputs and overlays; skeletons and empty/error states |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Pristine | Dirty | Selecting a value ≠ saved | Internal user | — |
| Dirty | Pristine | Reset, Discard, reselecting saved values, all saves `200` | Internal user | — |
| Dirty | Partially saved | Some saves fail | Internal user | BR-12 |
| `preferred_theme = x` | `preferred_theme = y` | `PUT …/theme` | Session user | BR-06, BR-07, `y ≠ x` |
| Effective `light`/`dark` (system) | Opposite mode | Device color-scheme change | System | Preference `system` |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`): `users`
  (`preferred_theme` new, `preferred_language` read through existing contracts,
  `updated_at`); `audit_logs` through the existing audit writer; portal links
  (`customer_contacts.portal_user_id`) read-only to resolve the current portal
  account organization.
- Schema amendment SA-25 (approved by the user 2026-10-21):
  `ALTER TABLE users ADD COLUMN preferred_theme varchar(10) NOT NULL DEFAULT 'system';`
  No CHECK constraint (values enforced in code). Existing rows get `system`.
  The implementation adds SA-25 to the schema document beside the `users`
  table and SA-24. Migration generation requires `--generate-migration`; it is
  never applied by agents.
- `users` is shared by every organization and portal link of a user: the
  preference applies everywhere the user signs in.

## Tenant isolation and authorization

- Organization context: user, organization and (portal) current account are
  resolved only from the validated internal or portal session cookie. Neither
  endpoint accepts a user, organization, contact or customer identifier.
- Client-provided identifiers: ignored (BR-06); they never change which user is
  written or where the audit row is stored.
- Another user's preferences: unreachable by construction.
- Permission: `PUT /account/theme` any valid internal session (`[Authorize]`,
  no role or branch policy); `PUT /portal/account/theme` any valid portal
  session. A missing, revoked or wrong-type session → `401`.
- `/account/preferences` uses the existing account route guard; no role or
  branch check.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| PUT | `/account/theme` | JSON `{ theme, source }`, `source` ∈ `preferences_page`, `user_menu`, `technician_menu` | `200 { preferredTheme }` | `400` keys `theme`, `source` (keyless for malformed body) · `401` · `413` · `415` | Internal session |
| PUT | `/portal/account/theme` | JSON `{ theme, source }`, `source` = `portal_menu` | `200 { preferredTheme }` | Same as above | Portal session |
| GET | `/sessions/current` (changed) | — | Existing body + `user.preferredTheme` | Unchanged | Internal session |
| POST | `/sessions` (changed) | Unchanged | Existing body + `user.preferredTheme` | Unchanged | Anonymous |
| GET | `/portal/sessions/current` (changed) | — | Existing body + `user.preferredTheme` | Unchanged | Portal session |
| POST | `/portal/sessions` (changed) | Unchanged | Existing body + `user.preferredTheme` | Unchanged | Anonymous |
| POST | `/portal/sessions/current/account` (changed) | Unchanged | Existing body + `user.preferredTheme` | Unchanged | Portal session |
| PUT | `/account/language`, `/portal/account/language` | Unchanged (app-i18n-en-es) | Unchanged | Unchanged | Unchanged |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Preferences | Not applicable: saved values come from the session already loaded by the guard | Not applicable | BR-12 per-preference restore + toast | Every signed-in internal role and branch scope | BR-09 toast; page pristine |
| Appearance sublist (three shells) | Options `aria-disabled` while saving | Not applicable | BR-15 restore + toast | Every signed-in user of the shell | Theme applied, menu closed, focus on menu button |
| Every themed surface | Existing skeletons/loaders in the active theme | Existing, themed | Existing, themed | Existing, themed | Existing, themed |

- Mockups: `design/assets/Interfaz de configuración de cuenta unificada.png`
  (Approved, 2026-10-21) — Preferences section. The menu items follow the
  approved Language item pattern; dark surfaces follow the tokens.
- Responsive (existing `md` 48rem / `lg` 64rem breakpoints): account layout as
  account-settings; below `md` the three Appearance cards stack in one column
  and the language select is full width; actions stay visible without
  horizontal scroll at 320px; touch targets ≥44px below `lg`.
- Accessibility: BR-19; one `h1`; Language and Appearance are `h2`; the select
  has a visible label and helper linked by `aria-describedby`; card icons are
  decorative.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| No valid session on a theme endpoint | `401`; frontend restores, toast, then expired-session flow | None |
| Unsupported / wrong-case / missing `theme` | `400` `theme` "Choose a supported appearance."; frontend restores + toast | None |
| Missing or wrong-endpoint `source` | `400` `source` "Choose a supported source."; frontend restores + toast | None |
| Malformed body / non-JSON / oversize | Keyless `400` / `415` / `413`; frontend restores + toast | None |
| Save fails (`5xx`, network) | BR-12 or BR-15 restore + toast | None (transaction rolled back) |
| Same theme saved | `200` | None, no audit |
| Storage unavailable or invalid local value | Ignored; BR-02 next source | None |
| `matchMedia` unavailable | Effective `light` for `system` | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | No session (parameterized: local `dark` + device light; local `light` + device dark; local `system`, none or `sepia` + device dark; same + device light; no local + no `matchMedia`; storage throws + device dark) | Sign in loads | Before the application bundle runs, `<html>` already has `app-dark` only when the BR-02 mode is dark, and `color-scheme` and `theme-color` match BR-04; no other mode is painted first |
| AC-02 | Local `dark`, user with `preferredTheme` `light` (internal and portal, parameterized); separately `preferredTheme` `system` with device dark, and unsupported server value `sepia` | They sign in, or reload with a valid session | The UI ends `light` and the local value becomes `light`; `system` gives dark with local `system`; `sepia` leaves the local preference applied; no theme request is sent |
| AC-03 | Effective preference `system`, device dark | The device switches to light and back; then the user chooses **Light** and the device switches to dark | The theme, `color-scheme` and `theme-color` follow each device change without reload or request; after choosing **Light** device changes are ignored |
| AC-04 | A user on a page with a dirty form, query parameters, an open drawer and a scrolled list | They change appearance from the menu | No reload, navigation or extra session request occurs; URL, form values, dirty state, drawer and scroll are unchanged; no discard dialog appears |
| AC-05 | Signed-in users (parameterized: `owner`, `viewer`, `technician`, unknown role, user limited to one branch); and no session | They open Account settings and select **Preferences**; the visitor opens `/account/preferences` | The nav lists **My profile**, **Preferences**, **Security** with `aria-current="page"` on Preferences; breadcrumb and subtitle match BR-09; `/account` still ends on `/account/profile`; menu Account settings targets are unchanged; the visitor ends on `/auth/sign-in` |
| AC-06 | A user with saved `en` and `system` | Preferences renders | Language shows **Display language** with English and Español, English selected, and the helper; Appearance shows System (selected), Light and Dark with BR-09 texts; the info note is shown; **Save preferences** and **Reset** are disabled; layout matches the approved design |
| AC-07 | Preferences open | The user uses only the keyboard on Appearance | Tab reaches one card (the selected); arrow keys move and select; Space selects; each card exposes `role="radio"` and `aria-checked`; the selected card shows the filled radio and primary border; focus is visible in both themes |
| AC-08 | Saved `en` + `system` | The user selects **Dark** and **Español**, then **Reset**; separately reselects the saved values | Dark and Spanish apply at once with no request and both local values unchanged; Save and Reset are enabled; Reset returns to English + saved theme and disables both; reselecting saved values also disables both |
| AC-09 | Changes to language only, theme only, and both (parameterized) | The user selects **Save preferences** | Only the changed endpoints are called (theme with `source` `preferences_page`); while saving "Saving…" is shown, controls are disabled and repeat clicks send nothing; on `200`s "Preferences saved" is shown in the active language, the page is pristine, local values are updated and the next session returns the new values |
| AC-10 | Both changed (`es`, `dark`) | Save fails for language only, theme only, or both (parameterized: `500`, network, `400`); separately one returns `401` | The failed preference returns to its saved value in UI and local storage while the successful one stays saved; the BR-12 language, appearance or preferences toast is shown, with no success toast; the `401` case then follows the expired-session flow |
| AC-11 | Preferences dirty | The user navigates away and chooses **Keep editing**, then navigates again and chooses **Discard**; separately signs out | Keep editing keeps the page and previews; Discard applies the saved theme and language, then navigates; sign-out shows no dialog |
| AC-12 | Preferences dirty with `es` and `dark` pending | The user chooses **Light** in the user menu and it saves | The page shows Light selected as the saved value; the language change stays pending; Save remains enabled for the language only |
| AC-13 | Each shell (main user menu, technician **More**, portal user menu; parameterized) at desktop and mobile widths | The user opens **Appearance** with the keyboard | The item follows **Language** and shows the active option; desktop opens a flyout and mobile an inline sublist with System, Light and Dark; the active one has a check, selected styling and `aria-checked="true"`; Escape/Left closes with focus on **Appearance**; choosing closes the menu with focus on the menu button |
| AC-14 | Each shell (parameterized) with a saved preference | The user chooses a different option, then the active one; separately the save fails (`500`, network, `400`, `401`) | The theme applies at once and the matching endpoint receives `source` `user_menu`, `technician_menu` or `portal_menu`; options are `aria-disabled` in flight; `200` updates the local value; choosing the active option sends nothing; failures restore the previous theme and local value with the appearance toast, and `401` then follows the expired-session flow |
| AC-15 | A signed-in internal user with `system` | `PUT /account/theme { theme: "dark", source: "user_menu" }` | `200 { preferredTheme: "dark" }` with `Cache-Control: no-store`; `preferred_theme` and `updated_at` change; one `user.theme_updated` row exists in the session organization with metadata exactly `{ previousTheme: "system", newTheme: "dark", source: "user_menu" }` and the client IP, and no device data; the next `GET /sessions/current` returns `dark` |
| AC-16 | A user whose stored theme is `dark` | The API receives `{ theme: "dark", source: "preferences_page" }` | `200 { preferredTheme: "dark" }`; `updated_at` unchanged; no audit row |
| AC-17 | Invalid bodies sent to both endpoints (parameterized: `theme` `Dark`, `" dark"`, `""`, `null`, `5`, `sepia`, missing; `source` missing, `unknown`, `portal_menu` on internal, `user_menu` on portal; both invalid; malformed JSON; non-JSON; >4 KB) | The request is processed | `400` with the BR-06 `theme` and/or `source` keys and messages, or keyless `400` / `415` / `413`; nothing stored; no audit |
| AC-18 | Users A (organization 1) and B (organization 2); no session | A sends a valid body plus `userId` = B and `organizationId` = 2; an anonymous client calls both endpoints; a portal session calls `/account/theme` and an internal session calls `/portal/account/theme` | Only A changes; B unchanged; the audit row exists only in organization 1; anonymous and wrong-session-type calls return `401` and change nothing |
| AC-19 | A portal user with two linked accounts in different organizations, current account in organization 2 | They choose **Dark** in the portal menu | `PUT /portal/account/theme` returns `200`; one `user.theme_updated` row with `source` `portal_menu` exists in organization 2 only; `GET /portal/sessions/current` and switching account return `preferredTheme` `dark`; every session body carrying `preferredLanguage` also carries `preferredTheme` |
| AC-20 | The generated migration and an existing database snapshot | The persistence diff is reviewed | `users.preferred_theme varchar(10) NOT NULL DEFAULT 'system'` is added without CHECK; existing and newly created users read `system`; SA-25 appears in the schema document; the migration is not applied |
| AC-21 | The current tree, then fixtures (parameterized: hex in a component SCSS, `rgb()` in a template `style`, named color in a component `.ts`, literal SVG `fill`, a token without a dark value, an allowlisted value) | `npm run theme:check` runs | The current tree passes; each non-allowlisted fixture fails with a non-zero exit naming file and line; the allowlisted value passes; the frontend CI workflow runs the command |
| AC-22 | Dark mode active, each row of the Theme coverage table (parameterized smoke, one representative page per surface, at 320, 768 and 1280px) | Each renders its loading, empty, error and success states and opens its menus, dialogs, drawers, popovers, tooltips, toasts and PrimeNG DatePicker, Select, Table and Paginator | No white backgrounds, light borders or dark text appear outside tokens; the navy sidebar stays dark; statuses keep meaning with text or icon; charts show readable axes, gridlines, legends and tooltips; uploaded images and logos are unchanged; light mode is visually unchanged from before the feature |
| AC-23 | Light and dark (parameterized) | Representative text, controls, statuses, charts and focus states are measured and the user navigates by keyboard | Contrast meets BR-19 in both themes; focus ring is visible on every control; selected, disabled, hover and error states are distinguishable without relying on color |

## Testing requirements

Parameterized cases count as one method. The frontend count exceeds the default
range because the feature is cross-cutting (pre-bootstrap resolution, live
system following, page draft/save logic, three menus and the check script are
distinct risks).

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Theme and per-endpoint source validation, only if it lives outside the endpoint pipeline. ≤1 method | AC-17 |
| Backend integration | Internal endpoint success with exact audit metadata, no-op without write/audit, invalid bodies; portal endpoint with current-account organization; session bodies include `preferredTheme`; ignored identifiers, anonymous and wrong-session-type `401`. ≤5 methods | AC-02, AC-15 to AC-19 |
| Authorization/tenant isolation | One representative test: identifiers of another user/organization ignored, audit only in the session organization; plus wrong-session-type `401`s and portal organization resolution | AC-18, AC-19 |
| Persistence review | `database-reviewer` on the SA-25 mapping and generated migration | AC-20 |
| Frontend service/component | Pre-bootstrap script resolution and fallbacks (pure function); session preference application and local value; system listener on/off; Preferences preview, reset, dirty/pristine, save of changed only, per-preference failure toasts, discard and menu sync; Appearance cards keyboard/ARIA; menu item in the three shells with source and failure restore. ≤8 methods | AC-01 to AC-03, AC-06 to AC-14 |
| Script | `theme:check` against the current tree and fixtures | AC-21 |
| User visual QA | No flash on reload in each theme, state preservation, design fidelity, menus at desktop/mobile, dark coverage smoke, contrast and focus | AC-01, AC-04, AC-05, AC-06, AC-13, AC-22, AC-23 |

## Dependencies

- `specs/account-settings/spec.md` (AUDITED): layout, discard dialog, route
  guard; OD-08 superseded only for Preferences and the Dark mode item; FR-02's
  two-item navigation and BR-09 subtitle are superseded by FR-05 / BR-09 here.
- `specs/app-i18n-en-es/spec.md` (AUDITED): language service, endpoints, local
  key, Language menu item pattern (BR-18), translation resources and
  `i18n:check`; all new copy is added in `en` and `es`.
- `specs/authenticated-app-shell/spec.md`, `specs/technician-todays-jobs/spec.md`,
  `specs/customer-portal-dashboard/spec.md`: shells and menus.
- `specs/sign-in/spec.md` and portal session specs: session bodies extended
  additively.
- `specs/audit-trail/spec.md`: audit writer and catalog; one new event.
- Every feature spec of the Theme coverage table: visuals themed, behavior
  unchanged.
- CI change authorized by the user 2026-10-21 (`theme:check` step).
- Implementation classification for `spec-impl`: Backend TARGETED (SA-25, two
  endpoints, session contracts, audit); Frontend FULL (pre-bootstrap script,
  theme service, Preferences page, three menus, global token/preset audit,
  every feature, CI step, `docs/frontend`). `--generate-migration` required.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | One shared theme service is used by the page, the three menus and the session services, mirroring the language service. |
| AS-02 | The shared session body type means `POST /sessions`, `POST /portal/sessions` and `POST /portal/sessions/current/account` gain `preferredTheme` alongside the GET endpoints, as with `preferredLanguage`. |
| AS-03 | The account layout subtitle is shared by all sections, so the approved design's subtitle replaces the previous one everywhere; no other My profile or Security copy changes. |
| AS-04 | The existing light values of the tokens stay as they are; dark values may be tuned to meet BR-19. |
| AS-05 | The BR-04 script is inline in `index.html` and allowlisted by `theme:check`; no CSP currently restricts inline scripts. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Approved design path (cited file missing) | Existing unified account image / add new file | Yes | `design/assets/Interfaz de configuración de cuenta unificada.png` (2026-10-21) |
| OD-02 | Audit `source` in the contract | Required body field / optional with default / header | Yes | Required `source`, validated per endpoint (2026-10-21) |
| OD-03 | Partial failure on Save preferences | Independent / revert both | Yes | Independent; restore only failed, specific toasts (2026-10-21) |
| OD-04 | Hardcoded color enforcement | `theme:check` + CI / one-time audit | Yes | `theme:check` with versioned allowlist in frontend CI (2026-10-21) |
| OD-05 | Selector on anonymous pages | None / footer selector | Yes | None; local value or device (2026-10-21) |
| OD-06 | Minor behaviors | Proposed | Yes | Local key kept on sign-out; `theme-color` `#0f9d9a`/`#0b1620`; navy sidebar dark in both; menu change updates Preferences saved state (2026-10-21) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02 |
| FR-03 | AC-03 |
| FR-04 | AC-04 |
| FR-05 | AC-05 |
| FR-06 | AC-06 |
| FR-07 | AC-08 |
| FR-08 | AC-09, AC-10 |
| FR-09 | AC-11, AC-12 |
| FR-10 | AC-13 |
| FR-11 | AC-14 |
| FR-12 | AC-15, AC-16, AC-17, AC-18, AC-19 |
| FR-13 | AC-02, AC-15, AC-19 |
| FR-14 | AC-20 |
| FR-15 | AC-22 |
| FR-16 | AC-21 |
| FR-17 | AC-07, AC-13, AC-23 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-21 | — → DRAFT     | Created |
| 2026-10-21 | DRAFT → APPROVED | Approved by user via /spec approve |
