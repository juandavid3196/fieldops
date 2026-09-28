# Design 17 — Company setup completion

| Field    | Value                                   |
| -------- | --------------------------------------- |
| Feature  | `design-17-company-setup-completion`    |
| Type     | Full-stack                              |
| Status   | APPROVED                                |
| Created  | 2026-09-28                              |
| Updated  | 2026-09-28                              |
| Approved | 2026-09-28                              |

## Context and objective

Company setup (`/admin/company`) and the authenticated shell are implemented
as reduced adaptations of design 17: no navy sidebar, no full navigation, no
Administration column, and no logo, website, company address, default country,
prices-include-tax, main branch, team count, service ZIP codes, company billing
inheritance or sequence editing. This DESIGN SLICE completes screen 17 exactly
as defined by the approved Company Setup handoff. Every visible business field
is backed by real API data and persistence, and every future module shares one
"Coming soon" page. Success means the screen matches the rendered handoff side
by side at desktop and mobile widths, while tenant isolation, backend
authorization and audit behavior stay as strict as the existing features.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (role `owner`) | Everything in `company-settings-and-branches` plus: edit the new organization fields, upload and remove the logo, edit document sequences, confirm a currency change, set the main branch, edit service ZIP codes and company billing inheritance; use the full shell navigation | Deactivate the main branch; make an inactive branch main; supply an organization identifier; reach another organization's data |
| Viewer (role `viewer`) | Open `/admin/company` read-only, including the logo, the Team column, branch drawer and a read-only sequences dialog; use the full shell navigation | Any mutation, including logo and set-main endpoints (`403`) |
| Other roles (`dispatcher`, `technician`, `accounting`, `operations_manager`, unknown) | Use the shell; open module items and Coming soon pages; open `/admin/company` by URL and see the existing forbidden state | See the Administration item; read or change company settings, logo or branches (`403`) |
| Unauthenticated visitor | — | Any endpoint (`401`); any shell route redirects to `/auth/sign-in` |
| System | Resolve organization, user and membership from the validated session; derive the logo MIME type from file content; write audit rows | Trust client organization identifiers, client MIME types or file extensions |

## Scope

- Handoff-faithful shell: navy sidebar with grouped navigation, brand, active
  indicator and Collapse; top bar with search field, organization button,
  Help, notifications bell and user menu; tablet rail and mobile drawer.
- One shared Coming soon page for every future module and top-bar
  destination.
- Administration column (224px) with the seven handoff items; horizontal link
  row below 1100px.
- Company setup rebuilt to the handoff: page header, read-only banner,
  Company profile (logo, legal/display names, website, business email, phone,
  tax ID, 4-part company address, default country, default time zone),
  Branches card, Taxes & currency (including Prices include tax, currency
  warning, Manage tax rates link) and Document numbering (including Edit
  sequences dialog) side by side.
- Logo upload, replacement, removal and retrieval with content validation.
- Currency-change confirmation when invoices exist.
- Main branch indicator, Set as main branch action and main-branch
  deactivation rule.
- Team column (active technician count), service ZIP codes and "Use company
  billing settings" in the branch drawer.
- Branch drawer rebuilt to the handoff: docked at ≥1440px, overlay below,
  full screen below 768px.
- Schema amendment, EF migration and backfill; registration creates its first
  branch as main.
- New `primeicons` dependency, self-hosted Inter (with OFL license), handoff
  `--fo-*` tokens and the 1100px/1440px breakpoints; frontend docs updated to
  match.
- Owner edit and Viewer read-only behavior for every new control.
- Desktop and mobile visual comparison against the rendered handoff.

## Non-goals

- Implementing any future module (Requests, Quotes, Work orders, Schedule,
  Customers, Team, Products and services, Invoices, Reports, Users &
  permissions, company-level Business hours, Notifications, Help, global
  search, tax rates). They only reach the Coming soon page.
- Notification counts, search results, organization switching or any
  placeholder operational data.
- Named tax rates or a `tax_rates` table ("Manage tax rates" → Coming soon).
- Branch-level billing overrides when "Use company billing settings" is off;
  routing by service ZIP codes.
- Customer-facing use of the logo (documents, portal).
- Persisting the sidebar collapse state beyond the current page visit.
- Copying the handoff HTML/JavaScript into Angular, or reproducing sample
  values from the screenshot or handoff.
- File storage services other than the database table defined here.
- Applying the migration to any database.

## User flow

1. A signed-in user opens a shell route; `authGuard` revalidates the session
   and the shell renders with navigation filtered by BR-01.
2. The user selects a future module, Help, the bell or submits search; the
   Coming soon page for that destination renders inside the shell.
3. An Owner opens Administration (`/admin/company`). The page loads settings,
   the logo (when present) and branches, with skeletons meanwhile.
4. The Owner uses the Administration column to jump to a section, edits
   profile, taxes and numbering fields, optionally edits sequences in the
   dialog, and selects **Save changes**. If the currency changed and invoices
   exist, a confirmation appears first.
5. The Owner selects **Change logo**, picks a file; it uploads immediately and
   the preview updates. **Remove logo** deletes it.
6. The Owner opens a branch row or **Add branch**; the drawer opens docked
   (≥1440px) or as an overlay, edits details, ZIP codes, billing inheritance
   and business hours, and saves.
7. The Owner selects **Set as main branch** on an active branch; the tag
   moves to it.
