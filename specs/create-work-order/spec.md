# Create work order

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `create-work-order` |
| Type     | Full-stack          |
| Status   | APPROVED            |
| Created  | 2026-10-06          |
| Updated  | 2026-10-06          |
| Approved | 2026-10-06          |

## Context and objective

An approved quote (`customer-quote-approval`) has no executable follow-up.
This feature turns the approved `QuoteVersion` into one `WorkOrder` through an
idempotent operation. Staff complete job details, tasks, planned materials,
instructions, scheduling requirements and communication preferences, can save
a draft, and then create the order. Creation prepares one unscheduled visit.
Prices stay frozen from the approved version. Scheduling, availability and
technician assignment belong to Design 6.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Operations Manager (`operations_manager`) | Open the editor for any approved quote of the organization; save drafts; create work orders; list and save checklist templates; read jobs | Edit a created work order; schedule or assign |
| Dispatcher (`dispatcher`) | The same as Owner for quotes and work orders in branch scope (BR-03) | Quotes, work orders or branches outside scope (`404`, or `400 branchId` for a target branch) |
| Viewer (`viewer`) | Read the jobs list and job detail | Use the editor, any mutation or the template endpoints (`403`) |
| Accounting (`accounting`), Technician (`technician`), unknown roles | — | Every endpoint of this feature (`403`) |

## Scope

- An internal route that opens the work order editor for an approved quote
  (Design 5).
- Read-only (locked) context: customer, property, approved quote and the
  latest completed assessment with its photos.
- Job details: title, job type, service category, branch, priority,
  estimated duration and required skills.
- Tasks entered manually or appended from organization checklist templates,
  plus saving the current tasks as a new template.
- Planned materials, prefilled from the approved product lines or added from
  the catalog or manually. No stock is deducted.
- Technician instructions (editable), plus the property access note and
  assessment photos (read-only).
- Scheduling requirements (preferred date, arrival window and recurrence) and
  stored customer communication preferences.
- **Save draft** and **Create work order**, with an idempotent create. One
  approved quote yields at most one work order.
- The creation of visit #1 as `unscheduled`.
- The **Jobs** sidebar entry, a minimal jobs list and a read-only job detail.
- The work order action on `/quotes/:quoteId`.

## Non-goals

- Calendar, drag-and-drop scheduling, availability checks and technician
  assignment (Design 6). **Check availability** and **Assigned technician**
  are shown disabled.
- Materializing recurring occurrences or visits beyond #1 (Design 6).
- Sending emails, SMS, reminders or in-app notifications. Preferences are only
  stored.
- Technician execution, time entries, evidence, actual material consumption,
  inventory, stock levels or reservations.
- Invoicing, payments and any change to prices, quote lines, quote versions
  or quote responses.
- Editing, cancelling or deleting a created work order, and deleting a draft.
- A checklist template management screen (edit, deactivate or delete).
- Linking a customer or property to a guest request from this screen.
- The split-button menu next to **Create work order**.

## User flow

1. A manager opens an approved quote at `/quotes/:quoteId` and selects
   **Create work order** (or **Continue draft**).
2. The system opens `/quotes/:quoteId/work-order` with the locked context and
   the prefilled form. Opening it writes nothing.
3. The manager edits job details, tasks, materials, instructions, scheduling
   requirements and communication preferences.
4. **Save draft** stores a `draft` work order with a number. It does not
   change the request, create visits or write audit rows.
5. **Create work order** revalidates the quote approval. In one transaction,
   the order becomes `ready_to_schedule`, the request becomes `converted`,
   visit #1 is created as `unscheduled` and the audit row is written.
