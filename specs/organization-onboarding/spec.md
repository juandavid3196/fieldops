# Organization onboarding

| Field    | Value                     |
| -------- | ------------------------- |
| Feature  | `organization-onboarding` |
| Type     | Full-stack                |
| Status   | APPROVED                  |
| Created  | 2026-09-24                |
| Updated  | 2026-09-24                |
| Approved | 2026-09-24                |

## Context and objective

FieldOps has no way to create a tenant. This feature adds a public, pre-tenant
registration: in a single submit, an anonymous visitor creates an
organization, its first branch and the initial Owner account. On success the
visitor is sent to Sign In. No session is created. Success means one
all-or-nothing operation that leaves either a complete, usable tenant or no data.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Anonymous visitor | Open `/auth/register-company`; submit one registration that creates a new organization, first branch and Owner account | Choose or supply any organization, branch, user or role identifier; join or modify an existing organization; obtain a session or token |
| System | Generate every identifier; assign the seeded `owner` role; write the audit row | Accept client-supplied identifiers |

The endpoint is anonymous by design. After registration, all authorization
belongs to the future authentication spec.

## Scope

- Public registration page based on approved design 17 (Company Setup), adapted
  to a single public form (see UI behavior).
- One public API endpoint that validates the request and atomically creates the
  organization, first branch, Owner user, organization membership, membership
  branch link and audit row.
- Server-side identifier generation and tenant creation.
- Duplicate email handling.
- Password policy and PBKDF2 password hashing.
- Rate limiting and request-size limits for the endpoint.
- The validation error contract for this form (the first form spec, per
  `docs/frontend/frontend-configuration.md`), including `ApiErrorService`
  changes.
- Loading, validation, submitting, error and success UI states; accessibility
  and responsive behavior.

## Non-goals

- Login, Sign In page, JWT, cookies, refresh tokens or any session.
- Email verification and password recovery.
- Invitations; user and permission administration.
- Additional branches; branch list, edit or deactivation.
- Editing the organization after registration (the authenticated Company
  Setup screen).
- Subscription or billing.
- Company logo, website, prices-include-tax, service ZIP codes,
  use-company-billing and the "Main branch" flag (no schema columns).
- Editable next quote number, next work order number and
  `require_customer_signature` (schema defaults apply).
- Terms-of-service consent (no schema column).
- Captcha or other external anti-bot services.
- Configuring forwarded headers or proxy trust (deferred to deployment).
- Uniqueness of organization name or tax ID.

## User flow

1. Visitor opens `/auth/register-company`.
2. System shows the form with its sections: Company profile, First branch,
   Taxes & currency, Document numbering, Owner account. Fields start empty or
   with the defaults in BR-02.
3. Visitor fills in the form and selects **Create organization**.
4. System validates in the browser. Invalid: an error summary appears, each
   invalid field shows its message and focus moves to the first invalid field.
   Nothing is sent.
5. System sends one `POST /organization-registrations`, locks the form and shows
   the submitting state.
6. Backend validates, normalizes and creates every record in one transaction.
7. System navigates to `/auth/sign-in?registered=true`.
8. On an error response, the form unlocks, keeps every entered value except
   the password fields, and shows the error defined in Error behavior.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The system must serve a public page at `/auth/register-company`, lazy loaded from `features/organizations`, with no authentication. It shows the sections in this order: Company profile, First branch, Taxes & currency, Document numbering, Owner account, and one submit action. |
