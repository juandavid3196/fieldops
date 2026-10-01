# Design 20 — Customer management

| Field    | Value                  |
| -------- | ---------------------- |
| Feature  | `customer-management`  |
| Type     | Full-stack             |
| Status   | APPROVED               |
| Created  | 2026-09-30             |
| Updated  | 2026-09-30             |
| Approved | 2026-09-30             |

## Context and objective

Organizations need to register and maintain the customers they serve. This
feature delivers the Design 20 Customers page: metrics, tabs, search, filters,
sorting and pagination over residential and commercial customers, plus a drawer
that creates a customer with its primary contact and first property in one
transaction, warns about possible duplicates without blocking, and edits,
archives and reactivates customers. Owners can also import customers from a
validated, previewed CSV. Every read and write is confined to the caller's
organization, and to the caller's branches where the role is branch-scoped.

## Actors and permissions

Derived from the existing permission catalog (module `customers`: Owner Full,
Operations Manager View, Dispatcher Edit, Technician Assigned only, Accounting
View, Viewer View if granted) and the OD-11 decision.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Everything: view, create, edit, archive, reactivate, create tags, download template, preview and confirm CSV import; all branches | — |
| Dispatcher (`dispatcher`) | View, create, edit, archive, reactivate, create tags, within assigned branches (all branches when `is_all_branches`) | CSV template/import (`403`); customers of other branches (`404`) |
| Operations Manager (`operations_manager`) | View list, metrics and read-only drawer; all branches | Any mutation, tag creation or import (`403`) |
| Accounting (`accounting`) | View list, metrics and read-only drawer within assigned branches (all when `is_all_branches`) | Any mutation, tag creation or import (`403`); customers of other branches (`404`) |
| Viewer (`viewer`) | View list, metrics and read-only drawer; all branches | Any mutation, tag creation or import (`403`) |
| Technician (`technician`) | — (assigned-only access needs assignments that do not exist yet) | Every endpoint in this spec (`403`) |

## Scope

- Customers page inside the authenticated shell at `/customers`: header,
  Import customers / New customer actions, four metrics, tabs with counts,
  search, Customer type / Branch / Tags / Balance status filters, sort,
  table and paginator.
- Residential (`person`) and commercial (`company`) customers; derived Lead /
  Active / Archived lifecycle and Overdue display status.
- Create drawer: customer, primary contact and first property in one
  transaction; branch, tags, preferred communication, service instructions and
  internal note.
- Possible-duplicate warning by exact normalized email or phone, including
  archived customers, with **Create anyway**; never blocks.
- Edit drawer (same fields), read-only drawer for view roles, archive and
  reactivate; no physical deletion.
- Organization tag catalog with inline tag creation; up to 10 tags per customer.
- Owner-only CSV import: template download, upload, validated preview with
  duplicate warnings, all-or-nothing confirmation.
- Branch scoping of reads and writes by role; the page's own Branch filter
  narrows metrics, counts and list.
- Loading, empty, filtered-empty, error, forbidden, submitting and responsive
  states.
- Schema amendment and migration generation (never application) per OD-16.
- Audit rows for customer mutations and imports.

## Non-goals

- Customer detail page, full history, customer notes timeline, multiple
  contacts or multiple properties per customer (Design 21). The list shows the
  property count but offers no property management beyond the first property.
- Customer portal invitation (OD-06). The mockup's "Send portal invitation"
  checkbox is not rendered; `customer_contacts.portal_user_id` is untouched.