8. A Viewer sees the same screen read-only.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The shell sidebar must render the BR-01 navigation with the handoff structure: brand, grouped items with icons, divider, Administration, and a Collapse control. The item matching the current route has `aria-current="page"`. Collapse toggles between the full (256px) and rail (72px) sidebar for the current page visit only. |
| FR-02 | Responsive shell: below 768px the sidebar is a modal left drawer opened from a top-bar menu button (existing shell behavior); from 768px to 1099px the sidebar starts as the 72px rail; from 1100px it starts full. In the rail, items show only icons with their label as accessible name and tooltip, and group headings are hidden. |
| FR-03 | The frontend must serve `/coming-soon/:module` inside the shell for every BR-02 slug, showing h1 "{Module}", the text "{Module} isn't available yet." and a **Back to Overview** link. An unknown slug redirects to `/overview`. The page makes no API request and shows no data. |
| FR-04 | The top bar must show, from 768px: the search field (Enter navigates to `/coming-soon/search`, no suggestions), the organization button with the session organization name (its menu lists only that organization, marked current, and triggers no request), Help (`/coming-soon/help`), the bell without a badge (`/coming-soon/notifications`) and the existing user menu. Below 768px it follows the existing shell rules plus the bell; search, organization button and Help are hidden. |
| FR-05 | Company setup must render the Administration column (224px, from 1100px) with the BR-03 items. Anchor items scroll to their section and become current (`aria-current="location"`; Company profile by default). Other items navigate to their Coming soon page. Below 1100px the same items render as a horizontal, scrollable link row above the page header. |
| FR-06 | The page must follow the handoff layout, order and copy (BR-12): breadcrumb, header with actions, read-only banner, Company profile, Branches, then Taxes & currency and Document numbering side by side where width allows. |
| FR-07 | `GET` and `PUT /organization-settings` must include the BR-04 fields, `hasInvoices` and `logo`, validating and normalizing them per BR-04 and BR-05 with the same concurrency, audit, logging and error rules as `company-settings-and-branches`. |
| FR-08 | When a `PUT` changes `currency` and the organization has at least one invoice, the server must require `confirmCurrencyChange = true`, otherwise return `409` with `errors.currency` (BR-06). The frontend asks for confirmation before sending such a save and resends after confirmation when the server reports the conflict. |
| FR-09 | The backend must provide logo retrieval, upload/replace and removal for the session organization per BR-07 and BR-08, and serve it with the BR-08 headers. Logo operations do not change `organizations.updated_at`. |
| FR-10 | The Company profile must show the current logo (or the "No logo" placeholder), **Change logo** and, when a logo exists, **Remove logo**. Choosing a file validates type and size client-side, uploads immediately with progress, updates the preview and shows the BR-13 toast. Logo actions do not dirty the organization form. |
| FR-11 | **Edit sequences** must open a dialog with next quote, work order and invoice numbers. **Apply** copies valid values into the organization form (dirty); **Cancel** discards. Values persist only with **Save changes**. For read-only users the link reads **View sequences** and the dialog is read-only with only **Close**. |
| FR-12 | **Manage tax rates** must navigate to `/coming-soon/tax-rates`. **Prices include tax** is a switch bound to `pricesIncludeTax`. |
| FR-13 | `GET /branches` must return `isMain` and `technicianCount` (BR-10). The table shows the "Main branch" tag, Team as "1 technician" / "{n} technicians", Time zone as its generic name, and the selected row highlighted while the drawer is open. Cards below 768px show name, Main branch tag, address, status and actions. |
| FR-14 | Branch detail, create and update must include `servicePostalCodes` and `usesCompanyBilling` (BR-09), plus `isMain` in the detail body, with the existing validation, concurrency and audit rules. |
| FR-15 | `POST /branches/{id}/set-main` must make an active branch the organization's only main branch per BR-11. The row menu offers **Set as main branch** for active non-main branches (toast BR-13). **Deactivate** is disabled for the main branch (row menu and drawer) with the BR-11 tooltip; the server rejects it with `409`. |
| FR-16 | The branch drawer must follow the handoff: 420px wide, header (title, status tag, close), Details section (name, code + helper, contact phone, contact email, time zone, service ZIP codes + helper, billing switch + helper), divider, Business hours section, info note, and footer (Deactivate/Reactivate, Cancel, Create/Update). From 1440px it is docked beside the content without mask or focus trap; from 768px to 1439px it overlays below the top bar with a mask that closes it (dirty guard applies); below 768px it is full screen. |
| FR-17 | For `canManage = false`, every new control is read-only: no Change/Remove logo, Set as main branch, Save/Discard, Add branch; switches disabled; sequences dialog read-only; drawer footer shows only **Close**. |
| FR-18 | The schema amendment and migration must add the BR-14 structures, backfill one main branch per organization, and registration must create its first branch with `is_main = true`. |
| FR-19 | Every new successful change must write exactly one audit row per BR-15; no-ops and errors write none. |
| FR-20 | The implementation must match the rendered handoff per BR-16 at 1440×900 and the handoff's mobile rules at 390×844, using handoff tokens, PrimeIcons and Inter, with dynamic data only. |
| FR-21 | The shell, Administration column, dialogs and drawer must meet the accessibility rules in UI behavior. |

## Business and validation rules

