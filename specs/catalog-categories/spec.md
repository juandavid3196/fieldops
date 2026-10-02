# Catalog categories

| Field    | Value                 |
| -------- | --------------------- |
| Feature  | `catalog-categories`  |
| Type     | Full-stack            |
| Status   | APPROVED              |
| Created  | 2026-10-02            |
| Updated  | 2026-10-02            |
| Approved | 2026-10-02            |

## Context and objective

`service_categories` and `catalog_items.category_id` exist, but no endpoint or
screen manages them, so an organization cannot make services visible on the
public service request form (which lists only active services inside active
categories). This slice adds category management and category assignment inside
Products & Services, plus a backend-computed readiness indicator, without schema
changes or a new module.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner, Operations Manager (`owner`, `operations_manager`; CatalogManage) | View categories and readiness; create, rename, deactivate, reactivate categories; assign, change or clear an item's category | Hard-delete categories; supply an organization identifier; reach another organization |
| Dispatcher, Accounting, Viewer (`dispatcher`, `accounting`, `viewer`; CatalogView) | View readiness and an item's category in Item details; list categories | Any category mutation or `categoryId` change (`403`); see **Manage categories** |
| Technician (`technician`) and unknown roles | Use the shell | Any endpoint here (`403`) |
| Unauthenticated visitor | — | Any endpoint here (`401`) |
| System | Resolve organization/user/membership from the session; compute counts and readiness; write audit rows | Trust client organization identifiers or unverified category ids |

## Scope

- Category list, create, rename, deactivate and reactivate endpoints.
- Nullable `categoryId` on catalog item create, update and detail.
- Readiness status in the existing catalog summary.
- **Manage categories** dialog on the Products & Services page.
- Category selector in the existing Add/Edit item drawer and category display in
  Item details.
- Readiness indicator on the Products & Services page.
- Audit rows for category mutations and item category changes.

## Non-goals

- Hard deletion, subcategories/hierarchies, icons or images, category
  descriptions (`service_categories.description` is neither exposed nor edited).
- Manual ordering of categories (drag-and-drop or move controls); categories are
  always ordered by name.
- Category CSV import/export; the item CSV template, export and import keep
  their seven columns and imported items have no category.
- Category column or filter in the items table; category in the items list
  response.
- Pricing by category; Company settings changes; an "accepts public requests"
  setting or column.
- Changes to the public service request form or its endpoints.
- Schema changes or migrations.
- Changes to the permission matrix.

## User flow

1. A manager opens Products & Services and sees the readiness indicator.
2. The manager selects **Manage categories**; the dialog lists categories by name
   with status and counts.
3. The manager creates a category; the list and the indicator refresh.
4. The manager opens an item drawer, chooses the category in **Category** and
   saves.
5. The indicator shows ready once an active category contains an active service.
6. The manager deactivates a category after confirming; its items keep their
   category, and its services leave the public form until it is reactivated.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The system must list the session organization's categories ordered by name with status, total item count and active service count. |
