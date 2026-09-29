# Organization onboarding wizard

| Field    | Value                              |
| -------- | ---------------------------------- |
| Feature  | `organization-onboarding-wizard`   |
| Type     | Frontend                           |
| Status   | APPROVED                           |
| Created  | 2026-09-29                         |
| Updated  | 2026-09-29                         |
| Approved | 2026-09-29                         |

## Context and objective

The public company registration at `/auth/register-company` is one long form.
This feature rebuilds it as the four-step wizard from the approved designs:
Company & branch → Business settings → Owner account → Review. The API
contract, fields, validation rules and atomic organization creation stay
exactly as they are. Wizard data exists only in browser memory and is sent in
one request when the visitor selects **Create organization**. Success means a
visitor can complete, review and correct every step with the keyboard, on any
width from 320px, and the backend receives the same request it receives today.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Anonymous visitor | Open `/auth/register-company`; move between steps; review; submit one registration | Supply any organization, branch, user or role identifier; save partial data on the server; obtain a session |
| System (frontend) | Hold wizard values in memory; validate steps; send one `POST` on Create | Send any request before Create; write wizard values to URL, web storage, cookies or IndexedDB |

Backend authorization is unchanged: the endpoint stays anonymous and pre-tenant
(`organization-onboarding`).

## Scope

- Replace the single-page form at `/auth/register-company` with the four-step
  wizard, keeping the route, lazy feature and public access.
- Side panel with brand, stepper, security note and Sign in link; step header;
  sticky action bar.
- Per-step validation using the existing field rules and messages.
- Back, stepper, Review **Edit** links and **Back to review** navigation.
- Business hours editor in 30-minute dropdowns with **Copy Monday to
  weekdays**.
- Live document numbering examples, password requirements and show-password
  checkbox.
- Review step summarizing every value, and single submission.
- Server error routing to the step containing the field.
- Leave confirmation when entered data would be lost.
- Responsive layout (including the compact header without a mockup),
  keyboard accessibility, validation and error states.

## Non-goals

- Any backend, contract, validation-rule, message, rate-limit, persistence or
  schema change.
- Autosave, drafts, resuming later, or partial backend records.
- Storing any wizard value in the URL, web storage, cookies or IndexedDB.
- Per-step URLs or browser history entries per step.
- New fields (logo, website, organization address, tax switch, sequences for
  quotes or work orders, terms consent).
- Changing the Company setup branch drawer or its business hours editor.
- Email verification, sign-in or session creation after registration.
- Agent-run browser, screenshot, pixel or axe checks (visual QA is user-owned).

## User flow

1. Visitor opens `/auth/register-company`; step 1 renders with defaults.
2. Visitor fills step 1 and selects **Continue**. Invalid: the step stays,
   shows field errors and an error summary, and focuses the first invalid
   field. Valid: step 2 opens and step 1 is marked completed.
3. The same applies to steps 2 and 3. **Back** returns to the previous step
   without validating.
4. Step 4 (Review) shows every value. **Edit** opens a step whose primary
   action becomes **Back to review**.
5. Visitor selects **Create organization**. The frontend validates all steps,
   sends one `POST /organization-registrations` and shows the submitting state.