Rules of `company-settings-and-branches` (BR-01 to BR-16) and
`authenticated-app-shell` remain in force except where superseded here (see
Dependencies). Text is trimmed; whitespace-only counts as empty.

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Sidebar items, in order: **Overview** (`/overview`); group **Operations**: Requests, Quotes, Work orders, Schedule; group **People**: Customers, Team; group **Finance**: Products and services, Invoices, Reports; divider; **Administration** (`/admin/company`). Module items are visible to every signed-in user and link to their BR-02 page. Administration is visible only to `owner` and `viewer` (UX only; backend authoritative). Icons: `pi-th-large, pi-inbox, pi-file-edit, pi-wrench, pi-calendar, pi-users, pi-id-card, pi-box, pi-receipt, pi-chart-bar, pi-cog`. | Frontend |
| BR-02 | Coming soon slugs → module name: `requests` Requests, `quotes` Quotes, `work-orders` Work orders, `schedule` Schedule, `customers` Customers, `team` Team, `products-and-services` Products and services, `invoices` Invoices, `reports` Reports, `users-and-permissions` Users & permissions, `business-hours` Business hours, `notifications` Notifications, `help` Help, `search` Search, `tax-rates` Tax rates. | Frontend |
| BR-03 | Administration column items, in order: Company profile (anchor), Branches (anchor), Business hours (`business-hours`), Users & permissions (`users-and-permissions`), Taxes & currency (anchor), Document numbering (anchor), Notifications (`notifications`). Heading "Administration"; landmark label "Administration". | Frontend |
| BR-04 | New organization fields (request/error keys): `website` (optional, ≤255, `https?://` optional, a host with at least one dot and no spaces; stored as typed after trim; "Enter a valid website." / "Use 255 characters or fewer."); `addressLine1` (required, ≤180), `city` (required, ≤100), `postalCode` (required, ≤30), `countryCode` (required, onboarding supported-country list, uppercased) — same messages as onboarding first-branch fields; `stateRegion` (when `countryCode = US`: required, one of the 50 states + `DC`, "Select a state."; otherwise optional ≤100); `pricesIncludeTax` (required boolean); `nextQuoteNumber`, `nextWorkOrderNumber` (integer 1 to 999,999,999,999, same messages as `nextInvoiceNumber`); `confirmCurrencyChange` (optional boolean, default `false`, not stored). | Both |
| BR-05 | Server floors: `nextQuoteNumber` > highest `quotes.quote_number` of the organization ("Enter a number greater than the last quote number."); `nextWorkOrderNumber` > highest `work_orders.work_order_number` ("Enter a number greater than the last work order number."); `nextInvoiceNumber` unchanged rule. Any value ≥1 when none exist. | Backend |
| BR-06 | Currency confirmation: when `currency` differs from the stored value, the organization has ≥1 invoice and `confirmCurrencyChange` is not `true` → `409` with `errors.currency` "Confirm the currency change." and no change. Frontend dialog: "Change currency to {code}? Existing invoices keep their original currency." → **Change currency** / **Cancel**. | Both |
| BR-07 | Logo file: `multipart/form-data` field `file`; ≤2 MB (2,097,152 bytes) else `errors.file` "Choose a file of 2 MB or smaller."; type detected from content only: PNG signature, JPEG signature, or SVG per BR-08; anything else, or a detected type not matching the declared part type, → `errors.file` "Choose a JPG, PNG or SVG file.". Request body >3 MB → `413`; non-multipart → `415`. Client mirrors size and extension/type checks before uploading. | Both |
| BR-08 | SVG acceptance: well-formed XML parsed with DTD processing prohibited; no DOCTYPE; root element `svg` in the SVG namespace; no `script`, `foreignObject`, `iframe`, `embed` or `object` elements; no attribute whose name starts with `on`; `href`/`xlink:href` only as `#fragment`; no `url(`/`@import` pointing outside the document in `style` elements or attributes. Failure → `errors.file` "Choose a JPG, PNG or SVG file.". Logo responses carry the stored MIME type, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox`, `Content-Disposition: inline` and `Cache-Control: no-store`. The frontend displays the logo only through an `<img>` element. | Both |
| BR-09 | Branch fields: `servicePostalCodes` (array of strings; POST optional, default `[]`; PUT required); each trimmed, empties dropped, duplicates removed keeping first order; each 1–30 of letters, digits, spaces or hyphens ("Enter valid ZIP codes separated by commas."); at most 200 ("Enter up to 200 ZIP codes."); key `servicePostalCodes`. UI input is comma-separated text. `usesCompanyBilling` (boolean; POST optional, default `true`; PUT required). A required `servicePostalCodes` or `usesCompanyBilling` that is missing or of the wrong JSON type → `400` with that key and "Enter a valid value.". | Both |
| BR-10 | `technicianCount` = number of `technician_profiles` of the session organization with that `branch_id` and `status = 'active'`. | Backend |
| BR-11 | Main branch: exactly one per organization after migration. Set-main on an active non-main branch clears the previous main and sets the target in one transaction that locks the organization's branch rows (the same lock as deactivation). Already main → `204` no-op without audit. Inactive target → `409` without field errors ("Only an active branch can be the main branch."). Deactivating the main branch → `409` without field errors ("The main branch can't be deactivated."; also the disabled-action tooltip). This rule subsumes the last-active-branch rule, whose server check stays. | Both |
| BR-12 | Copy adopted from the handoff: breadcrumb "Administration / Company"; description "Manage your organization details, branches, billing defaults, and document numbering."; read-only banner "You have view-only access to company settings. Contact an Owner to request changes."; branch load error "We couldn't load branches. Check your connection and try again."; logo helper "JPG, PNG or SVG. Max 2 MB."; no-logo placeholder "No logo"; tax switch "Prices include tax" + "Show prices with tax included on quotes and invoices."; currency note "Changing the currency after invoices exist requires confirmation and may affect historical records."; numbering helper "Prefixes appear before the sequential number, for example INV-1049." (literal example); ZIP helper "Separate ZIP codes with commas."; billing switch "Use company billing settings" + "Inherit tax rates, currency, and document numbering from company settings."; drawer note "Deactivating a branch prevents new records. All historical data is preserved."; search placeholder "Search requests, customers, work orders…". Sequences dialog title "Edit sequences" / "Sequences" (read-only), labels "Next quote number", "Next work order number", "Next invoice number". | Frontend |
| BR-13 | Toasts: "Logo updated", "Logo removed", "{Branch name} is now the main branch", plus the existing ones. Logo upload/remove failures show `ApiError.message` or the field message under the logo control. | Frontend |
| BR-14 | Schema amendment (approved): `organizations` + `website varchar(255)`, `address_line1 varchar(180)`, `city varchar(100)`, `state_region varchar(100)`, `postal_code varchar(30)`, `country_code char(2)` (all nullable), `prices_include_tax boolean NOT NULL DEFAULT false`. New `organization_logos (organization_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE CASCADE, content_type varchar(40) NOT NULL CHECK (content_type IN ('image/png','image/jpeg','image/svg+xml')), content bytea NOT NULL, size_bytes integer NOT NULL CHECK (size_bytes BETWEEN 1 AND 2097152), created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now())`. `branches` + `is_main boolean NOT NULL DEFAULT false`, `service_postal_codes varchar(30)[] NOT NULL DEFAULT '{}'`, `uses_company_billing boolean NOT NULL DEFAULT true`, `CHECK (NOT is_main OR is_active)`, `CREATE UNIQUE INDEX ux_branches_org_main ON branches(organization_id) WHERE is_main`. Backfill: per organization, the oldest active branch by `(created_at, id)` becomes main. An organization without an active branch (impossible under the base BR-06) makes the migration fail; it never changes branch status. | Backend |
| BR-15 | Audit (BR-12 of the base spec applies): new organization fields appear in `organization.settings_updated` diffs by request key (`confirmCurrencyChange` never); `organization.logo_updated` / `organization.logo_removed` (`entity_type` `organization`, `after_data`/`before_data` `{ contentType, sizeBytes }`, never content); `branch.set_as_main` (`entity_type` `branch`, target branch, `before_data {isMain:false}`, `after_data {isMain:true}`, `metadata { previousMainBranchId }`); branch diffs include `servicePostalCodes` and `usesCompanyBilling`. Logo removal with no logo → `204` without audit. | Backend |
| BR-16 | Visual acceptance at 1440×900 against the rendered handoff (same theme, drawer open), tolerance ±2px: sidebar 256px, top bar 64px, Administration column 224px, docked drawer 420px; content padding 20/24px; cards white, 1px border, radius 10px, padding 20/24px; controls 40px (table actions 32px), radius 6px; type scale h1 28/700, h2 20/700, h3 16/600, body 14, label 13/600, helper 12; handoff colors via `--fo-*` tokens for navy, surfaces, text, borders, primary, statuses and focus ring; form grids (profile auto-fit min 200px, address 4-part, taxes/numbering auto-fit min 340px); table columns, selected-row treatment and status tags; drawer sections and footer. Values may differ; structure, dimensions, colors and states may not. | Frontend |
| BR-17 | Logging: logo bytes, website and address values are never logged (base BR-14 extended). | Backend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Branch `is_main = false`, active | `is_main = true` (previous main → `false`) | `POST /branches/{id}/set-main` | Owner | Target active (BR-11) |
| No logo | Logo stored | `PUT /organization-settings/logo` | Owner | BR-07, BR-08 |
| Logo stored | Logo replaced / removed | `PUT` / `DELETE /organization-settings/logo` | Owner | BR-07, BR-08 |

Existing branch activation transitions are unchanged, with one added guard on
`is_active = true → false`: the branch must not be the main branch (BR-11,
`409` otherwise).

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `organizations` (existing columns of the base spec plus
  `next_quote_number`, `next_work_order_number`), `branches`,
  `organization_users`, `organization_user_branches`, `roles`, `invoices`
  (existence and `MAX(invoice_number)`), `quotes` (`MAX(quote_number)`),
  `work_orders` (`MAX(work_order_number)`), `technician_profiles`
  (`branch_id`, `status`, `organization_id` count), `audit_logs` (insert).
- Schema amendments required: BR-14. Status: **approved by the user
  2026-09-28** (OD-10, OD-12, OD-14, OD-15, OD-22). Implementation updates
  `docs/database/fieldops-schema.sql`, the EF model and generates one
  migration (with backfill) under `--generate-migration`; the migration is
  never applied by agents. Persistence changes require `database-reviewer`.
- Domain changes: `Organization` settings update accepts the new fields;
  `Branch` gains `IsMain`, `ServicePostalCodes`, `UsesCompanyBilling`,
  set-main and main-deactivation guard; registration creates the first branch
  as main; new `OrganizationLogo` entity.

## Tenant isolation and authorization

- Organization context: session `OrganizationId`, `UserId`, `MembershipId`;
  role read from the membership on every request (unchanged).
- Client-provided organization identifiers: none accepted. Logo endpoints
  have no identifier and act only on the session organization. Branch ids are
  looked up with the session `OrganizationId` and BR-11 (base) access.
- Another organization's branch on `set-main`: `404`, no change. Another
  organization's logo is unreachable; a session organization without a logo
  gets `404` on `GET`.
- Aggregates (`technicianCount`, `hasInvoices`, sequence floors) are
  filtered by the session `OrganizationId`.
- Permissions: `GET /organization-settings/logo` → CompanySettingsView;
  `PUT`/`DELETE /organization-settings/logo` and `POST /branches/{id}/set-main`
  → CompanySettingsManage. Frontend BR-01 visibility and read-only UI are UX
  only.
- Upload safety: BR-07/BR-08 (authorization, size, content type, SVG
  sanitization rules, response headers).
- CSRF: existing `SameSite=Strict` session cookie and CORS allow-list, as in
  the base spec.

## API contracts

Contract status: Final. Existing contracts of `company-settings-and-branches`
apply; this table lists changed and new rows. All authenticated responses
carry `Cache-Control: no-store`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/organization-settings` | — | `200` base body + `website, addressLine1, city, stateRegion, postalCode, countryCode, pricesIncludeTax, nextQuoteNumber, nextWorkOrderNumber, hasInvoices, logo: { contentType, sizeBytes, updatedAt } \| null` | `401`, `403` | View |
| PUT | `/organization-settings` | base fields + BR-04 fields + optional `confirmCurrencyChange` | `200` GET body | base errors · `400` BR-04/BR-05 keys · `409` `errors.currency` (BR-06) | Manage |
| GET | `/organization-settings/logo` | — | `200` image bytes, BR-08 headers | `401`, `403`, `404` none | View |
| PUT | `/organization-settings/logo` | `multipart/form-data`, field `file` | `200` `{ contentType, sizeBytes, updatedAt }` | `400` `errors.file` · `401` · `403` · `413` · `415` | Manage |
| DELETE | `/organization-settings/logo` | — | `204` (also when none) | `401`, `403` | Manage |
| GET | `/branches` | — | `200` items + `isMain, technicianCount` | unchanged | View |
| GET | `/branches/{id}` | — | `200` detail + `isMain, servicePostalCodes, usesCompanyBilling` | unchanged | View |
| POST | `/branches` | base fields + optional `servicePostalCodes`, `usesCompanyBilling` | `201` detail (`isMain = false`) | base errors · `400` `servicePostalCodes` | Manage |
| PUT | `/branches/{id}` | base fields + required `servicePostalCodes`, `usesCompanyBilling` | `200` detail | base errors · `400` `servicePostalCodes` | Manage |
| POST | `/branches/{id}/deactivate` | — | `204` | base errors · `409` main branch (BR-11) | Manage |
| POST | `/branches/{id}/set-main` | No body | `204` | `401` · `403` · `404` · `409` inactive (BR-11) | Manage |