| FR-02 | The system must let CatalogManage roles create a category with a trimmed, required, case-insensitively unique name. |
| FR-03 | The system must let CatalogManage roles rename a category under the same name rules. |
| FR-04 | The system must let CatalogManage roles deactivate and reactivate a category idempotently, without deleting, deactivating or unassigning its items. |
| FR-05 | The system must accept a nullable `categoryId` on catalog item create and update, assigning only active categories of the session organization, while allowing an item to keep its current inactive category. |
| FR-06 | The system must return the item's category (id, name, active flag) in the catalog item detail. |
| FR-07 | The system must compute public-request readiness from active categories and active services and return it in the catalog summary. |
| FR-08 | The frontend must show the readiness indicator on Products & Services for every CatalogView role. |
| FR-09 | The frontend must provide the **Manage categories** dialog for CatalogManage roles with create, rename, deactivate (confirmed) and reactivate, refreshing the dialog and the indicator without a page reload. |
| FR-10 | The frontend must provide a **Category** selector in the Add/Edit item drawer and show the category in Item details. |
| FR-11 | Every successful category mutation and every item category change must write audit rows per BR-09; no-ops and failures write none. |
| FR-12 | All category operations must be scoped to the session organization and authorized server-side. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Permissions reuse Products & Services policies: **CatalogView** (`owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`) → list categories, summary, item detail. **CatalogManage** (`owner`, `operations_manager`) → create, rename, activate, deactivate categories; item create/update including `categoryId`. Other roles → `403`; no session → `401`. Frontend visibility is UX only. | Backend |
| BR-02 | Category name: trimmed before validation and storage; required ("Enter a category name."); ≤120 characters after trimming ("Category name must be 120 characters or fewer."). Unknown or invalid body → `400` with key `name`. | Both |
| BR-03 | Name uniqueness: within an organization, the trimmed name compared case-insensitively is unique across active and inactive categories. Checked by the application; violation → `409` `errors.name` "A category with this name already exists." A concurrent exact-name duplicate is rejected by the existing `UNIQUE(organization_id, name)` and returns the same `409`. A concurrent duplicate differing only by case is an accepted residual risk (decision OD-02). The same name is allowed in other organizations. Renaming a category to its own name with a different case is allowed. | Backend (application check) / Frontend (message) |
| BR-04 | Rename with a name identical to the stored one → `200` with no change and no audit. Inactive categories may be renamed. | Backend |
| BR-05 | New categories are active. Deactivate an active category → `is_active = false`; reactivate an inactive one → `is_active = true`; repeating either on a category already in that state → `204` without change or audit. Deactivation never deletes, deactivates or edits catalog items, and items keep `category_id`. Reactivation makes its active services eligible on the public form again; inactive items stay excluded. | Backend |
| BR-06 | List: `[{ id, name, isActive, itemCount, activeServiceCount }]` ordered by lower-case name, ties by id. `itemCount` = all items of both types and states with that category; `activeServiceCount` = active `service` items with that category. | Backend |
| BR-07 | Item `categoryId` (create and update body, optional, nullable; absent = null = "No category"). A non-null value must be an active category of the session organization, except that on update the item's current category is accepted even when inactive. Unknown, foreign or inactive (not current) ids → `400` `errors.categoryId` "Choose an active category." with no change. Both types (service and product) may have a category. Item list rows, CSV export, template and import are unchanged. | Backend |
| BR-08 | Readiness, organization-wide and independent of list filters: `ready` when at least one active category has at least one active `service` item; otherwise `no_active_categories` when no active category exists, else `no_active_services`. Copy: ready — "Public service requests are ready."; `no_active_categories` — "Create and activate a category to accept public requests."; `no_active_services` — "Assign an active service to an active category to accept public requests." The indicator covers only the catalog condition of the public form and changes no settings. | Backend (status) / Frontend (copy) |
| BR-09 | Audit (`actor_user_id` caller, `branch_id` null, `entity_type` `catalog_category`, `entity_id` category id): `catalog_category.created` (after `{ name, isActive }`); `catalog_category.renamed` (before/after `{ name }`); `catalog_category.deactivated` / `catalog_category.reactivated` (before/after `{ isActive }`). An item created with a category includes `categoryId` in the after data of the existing `catalog_item.created` row (omitted when null). Item category changes reuse `catalog_item.updated` with `categoryId` among the changed keys (before/after ids only). Nothing else is recorded. | Backend |
| BR-10 | Public form eligibility (existing, unchanged): a service appears only when the item is active, of type `service`, has a category and the category is active; categories and services ordered by name. | Backend (existing) |
| BR-11 | Dialog: title "Manage categories"; create row with input "Category name" and **Add category**; one row per category with name, status tag "Active" (success) / "Inactive" (warning), counts "{itemCount} items · {activeServiceCount} active services" (singular "item"/"service" for 1), **Rename** and **Deactivate** (active) or **Reactivate** (inactive) with accessible labels "Rename {name}", "Deactivate {name}", "Reactivate {name}". Rename is inline: the name becomes an input with **Save** and **Cancel**; Enter saves, Escape cancels. Deactivate uses the shared FieldOps confirmation dialog: "Deactivate {name}? Its services will no longer appear in the public request form. Catalog items will not be deleted." → **Deactivate** / **Cancel**. Reactivate needs no confirmation. Toasts: "Category added", "Category renamed", "{name} deactivated", "{name} reactivated". Desktop: centered dialog; small screens: full-width dialog following existing patterns. Closing the dialog keeps the page state. | Frontend |
| BR-12 | Drawer **Category** selector (after Description): first option "No category", then active categories by name; when the item's current category is inactive it is also listed and marked "{name} (Inactive)" and stays selected until changed. New items default to "No category". Category participates in the dirty-close guard. Item details (read-only roles) show the category name, "{name} (Inactive)" or "No category" as read-only text. | Frontend |
| BR-13 | Readiness indicator on Products & Services, visible to every CatalogView role, placed below the metric cards and above the tabs: ready → success note; not ready → warning note; copy per BR-08. Hidden while the summary is loading or when it fails (metrics show their existing failure state). **Manage categories** appears in the page header next to **Add item** only for CatalogManage roles. Search, filters, tabs, table, paging, drawer layout and responsive behavior are otherwise unchanged. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| (none) | Active | Create | Owner, Operations Manager | BR-02, BR-03 |
| Active | Inactive | Deactivate (confirmed) | Owner, Operations Manager | — |
| Inactive | Active | Reactivate | Owner, Operations Manager | — |
| Item without category / with category | Item with another category / without category | Item create or update | Owner, Operations Manager | BR-07 |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `service_categories` (`id`, `organization_id`, `name`, `is_active`; insert and
  update), `catalog_items` (`category_id` read/write; `type`, `is_active`,
  `organization_id` read), `audit_logs` (insert).
