# Company settings and branches

| Field    | Value                           |
| -------- | ------------------------------- |
| Feature  | `company-settings-and-branches` |
| Type     | Full-stack                      |
| Status   | APPROVED                        |
| Created  | 2026-09-27                      |
| Updated  | 2026-09-27                      |
| Approved | 2026-09-27                      |

## Context and objective

Registration creates an organization and its first branch, but nobody can
change them afterwards or add branches. This feature adds the authenticated
Company setup screen (design 17) at `/admin/company`. There, an Owner
maintains the current organization's profile, taxes and currency, and
document numbering, and creates, edits, deactivates and reactivates branches.
A Viewer sees the same screen read-only. Success means that every read and
write is limited to the session organization, is authorized in the backend,
and writes an audit row for every change.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (role `owner`) | Open `/admin/company`; read and update organization settings; list and read all branches of the session organization; create and update branches; deactivate and reactivate branches | Read or change another organization or its branches; supply an organization identifier; deactivate the last active branch; delete an organization or branch |
| Viewer (role `viewer`) | Open `/admin/company`; read organization settings; list and read the branches they can access (BR-11) | Any mutation (API returns `403`); read branches outside their access |
| Other roles (`dispatcher`, `technician`, `accounting`, `operations_manager`) | Open `/overview` and follow its link | Read or change company settings or branches (API returns `403`) |
| Unauthenticated visitor | — | Any endpoint in this spec (`401`); `/admin/company` redirects to `/auth/sign-in` |
| System | Resolve user, organization and membership from the validated session; write audit rows | Accept client-supplied organization, user or role identifiers |

## Scope

- Authenticated page `/admin/company` built from design 17, with the page's
  own header and in-page section links (no global app shell).
- "Company settings" link on `/overview` as the entry point.
- Organization settings: display name, legal name, tax ID, business email,
  phone, default time zone, currency, default tax rate, quote, work order
  and invoice prefixes, and next invoice number. One Save covers all of them.
- Branch list with client-side search, status filter and sorting.
- Branch drawer to create, view and edit a branch: name, code, contact phone
  and email, address, time zone and business hours.
- Deactivating and reactivating branches, with the last-active-branch rule.
- Read-only mode for Viewer; in-page forbidden state for other roles.
- Two role-based backend authorization policies (BR-10) and optimistic
  concurrency based on `updated_at` (BR-07).
- Audit rows for every successful change.
- Loading, empty, error, permission, dirty, submitting and success states;
  unsaved-changes guard; responsive layout and accessibility.

## Non-goals

- Public organization registration, Sign In, session redesign and
  organization switching.
- The global app shell (sidebar, top bar, organization selector,
  Administration sub-nav as shared navigation): a follow-up spec.
- Invitations, user, role and permission administration, and assigning users
  to branches (`organization_user_branches` is read only here).
- Seeding `permissions` or `role_permissions`.
- Technician profiles and the branch "Team" column.
- Fields without schema support: company logo, website, company address and
  default country at organization level, prices-include-tax, service ZIP
  codes, "Use company billing settings", and the "Main branch" tag.
- "Manage tax rates" and "Edit sequences" links; editing `next_quote_number`,
  `next_work_order_number` and `require_customer_signature`.
- A currency-change confirmation dialog, and blocking deactivation because of
  operational records.
- Customers, requests, scheduling, invoicing, payments, map or geocoding.
- Permanent deletion of organizations or branches; deactivating the
  organization.
- Server-side paging of branches; browser `beforeunload` prompts.
- New icons, packages, design tokens or shared abstractions.

## User flow

1. A signed-in user selects **Company settings** on `/overview`.
2. The system loads `GET /organization-settings` and `GET /branches` and shows
   skeletons meanwhile.
3. Other roles see the forbidden state (`403`). A Viewer
   (`canManage = false`) sees the read-only banner and disabled controls.
4. An Owner edits organization fields; **Save changes** and **Discard
   changes** become enabled.
5. The Owner selects **Save changes**. The system validates in the browser,
   sends `PUT /organization-settings` with the loaded `updatedAt`, locks the
   form, shows "Company settings saved" and reloads the session.
6. The Owner searches or filters branches and selects a row, or **Add
   branch**. The branch drawer opens.
7. The Owner edits the branch and selects **Update branch** or **Create
   branch**. The system saves the branch, closes the drawer, shows a toast and
   reloads the list.
8. The Owner selects **Deactivate** (row menu or drawer) and confirms. The
   system deactivates the branch, or explains why it cannot. **Reactivate**
   works the same way without a confirmation.