| FR-02 | The frontend must validate every field against BR-03 to BR-18. Validation runs on submit, and on blur after the first submit attempt. Each invalid field shows exactly one message, chosen by BR-25; an error summary appears; focus moves to the first invalid field. Invalid forms are never sent. |
| FR-03 | The backend must expose anonymous `POST /organization-registrations`, accepting only `application/json`. It normalizes and validates the request against BR-03 to BR-17 and, when the request is invalid, returns `400` ValidationProblemDetails with the BR-25 keys and messages. Other content types return `415`; malformed JSON returns `400` without field keys. BR-19 is handled by FR-06. |
| FR-04 | On a valid request, the backend must create in one transaction: one `organizations` row, one `branches` row, one `users` row, one `organization_users` row (role `owner`, status `active`, `is_all_branches = true`, `joined_at` = now), one `organization_user_branches` row linking that membership to the branch, and one `audit_logs` row (BR-22). |
| FR-05 | The backend must generate every identifier server-side and resolve the new `OrganizationId` only from the created organization. Any identifier properties in the request, including `organizationId`, must be ignored. |
| FR-06 | When the normalized owner email already exists in `users`, the backend must return `409` with a field error on `owner.email` and create no data. This includes a concurrent request that loses the unique-constraint race. |
| FR-07 | The backend must store only a PBKDF2 hash of the password (BR-20). The plain password must never be stored, logged or returned. |
| FR-08 | The endpoint must enforce BR-21: `429` with `Retry-After` when a rate limit is exceeded, and `413` for bodies larger than 32 KB, with no data change in either case. |
| FR-09 | If any step fails after validation (including a missing `owner` role or a database error), the backend must roll back so that no organization, branch, user, membership, membership-branch link or audit row remains, and return the Error behavior response. |
| FR-10 | On success the backend must return `201` with body `{ "organizationId": "{uuid}" }` and set no cookie or authentication header. The frontend must then navigate to `/auth/sign-in?registered=true`. |
| FR-11 | While a request is in flight, the frontend must show the submitting state, lock all inputs and ignore further submits so that exactly one request is sent per attempt. |
| FR-12 | The frontend must show each error response as defined in Error behavior. After `429` the submit action stays disabled for the `Retry-After` seconds (60 if absent). |
| FR-13 | `ApiErrorService` must keep field errors for `409` as well as `400`/`422`. It keeps only messages from the BR-24 catalog and replaces any other message with "Enter a valid value." The form maps dotted keys to its fields. |
| FR-14 | The First branch time zone must be prefilled from the company time zone, and follow it until the visitor changes the branch time zone. |
| FR-15 | The First branch section must include a 7-day business hours editor (open toggle plus start and end time per day) that produces the BR-15 shape. |
| FR-16 | The page must meet the accessibility and responsive rules in UI behavior. |

## Business and validation rules