- The frontend distinguishes `409` cases by endpoint and field keys
  (`errors.currency`, `errors.code`, none).

## UI behavior and states

Design references (approved; the handoff is the definitive visual and
interaction target and its §6 corrections prevail over the screenshot):

- `design/identity-access/handoff/Company Setup.dc.html` with `support.js`
  and `image-slot.js` (reference implementation; rebuilt in Angular/PrimeNG,
  never copied)
- `design/identity-access/handoff/Identity-and-Organization-Handoff.md` §2,
  §4, §5, §6, §8
- `design/identity-access/screens/17-design.png` (secondary reference)

Adaptations to the handoff (only where approved decisions require them):
**Remove logo** text button beside **Change logo** when a logo exists; "No
logo" placeholder (building icon) when none; the bell has no badge;
organization button menu with one current entry; brand uses the existing
FieldOps brand mark (handoff §4 `BrandLogo`), not the prototype gear;
Manage tax rates → Coming soon; Edit sequences dialog; Set as main branch
row item; New branch drawer per handoff §2.7.

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Shell | N/A (guard resolves session first) | N/A | Existing sign-out error | BR-01 filtered Administration | Navigation |
| Coming soon | N/A (no request) | N/A | Unknown slug → `/overview` | Visible to all signed-in users | Module page |
| Company setup | Profile and logo skeletons, 3 table skeleton rows; save/upload spinners with inputs locked | Existing branch empty/no-match states | Existing settings and branch load errors (BR-12 copy); logo load failure shows the "No logo" placeholder; upload errors under the logo control | Existing forbidden state; FR-17 read-only | BR-13 toasts |
| Sequences dialog | N/A (form values) | N/A | Field messages on Apply. A save `400` for `nextQuoteNumber` or `nextWorkOrderNumber`: the error summary lists the message with a link that opens the dialog; when such a key is the first invalid one, focus moves to **Edit sequences**; the opened dialog shows the server message on that field. A `nextInvoiceNumber` error shows on the card field and in the dialog | Read-only for Viewer | Values applied, form dirty |
| Currency confirmation | Save spinner after confirm | N/A | Cancel: no request, values kept | Owner only | Save proceeds |
| Branch drawer | Existing | N/A | Existing + `servicePostalCodes` field errors; set-main/deactivate `409` messages as toasts | Read-only: only Close | Existing |