9. When the user leaves the page with unsaved changes or closes a dirty
   drawer, the system asks for confirmation.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/admin/company`, lazily loaded from `features/organizations` as a second route entry and protected by `authGuard`. `/overview` must show a **Company settings** link to it for every signed-in user. |
| FR-02 | The backend must authorize every endpoint in this spec with the BR-10 policies. User, organization and membership are taken only from the validated session. Any organization identifier in the path, query or body is ignored. |
| FR-03 | `GET /organization-settings` must return the session organization's BR-01 fields, `updatedAt`, and `canManage` (`true` only for the manage policy). |
| FR-04 | `PUT /organization-settings` must normalize and validate BR-01 and BR-02, check BR-07, update the session organization, set `updated_at` to now, write the BR-12 audit row in the same transaction, and return `200` with the FR-03 body. |
| FR-05 | `GET /branches` must return the branches of the session organization that the caller may access (BR-11), sorted by name ascending, both active and inactive, with the list fields in API contracts. |
| FR-06 | `GET /branches/{id}` must return one branch's detail fields when it belongs to the session organization and the caller may access it (BR-11). |
| FR-07 | `POST /branches` must normalize and validate BR-03, enforce BR-04 and BR-05, create an active branch in the session organization, write the audit row in the same transaction, and return `201` with the detail body. |
| FR-08 | `PUT /branches/{id}` must normalize and validate BR-03, enforce BR-04 and BR-07, update the branch, set `updated_at` to now, write the audit row in the same transaction, and return `200` with the detail body. Inactive branches may be edited. |
| FR-09 | `POST /branches/{id}/deactivate` and `POST /branches/{id}/reactivate` must change `is_active` per the States table, enforce BR-06 and BR-08, set `updated_at` and write the audit row when the state changes, and return `204`. |
| FR-10 | A branch id that does not exist, belongs to another organization or is outside the caller's BR-11 access must return `404` on every branch endpoint, with no data change. |
| FR-11 | Every successful state change must write exactly one `audit_logs` row per BR-12. Requests that change nothing (`204` no-op, errors) write none. |
| FR-12 | The page must show the loading, error, forbidden and read-only states in UI behavior. When `canManage` is `false`, the page shows the read-only banner, disables all inputs, hides every mutating action, and opens the drawer read-only. |
| FR-13 | The organization form must enable **Save changes** and **Discard changes** only when dirty. It validates on save and on blur after the first save attempt, shows an error summary and focuses the first invalid field. It locks inputs and sends exactly one request per save. On `200` it shows the success toast, resets to the returned values and reloads the session. **Discard changes** asks for confirmation and restores the loaded values. |
| FR-14 | The branch list must show Branch (name and code), Address, Time zone, Status and Actions. It supports search on name, code and city (case-insensitive, contains), a status filter (All statuses / Active / Inactive), and ascending/descending sort on the Branch column. All of this runs client-side over the `GET /branches` result. |
| FR-15 | The branch drawer must create ("New branch", **Create branch**) and edit ("{Branch name}", **Update branch**) a branch with the BR-03 fields and the business hours editor. It validates like FR-13 and maps server field errors to its fields. On success it closes, shows the toast and reloads the list. |
| FR-16 | Deactivation must ask for confirmation. The action is disabled, with the BR-06 message as a tooltip, when the branch is the only active one in the loaded list. Reactivation needs no confirmation. Both reload the list and show a toast on success. |
| FR-17 | Leaving `/admin/company` with a dirty organization form or drawer, and closing a dirty drawer, must ask "Discard unsaved changes?" with **Discard** / **Keep editing**. |
| FR-18 | The page must meet the responsive and accessibility rules in UI behavior. |
| FR-19 | When any request made by the page or drawer returns `401`, the frontend must clear the session state and navigate to `/auth/sign-in`, discarding unsaved edits without the FR-17 prompt. |
| FR-20 | The backend must follow BR-14 for every endpoint in this spec, including failed requests. |

## Business and validation rules

All text fields are trimmed; a whitespace-only value counts as empty. Server
validation is authoritative; the client mirrors it except where marked
"Server".

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Organization fields (request and error keys), with the same limits, formats and messages as `organization-onboarding` BR-03 to BR-12 and its validation messages table: `name` (label "Display name", required, ≤160), `legalName` (required, ≤200), `taxId` (optional, ≤60), `email` (required, lowercased), `phone` (required), `timezone` (required IANA), `currency` (required, BR-09 list), `defaultTaxRate` (0–100, ≤4 decimals), `quotePrefix`, `workOrderPrefix`, `invoicePrefix` (required, uppercased, 1–20 of `A–Z0–9-`), `nextInvoiceNumber` (integer 1 to 999,999,999,999). | Both |
| BR-02 | Server: `nextInvoiceNumber` must be greater than the highest `invoices.invoice_number` of the session organization (any value ≥1 when none exist). Failure → `nextInvoiceNumber`: "Enter a number greater than the last invoice number." | Backend |
| BR-03 | Branch fields (request and error keys): `name` (required, ≤140), `code` (required, uppercased, 1–8 of `A–Z0–9-`), `phone` (optional), `email` (optional, lowercased), `timezone` (required IANA), `addressLine1` (required, ≤180), `addressLine2` (optional, ≤180), `city` (required, ≤100), `stateRegion` (optional, ≤100), `postalCode` (required, ≤30), `countryCode` (required ISO 3166-1 alpha-2), `businessHours` (required object, shape of `organization-onboarding` BR-15; `{}` = all days closed). Formats and messages follow the onboarding rules for the same fields; `addressLine2` uses "Use 180 characters or fewer." Business hours errors use `businessHours.{day}.start` / `.end` and `businessHours`. | Both |
| BR-04 | `code` is unique within the organization, compared after uppercasing, across active and inactive branches. A conflict (pre-check or the `(organization_id, code)` unique constraint) → `409` with `errors.code`: "Another branch already uses this code." | Backend |
| BR-05 | An organization has at most 100 branches (active and inactive). Creating a 101st → `409` without field errors. | Backend |
| BR-06 | The last active branch of an organization cannot be deactivated → `409` without field errors. The check and update run in one transaction that locks the organization's branch rows, so concurrent deactivations cannot leave zero active branches. | Backend |
| BR-07 | `PUT` bodies carry the `updatedAt` value last read. If it differs from the stored `updated_at`, the response is `409` without field errors and nothing changes. A missing or malformed `updatedAt` → `400` with key `updatedAt`: "Enter a valid value." Every successful change sets `updated_at` to the current UTC time. | Backend |
| BR-08 | Deactivating an inactive branch or reactivating an active one returns `204`, changes nothing and writes no audit row. | Backend |
| BR-09 | Request bodies: `application/json` only (`415` otherwise); malformed JSON → `400` without field keys; maximum body 32 KB (`413`). Unknown properties are ignored. | Backend |
| BR-10 | Policies, based on the membership role read from the validated session on every request: **CompanySettingsView** = `owner` or `viewer`, for the GET endpoints; **CompanySettingsManage** = `owner`, for `PUT /organization-settings`, `POST /branches`, `PUT /branches/{id}` and the deactivate/reactivate endpoints. Any other role → `403`. No session → `401`. | Backend |
| BR-11 | Branch access: an `owner` accesses every branch of the organization. A `viewer` whose membership has `is_all_branches = true` accesses every branch; otherwise only branches linked to the membership in `organization_user_branches`. | Backend |
| BR-12 | Audit row: `organization_id` and `actor_user_id` from the session, `ip_address` = client IP, `metadata` `{}`. Actions: `organization.settings_updated` (`entity_type` `organization`, `entity_id` = organization id, `branch_id` null); `branch.created`, `branch.updated`, `branch.deactivated`, `branch.reactivated` (`entity_type` `branch`, `entity_id` and `branch_id` = branch id). `before_data` / `after_data` contain only the changed fields by request key (created: `before_data` null, `after_data` all fields; deactivated/reactivated: `isActive`). The `email` and `phone` values are never stored; a changed one appears as `"[changed]"`. | Backend |
| BR-13 | Field message catalog: `organization-onboarding` BR-24 plus "Another branch already uses this code." and "Enter a number greater than the last invoice number." `ApiErrorService` accepts both new messages. | Both |
| BR-14 | Never log request bodies, emails, phones or tax IDs. Only method and path (existing `RequestLoggingMiddleware`). | Backend |
| BR-15 | Page copy (frontend, chosen by `ApiError.kind` and endpoint): forbidden "You don't have access to company settings."; read-only "You have view-only access to company settings."; load error "We couldn't load company settings." + **Retry**; branch load error "We couldn't load branches." + **Retry**; stale `409` "This record was changed by someone else. Reload to see the latest version." + **Reload**; BR-05 "Your organization has reached the limit of 100 branches."; BR-06 "At least one branch must stay active."; branch `404` "This branch is no longer available."; other failures show `ApiError.message`. | Frontend |
| BR-16 | Toasts: "Company settings saved", "{Branch name} created", "{Branch name} updated", "{Branch name} deactivated", "{Branch name} reactivated". | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | Branch `is_active = true` | `POST /branches` | Owner | BR-03, BR-04, BR-05 |
| Branch `is_active = true` | Branch `is_active = false` | `POST /branches/{id}/deactivate` | Owner | Another active branch exists (BR-06) |
| Branch `is_active = false` | Branch `is_active = true` | `POST /branches/{id}/reactivate` | Owner | None |

Inactive user, membership or organization: the existing session check
(`SessionCookieEvents`) makes the request unauthenticated (`401`), clears the
cookie, and the frontend returns to Sign In. No other behavior is added. An
inactive branch stays readable and editable; blocking new records for it
belongs to future specs.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `organizations`: read and update `name, legal_name, tax_id, email, phone,
    timezone, currency, default_tax_rate, quote_prefix, work_order_prefix,
    invoice_prefix, next_invoice_number, updated_at`; read `id, is_active`.
  - `branches`: create, read and update `id, organization_id, name, code,
    email, phone, address_line1, address_line2, city, state_region,
    postal_code, country_code, timezone, business_hours, is_active,
    created_at, updated_at`.
  - `organization_users`: read `id, role_id, is_all_branches`;
    `organization_user_branches`: read `organization_user_id, branch_id`;
    `roles`: read `code`.
  - `invoices`: read `MAX(invoice_number)` per organization (BR-02).
  - `audit_logs`: insert per BR-12.
- Constraints relied on: `branches UNIQUE (organization_id, code)` (BR-04);
  `ck_organizations_default_tax_rate`; index `ix_branches_org_active`.
- Schema amendments required: None. No EF migration.
- Domain code changes (no schema effect): `Organization` and `Branch` gain
  update and state-change methods that set `UpdatedAt`; `Branch.Create`
  accepts `addressLine2`.

## Tenant isolation and authorization

- Organization context: `OrganizationId`, `UserId` and `MembershipId` come
  from the validated session ticket (`SessionCookieEvents`), and the role
  from the membership on every request.
- Client-provided organization identifiers: never accepted. Request bodies
  have no organization field; an `organizationId` property is ignored.
  Branch ids in paths are always looked up together with the session
  `OrganizationId`.
- Branch of another organization: `404`, identical to an unknown id, with no
  data change (FR-10). The same applies to a branch outside a Viewer's
  BR-11 access.
- Every query and update filters by the session `OrganizationId`; the
  `MAX(invoice_number)` read is scoped the same way.
- Permissions: BR-10. Frontend `canManage`, hidden actions and disabled
  controls are UX only.
- CSRF: the mutating endpoints rely on the existing `SameSite=Strict`,
  `HttpOnly`, `Secure` session cookie and the CORS allow-list. No antiforgery
  token is added.

## API contracts

Contract status: Final. Paths are relative to the API base URL
(`API_CONFIG`). All responses to authenticated endpoints carry
`Cache-Control: no-store`. Error bodies follow
`docs/backend/api-configuration.md` (ProblemDetails with `traceId`).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/organization-settings` | — | `200` `{ name, legalName, taxId, email, phone, timezone, currency, defaultTaxRate, quotePrefix, workOrderPrefix, invoicePrefix, nextInvoiceNumber, updatedAt, canManage }` | `401`, `403` | View |
| PUT | `/organization-settings` | JSON: BR-01 fields + `updatedAt` | `200` GET body | `400` ValidationProblemDetails (BR-01, BR-02, BR-07 keys) · `401` · `403` · `409` stale · `413` · `415` | Manage |
| GET | `/branches` | — | `200` `{ items: [{ id, name, code, addressLine1, addressLine2, city, stateRegion, postalCode, countryCode, timezone, isActive }] }` | `401`, `403` | View |
| GET | `/branches/{id}` | — | `200` `{ id, name, code, email, phone, addressLine1, addressLine2, city, stateRegion, postalCode, countryCode, timezone, businessHours, isActive, updatedAt }` | `401`, `403`, `404` | View |
| POST | `/branches` | JSON: BR-03 fields | `201` detail body, `Location: /branches/{id}` | `400` · `401` · `403` · `409` (`errors.code`, BR-04) · `409` limit (BR-05) · `413` · `415` | Manage |
| PUT | `/branches/{id}` | JSON: BR-03 fields + `updatedAt` | `200` detail body | `400` · `401` · `403` · `404` · `409` (`errors.code`) · `409` stale · `413` · `415` | Manage |
| POST | `/branches/{id}/deactivate` | No body | `204` | `401` · `403` · `404` · `409` last active (BR-06) | Manage |
| POST | `/branches/{id}/reactivate` | No body | `204` | `401` · `403` · `404` | Manage |