- Name-similarity duplicate suggestions.
- Bulk selection and bulk actions (the mockup's row checkboxes are omitted).
- CRM features, campaigns, automations, messaging or sending email/SMS.
- Tag rename, deletion or a tag management screen.
- CSV export and import updates of existing customers.
- Invoices, visits and work orders creation; they are only read for
  derived values.
- Mockup sample data and legacy shell labels.
- The mockup's top-bar "All branches" selector and any other shell change
  (OD-17); only the page's Branch filter applies.
- Applying migrations.

## User flow

1. A permitted user opens **Customers**; the system shows metrics, the **All
   customers** tab, and the first page sorted by Last activity.
2. The user switches tabs, searches, filters, sorts or pages; counts, rows and
   the "Showing x – y of n customers" footer update.
3. An Owner or Dispatcher selects **New customer**; the drawer opens on
   Residential. They choose a type, complete the fields, and the system checks
   email and phone for possible duplicates.
4. On a match, the system shows the "Possible existing customer" panel and
   disables **Create customer** until the user selects **Create anyway**,
   changes the email/phone so no match remains, or opens the existing customer.
5. The user selects **Create customer**; the system creates the customer,
   primary contact, first property and tag assignments atomically, closes the
   drawer, shows a success toast and refreshes the list and metrics.
6. The user selects a customer name or **Edit** in the row menu; the drawer
   opens with current values (read-only for view roles). Saving updates the
   customer.
7. The user selects **Archive** / **Reactivate** in the row menu and confirms;
   the customer moves between tabs.
8. An Owner selects **Import customers**, downloads the template if needed,
   uploads a CSV, reviews the validated preview with row errors or duplicate
   warnings, and confirms; all rows are created or none.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/customers` inside the shell, replacing the `customers` Coming soon slug; the sidebar **Customers** item links to it and is active on it. |
| FR-02 | `GET /customers/metrics` must return the BR-05 metrics for the caller's visible scope and optional branch, and the page must show them in four cards. |
| FR-03 | `GET /customers` must return one page of customers for the requested tab, search, filters and sort (BR-06–BR-08) plus the four tab counts, and the page must render the table per BR-09 with tabs, filters, sort and paginator. |
| FR-04 | The system must derive each customer's lifecycle (Lead / Active / Archived) and display status (Archived / Overdue / Lead / Active) and balance per BR-03 and BR-04. |
| FR-05 | `POST /customers` must create the customer, primary contact, first property and tag assignments in one transaction from the drawer fields validated per BR-10–BR-13. |
| FR-06 | `POST /customers/duplicate-check` must return possible matches per BR-14, and the drawer must show the warning panel and gate **Create customer** per BR-15 without the server ever rejecting a duplicate. |
| FR-07 | `GET /customers/{id}` and `PUT /customers/{id}` must load and update the customer, primary contact, first property and tags with the same validation, keeping the type immutable. |
| FR-08 | `POST /customers/{id}/archive` and `/reactivate` must toggle `customers.is_active` without deleting or altering contacts, properties or references. |
| FR-09 | `GET /customer-tags` and `POST /customer-tags` must list and create organization tags per BR-12. |
| FR-10 | Owners must be able to download the CSV template, preview an import and confirm it per BR-17, creating all rows or none. |
| FR-11 | Every endpoint must resolve the organization from the session and apply role and branch scope per BR-01 and BR-02. |
| FR-12 | The page must render loading, empty, filtered-empty, error, forbidden, read-only, submitting and responsive states per BR-18 and the UI table. |
| FR-13 | Customer mutations, tag creation and imports must write audit rows per BR-19. |
| FR-14 | `GET /customers/branch-options` must return the organization `countryCode` and only the active branches in the caller's branch scope, for every Customers read role, so branch selection and address rules work for all permitted roles without changing `GET /branches`. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Role policy: read endpoints (`GET /customers`, `/customers/metrics`, `/customers/{id}`, `/customers/branch-options`, `/customer-tags`) allow `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`. Mutations (`POST /customers`, `PUT`, archive, reactivate, `POST /customers/duplicate-check`, `POST /customer-tags`) allow `owner`, `dispatcher`. Import endpoints allow `owner` only. `technician` and any other role → `403` everywhere. Frontend visibility is UX only. | Backend |
| BR-02 | Branch scope: `owner`, `operations_manager`, `viewer` and any user with `organization_users.is_all_branches` see all branches; `dispatcher` and `accounting` otherwise see only branches in `organization_user_branches`. A customer is visible when `customers.branch_id` is in scope; a customer outside scope behaves as not found (`404`). Metrics, tab counts and lists only include in-scope customers. A `branchId` query/filter or body value outside scope or not in the organization → `400` `errors.branchId` "Choose a branch you have access to." | Backend |
| BR-03 | Lifecycle (derived, no stored status): **Archived** when `is_active = false`; otherwise **Active** when the customer has at least one visit (through its work orders) with status `completed` or `approved`, or a work order with status `completed` or `approved_for_billing`; otherwise **Lead**. The transition to Active happens automatically when such data exists; no user action sets Lead or Active. | Backend |
| BR-04 | Balance = sum of `invoices.balance_due` for the customer's invoices with status `sent`, `partially_paid` or `overdue`, in `organizations.currency`. Display status precedence: Archived > Overdue (at least one invoice with status `overdue`) > Lead > Active. Tones: Active green, Lead grey, Overdue red, Archived grey. The Type column always shows Residential or Commercial. | Backend / Frontend |
| BR-05 | Metrics (in-scope, optional `branchId`, independent of tab/search/filters): Total customers = non-archived customers; Active customers = lifecycle Active; New this month = customers (any lifecycle) with `created_at` in the current calendar month in `organizations.timezone`; Outstanding balance = sum of BR-04 balances of all in-scope customers. Currency formatted with the organization currency, no decimals when whole, otherwise two. | Backend / Frontend |
| BR-06 | Tabs and counts: **All customers** = Lead + Active (archived excluded); **Leads**; **Active**; **Archived**. Labels include counts, e.g. "Leads (86)". Counts honor branch scope, `branchId`, search and filters but not the tab itself. | Backend / Frontend |
| BR-07 | Search (trimmed, ≤100 chars, case-insensitive, contains) matches display name, primary contact first/last name and email; a term with ≥3 digits also matches the normalized primary contact phone by digits. Filters: Customer type (All / Residential / Commercial); Branch (All branches or one in-scope branch; page-local, default All branches; it also scopes the metrics); Tags (multi-select, matches customers having any selected tag); Balance status (Any / No balance = 0 / Has balance > 0 / Overdue = at least one overdue invoice). Changing tab, search, filter or sort returns to page 1. Search is debounced client-side (300 ms). | Both |
| BR-08 | Sort options: **Last activity** (default; last completed service date descending, customers with no completed service last, then `updated_at` descending), **Name A–Z**, **Name Z–A** (display name, case-insensitive), **Newest** (`created_at` descending), **Balance high–low** (balance descending). Every sort ends with `id` as a stable tie-breaker. The Name column header toggles Name A–Z / Z–A. Page size 10; pages are 1-based; a page beyond the last returns an empty `items` with correct `totalCount`. | Backend / Frontend |
| BR-09 | Table columns: Name (link-styled, opens the drawer), Type, Contact information (primary contact email and formatted phone on two lines, empty lines omitted), Properties ("1 property" / "N properties", plain text, counting active properties), Last service (date "MMM d, yyyy" plus the work order `scope_snapshot` first line truncated with ellipsis; "No completed service" when none, including an Active customer whose activity comes only from a completed work order (OD-18)), Next service (earliest visit with `scheduled_start` ≥ now and status `scheduled`, `assigned` or `on_the_way`: date plus time "h:mm a"; "None" when none), Balance, Status (BR-04 tag), Actions (menu). The API returns dates as UTC instants plus the organization `timezone`; the page formats them in that timezone. Menu: **Edit** (read roles see **View** instead) and **Archive** or **Reactivate** (Owner/Dispatcher only). Footer "Showing x – y of n customers" and a paginator with first pages, ellipsis and last page. | Frontend |
| BR-10 | Drawer fields and validation (trimmed; empty optional strings stored as null): see the Field validation table below. Server errors are returned as `400` `errors.<field>` with the same messages; the drawer shows them under the fields. | Both |
| BR-11 | Mapping: `type` residential → `person`, commercial → `company`. Residential `display_name` = "First Last"; commercial `display_name` = Company name. Primary contact: `customer_contacts` with `is_primary = true`, `is_active = true`. `customers.primary_email` / `primary_phone` mirror the primary contact. First property: `properties.name` "Primary property", `branch_id` = customer branch, `country_code` = `organizations.country_code` (`US` when null), `service_notes` = Service instructions. Internal note → `customers.notes`. Type is immutable after creation (the type toggle is disabled in edit). | Backend |
| BR-12 | Tags: organization catalog `customer_tags`; name trimmed, 1–40 chars, unique per organization case-insensitively. `POST /customer-tags` returns the existing tag (`200`) for a case-insensitive match, else creates (`201`). The drawer tag selector lists all organization tags, filters as the user types and offers "Create "<text>"" when no exact match (Owner/Dispatcher only). A tag created from the drawer is persisted immediately by `POST /customer-tags`, outside the customer transaction, and stays in the catalog even if the drawer is cancelled or the save fails. A customer has 0–10 distinct tags, all of the caller's organization; otherwise `errors.tagIds` "Choose up to 10 tags." / "Choose a valid tag.". | Both |
| BR-13 | Preferred communication: stored on the primary contact as `prefers_email` / `prefers_sms`; at least one required ("Choose at least one communication method."); SMS requires a Mobile phone ("Add a mobile phone to use SMS."). Both errors use the key `errors.preferredCommunication` and show under the Preferred communication group. New drawer defaults: Email checked, SMS unchecked. | Both |
| BR-14 | Duplicate check: email normalized (trim, lower-case) and phone normalized (BR-10) are compared for exact equality against every contact (`customer_contacts.email` / `phone`) of every customer of the organization, including archived customers and regardless of branch scope; the current customer is excluded in edit through `excludeCustomerId`, which is ignored without error when it is unknown, of another organization or outside the caller's branch scope. Matching runs only for a syntactically valid email or phone. Response lists up to 3 distinct customers ordered by display name, each with id, display name, primary email, formatted primary phone, property count, lifecycle/display status and which field matched. Customers outside the caller's branch scope return only display name, matched field and status (no contact data, no id). No name similarity. The server never rejects create/edit because of a match. | Backend |
| BR-15 | Duplicate UX: the check runs on email/phone blur and after 500 ms of no typing when the value is valid. On matches, the drawer shows the amber panel "Possible existing customer" / "Review this match before creating a duplicate." with one card per match (name, email · phone, "N property/properties" · status) and actions **View existing customer** (only for in-scope matches), **Use different email** (or **Use different phone** when the phone matched; clears and focuses that field) and **Create anyway**, plus the note "Exact email or phone matches require review.". While unresolved, **Create customer** (and **Save changes** in edit) is disabled with "Resolve the possible match to continue." underneath. **Create anyway** hides the panel and enables saving for the current email/phone values; changing them re-runs the check. **View existing customer** asks "Discard unsaved changes?" (**Discard** / **Keep editing**) when the form is dirty, then opens that customer in the edit drawer. A failed check shows no panel and does not block saving. | Frontend |
| BR-16 | Archive / reactivate: confirmation dialog "Archive <name>?" / "Archived customers are hidden from active lists. Their contacts, properties and history are kept." (**Archive** / **Cancel**); reactivate needs no confirmation. Archiving an archived customer or reactivating an active one → `409` "This customer is already archived." / "This customer is already active.". Both update `updated_at`. No endpoint deletes customers, contacts or properties. Archived customers can be edited. | Both |
| BR-17 | CSV import (Owner only). Template `GET /customers/import/template`: header row only, file `customers-template.csv`. Columns exactly (case-insensitive, any order, none missing/extra): `type,company_name,first_name,last_name,title,email,mobile_phone,prefers_email,prefers_sms,address_line1,city,state,zip_code,branch_code,tags,service_instructions,internal_note`. Upload: `multipart/form-data` field `file`, UTF-8, RFC 4180, comma, header row; ≤1 MB (1,048,576 bytes) else `errors.file` "Choose a CSV file of 1 MB or smaller."; body >2 MB → `413`; non-multipart → `415`; invalid UTF-8 → "Choose a CSV file."; header mismatch → "The file doesn't match the template."; 0 rows → "The file has no customers."; >500 rows → "Import up to 500 customers at a time.". Row values: `type` residential/commercial; fields per BR-10; `prefers_email`/`prefers_sms` `true`/`false`/`yes`/`no` (empty → email true, SMS false); `branch_code` = an active branch `code` of the organization; `tags` `;`-separated names, missing tags created, ≤10 per row. **Preview** (`POST /customers/import/preview`) validates without writing and returns `rowErrors` `[{ row, column, message }]` (file line numbers, ordered, at most 100), `duplicateWarnings` `[{ row, matchedField, existingDisplayName }]` for matches against existing customers (BR-14) or earlier rows in the file, and `validRowCount`. The dialog shows errors (and disables **Import customers**) or a summary "N customers ready to import" with the duplicate warnings, which never block. **Confirm** (`POST /customers/import`) re-uploads the same file, re-validates, and on any row error returns `400` with `rowErrors` and creates nothing; otherwise creates every row (customer, primary contact, first property, tags) in one transaction and returns `200 { importedCount }`. Import only creates. CSV content is never logged. | Backend / Both |
| BR-18 | Role UX: Owner sees **Import customers**, **New customer**, Edit/Archive/Reactivate. Dispatcher the same without **Import customers**. Operations Manager, Accounting and Viewer see no header actions; name/View opens the drawer read-only (title "Customer details", controls disabled, no duplicate check, tag creation or save; footer **Close**). Technician and unknown roles see the forbidden state "You don't have access to customers." with no further data requests. The Branch filter and the drawer Branch select list only the branches returned by `GET /customers/branch-options`; State/ZIP rules use its `countryCode`. `GET /branches` is not used by this page. | Frontend |
| BR-19 | Audit (`entity_type` `customer`, `actor_user_id` caller, `branch_id` customer branch): `customer.created` (after `{ type, branchId, tagCount }`); `customer.updated` (metadata `{ changedFields: [...] }` names only, no values; no-op → no row); `customer.archived` / `customer.reactivated` (before/after `{ isActive }`); `customer.imported` (`entity_id` and `branch_id` null, metadata `{ importedCount }`, one row per import); `customer_tag.created` (`entity_type` `customer_tag`, after `{ name }`). Email, phone, addresses, instructions and notes are never written to audit rows or logs. | Backend |
| BR-20 | Unsaved changes: closing a dirty drawer (X, **Cancel**, Escape) asks "Discard unsaved changes?" (**Discard** / **Keep editing**). While saving, the save button shows a loading state and fields are disabled; double submission is prevented. | Frontend |

### Field validation (BR-10)

| Field | Applies to | Rule | Message |
| ----- | ---------- | ---- | ------- |
| Customer type | Create | Required; residential / commercial | "Choose a customer type." |
| Company name | Commercial | Required, 1–180 chars | "Enter a company name." / "Use 180 characters or fewer." |
| First name | Both | Required, 1–100 chars | "Enter a first name." / "Use 100 characters or fewer." |
| Last name | Both | Required, 1–100 chars; residential First + " " + Last ≤180 (error on Last name) | "Enter a last name." / "Use 100 characters or fewer." / "Use 180 characters or fewer for the full name." |
| Title | Commercial | Optional, ≤100 chars | "Use 100 characters or fewer." |
| Email | Both | Required, ≤254 chars, valid address; stored lower-case | "Enter an email." / "Enter a valid email." |
| Mobile phone | Both | Optional; may contain digits, spaces, `()`, `-`, `.` and a leading `+`; after stripping, 10–15 digits; an 11-digit number starting with `1` is stored as its last 10 digits; stored digits-only; 10 digits display as "(512) 555-7832" | "Enter a valid phone number." |
| Preferred communication | Both | BR-13 | BR-13 |
| Address line 1 | Both | Required, 1–180 chars | "Enter an address." / "Use 180 characters or fewer." |
| City | Both | Required, 1–100 chars | "Enter a city." / "Use 100 characters or fewer." |
| State | Both | Optional; when the organization country is `US` (or null), one of the 50 states + DC two-letter codes chosen from a dropdown; otherwise free text ≤100 chars | "Choose a valid state." |
| ZIP code | Both | Optional; `US`: `12345` or `12345-6789`; otherwise ≤30 chars | "Enter a valid ZIP code." |
| Service instructions | Both | Optional, ≤2,000 chars | "Use 2,000 characters or fewer." |
| Branch | Both | Required; active branch in scope (BR-02) | "Choose a branch." / BR-02 |
| Tags | Both | BR-12 | BR-12 |
| Internal note | Both | Optional, ≤2,000 chars; never shown outside staff views | "Use 2,000 characters or fewer." |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | Lead | Create or import | Owner, Dispatcher (import: Owner) | Valid data, in-scope branch |
| — | Active | Create or import | System | Never at creation (no completed work exists yet) |
| Lead | Active | Completed/approved visit or completed/approved-for-billing work order exists | System (derived) | BR-03 |
| Lead, Active | Archived | Archive | Owner, Dispatcher | Customer in scope; `is_active = true` |
| Archived | Lead or Active (derived) | Reactivate | Owner, Dispatcher | Customer in scope; `is_active = false` |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `customers` (`id`, `organization_id`, `type`, `display_name`,
  `primary_email`, `primary_phone`, `notes`, `is_active`, `created_at`,
  `updated_at`); `customer_contacts` (`id`, `organization_id`, `customer_id`,
  `first_name`, `last_name`, `email`, `phone`, `title`, `is_primary`,
  `is_active`, timestamps); `properties` (`id`, `organization_id`,
  `customer_id`, `branch_id`, `name`, `address_line1`, `city`,
  `state_region`, `postal_code`, `country_code`, `service_notes`,
  `is_active`, timestamps); read-only: `branches`, `organizations`
  (`timezone`, `currency`, `country_code`), `organization_users`,
  `organization_user_branches`, `work_orders`, `visits`, `invoices`;
  `audit_logs` (insert).
- Not used: `legal_name`, `tax_id`, `billing_address`, `address_line2`,
  latitude/longitude, `access_instructions`, `customer_notes`,
  `portal_user_id`.
- Schema amendments required (authorized by the user, OD-16; the schema doc is
  amended during implementation and a migration is generated with
  `--generate-migration`, never applied):
  1. `customers.branch_id uuid NOT NULL` with the double-FK pattern
     (`REFERENCES branches(id)` plus `FOREIGN KEY(organization_id, branch_id)
     REFERENCES branches(organization_id, id)`) and index
     `ix_customers_org_branch (organization_id, branch_id)`. Existing rows are
     backfilled with their organization's main branch before `NOT NULL` is
     enforced.
  2. `customer_contacts.prefers_email boolean NOT NULL DEFAULT true`,
     `prefers_sms boolean NOT NULL DEFAULT false`,
     `CHECK (prefers_email OR prefers_sms)`; partial unique index
     `ux_customer_contacts_primary (customer_id) WHERE is_primary`; index
     `ix_contacts_org_phone (organization_id, phone)`.
  3. `customer_tags (id uuid PK, organization_id uuid NOT NULL REFERENCES
     organizations(id), name varchar(40) NOT NULL, normalized_name varchar(40)
     NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
     UNIQUE(organization_id, normalized_name), UNIQUE(organization_id, id))`.
  4. `customer_tag_assignments (organization_id uuid NOT NULL REFERENCES
     organizations(id), customer_id uuid NOT NULL, tag_id uuid NOT NULL,
     created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(customer_id,
     tag_id), FOREIGN KEY(organization_id, customer_id) REFERENCES
     customers(organization_id, id), FOREIGN KEY(organization_id, tag_id)
     REFERENCES customer_tags(organization_id, id))`; index
     `(organization_id, tag_id)`.
- Transactions: create and edit are single transactions (customer, contact,
  property, tag assignments, audit); tags they reference already exist.
  Import confirm is a single transaction that also creates missing tags.
  Drawer tag creation is its own write (BR-12); orphan tags are accepted. Tag creation
  races on the unique normalized name resolve to the existing tag.
- Persistence changes require `database-reviewer`.

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session
  (active `organization_users` membership), as in existing features.
- Client-provided organization identifiers: never accepted. Client-provided
  `branchId`, `tagIds` and customer ids are always verified to belong to the
  caller's organization and branch scope.
- Customer of another organization (or outside branch scope) on
  `GET`/`PUT`/archive/reactivate: `404`, no data change. Tag id of another
  organization: `400` `errors.tagIds`. Branch of another organization or out of
  scope: `400` `errors.branchId`.
- Duplicate check searches only the caller's organization; out-of-scope
  matches are redacted per BR-14.
- Import `branch_code` resolves only within the caller's organization.
- Permissions per action: BR-01; branch scope: BR-02.

## API contracts

Contract status: Final. All endpoints require an authenticated session, the
existing CSRF protection on unsafe methods, and return `401` when
unauthenticated. Validation errors use the existing `400` problem shape with
`errors.<field>`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/customers/metrics` | Query `branchId?` | `200 { totalCustomers, activeCustomers, newThisMonth, outstandingBalance, currency }` | `400` branchId, `403` | Read |
| GET | `/customers` | Query `tab` (`all`\|`leads`\|`active`\|`archived`, default `all`), `search?`, `type?` (`residential`\|`commercial`), `branchId?`, `tagIds?` (repeated), `balanceStatus?` (`any`\|`none`\|`has_balance`\|`overdue`), `sort?` (`last_activity`\|`name_asc`\|`name_desc`\|`newest`\|`balance_desc`), `page?` (≥1, default 1) | `200 { items: [{ id, type, displayName, primaryEmail, primaryPhone, propertyCount, lastService: { completedAt, summary } \| null, nextService: { startsAt } \| null, balance, lifecycle, displayStatus, branchId }], totalCount, page, pageSize: 10, tabCounts: { all, leads, active, archived }, currency, timezone }` (`completedAt`, `startsAt` are UTC ISO-8601 instants; `timezone` is `organizations.timezone`) | `400` invalid query values, `403` | Read |
| GET | `/customers/{id}` | — | `200 { id, type, companyName?, contact: { firstName, lastName, title?, email, phone?, prefersEmail, prefersSms }, property: { addressLine1, city, stateRegion?, postalCode? }, serviceInstructions?, internalNote?, branchId, tags: [{ id, name }], lifecycle, displayStatus, isActive }` | `403`, `404` | Read |
| POST | `/customers/duplicate-check` | `{ email?, phone?, excludeCustomerId? }` | `200 { matches: [{ customerId?, displayName, primaryEmail?, primaryPhone?, propertyCount?, displayStatus, matchedField: "email"\|"phone", inScope }] }` | `403` | Mutate |
| POST | `/customers` | `{ type, companyName?, contact: { firstName, lastName, title?, email, phone?, prefersEmail, prefersSms }, property: { addressLine1, city, stateRegion?, postalCode? }, serviceInstructions?, internalNote?, branchId, tagIds: [] }` | `201 { id }` + `Location` | `400` fields, `403` | Mutate |
| PUT | `/customers/{id}` | Same as POST without `type` | `200` (GET shape) | `400`, `403`, `404` | Mutate |
| POST | `/customers/{id}/archive` | — | `204` | `403`, `404`, `409` | Mutate |
| POST | `/customers/{id}/reactivate` | — | `204` | `403`, `404`, `409` | Mutate |
| GET | `/customers/branch-options` | — | `200 { countryCode, branches: [{ id, name }] }` — active branches in the caller's scope (BR-02) ordered by name; `countryCode` = `organizations.country_code`, `US` when null | `403` | Read |
| GET | `/customer-tags` | — | `200 [{ id, name }]` ordered by name | `403` | Read |
| POST | `/customer-tags` | `{ name }` | `201 { id, name }` or `200` existing | `400` name, `403` | Mutate |
| GET | `/customers/import/template` | — | `200 text/csv` attachment `customers-template.csv` | `403` | Import |
| POST | `/customers/import/preview` | multipart `file` | `200 { validRowCount, rowErrors: [], duplicateWarnings: [] }` (row errors returned in `200`; nothing written) | `400` `errors.file`, `403`, `413`, `415` | Import |
| POST | `/customers/import` | multipart `file` | `200 { importedCount }` | `400` `errors.file` or `rowErrors`, `403`, `413`, `415` | Import |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Customers page | Metric card skeletons and table row skeletons; header visible | No customers in scope: "No customers yet" / "Add your first customer or import a CSV." with the role's header actions; filtered: "No customers match your filters." with **Clear filters**; Archived tab empty: "No archived customers." | Metrics or list failure: inline "We couldn't load customers." with **Retry** (per failed region) | Technician/unknown: forbidden state (BR-18); read roles: no actions | Rows, counts and footer render |
| Customer drawer | Edit/view: form skeleton until `GET /customers/{id}` resolves; tags list loading in selector | — | Load failure: "We couldn't load this customer." with **Retry**; `404`: toast "This customer is no longer available." and drawer closes; save failure: field errors or toast "We couldn't save the customer. Try again." (drawer stays open, data kept) | Read-only mode (BR-18) | Toast "Customer created." / "Customer updated."; drawer closes; list and metrics refresh |
| Archive/Reactivate | Menu action disabled while pending | — | Toast "We couldn't update the customer. Try again."; `409` shows its message and refreshes | Hidden for read roles | Toast "Customer archived." / "Customer reactivated."; list, counts and metrics refresh |
| Import dialog | Upload progress / "Checking file…" then "Importing…" | — | File errors under the picker; row errors table (row, column, message; "Showing first 100 errors"); network error toast | Owner only | Toast "N customers imported."; dialog closes; list and metrics refresh |

- Mockups: `design/assets/20-design.png` (Approved; no handoff; the image is
  the visual authority). Deviations decided by the user: no row checkboxes, no
  portal invitation checkbox, **Create anyway** action added, duplicate note
  without the name-similarity sentence, Type column never shows Lead,
  page size 10, top-bar branch selector not implemented (the shell stays as
  is).
- Drawer layout follows the mockup: title "New customer" / "Edit customer" /
  "Customer details", Residential/Commercial toggle, fields in mockup order
  (Commercial adds Company name first and Title after the name row), footer
  **Cancel** and **Create customer** / **Save changes**.
- Responsive (following the list/drawer behavior implemented for Designs
  17–19): ≥1280 px as mockup, drawer overlays the right side; <1280 px metrics
  in 2 columns and filters wrap; <768 px metrics in 1 column, header actions
  stack full-width, search full-width, table scrolls horizontally inside its
  card (no page-level horizontal scroll), drawer full-width, paginator shows
  previous/next with the current page. 16 px side gutter on phones.
- Accessibility: tabs are a tablist with counts in the accessible name;
  sort control and Name header expose sort state; row menu buttons are labelled
  "Actions for <name>"; drawer traps focus and returns it to the trigger;
  duplicate panel is announced politely; status is not conveyed by color alone
  (text label in tag); field errors are linked to their inputs.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid field on create/edit | `400` `errors.<field>`; messages under fields | None |
| Branch outside scope/org | `400` `errors.branchId` "Choose a branch you have access to." | None |
| Foreign or >10 tags | `400` `errors.tagIds` | None |
| Customer of another org or out of scope | `404`; drawer toast "This customer is no longer available." | None |
| Mutation by read role / Technician any call | `403`; UI never offers the action; forbidden state for Technician | None |
| Import by non-Owner | `403` | None |
| Archive archived / reactivate active | `409` with BR-16 message; list refreshes | None |
| Failure mid-create (contact/property/tag) | `500` generic toast "We couldn't save the customer. Try again." | Entire transaction rolled back |
| Import with any row error at confirm | `400` `rowErrors` | None |
| Oversized / non-multipart / invalid CSV | `413` / `415` / `400` `errors.file` | None |
| Duplicate-check failure | No panel; saving allowed | None |
| Unsaved changes on close | "Discard unsaved changes?" | None until Discard |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner signed in | They open **Customers** in the sidebar | `/customers` renders inside the shell with the Design 20 header, actions, four metrics, tabs, filters, sort, table and paginator; no Coming soon page; sidebar item active |
| AC-02 | In-scope customers with invoices, visits and work orders in various states | Metrics and list load | Total, Active, New this month and Outstanding balance follow BR-05; each row's balance, lifecycle and display status follow BR-03/BR-04 (Overdue precedence, archived excluded from Total) |
| AC-03 | A Lead customer | A completed visit or completed work order for it exists | It is returned as Active in list, tabs and metrics without any user action |
| AC-04 | Customers across tabs | The user switches tabs | Rows match BR-06 tab membership, All excludes archived, tab labels show counts honoring search/filters/branch |
| AC-05 | Customers with varied names, emails, phones, types, branches, tags and balances | The user searches and applies each filter and combinations | Results follow BR-07 (phone digit matching, tags any-match, balance buckets); page resets to 1 |
| AC-06 | Customers with and without completed services | The user applies each sort option | Order follows BR-08, customers without completed service last under Last activity, stable across pages |
| AC-07 | More than 10 matching customers | The list renders and the user pages | 10 rows per page, footer "Showing x – y of n customers", paginator navigation; row cells render per BR-09 including "No completed service", "None", property count text and org-timezone dates |
| AC-08 | An Owner or Dispatcher with valid Residential data | They submit **Create customer** | One customer (`person`, display "First Last", branch), one primary contact with preferences, one "Primary property" with the branch and org country, tag assignments and a `customer.created` audit row are persisted; `201`; toast; list and metrics refresh; customer appears as Lead |
| AC-09 | Commercial type selected | The drawer renders and is submitted | Company name and Title appear; `company` customer with display name = Company name and contact title is created |
| AC-10 | Invalid values per the Field validation table and BR-12/BR-13 (table-driven) | The user submits, and the API is called directly | Client shows field messages and does not submit; server returns `400` with the same `errors.<field>` and persists nothing |
| AC-11 | A persistence failure after the customer row is written | Create runs | No customer, contact, property, tag or audit row remains (transaction rolled back) |
| AC-12 | An existing (including archived) customer with the same normalized email or phone | The user enters that email or phone | The duplicate panel shows the match per BR-15; **Create customer** is disabled with the helper text; **Create anyway** enables creation; the server creates the new customer when submitted |
| AC-13 | A duplicate panel showing an in-scope match and a dirty form | The user selects **View existing customer** | The discard confirmation appears; on **Discard** the existing customer opens in the edit drawer; **Use different email/phone** clears and focuses the field |
| AC-14 | A match in a branch outside a Dispatcher's scope | Duplicate check runs | Only display name, matched field and status are returned and shown; no **View existing customer** |
| AC-15 | An existing customer | An Owner/Dispatcher edits fields and tags and saves | Customer, primary contact, first property and tags update in one transaction; type toggle disabled; `customer.updated` audit lists changed field names only; no-op save writes no audit row |
| AC-16 | Active and archived customers | The user archives (after confirmation) and reactivates | `is_active` toggles, contacts/properties untouched, tabs/metrics refresh, audit rows written; repeating returns `409`; no delete endpoint exists |
| AC-17 | The tag selector | The user types a new name and chooses "Create "<text>"", and types an existing name in another case | A new org tag is created (`201`) and selected; the existing-case match returns the existing tag (`200`); an 11th tag cannot be added |
| AC-18 | Organization A and B customers and tags | A user of A requests B's customer by id (GET/PUT/archive/reactivate) or sends B's tag/branch ids | `404` for customers, `400` for tag/branch ids; list, metrics, duplicate check and tags never include B's data |
| AC-19 | A Dispatcher and an Accounting user assigned to branch X, customers in X and Y | They list, get and create | Only X customers and counts are visible; Y customer by id → `404`; creating in Y → `400 errors.branchId`; their `GET /customers/branch-options` returns only active branch X (inactive branches never listed) and lets the Dispatcher create in X; Owner/Operations Manager/Viewer see both |
| AC-20 | Each role | They call read, mutation and import endpoints | Responses follow BR-01 (`403` for denied); Technician gets `403` on every endpoint |
| AC-21 | Owner, Dispatcher, a read role and a Technician on `/customers` | The page renders | UI follows BR-18: Owner all actions; Dispatcher without Import; read roles no actions and read-only "Customer details" drawer; Technician forbidden state |
| AC-22 | An Owner with a valid CSV containing a duplicate of an existing customer | They download the template, upload, preview and confirm | Template has the exact header; preview shows ready count and the duplicate warning without writing; confirm creates all rows (with new tags) in one transaction, returns `importedCount`, writes one `customer.imported` audit row |
| AC-23 | CSV files with header mismatch, >500 rows, >1 MB, unknown `branch_code` or invalid row values | Preview and confirm are called | File errors or `rowErrors` (≤100, ordered, file line numbers) are returned; **Import customers** stays disabled; confirm with errors creates nothing |
| AC-24 | Slow, failing and empty responses | The page and drawer load | Skeletons, empty (no data vs filtered, with **Clear filters**), error with **Retry**, save error keeping data, and the dirty-close confirmation behave per the UI table and BR-20 |
| AC-25 | Viewports ≥1280, 768–1279 and <768 px | The page and drawer render | Layout follows the responsive rules without page-level horizontal scroll; keyboard focus and accessible names follow the accessibility constraints |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Phone/email normalization and validity (table-driven); lifecycle/display-status derivation precedence | AC-02, AC-03, AC-10, AC-12 |
| Backend integration | Create transaction (all rows + audit) and rollback on mid-transaction failure; edit with tags and audit changed-field names; archive/reactivate with `409`; list tabs/search/filters/sort/paging and metrics against seeded invoices/visits/work orders; duplicate check including archived and out-of-scope redaction; tag create/existing; CSV preview errors/warnings and all-or-nothing confirm | AC-02–AC-08, AC-11, AC-12, AC-14–AC-17, AC-22, AC-23 |
| Authorization/tenant isolation | One cross-organization denial per path (customer by id, foreign tag/branch ids, list/metrics exclusion); branch-scope denial for a Dispatcher; role matrix for read/mutate/import including Technician `403` | AC-18, AC-19, AC-20 |
| Frontend component/service | Drawer validation, type toggle, preferences/SMS rule, duplicate panel gating (**Create anyway**, **View existing customer** with discard confirm); list query building/tab-filter sync and states; role UX; import dialog preview/confirm states | AC-09, AC-10, AC-12, AC-13, AC-21, AC-24 |
| Browser (final audit) | User-supplied visual QA only: happy path create with duplicate warning, responsive layouts | AC-01, AC-25 |

## Dependencies

- Authenticated app shell and session organization resolution
  (`authenticated-app-shell`) — implemented; unchanged by this spec.
- Branches (`company-settings-and-branches`) and user branch assignments
  (`design-18-users-and-permissions`) — implemented.
- Permission catalog `customers` module (Design 18) — existing, unchanged.
- CSV import and drawer patterns (`products-services`) — precedent.
- Work orders, visits and invoices tables — mapped; no feature creates their
  data yet, so derived values are empty until those modules exist.
- Schema amendment and migration generation authorized (OD-16).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | "Last service" date is the visit `actual_completed_at` (falling back to `scheduled_end`) of the latest `completed`/`approved` visit. |
| AS-02 | Property count counts active properties; with this spec it is always 1 for created customers. |
| AS-03 | Edit applies last-write-wins; no optimistic concurrency token is required. |
| AS-04 | The API route prefix and problem-details shape follow existing endpoints. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Customer branch | Add `customers.branch_id`; property only | No | Add required `customers.branch_id`; first property inherits it (user, 2026-09-30) |
| OD-02 | Lead state | Derived; stored status | No | Derived; automatically Active once completed/approved visits or work exist; Lead in Type column is a mockup error (user, 2026-09-30) |
| OD-03 | Overdue status and balance | BR-04 | No | Confirmed (user, 2026-09-30) |
| OD-04 | Metrics and tabs | BR-05, BR-06 | No | Confirmed (user, 2026-09-30) |
| OD-05 | Duplicate resolution vs never blocking | Design-only; add **Create anyway** | No | Add **Create anyway**; exact normalized email/phone only, archived included; **View existing customer** opens edit drawer after dirty confirm (user, 2026-09-30) |
| OD-06 | Portal invitation | New table; send now; exclude | No | Excluded; future complete flow with a working link and acceptance (user, 2026-09-30) |
| OD-07 | Tags | Catalog + assignment; `text[]` | No | Catalog `customer_tags` + assignment table, max 10 per customer (user, 2026-09-30) |
| OD-08 | Preferred communication | On contact | No | Confirmed (user, 2026-09-30) |
| OD-09 | Commercial form | Company name + contact + Title | No | Confirmed (user, 2026-09-30) |
| OD-10 | First property requirements and mappings | BR-11 | No | Confirmed (user, 2026-09-30) |
| OD-11 | Permissions and branch scope | BR-01, BR-02 | No | Confirmed; CSV import Owner only (user, 2026-09-30) |
| OD-12 | Edit and archive | BR-11, BR-16 | No | Confirmed (user, 2026-09-30) |
| OD-13 | CSV import | BR-17 | No | Confirmed (user, 2026-09-30) |
| OD-14 | Table elements | Omit checkboxes; bulk actions | No | Omit checkboxes; name opens drawer; property count text; menu Edit and Archive/Reactivate (user, 2026-09-30) |
| OD-15 | Sort, paging, route | BR-08, `/customers` | No | Confirmed; customers without activity last under Last activity (user, 2026-09-30) |
| OD-16 | Schema amendment and migration | Authorize; defer | No | Authorized: amend schema doc and generate migration with `--generate-migration`; never apply (user, 2026-09-30) |
| OD-17 | Top-bar branch selector does not exist in the shell | Page filter only; add to this spec; separate spec | No | Page Branch filter only; no shell change (user, 2026-09-30) |
| OD-18 | Active customer without a completed visit | Show "No completed service"; use work order as last service | No | Accepted: shows "No completed service" and sorts last under Last activity (user, 2026-09-30) |
| OD-19 | Branch options and organization country for non-Owner roles (`GET /branches` allows only owner/viewer) | New `GET /customers/branch-options`; relax `GET /branches` | No | New `GET /customers/branch-options` with Customers read permission returning `countryCode` and in-scope active branches; `GET /branches` authorization unchanged; also `errors.preferredCommunication` mapping and full-name 180 message unified (user, 2026-09-30) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-24 |
| FR-03 | AC-04, AC-05, AC-06, AC-07 |
| FR-04 | AC-02, AC-03 |
| FR-05 | AC-08, AC-09, AC-10, AC-11, AC-17 |
| FR-06 | AC-12, AC-13, AC-14 |
| FR-07 | AC-10, AC-15 |
| FR-08 | AC-16 |
| FR-09 | AC-17 |
| FR-10 | AC-22, AC-23 |
| FR-11 | AC-18, AC-19, AC-20 |
| FR-12 | AC-21, AC-24, AC-25 |
| FR-13 | AC-08, AC-15, AC-16, AC-22 |
| FR-14 | AC-19, AC-20 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-30 | — → DRAFT     | Created from Design 20 with the user's decisions OD-01 to OD-16 |
| 2026-09-30 | DRAFT → DRAFT | Revised after validation: OD-17 (page Branch filter only, no shell change), OD-18 (Active without completed visit), UTC instants + `timezone` in list contract, drawer tag creation outside the customer transaction, `excludeCustomerId` ignored when not authorized |
| 2026-09-30 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-09-30 | APPROVED → DRAFT | Revised during implementation (user decision OD-19): added FR-14 and `GET /customers/branch-options`; `errors.preferredCommunication` key; full-name 180-character message; AC-19 extended |
| 2026-09-30 | DRAFT → APPROVED | Approved by user via /spec approve |
