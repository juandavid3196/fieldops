# Design 19 — Products & services

| Field    | Value                |
| -------- | -------------------- |
| Feature  | `products-services`  |
| Type     | Full-stack           |
| Status   | APPROVED             |
| Created  | 2026-09-30           |
| Updated  | 2026-09-30           |
| Approved | 2026-09-30           |

## Context and objective

"Products and services" is a Coming soon placeholder today, although the
`catalog_items` table and entity already exist. This slice delivers the Design 19
catalog at `/admin/products-services`: metrics, tabs, search, filters, sorting,
paging, create/edit drawer with calculated margin, taxable flag, optional image,
activate/deactivate without deleting history, usage counters, CSV export,
template download and a controlled CSV import. Every operation is authorized
server-side and scoped to the session organization; inactive items keep all
historical references.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View; create, edit, activate/deactivate; upload/remove image; export; download template; import CSV | Hard-delete items; supply an organization identifier; reach another organization |
| Operations Manager (`operations_manager`) | View; create, edit, activate/deactivate; upload/remove image; export | Import CSV or download the template (`403`); hard-delete |
| Dispatcher, Accounting, Viewer (`dispatcher`, `accounting`, `viewer`) | View the page, item details, usage and image; export | Any mutation, template or import (`403`) |
| Technician (`technician`) and unknown roles | Use the shell | Read or change anything here (`403`; forbidden state by URL) |
| Unauthenticated visitor | — | Any endpoint (`401`) |
| System | Resolve organization/user/membership from the session; detect image type from content; compute margin and usage; write audit rows | Trust client organization identifiers, MIME types or file extensions |

## Scope

- `/admin/products-services` inside the authenticated shell, replacing the
  `products-and-services` Coming soon slug.
- Header with **Import CSV** and **Add item**; four metric cards; tabs All items
  / Services / Products with counts; search; Type, Tax status and Status filters;
  sortable table; paginator; two fixed information notes; **Download CSV**
  (template) and **Export list** buttons.
- Add/Edit item drawer: Service/Product selector, name, description, unit cost,
  unit price, calculated estimated margin, Taxable and Active switches, optional
  image, usage line.
- Activate/deactivate from the row menu and the drawer; no hard delete.
- Usage counters (quotes, jobs, invoices) from existing records.
- CSV export honoring filters; template download; all-or-nothing CSV import
  (create only) through a minimal import dialog.
- New Products & services row in the Design 18 permission matrix.
- Schema amendment (BR-17), EF model and one generated migration (never
  applied); audit rows.
- Loading, empty, filtered-empty, error, partial-metrics, forbidden, read-only,
  validation, submitting and success states; desktop, tablet and mobile.

## Non-goals

- Inventory, stock levels, suppliers, purchase orders, stock movements,
  warehouses or reorder points.
- SKU, unit, category (`service_categories`) or per-item tax rate editing;
  these columns keep their defaults and are not exposed.
- Hard deletion of items, bulk edit, bulk activation, duplicate item.
- Import updates/upserts, partial imports, image import, file formats other
  than CSV.
- Discounts or price adjustments (they belong to quotes/invoices).
- Using catalog items on quotes, work orders or invoices (future specs); this
  slice only counts existing references.
- Branch-specific catalogs (the catalog is organization-wide; the top-bar branch
  selector does not affect this page).
- Screenshot-only content: legacy navigation labels (Dashboard, Jobs, Billing,
  Settings) and sample data.
- Playwright/browser automation or automated visual audits by agents.
- Applying the migration to any database.

## User flow

1. A permitted user opens Finance → Products and services (or Administration →
   Products & services). Metrics, tab counts and the table load with skeletons.
2. The user switches tabs, searches, filters, sorts and pages the table.
3. An Owner or Operations Manager selects **Add item**, completes the drawer
   (margin updates live), optionally adds an image and selects **Save item**;
   the drawer closes, a toast shows, table and metrics refresh.
4. From a row menu they select **Edit** (drawer in edit mode with usage) or
   **Deactivate** / **Activate** (deactivation asks for confirmation).
5. Any permitted user selects **Export list** and receives a CSV of the
   currently filtered items.
6. An Owner selects **Download CSV** to obtain the template, fills it, selects
   **Import CSV**, chooses the file and imports it; either every row is created
   or none is and row errors are listed.
7. Dispatcher, Accounting and Viewer users see the page read-only; a Technician
   sees the forbidden state.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The frontend must serve `/admin/products-services` inside the shell with the BR-14 layout and copy, replacing the `products-and-services` Coming soon slug; the Finance "Products and services" item and a new Administration column item "Products & services" link to it. |