- `updatedAt` is an ISO 8601 UTC timestamp with microsecond precision, sent
  back exactly as received.
- `404`, `409` and `403` mappings use explicit endpoint results or dedicated
  `IExceptionHandler`s registered before `GlobalExceptionHandler`
  (`docs/backend/api-configuration.md`).
- The frontend tells `409` cases apart by endpoint and by whether
  `fieldErrors` is empty.

## UI behavior and states

Design references (approved), used in their original authenticated context:

- `design/identity-access/screens/17-design.png` (verified as Company setup)
- `design/identity-access/handoff/Company Setup.dc.html` (reference only)
- `design/identity-access/handoff/Identity-and-Organization-Handoff.md` §2
  (`[C]` items; `[S]` items only where this spec adopts them)

Adaptations to the design:

- No app shell. The page header shows the breadcrumb text "Administration /
  Company", h1 "Company setup", the description "Manage your organization
  details, branches, taxes and document numbering." and the actions
  **Discard changes** / **Save changes**. Below it, a nav labelled "Company
  settings sections" links to the Company profile, Branches, Taxes & currency
  and Document numbering sections.
- Company profile: Display name, Legal business name, Tax ID, Business email,
  Phone number, Default time zone. No logo, website, address or country.
- Taxes & currency: Currency, Default sales tax rate (suffix %), and the note
  "Changing the currency doesn't convert amounts in existing records."