6. The system navigates to `/jobs/:workOrderId` and shows the read-only job.
7. Repeating creation, from any tab or request, returns the same work order.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The quote page must offer the work order action for approved quotes per BR-04. |
| FR-02 | The editor must load the locked context and a prefilled or saved form without writing data (BR-05, BR-06, BR-07). |
| FR-03 | The editor must validate every field and client identifier server-side (BR-08, BR-09). |
| FR-04 | Managers must manage tasks manually, with drag-and-drop and accessible reordering, and from checklist templates (BR-10, BR-11). |
| FR-05 | Managers must manage planned materials without changing approved prices or inventory (BR-12, BR-17). |
| FR-06 | Job type, recurrence, scheduling requirements and communication preferences must be stored as specified, with no visits beyond #1 and no notifications (BR-13, BR-14, BR-15). |
| FR-07 | **Save draft** must insert or update one `draft` work order with optimistic concurrency and no side effects (BR-16). |
| FR-08 | **Create work order** must revalidate the approval and atomically create the executable order, visit #1, the request transition and the audit row (BR-18, BR-19). |
| FR-09 | Creation must be idempotent: one work order per approved quote, under repeats and concurrency (BR-20). |
| FR-10 | Prices must stay frozen from the approved version (BR-17). |
| FR-11 | Every endpoint must resolve the organization from the session and apply role and branch scope (BR-01, BR-02, BR-03). |
| FR-12 | The sidebar must show **Jobs**, with a minimal list and a read-only job detail (BR-21, BR-22). |
| FR-13 | The editor must reproduce Design 5 with the specified states and responsive behavior (BR-23, BR-24). |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Policies. **Manage** = `owner`, `operations_manager`, `dispatcher`. It covers the editor, draft, create and checklist template endpoints. **Read** = Manage + `viewer`. It covers the jobs list and detail. Every other role → `403`. Frontend visibility is UX only. | Backend |
| BR-02 | Quote access. Editor endpoints load the quote through the `quote-builder` BR-07 visibility (organization + request branch scope). A quote of another organization or outside scope → `404`, identical to a nonexistent id. | Backend |
| BR-03 | Work order scope. Branch scope follows `requests-pipeline` BR-02: `owner`, `operations_manager`, `viewer` and members with `is_all_branches` see all branches, while a `dispatcher` sees only their `organization_user_branches`. A work order is visible when its `branch_id` is in scope. Otherwise `GET /work-orders/{id}` → `404` and the list excludes it. The target `branchId` must be an active branch of the organization in the caller's scope, else `400 errors.branchId`. | Backend |
| BR-04 | Quote page action. `QuoteDetail` adds `workOrder: { id, displayNumber, status } \| null` (the order of the quote's `approved_version_id`) and `canManageWorkOrders`. On `/quotes/:quoteId` with `status = approved`: no order → **Create work order** (primary, Manage only). `draft` → **Continue draft** (Manage only). Created (status ≠ `draft`) → **View job** (`/jobs/:id`, Read roles). Non-approved quotes show none of these. | Both |
| BR-05 | Editor load. `GET /quotes/{quoteId}/work-order` requires `quotes.status = approved` and a non-null `approved_version_id`, else `409 quote_not_approved`. It also requires non-null `quotes.customer_id` and `property_id`, else `409 customer_required`. It returns `WorkOrderEditor` and writes nothing. When the order exists with status ≠ `draft`, the page navigates to `/jobs/:id`. | Both |
| BR-06 | Locked context (Design 5 left column, lock icons, never editable). **Customer (from quote)**: initials, display name, the phone and email of the customer's primary active contact (each hidden when null; phone as a `tel:` link, email as a `mailto:` link), and the property address. **Approved quote**: "#<Q-n>", "Approved <MMM d, yyyy> at <h:mm a>" from the approval `responded_at` (organization time zone), the approved total from the approval `quote_responses.total` in the quote currency, and **View approved quote** (`/quotes/:quoteId`). **Assessment (from quote)**: the latest `completed` assessment of the request (by `completed_at`). It shows the technician's initials and name ("Unassigned" when null), **Diagnosis** ("No diagnosis recorded." when empty) and up to 6 photo thumbnails, each opening the accessible full-size viewer (previous/next, Escape closes). Photos use the existing `quote-builder` assessment photo endpoint. With no completed assessment, the card shows "No assessment recorded for this quote." | Both |
| BR-07 | Prefill (when no draft exists). Title = the version `scope`, trimmed and truncated to 160 characters. Job type `one_time`. Category = the request `category_id` when active, else empty. Branch = the quote `branch_id`, else the request branch, else the customer branch, kept only when active and in the caller's scope, else empty. Priority from the request urgency: `emergency` → Urgent, `urgent` → High, `standard` → Normal. Duration and skills empty. Tasks = the names of the approved service lines (non-optional plus optional lines selected in the approval), in `sort_order`, truncated to 240. Materials = the approved product lines (same selection rule), with description = name truncated to 240, the quantity, the unit, source `truck_stock` and `quoteLineId`. Instructions empty. No preferred date, window "Any time", recurrence null and all three communication preferences true. With a draft, its saved values are returned instead. | Backend |
| BR-08 | Field validation, applied identically by draft and create (table below). Errors return `400` ProblemDetails with `errors.<field>`, shown inline under the field, with focus moved to the first invalid field. Nothing is saved. | Both |
| BR-09 | Client identifiers. `serviceCategoryId` must be an active category of the organization. `skillIds` must be distinct active skills of the organization. `catalogItemId` must be an active `product` catalog item of the organization. `quoteLineId` must be a `product` line of the quote's approved version, either non-optional or selected in the approval, and appear at most once. `branchId` follows BR-03. Any failure → `400` on that key (`errors.serviceCategoryId`, `errors.skillIds`, `errors.materials[i].catalogItemId`, `errors.materials[i].quoteLineId`, `errors.branchId`) with no data of the foreign row. | Backend |
| BR-10 | Tasks (**Tasks & checklist**). Each row has a drag handle, a non-interactive checkbox (completion belongs to execution), the editable label, **Move up**, **Move down** and **Remove**. The buttons have accessible names "Move <label> up", "Move <label> down" and "Remove <label>". The first row's **Move up** and the last row's **Move down** are disabled. Drag-and-drop and the buttons produce the same order. Each move is announced politely ("<label> moved to position <n> of <total>"), and focus stays on the moved control. **Add task** appends an empty row and focuses it. All tasks are required (`is_required = true`), and `sort_order` follows list order. | Both |
| BR-11 | Checklist templates. **Use checklist template** opens a dialog listing the organization's active templates: first those whose category equals the selected category, then the rest, each group alphabetical, with name and "<n> tasks". Selecting one and confirming appends its item labels after the existing tasks. If the result would exceed 50 tasks, nothing is appended and the dialog shows "A work order can have up to 50 tasks.". With no templates: "No checklist templates yet." The dialog also offers **Save current tasks as template** with **Template name** (1–120 characters after trim, unique per organization ignoring case → `400 errors.name` "A template with this name already exists."). It saves the current non-empty task labels (1–50 items, each 1–240 characters after trim → `400 errors.items`) and the selected category (or none; an inactive or foreign category → `400 errors.serviceCategoryId`). Templates are never edited or deleted here, and applying one keeps no link to it. | Both |
| BR-12 | Materials (**Required materials**). Columns: Material, Planned qty, Source, Status. Status always shows "Planned" (neutral), because no inventory exists. **Add material** opens a dialog: either pick an active product from `GET /catalog-items` (prefills description and unit), or type a manual description. Then set quantity, unit and source (**Truck stock**, **Warehouse**, **To purchase**). Rows can be edited or removed (accessible names "Edit <material>", "Remove <material>"). The footnote reads "Technician will record actual quantities used.". Materials are operational planning only. They have no price, are never billable, never create `visit_materials` and never touch stock. | Both |
| BR-13 | Job type and recurrence. `one_time` → **Recurrence** shows "Does not repeat" disabled, and `recurrence` must be null. `recurring` → **Recurrence** is required with frequency **Weekly**, **Every 2 weeks**, **Monthly** or **Quarterly** plus **Occurrences** (2–24). The values are stored only. No occurrence or visit beyond #1 is created by this feature. A mismatch → `400 errors.recurrence`. | Both |
| BR-14 | Scheduling requirements. **Preferred date** is optional and, when set, must be today or later in the selected branch's time zone (organization time zone when the branch has none). **Preferred arrival window** is enabled only when a date is set: Any time, 8:00 AM – 11:00 AM, 9:00 AM – 12:00 PM, 12:00 PM – 3:00 PM, 1:00 PM – 4:00 PM, 3:00 PM – 6:00 PM. The server stores `preferred_start` and `preferred_end` as instants of that local date and window ("Any time" = 00:00 to 24:00 local). With no date, both are null. **Assigned technician** shows "Not assigned" disabled, with the note "Assignment happens after the work order is created.". **Check availability** is disabled. | Both |
| BR-15 | Customer communication. Checkboxes **Notify customer when scheduled**, **Send technician details** and **Send arrival reminder 2 hours before**, all checked by default and stored as given. This feature sends nothing and creates no `notifications` rows. | Both |
| BR-16 | Save draft. `PUT /quotes/{quoteId}/work-order/draft` with `updatedAt` = null and no existing order inserts a `draft` order. Under an organization row lock, it allocates `work_order_number` from `next_work_order_number` and increments it. It stores the fields, skills, tasks and materials with `quote_version_id` = `approved_version_id`, `scope_snapshot` = the version scope, and the quote customer and property. The response is `201`. With an existing `draft` and a matching `updatedAt`, it replaces the fields, skills, tasks and materials, sets a new `updated_at` and returns `200`. A mismatched or null `updatedAt` while a draft exists → `409 work_order_changed`. An order that is no longer `draft` → `409 work_order_created`. A draft save never changes the request, creates visits, writes audit rows or sends anything. The header shows "Draft saved" when the form matches the saved draft, "Unsaved changes" after an edit, and nothing before the first save. | Both |
| BR-17 | Frozen prices. No work order request field carries a price, cost, tax or total, and unknown properties are ignored. Displayed money comes only from the approval `quote_responses` totals. No endpoint of this feature writes `quotes`, `quote_versions`, `quote_lines` or `quote_responses`. The summary shows the info banner "Pricing remains linked to the approved quote. Changes to billable items require customer approval." Billable changes require a quote revision, which is outside this feature. | Both |
| BR-18 | Create. `POST /quotes/{quoteId}/work-order` takes a row lock on the quote, then revalidates inside the transaction. The quote must still be `approved`, and its `approved_version_id` must equal the existing order's `quote_version_id` (when one exists), else `409 quote_not_approved`. The customer and property are required (`409 customer_required`). The request must be `quoted` (`409 request_changed`). It applies BR-08/BR-09 to the body. Then, in one transaction: insert the order or update the draft (with the BR-16 `updatedAt` check) using the body values; set `status = ready_to_schedule`; set the request to `converted` with a `request_status_history` row (`quoted` → `converted`, the actor, no reason); insert visit #1 (`visit_number = 1`, `unscheduled`, no schedule) with a `visit_status_history` row (null → `unscheduled`); copy each task to `visit_checklist_items` (label, `is_required`, `sort_order`, and `template_item_id` = the task's `work_order_checklist_templates` row); and write the BR-19 audit row. Any failure rolls back every write. Success → `201 { id, displayNumber }`. The page then navigates to `/jobs/:id` with the toast "Work order <WO-n> created.". | Backend |
| BR-19 | Audit. One `work_order.created` row with `entity_type` `work_order`, `entity_id`, the organization and `branch_id` of the order, and the actor. After data = `{ status }`. Metadata = `{ workOrderNumber, quoteId, quoteVersionId, visitId, taskCount, materialCount }`. It never contains customer contact data, instructions, the access note or material descriptions. Draft saves and idempotent repeats write no audit. `checklist_template.created` uses `entity_type` `checklist_template`, `entity_id` the template id, the organization, null `branch_id`, the actor, after data `{ name, serviceCategoryId }` and metadata `{ itemCount }`, never item labels. | Backend |
| BR-20 | Idempotency and concurrency. The `work_orders.quote_version_id` unique constraint is the backstop. When the quote's order already has status ≠ `draft`, `POST` returns `200 { id, displayNumber }` of that order, ignores the body and changes nothing. Concurrent creates serialize on the quote lock: exactly one transitions, and the others return `200` with the same id. Concurrent first drafts: one inserts, and the other gets `409 work_order_changed` (a unique violation is mapped the same way). The page disables **Save draft**, **Create work order** and **Cancel** while a request is pending and sends one at a time. | Both |
| BR-21 | Jobs navigation and list. The sidebar item "Work orders" becomes **Jobs** (same position and icon) and links to `/jobs`. Like the other operational items, it is shown to every authenticated role; roles without Read get the existing forbidden state on `/jobs` and `/jobs/:id`. `/jobs` shows a table of visible orders, newest first, 25 per page: **Job** ("#<WO-n>" + title), **Customer**, **Branch**, **Priority**, **Status** ("Draft" or "Unscheduled" for `ready_to_schedule`) and **Created** ("MMM d, yyyy" in the organization time zone). A row opens `/jobs/:id`. Empty: "No jobs yet. Jobs are created from approved quotes.". | Both |
| BR-22 | Job detail (`/jobs/:id`, read-only). Breadcrumb **Jobs** › "#<WO-n>". The title shows the status chip and priority. Dates and times use the organization time zone, except the preferred date and window, which use the order's branch time zone (BR-14). It shows the customer and property, the linked quote ("#<Q-n>", approved total, link to the quote), job details (type, category, branch, duration, skills, recurrence), scheduling requirements, tasks, planned materials, instructions, the access note, communication preferences and **Visits** ("Visit #1 · Unscheduled", or "No visits yet." for a draft). For a `draft`, Manage roles also see **Continue editing** (`/quotes/:quoteId/work-order`). There are no other actions. | Both |
| BR-23 | Editor layout (Design 5). Breadcrumb **Quotes** (plain text) › "<Q-n>" (`/quotes/:quoteId`) › **Create work order**. Title "Create work order" with the chip "Quote approved" (success). Subtitle "Convert the approved scope into an executable job.". Header right: "From quote #<Q-n>" and the BR-16 save state. The **Job details** status shows the chip "Unscheduled" and "Will be available for scheduling after creation.". **Instructions (visible to assigned technician)**: **Technician instructions** (editable), **Attachments (from assessment)** (read-only thumbnails, the BR-06 viewer; "No attachments." when none) and **Access note** (read-only `properties.access_instructions`; "No access note." when empty). **Work order summary**: Status "Unscheduled", Tasks count, Planned materials count, Estimated duration ("—" when empty), Linked quote "<total> (#<Q-n>)", and the BR-17 banner. Footer: **Cancel**, **Save draft** and **Create work order** (primary, no dropdown). **Cancel** with unsaved changes opens the existing discard-changes dialog, then navigates to `/quotes/:quoteId`. The same guard applies to any navigation away. | Frontend |
| BR-24 | Responsive. At ≥ 1280 px, three columns as Design 5 (locked context · form · scheduling, communication, summary and actions). From 768 to 1279 px, two columns: the form column on the left; the locked cards, scheduling, communication and summary on the right; actions at the end. Below 768 px, one column in this order: locked cards, job details, tasks, materials, instructions, scheduling, communication, summary, then an action bar fixed to the bottom. The materials table scrolls horizontally inside its card. There is no page-level horizontal scroll at 375 px, and every control keeps a visible focus indicator. | Frontend |
| BR-25 | Schema amendment, as defined in [BR-25 Schema amendment](#br-25-schema-amendment). | Backend |

### Field validation (BR-08)

| Field | Rule | Error (`errors.<key>`) |
| ----- | ---- | ---------------------- |
| `title` | Required, 1–160 characters after trim | "Enter a work order title." / "Title must be 160 characters or fewer." |
| `jobType` | `one_time` \| `recurring` | "Select a job type." |
| `serviceCategoryId` | Required (BR-09) | "Select a service category." |
| `branchId` | Required (BR-03) | "Select a branch." |
| `priority` | `urgent` (1), `high` (2), `normal` (3), `low` (4) | "Select a priority." |
| `estimatedDurationMinutes` | Null or 30–720 in steps of 30 (shown "30 min", "1h", "1.5h" … "12h") | "Select a valid duration." |
| `skillIds` | 0–10 (BR-09) | "Select up to 10 skills." |
| `tasks` | 1–50 rows. `label` 1–240 characters after trim | `tasks`: "Add at least one task." / "A work order can have up to 50 tasks."; `tasks[i].label`: "Enter a task." / "Task must be 240 characters or fewer." |
| `materials` | 0–50 rows. `description` 1–240 characters. `quantity` > 0 and ≤ 99999.999, with up to 3 decimals. `unit` 1–40 characters. `source` ∈ `truck_stock`, `warehouse`, `to_purchase` | "Enter a material." · "Enter a quantity greater than 0." · "Enter a unit." · "Select a source." |
| `instructions` | 0–2000 characters after trim (empty → null) | "Instructions must be 2000 characters or fewer." |
| `preferredDate`, `arrivalWindow` | BR-14. A window without a date is rejected | "Select today or a later date." / "Select a preferred date first." |
| `recurrence` | BR-13 | "Select how often this job repeats." / "Enter between 2 and 24 occurrences." |
| `communication.*` | Booleans, required | — |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| (none) | `draft` | Save draft | Manage | BR-05, BR-08, BR-09; no existing order |
| `draft` | `draft` | Save draft | Manage | Matching `updatedAt` |
| (none) or `draft` | `ready_to_schedule` | Create work order | Manage | BR-18 |
| `ready_to_schedule` | — | Create repeated | Manage | `200` existing, no change (BR-20) |
| Request `quoted` | `converted` | Create work order | Manage | Same transaction as BR-18 |
| Visit (none) | `unscheduled` (#1) | Create work order | Manage | Same transaction as BR-18 |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `work_orders` (amended): insert and update while `draft`; status to
    `ready_to_schedule`; read.
  - `work_order_required_skills`, `work_order_checklist_templates` (per-order
    task rows): replaced on draft save and create.
  - `work_order_planned_materials` (new): replaced on draft save and create.
  - `checklist_templates`, `checklist_template_items` (new): insert and read.
  - `visits`, `visit_status_history`, `visit_checklist_items`: insert on
    create.
  - `service_requests`, `request_status_history`: `quoted` → `converted`.
  - `organizations`: `work_order_prefix`, `next_work_order_number` (locked
    increment), `timezone`, `currency`.
  - `audit_logs`: insert.
  - Read only: `quotes`, `quote_versions`, `quote_lines`, `quote_responses`,
    `quote_response_optional_lines`, `customers`, `customer_contacts`,
    `properties`, `branches`, `service_categories`, `skills`,
    `catalog_items`, `assessments`, `assessment_attachments`,
    `technician_profiles`.
  - Not written: `quotes`, `quote_versions`, `quote_lines`, `quote_responses`,
    `visit_materials`, `visit_assignments`, `notifications`.
- Schema amendments required (BR-25 below), **confirmed by the user
  2026-10-06** (OD-01). Implementation updates
  `docs/database/fieldops-schema.sql` and the EF model, and generates one
  migration under `--generate-migration`. Agents never apply it. The mapping
  change requires `database-reviewer`. No feature has written `work_orders`
  rows yet, so the new NOT NULL columns need no backfill.

### BR-25 Schema amendment

- `work_orders` adds:
  - `title varchar(160) NOT NULL`
  - `job_type varchar(20) NOT NULL DEFAULT 'one_time' CHECK (job_type IN ('one_time','recurring'))`
  - `service_category_id uuid NOT NULL` with
    `FOREIGN KEY(organization_id,service_category_id) REFERENCES service_categories(organization_id,id)`
  - `estimated_duration_minutes integer CHECK (estimated_duration_minutes BETWEEN 30 AND 720 AND estimated_duration_minutes % 30 = 0)`
  - `recurrence_frequency varchar(20) CHECK (recurrence_frequency IN ('weekly','biweekly','monthly','quarterly'))`
    and `recurrence_count smallint CHECK (recurrence_count BETWEEN 2 AND 24)`,
    with `CHECK ((job_type = 'recurring') = (recurrence_frequency IS NOT NULL AND recurrence_count IS NOT NULL))`
    and `CHECK ((recurrence_frequency IS NULL) = (recurrence_count IS NULL))`
  - `notify_customer_when_scheduled`, `send_technician_details`,
    `send_arrival_reminder`, each `boolean NOT NULL DEFAULT true`
  - `CHECK ((preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end))`
  - Composite tenant FKs `FOREIGN KEY(organization_id,branch_id) REFERENCES branches(organization_id,id)`
    and `FOREIGN KEY(organization_id,quote_version_id) REFERENCES quote_versions(organization_id,id)`
  - Index `ix_work_orders_org_created ON work_orders(organization_id, created_at DESC)`
- New `work_order_planned_materials`: `id uuid PK`,
  `organization_id uuid NOT NULL REFERENCES organizations(id)`,
  `work_order_id uuid NOT NULL`, `quote_line_id uuid REFERENCES quote_lines(id)`,
  `catalog_item_id uuid`, `description varchar(240) NOT NULL`,
  `quantity numeric(12,3) NOT NULL CHECK(quantity>0)`, `unit varchar(40) NOT NULL`,
  `source varchar(20) NOT NULL CHECK (source IN ('truck_stock','warehouse','to_purchase'))`,
  `sort_order integer NOT NULL DEFAULT 0`,
  `FOREIGN KEY(organization_id,work_order_id) REFERENCES work_orders(organization_id,id) ON DELETE CASCADE`,
  `FOREIGN KEY(organization_id,catalog_item_id) REFERENCES catalog_items(organization_id,id)`,
  index on `work_order_id`. Its version and selection for `quote_line_id` are
  enforced by the application (BR-09).
- New `checklist_templates`: `id uuid PK`,
  `organization_id uuid NOT NULL REFERENCES organizations(id)`,
  `service_category_id uuid` with
  `FOREIGN KEY(organization_id,service_category_id) REFERENCES service_categories(organization_id,id)`,
  `name varchar(120) NOT NULL`, `is_active boolean NOT NULL DEFAULT true`,
  `created_by_user_id uuid NOT NULL REFERENCES users(id)`, `created_at`,
  `updated_at`, `UNIQUE(organization_id,id)`, and the unique index
  `ux_checklist_templates_org_name ON checklist_templates(organization_id, lower(name))`.
- New `checklist_template_items`: `id uuid PK`,
  `organization_id uuid NOT NULL REFERENCES organizations(id)`,
  `template_id uuid NOT NULL`, `label varchar(240) NOT NULL`,
  `sort_order integer NOT NULL DEFAULT 0`,
  `FOREIGN KEY(organization_id,template_id) REFERENCES checklist_templates(organization_id,id) ON DELETE CASCADE`.
- Child tables without `organization_id` (`work_order_required_skills`,
  `work_order_checklist_templates`, `visit_checklist_items`) are unchanged.
  The application verifies that `skill_id` belongs to the organization
  (schema invariant 1).

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  body or query carries an organization id. Any such field is ignored.
- Client identifiers, verified server-side against the session organization
  and the branch scope: `quoteId` and work order `id` in paths (`404`);
  `branchId`, `serviceCategoryId`, `skillIds`, `catalogItemId` and
  `quoteLineId` in bodies (BR-03, BR-09 → `400` with no foreign data);
  template `serviceCategoryId` (`400`).
- A quote or work order of another organization, or outside branch scope →
  `404`, identical to a nonexistent id. The list never includes them.
- Templates are organization-scoped. Another organization's templates are
  never listed.
- Composite FKs (BR-25) enforce same-organization rows for the branch, the
  quote version, the category, planned materials and template items.
- Policies per BR-01. Responses use `Cache-Control: no-store`.
- Concurrency: the quote row lock (BR-18), the organization row lock for
  numbering (BR-16), the `updated_at` token, and the
  `work_orders.quote_version_id` unique constraint (BR-20).

## API contracts

Contract status: Final. Errors use ProblemDetails, and every `409` carries
`code`. Dates are `YYYY-MM-DD`, instants are ISO with offset, money is a
decimal with 2 decimals, and quantities have up to 3.

`WorkOrderBody` = `{ title, jobType, serviceCategoryId, branchId, priority,
estimatedDurationMinutes | null, skillIds: [], tasks: [{ label }], materials: [{ quoteLineId?, catalogItemId?, description,
quantity, unit, source }], instructions | null, preferredDate | null,
arrivalWindow: 'any' | '08-11' | '09-12' | '12-15' | '13-16' | '15-18',
recurrence: { frequency, count } | null, communication: {
notifyCustomerWhenScheduled, sendTechnicianDetails, sendArrivalReminder } }`.

`WorkOrderEditor` = `{ quote: { id, displayNumber, approvedAt,
approvedTotal, currency }, requestId, customer: { name, phone | null, email |
null, address }, accessNote | null, assessment: { id, technicianName | null,
diagnosis | null, photos: [{ id }] } | null, workOrder: { id, displayNumber,
status, updatedAt } | null, values: WorkOrderBody, options: { branches: [{
id, name, timezone }], categories: [{ id, name }], skills: [{ id, name }] },
organizationTimezone }`.

`WorkOrderListItem` = `{ id, displayNumber, title, customerName, branchName,
priority, status, createdAt }`.

`WorkOrderDetail` = `{ id, displayNumber, status, updatedAt, canManage,
quote: { id, displayNumber, approvedTotal, currency }, customer, propertyAddress,
accessNote, jobType, category, branch, priority, estimatedDurationMinutes,
skills, recurrence, preferredStart, preferredEnd, arrivalWindow,
preferredDate, tasks, materials, instructions, communication, visits: [{
visitNumber, status }] }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/quotes/{quoteId}/work-order` | — | `200 WorkOrderEditor` | `403` · `404` · `409` (`quote_not_approved`, `customer_required`) | Manage |
| PUT | `/quotes/{quoteId}/work-order/draft` | `WorkOrderBody` + `updatedAt \| null` | `201 WorkOrderEditor` (inserted) · `200 WorkOrderEditor` | `400` (BR-08/BR-09 keys) · `403` · `404` · `409` (`quote_not_approved`, `customer_required`, `work_order_changed`, `work_order_created`) | Manage |
| POST | `/quotes/{quoteId}/work-order` | `WorkOrderBody` + `updatedAt \| null` | `201 { id, displayNumber }` · `200 { id, displayNumber }` (already created) | `400` · `403` · `404` · `409` (`quote_not_approved`, `customer_required`, `request_changed`, `work_order_changed`) | Manage |
| GET | `/work-orders?page=&pageSize=` | — | `200 { items: WorkOrderListItem[], total }` (pageSize 25, maximum 100) | `400` (paging) · `403` | Read |
| GET | `/work-orders/{id}` | — | `200 WorkOrderDetail` | `403` · `404` | Read |
| GET | `/checklist-templates` | — | `200 [{ id, name, serviceCategoryId \| null, items: [{ label }] }]` (active only) | `403` | Manage |
| POST | `/checklist-templates` | `{ name, serviceCategoryId \| null, items: [{ label }] }` | `201 { id, name, serviceCategoryId, items }` | `400` (`name`, `serviceCategoryId`, `items`) · `403` | Manage |
| GET | `/quotes/{id}` | Unchanged | Adds `workOrder` and `canManageWorkOrders` (BR-04) | Unchanged | Read (unchanged) |

- Reused unchanged: `GET /catalog-items` (the material picker, filtered to
  active products) and
  `GET /service-requests/{id}/assessments/{assessmentId}/photos/{photoId}`.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/quotes/:quoteId/work-order` | Skeleton of the three columns. Actions disabled | No assessment, photos or access note: BR-06/BR-23 texts | `409 quote_not_approved`: "This quote is no longer approved." with **Back to quote**. `409 customer_required`: "Link a customer and property to this request before creating a work order." with **Back to quote**. `404`: the existing not-found state. Other: "We couldn't load this work order. Try again." with **Retry** | `403`: the existing forbidden state | BR-06, BR-07, BR-23 |
| Save draft / Create | Button progress. Save, Create and Cancel disabled. One request at a time | — | `400`: inline errors with focus on the first one. `409 work_order_changed`: "This work order was changed by someone else. Reload to see the latest version." with **Reload** (input kept until reload). `409 work_order_created`: navigate to `/jobs/:id` with the toast "This work order has already been created.". `409 request_changed`: "The request changed. Reload and try again." Other: "We couldn't save the work order. Try again." with input kept | — | Draft: "Draft saved". Create: navigate with the BR-18 toast |
| Template dialog | Spinner in the list | "No checklist templates yet." | Load failure: "We couldn't load templates." with **Retry**. `400 name` inline | Manage only | Items appended, or template saved with the toast "Template saved." |
| Material dialog | Catalog search spinner | "No products found." | Inline field errors | — | Row added or updated |
| `/jobs` | Table skeleton | BR-21 empty text | "We couldn't load jobs. Try again." with **Retry** | `403`: forbidden state | BR-21 |
| `/jobs/:id` | Skeleton | "No visits yet." for a draft | `404` not-found state; other with **Retry** | `403`: forbidden state | BR-22 |

- Mockups: `design/assets/5-design.png` (Approved; primary reference). Data
  shown in it are examples. The jobs list and detail have no mockup. They use
  the existing table and card patterns, with the content fixed by BR-21 and
  BR-22.
- Responsive and accessibility: BR-10, BR-24. Locked cards expose their lock
  icon with the accessible name "Locked, from quote". Status chips use text
  plus an icon. Disabled scheduling controls keep their explanatory text.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Quote not approved, or `approved_version_id` mismatch | `409 quote_not_approved`; BR-23 blocked state | None |
| Guest quote without customer or property | `409 customer_required` | None |
| Invalid field or foreign identifier | `400 errors.<key>`; inline | None |
| Stale `updatedAt` or concurrent first draft | `409 work_order_changed` | None |
| Draft save after creation | `409 work_order_created`; navigate to the job | None |
| Create repeated or concurrent | `200` with the existing id | None |
| Request no longer `quoted` at create | `409 request_changed` | None (full rollback) |
| Duplicate template name | `400 errors.name` | None |
| Other organization or out-of-scope quote or work order | `404` | None |
| Role without permission | `403` | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An approved quote without an order, one with a draft, one with a created order, and a `sent` quote | An Owner, an Operations Manager, an in-scope Dispatcher and a Viewer open each `/quotes/<id>` | Managers see **Create work order**, **Continue draft** and **View job** respectively. The Viewer sees only **View job** for the created one. Nobody sees any of them on the `sent` quote |
| AC-02 | An approved quote with selected and unselected optional lines, an `urgent` request with a category, a completed assessment with photos, and a property access note | A manager opens the editor | The page matches Design 5 with the BR-06 locked cards (approved total from the approval) and the BR-07 prefill. Unselected optional lines appear in neither tasks nor materials. Priority is High, the status chip is "Unscheduled", the technician and **Check availability** are disabled, the three preferences are checked, the summary counts match, and the banner shows. No row is written |
| AC-03 | An approved quote whose request has no completed assessment and whose property has no access note | The editor loads | The assessment card shows "No assessment recorded for this quote.", attachments show "No attachments." and the access note shows "No access note.". Other sections render normally |
| AC-04 | The editor | Each invalid value of the BR-08 table is sent via draft and via create | Each returns `400` with the listed key and message, shown inline with focus on the first error. Nothing is inserted or changed |
| AC-05 | Identifiers of another organization (category, skill, catalog item), an inactive category, a branch outside the Dispatcher's scope, a quote line of another version, an unselected optional line and a service line | They are sent in a draft | Each returns `400` on its BR-09 key with no foreign data, and nothing is saved |
| AC-06 | An approved quote without an order | **Save draft** is used | `201`. A `draft` order exists with the next WO number (organization counter incremented once), the version id, scope snapshot, customer, property, fields, skills, tasks and materials. The header shows "Draft saved". The request stays `quoted`. No visit, audit or notification row exists |
| AC-07 | A saved draft | It is saved again with the current `updatedAt` after changing fields, removing a task and adding a material; then saved with the old `updatedAt` | The first replaces every value and returns a new `updatedAt`. The second returns `409 work_order_changed`, changes nothing, and the page shows the reload message with input kept |
| AC-08 | A saved draft | The manager returns via **Continue draft** | The editor shows the saved values, not the prefill, and the header shows "Draft saved". An edit switches it to "Unsaved changes" |
| AC-09 | A draft (and separately, no draft) on an approved quote | **Create work order** is used | `201`. The order is `ready_to_schedule` with the submitted values. The request is `converted` with one history row. Visit #1 is `unscheduled` with no schedule and one status history row. `visit_checklist_items` copy the tasks in order. Planned materials are stored, and no `visit_materials`, `visit_assignments` or `notifications` rows exist. One `work_order.created` audit row matches BR-19. The page opens `/jobs/<id>` with the toast |
| AC-10 | A created order | Create is repeated with a different body, three creates run concurrently on a fresh quote, and a draft save is sent for the created order | Repeats return `200` with the same id and change nothing (no second visit, history or audit). The concurrent run yields exactly one order, one visit and one audit row, and every response carries the same id. The draft save returns `409 work_order_created` |
| AC-11 | Quotes in `draft`, `sent`, `rejected` and `cancelled`, and a draft order whose `quote_version_id` differs from the quote's `approved_version_id` | The editor is loaded, a draft is saved and create is posted | Each returns `409 quote_not_approved` and changes nothing. The page shows the blocked state with **Back to quote** |
| AC-12 | An approved quote of a guest request without customer or property | The editor is opened and create is posted | Both return `409 customer_required`. The page shows the BR-23 message, and nothing is written |
| AC-13 | A draft whose request is moved out of `quoted` before create | Create is posted | `409 request_changed`. The draft is unchanged, the order stays `draft`, and no visit, checklist item, history or audit row exists |
| AC-14 | A created order whose materials were edited (manual material added, quantity changed, quoted material removed) | The quote, its versions, lines and responses are inspected, and the job detail is opened | Quote data are byte-identical to before. The approved total shown equals the approval total. Price fields in the body are ignored. The banner states that billable changes require approval |
| AC-15 | Five tasks in the editor | Tasks are added, edited and removed; one task is dragged; another is moved with **Move up**/**Move down** by keyboard; then the draft is saved and reloaded | Both methods give the same resulting order and announce "<label> moved to position <n> of <total>" with focus kept. Edge buttons are disabled. The saved order persists after reload. A 51st task is refused with "A work order can have up to 50 tasks." |
| AC-16 | Active templates in two categories, and an organization with none | **Use checklist template** is opened, a template is applied, a 48-task list receives a 5-item template, and **Save current tasks as template** is used with a new name and with an existing name ignoring case | The selected category's templates are listed first. Applying appends the items after the existing tasks. The overflow appends nothing and shows the limit message. The new name saves with the toast "Template saved." and one audit row. The duplicate returns `400 errors.name`. With none: "No checklist templates yet." |
| AC-17 | The materials card | A catalog product and a manual material are added, a row is edited and another removed | Catalog rows prefill description and unit. Only active products are offered. Every row shows source and the status "Planned", the footnote shows, and the summary count updates |
| AC-18 | The job details | Job type is `one_time`, then `recurring` without a frequency, with 25 occurrences, and valid (Monthly × 6); a crafted `one_time` body with a recurrence is posted | `one_time` shows "Does not repeat" disabled. The two invalid recurring cases return `400 errors.recurrence`. The valid one stores frequency and count, and create still yields only visit #1. The crafted body returns `400 errors.recurrence` |
| AC-19 | A branch in another time zone | A preferred date of yesterday (branch time zone), a window without a date, and a valid date with 9:00 AM – 12:00 PM are saved | The first two return `400`. The valid one stores `preferred_start`/`preferred_end` equal to 09:00 and 12:00 local in the branch time zone. The editor and job detail show the same date and window |
| AC-20 | Users of each role, a Dispatcher of branch A, and an Owner of another organization | They call each endpoint for a quote and order of branch B | Accounting, Technician and the Viewer (on Manage endpoints) get `403` and the forbidden state. The Dispatcher and the foreign Owner get `404` on the quote editor and job detail, and the list excludes them. The Viewer reads the list and detail. No foreign data is returned |
| AC-21 | Created and draft orders in visible and non-visible branches, and an organization with none | A user opens **Jobs** from the sidebar | The sidebar shows **Jobs** in place of "Work orders" and opens `/jobs`; an Accounting user following it sees the forbidden state. The table shows only visible orders, newest first, 25 per page, with the BR-21 columns and "Draft"/"Unscheduled" statuses. A row opens its detail. The empty organization shows the empty text |
| AC-22 | A created order and a draft | A manager and a Viewer open `/jobs/<id>` | It shows the BR-22 read-only content: "Visit #1 · Unscheduled" for the created order, and "No visits yet." for the draft. Only the manager sees **Continue editing**, and only on the draft. No edit actions exist on the created order |
| AC-23 | An editor with unsaved changes | **Cancel**, a sidebar link and browser back are used, then the form is saved and **Cancel** is used again; a save is clicked twice quickly | Each navigation with unsaved changes opens the discard dialog, and confirming leaves without saving. After saving, Cancel navigates directly to the quote. Double clicks send one request, with buttons disabled while pending |
| AC-24 | Viewports of 1440, 1024 and 375 px | The editor is rendered and used by keyboard | 1440 shows the three Design 5 columns. 1024 shows the two-column arrangement. 375 shows the single-column order with the bottom action bar and no page-level horizontal scroll, and the materials table scrolls within its card. All controls are reachable with visible focus |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Preferred window to instants in a branch time zone (including a DST date), and job type/recurrence consistency | AC-18, AC-19 |
| Backend integration | Editor load: prefill selection (approved, selected optional, unselected excluded), and `quote_not_approved`/`customer_required` guards with no writes | AC-02, AC-11, AC-12 |
| Backend integration | Draft lifecycle: insert with numbering, update, stale `updatedAt`, no side effects | AC-06, AC-07 |
| Backend integration | Validation and identifiers: table-driven BR-08 cases and the BR-09 foreign/invalid ids | AC-04, AC-05, AC-18, AC-19 |
| Backend integration | Transactional create: order, request transition, visit #1, checklist copy, planned materials, audit, no notifications or quote writes | AC-09, AC-14 |
| Backend integration | Idempotency: sequential repeat, concurrent creates, draft after creation | AC-10 |
| Backend integration | Approved version mismatch and `request_changed` with full rollback. The mismatch is unreachable through the API (`customer-quote-approval` BR-24), so it is arranged directly in test data | AC-11, AC-13 |
| Backend integration | Checklist templates: list order, create, duplicate name | AC-16 |
| Authorization/tenant isolation | One cross-organization `404` (editor and job), Dispatcher out-of-scope `404` and list exclusion, `403` for Accounting/Technician/Viewer on Manage | AC-05, AC-20 |
| Frontend component/service | Editor prefill rendering and summary counts; task reorder by drag and buttons with announcements; template apply and overflow; materials dialog; save state, submission locking, `409` handling and the discard guard; jobs nav, list and detail | AC-01, AC-02, AC-03, AC-08, AC-15, AC-16, AC-17, AC-21, AC-22, AC-23 |
| User visual QA (manual) | Design 5 fidelity and responsive arrangement | AC-02, AC-24 |

Backend integration totals 8 groups including authorization, the default
ceiling. Agents run no browser automation.

## Dependencies

- `customer-quote-approval` (AUDITED): `approved_version_id`, the approval
  `quote_responses` totals and selected optional lines. BR-04 extends its
  `/quotes/:quoteId` view.
- `quote-builder` (AUDITED): quote visibility (BR-07), frozen lines, the
  assessment photo endpoint and the photo viewer.
- `requests-pipeline` (AUDITED): branch scope (BR-02) and the `converted`
  status.
- `schedule-assessment` (AUDITED): completed assessments and photos.
- `catalog-categories` (AUDITED), `products-services` (APPROVED),
  `technician-skills-availability` (AUDITED): categories, catalog products and
  skills.
- `authenticated-app-shell` (AUDITED): sidebar, forbidden and not-found
  states. The shared discard-changes dialog already exists.
- Design 6 (future): scheduling, assignment, availability, recurrence
  materialization and notification sending.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Priority mapping Urgent=1, High=2, Normal=3 (default), Low=4. 5 is unused (confirmed 2026-10-06). |
| AS-02 | Duration options run from 30 min to 12 h in 30-minute steps and are optional (confirmed 2026-10-06). |
| AS-03 | Arrival windows are the fixed BR-14 list, in the branch time zone (confirmed 2026-10-06). |
| AS-04 | 0–10 active organization skills, with no minimum proficiency set (`minimum_proficiency` null) (confirmed 2026-10-06). |
| AS-05 | The work order display number is `<work_order_prefix>-<work_order_number>`, following the quote numbering precedent. |
| AS-06 | The **Quotes** breadcrumb item is plain text, because no quotes list exists yet. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Persistence package | As proposed · adjusted | No | As proposed (BR-25) — user 2026-10-06 |
| OD-02 | Draft creation | On first save, manual save · on open · autosave | No | On first save, manual, with `updatedAt` (BR-16) — user 2026-10-06 |
| OD-03 | Create effects | `ready_to_schedule` + request `converted` + audit in one transaction | No | Confirmed. Draft saves never convert or create visits (BR-16, BR-18) — user 2026-10-06 |
| OD-04 | Initial visits | One unscheduled · N per occurrence · none | No | Only visit #1, only on create. Occurrences belong to Design 6 (BR-18, BR-13) — user 2026-10-06 |
| OD-05 | Job type and recurrence | `one_time`/`recurring` with stored rule · trade type | No | `one_time`/`recurring` (BR-13) — user 2026-10-06 |
| OD-06 | Checklist templates | Org tables + apply + save · read only · from quote lines | No | Org tables, apply and save, no management screen (BR-11) — user 2026-10-06 |
| OD-07 | Task prefill | From approved service lines · empty | No | From service lines. Reorder by drag-and-drop and accessible buttons (BR-07, BR-10) — user 2026-10-06 |
| OD-08 | Materials and status | Prefill, "Planned" status · visual "Available" · hide | No | Prefill, "Planned". Manual materials are operational and never change the approved price (BR-12, BR-17) — user 2026-10-06 |
| OD-09 | Permissions | Owner/OM/Dispatcher manage, Viewer reads · Owner/Dispatcher only | No | Owner/OM/Dispatcher manage; Viewer reads (BR-01) — user 2026-10-06 |
| OD-10 | Guest quotes | Block with `409` · link customer here | No | Block (BR-05) — user 2026-10-06 |
| OD-11 | Jobs navigation | Rename to Jobs + list + detail · detail only | No | Jobs + minimal list + read-only detail (BR-21, BR-22) — user 2026-10-06 |
| OD-12 | Instructions | Editable instructions, read-only access note and photos · all read-only | No | Editable instructions (BR-23) — user 2026-10-06 |
| OD-13 | Create split button | Omit · disabled | No | Omitted until Design 6 — user 2026-10-06 |
| OD-14 | Notifications | Send now · store only | No | Store only. Nothing is sent (BR-15) — user 2026-10-06 |
| OD-15 | Approval recheck | — | No | Create rechecks `approved` and `approved_version_id` inside the lock (BR-18) — user 2026-10-06 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-03, AC-08 |
| FR-03 | AC-04, AC-05 |
| FR-04 | AC-15, AC-16 |
| FR-05 | AC-14, AC-17 |
| FR-06 | AC-02, AC-09, AC-18, AC-19 |
| FR-07 | AC-06, AC-07, AC-08 |
| FR-08 | AC-09, AC-11, AC-12, AC-13 |
| FR-09 | AC-10 |
| FR-10 | AC-14 |
| FR-11 | AC-05, AC-20 |
| FR-12 | AC-21, AC-22 |
| FR-13 | AC-02, AC-23, AC-24 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-06 | — → DRAFT | Created |
| 2026-10-06 | DRAFT → DRAFT | Revised after validation: template audit fields and item limits, Jobs sidebar visibility, list/detail time zones, BR-25 row, test-data note for the version mismatch |
| 2026-10-06 | DRAFT → APPROVED | Approved by user via /spec approve |