| FR-02 | `GET /catalog-items` must return a filtered, sorted, paged list of the session organization's items (active and inactive) per BR-03 and BR-04. |
| FR-03 | `GET /catalog-items/summary` must return the metric and tab counts per BR-05, independent of list filters. |
| FR-04 | The page must provide tabs, search, Type/Tax status/Status filters, sortable columns, paginator and the list states of BR-15, with tabs and the Type filter kept in sync per BR-06. |
| FR-05 | `POST /catalog-items` must create an item per BR-07 and BR-08 and return its detail. |
| FR-06 | `GET /catalog-items/{id}` must return the item detail including image metadata and usage counters per BR-04 and BR-10. |
| FR-07 | `PUT /catalog-items/{id}` must update type, name, description, unit cost, unit price, taxable and active per BR-07 and BR-08. |
| FR-08 | The Add/Edit drawer must follow Design 19: fields, live calculated margin (BR-09), switches, image area, usage line (edit only), validation, submitting, dirty-close guard and read-only mode per BR-14 and BR-15. |
| FR-09 | `POST /catalog-items/{id}/activate` and `/deactivate` must change `is_active` without deleting or altering any referencing record; the row menu offers them per BR-12. |
| FR-10 | Usage counters must count distinct quotes, work orders and invoices referencing the item per BR-10. |
| FR-11 | Image retrieval, upload/replace and removal must follow BR-11 and be available in the drawer. |
| FR-12 | `GET /catalog-items/export` must return a CSV of all items matching the current filters and sort per BR-13; **Export list** downloads it. |
| FR-13 | `GET /catalog-items/import-template` must return the header-only CSV template; **Download CSV** downloads it. |
| FR-14 | `POST /catalog-items/import` must validate and create all rows of a CSV atomically, or none, per BR-13; the import dialog shows the result or row errors. |
| FR-15 | Every successful mutation must write the audit rows of BR-16; no-ops and failures write none. |
| FR-16 | Every endpoint must enforce BR-01 authorization and the organization isolation of Tenant isolation and authorization. |
| FR-17 | The UI must adapt to the role per BR-02: managers see Add item and row actions; only the Owner sees Import CSV and Download CSV; read-only roles see a banner and view-only details; other roles see the forbidden state. |
| FR-18 | `GET /permission-matrix` and the Design 18 matrix must include the Products & services module per BR-18. |
| FR-19 | The page and drawer must follow the responsive and accessibility rules of UI behavior and match `design/assets/19-design.png` with dynamic data only. |
| FR-20 | The BR-17 schema amendment must be applied to `docs/database/fieldops-schema.sql`, the EF model and one generated migration (never applied by agents). |

## Business and validation rules