- Document numbering: Quote, Work order and Invoice prefix, Next invoice
  number, with the helper "Prefixes appear before the sequential number, for
  example INV-1049."
- Branches: header count "{n} branches", **Add branch**, search "Search
  branches…", status filter. No Team column and no "Main branch" tag. Row
  menu: **Edit** (**View** when read-only), **Deactivate** / **Reactivate**.
- Branch drawer: one scrollable form (no tabs). Name, Code (helper "Used in
  document numbering and reports."), Contact phone, Contact email, Time zone,
  Address line 1, Address line 2, City, State / region, Postal code, Country,
  Business hours (7-day editor). The status tag appears next to the title.
  The note reads "Deactivating a branch prevents new records. All historical
  data is preserved." Footer: **Deactivate branch** / **Reactivate branch**
  (left), **Cancel**, and **Update branch** / **Create branch**.
- Deactivate confirmation: "Deactivate {Branch name}? No new requests, quotes
  or work orders can be created for this branch. Existing records stay
  available." → **Deactivate** (danger) / **Cancel**.
- New branch defaults: time zone = organization time zone; business hours
  empty (all closed).

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Company setup (`/admin/company`) | Skeletons for profile fields and 3 skeleton rows in the branch table until both GETs resolve. Save: spinner, inputs locked | Branch list: "No branches to show." (Viewer without access); "No branches match these filters." + **Clear filters** | BR-15 load error + **Retry** (settings); branch load error + **Retry** (table only); save errors per Error behavior with values kept | `403`: only the header and the forbidden message. `canManage = false`: read-only banner, disabled inputs, no mutating actions | BR-16 toast; form pristine with the returned values |
| Branch drawer | Skeleton fields while `GET /branches/{id}` loads. Submit: spinner, inputs locked | Not applicable | Field errors + summary; stale `409` + **Reload**; `404` closes the drawer, shows the BR-15 message and reloads the list | Read-only: disabled inputs, only **Close** in the footer | Drawer closes; BR-16 toast; list reloads |

- Mockups: design 17 (Approved).
- Responsive (`md` = 48rem, `lg` = 64rem from `_breakpoints.scss`; no new
  breakpoints):
  - Below `md`: forms use one column; the branch table becomes a list of
    cards (name, code, address, status, actions menu); the drawer is full
    screen; Save and Discard sit in a sticky bottom bar; no horizontal scroll
    at 320px; touch targets ≥44px.
  - From `md`: 2-column forms; branch table.
  - From `lg`: Company profile uses 3 columns; Taxes & currency and Document
    numbering sit side by side.
  - The drawer is an overlay with a mask at every width (docking dropped).
- Accessibility:
  - Each section is a `section` labelled by its `h2`.
  - The drawer is a labelled dialog that traps focus. Esc closes it (with the
    dirty guard), and focus returns to the row or button that opened it.
  - Rows open on click, Enter or Space; the Branch column header has
    `aria-sort`; row menu buttons are labelled "More actions for {Branch
    name}".
  - Required fields show `*` and `aria-required="true"`. Invalid fields use
    `aria-invalid` and `aria-describedby`. The error summary receives focus.
  - Toggles are switches labelled "{Day} open". Status is shown as text in a
    tag, never by color alone.