- Responsive (existing `md` 768px plus new 1100px and 1440px breakpoints,
  AS-08): below 768px one-column forms, branch cards, sticky
  Save/Discard bar with safe-area padding, full-screen drawer, no horizontal
  scroll at 320px, touch targets ≥44px; 768–1099px rail sidebar, link row,
  overlay drawer; 1100–1439px full sidebar, Administration column, overlay
  drawer; ≥1440px docked drawer. Main column never narrower than 320px.
- Accessibility: landmarks "Main navigation", "Administration",
  "Breadcrumb"; group headings are labels, not links; rail items expose
  labels; overlay and full-screen drawers are modal dialogs that trap focus
  and return focus to the originating row; the docked drawer is a labelled
  non-modal region, Esc still closes it (dirty guard); switches have
  accessible names ("Prices include tax", "Use company billing settings",
  "{Day} open"); logo `<img>` alt "{Display name} logo"; status and Main
  branch tags use text, never color alone; visible focus ring per handoff.
- Styling: handoff tokens as `--fo-*` app tokens with dark values (AS-08);
  PrimeIcons 7; Inter 400/500/600/700 self-hosted with OFL license;
  no `::ng-deep` or global `.p-*` overrides beyond documented token mapping.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| New org field invalid (client or `400`) | BR-04/BR-05 messages on fields and summary | None |