Text is trimmed; whitespace-only counts as empty. Money is `numeric(14,2)`:
0 to 999,999,999,999.99, at most two decimals.

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Authorization policies (role read from the membership on every request): **CatalogView** = `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer` → list, summary, detail, image `GET`, export. **CatalogManage** = `owner`, `operations_manager` → create, update, activate, deactivate, image `PUT`/`DELETE`. **CatalogImport** = `owner` → template and import. Any other role → `403`; no session → `401`. | Backend |
| BR-02 | Role UX (frontend visibility is UX only): Owner — Import CSV, Add item, Download CSV, Export list, row menus. Operations Manager — Add item, Export list, row menus; no Import CSV or Download CSV. Dispatcher/Accounting/Viewer — read-only banner "You have view-only access. Adding and editing items requires the Owner or Operations Manager role.", Export list only, row menu with only **View details** (drawer read-only, title "Item details", controls disabled, no image upload/remove, footer **Close**). Technician/unknown — forbidden state "You don't have access to products and services." with no data requests beyond the rejected one. The Administration column is shown only to `owner` and `viewer` (existing shell rule); other permitted roles see the page without it. | Frontend |
| BR-03 | `GET /catalog-items` query: `type` (`service`\|`product`), `search` (≤100; case-insensitive contains over name and description), `taxStatus` (`taxable`\|`non_taxable`), `status` (`active`\|`inactive`), `sort` (`name` default, `-name`, `type`, `-type`, `unitCost`, `-unitCost`, `unitPrice`, `-unitPrice`, `taxable`, `-taxable`, `status`, `-status`; ties by name then id), `page` ≥1 (default 1), `pageSize` fixed at 10 (other value → `400`). Invalid values → `400` with that key. Filters combine with AND; absent filter = all. | Backend |
| BR-04 | Row fields: `id`, `type`, `name`, `description`, `unitCost`, `unitPrice`, `estimatedMarginPercent` (BR-09, null when price is 0), `isTaxable`, `isActive`, `hasImage`, `updatedAt`. Detail = row + `image: { contentType, sizeBytes, updatedAt } \| null` + `usage: { quotes, jobs, invoices }`. UI: type tag "Service"/"Product"; money with the organization currency symbol and two decimals; margin "62.5%" or "—"; Taxable "Yes"/"No"; status tag "Active" (success) / "Inactive" (warning) with dot icon and text. | Both |
| BR-05 | Summary: `activeItems` (active, both types), `activeServices`, `activeProducts`, `inactiveItems` (inactive, both types); tab counts `allItems`, `services`, `products` include active and inactive items. Always organization-wide and independent of list filters. The response also carries the session organization's `currency` (ISO 4217 code from `organizations.currency`), which the UI uses for every money symbol on the page and drawer. | Backend |
| BR-06 | Tabs and Type filter are one state: selecting Services/Products sets Type to that value; selecting a Type sets the matching tab; All items ↔ All types. Search is debounced 300 ms; any filter/tab/search change resets to page 1. Paginator text "Showing {from} – {to} of {total} items". | Frontend |
| BR-07 | Item body: `type` (required, `service`\|`product` — "Select a type."), `name` (required; trimmed, internal whitespace collapsed to one space; ≤160 — "Enter a name." / "Name must be 160 characters or fewer."), `description` (optional, ≤1000 — "Description must be 1,000 characters or fewer."; empty → null), `unitCost` (required, money — "Enter a unit cost of 0 or more."), `unitPrice` (required, money — "Enter a unit price of 0 or more."), `isTaxable` (boolean, default true), `isActive` (boolean, default true). Unknown/invalid values → `400` with that key. `sku`, `unit`, `category_id` and `tax_rate` keep their defaults (null, `'unit'`, null, 0) and are never changed here. A cost above the price is allowed (negative margin). Type may change on edit. | Both |
| BR-08 | Name uniqueness: within an organization, `type` + normalized name (lower-case of the stored, whitespace-collapsed name) is unique across active and inactive items. Checked by the application and guaranteed by the BR-17 unique index; a violation (including a concurrent one) → `409` `errors.name` "An item with this name already exists for this type." The same name is allowed for the other type and in other organizations. | Backend (DB constraint) / Frontend (message) |
| BR-09 | Estimated margin = (unit price − unit cost) ÷ unit price × 100, rounded half away from zero to one decimal; unit price 0 → null ("—"). Estimated gross profit per unit = unit price − unit cost (money, may be negative). Computed by the backend for rows/export and live in the drawer with the same rule. The drawer card shows "Estimated margin (calculated)", the percentage, "Estimated gross profit per unit {amount}" and "(Unit price – unit cost) ÷ unit price", with a lock icon (not editable). | Both |
| BR-10 | Usage (any status of the referencing record, organization-scoped, distinct ids so a record reached through two paths counts once): `quotes` = distinct quotes with a `quote_lines` row (any version) whose `catalog_item_id` is the item; `jobs` = distinct `work_orders` whose `quote_version_id` has such a quote line **or** that have a `visits` → `visit_materials` row with the item; `invoices` = distinct invoices with an `invoice_lines` row whose `source_quote_line_id` or `source_visit_material_id` points to a line/material with the item. UI (edit/detail only): "Used on {q} quotes, {j} jobs, and {i} invoices." with singular forms for 1 ("1 quote", "1 job", "1 invoice"). | Backend / Frontend (copy) |
| BR-11 | Image: one per item, optional. Upload/replace `PUT /catalog-items/{id}/image`, `multipart/form-data` field `file`, ≤5 MB (5,242,880 bytes) else `errors.file` "Choose an image of 5 MB or smaller."; type detected from content only (PNG or JPEG signature); anything else, or a detected type not matching the declared part type → `errors.file` "Choose a PNG or JPG image."; request body >6 MB → `413`; non-multipart → `415`. `GET` serves the bytes with the stored MIME type, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`, `Cache-Control: no-store`; no image → `404`. `DELETE` → `204`; removing when none → `204` without audit. Image operations do not change `catalog_items.updated_at`. Client mirrors size/type checks. Drawer: drop zone "Drag and drop an image here or click to upload" / "PNG, JPG up to 5MB"; with an image, a preview plus **Replace** and **Remove**. Image changes are staged and sent after the item save succeeds (create: after `201`); if the item saves but the image call fails, the item stays saved, the drawer stays open in edit mode and the image error shows under the image area. | Both |
| BR-12 | Row menu (`aria-label` "More actions for {name}"): managers — **Edit**, then **Deactivate** (active) or **Activate** (inactive); read-only roles — **View details**. Deactivate asks: "Deactivate {name}? It stays on existing quotes, jobs and invoices, and can be reactivated at any time." → **Deactivate** / Cancel. Activate needs no confirmation. Repeating a transition → `204` no-op without audit. Deactivation never deletes or edits `quote_lines`, `visit_materials`, `invoice_lines` or the image. The drawer Active switch changes the same flag through `PUT`. Toasts: "Item added", "Item updated", "{name} deactivated", "{name} activated", "Imported {n} items". | Both |
| BR-13 | CSV (UTF-8, RFC 4180, comma, header row). Template/export/import columns exactly: `type,name,description,unit_cost,unit_price,taxable,active`. Export values: `service`/`product`, invariant decimals with two places, `true`/`false`; every cell starting with `=`, `+`, `-`, `@`, tab or carriage return is prefixed with `'`; file name `products-services-YYYY-MM-DD.csv` (organization date); honors BR-03 filters and sort without paging. Template: header row only, file name `products-services-template.csv`. Import: `multipart/form-data` field `file`, ≤1 MB (1,048,576 bytes) else `errors.file` "Choose a CSV file of 1 MB or smaller."; request body >2 MB → `413`; non-multipart → `415`; not valid UTF-8 text → `errors.file` "Choose a CSV file."; header not exactly the seven columns (case-insensitive, any order, no missing/extra) → `errors.file` "The file doesn't match the template."; 0 data rows → "The file has no items."; more than 500 → "Import up to 500 items at a time.". Row values: `type` service/product (case-insensitive); `name`, `description`, `unit_cost`, `unit_price` per BR-07 (invariant decimal, `.` separator, no currency symbol); `taxable`, `active` = `true`/`false`/`yes`/`no` (case-insensitive), empty → true. Duplicates within the file or with existing items (BR-08) are row errors. Any error → `400` with `rowErrors: [{ row, column, message }]` (`row` = file line number; at most 100 returned, ordered) and nothing created. Success → `200 { importedCount }`, all rows in one transaction; a concurrent unique violation → `409` and nothing created. Import creates only; it never updates existing items. | Backend (import), Both (export/template) |
| BR-14 | Copy and layout (from `19-design.png`; navigation per Design 17/18): breadcrumb "Administration / Products & services"; h1 "Products & services"; description "Manage reusable line items for quotes, jobs, and invoices."; buttons **Import CSV**, **Add item**; metrics Active items, Services, Products, Inactive (icons box, wrench, box, ban); tabs "All items ({n})", "Services ({n})", "Products ({n})"; search placeholder "Search items..."; filters Type ("All types", "Service", "Product"), Tax status ("All", "Taxable", "Non-taxable"), Status ("All", "Active", "Inactive"); columns Name, Type, Description (two-line clamp, full text in a tooltip), Unit cost, Unit price, Estimated margin (info icon, tooltip "(Unit price – unit cost) ÷ unit price"; not sortable), Taxable, Status, Actions; info note "Catalog items are defaults. Price and description may be adjusted on an individual quote or invoice without changing this list."; warning note "Discounts are added as adjustments on a quote or invoice and are not saved as products or services."; buttons **Download CSV**, **Export list**. Drawer: title "Add item" / "Edit item" / "Item details"; Service/Product selector; Name *, Description, Unit cost * ($-prefixed with the organization currency symbol, helper "Internal only. Used for profitability analysis."), Unit price *, margin card (BR-09), Taxable, Active, "Item image (optional)", "Usage" (BR-10); footer **Cancel** / **Save item**. Administration column items: Company profile, Branches, Business hours, Users & permissions, **Products & services**, Taxes & currency, Document numbering, Notifications. On this route the sidebar Finance item "Products and services" is current and the Administration column marks Products & services current. | Frontend |
| BR-15 | States: loading — metric skeletons and six skeleton rows; initial empty (organization has no items) — "No products or services yet." plus **Add item** for managers; filtered-empty — "No items match these filters." + **Clear filters** (resets tab, search and filters); list error — "We couldn't load items. Check your connection and try again." + **Retry** replacing the table; metrics failure with table loaded — each metric and tab count shows "—" with tooltip "Couldn't load"; detail load failure in the drawer — inline error + Retry; validation on blur after first interaction and on submit with BR-07 messages; submitting — spinner, drawer locked; `400`/`409` mapped to their field or, without a field, an inline message keeping input; closing a dirty drawer asks "Discard changes?" → **Discard** / **Keep editing**; export/template/import in progress — button spinner, button disabled. | Frontend |
| BR-16 | Audit (`entity_type` `catalog_item`, `actor_user_id` caller, `branch_id` null): `catalog_item.created` (after `{ type, name, unitCost, unitPrice, isTaxable, isActive }`); `catalog_item.updated` (before/after changed keys only; no-op → no row); `catalog_item.activated` / `catalog_item.deactivated` (before/after `{ isActive }`, also used when `PUT` changes only `isActive`; a `PUT` that changes `isActive` and other fields writes one `catalog_item.updated` row including `isActive`); `catalog_item.image_updated` / `catalog_item.image_removed` (`{ contentType, sizeBytes }`, never content); `catalog_item.imported` (`entity_id` null, metadata `{ importedCount }`, one row per import). Image bytes and CSV content are never logged or audited. | Backend |
| BR-17 | Schema amendment (approved by the user 2026-09-30): `catalog_items` + `is_taxable boolean NOT NULL DEFAULT true`, + `normalized_name varchar(160) GENERATED ALWAYS AS (lower(name)) STORED`, + `CREATE UNIQUE INDEX ux_catalog_items_org_type_name ON catalog_items(organization_id, type, normalized_name)`. New `catalog_item_images (catalog_item_id uuid PRIMARY KEY, organization_id uuid NOT NULL REFERENCES organizations(id), content_type varchar(40) NOT NULL CHECK (content_type IN ('image/png','image/jpeg')), content bytea NOT NULL, size_bytes integer NOT NULL CHECK (size_bytes BETWEEN 1 AND 5242880), created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items(organization_id, id) ON DELETE CASCADE)`. `tax_rate` stays unchanged and unexposed. No other table changes. | Backend |
| BR-18 | Permission matrix (Design 18 BR-07 extended): new module `products_services` "Products & services", placed after Invoices & payments, levels in role order Owner Full, Operations Manager Edit, Dispatcher View, Technician None, Accounting View, Viewer View. The matrix now has ten modules; it stays display-only and enforcement stays on BR-01 policies (CSV import is Owner-only within Full). | Backend / Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| (none) | Active or Inactive | Create / import | Owner, Operations Manager (import: Owner) | BR-07, BR-08, BR-13 |
| Active | Inactive | Deactivate (menu, confirmed) or `PUT isActive=false` | Owner, Operations Manager | — |
| Inactive | Active | Activate (menu) or `PUT isActive=true` | Owner, Operations Manager | BR-08 still holds (always true: uniqueness covers inactive rows) |
| No image | Image stored | Image `PUT` | Owner, Operations Manager | BR-11 |
| Image stored | Replaced / removed | Image `PUT` / `DELETE` | Owner, Operations Manager | BR-11 |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `catalog_items` (`id`, `organization_id`, `type`, `name`, `description`,
  `unit_cost`, `unit_price`, `is_active`, `created_at`, `updated_at`; `sku`,
  `unit`, `category_id`, `tax_rate` untouched), `quote_lines`
  (`catalog_item_id`, `quote_version_id`), `quote_versions` (`id`, `quote_id`),
  `quotes` (`id`, `organization_id`), `work_orders` (`id`, `organization_id`,
  `quote_version_id`), `visits` (`id`, `work_order_id`), `visit_materials`
  (`catalog_item_id`, `visit_id`), `invoices` (`id`, `organization_id`),
  `invoice_lines` (`invoice_id`, `source_quote_line_id`,
  `source_visit_material_id`), `organizations` (`currency`, `timezone`,
  read only), `audit_logs` (insert).