6. `201`: navigate to `/auth/sign-in?registered=true`.
7. Error: route per BR-10 and keep every value, including passwords.
8. Leaving the page with changed data asks for confirmation (FR-12).

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve the wizard at `/auth/register-company` in place of the single-page form, public and lazy loaded from the existing organizations feature. The page makes no HTTP request before **Create organization**. |
| FR-02 | The wizard must show the BR-02 steps with the BR-03 layout and copy: side panel (brand, heading, stepper, security note, Sign in link), step header (eyebrow "STEP {n} OF 4", title, description), step content and action bar. |
| FR-03 | **Continue** (and **Back to review**) must validate only the current step's BR-02 fields with the unchanged `organization-onboarding` rules and messages (BR-01). Invalid: stay, show each field's message and a step error summary with links, focus the first invalid field; after that attempt, fields revalidate on blur. Valid: advance and mark the step completed. |
| FR-04 | Navigation: **Back** (steps 2–4) returns one step without validating and keeps all values. In the stepper, every step already reached (current, completed, or opened earlier and left with Back) is selectable without validation; steps not yet reached are not. Once Review has been reached, any step opened by **Edit**, the stepper or error routing shows **Back to review** instead of **Continue**. Review **Edit** links open: Company profile and First branch → step 1; Business settings → step 2; Owner account → step 3. |
| FR-05 | Business hours (step 2) must show one row per day with an open switch ("Open"/"Closed") and start/end dropdowns using the BR-04 options; a closed day's dropdowns are disabled. **Copy Monday to weekdays** copies Monday's open state, start and end to Tuesday–Friday and leaves Saturday and Sunday unchanged. The request shape stays BR-15 of `organization-onboarding`. |
| FR-06 | Document numbering must show the BR-06 examples line, updated as prefixes and next invoice number change. |
| FR-07 | Step 3 must show a **Show password** checkbox that toggles visibility of both password fields, and a Password requirements list updated as the visitor types (BR-07). |
| FR-08 | Review must show the "Everything is ready" banner and four summary cards with every submitted value formatted per BR-05 and BR-09, each with an **Edit** link. |
| FR-09 | Wizard values must exist only in memory (BR-11). **Create organization** validates all steps (the earliest invalid step opens as in FR-03) and, when valid and not rate-locked, sends exactly one `POST /organization-registrations` with the unchanged contract body. |
| FR-10 | While submitting, **Create organization** shows a spinner and "Creating organization…", and Create, Back, Edit and the stepper are disabled; further activations send nothing. On `201` the frontend navigates to `/auth/sign-in?registered=true` (replacing history) without the leave confirmation. |
| FR-11 | Error responses must be handled per BR-10 and Error behavior, keeping every value including both password fields. After `429`, Create stays disabled for `Retry-After` seconds (60 if absent). |
| FR-12 | When any wizard value differs from its initial value (BR-08), leaving the route in the app (Sign in link, browser Back, any in-app navigation except success) must show the "Discard setup?" confirmation, and reloading or closing the tab must trigger the browser's leave prompt. Unchanged wizards leave without prompts. |
| FR-13 | The layout must follow the Responsive rules in UI behavior. |
| FR-14 | The wizard must meet the Accessibility rules in UI behavior. |
| FR-15 | Desktop presentation must match the approved designs, with only the approved deviations listed in UI behavior. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Field keys, defaults, limits, formats, message catalog, message selection, the validation messages table, company→branch time zone follow and server rules are exactly `organization-onboarding` BR-01 to BR-25, FR-13 and FR-14. The owner password stays 12–128 characters and not equal to the owner email ignoring case. Business hours defaults stay Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed. | Both (unchanged) |
| BR-02 | Steps and fields. **1 Company & branch** ("Company & branch"): `organization.{name, legalName, email, phone, taxId, timezone}`; `branch.{name, code, phone, email, timezone, addressLine1, city, stateRegion, postalCode, countryCode}`. **2 Business settings**: `branch.businessHours` (all keys under it); `organization.{currency, defaultTaxRate, quotePrefix, workOrderPrefix, invoicePrefix, nextInvoiceNumber}`. **3 Owner account**: `owner.{firstName, lastName, email, phone, password}`, `confirmPassword`. **4 Review**: no fields. | Frontend |
| BR-03 | Copy (from the designs unless marked *approved deviation*). Side panel: "FieldOps"; "Set up your workspace"; "Get your organization up and running in a few simple steps."; step labels "Company & branch", "Business settings", "Owner account", "Review"; "Your information is secure and encrypted."; "Already have an account?" + link "Sign in" (`/auth/sign-in`). Step titles/descriptions: 1 "Create your organization" / "Tell us about your company and first location. You can change these details later."; 2 "Business settings" / "Set your operating hours, currency, taxes, and document numbering."; 3 "Owner account" / "Create the account you'll use to manage this organization."; 4 "Review and create" / "Confirm your details before creating your FieldOps workspace.". Sections: "Company profile" / "This information identifies your company across FieldOps, invoices, and reports."; "First branch" / "Add your first operating location. You can add more branches later." + note "This will be your default operating location. You can add more branches after setup is complete."; group "Branch address"; "Business hours" / "Set the days and times your work is available. These hours will be shown on quotes and customer communications."; "Taxes & currency" / "Choose your currency and default tax rate. These settings will be used on new quotes and invoices." + note "The selected currency will be used on new quotes and invoices. You can change this later in your settings."; "Document numbering" / "Set the prefixes and starting number for your documents. This helps keep your records organized and consistent."; "Your account" / "This user will receive the Owner role and have full access to manage your organization in FieldOps."; "Password requirements", "12–128 characters", "Must not match your email"; note "You'll be the first Owner and can invite your team after setup."; "Your password is securely hashed and never stored as plain text.". Labels: Display name, Legal business name, Business email, Business phone, Tax ID, Company time zone, Branch name, Branch code (helper "Used in document numbering and reports."), Branch phone, Branch email, Branch time zone, Address line 1, City, State / Region, Postal code, Country, Currency, Default tax rate, Quote prefix, Work order prefix, Invoice prefix, Next invoice number, First name, Last name, Email, Phone "(optional)", Password, Confirm password, Show password. Actions: steps 1–3 "Continue" (*approved deviation* from "Save and continue"), "Back", "Back to review", "Copy Monday to weekdays", "Create organization" + "This may take a few seconds.", "Edit". Action-bar note: steps 1–3 "You can review everything before creating." (*approved deviation* from "Your progress is saved securely."); step 4 "By creating this organization, you confirm that the information is correct.". Review: "Everything is ready" / "Your organization is configured and ready to be created. You can change these settings later."; cards "Company profile" / "Your company information and location details.", "First branch" / "Your primary operating location.", "Business settings" / "Your financial and operational preferences.", "Owner account" / "Your account information and role."; tag "Owner". Leave dialog: "Discard setup?" / "Your organization hasn't been created. The details you entered will be lost." / "Discard" / "Keep editing". | Frontend |
| BR-04 | Time options: 48 values `00:00` to `23:30` in 30-minute steps, displayed in 12-hour form ("12:00 AM", "8:00 AM", "12:30 PM", "11:30 PM") and submitted as `HH:mm`. The default hours are among the options. | Frontend |
| BR-05 | Business hours summary: days in Monday→Sunday order abbreviated Mon, Tue, Wed, Thu, Fri, Sat, Sun; consecutive open days with identical start and end form one group, shown as "Mon – Fri" (single day "Sat"); each group reads "{days}, {start} – {end}" with BR-04 display times; groups are joined with " · "; closed days are omitted; the line ends with " ({branch time zone generic name})". All days closed → "Closed all week" (no time zone suffix). Example for defaults in `America/Chicago`: "Mon – Fri, 8:00 AM – 5:00 PM · Sat, 9:00 AM – 1:00 PM (Central Time)". | Frontend |
| BR-06 | Examples line: "Examples: {quotePrefix}-1 · {workOrderPrefix}-1 · {invoicePrefix}-{nextInvoiceNumber}" using the trimmed, uppercased prefixes and the next invoice number without digit grouping (quote and work order sequences start at 1 by schema default). An empty prefix or an invalid next invoice number shows "—" in its place. | Frontend |
| BR-07 | Password requirements: "12–128 characters" is met when the password length is 12–128; "Must not match your email" is met when the password is non-empty and does not fail the BR-01 password-versus-email rule (same comparison as the validator, so the list and the message never disagree). Both are unmet while the password is empty. Each item shows a met/unmet icon and an accessible state text, never color alone. The list is guidance only; validation messages remain BR-01. | Frontend |
| BR-08 | A wizard is changed when any field value, business hours value or `confirmPassword` differs from its initial value (defaults included). The Show password checkbox and the current step do not count. | Frontend |
| BR-09 | Review formatting: time zones use the existing "(UTC±hh:mm) {generic name} — {IANA id}" label; currency "{code} — {display name}"; tax rate "{value}%"; branch address on three lines: address line 1; "{City}, {State / Region} {Postal code}" (without state: "{City} {Postal code}"); country display name; owner "{First name} {Last name}", email, initials from the first letters of first and last name uppercased, and the "Owner" tag; empty optional values (Tax ID, branch phone, branch email, owner phone) show "Not provided". Company profile rows: Display name, Legal business name, Business email, Business phone, Tax ID, Company time zone. First branch rows: Branch name, Branch code, Address, Branch phone, Branch email, Branch time zone, Business hours (BR-05). Business settings rows: Default currency, Default tax rate, Quote prefix, Work order prefix, Invoice prefix, Next invoice number (*approved deviation*: added). Owner card: name, email, tag, Phone. Passwords are never shown. | Frontend |
| BR-10 | Server error routing: `400` whose field keys map to wizard fields through the existing server field-error mapping and `409` open the earliest BR-02 step containing a mapped key, show those messages on their fields and in that step's error summary, and focus the first invalid field. Mapped keys in later steps stay attached to their fields until revalidated. `429`, `413`, `415`, `500`, network failure and `400` without mappable keys stay on Review and show `ApiError.message` in the Review error summary, which receives focus. | Frontend |
| BR-11 | Wizard values, including both password fields, live only in component memory for the current page instance. They are kept across step changes and error responses, and discarded on successful navigation, confirmed discard, reload or tab close. They are never written to the URL, web storage, cookies, IndexedDB or logs. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Step n (1–3) | Step n+1 (or Review when Review was reached) | Continue / Back to review | Visitor | Step n valid (FR-03) |
| Step n (2–4) | Step n−1 | Back | Visitor | Not submitting |
| Any step | Already reached step k | Stepper | Visitor | Step k already reached (FR-04); not submitting |
| Review | Step 1, 2 or 3 | Edit | Visitor | Not submitting |
| Review | Submitting | Create organization | Visitor | All steps valid; not rate-locked |
| Submitting | Sign in (`?registered=true`) | `201` | System | — |
| Submitting | Earliest step with a mapped key | `400` with keys, `409` | System | BR-10 |
| Submitting | Review (error summary) | Other errors | System | BR-10 |
| Wizard (changed) | Leave dialog | In-app navigation away | Visitor | BR-08; not success |