- Styling: Aura + teal preset tokens only; PrimeNG built-in icons only. No
  handoff hex values, navy sidebar, Inter, or other deferred tokens.
- Toast and confirmation services are provided on the page, not in
  `app.config.ts`.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Client validation fails | Field messages (BR-13) and error summary; no request | None |
| `400` with field errors | Messages on mapped fields (non-catalog text → "Enter a valid value."); summary shows `ApiError.message` | None |
| `400` without field keys, `413`, `415` | Summary shows `ApiError.message` | None |
| `401` on any request | Session cleared; navigate to `/auth/sign-in` without the unsaved-changes prompt (FR-19) | None |
| `403` on load | Forbidden state (BR-15) | None |
| `403` on a mutation | Summary shows `ApiError.message`; values kept | None |
| `404` branch | Drawer closes; "This branch is no longer available."; list reloads | None |
| `409` duplicate code | `code` field shows "Another branch already uses this code." | None |
| `409` stale (`PUT`) | BR-15 stale message + **Reload**; edits kept until Reload | None |
| `409` branch limit | BR-15 limit message in the drawer | None |
| `409` last active branch | BR-15 message in a toast; branch stays active | None |
| `500`, network | `ApiError.message`; form unlocked, values kept | None (transaction rolled back) |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A signed-in Owner | `GET /organization-settings` is called | `200` with the session organization's BR-01 values, `updatedAt` and `canManage = true` |
| AC-02 | A signed-in Viewer | They call `GET /organization-settings` and `GET /branches` | Both return `200`; the settings body has `canManage = false` |
| AC-03 | A signed-in `dispatcher`, `technician`, `accounting` or `operations_manager` | They call any endpoint in this spec | The response is `403` and no data changes |
| AC-04 | No session cookie | Any endpoint in this spec is called | The response is `401` |
| AC-05 | An Owner of organization A and an existing organization B | `PUT /organization-settings` with a body containing `organizationId` = B | Only A is updated; B's row is unchanged |
| AC-06 | An Owner and a valid body with the current `updatedAt` | `PUT /organization-settings` | `200` with trimmed, lowercased email and uppercased prefixes; `updated_at` advanced; one `organization.settings_updated` audit row with only the changed fields per BR-12 |
| AC-07 | For each organization rule in BR-01 and BR-02 (including a `nextInvoiceNumber` not above the highest invoice number), a body failing only that rule | `PUT /organization-settings` | `400` with that key and its message; no change |
| AC-08 | A stored record whose `updated_at` differs from the sent `updatedAt` | `PUT /organization-settings` or `PUT /branches/{id}` | `409` without field errors; the record and audit log are unchanged |
| AC-09 | An Owner in an organization with 3 branches, and another organization's branches | `GET /branches` | `200` with exactly the 3 branches, active and inactive, sorted by name |
| AC-10 | A Viewer with `is_all_branches = false` linked to 1 of 3 branches | They call `GET /branches` and `GET /branches/{id}` for an unlinked branch | The list contains only the linked branch; the detail call returns `404` |
| AC-11 | An Owner of organization A and a branch of organization B | `GET`, `PUT`, `deactivate` or `reactivate` is called with B's branch id | `404`, and B's branch and audit rows are unchanged |
| AC-12 | An Owner and a valid branch body | `POST /branches` | `201` with `Location`, the detail body, `isActive = true`, uppercased code, the session organization id on the row, and one `branch.created` audit row with `branch_id` set |
| AC-13 | A branch with code `AUS-C` | Another branch is created or updated with code ` aus-c ` | `409` with `errors.code` "Another branch already uses this code."; no change |
| AC-14 | An organization with 100 branches | `POST /branches` | `409` without field errors; no branch created |
| AC-15 | For each branch rule in BR-03 (including business hours), a body failing only that rule | `POST /branches` or `PUT /branches/{id}` | `400` with that key and its message; no change |
| AC-16 | An Owner changes a branch's name and email | `PUT /branches/{id}` | `200`; one `branch.updated` audit row whose data contains `name` values and `email` only as `"[changed]"` |
| AC-17 | Two active branches | The Owner deactivates one, then reactivates it | Deactivate returns `204`, sets `is_active = false` and writes one `branch.deactivated` audit row; reactivate returns `204`, sets `is_active = true` and writes one `branch.reactivated` audit row |
| AC-18 | An organization with exactly one active branch | `POST /branches/{id}/deactivate` on it | `409` without field errors; the branch stays active; no audit row |
| AC-19 | A signed-in user on `/overview` | They select **Company settings** | The browser is on `/admin/company` |
| AC-20 | `/admin/company` | Its GET requests are pending | Profile field skeletons and 3 skeleton table rows are shown |
| AC-21 | `canManage = false` | The page and a branch drawer render | Read-only banner; all inputs disabled; no Save, Discard, Add branch, Update branch, Deactivate or Reactivate actions |
| AC-22 | A loaded form | The Owner edits a field, then selects **Discard changes** and confirms | Save/Discard are disabled before the edit and enabled after it; after Discard the loaded values are restored and both are disabled |
| AC-23 | A dirty, valid organization form | The Owner saves and receives `200` | Inputs are locked during the request; a second click sends no request; "Company settings saved" is shown; the form is pristine; the session is reloaded |
| AC-24 | An organization form or drawer with an invalid field | The user saves | No request is sent; the field shows its BR-13 message; the summary appears and focus moves to the first invalid field |
| AC-25 | A save that returns a stale `409` | The frontend handles it | The stale message and **Reload** appear with the edits kept; **Reload** fetches fresh data and clears the dirty state |
| AC-26 | Branches "Austin Central" (active), "North Austin" (inactive), "Round Rock" (active) | The user types "austin" and selects the Active filter | Only "Austin Central" is listed; a search with no match shows "No branches match these filters." and **Clear filters** |
| AC-27 | An Owner | They select **Add branch**, fill valid values and receive `201` | The drawer opened as "New branch" with the organization time zone; it closes, "{Branch name} created" is shown and the list reloads |
| AC-28 | Two active branches | The Owner selects **Deactivate** on one and confirms | The confirmation shows the deactivate copy from UI behavior; after `204` the branch shows Inactive and "{Branch name} deactivated" is shown |
| AC-29 | A dirty organization form or drawer | The user navigates away, presses Esc, or closes the drawer | "Discard unsaved changes?" appears; **Keep editing** keeps the state; **Discard** proceeds |
| AC-30 | Viewport 320px | The page renders with a drawer open and closed | One column, branch cards, full-screen drawer, sticky Save bar, no horizontal scroll |
| AC-31 | Keyboard-only use | The user tabs through the page and a drawer | Every control is reachable with visible focus; rows open with Enter or Space; the drawer traps focus and returns it to the opening row |
| AC-32 | A signed-in Viewer | They call each Manage endpoint | Every call returns `403`; no row or audit row changes |
| AC-33 | A signed-out visitor | They open `/admin/company` | The browser is on `/auth/sign-in` |
| AC-34 | `/admin/company` | The settings GET fails with `500` or a network error | "We couldn't load company settings." and **Retry** are shown; **Retry** repeats the request |
| AC-35 | `/admin/company` | The settings GET returns `403` | Only the header and "You don't have access to company settings." are shown; no form or branch list |
| AC-36 | The branch drawer | A create or update returns `409` with `errors.code` | The Code field shows "Another branch already uses this code."; the drawer stays open with the values kept |
| AC-37 | Exactly one active branch in the list | The Owner opens its row menu | **Deactivate** is disabled with the tooltip "At least one branch must stay active." |
| AC-38 | Viewport 800px | The page renders | Forms use 2 columns and branches show as a table |
| AC-39 | Viewport 1280px | The page renders | Company profile uses 3 columns; Taxes & currency and Document numbering sit side by side |
| AC-40 | An automated axe check in default, read-only, forbidden, load-error and drawer-open states | It runs | It reports no violations |
| AC-41 | An inactive branch, and an active branch in an organization with at least two active branches | The Owner deactivates the inactive one and reactivates the active one | Both return `204`; no row changes and no audit row is written |
| AC-42 | An Owner and a branch of their organization | `GET /branches/{id}` | `200` with every detail field, including `businessHours` and `updatedAt` |
| AC-43 | The branch table | The user activates the Branch column header twice | Rows sort by name ascending, then descending, and `aria-sort` changes from `ascending` to `descending` |
| AC-44 | An Owner and an existing branch | They open its row, change the name, select **Update branch** and receive `200` | The drawer opened with the branch's loaded values and the title set to its name; it closes, "{Branch name} updated" is shown and the list reloads |
| AC-45 | An inactive branch | The Owner selects **Reactivate** | No confirmation appears; after `204` the branch shows Active and "{Branch name} reactivated" is shown |
| AC-46 | A form after a failed save attempt | The user corrects an invalid field and blurs it | That field's error disappears without another save |
| AC-47 | For each BR-09 case (non-JSON `Content-Type`, body over 32 KB, malformed JSON) | It is sent to `PUT /organization-settings`, `POST /branches` or `PUT /branches/{id}` | `415`, `413` or `400` without field keys respectively; no change |
| AC-48 | A `PUT` body with `updatedAt` missing or not a timestamp | It is sent | `400` with key `updatedAt` and "Enter a valid value."; no change |
| AC-49 | An organization with exactly two active branches | Both are deactivated by concurrent requests | Exactly one returns `204` and the other `409`; one branch stays active; one audit row is written |
| AC-50 | The page or drawer with unsaved edits | A request returns `401` | The session is cleared and the browser is on `/auth/sign-in`, with no unsaved-changes prompt |
| AC-51 | Requests to every endpoint, valid and invalid, carrying emails, phones and a tax ID | Logs are inspected | No entry contains a request body, email, phone or tax ID |