- Schema amendments required: BR-17 — **approved by the user 2026-09-30**.
  Implementation first updates `docs/database/fieldops-schema.sql`, then the EF
  model, and generates one migration under `--generate-migration`; agents never
  apply it. The unique index assumes no existing duplicate items (no feature
  writes catalog items today). Persistence changes require `database-reviewer`.
- Domain changes: `CatalogItem` gains `IsTaxable`, update, activate/deactivate
  and name normalization; new `CatalogItemImage` entity. Usage counts are
  read-only queries; no referencing table is modified.
- `permissions` / `role_permissions` are not used; the matrix is the existing
  code catalog.

## Tenant isolation and authorization

- Organization context: session `OrganizationId`, `UserId`, `MembershipId`; role
  read from the membership on every request.
- Client-provided organization identifiers: none accepted anywhere. Item ids
  are always resolved against the session `OrganizationId`; list, summary,
  export, usage and duplicate checks are organization-scoped.
- An item of another organization on any `{id}` route (detail, update,
  activate, deactivate, image `GET`/`PUT`/`DELETE`): `404`, no change. Usage
  never counts other organizations' records.
- Permissions: BR-01. Frontend visibility (BR-02) is UX only.
- Uploads: authorized by CatalogManage (image) / CatalogImport (CSV), size
  limited before full buffering, type detected from content; CSV cells
  neutralized against formula injection on export.