| Currency changed with invoices, not confirmed | `409` `errors.currency`; frontend opens the BR-06 dialog | None |
| Logo wrong type, content mismatch, unsafe SVG, >2 MB | "Choose a JPG, PNG or SVG file." / "Choose a file of 2 MB or smaller." under the control | None |
| Logo body >3 MB / non-multipart | `413` / `415`; `ApiError.message` under the control | None |
| `servicePostalCodes` invalid | Field message in the drawer | None |
| Set main on inactive branch | `409`; toast "Only an active branch can be the main branch." | None |
| Deactivate main branch | `409`; toast "The main branch can't be deactivated." | None |
| Set main on unknown/other-org branch | `404`; existing branch-unavailable handling | None |
| `401` on any new request | Existing session-cleared redirect | None |
| `403` on a new mutation | `ApiError.message`; values kept | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A signed-in `owner` at 1440px | `/admin/company` renders | The sidebar shows BR-01 groups, items and icons in order with Administration marked `aria-current="page"`; the top bar shows search, organization button with the session organization name, Help, a bell without badge and the user menu |
| AC-02 | Roles `owner`, `viewer`, `dispatcher`, `technician`, `accounting`, `operations_manager`, unknown (parameterized) | The shell renders | Module items are visible for all; Administration only for `owner` and `viewer` |
| AC-03 | A signed-in user | They select a module item, Help, the bell, or press Enter in search (parameterized), open an unknown slug, and open the organization button menu | Each destination lands on its BR-02 Coming soon page with the module h1, text and Back to Overview, no API request, and the module item active where applicable; the unknown slug redirects to `/overview`; the organization menu lists only the session organization marked current, and selecting it sends no request and changes nothing |
| AC-04 | Removed 2026-09-28: merged into AC-03 (top-bar destinations) | — | — |
| AC-05 | Viewports 390, 800 and 1280px (parameterized) | The shell renders and the user toggles Collapse where shown | 390: menu button + modal drawer; 800: 72px rail with icon-only items having accessible names and tooltips; 1280: 256px sidebar; Collapse switches 256↔72px and resets on reload |
| AC-06 | `/admin/company` at 1280px and at 800px | The user uses the Administration items | 1280: 224px column with the BR-03 items; anchors scroll and set `aria-current="location"`; Business hours, Users & permissions and Notifications open their Coming soon pages. 800: the same items as a horizontal scrollable row |
| AC-07 | An Owner and a valid body with the BR-04 fields and current `updatedAt` | `GET` then `PUT /organization-settings` | `GET` returns the new fields, `hasInvoices` and `logo`; `PUT` returns `200` with normalized values (uppercased `countryCode`, trimmed website) persisted, and one audit row listing only changed keys including `pricesIncludeTax` |
| AC-08 | For each BR-04/BR-05 rule (US state required, website format, address required, quote and work order floors), a body failing only that rule (parameterized) | `PUT /organization-settings` | `400` with that key and message; no change |
| AC-09 | An organization with one invoice, and one without (parameterized) | A `PUT` changes currency without, then with, `confirmCurrencyChange` | With invoices: `409` `errors.currency` then `200`; without invoices: `200` on the first request |
| AC-10 | An Owner who changed the currency, with `hasInvoices = true` at load, and separately with `hasInvoices = false` at load while the server returns `409` `errors.currency` (parameterized) | They select Save changes, then Cancel, then Save and **Change currency** | With `true`: the dialog appears before any request; Cancel sends no request and keeps values; confirming sends one `PUT` with `confirmCurrencyChange = true`. With `false`: the first `PUT` is sent without confirmation, the `409` opens the same dialog with values kept, and confirming resends exactly once with `confirmCurrencyChange = true` |
| AC-11 | An Owner | They upload a valid PNG, replace it with a safe SVG, fetch it, then delete it twice | Uploads return `200` metadata; `GET` returns the bytes with the stored MIME and the BR-08 headers; one audit row per upload and for the first delete; both deletes return `204`; afterwards `GET` returns `404`; no log entry contains logo bytes |
| AC-12 | Files: text renamed `.png`, PNG declared as JPEG, 2 MB + 1 byte, SVG with `script`, `onload`, `foreignObject`, external `href`, DOCTYPE; a 3 MB + 1 byte body; a JSON body (parameterized) | `PUT /organization-settings/logo` | `400` `errors.file` with the BR-07 message for each file case, `413` and `415` respectively; the stored logo is unchanged |
| AC-13 | An Owner on Company setup | They pick a `.gif`, then a 3 MB PNG, then a valid PNG, then select Remove logo | The first two show the client message with no request; the valid file shows progress, updates the `<img>` preview and shows "Logo updated" without dirtying the form; Remove shows "No logo" and "Logo removed" |
| AC-14 | Branches where one is main, with 1 and 3 active technicians plus inactive and other-organization technicians | `GET /branches` and the table renders | Items carry `isMain` and `technicianCount` counting only active same-organization technicians; the table shows the Main branch tag, "1 technician" / "3 technicians", and generic time zone names |
| AC-15 | An Owner | They create and update branches with ` 78701, 78702,78701 ` codes and `usesCompanyBilling = false`, then with 201 codes, an invalid code, and a `PUT` missing `usesCompanyBilling` (parameterized) | Valid: detail returns `["78701","78702"]` and `false`, persisted with audit diffs; invalid: `400` with the offending key and its BR-09 message, and no change |
| AC-16 | Active main A, active B, inactive C | Set-main on B, again on B, then on C | B returns `204`, B is main and A is not, one `branch.set_as_main` row with `previousMainBranchId`; repeating returns `204` without audit; C returns `409` and nothing changes |
| AC-17 | The main branch in the table and drawer | The Owner opens its row menu and drawer, then calls deactivate directly | Deactivate is disabled with "The main branch can't be deactivated." in both places; the API returns `409` and the branch stays active; an active non-main row offers Set as main branch, which shows "{Branch name} is now the main branch" |
| AC-18 | A test database with organizations having 1 and 3 branches before the migration | The generated migration runs in the test container, then a second main is inserted and a company registers | Exactly the oldest active branch per organization is main; existing branches have `uses_company_billing = true` and empty ZIP codes; the second main violates `ux_branches_org_main`; the registered company's first branch is main |
| AC-19 | An Owner, and a Viewer (parameterized) | They open the sequences link | Owner: "Edit sequences" shows the three loaded values; Apply with valid values updates the form and enables Save, Cancel restores; out-of-range values show field messages; after Save returns `400` for `nextQuoteNumber`, the error summary shows the message with a link, focus moves to **Edit sequences**, and opening the dialog shows the message on Next quote number. Viewer: "View sequences" read-only with only Close |
| AC-20 | A Viewer | The page, drawer and dialogs render, and they call the logo `PUT`/`DELETE` and set-main endpoints | Read-only banner (BR-12 copy); no Change/Remove logo, Set as main, Save/Discard or Add branch; switches disabled; drawer footer only Close; every call returns `403` with no change |
| AC-21 | Other roles and no session (parameterized); Owners of organizations A and B | They call the new endpoints; A calls set-main with B's branch id; B calls logo `GET` with no logo while A has one | Other roles `403`, no session `401`; set-main `404` with B unchanged; B gets `404`, never A's logo |
| AC-22 | An Owner opens a branch at 1440, 1280 and 390px (parameterized) | The drawer renders | 1440: docked 420px beside the content, no mask, content reflows, focus not trapped, selected row highlighted; 1280: overlay below the top bar with mask, focus trapped, mask click closes via the dirty guard; 390: full screen |
| AC-23 | An Owner selects Add branch | The drawer renders | Title "New branch" and the FR-16 sections, fields, helpers and footer in order; ZIP codes empty, company billing on, time zone = organization time zone, all days closed |
| AC-24 | The implementation at 390×844 (and 320px for overflow) | Company setup renders and is compared with the handoff §2.4 mobile rules and tokens | Sidebar drawer, one-column forms, branch cards with name, Main branch tag, address, status and menu, full-screen branch drawer and sticky Save/Discard bar; no horizontal scroll; touch targets ≥44px; tokens, typography and spacing match BR-16 |
| AC-25 | The implementation and the rendered handoff at 1440×900, drawer open | They are compared side by side | Every BR-16 item matches within tolerance; every fixed interface text (labels, helpers, notes, banners, links, headings, placeholders) matches BR-12 and the handoff exactly; only organization and branch data differ, and they are the local organization's real data |
| AC-26 | Removed 2026-09-28: merged into AC-24 (mobile comparison) | — | — |
| AC-27 | An automated axe check on the shell (full, rail, drawer), Company setup (default, read-only), sequences dialog, currency dialog, docked and overlay drawer | It runs, plus a keyboard pass | No violations; every control reachable with visible focus; focus trap and return behave per UI behavior |