## Testing requirements

Each row is a behavior group with a method budget; parameterized cases count
as one method.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Organization and branch validators (parameterized over BR-01/BR-03 rules and `updatedAt`); `nextInvoiceNumber` floor; audit diff that masks `email`/`phone`. ≤4 methods | AC-07, AC-15, AC-16, AC-48 |
| Backend integration — organization settings | GET owner/viewer (`canManage`); PUT success with normalization and audit; PUT stale `409`; parameterized server validation incl. `updatedAt`; parameterized BR-09 request errors; log inspection. ≤6 methods | AC-01, AC-02, AC-06, AC-07, AC-08, AC-47, AC-48, AC-51 |
| Backend integration — branches | List ordering and Viewer scoping; detail GET; create with audit and `Location`; duplicate code (create and update, parameterized); branch limit; update audit masking; deactivate/reactivate including no-op repeats; last active `409` including the concurrent race. ≤8 methods | AC-09, AC-10, AC-12, AC-13, AC-14, AC-16, AC-17, AC-18, AC-41, AC-42, AC-49 |
| Authorization/tenant isolation | Parameterized role matrix: Viewer on each Manage endpoint `403`, other roles `403`, no session `401`; one representative cross-organization branch test across GET/PUT/deactivate/reactivate `404`; body `organizationId` ignored. Always required, not reduced by budgets | AC-03, AC-04, AC-05, AC-11, AC-32 |
| Frontend component/service | Page states (loading, load error + Retry, forbidden, read-only); organization form dirty/discard/save/lock/session reload and blur revalidation; client validation and summary focus; stale `409` and `401` handling; branch search/filter/sort; drawer create/edit and `409` code mapping; deactivate/reactivate and last-active disable; unsaved-changes guard; overview link and `authGuard` redirect. ≤8 methods | AC-19 to AC-29, AC-33 to AC-37, AC-43 to AC-46, AC-50 |
| Browser (final audit) | Owner opens `/admin/company` from `/overview`, opens a branch drawer and cancels (no data-changing submission against the local database); 320/800/1280 px layouts; keyboard pass; axe checks. 1 scenario | AC-19, AC-30, AC-31, AC-38, AC-39, AC-40 |