- Sensitive data: image bytes and CSV contents are never logged; unit cost is
  internal but visible to every CatalogView role by decision.
- CSRF: existing `SameSite=Strict` session cookie, JSON/multipart only, CORS
  allow-list.

## API contracts

Contract status: Final. All authenticated responses carry
`Cache-Control: no-store`. Row/Detail = BR-04. Validation errors use the
existing ProblemDetails `errors` shape.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/catalog-items` | query BR-03 | `200 { items: Row[], page, pageSize, totalCount }` | `400` BR-03 keys · `401` · `403` | CatalogView |
| GET | `/catalog-items/summary` | — | `200 { activeItems, activeServices, activeProducts, inactiveItems, allItems, services, products, currency }` | `401` · `403` | CatalogView |
| GET | `/catalog-items/{id}` | — | `200 Detail` | `404` · `401` · `403` | CatalogView |
| POST | `/catalog-items` | BR-07 body | `201 Detail` + `Location` | `400` field keys · `409` `errors.name` · `401` · `403` | CatalogManage |
| PUT | `/catalog-items/{id}` | BR-07 body | `200 Detail` | `400` · `404` · `409` `errors.name` · `401` · `403` | CatalogManage |
| POST | `/catalog-items/{id}/activate` | none | `204` | `404` · `401` · `403` | CatalogManage |
| POST | `/catalog-items/{id}/deactivate` | none | `204` | `404` · `401` · `403` | CatalogManage |
| GET | `/catalog-items/{id}/image` | — | `200` image bytes (BR-11 headers) | `404` · `401` · `403` | CatalogView |
| PUT | `/catalog-items/{id}/image` | multipart `file` | `200 { contentType, sizeBytes, updatedAt }` | `400` `errors.file` · `404` · `413` · `415` · `401` · `403` | CatalogManage |
| DELETE | `/catalog-items/{id}/image` | — | `204` | `404` · `401` · `403` | CatalogManage |
| GET | `/catalog-items/export` | query BR-03 minus `page`/`pageSize` | `200 text/csv` attachment | `400` · `401` · `403` | CatalogView |
| GET | `/catalog-items/import-template` | — | `200 text/csv` attachment | `401` · `403` | CatalogImport |
| POST | `/catalog-items/import` | multipart `file` | `200 { importedCount }` | `400` `errors.file` or `rowErrors` · `409` · `413` · `415` · `401` · `403` | CatalogImport |
| GET | `/permission-matrix` | — | Design 18 contract with the BR-18 module added | `401` · `403` | Design 18 (unchanged) |

- The currency comes from `GET /catalog-items/summary` (BR-05), so every
  CatalogView role can render money without `/organization-settings`
  (Owner/Viewer only). The export file date uses `organizations.timezone`
  server-side.

## UI behavior and states

- Mockup: `design/assets/19-design.png` — **Approved** by the user as the single
  visual reference (no handoff exists for Design 19). Screenshot-only elements
  (legacy navigation labels, "Settings" breadcrumb, sample data and counts) are
  not implemented; navigation follows Design 17/18.
- Import dialog (not in the mockup; minimal, approved in scope): title "Import
  products & services", text "Use the template columns. Up to 500 items, 1 MB
  per file.", link **Download template**, file chooser (CSV), **Cancel** /
  **Import**; on `400` it lists row errors as "Row {row} · {column}: {message}"
  (or the file error) and keeps the dialog open; on success it closes, shows
  the BR-12 toast and refreshes table and metrics.

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Catalog page | Metric and row skeletons | Initial empty + Add item (managers); filtered-empty + Clear filters | Table error + Retry; metrics/tab counts "—" | Read-only banner and View details for read roles; forbidden state for others | Toasts, table and metrics refreshed |
| Add/Edit drawer | Detail skeleton; submit spinner, drawer locked | N/A | Field messages; inline message keeping input; image error under image area | Read-only "Item details" for read roles | Drawer closes, toast |
| Deactivate confirmation | Confirm button spinner | N/A | Toast with `ApiError.message`, state unchanged | Managers only | Toast, refresh |
| Import dialog | Import button spinner | N/A | File or row errors listed, dialog open | Owner only | Dialog closes, toast, refresh |
| Export / template | Button spinner | Export with no matches downloads header only | Toast "We couldn't export items. Try again." / "We couldn't download the template. Try again." | Export: all read roles; template: Owner | File downloaded |

- Responsive (Design 17/18 breakpoints): ≥1440px drawer docked at 400px, no
  mask or focus trap, content reflows; 1100–1439px overlay drawer below the top
  bar with mask (click closes via the dirty guard); 768–1099px rail sidebar,
  Administration link row, table scrolls horizontally inside its card;
  below 768px sidebar drawer, metrics 2×2, tabs scroll horizontally, filters
  stack full width, items as cards (name, type tag, unit price, margin,
  taxable, status tag, menu), footer notes and buttons stack, full-screen
  drawer with sticky footer; no horizontal page scroll at 320px; touch
  targets ≥44px on mobile.
- Accessibility: metric cards icon + label + number; tabs expose selection and
  counts; table `th scope="col"`, sortable headers announce sort direction;
  margin info icon has an accessible tooltip; status and type tags carry text;
  row menu `aria-label` "More actions for {name}"; Service/Product selector is a
  labelled single-choice group; money inputs labelled with the currency; margin
  value announced via `aria-live="polite"`; switches with accessible names;
  drop zone operable by keyboard; overlay/full-screen drawer and dialogs trap
  focus and return it to the originating control; docked drawer is a labelled
  non-modal region, Esc closes it (dirty guard); errors linked with
  `aria-describedby`; color is never the only signal.
- Styling: existing `--fo-*` tokens, PrimeIcons, Inter, shell and
  Administration column; PrimeNG components already used by Design 18. No new
  package.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Item validation failure | `400` field keys, BR-07 messages under fields | None |
| Duplicate name for type | `409` `errors.name` BR-08 message under Name | None |
| Item of another organization or unknown | `404`; toast "This item is no longer available.", drawer closes, list refreshes | None |
| Invalid image | `400` `errors.file` BR-11 message under the image area | None (item save already committed stays) |
| Image too large (body) / not multipart | `413` / `415`; same image-area message as the size/type error | None |
| Import file/header/row errors | `400` with `errors.file` or `rowErrors`, listed in the dialog | None |
| Import body too large / not multipart | `413` / `415`; the dialog shows "Choose a CSV file of 1 MB or smaller." / "Choose a CSV file." | None |
| Import concurrent duplicate | `409` "Some items already exist. Review the file and try again." in the dialog | None |
| `401` | Existing session-cleared redirect | None |
| `403` on read | Forbidden state | None |
| `403` on mutation | `ApiError.message`; input kept | None |
| List load failure | BR-15 error state with Retry | None |
| Export/template failure | BR-15/UI toast | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | A signed-in Owner at 1440px | `/admin/products-services` renders | The shell with Finance "Products and services" current, the Administration column with Products & services current, breadcrumb, h1, description, Import CSV, Add item, four metrics, tabs with counts, search and three filters, nine-column table, paginator, both notes, Download CSV and Export list appear per BR-14 with real data; the `products-and-services` Coming soon slug no longer applies |
| AC-02 | Active/inactive services and products with varied prices, taxable flags and descriptions in two organizations | `GET /catalog-items` with `type`, `search`, `taxStatus`, `status`, each sort and paging (parameterized) | Only the session organization's items are returned with BR-04 fields and BR-09 margins (null at price 0, negative when cost > price); filters combine; sort, ties, `pageSize = 10` and `totalCount` are correct; invalid values → `400` with their key |
| AC-03 | The same data | `GET /catalog-items/summary` | Metric counts cover active items only by type, `inactiveItems` counts inactive items, tab counts include both statuses; values ignore list filters and exclude the other organization; `currency` is the session organization's code and every money value on the page and drawer uses its symbol |
| AC-04 | An Owner on the page | They switch tabs, change Type, search, filter, sort, page, hit no matches, clear filters, and simulate list and summary failures (parameterized) | Tabs and Type stay in sync; requests carry filters, sort and page reset to 1; paginator text follows BR-06; filtered-empty and initial-empty states, loading skeletons, error + Retry and "—" metrics follow BR-15 |
| AC-05 | An Owner or Operations Manager and a valid body | `POST /catalog-items` | `201` Detail; name trimmed/collapsed; `is_taxable`, `is_active` stored; `sku` null, `unit` `'unit'`, `tax_rate` 0; `organization_id` from the session; one `catalog_item.created` audit row |
| AC-06 | Bodies failing one BR-07 rule each; a duplicate name in the same type differing only by case/spacing; the same name in the other type and in another organization; two concurrent creates with the same name (parameterized) | `POST`/`PUT /catalog-items` | Each invalid body → `400` with its key and message and no change; duplicates (including the concurrent loser) → `409` `errors.name` with no change, enforced by the unique index; the other-type and other-organization names succeed |
| AC-07 | An existing item | `PUT /catalog-items/{id}` with changed fields, a type change, only `isActive` changed, and an identical body (parameterized) | `200` Detail with values persisted and `updated_at` advanced; audit follows BR-16 (updated with changed keys, activated/deactivated for active-only change, no row for the no-op) |
| AC-08 | A manager opens Add item and Edit item | They switch type, enter cost/price (including price 0 and cost above price), submit invalid then valid data, receive a `409`, and close a dirty drawer | Fields, copy and helper follow BR-14; the margin card updates live per BR-09 ("—" at price 0, negative values shown); validation follows BR-15; submitting locks the drawer; `409` shows under Name with input kept; success closes, toasts and refreshes; the dirty close asks to discard; edit mode shows the Usage line |
| AC-09 | An active item referenced by quote lines, visit materials and invoice lines | A manager deactivates it (confirming), repeats it, then activates it via the menu and via the drawer switch | Deactivate: confirmation per BR-12, `204`, `is_active = false`, one audit row, toast; repeat → `204` no audit; referencing rows, image and usage counts unchanged; the item stays listed under status Inactive and in the Inactive metric; activate restores it with one audit row; Cancel sends nothing |
| AC-10 | An item referenced by one quote with two versions, a work order from that quote whose visit also used the item as material, an invoice with lines from both the quote line and the material, and references in another organization (parameterized) | `GET /catalog-items/{id}` | `usage` counts each quote, work order and invoice once (1, 1, 1 in the base case), counts all statuses, ignores other organizations; the drawer shows the BR-10 sentence with singular/plural forms |
| AC-11 | A manager and files: valid PNG, valid JPEG, >5 MB, spoofed extension/declared type, non-image, body >6 MB, non-multipart (parameterized) | Image `PUT`, then `GET`, replace and `DELETE` twice | Valid files → `200` metadata, bytes stored, `GET` serves them with BR-11 headers, one `image_updated` audit row; invalid → `400` `errors.file` / `413` / `415` with no change; `DELETE` → `204` with one `image_removed` row, the second `204` without audit; `updated_at` of the item unchanged |
| AC-12 | A manager in the drawer | They drop/choose an image, pick an invalid one, remove and replace it, save a new item with an image, and simulate an image failure after the item saves | Client validation shows BR-11 messages; the preview, Replace and Remove work; the image request is sent only after the item save succeeds; on image failure the item stays saved, the drawer switches to edit mode and shows the image error |
| AC-13 | Items matching and not matching the filters, including cells starting with `=`, `+`, `-`, `@` and containing commas, quotes and newlines | Any CatalogView role calls `GET /catalog-items/export` with filters and sort | `text/csv` attachment `products-services-YYYY-MM-DD.csv` with the BR-13 header and only matching rows of the session organization in sort order; RFC 4180 quoting; dangerous cells prefixed with `'`; **Export list** sends the current filters and downloads it |
| AC-14 | An Owner | They select **Download CSV** (and the dialog's Download template) | `products-services-template.csv` containing only the BR-13 header row downloads |
| AC-15 | An Owner and a valid CSV of mixed rows (case variants, empty taxable/active, columns in another order) | `POST /catalog-items/import` | `200 { importedCount }`; every row created with BR-07 normalization and defaults in one transaction; one `catalog_item.imported` audit row; table and metrics refresh |
| AC-16 | CSV files failing one rule each: >1 MB, non-UTF-8, wrong/missing/extra header, no rows, 501 rows, invalid type/money/boolean/name, duplicates inside the file, duplicate of an existing item (parameterized) | `POST /catalog-items/import` | `400` with `errors.file` or `rowErrors` (`row` = file line, column, BR-13 message, ≤100 entries) and zero items created, including when only the last row is invalid |
| AC-17 | An Owner | They open Import CSV, choose a file with errors, then a valid file, and cancel once | The dialog follows UI behavior; row errors are listed and the dialog stays open; success closes it with "Imported {n} items" and refreshes; Cancel imports nothing |
| AC-18 | Sessions for `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`, `technician` and none (parameterized) | Every endpoint of the API contracts is called | Results follow BR-01: CatalogView reads allowed for the five view roles; manage endpoints only Owner/Operations Manager; template/import only Owner; every denied call `403` with no change; technician `403` everywhere; no session `401` |
| AC-19 | Managers of organizations A and B | A calls list, summary and export, then every `{id}` route with B's item id, and imports a name that exists only in B | A never sees B's rows, counts or usage; every `{id}` call returns `404` with B unchanged; the import succeeds because uniqueness is per organization |
| AC-20 | An Owner, an Operations Manager, a Viewer (read role) and a Technician at `/admin/products-services` | The page renders and menus/drawer open | UI follows BR-02: Owner all controls; Operations Manager without Import CSV/Download CSV and without the Administration column; Viewer banner, only Export list, View details opens the read-only "Item details" drawer with usage and image and a Close button; Technician forbidden state |
| AC-21 | An Owner and a Viewer | `GET /permission-matrix` and the Design 18 matrix render | Ten modules are returned and shown, including "Products & services" after Invoices & payments with BR-18 levels and tones; nothing else in the matrix changes |
| AC-22 | Viewports 390, 800, 1280 and 1440px (parameterized), and 320px | The page and drawer render | 1440 docked 400px drawer; 1280 overlay drawer with mask and focus trap; 800 rail sidebar, Administration link row, table scrolling inside its card; 390 sidebar drawer, 2×2 metrics, scrolling tabs, stacked filters, item cards, full-screen drawer with sticky footer; no horizontal page scroll at 320px; mobile touch targets ≥44px |
| AC-23 | The implementation and `19-design.png` at 1440×900 with the drawer open | They are compared (USER VISUAL QA REPORT) | Structure, shell dimensions, tokens, metric cards, tabs, filters, table columns and tags, notes, footer buttons and drawer sections (margin card, switches, image zone, usage, footer) match within ±2px; fixed copy matches BR-14; only data and the BR-14 navigation differ |
| AC-24 | The page, drawer, confirmation and import dialog | A keyboard pass and accessibility review run | Every control is reachable with visible focus; labels, `aria-label`s, `th` scopes, sort state, live margin, drawer/dialog focus trap and return follow UI behavior; color is never the only signal |
| AC-25 | An empty test database and the generated migration | The migration applies in the test container and items/images are written | `is_taxable`, `normalized_name`, the unique index and `catalog_item_images` exist per the amended schema doc; a same-organization duplicate type+name violates the index; the EF model matches the amended schema (database-reviewer) |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit (≤4) | Margin/gross profit rule incl. price 0, negative and rounding; name normalization; CSV row parsing and header validation table | AC-02, AC-06, AC-08, AC-16 |
| Backend integration (≈8, parameterized) | List/summary filters, sort, paging and counts; create/update validation, duplicates and audit; activate/deactivate history preservation; usage distinct counts; image upload/type/size/headers; export content and injection neutralization; import success and all-or-nothing failures | AC-02, AC-03, AC-05–AC-07, AC-09–AC-11, AC-13–AC-16, AC-25 |
| Authorization/tenant isolation | One role × endpoint matrix test; one cross-organization test over list/summary/export and all `{id}` routes plus per-organization uniqueness; permission matrix module | AC-18, AC-19, AC-21 |
| Frontend component/service (4–6) | List state (tabs↔Type sync, filters, paging, empty/error/partial metrics); drawer validation, live margin and 409 mapping; staged image flow incl. post-save failure; role-based UI (Owner/Operations Manager/read-only/forbidden); import dialog errors and success; export/template requests | AC-01, AC-04, AC-08, AC-12, AC-14, AC-17, AC-20 |
| Persistence review | database-reviewer on the BR-17 amendment, mapping and migration | AC-25 |
| User visual QA (manual, user-owned) | Desktop fidelity, responsive and accessibility pass; agents run no browser tooling | AC-22, AC-23, AC-24 |

## Dependencies

- `design-17-company-setup-completion` (shell, Administration column, Coming
  soon slugs) — APPROVED/implemented.
- `design-18-users-and-permissions` (permission matrix contract BR-07) —
  APPROVED/implemented; this spec extends its matrix to ten modules, so its
  matrix evidence must be updated with the new row.
- Existing session authentication, ProblemDetails errors, audit writer and the
  logo upload validator pattern (`company-settings-and-branches`, Design 17).
- `--generate-migration` authorization for implementation (granted by the user
  2026-09-30; generation only).

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Money is displayed with the symbol of the BR-05 `currency` code; values are not converted. |
| AS-02 | The top-bar branch selector does not affect the catalog (organization-wide). |
| AS-03 | Both footer notes are static copy for every permitted role. |
| AS-04 | Edits are last-write-wins; no concurrency token is added (not required by the design). |
| AS-05 | Description limit of 1,000 characters is a UI/API limit only; the column stays `text`. |
| AS-06 | The Administration column keeps the existing Business hours item; Products & services is inserted after Users & permissions following the mockup order. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Design reference without handoff | PNG only; responsive from Design 17/18 | No | PNG only, responsive from Design 17/18; no sample data or legacy navigation (user, 2026-09-30) |
| OD-02 | Route | `/admin/products-services` in Administration; module route | No | `/admin/products-services` (user, 2026-09-30) |
| OD-03 | Permissions | Custom split | No | View: owner, operations_manager, dispatcher, accounting, viewer; manage: owner, operations_manager; import: owner; technician none; update Design 18 matrix (user, 2026-09-30) |
| OD-04 | Taxable vs `tax_rate` | Add `is_taxable`; derive from `tax_rate` | No | Add `is_taxable` (user, 2026-09-30) |
| OD-05 | Image storage | `catalog_item_images`; exclude | No | `catalog_item_images` (user, 2026-09-30) |
| OD-06 | Usage definitions | As BR-10 | No | Confirmed with distinct counts across sources (user, 2026-09-30) |
| OD-07 | Delete | None; delete when unused | No | No hard delete (user, 2026-09-30) |
| OD-08 | CSV buttons | Export list = filtered export, Download CSV = template | No | Confirmed (user, 2026-09-30) |
| OD-09 | Import rules and uniqueness | BR-13; app-only or DB-enforced uniqueness | No | Confirmed; uniqueness by organization + type + normalized name enforced by DB constraint, duplicate → `409` (user, 2026-09-30) |
| OD-10 | Metrics vs tab counts | Active-only metrics; tabs all statuses | No | Accepted (user, 2026-09-30) |
| OD-11 | Audit | BR-16 | No | Yes, including image update/removal (user, 2026-09-30) |
| OD-12 | Schema amendment and migration | Amend doc first, generate migration, never apply | No | Authorized (user, 2026-09-30) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-23 |
| FR-02 | AC-02 |
| FR-03 | AC-03 |
| FR-04 | AC-04 |
| FR-05 | AC-05, AC-06 |
| FR-06 | AC-10, AC-20 |
| FR-07 | AC-06, AC-07 |
| FR-08 | AC-08, AC-12 |
| FR-09 | AC-09 |
| FR-10 | AC-10 |
| FR-11 | AC-11, AC-12 |
| FR-12 | AC-13 |
| FR-13 | AC-14 |
| FR-14 | AC-15, AC-16, AC-17 |
| FR-15 | AC-05, AC-07, AC-09, AC-11, AC-15 |
| FR-16 | AC-18, AC-19 |
| FR-17 | AC-20 |
| FR-18 | AC-21 |
| FR-19 | AC-22, AC-23, AC-24 |
| FR-20 | AC-25 |

## Change log

| Date | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-30 | — → DRAFT | Created from Design 19 with the user's decisions OD-01 to OD-12 |
| 2026-09-30 | DRAFT → DRAFT | Validation fixes: currency added to the summary contract for all CatalogView roles; data impact cites `organizations.currency`/`timezone`; import `413`/`415` limits and messages; AC-25 cascade assertion removed |
| 2026-09-30 | DRAFT → APPROVED | Approved by user via /spec approve |