- Existing constraints relied on: `UNIQUE(organization_id, name)` and
  `UNIQUE(organization_id, id)` on `service_categories`; composite FK
  `catalog_items(organization_id, category_id)` → `service_categories`.
- Schema amendments required: None. No migration is generated. The EF model may
  gain only domain behavior (rename, activate/deactivate) without mapping
  changes; any mapping change requires `database-reviewer`.

## Tenant isolation and authorization

- Organization context: session `OrganizationId`, `UserId`, `MembershipId`; role
  read from the membership on every request.
- Client-provided organization identifiers: none accepted. `categoryId` in item
  bodies is a client identifier verified against the session organization
  (BR-07).
- A category of another organization on any `{id}` route (rename, activate,
  deactivate): `404`, no change. As `categoryId` in an item body: `400`
  `errors.categoryId`, identical to an unknown id, no change.
- Counts and readiness never include other organizations' rows.
- Permissions: BR-01.
- Sensitive data: none introduced; audit stores ids, names and flags only.
- CSRF: existing `SameSite=Strict` session cookie, JSON only, CORS allow-list.

## API contracts

Contract status: Final. All responses carry `Cache-Control: no-store`.
Validation errors use the existing ProblemDetails `errors` shape. `Category` =
`{ id, name, isActive, itemCount, activeServiceCount }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/catalog-categories` | — | `200 { items: Category[] }` (BR-06) | `401` · `403` | CatalogView |
| POST | `/catalog-categories` | `{ name }` | `201 Category` + `Location` | `400` `errors.name` · `409` `errors.name` · `401` · `403` | CatalogManage |
| PUT | `/catalog-categories/{id}` | `{ name }` | `200 Category` | `400` `errors.name` · `404` · `409` `errors.name` · `401` · `403` | CatalogManage |
| POST | `/catalog-categories/{id}/activate` | none | `204` | `404` · `401` · `403` | CatalogManage |
| POST | `/catalog-categories/{id}/deactivate` | none | `204` | `404` · `401` · `403` | CatalogManage |
| POST | `/catalog-items` | Products & Services body + `categoryId: uuid \| null` | `201 Detail` (with category fields) | Existing · `400` `errors.categoryId` | CatalogManage |
| PUT | `/catalog-items/{id}` | Products & Services body + `categoryId: uuid \| null` | `200 Detail` (with category fields) | Existing · `400` `errors.categoryId` | CatalogManage |
| GET | `/catalog-items/{id}` | — | `200 Detail` + `categoryId: uuid \| null`, `categoryName: string \| null`, `categoryIsActive: boolean \| null` | Existing | CatalogView |
| GET | `/catalog-items/summary` | — | Existing fields + `publicRequestReadiness: "ready" \| "no_active_categories" \| "no_active_services"` | Existing | CatalogView |

- Item list, export, template and import contracts are unchanged.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Readiness indicator | Hidden | — | Hidden when summary fails | Visible to all CatalogView roles | Success or warning note (BR-08, BR-13) |
| Manage categories dialog | Three skeleton rows | "No categories yet. Add one to group your services." | "We couldn't load categories. Check your connection and try again." + **Retry** replacing the list | Action and dialog only for CatalogManage roles | Toast; list and indicator refresh in place |
| Dialog create / rename | Submitting: spinner on the button, that input and button disabled | — | `400`/`409` under the input, input kept; other failures: "We couldn't save the category. Try again." under the input | — | Toast; input cleared (create) or row returns to view mode (rename) |
| Dialog deactivate / reactivate | Row action disabled with spinner | — | Toast "We couldn't update {name}. Try again."; state unchanged | — | Toast; status tag and indicator refresh |
| Drawer Category selector | Selector disabled while categories load | Only "No category" (plus current inactive category, if any) | Selector disabled, "Couldn't load categories." + **Retry** under it; saving keeps the current category | Read-only text in Item details | Saved value shown on reopen |