## Testing requirements

25 active ACs (AC-04 and AC-26 merged). The slice spans the shell, a new
upload boundary (type/size/SVG security) and a migration with backfill; each
group is parameterized below. Existing evidence of the
base specs is reused and not recreated.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Logo content validator (PNG/JPEG signatures, SVG rule table incl. DTD/XXE, script, `on*`, `foreignObject`, external refs) parameterized; ZIP code normalization. ≤2 methods | AC-11, AC-12, AC-15 |
| Backend integration | (1) Settings GET/PUT new fields, audit and parameterized BR-04/BR-05 failures; (2) currency confirmation parameterized; (3) logo lifecycle with headers, audit and log inspection; (4) parameterized logo rejections incl. `413`/`415`; (5) branch list `isMain`/`technicianCount` and ZIP/billing create/update incl. invalid; (6) set-main and main-deactivation rules; (7) migration backfill, unique main index and registration main. 7 methods | AC-07 to AC-09, AC-11, AC-12, AC-14 to AC-18 |
| Authorization/tenant isolation | Parameterized role/no-session matrix over the new endpoints incl. Viewer `403`; representative cross-organization set-main `404` and logo isolation. Always required | AC-20 (API), AC-21 |
| Frontend component/service | (1) Shell navigation: parameterized BR-01 visibility, Coming soon routing and unknown slug, organization menu, collapse; (2) Administration column/link row anchors and Coming soon items; (3) logo UI client validation, upload, preview, remove; (4) currency confirmation (pre-send and server `409` resend), sequences dialog incl. server-error routing and read-only, and the Prices include tax switch binding; (5) branch table main/team, row menu set-main and disabled deactivate, new-branch drawer defaults and ZIP/billing fields; (6) Viewer read-only for new controls. 6 methods | AC-02, AC-03, AC-05, AC-06, AC-07 (UI switch), AC-10, AC-13, AC-14 (UI), AC-17, AC-19, AC-20 (UI), AC-23 |
| Browser (final audit) | Desktop 1440×900 side-by-side comparison with the rendered handoff (incl. 1280 overlay check) and mobile 390×844 comparison; axe and keyboard pass. Read-only interactions against the local database (no data-changing submissions). 2 scenarios | AC-01, AC-05, AC-22, AC-24, AC-25, AC-27 |

## Dependencies

- `specs/company-settings-and-branches/spec.md` (APPROVED): base contracts,
  validation, concurrency, audit and UI states. This spec supersedes its
  decisions OD-07 (unsupported fields), OD-08 (address/country), OD-09 (main
  branch), OD-10 (Team), OD-12 (sequences), OD-13 (currency dialog), OD-19
  (layout/shell), OD-20 (drawer docking) and the "no handoff hex, navy, Inter,
  PrimeIcons" styling rule, plus the adapted copy listed in BR-12.
- `specs/authenticated-app-shell/spec.md` (AUDITED): session, guard, user
  menu and sign-out behavior remain. This spec supersedes its FR-02/BR-01
  navigation list, the "Administration → Company settings" group, and the
  non-goals for placeholder modules, search, organization selector,
  notifications, Help, collapse, navy sidebar, Inter and PrimeIcons.
- `specs/organization-onboarding/spec.md` (AUDITED): field rules, supported
  country/currency lists, business hours editor; registration change FR-18.
- `specs/sign-in/spec.md` (AUDITED): session contract.
- Tables `quotes`, `work_orders`, `technician_profiles`, `invoices`:
  migrated (read only here).
- Implementation classification for `spec-impl`: Backend FULL (new upload
  boundary, schema amendment and migration); Frontend FULL (shell rebuild,
  new route, dependency, tokens, breakpoints). Migration generation requires
  `--generate-migration`.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The frontend loads the logo through its HttpClient service as a blob and binds an object URL to the `<img>`, so the existing `401` handling applies. |