All text fields are trimmed; a whitespace-only value counts as empty. Server
validation is authoritative; the client mirrors it.

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Request keys (dotted paths used in errors): `organization.{name, legalName, taxId, email, phone, timezone, currency, defaultTaxRate, quotePrefix, workOrderPrefix, invoicePrefix, nextInvoiceNumber}`, `branch.{name, code, phone, email, timezone, addressLine1, city, stateRegion, postalCode, countryCode, businessHours}`, `owner.{firstName, lastName, email, password, phone}`. Business hours errors use `branch.businessHours.{day}.start` / `.end` for a day's times and `branch.businessHours` for the object as a whole. `confirmPassword` exists only in the browser. | Both |
| BR-02 | Client defaults: currency `USD`; default tax rate `0`; quote prefix `Q`, work order prefix `WO`, invoice prefix `INV`; next invoice number `1`; company time zone = browser time zone (`Intl.DateTimeFormat().resolvedOptions().timeZone`) if it is in the client's time zone list (`Intl.supportedValuesOf('timeZone')`), otherwise empty; business hours Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed. | Frontend |
| BR-03 | `organization.name` (label "Display name"): required, ≤160 characters → `organizations.name`. | Both |
| BR-04 | `organization.legalName` (label "Legal business name"): required, ≤200 → `organizations.legal_name`. | Both |
| BR-05 | `organization.taxId` (label "Tax ID"): optional, ≤60 → `organizations.tax_id`. | Both |
| BR-06 | Emails (`organization.email` required, `branch.email` optional, `owner.email` required): ≤254 characters, one `@` with a non-empty local part and a domain containing a dot, no whitespace. All are stored lowercased. | Both |
| BR-07 | Phones (`organization.phone` required, `branch.phone` optional, `owner.phone` optional): ≤40 characters; only digits, spaces and `+ ( ) - .`; at least 7 digits. | Both |
| BR-08 | Time zones (`organization.timezone`, `branch.timezone`, both required, ≤80): an IANA identifier (for example `America/Chicago`) that the server resolves with `TimeZoneInfo`. Windows identifiers are rejected. | Both |
| BR-09 | `organization.currency`: required, and a member of the supported currency list: the ISO 4217 List One alphabetic codes for legal-tender currencies, excluding fund codes and the precious-metal, special and test codes (`XAU`, `XAG`, `XPT`, `XPD`, `XDR`, `XSU`, `XUA`, `XBA`–`XBD`, `XTS`, `XXX`). The frontend and backend each hold an identical static copy of this list. The client offers only codes from this list, with display names from `Intl.DisplayNames`. | Both |
| BR-10 | `organization.defaultTaxRate`: required number, 0 to 100 inclusive, at most 4 decimal places. | Both |
| BR-11 | `quotePrefix`, `workOrderPrefix`, `invoicePrefix`: required, 1–20 characters, uppercased, then only `A–Z`, `0–9` and `-`. | Both |
| BR-12 | `organization.nextInvoiceNumber`: required integer from 1 to 999,999,999,999. `next_quote_number` and `next_work_order_number` keep the schema default `1`. | Both |
| BR-13 | `branch.name`: required, ≤140. `branch.code`: required, uppercased, then 1–8 characters of `A–Z`, `0–9` and `-`. | Both |
| BR-14 | Branch address (label "Company address", stored as the first branch's address): `addressLine1` required ≤180, `city` required ≤100, `stateRegion` optional ≤100, `postalCode` required ≤30, `countryCode` required ISO 3166-1 alpha-2 (uppercase, resolved by `RegionInfo`; label "Default country"). `address_line2` stays null. | Both |
| BR-15 | `branch.businessHours`: object whose keys are lowercase English weekdays (`monday`…`sunday`). Each open day maps to `{ "start": "HH:mm", "end": "HH:mm" }` in 24-hour time with `start < end`, and closed days are omitted. Unknown keys or extra properties are invalid. All days closed is allowed and stored as `{}`. | Both |
| BR-16 | `owner.firstName` and `owner.lastName`: required, ≤100 each. | Both |
| BR-17 | `owner.password`: 12–128 characters (UTF-16 code units), not trimmed, and not equal to `owner.email` ignoring case. No composition rules. | Both |
| BR-18 | `confirmPassword` must equal `owner.password`. | Frontend |
| BR-19 | `owner.email` must not exist in `users` after normalization (trim + lowercase). Organization name and tax ID may repeat across organizations. | Backend |
| BR-20 | Password hash: PBKDF2-HMAC-SHA256 through `Rfc2898DeriveBytes.Pbkdf2` with 600,000 iterations, a 16-byte random salt and a 32-byte derived key. Stored as `pbkdf2-sha256$600000${base64-salt}${base64-hash}` in `users.password_hash`. No new package. | Backend |
| BR-21 | Rate limits on this endpoint use ASP.NET Core's built-in rate limiter. Per client IP (`RemoteIpAddress`): 5 requests per 15-minute fixed window. Global: 100 requests per 1-hour fixed window. Every request counts, whatever its outcome. Maximum request body 32 KB. The limiter runs after CORS so `429` carries CORS headers. | Backend |
| BR-22 | Audit row: `action = 'organization.registered'`, `entity_type = 'organization'`, `entity_id` = new organization id, `organization_id` = new organization id, `actor_user_id` = new owner id, `ip_address` = client IP, `branch_id`, `before_data`, `after_data` null, `metadata` `{}`. | Backend |
| BR-23 | Never log request bodies, passwords, password hashes, emails or phone numbers. Only the method and path are logged (existing `RequestLoggingMiddleware`). | Backend |
| BR-24 | User-facing message catalog, the only field messages allowed: "This field is required." · "Use {n} characters or fewer." · "Enter a valid email address, for example name@company.com." · "Enter a valid phone number." · "Select a time zone." · "Select a currency." · "Select a country." · "Enter a rate between 0 and 100 with up to 4 decimals." · "Use 1–20 characters: letters A–Z, numbers and hyphens." · "Use 1–8 characters: letters A–Z, numbers and hyphens." · "Enter a whole number from 1 to 999,999,999,999." · "End time must be after start time." · "Use 12 to 128 characters." · "Choose a password that is different from your email." · "Passwords don't match." · "An account with this email already exists. Sign in instead." · "Enter a valid value." | Both |
| BR-25 | Message selection: each invalid field shows exactly one message, taken from the first rule that fails in the order the validation messages table lists it for that field. `{n}` is the field's maximum length. | Both |

### Validation messages

Rules for each field are checked in order, and the first failing rule gives
the message (BR-25). "Client" means the check exists only in the browser;
"Server" means it exists only in the backend. Otherwise both apply.

| Field key | Rule order → message |
| --------- | -------------------- |
| `organization.name` (160), `organization.legalName` (200), `branch.name` (140), `owner.firstName` (100), `owner.lastName` (100) | 1. Empty → "This field is required." 2. Longer than `{n}` → "Use {n} characters or fewer." |
| `organization.taxId` (60) | 1. Longer than 60 → "Use 60 characters or fewer." |
| `organization.email`, `owner.email` | 1. Empty → "This field is required." 2. Longer than 254 → "Use 254 characters or fewer." 3. BR-06 format fails → "Enter a valid email address, for example name@company.com." |
| `branch.email` | Same as `owner.email` without rule 1; empty is valid. |
| `organization.phone` | 1. Empty → "This field is required." 2. Longer than 40 → "Use 40 characters or fewer." 3. BR-07 format fails → "Enter a valid phone number." |
| `branch.phone`, `owner.phone` | Same as `organization.phone` without rule 1; empty is valid. |
| `organization.timezone`, `branch.timezone` | 1. Empty or not a BR-08 identifier → "Select a time zone." |
| `organization.currency` | 1. Empty or not in the BR-09 list → "Select a currency." |
| `branch.countryCode` | 1. Empty or not a BR-14 code → "Select a country." |
| `branch.addressLine1` (180), `branch.city` (100), `branch.postalCode` (30) | 1. Empty → "This field is required." 2. Longer than `{n}` → "Use {n} characters or fewer." |
| `branch.stateRegion` (100) | 1. Longer than 100 → "Use 100 characters or fewer." |
| `organization.defaultTaxRate` | 1. Empty → "This field is required." 2. Not a number, outside 0–100 or more than 4 decimals → "Enter a rate between 0 and 100 with up to 4 decimals." |
| `organization.quotePrefix`, `organization.workOrderPrefix`, `organization.invoicePrefix` | 1. Empty → "This field is required." 2. More than 20 characters or a character outside `A–Z`, `0–9`, `-` → "Use 1–20 characters: letters A–Z, numbers and hyphens." |
| `organization.nextInvoiceNumber` | 1. Empty → "This field is required." 2. Not an integer from 1 to 999,999,999,999 → "Enter a whole number from 1 to 999,999,999,999." |
| `branch.code` | 1. Empty → "This field is required." 2. More than 8 characters or a character outside `A–Z`, `0–9`, `-` → "Use 1–8 characters: letters A–Z, numbers and hyphens." |
| `branch.businessHours.{day}.start`, `branch.businessHours.{day}.end` (open day) | 1. Empty → "This field is required." 2. Server: not `HH:mm` in 24-hour time → "Enter a valid value." 3. `.end` only: end not after start → "End time must be after start time." |
| `branch.businessHours` | 1. Server: not an object, a key other than `monday`…`sunday`, or an extra property in a day → "Enter a valid value." |
| `owner.password` | 1. Empty → "This field is required." 2. Fewer than 12 or more than 128 characters → "Use 12 to 128 characters." 3. Equal to `owner.email` ignoring case → "Choose a password that is different from your email." |
| `confirmPassword` (client) | 1. Empty → "This field is required." 2. Different from `owner.password` → "Passwords don't match." |
| `owner.email` (server, `409`) | Normalized email already in `users` (BR-19) → "An account with this email already exists. Sign in instead." |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | Organization `is_active = true` | Successful registration | System | FR-04 transaction commits |
| — | Branch `is_active = true` | Successful registration | System | FR-04 transaction commits |
| — | User `status = active`, `email_verified_at = null` | Successful registration | System | Email not in `users` (BR-19) |
| — | Membership `status = active`, role `owner`, `is_all_branches = true`, `joined_at` = now | Successful registration | System | Seeded role `owner` exists |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `organizations`: `id, name, legal_name, tax_id, email, phone, timezone, currency, default_tax_rate, quote_prefix, work_order_prefix, invoice_prefix, next_invoice_number`. Other columns keep their defaults.
  - `branches`: `id, organization_id, name, code, email, phone, address_line1, city, state_region, postal_code, country_code, timezone, business_hours`. `address_line2` null; other columns keep their defaults.
  - `users`: `id, email, password_hash, first_name, last_name, phone, status`.
  - `roles`: read only, `code = 'owner'`.
  - `organization_users`: `id, organization_id, user_id, role_id, status, is_all_branches, joined_at`. `invited_by_user_id` null.
  - `organization_user_branches`: `organization_user_id, branch_id`.
  - `audit_logs`: columns per BR-22.
- Schema amendments required: None. No EF migration.
- Domain code changes (no schema effect):
  - `Organization` must accept the BR-03 to BR-12 values.
  - `Branch` must accept contact, address, time zone and business hours.
  - `User` must allow an `active` status.
  - `OrganizationUser` must allow `is_all_branches`, `status` and `joined_at`.
- Constraint used for FR-06: `users (email)` unique. The EF migration names it
  `ix_users_email`; `database-reviewer` confirms the unique index from the EF
  model and committed migrations before the conflict mapping relies on it
  (see Dependencies).
- `organization_user_branches` has no `organization_id` or composite FK. The
  branch and membership are created in the same transaction for the same
  organization, so no cross-tenant link is possible here.

## Tenant isolation and authorization

- Organization context: none exists before the request. This workflow is
  pre-tenant and anonymous (`[AllowAnonymous]`). The new `OrganizationId` is
  generated by the domain and used for every tenant-owned row created by the
  request.
- Client-provided organization identifiers: never accepted. The request
  contract has no identifier fields. Unknown properties such as
  `organizationId`, `branchId`, `userId` or `roleId` are ignored, and the result
  is identical to a request without them.
- An existing organization: cannot be read, joined or modified through this
  endpoint. A request that names an existing organization's id creates a new,
  separate organization; the existing organization's rows are unchanged.
- The only cross-tenant signal is the duplicate-email `409` (FR-06), accepted
  and limited by BR-21. No other response includes data from other
  organizations.
- The role is always the seeded `owner`; the client cannot choose it.
- No permission is required. After registration, every Company Setup action
  requires the future authentication spec.

## API contracts

Contract status: Final. Paths are relative to the API base URL (`API_CONFIG`).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/organization-registrations` | `application/json` body per BR-01: `{ "organization": {…}, "branch": {…, "businessHours": {…}}, "owner": {…} }`; ≤32 KB | `201` `{ "organizationId": "{uuid}" }`, no cookies, no auth headers, no `Location` | `400` ValidationProblemDetails (`errors` dotted keys, BR-24 messages, `traceId`) · `409` ProblemDetails with `errors: { "owner.email": ["An account with this email already exists. Sign in instead."] }` · `413` ProblemDetails · `415` ProblemDetails · `429` ProblemDetails + `Retry-After` (seconds) · `500` generic ProblemDetails | Anonymous |

- Malformed JSON or type mismatches return the framework's automatic `400`. The
  frontend never shows its messages (FR-13).
- Every error body follows `docs/backend/api-configuration.md`: no exception
  text, no stored values, and a `traceId`.
- The `409` mapping uses a dedicated `IExceptionHandler` registered before
  `GlobalExceptionHandler`, plus the pre-insert existence check.

## UI behavior and states

Design reference (approved by the user on 2026-09-24): design 17, Company
Setup. It is used in this spec as a deliberate reinterpretation: the handoff
describes it as an authenticated `/admin/company` screen, and here it serves
as the public, single-submit registration form.

- `design/identity-access/screens/17-design.png`
- `design/identity-access/handoff/Company Setup.dc.html` (reference only; rebuild in Angular + PrimeNG + semantic HTML)
- `design/identity-access/handoff/Identity-and-Organization-Handoff.md` §2 (only `[C]` items; `[S]`/`[O]` items are not requirements)
- `image-slot.js` and `support.js` are export-support files, not application code.

Carried over from design 17: Company profile fields (minus logo and website),
the grouped address, time zone and country; the branch drawer fields (name,
code with its helper "Used in document numbering and reports.", phone, email,
time zone, business hours) rendered as an inline section; Taxes & currency
(minus prices-include-tax, the currency warning and "Manage tax rates");
Document numbering (minus "Edit sequences") with its helper "Prefixes appear
before the sequential number, for example INV-1049."

Not carried over: app shell, sidebar, top bar, breadcrumb, Administration
sub-nav, branch table and search, "Add branch", Viewer read-only banner,
Discard/Save changes, dirty guard, drawer docking, branch status tag,
deactivate branch, "Main branch" tag.

The Owner account section has no mockup. It uses design 17's field pattern:
label above the field, `*` for required fields, helper text and an error
message under the field. Fields: First name, Last name, Email, Phone, Password
and Confirm password, with PrimeNG `p-password`, `toggleMask` and
`feedback=false`. Password helper: "Use at least 12 characters."

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Register company (`/auth/register-company`) | No initial data request: the page renders at once. Submitting: the button shows a spinner and the label "Creating organization…", inputs are locked and repeat submits are ignored | Not applicable (form) | Field errors plus a summary `p-message` (`role="alert"`) with links to the fields; response errors per Error behavior. Entered values are kept, password fields are cleared | Not applicable: public page | Navigate to `/auth/sign-in?registered=true`; no toast |

- Mockups: design 17 (Approved).
- Accessibility:
  - Each section is a `section` element labelled by its `h2` through `aria-labelledby`.
  - Required fields have `*` and `aria-required="true"`.
  - Invalid fields have `aria-invalid="true"` and `aria-describedby` pointing at their message.
  - The address group has one visible label, and each of its inputs has an `aria-label`.
  - Each day's open toggle is a switch labelled "{Day} open". Its time inputs are labelled "{Day} start time" and "{Day} end time". A closed day shows "Closed".
  - The error summary receives focus on failed submit, and each summary link focuses its field.
  - The show/hide password buttons are labelled "Show password" / "Hide password".
  - Color is never the only error indicator.
  - Enter submits the form.
- Responsive:
  - Breakpoints: `md` = 48rem, `lg` = 64rem (`docs/frontend/styling-architecture.md` §7).
  - Below `md`: every section is one column.
  - From `md`: every section uses a 2-column grid.
  - From `lg`: Company profile uses 3 columns, as in design 17; the other sections stay at 2 columns.
  - The address group, the business hours editor and the error summary always span the full width.
  - Touch targets ≥44px on mobile; no horizontal scroll at 320px.
  - `src/styles/_breakpoints.scss` is created with this first consumer, per `docs/frontend/styling-architecture.md`.
- Styling: Aura + teal preset tokens only. No handoff hex values. Deferred
  items (Inter, navy, type scale, radius 10, spacing scale, status colors,
  PrimeIcons) are not used.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Client validation fails | Field messages (BR-24) and error summary; no request | None |
| `400` with `errors` | Each mapped key shows its message on the field (non-catalog text → "Enter a valid value."); summary shows `ApiError.message` | None |
| `400` without mappable keys (malformed body) | Summary shows `ApiError.message` | None |
| `409` duplicate email | Field error on Owner email: "An account with this email already exists. Sign in instead." | None |
| `413` / `415` | Summary shows `ApiError.message` | None |
| `429` | Summary shows `ApiError.message`; submit disabled for `Retry-After` seconds (60 if absent), then re-enabled | None |
| `500`, missing `owner` role, database failure | Summary shows `ApiError.message`; full rollback (FR-09) | None |
| Network failure (status 0) | Summary shows `ApiError.message`; form unlocked | None, or complete creation if the server committed; a retry then gets `409` |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An anonymous visitor | They open `/auth/register-company` | The form renders the five sections in FR-01 order with no authentication prompt |
| AC-02 | The form with empty required fields | The visitor submits | No HTTP request is sent and every empty required field shows its rule-1 message from the validation messages table |
| AC-03 | A failed submit | The page updates | The error summary is shown and focus is on the first invalid field |
| AC-04 | A form after a failed submit | The visitor corrects a field and blurs it | That field's error disappears |
| AC-05 | For each field and rule in the validation messages table checked by the client, a value that fails only that rule | The frontend validates | The field shows that rule's message |
| AC-06 | A valid request | It is posted | The response is `201` with `{ "organizationId" }` and no `Set-Cookie` or authentication header |
| AC-07 | A valid request | It is posted | Exactly one row is created in each of `organizations`, `branches`, `users`, `organization_users`, `organization_user_branches` and `audit_logs`, all tied to the returned `organizationId` |
| AC-08 | A successful registration | The membership row is read | It has role `owner`, `status = active`, `is_all_branches = true`, non-null `joined_at`, and a `organization_user_branches` row for the new branch |
| AC-09 | A successful registration | The user row is read | `status = active`, `email_verified_at` is null and email is lowercased and trimmed |
| AC-10 | A request whose body includes `organizationId` set to an existing organization's id | It is posted | A new organization with a different id is created and the existing organization's rows are unchanged |
| AC-11 | For each field and rule in the validation messages table checked by the server, a request that fails only that rule | It is posted | The response is `400` with that field's key and that rule's message, and no rows are created |
| AC-12 | An existing user with email `owner@acme.com` | A request uses ` Owner@ACME.com ` | The response is `409` with the `owner.email` error and no rows are created |
| AC-13 | Two valid concurrent requests with the same owner email | Both are posted | One returns `201`, the other `409`, and only one organization exists for that email |
| AC-14 | A successful registration | `users.password_hash` is read | It matches `pbkdf2-sha256$600000${base64-salt}${base64-hash}` and verifies against the submitted password with PBKDF2-HMAC-SHA256 |
| AC-15 | A registration request | Logs are inspected | No log entry contains the password, hash, emails or phones |
| AC-16 | A client IP that sent 5 requests in the current 15-minute window | It sends a 6th | The response is `429` with `Retry-After` and CORS headers, and no rows are created |
| AC-17 | 100 requests in the current hour | Any client sends another | The response is `429` with `Retry-After` |
| AC-18 | A body larger than 32 KB | It is posted | The response is `413` and no rows are created |
| AC-19 | The `owner` role row is missing, or the audit insert fails | A valid request is posted | The response is `500` and no rows exist in any of the six tables for that request |
| AC-20 | A successful registration | The `audit_logs` row is read | It matches every BR-22 value |
| AC-21 | A valid form | The visitor submits | The submitting state is shown, inputs are locked and a second click sends no second request |
| AC-22 | A `201` response | The frontend handles it | The browser navigates to `/auth/sign-in?registered=true` |
| AC-23 | A `409` response | The frontend handles it | The Owner email field shows "An account with this email already exists. Sign in instead." and the other values are kept |
| AC-24 | A `429` response with `Retry-After: 30` | The frontend handles it | Submit is disabled for 30 seconds, then enabled |
| AC-25 | A `500` or network error | The frontend handles it | The summary shows `ApiError.message`, the form is unlocked and the password fields are empty |
| AC-26 | A `400` field error with text outside BR-24 | `ApiErrorService` maps it | The field shows "Enter a valid value." |
| AC-27 | A `409` with an `errors` object | `ApiErrorService` maps it | `ApiError.fieldErrors` contains the `owner.email` entry |
| AC-28 | The company time zone is changed and the branch time zone was never edited | The branch section updates | The branch time zone equals the company time zone |
| AC-29 | The branch time zone was edited | The company time zone is changed | The branch time zone keeps the edited value |
| AC-30 | Business hours with Monday 08:00–17:00 and Sunday closed | The request is built | `businessHours` contains `"monday": {"start":"08:00","end":"17:00"}` and no `sunday` key |
| AC-31 | A day with end time not after start time | The form is validated | That day shows "End time must be after start time." |
| AC-32 | A viewport 320px wide | The page renders | All fields are in one column and there is no horizontal scroll |
| AC-33 | Keyboard-only use | The visitor tabs through the page | Every control is reachable in visual order with a visible focus indicator, and Enter submits |
| AC-34 | An automated accessibility check (axe) on the page in default and error states | It runs | It reports no violations |
| AC-35 | A request with `Content-Type: text/plain` | It is posted | The response is `415` ProblemDetails and no rows are created |
| AC-36 | A request whose body is not valid JSON | It is posted | The response is `400` ProblemDetails with a `traceId`, no field keys, and no rows are created |
| AC-37 | A viewport 800px wide | The page renders | Every section shows its fields in 2 columns |
| AC-38 | A viewport 1280px wide | The page renders | Company profile shows its fields in 3 columns |
| AC-39 | Owner email `ab@cd.io` and password `ab@cd.io` | The form is validated | The password field shows only "Use 12 to 128 characters." |
| AC-40 | The currency selector | It is opened | It lists `USD` and does not list `XAU` or any other code outside the BR-09 list |

## Testing requirements

| Level | Covers |
| ----- | ------ |
| Backend unit | Validators (BR-03 to BR-17): AC-11. Password hasher: AC-14. Domain factories and states: AC-08, AC-09 |
| Backend integration (Testcontainers) | AC-06, AC-07, AC-08, AC-09, AC-12, AC-13, AC-15, AC-16, AC-17, AC-18, AC-19, AC-20, AC-35, AC-36 |
| Authorization/tenant isolation | AC-10 (client id ignored, existing tenant unchanged); AC-06 (no session issued); AC-07 (all rows share the new organization id); anonymous access to the endpoint (AC-06) |
| Frontend component/service | AC-01, AC-02, AC-03, AC-04, AC-05, AC-21, AC-22, AC-23, AC-24, AC-25, AC-26, AC-27, AC-28, AC-29, AC-30, AC-31, AC-39, AC-40 |
| Frontend QA (Playwright, final audit) | AC-32, AC-33, AC-34, AC-37, AC-38; no data-changing submissions against the local database |

## Dependencies

- Sign In route `/auth/sign-in` (future `authentication` spec, not started):
  must exist before this spec is implemented, so that AC-22 can pass. Status:
  Pending.
- Seeded `owner` role in `roles`: present (schema seed and migrations).
- Identity and tenancy tables migrated: present
  (`InitialIdentityAndTenancy`).
- `database-reviewer` confirms the `users (email)` unique index from the EF
  model and committed migrations before implementation: Pending.
- First use case: add the target project references from `backend/CLAUDE.md`
  (`Api → Application + Infrastructure`, `Infrastructure → Application + Domain`,
  `Application → Domain`).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Server validation uses FluentValidation (already installed in `FieldOps.Application`); errors keep the ValidationProblemDetails shape. |
| AS-02 | Duplicate-email conflicts caught at the database are mapped to `409` by a dedicated `IExceptionHandler`. |
| AS-03 | Endpoint path `/organization-registrations`, following the existing no-prefix routing (production reaches it under `/api` through the proxy). |
| AS-04 | The submit button label is "Create organization"; the page title is "Create your organization". |
| AS-05 | Country options come from a static ISO 3166-1 alpha-2 list in the feature, with display names from `Intl.DisplayNames`. |
| AS-06 | The frontend uses PrimeNG built-in icons only; `primeicons` is not added. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Page layout | Single page / wizard | Yes | Single page with stacked sections (2026-09-24) |
| OD-02 | Mockup fields without columns | Drop all / map address and country to first branch | Yes | Map company address and default country to the first branch; drop the rest (2026-09-24) |
| OD-03 | Legal name, business email, phone required? | Required / optional | Yes | Required in app and server (2026-09-24) |
| OD-04 | Owner section fields | With/without confirm and terms | Yes | Names, email, password, client-only confirm, optional phone; no terms (2026-09-24) |
| OD-05 | Account state | Active / pending; all branches | Yes | User and membership active, `is_all_branches = true` plus branch link (2026-09-24) |
| OD-06 | Duplicate email response | Field `409` / generic | Yes | Field-level `409` after normalization (2026-09-24) |
| OD-07 | Duplicate organization | Allow / reject | Yes | Allow; no uniqueness (2026-09-24) |
| OD-08 | Password policy and hashing | PBKDF2 built-in / Identity hasher / Argon2id | Yes | 12–128, not equal to email, PBKDF2-HMAC-SHA256 600k (2026-09-24) |
| OD-09 | Abuse protection | Per-IP + global / + honeypot / global only | Yes | Per-IP 5/15 min, global 100/h, 32 KB, no captcha; proxy risk accepted until deployment (2026-09-24) |
| OD-10 | First branch fields | With / without business hours | Yes | With business hours (2026-09-24) |
| OD-11 | Taxes and numbering | Invoice sequence only / all sequences | Yes | Invoice sequence only; time zone list from `Intl`, server re-validates (2026-09-24). Currency list refined to one static ISO 4217 list shared by client and server (BR-09), so no offered code can be rejected (2026-09-24) |
| OD-12 | Redirect without Sign In route | Dependency / in-page success | Yes | Redirect required; Sign In route is an implementation dependency (2026-09-24) |
| OD-13 | Route, feature folder, error contract | `organizations` / `authentication` | Yes | `/auth/register-company` in `features/organizations`; contract per FR-13 (2026-09-24) |
| OD-14 | Audit row | With IP / none / without IP | Yes | With IP (2026-09-24) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-03, AC-04, AC-05, AC-39, AC-40 |
| FR-03 | AC-11, AC-35, AC-36 |
| FR-04 | AC-07, AC-08, AC-09, AC-20 |
| FR-05 | AC-10 |
| FR-06 | AC-12, AC-13 |
| FR-07 | AC-14, AC-15 |
| FR-08 | AC-16, AC-17, AC-18 |
| FR-09 | AC-19 |
| FR-10 | AC-06, AC-22 |
| FR-11 | AC-21 |
| FR-12 | AC-23, AC-24, AC-25 |
| FR-13 | AC-26, AC-27 |
| FR-14 | AC-28, AC-29 |
| FR-15 | AC-30, AC-31 |
| FR-16 | AC-32, AC-33, AC-34, AC-37, AC-38 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-24 | — → DRAFT     | Created |
| 2026-09-24 | DRAFT → DRAFT | Fixed validation findings F1–F7: FR-02/FR-03 rule ranges; BR-25 and validation messages table; BR-02 time zone default; BR-09 shared currency list; `415` and malformed-JSON ACs; column layout per breakpoint. Added AC-35 to AC-40 |
| 2026-09-24 | DRAFT → APPROVED | Approved by user via /spec approve |