## Data and persistence impact

- Tables and columns used: none directly. Persistence is unchanged and remains
  as defined by `organization-onboarding` FR-04 and
  `design-17-company-setup-completion` FR-18 (first branch is main).
- Schema amendments required: None.

## Tenant isolation and authorization

- Organization context: none; the workflow stays pre-tenant and anonymous.
  The server generates every identifier (`organization-onboarding` FR-05).
- Client-provided organization identifiers: never sent. The request body is
  built only from BR-02 fields; the server ignores unknown properties.
- Other organizations: unaffected; this feature adds no endpoint or response.
- Permission: none. Frontend-only change; backend authorization unchanged.

## API contracts

Contract status: Final (unchanged; listed for reference).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/organization-registrations` | Unchanged `organization-onboarding` BR-01 body | `201` `{ "organizationId": "{uuid}" }` | Unchanged: `400`, `409`, `413`, `415`, `429` + `Retry-After`, `500` | Anonymous |

## UI behavior and states

Design references (approved by the user; rebuilt with Angular, PrimeNG and
semantic HTML, never copied; sample values are not reproduced):

- `design/assets/desing-1.png` — step 1 Company & branch
- `design/assets/desing-2.png` — step 2 Business settings
- `design/assets/desing-3.png` — step 3 Owner account
- `design/assets/desing-4.png` — step 4 Review

Approved deviations from the designs: "Continue" instead of "Save and
continue"; steps 1–3 action-bar note "You can review everything before
creating." without the saved-progress check; "Back to review" after Review is
reached; "Next invoice number" row on Review; "Not provided" for empty
optional values; live examples line (BR-06); leave dialog; compact header
below 64rem (no mockup).

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Steps 1–3 | N/A (no request; renders at once) | N/A (form with BR-01 defaults) | Field messages + step error summary (`role="alert"`) with links to fields; server field errors routed per BR-10 | N/A: public page | Next step, previous step marked completed |
| Review | N/A | N/A | Error summary with `ApiError.message`; `429` lock on Create | N/A | Submitting (spinner, "Creating organization…", controls disabled) → Sign in |
| Leave dialog | N/A | N/A | N/A | N/A | Discard → navigation continues; Keep editing → stays with values |

- Styling: existing `--fo-*` tokens (with their dark values), Inter and
  PrimeIcons from design 17; navy side panel; cards with icon badge, title and
  description as in the designs. No handoff hex values in component styles.
- Responsive (breakpoints `md` = 48rem, `lg` = 64rem):
  - From `lg`: fixed navy side panel on the left as in the designs; content
    column scrolls; the action bar stays visible at the bottom of the content.
    Step 2 shows Business hours beside a column with Taxes & currency above
    Document numbering. Review shows the cards in a 2×2 grid.
  - Below `lg`: the side panel becomes a compact top header with the brand,
    "Step {n} of 4 · {step label}" and a 4-segment progress indicator; the
    security note and Sign in link move below the step content. Step 2 cards
    and Review cards stack in one column.
  - From `md`: step 1 and step 3 fields use 2 columns; Country and the note
    span the full width; the three prefixes share one row.
  - Below `md`: every field is one column; business hours rows show the day
    and switch on one line and the two dropdowns on the next; action-bar
    buttons are full width and the bar is sticky with safe-area padding.
  - No horizontal scroll at 320px; touch targets ≥44px below `md`.
- Accessibility:
  - The stepper is a labelled navigation list ("Setup steps"); the current
    step has `aria-current="step"`; completed steps expose "completed" in
    their accessible name; steps not yet reached are not focusable as actions.
  - On step change, focus moves to the new step title (`h1`).
  - Each card section is labelled by its heading; required fields have `*`
    and `aria-required="true"`; invalid fields have `aria-invalid="true"` and
    `aria-describedby` pointing at their message; the address group has one
    visible label.
  - Day switches are named "{Day} open"; dropdowns "{Day} start time" and
    "{Day} end time".
  - The password fields reference the requirements list through
    `aria-describedby`; requirement state is conveyed by text and icon.
  - Edit links are named "Edit {card title}".
  - Enter in a field of steps 1–3 triggers Continue/Back to review; on Review
    it triggers Create organization.
  - The leave dialog is modal, traps focus, starts on "Keep editing" and
    returns focus to the invoking control on Keep editing.
  - Visible focus indicator on every control; color is never the only state
    indicator.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Step invalid on Continue / Back to review / Create | BR-01 field messages and step error summary; focus first invalid; no request | None |
| `400` with mapped field keys | Earliest step with a key opens; field messages and summary; values kept (BR-10) | None |
| `409` duplicate email | Step 3; Email shows "An account with this email already exists. Sign in instead."; values kept | None |
| `429` | Review summary shows `ApiError.message`; Create disabled for `Retry-After` seconds (60 if absent) | None |
| `400` without mappable keys, `413`, `415`, `500` | Review summary shows `ApiError.message`; controls re-enabled; values kept | None |
| Network failure (status 0) | Review summary shows `ApiError.message`; values kept | None, or complete creation if the server committed; a retry then gets `409` |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An anonymous visitor | They open `/auth/register-company` | Step 1 renders with the BR-03 side panel, "STEP 1 OF 4", the step 1 BR-02 fields with BR-01 defaults, "Continue" and the steps 1–3 note, no Back; the stepper marks step 1 current and steps 2–4 not selectable; no HTTP request is sent |
| AC-02 | Steps 1, 2 and 3 each with one invalid field (parameterized) | The visitor selects Continue | The step does not change; only that step's fields are validated with their BR-01 messages; the step error summary appears; focus is on the first invalid field; no request is sent; after correcting and blurring the field its message clears |
| AC-03 | A valid step 1, then a valid step 2 | The visitor selects Continue each time | The next step opens with "STEP {n} OF 4", its title receives focus, and the previous step shows as completed and becomes selectable |
| AC-04 | A visitor on step 3 with all fields entered | They select Back, then select step 3 in the stepper, and try to select step 4 before Review was reached | Back opens step 2 without validation; step 3 reopens with every value including both passwords; step 4 cannot be activated by pointer or keyboard |
| AC-05 | Step 1 with an untouched branch time zone, and separately an edited one (parameterized) | The company time zone changes | Untouched: the branch time zone follows; edited: it keeps its value |
| AC-06 | Step 2 | The visitor opens a start dropdown, picks "8:30 AM", closes Sunday and sets Monday end before start, then selects Continue | The dropdown lists the 48 BR-04 options; the stored value is `08:30`; Sunday shows "Closed" with disabled dropdowns; Monday end shows "End time must be after start time." |
| AC-07 | Monday open 07:30–16:00, Tuesday closed, Saturday 10:00–14:00; and separately Monday closed (parameterized) | The visitor selects Copy Monday to weekdays | Tuesday–Friday become open 07:30–16:00 (or all closed in the second case); Saturday and Sunday are unchanged |
| AC-08 | Prefixes `q`, `WO`, empty invoice prefix, next invoice number 1049 | The visitor edits the values | The examples line reads "Examples: Q-1 · WO-1 · —-1049" and updates after each change, per BR-06 |
| AC-09 | Owner email `owner@acme.io` and passwords: empty, 11 characters, `OWNER@ACME.IO`, a different 12-character password (parameterized) | The requirements list renders and Continue is selected | The two requirement states match BR-07 for each case; validation messages match BR-01 (for `OWNER@ACME.IO`: only "Choose a password that is different from your email."); Show password switches both fields between hidden and visible |
| AC-10 | Hours sets: defaults in `America/Chicago`; Mon–Sun identical; Mon and Wed only; all closed (parameterized) | The Review summary is built | Results, all in `America/Chicago`: "Mon – Fri, 8:00 AM – 5:00 PM · Sat, 9:00 AM – 1:00 PM (Central Time)"; every day 8:00–17:00 → "Mon – Sun, 8:00 AM – 5:00 PM (Central Time)"; Monday and Wednesday 9:00–13:00 → "Mon, 9:00 AM – 1:00 PM · Wed, 9:00 AM – 1:00 PM (Central Time)"; all closed → "Closed all week" |
| AC-11 | A wizard with all values valid, Tax ID and branch phone empty | Review renders | "Everything is ready" and the four cards show every BR-09 row with BR-09 formatting, "Not provided" for empty optional values, owner initials and the "Owner" tag, and no password |
| AC-12 | Review reached | The visitor selects Edit on Business settings, makes a field invalid and selects Back to review, then fixes it and selects Back to review again | Step 2 opens with "Back to review" instead of Continue; the invalid attempt stays on step 2 with errors; the valid attempt returns to Review showing the new value |
| AC-13 | A visitor who moved through all steps | Network traffic is inspected before Create, and web storage, cookies and the URL are checked | No request was sent before Create; no wizard value appears in the URL, `localStorage`, `sessionStorage` or cookies; reloading the page shows step 1 with defaults |
| AC-14 | A valid wizard on Review | The visitor selects Create organization twice quickly | Exactly one `POST /organization-registrations` is sent with a body equal to the existing request builder's output for the same values (BR-01 keys, closed days omitted, no `confirmPassword`, no identifiers); the submitting state shows the spinner and "Creating organization…" with Create, Back, Edit and stepper disabled |
| AC-15 | A `201` response | The frontend handles it | The browser navigates to `/auth/sign-in?registered=true` replacing history, with no leave dialog |
| AC-16 | Responses: `400` with `organization.name` and `owner.email`; `400` with only `branch.businessHours.monday.end`; `409` on `owner.email` (parameterized) | The frontend handles each | Steps 1, 2 and 3 open respectively; the mapped messages show on their fields and in the summary; focus is on the first invalid field; every value including both passwords is kept |
| AC-17 | Responses: `429` with `Retry-After: 30`; `500`; network failure; `400` without mappable keys (parameterized) | The frontend handles each | The Review stays open; the error summary shows `ApiError.message` and receives focus; values including passwords are kept; after `429` Create is disabled for 30 seconds, then enabled; otherwise it is enabled at once |
| AC-18 | A changed wizard, and separately an unchanged one (parameterized) | The visitor selects Sign in, then Keep editing, then Sign in and Discard; and reloads | Changed: the "Discard setup?" dialog appears, Keep editing keeps the step and values, Discard navigates to `/auth/sign-in`, and reload triggers the browser leave prompt; unchanged: navigation and reload happen without prompts |
| AC-19 | Viewports 1440, 800, 390 and 320px (user visual QA) | Each step renders | Layout follows the Responsive rules: side panel from 1024px, compact header with "Step {n} of 4 · {label}" and progress indicator below; stacking and column counts as specified; no horizontal scroll at 320px; touch targets ≥44px below 768px |
| AC-20 | Keyboard-only use and a screen reader (user visual QA) | The visitor completes all steps, uses Edit, the stepper, the dialog and Create | Every control is reachable in visual order with visible focus; Enter continues/creates; focus moves to each new step title; stepper, switches, dropdowns, requirements and Edit links expose the names and states in Accessibility; color is never the only indicator |
| AC-21 | The implementation at 1440px beside each approved design (user visual QA) | Steps 1–4 are compared | Structure, order, copy, icons, card treatment, stepper states and action bar match the designs; differences are limited to the approved deviations and entered data |

## Testing requirements

21 active ACs. No backend change: backend evidence from
`organization-onboarding` is reused (contract, validation, atomicity, rate
limits, tenant isolation) and not recreated.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit / integration | N/A: contract, validation and persistence unchanged | — |
| Authorization/tenant isolation | N/A: no new endpoint or authorization path; the unchanged request builder sends no identifiers (checked in frontend group 2) | AC-14 |
| Frontend component/service | (1) Step navigation and per-step validation: parameterized invalid Continue per step, advance/completed state, Back keeps values, stepper selectability, Edit and Back to review, time zone follow. (2) Submission: no request before Create and no storage/URL writes, single `POST` body equal to the request builder, submitting lock, `201` navigation without dialog, parameterized server error routing and value retention incl. `429` lock. (3) Leave guard: changed/unchanged, Keep editing/Discard, `beforeunload` registration. (4) Pure helpers, parameterized: BR-04 time options, Copy Monday, BR-05 summary, BR-06 examples, BR-07 requirement states. (5) Review rendering (BR-09) and show-password toggle. 5 methods | AC-01 to AC-18 |
| User visual QA | Responsive layouts, keyboard/screen reader pass and design comparison on the current frontend fingerprint | AC-19, AC-20, AC-21 |

## Dependencies

- `specs/organization-onboarding/spec.md`: field rules, messages, contract,
  error contract and backend behavior (implemented). This spec supersedes its
  FR-01 (single page), FR-02 (whole-form validation, now per step), FR-15
  (time inputs, now BR-04 dropdowns in the wizard), FR-16 and the UI behavior
  section, AS-04 (page title), and the user-flow rule that clears password
  fields on error (now BR-11).
- `specs/design-17-company-setup-completion/spec.md`: `--fo-*` tokens, Inter,
  PrimeIcons, time zone label helper and first-branch-is-main (implemented).
- `specs/sign-in/spec.md`: `/auth/sign-in?registered=true` (implemented).
- Implementation classification for `spec-impl`: Frontend only; no backend
  area.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The wizard keeps one reactive form for all steps and reuses the existing validators, request builder, server-error mapper and error summary. |
| AS-02 | The Company setup branch drawer keeps the existing business hours editor unchanged; the wizard's dropdown presentation does not alter it. |
| AS-03 | The leave confirmation uses a route `canDeactivate` guard with a PrimeNG confirm dialog plus a `beforeunload` listener active only while changed. |
| AS-04 | Time dropdowns, the currency and country selects and the time zone selects use PrimeNG Select; the switch uses PrimeNG ToggleSwitch; the checkbox uses PrimeNG Checkbox. |
| AS-05 | `frontend/CLAUDE.md` "primeicons is not installed" is stale (installed by design 17); correcting it is outside this spec. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Spec type | Frontend / Full-stack | Yes | Frontend; backend unchanged, existing evidence reused (2026-09-29) |
| OD-02 | "Progress saved" copy | Replace / keep | Yes | "Continue"; steps 1–3 note "You can review everything before creating."; Review keeps its confirmation note (2026-09-29) |
| OD-03 | Leaving with data | Confirm / none | Yes | Browser prompt and "Discard setup?" dialog when changed (2026-09-29) |
| OD-04 | Step URLs | One route / per-step query | Yes | One route; step in memory; reload restarts (2026-09-29) |
| OD-05 | Step navigation | Per-step validation with Back to review / strictly sequential | Yes | Per-step validation, completed steps selectable, Edit → Back to review (2026-09-29) |
| OD-06 | Server error placement | Route to step / Review only | Yes | Route to earliest step with a field error; others on Review (2026-09-29) |
| OD-07 | Passwords after errors | Keep in memory / clear | Yes | Keep until success or leaving (2026-09-29) |
| OD-08 | Time controls | 30-min dropdowns / 15-min / native | Yes | 30-minute dropdowns, wizard only (2026-09-29) |
| OD-09 | Copy Monday | Tue–Fri incl. closed / only when open | Yes | Copies open state and times to Tue–Fri (2026-09-29) |
| OD-10 | Review details | Grouped hours + invoice number + "Not provided" / mockup only | Yes | Grouped summary, added Next invoice number, "Not provided" (2026-09-29) |
| OD-11 | Numbering examples | Live real numbers / static / live with 1049 | Yes | Live with real next numbers (2026-09-29) |
| OD-12 | Mobile/tablet layout | Compact header / ui-designer | Yes | Compact header below 1024px, stacked cards, sticky full-width actions below 768px (2026-09-29) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-13 |
| FR-02 | AC-01, AC-03, AC-21 |
| FR-03 | AC-02, AC-03, AC-05, AC-12 |
| FR-04 | AC-04, AC-12 |
| FR-05 | AC-06, AC-07 |
| FR-06 | AC-08 |
| FR-07 | AC-09 |
| FR-08 | AC-10, AC-11 |
| FR-09 | AC-13, AC-14 |
| FR-10 | AC-14, AC-15 |
| FR-11 | AC-16, AC-17 |
| FR-12 | AC-18 |
| FR-13 | AC-19 |
| FR-14 | AC-20 |
| FR-15 | AC-21 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-29 | — → DRAFT     | Created; decisions OD-01 to OD-12 answered by the user |
| 2026-09-29 | DRAFT → DRAFT | Validation fixes: FR-04 stepper selects any already-reached step (aligns with AC-04; States and Accessibility updated); AC-10 exact strings; BR-07 reuses the BR-01 password-versus-email comparison; BR-10 no longer names a code function |
| 2026-09-29 | DRAFT → APPROVED | Approved by user via /spec approve |