## Dependencies

- `specs/sign-in/spec.md`: cookie session, `authGuard`, `/overview`
  (AUDITED).
- `specs/organization-onboarding/spec.md`: validation rules, supported
  currency/country lists, business hours validator and editor, form-field and
  error-summary components (AUDITED).
- Seeded roles `owner` and `viewer`: present (schema seed and migrations).
- Tables `organizations`, `branches`, `organization_users`,
  `organization_user_branches`, `invoices`, `audit_logs`: migrated.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Authorization uses ASP.NET Core named policies with a requirement handler that reads the validated session's role. |
| AS-02 | Endpoint paths follow the existing no-prefix routing (production reaches them under `/api` through the proxy). |
| AS-03 | Validation reuses the onboarding FluentValidation rules and `BusinessHoursValidator` without duplicating their logic; key prefixes change as needed. |
| AS-04 | The onboarding components (form-field, error-summary, business-hours-editor) and data lists are reused inside `features/organizations` without moving to `shared/`. |
| AS-05 | Time zone options in the frontend come from `Intl.supportedValuesOf('timeZone')`, as in onboarding. |
| AS-06 | The frontend does not pre-check branch code uniqueness; the server `409` is authoritative. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Permission codes | Role-based policies / seed permissions (migration) | Yes | Role-based policies `CompanySettingsView` (`owner`, `viewer`) and `CompanySettingsManage` (`owner`); permissions table has no seeds (2026-09-27) |
| OD-02 | Other roles | In-page `403` / guard redirect | Yes | API `403` + in-page forbidden state (2026-09-27) |
| OD-03 | Viewer experience | Read-only page / forbidden | Yes | Read-only page (2026-09-27) |
| OD-04 | UI edit signal | `canManage` flag / `role.code` guard | Yes | `canManage` in the settings GET; session contract unchanged (2026-09-27) |
| OD-05 | Viewer branch visibility | All branches / own branches | Yes | Own branches per membership access (BR-11) (2026-09-27) |
| OD-06 | Inactive user, membership, organization | Existing session check / extra behavior | Yes | Existing session check only (2026-09-27) |
| OD-07 | Unsupported design fields | Drop / other | Yes | Drop all fields without schema support (2026-09-27) |
| OD-08 | Company address and country | Drop / show first branch read-only | Yes | Drop from Company profile (2026-09-27) |
| OD-09 | Main branch | Derived / none + last-active rule / both | Yes | No main branch; last active branch cannot be deactivated (2026-09-27) |
| OD-10 | Team column | Drop / technician count | Yes | Drop (2026-09-27) |
| OD-11 | Deactivation with operational records | Allow / block | Yes | Allow; history preserved (2026-09-27) |
| OD-12 | Editable sequences | All / prefixes + invoice / prefixes only | Yes | Prefixes + next invoice number with a floor (2026-09-27) |
| OD-13 | Currency/time zone restrictions | Allow with note / block | Yes | Allow with a warning note; no dialog (2026-09-27) |
| OD-14 | Branch code | Unique, editable / unique, immutable | Yes | Unique per organization, 1–8, editable, `409` field error (2026-09-27) |
| OD-15 | Branch required fields | Onboarding set / minimal | Yes | Onboarding set (2026-09-27) |
| OD-16 | Concurrent edits | `updatedAt` check / last write wins | Yes | `updatedAt` optimistic check, `409` (2026-09-27) |
| OD-17 | Repeat deactivate/reactivate | `409` / idempotent `204` | Yes | Idempotent `204`, no audit (2026-09-27) |
| OD-18 | Audit rows | Proposed actions and payload | Yes | Confirmed as BR-12 (2026-09-27) |
| OD-19 | App shell | Page header only / minimal shell / full shell + primeicons | Yes | Page header and in-page links; overview link; shell is a follow-up spec (2026-09-27) |
| OD-20 | Drawer | Overlay at all widths / docked ≥1440px | Yes | Overlay at all widths, full screen below `md` (2026-09-27) |
| OD-21 | Save scope | One organization save + per-branch save / per card | Yes | One organization save; branches from the drawer (2026-09-27) |
| OD-22 | Branch search and filter | Client-side, 100 cap / server-side | Yes | Client-side over the full list; 100-branch cap (2026-09-27) |
| OD-23 | Feature location | `features/organizations` / `features/branches` + `shared/` | Yes | `features/organizations`, second lazy route (2026-09-27) |
| OD-24 | Unsaved-changes guard | Route + drawer / + `beforeunload` | Yes | Route leave and dirty drawer close only (2026-09-27) |
| OD-25 | Toast/confirm providers | Page-level / `app.config.ts` | Yes | Page-level providers (2026-09-27) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-19, AC-33 |
| FR-02 | AC-02, AC-03, AC-04, AC-05, AC-32 |
| FR-03 | AC-01, AC-02 |
| FR-04 | AC-06, AC-07, AC-08, AC-47, AC-48 |
| FR-05 | AC-09, AC-10 |
| FR-06 | AC-10, AC-42 |
| FR-07 | AC-12, AC-13, AC-14, AC-15, AC-47 |
| FR-08 | AC-08, AC-13, AC-15, AC-16, AC-47, AC-48 |
| FR-09 | AC-17, AC-18, AC-41, AC-49 |
| FR-10 | AC-10, AC-11 |
| FR-11 | AC-06, AC-12, AC-16, AC-17, AC-18, AC-41 |
| FR-12 | AC-20, AC-21, AC-34, AC-35 |
| FR-13 | AC-22, AC-23, AC-24, AC-25, AC-46 |
| FR-14 | AC-26, AC-43 |
| FR-15 | AC-24, AC-27, AC-36, AC-44 |
| FR-16 | AC-28, AC-37, AC-45 |
| FR-17 | AC-29 |
| FR-18 | AC-30, AC-31, AC-38, AC-39, AC-40 |
| FR-19 | AC-50 |
| FR-20 | AC-51 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-27 | — → DRAFT     | Created |
| 2026-09-27 | DRAFT → DRAFT | Fixed validation findings F1–F4, W2, W3 (option a): split AC-02, AC-17, AC-19, AC-20, AC-27, AC-28, AC-30, AC-31 into single behaviors (added AC-32 to AC-41); added AC-42 to AC-46 (branch detail, sort, drawer edit, reactivate UI, blur revalidation), AC-47 to AC-49 (BR-09 errors, `updatedAt` validation, concurrent last-branch deactivation), FR-19/AC-50 (`401` handling), FR-20/AC-51 (logging); audit mask value `"[changed]"`; 51 ACs justified by authorization, tenancy, concurrency and deactivation-integrity coverage |
| 2026-09-27 | DRAFT → APPROVED | Approved by user via /spec approve |