- Mockups: None. Visual reference is the implemented Products & Services page
  and the shared FieldOps confirmation dialog (`confirm-dialog`); no new design
  exists.
- Responsive and accessibility constraints: the dialog is keyboard operable;
  focus moves to the rename input on **Rename** and returns to the **Rename**
  button on Save/Cancel; status is conveyed by text, not color alone.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Empty or whitespace name | `400` `errors.name` "Enter a category name." | None |
| Name > 120 characters after trim | `400` `errors.name` "Category name must be 120 characters or fewer." | None |
| Duplicate name (case-insensitive) | `409` `errors.name` "A category with this name already exists." | None |
| Category of another organization or unknown on `{id}` route | `404` | None |
| Item `categoryId` unknown, foreign or inactive (not current) | `400` `errors.categoryId` "Choose an active category." under Category | None |
| Category deactivated by someone else before item save | Same as previous row | None |
| Read-only role calls a mutation | `403` | None |
| No session | `401` | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An organization with active and inactive categories containing services and products in both states, and another organization with categories | A CatalogView role requests `GET /catalog-categories` | `200` with only the session organization's categories, ordered by name, each with correct `isActive`, `itemCount` and `activeServiceCount` |
| AC-02 | A manager | `POST /catalog-categories` with `"  Plumbing  "` | `201` with name "Plumbing", `isActive` true, counts 0, `organization_id` from the session; one `catalog_category.created` audit row |
| AC-03 | A category "Plumbing" in the organization and one named "Electrical" in another organization | Create with `"plumbing"`, with `"Electrical"`, with empty/whitespace and with 121 characters (parameterized) | `"plumbing"` → `409` `errors.name`; `"Electrical"` → `201`; empty and too long → `400` `errors.name` with BR-02 messages; no change on failures |
| AC-04 | Categories "Plumbing" and "Electrical" | Rename "Plumbing" to "Drains", to "plumbing" (own case change), to "Electrical", and to its current name (parameterized) | "Drains" and "plumbing" → `200` with one `catalog_category.renamed` row each (before/after name); "Electrical" → `409` `errors.name`; same name → `200` without audit |
| AC-05 | An active category with active and inactive services and a product | A manager deactivates it, repeats it, then reactivates it and repeats it | Each first call `204` with one `deactivated`/`reactivated` audit row; repeats `204` with no audit; all items keep `category_id`, `is_active` and other values; while inactive the public form configuration omits its services; after reactivation it lists only its active services |
| AC-06 | Active category A, inactive category B, a category of another organization | Item create/update with `categoryId` A, null, absent, B, the foreign id and an unknown id (parameterized) | A → stored (on create, the `catalog_item.created` row includes `categoryId`); null/absent → no category (no `categoryId` in the created row); B, foreign and unknown → `400` `errors.categoryId` "Choose an active category." with no change |
| AC-07 | An item whose category was deactivated | Update keeping that `categoryId`, then update to null | First → `200` with the inactive category kept; second → `200` without category; each changed update writes one `catalog_item.updated` row with `categoryId` before/after |
| AC-08 | An item with a category | `GET /catalog-items/{id}` | Detail includes `categoryId`, `categoryName`, `categoryIsActive`; without category all three are null |
| AC-09 | Organizations with no active categories; active categories without active services; an active category with one active service (parameterized) | `GET /catalog-items/summary` | `publicRequestReadiness` is `no_active_categories`, `no_active_services` and `ready` respectively, unaffected by other organizations' data |
| AC-10 | A category of organization B | A manager of organization A renames, activates or deactivates it | `404`, no change in B, no audit |
| AC-11 | Dispatcher, Accounting and Viewer sessions; a Technician session; no session (parameterized) | Category mutations and item create/update with `categoryId` | CatalogView roles and Technician → `403`; Technician also `403` on `GET /catalog-categories`; no session → `401`; no change |
| AC-12 | A manager on Products & Services | Opens **Manage categories** | Dialog per BR-11 shows loading, then the list (or empty/error state with Retry) |
| AC-13 | The dialog open | The manager adds a category, renames one inline (including a `409`), and cancels a rename with Escape | Create/rename follow BR-11 and the UI states table; `409` shows under the input with text kept; success toasts and refreshes the list and indicator without a page reload |
| AC-14 | An active category in the dialog | The manager selects **Deactivate**, cancels, then confirms; then selects **Reactivate** | Shared confirmation with the BR-11 copy; Cancel sends nothing; confirm sends the request, toasts and refreshes the row and indicator; Reactivate acts without confirmation |
| AC-15 | Active categories and one inactive category assigned to an item | A manager opens Add item and Edit item | Selector per BR-12: "No category" default for new items, active categories by name, the inactive current category marked "(Inactive)" and selected; choosing a category or "No category" and saving sends `categoryId`; a `400 errors.categoryId` shows under Category |
| AC-16 | Each readiness value | The page loads; then the summary fails | The matching note and copy per BR-08/BR-13 are shown; on summary failure the indicator is hidden |
| AC-17 | A Dispatcher, Accounting or Viewer user | Opens Products & Services and Item details | The indicator is visible; **Manage categories** is absent; Item details show the category as read-only text with no selector |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | None required; name trimming and validation are proven through integration | — |
| Backend integration (≈5, parameterized) | Create, validation and case-insensitive duplicate; rename incl. no-op and audit; deactivate/reactivate idempotency preserving items and changing public form output; item `categoryId` assignment rules incl. keeping an inactive current category and detail fields; list counts and readiness values | AC-01–AC-09 |
| Authorization/tenant isolation | One cross-organization denial on a category `{id}` route plus foreign `categoryId` in an item body; one representative role/permission check (read-only role mutation → `403`, Technician → `403`, no session → `401`) | AC-06, AC-10, AC-11 |
| Frontend component/service (≈4) | Dialog management (load/empty/error, create, inline rename with `409`, deactivate confirmation, reactivate, refresh); drawer Category selector incl. inactive current category and `400` mapping; readiness indicator per status and hidden on failure; read-only roles without write controls | AC-12–AC-17 |
| Browser | None by agents; user-supplied visual QA of the dialog, drawer selector and indicator on desktop and mobile | AC-12–AC-17 |