| AS-02 | Time zone generic names and "(UTC±hh:mm) {name}" select labels come from `Intl`, extending the existing display-name helper. |
| AS-03 | The US state list (50 states + DC) is a new data file in `features/organizations/data/`. |
| AS-04 | The Coming soon page lives under `layout/` or a small `features/coming-soon` lazy route; no shared abstraction beyond it. |
| AS-05 | Section highlighting in the Administration column follows the last selected anchor (no scroll-spy). |
| AS-06 | Tooltips in the rail use PrimeNG Tooltip; the organization menu and row menus use PrimeNG Menu. |
| AS-07 | Multipart request size limits are configured per endpoint; the global 32 KB JSON limit is unchanged for other endpoints. |
| AS-08 | The new `--fo-*` tokens live in the global token file and the 1100px/1440px breakpoints in the global breakpoint file under `src/styles/`, next to the existing ones. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Icons | `primeicons` / inline SVG / none | Yes | Add `primeicons` dependency — explicitly approved (2026-09-28) |
| OD-02 | Font | Self-hosted Inter / Google Fonts / system | Yes | Self-hosted Inter woff2 with OFL license — approved (2026-09-28) |
| OD-03 | Colors | `--fo-*` handoff tokens / Aura only | Yes | New `--fo-*` tokens with dark values — approved (2026-09-28) |
| OD-04 | Breakpoints and drawer | Handoff 768/1100/1440 / existing | Yes | Handoff breakpoints; docked drawer ≥1440 — approved (2026-09-28) |
| OD-05 | Collapse | Per visit / persisted / hidden | Yes | Functional, current visit only (2026-09-28) |
| OD-06 | Future modules | Shared Coming soon route / disabled items | Yes | One `/coming-soon/:module` page, visible to all roles; no module built, no fake data (2026-09-28) |
| OD-07 | Top bar controls | Coming soon destinations / disabled | Yes | Search Enter, Help, bell → Coming soon; bell without badge; organization menu lists only current (2026-09-28) |
| OD-08 | Logo storage | DB table / file storage service | Yes | `organization_logos` table, immediate upload/remove endpoints (2026-09-28) |
| OD-09 | Logo types | JPG/PNG/SVG hardened / JPG/PNG | Yes | JPG, PNG, SVG ≤2 MB; content and real MIME verified; SVG rejects scripts, handlers, `foreignObject`, external refs; `nosniff` + sandbox CSP; shown only via `<img>` (2026-09-28) |
| OD-10 | Company address and country | Organization columns, shared country / separate | Yes | New nullable columns; `country_code` is both address and default country; US state list; required on next save (2026-09-28) |
| OD-11 | Website | Stored as typed / normalized | Yes | Optional ≤255, with or without protocol, stored as typed (2026-09-28) |
| OD-12 | Main branch | `is_main` + set-main / fixed | Yes | `is_main` with partial unique index, oldest backfilled, cannot deactivate, Set as main action (2026-09-28) |
| OD-13 | Team column | Active technicians / all | Yes | Active technicians of the branch (2026-09-28) |
| OD-14 | Service ZIP codes | Array column / table | Yes | `varchar(30)[]`, ≤200, deduped, no routing (2026-09-28) |
| OD-15 | Company billing | Stored flag / overrides | Yes | `uses_company_billing` stored only, no override fields (2026-09-28) |
| OD-16 | Edit sequences | Dialog / Coming soon | Yes | Dialog for next quote/work order/invoice numbers with floors (2026-09-28) |
| OD-17 | Manage tax rates | Coming soon / `tax_rates` table | Yes | Coming soon; the handoff does not define its behavior (2026-09-28) |
| OD-18 | Currency change | Confirmation / note only | Yes | Confirmation when invoices exist; `hasInvoices`; server `confirmCurrencyChange` (2026-09-28) |
| OD-19 | No-logo preview | Placeholder / display name | Yes | Building icon + "No logo" (2026-09-28) |
| OD-20 | Administration column below 1100px | Link row / select | Yes | Horizontal scrollable link row (2026-09-28) |
| OD-21 | Screenshot vs handoff conflicts | Handoff / screenshot | Yes | Handoff is the definitive visual and interaction target; its §6 corrections prevail; rebuilt in Angular/PrimeNG, never copied (2026-09-28) |
| OD-22 | Approval of dependencies, tokens, contracts and persistence | Approve / consult per stage | Yes | Formally approved by the user; implementation agents must not stop to re-consult them (2026-09-28) |
| OD-23 | Screenshot values | Reproduce / dynamic | Yes | Organization values, branch, technician and notification counts are never hardcoded to imitate the screenshot (2026-09-28) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02, AC-05 |
| FR-02 | AC-05 |
| FR-03 | AC-03 |
| FR-04 | AC-01, AC-03 |
| FR-05 | AC-06 |
| FR-06 | AC-24, AC-25 |
| FR-07 | AC-07, AC-08 |
| FR-08 | AC-09, AC-10 |
| FR-09 | AC-11, AC-12, AC-21 |
| FR-10 | AC-13 |
| FR-11 | AC-08, AC-19 |
| FR-12 | AC-03, AC-07 |
| FR-13 | AC-14, AC-24 |
| FR-14 | AC-15 |
| FR-15 | AC-16, AC-17, AC-21 |
| FR-16 | AC-22, AC-23 |
| FR-17 | AC-19, AC-20 |
| FR-18 | AC-18 |
| FR-19 | AC-07, AC-11, AC-15, AC-16 |
| FR-20 | AC-24, AC-25 |
| FR-21 | AC-27 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-28 | — → DRAFT     | Created; decisions OD-01 to OD-20 answered "a", clarifications recorded as OD-21 to OD-23 |
| 2026-09-28 | DRAFT → DRAFT | Fixed validation findings: F1 server sequence-floor errors routed through the error summary to Edit sequences (UI states, AC-19); F2 server `409` currency path evidenced (AC-10); F3 fixed copy asserted in AC-25. Warnings: W1 merged AC-04 into AC-03 and AC-26 into AC-24 (25 active); W2 style file paths moved to AS-08; W3 missing/invalid required branch fields → "Enter a valid value." (BR-09, AC-15); W4 main-branch deactivation moved from States row to a guard note; W5 Prices include tax switch added to frontend group (4) |
| 2026-09-28 | DRAFT → APPROVED | Approved by user via /spec approve |