## Dependencies

- `specs/products-services/spec.md` — implemented; this spec extends its item
  body/detail/summary contracts, supersedes its non-goal "category
  (`service_categories`) … editing" for `category_id` only, and keeps its
  BR-01 policies.
- `specs/public-service-request/spec.md` — implemented; its form configuration
  already applies BR-10 and is not modified.
- Existing session authentication, ProblemDetails, audit writer, toasts and the
  shared FieldOps confirmation dialog.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The readiness indicator reflects only the catalog condition; slug, organization active flag and main branch conditions of the public form are not evaluated (confirmed by user 2026-10-02). |
| AS-02 | Item CSV export, template and import are unchanged; imported items have no category (confirmed by user 2026-10-02). |
| AS-03 | `service_categories.description` is not exposed or edited (confirmed by user 2026-10-02). |
| AS-04 | No category audit stores anything beyond ids, names and active flags. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | `service_categories` has no ordering column; how to order categories? | a) drop manual ordering, order by name · b) schema amendment · c) split into a later spec | No | a) — manual ordering removed from scope (user, 2026-10-02) |
| OD-02 | Case-insensitive uniqueness has no database index | a) application check, accept residual race · b) add `lower(name)` unique index via migration | No | a) (user, 2026-10-02) |
| OD-03 | Who manages categories and `categoryId`? | a) CatalogManage (Owner + Operations Manager) · b) Owner only for categories · c) Owner only for both | No | a) (user, 2026-10-02) |
| OD-04 | Where do read-only roles see an item's category? | a) drawer/Item details only · b) also a table column · c) also a filter | No | a) (user, 2026-10-02) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-03 |
| FR-03 | AC-04 |
| FR-04 | AC-05 |
| FR-05 | AC-06, AC-07 |
| FR-06 | AC-08 |
| FR-07 | AC-09 |
| FR-08 | AC-16, AC-17 |
| FR-09 | AC-12, AC-13, AC-14 |
| FR-10 | AC-15, AC-17 |
| FR-11 | AC-02, AC-04, AC-05, AC-07 |
| FR-12 | AC-01, AC-06, AC-10, AC-11 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-02 | — → DRAFT     | Created |
| 2026-10-02 | DRAFT → DRAFT | Revised after validation: visual reference is the implemented page and `confirm-dialog`; `catalog_item.created` records a non-null `categoryId`; indicator placed below metrics and above tabs; typo fixed |
| 2026-10-02 | DRAFT → APPROVED | Approved by user via /spec approve |
