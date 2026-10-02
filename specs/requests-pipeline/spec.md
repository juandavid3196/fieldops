# Requests pipeline

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `requests-pipeline` |
| Type     | Full-stack          |
| Status   | APPROVED            |
| Created  | 2026-10-02          |
| Updated  | 2026-10-02          |
| Approved | 2026-10-02          |

## Context and objective

Requests created by `public-service-request` (status `new`, source
`public_form`) have no staff-facing screen yet. This feature adds the Requests
page (Design 1) inside the authenticated shell. Staff can see open requests in
a four-column pipeline, filter and search them, and open a detail panel. From
the panel they can review a request, assign it, change its priority and branch,
write internal notes, ask the customer for information and log the answer,
schedule an assessment and mark the request ready for quote. Staff can also
create internal requests for existing customers. Every operation is scoped to
the session organization and the caller's branches, is audited, and updates
the board without a page reload. Together with `public-service-request`, this
closes Phase 3.

## Actors and permissions

Role access follows `PermissionCatalog` module `requests_quotes` and the
Customers precedent (OD-01, OD-02).

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Read everything and run every action in BR-03, in all branches | — |
| Dispatcher (`dispatcher`) | Read and run every action in BR-03 for requests in branch scope (BR-02) | Requests outside scope (`404`) |
| Operations Manager (`operations_manager`) | Read the board, metrics, detail, notes, activity and attachments in all branches | Any mutation (`403`); being an assignee |
| Accounting (`accounting`) | Read as Operations Manager, limited to branch scope (BR-02) | Any mutation (`403`); requests outside scope (`404`) |
| Viewer (`viewer`) | Read as Operations Manager, in all branches | Any mutation (`403`) |
| Technician (`technician`) and unknown roles | — | Every endpoint of this feature (`403`); the page shows the forbidden state |

## Scope

- Requests page at `/requests` inside the existing app shell. It contains the
  header, four metric cards, filters, search, **New request**, the four-column
  board and the detail panel.
- Pipeline, metrics, detail and option read endpoints.
- Request actions:
  - Start review.
  - Assign.
  - Change priority.
  - Set branch.
  - Add internal notes.
  - Request information, with an email to the customer.
  - Log customer response.
  - Schedule, reschedule and cancel an assessment.
  - Mark ready for quote (from `new`/`needs_review`) and Complete assessment
    (from `assessment_scheduled`).
  - Move back to review.
  - Cancel the request.
- Authenticated attachment download and preview, plus staff attachment upload.
- Internal request creation for an existing customer, contact and property.
- Activity timeline built from status history, request audit rows and customer
  messages.
- Navigation changes:
  - The sidebar **Requests** item points to `/requests`.
  - Request links in the customer detail "Recent work" point to
    `/requests?request=<id>`.

## Non-goals

- The Quotes module, including creating a quote record. In `ready_for_quote`,
  "Create quote" navigates to `/coming-soon/quotes`.
- Estimated value per column. It is hidden until Quotes exists (OD-15).
- Global Schedule calendar, technician availability, skills or absence
  validation for assessments. Only overlap is validated (OD-09).
- Completing an assessment with diagnosis or scope.
- Work orders, invoices, payments, the customer portal, real-time chat, SMS,
  automatic technician assignment and price calculation.
- Creating customers, contacts or properties from **New request** (OD-13).
- Drag-and-drop between columns. Status changes go through panel actions.
- Editing or deleting internal notes, messages or attachments.
- Shell changes: the global top-bar search, notifications bell, organization
  selector and sidebar labels stay as implemented. Only the Requests link
  target changes.
- The customer detail "Requests" tab and its **Create → Request** item. They
  keep their Coming soon behavior.
- Showing `quoted`, `converted` and `cancelled` requests on the board. They
  remain reachable through `?request=<id>` in read-only mode.

## User flow

1. A staff member opens **Requests** in the sidebar. The system shows the
   metrics and the four columns with the in-scope open requests (BR-02, BR-04).
2. The staff member filters or searches. Columns and counts update (BR-06).
3. The staff member opens a card. The URL gets `?request=<id>` and the detail
   panel opens with the request data, attachments, internal notes, activity
   and the actions allowed for the status and role (BR-03, BR-08).
4. A manager (Owner or Dispatcher) runs an action, for example Request
   information, Schedule assessment or Mark ready for quote. The system validates it,
   persists it with history and audit, and returns the updated detail. The
   board and metrics refresh without a page reload, and the card moves to its
   new column.
5. The staff member closes the panel with its close button, Escape or browser
   Back. The URL drops `request`, and focus returns to the card.
6. A manager presses **New request**, picks an existing customer, contact and
   property, and enters the service details. The new request appears in
   **New requests**.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The frontend must provide the `/requests` page in the app shell. The sidebar **Requests** item must link to it, and customer "Recent work" request links must open `/requests?request=<id>` (BR-17). |
| FR-02 | `GET /service-requests/pipeline` must return the four columns (`new`, `needs_review`, `assessment_scheduled`, `ready_for_quote`). Each column has its total count and its first 50 cards, sorted per BR-05, with the card fields of BR-04, limited to the caller's scope and the filters of BR-06. A `status` + `offset` query must return the next page of one column. |
| FR-03 | `GET /service-requests/metrics` must return the four metrics with their comparison deltas per BR-07, independent of filters. |
| FR-04 | `GET /service-requests/{id}` must return the detail of BR-08 for an in-scope request of the session organization in any status. |
| FR-05 | The frontend must render the board, cards, metrics, filters and detail panel as in Design 1, with the responsive, URL, focus and state behavior of the UI section. |
| FR-06 | The system must apply the status transitions of the States table. Each transition writes one `request_status_history` row and one audit row. A transition not allowed from the current status returns `409`. |
| FR-07 | Managers must be able to assign or unassign the request to an eligible member (BR-09), change its priority (BR-10) and set its branch (BR-11). |
| FR-08 | Managers must be able to add internal notes (BR-12). All read roles must see the internal notes. |
| FR-09 | Managers must be able to request information from the customer: the message is stored, the email is sent after commit, and a `new` request moves to `needs_review` (BR-13). |
| FR-10 | Managers must be able to log a customer response received outside FieldOps, which ends "Awaiting response" (BR-13, BR-14). |
| FR-11 | Managers must be able to schedule, reschedule and cancel the request's assessment (BR-15). |
| FR-12 | Managers must be able to mark a `new`/`needs_review` request ready for quote, and complete the started assessment of an `assessment_scheduled` request, which moves it to `ready_for_quote`. In `ready_for_quote`, "Create quote" must navigate to `/coming-soon/quotes` (BR-03, BR-15). |
| FR-13 | Managers must be able to cancel a request with a reason after a confirm dialog. Any scheduled assessment is cancelled with it. |
| FR-14 | Every read role must be able to download or preview attachments through an authenticated endpoint. Managers must be able to upload attachments within the per-request limit (BR-16). |
| FR-15 | Managers must be able to create an internal request for an existing in-scope customer, contact and property (BR-18). |
| FR-16 | The detail activity must list creation, status changes, assignments, priority and branch changes, information requests, logged responses, assessment events and attachment uploads in chronological order (BR-19). |
| FR-17 | Every mutation must write audit rows per BR-20, with no personal data or message content. |
| FR-18 | Every endpoint must resolve the organization from the session and apply role and branch scope (BR-01, BR-02). Client identifiers must be verified server-side. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Role policy: read endpoints (pipeline, metrics, detail, options, attachment download) allow `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`. Every mutation and the customer options endpoint allow `owner`, `dispatcher`. `technician` and any other role → `403` everywhere. Frontend visibility is UX only. | Backend |
| BR-02 | Branch scope (as the Customers precedent): `owner`, `operations_manager`, `viewer` and any member with `organization_users.is_all_branches` see all branches. `dispatcher` and `accounting` otherwise see only branches in `organization_user_branches`. A request is visible when `branch_id` is null or in scope. A request outside scope or of another organization → `404`. Lists, counts and metrics include only visible requests. | Backend |
| BR-03 | Panel actions per status, shown only to Owner and Dispatcher. Footer: **new**/**needs_review** → **Schedule assessment** (primary), **Request information**, **Mark ready for quote**. **assessment_scheduled** → **Complete assessment** (primary, BR-15), **Request information**, **Reschedule**. **ready_for_quote** → **Create quote** (primary, navigates to `/coming-soon/quotes`, no data change), **Request information**. Each label always triggers the same single behavior. More-actions menu (any open status unless noted): **Assign**, **Change priority**, **Set branch**, **Log customer response**, **Start review** (new only), **Move back to review** (ready_for_quote only), **Cancel assessment** (assessment_scheduled only), **Cancel request**. The internal note input and **Add file** are shown to managers on open requests. `quoted`, `converted`, `cancelled` and every read role see no actions and no inputs. | Both |
| BR-04 | Card fields. **Title** = catalog service name. If there is no service, the category name. If neither exists, the first line of `description`, truncated to 60 characters with "…" (OD-08). **Customer** = customer display name, else `guest_name`. **Service** = category name with its icon placeholder. **Date** (org timezone): `assessment_scheduled` → active assessment start ("MMM d, yyyy h:mm a"); otherwise `preferred_start` date ("MMM d, yyyy") when `dateMode = date`, "ASAP" for `asap`, "Flexible" for `flexible`, created date when no preferences. **Priority** = urgency chip shown for Urgent and Emergency only. **Avatar** = initials of the active assessment technician when one exists, otherwise of the assignee, otherwise none. Tooltip/accessible name: "Assessment technician: <name>" or "Assignee: <name>". **Age** = calendar days since `created_at` in org timezone: "Today" for 0, otherwise "<n>d". **Awaiting response** tag when BR-14 holds. The selected card is highlighted. | Both |
| BR-05 | Card order within a column: urgency (emergency, urgent, standard), then `created_at` ascending, then `request_number` ascending. Page size 50. The column header shows the label and the total count. | Backend |
| BR-06 | Filters (AND-combined; apply to columns and counts, not metrics). **Assignee**: All, Unassigned, or one eligible member (BR-09). **Service**: All or one category. **Priority**: All, Standard, Urgent, Emergency. **Source**: All, Public form (`public_form`), Internal (`internal`). **Date** on `created_at` in org timezone: All, Today, Last 7 days, Last 30 days. **Search**: trimmed, ≤ 100 characters, case-insensitive contains on title sources (service, category name), customer display name, `guest_name`, `guest_email`. A term like `REQ-1048` or `1048` also matches the request number exactly. Search is debounced 300 ms client-side. Unknown or foreign filter ids → `400` on that parameter. Filters live in page state, not the URL. | Both |
| BR-07 | Metrics (visible scope; org timezone). **New today** = requests created today, versus those created on the same weekday last week. **Awaiting response** = open requests with BR-14 true now, versus the same evaluation 7 days ago. **Assessments today** = assessments with status `scheduled` and start today, versus the same weekday last week. **Conversion rate** = requests created in the last 30 days that have reached `ready_for_quote`, `quoted` or `converted` (per status history) ÷ non-cancelled requests created in that period, rounded to whole %, versus the previous 30 days. Count deltas are whole % of change. When the previous value is 0, the delta is null and the UI shows "—". The conversion delta is in percentage points, shown as "+7%". Up is the positive tone, except Awaiting response, where up is the negative tone. "Open" = `new`, `needs_review`, `assessment_scheduled`, `ready_for_quote`. | Both |
| BR-08 | Detail contents. **Header**: `<request_prefix>-<n>`, title, status chip, priority chip. **Customer**: name, contact phone and email (from the linked contact, falling back to the `guest_*` snapshot), service address from `service_address`. **Request details**: category/service, branch (or "Unassigned branch"), assignee, source, preferred visit (date + window, ASAP or Flexible, plus scheduling notes), active-damage indicator, description. **Assessment**: when one is scheduled, its date, time and technician. **Attachments** with count. **Internal notes**, newest first, with author and time. **Activity** (BR-19). | Both |
| BR-09 | Eligible assignee: an active member (`organization_users.status = active`) with role `owner` or `dispatcher`. For a request with a branch, the member also needs that branch in scope (BR-02). `null` unassigns. An ineligible or foreign user id → `400` `errors.assigneeUserId` "Choose a team member who can manage this request.". Assigning a `new` request also moves it to `needs_review` in the same transaction. Assigning the current assignee again is a no-op (no history, no audit). | Backend |
| BR-10 | Priority = `urgency` ∈ `standard`, `urgent`, `emergency`, labelled Standard, Urgent, Emergency. Setting the same value is a no-op. | Both |
| BR-11 | Branch: an active branch of the organization in the caller's scope. Otherwise → `400` `errors.branchId` "Choose a branch you have access to.". Clearing is not allowed once set. If the current assignee does not have the new branch in scope → `400` `errors.branchId` "The assignee doesn't have access to this branch.". | Backend |
| BR-12 | Internal note: body 1–2000 characters after trim. Stored in `request_messages` with `visibility = internal`, `author_user_id` = caller, `author_contact_id` null. Never sent to the customer. | Both |
| BR-13 | Customer messages are stored in `request_messages` with `visibility = customer`. **Request information**: body 1–2000 characters, `author_user_id` = caller, `author_contact_id` null. The recipient is the linked active contact's email, else `guest_email`. With no email → `400` `errors.body` "This customer has no email address.". After commit, one email is sent through `IEmailSender`: subject `<organization name> needs more information about <REQ-n>`; text and HTML bodies contain the contact first name (or "there"), the organization name, the request number, the message body and, when set, "Questions? Call us at <organization phone>.". On send failure, only the request id and failure category are logged, and the response is still success. A `new` request moves to `needs_review` in the same transaction. **Log customer response**: body 1–2000 characters, `author_user_id` = caller, `author_contact_id` = the request contact. A request with no contact → `400` `errors.body` "This request has no customer contact.". | Both |
| BR-14 | "Awaiting response" holds for an open request when its latest `visibility = customer` message has `author_contact_id` null. Evaluated at time T, it uses only messages and status as of T. | Backend |
| BR-15 | Assessment. **Schedule** (from `new`/`needs_review`): body `{ start, end, technicianId?, branchId? }` (local org-time ISO date-times). `start` must be in the future. `end` must be after `start` on the same local day, and the duration ≤ 8 hours. `branchId` is required when the request has no branch, follows BR-11 and is set on the request. `technicianId` is optional. When given, it must be an active `technician_profiles` row of the organization whose `branch_id` equals the request branch. Overlap: the technician must have no other `scheduled` assessment with `[start, end)` intersecting → `400` `errors.technicianId` "This technician already has an assessment at that time.". The system creates one `assessments` row (`status = scheduled`, `created_by_user_id` = caller) and the request moves to `assessment_scheduled`. At most one `scheduled` assessment exists per request. **Reschedule** (in `assessment_scheduled`): same validation, excluding the assessment itself. It updates the same row's times and technician, and the status is unchanged. **Cancel assessment**: confirm dialog. The assessment becomes `cancelled`, and the request returns to `needs_review`. **Complete assessment** (explicit action, only in `assessment_scheduled`): when the active assessment's `scheduled_start` is later than now → `409` with title "This assessment hasn't started yet.". Otherwise, in one transaction, the assessment becomes `completed` (`completed_at` = now, `updated_at` = now) and the request moves to `ready_for_quote`. Diagnosis and scope stay empty. **Mark ready for quote** (only from `new`/`needs_review`) never creates or completes an assessment. **Cancel request** with a scheduled assessment cancels that assessment in the same transaction. | Both |
| BR-16 | Attachments. Download: `GET` returns the stored content with its `mime_type`, `Content-Disposition` (`inline` for images, `attachment` for PDF, sanitized stored name), `X-Content-Type-Options: nosniff` and `Cache-Control: private, no-store`. Image thumbnails are fetched through this endpoint. No public or unauthenticated URL exists. Upload (managers, open requests): multipart, 1–5 files per call, and existing + new ≤ 5 per request (`400` `errors.attachments` "A request can have up to 5 files."). Size, type detection, extension match, naming and the 25 MB per-call total follow `public-service-request` BR-13/BR-14, with `uploaded_by_user_id` = caller. Body > 26 MB → `413`. | Both |
| BR-17 | Navigation: the sidebar **Requests** item targets `/requests`, and `requests` is removed from the Coming soon modules. In customer "Recent work", request ID links and **View** target `/requests?request=<id>`. Quote and job links are unchanged. | Frontend |
| BR-18 | Internal request (New request drawer). Fields: **Customer** (search over active in-scope customers), **Contact** (active contacts of the customer, default primary), **Property** (active properties of the customer, default primary), **Category**, **Service** or "I'm not sure", **Description**, **Priority**, **Active damage**, **Availability**. Category/service/description/urgency/availability rules reuse `public-service-request` BR-05–BR-07 and its validation code, without contact, property, consent, honeypot or attachment fields. The contact and property must belong to the customer. A foreign, inactive or out-of-scope id → `400` on that field. A property whose `branch_id` is set and outside the caller's scope → `400` `errors.propertyId` "Choose a property in a branch you have access to.". Stored: `status = new`, `source = internal`, next request number (public BR-11), `branch_id` = the selected property's `branch_id`, or null when the property has none (OD-19; the request is then unassigned like public requests and a Dispatcher sets it later per BR-11), `customer_id`, `contact_id`, `property_id`, `guest_name`/`guest_email`/`guest_phone` snapshot from the contact, `service_address` snapshot from the property (`propertyType` = `home` for `person` customers, `business` for `company`), `preferred_start`/`preferred_end` per public BR-12, `consent_at` null. No email is sent. History none → `new` with the caller. | Both |
| BR-19 | Activity, oldest first. Entries come from: the `request_status_history` rows; this request's audit rows for assignment, priority, branch, assessment and attachment events; and the `visibility = customer` messages. Labels: "Request submitted" + source label ("Online form" / "Created by <name>"), "Status changed to <Status>", "Assigned to <name>" / "Unassigned", "Priority changed to <Priority>", "Branch set to <branch>", "Information requested" + message body, "Customer response logged" + body, "Assessment scheduled/rescheduled/cancelled" + date/time, "File added", each with actor name (or "Customer"/"System") and time ("Today at h:mm a", else "MMM d, yyyy h:mm a"). Internal notes appear only in Internal notes. | Both |
| BR-20 | Audit rows: `entity_type` `service_request`, `entity_id` request id, `actor_user_id` caller, `branch_id` request branch after the change. Actions: `service_request.created` (internal; after `{ requestNumber, status, source, urgency, hasActiveDamage }`), `service_request.status_changed` (before/after `{ status }`), `service_request.assigned` (before/after `{ assigneeUserId }`), `service_request.priority_changed` (before/after `{ urgency }`), `service_request.branch_changed` (before/after `{ branchId }`), `service_request.internal_note_added` (metadata `{ messageId }`), `service_request.information_requested` (metadata `{ messageId }`), `service_request.customer_response_logged` (metadata `{ messageId }`), `service_request.assessment_scheduled`/`assessment_rescheduled`/`assessment_cancelled` (metadata `{ assessmentId, technicianId, start, end }`), `service_request.ready_for_quote` (before/after `{ status }`), `service_request.assessment_completed` (before/after `{ status }`, metadata `{ assessmentId }`), `service_request.attachments_added` (metadata `{ count }`). Each operation writes exactly one audit row named for that operation. An implied status change (assignment, information request, assessment) is recorded in status history and in that same row's before/after `status`. Audit rows and logs never contain names, email, phone, address, description, note or message bodies, or file names. | Backend |

## States and transitions

Every row writes one `request_status_history` row (`changed_by_user_id` =
caller; `reason` set only for cancellation) and updates `updated_at`. Actor for
every row: Owner or Dispatcher in scope. Any other transition, including from
`quoted`, `converted` or `cancelled`, returns `409`.

| From | To | Trigger | Guard |
| ---- | -- | ------- | ----- |
| — | `new` | Public submission (existing) or New request | BR-18 |
| `new` | `needs_review` | Start review; Assign (BR-09); Request information (BR-13) | — |
| `new`, `needs_review` | `assessment_scheduled` | Schedule assessment | BR-15 |
| `assessment_scheduled` | `needs_review` | Cancel assessment | Active assessment exists |
| `new`, `needs_review` | `ready_for_quote` | Mark ready for quote | No assessment is created or completed |
| `assessment_scheduled` | `ready_for_quote` | Complete assessment | Active assessment `scheduled_start` ≤ now, else `409`; assessment set `completed` in the same transaction (BR-15) |
| `ready_for_quote` | `needs_review` | Move back to review | — |
| `new`, `needs_review`, `assessment_scheduled`, `ready_for_quote` | `cancelled` | Cancel request | Reason 1–500 chars; confirm dialog; sets `cancelled_at`; active assessment cancelled |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `service_requests`:
    - Read: all columns.
    - Update: `status`, `assigned_dispatcher_user_id`, `urgency`, `branch_id`,
      `cancelled_at`, `updated_at`.
    - Insert internal requests (BR-18).
  - `organizations`:
    - Read: `name`, `phone`, `timezone`, `request_prefix`.
    - Lock and increment `next_request_number` for internal creation.
  - `request_messages`: insert and read (`internal` and `customer`
    visibility).
  - `request_status_history`: insert and read.
  - `request_attachments`: read and insert. `content` is read only by the
    download endpoint.
  - `assessments`: insert; update `scheduled_start`, `scheduled_end`,
    `technician_id`, `status`, `completed_at`, `updated_at`.
  - `audit_logs`: insert and read (activity).
  - Read only: `customers`, `customer_contacts`, `properties`,
    `service_categories`, `catalog_items`, `branches`, `organization_users`,
    `organization_user_branches`, `roles`, `users` (names only),
    `technician_profiles`.
- Constraints relied on:
  - Composite `(organization_id, id)` FKs of `service_requests`,
    `request_messages`, `request_attachments` and `assessments`.
  - `CHECK(scheduled_start<scheduled_end)`.
  - The attachment mime and size checks.
  - `ix_requests_pipeline`.
- `assessments.technician_id`, `request_messages.author_contact_id` and
  `service_requests.assigned_dispatcher_user_id` have single-column FKs.
  Organization membership and scope of these ids are validated in the
  application layer (schema invariant 1).
- `source = 'internal'` uses the existing unconstrained `varchar(30)`.
- Schema amendments required: **None**. No migration.

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  request carries an organization id. Any such field is ignored.
- Client identifiers, all verified against the session organization, the
  caller's branch scope and the rules in BR-06, BR-09, BR-11, BR-15, BR-16 and
  BR-18:
  - Request id, attachment id.
  - `assigneeUserId`, `branchId`, `technicianId`.
  - `customerId`, `contactId`, `propertyId`.
  - `categoryId`, `serviceId`.
  - Filter ids.
- Request or attachment of another organization, or outside branch scope:
  `404`, identical to a nonexistent id, on every read and mutation.
- Ids in a body that are foreign or out of scope: `400` on that field,
  identical to a nonexistent id. Nothing persisted.
- Permissions: BR-01. Responses use `Cache-Control: no-store`.
- Concurrency: mutations on one request are serialized. A conflicting
  concurrent transition leaves exactly one success, and the other receives
  `409`. Assessment overlap checks and internal request numbering run inside
  the same transaction as their writes.

## API contracts

Contract status: Final. No path prefix (existing routing). Errors use
ProblemDetails with field errors where noted. Every mutation returns
`200 RequestDetail` (the shape of `GET /service-requests/{id}`). Attachment
upload returns the same shape, and creation returns `201 RequestDetail`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/service-requests/pipeline` | Query: `assigneeUserId` (uuid or `unassigned`), `categoryId`, `urgency`, `source`, `created` (`today`/`7d`/`30d`), `search`; optional `status` + `offset` for one column page | `200 { columns: [{ status, total, items: [Card] }], timezone }`; Card = `{ id, number, title, customerName, categoryName, dateKind ("assessment"/"preferred"/"asap"/"flexible"/"created"), date, urgency, avatar: { role ("technician"/"assignee"), name, initials } \| null, createdAt, awaitingResponse }` | `400` (invalid filter) · `403` | Read |
| GET | `/service-requests/metrics` | — | `200 { newToday: { value, deltaPercent }, awaitingResponse: { value, deltaPercent }, assessmentsToday: { value, deltaPercent }, conversionRate: { value, deltaPoints } }` (deltas nullable) | `403` | Read |
| GET | `/service-requests/options` | — | `200 { requestPrefix, timezone, categories: [{ id, name, services: [{ id, name }] }], assignees: [{ userId, name, initials, branchIds \| "all" }], branches: [{ id, name }], technicians: [{ id, name, initials, branchId }] }` (active, in-scope; managers use assignees/branches/technicians) | `403` | Read |
| GET | `/service-requests/customer-options` | Query `search` (≤ 100) | `200 { items: [{ id, displayName, branchId, contacts: [{ id, name, email, phone, isPrimary }], properties: [{ id, name, addressLine1, city, isPrimary }] }] }`, max 20, active and in scope | `400` · `403` | Manage |
| GET | `/service-requests/{id}` | — | `200 RequestDetail = { id, number, title, status, urgency, source, createdAt, branch: { id, name } \| null, assignee \| null, customer: { id, name } \| null, contact: { name, phone, email }, serviceAddress, category, service, availability: { dateMode, preferredDate, timeWindow, schedulingNotes } \| null, hasActiveDamage, description, awaitingResponse, assessment: { id, start, end, technician } \| null, attachments: [{ id, fileName, mimeType, sizeBytes, createdAt }], notes: [{ id, body, authorName, createdAt }], activity: [{ kind, label, detail, actorName, occurredAt }] }` | `403` · `404` | Read |
| POST | `/service-requests` | `{ customerId, contactId, propertyId, categoryId, serviceId, notSure, description, urgency, hasActiveDamage, availability: { dateMode, preferredDate, timeWindow, schedulingNotes } }` | `201 RequestDetail` | `400` (fields per BR-18) · `403` · `500` (rolled back) | Manage |
| POST | `/service-requests/{id}/start-review` | — | `200` | `403` · `404` · `409` | Manage |
| PUT | `/service-requests/{id}/assignee` | `{ assigneeUserId \| null }` | `200` | `400` · `403` · `404` · `409` (closed) | Manage |
| PUT | `/service-requests/{id}/priority` | `{ urgency }` | `200` | `400` · `403` · `404` · `409` | Manage |
| PUT | `/service-requests/{id}/branch` | `{ branchId }` | `200` | `400` · `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/notes` | `{ body }` | `200` | `400` · `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/information-requests` | `{ body }` | `200` | `400` · `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/customer-responses` | `{ body }` | `200` | `400` · `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/assessment` | `{ start, end, technicianId?, branchId? }` | `200` | `400` · `403` · `404` · `409` | Manage |
| PUT | `/service-requests/{id}/assessment` | `{ start, end, technicianId? }` | `200` | `400` · `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/assessment/cancel` | — | `200` | `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/assessment/complete` | — | `200` | `403` · `404` · `409` (not `assessment_scheduled`, or assessment not started) | Manage |
| POST | `/service-requests/{id}/ready-for-quote` | — | `200` | `403` · `404` · `409` (not `new`/`needs_review`) | Manage |
| POST | `/service-requests/{id}/move-to-review` | — | `200` | `403` · `404` · `409` | Manage |
| POST | `/service-requests/{id}/cancel` | `{ reason }` (1–500) | `200` | `400` · `403` · `404` · `409` | Manage |
| GET | `/service-requests/{id}/attachments/{attachmentId}` | — | `200` binary (BR-16 headers) | `403` · `404` | Read |
| POST | `/service-requests/{id}/attachments` | `multipart/form-data`, 1–5 parts `attachments` | `200` | `400` (`attachments`, `attachments[i]`) · `403` · `404` · `409` · `413` · `415` | Manage |

- Mutations on `quoted`, `converted` or `cancelled` requests return `409`.
  Validation failures return `400`, and invalid status transitions `409`.
- `409` body: ProblemDetails title "This request changed. Refresh to see the
  latest."
- Assessment `start`/`end` are local org-time `YYYY-MM-DDTHH:mm`, and
  responses carry ISO offsets. `preferredDate` is `YYYY-MM-DD`.
- Internal contract: existing `IEmailSender`, unchanged.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Requests page | Skeletons for metric cards and column cards; filters disabled | No open requests and no filters: "No requests yet" / "New requests from your public form and your team will appear here." plus **New request** for managers. A column with no cards: "No requests" | Load failure: existing error pattern "We couldn't load requests." with **Retry** | Technician/unknown: existing forbidden state "You don't have access to requests." with no further data requests. Read roles: no **New request** | Board, metrics and counts render |
| Filtered board | Columns keep their content with a loading indicator | No-results: "No requests match your filters." with **Clear filters** | As above | — | Columns and counts reflect the filters |
| Detail panel | Panel skeleton | — | `404`: "This request isn't available." with **Close**. Other errors: message with **Retry** | Read roles: no footer, menu, note input or **Add file** | BR-08 contents and BR-03 actions |
| Action dialogs (Assign, Priority, Branch, Request information, Log response, Schedule/Reschedule, Cancel request) | Submit shows progress and is disabled while in flight; one request only | — | `400`: inline field errors. `409`: dialog closes, toast with the 409 title, detail and board reload. Other: toast "We couldn't save this change. Please try again." with data kept | — | Dialog closes; success toast; detail, board and metrics refresh without page reload |
| Destructive confirms (Cancel request, Cancel assessment) | As dialogs | — | As dialogs | — | Existing confirm-dialog: "Cancel <REQ-n>?" with required Reason / "Cancel assessment?" ("The request returns to Needs review."); buttons **Cancel request**/**Cancel assessment** and **Keep** |
| New request drawer | Options load skeleton; customer search shows progress | Customer search with no matches: "No customers found." | Inline field errors; save failure toast; data kept | Managers only | Drawer closes; the new request opens in the panel and appears in **New requests** |
| Attachments | Thumbnail placeholders while images load through the authenticated endpoint | "No files" | Upload: inline per-file rejection (type, size, count) before sending; server errors shown per file | **Add file** managers only, hidden at 5 files | Thumbnails (images) or file icon (PDF) with name; click downloads/previews |

- Mockup: `design/assets/1er-design.png` (Approved).
  - The main area must match it: header and subtitle, metric cards, filter
    bar with **New request**, column headers with counts, cards, the detail
    panel layout, the attachments grid with **Add file**, the internal note
    input, the activity timeline and the footer buttons.
  - Deviations by decision:
    - The shell (sidebar labels, top bar) stays as implemented.
    - The column "est. value" line is hidden (OD-15).
    - The column kebab menus are not rendered (AS-03).
    - Card avatars follow BR-04.
    - Footer labels follow BR-03. In `needs_review`, the mockup's third
      button reads **Mark ready for quote** instead of **Create quote**.
    - Priority labels follow BR-10.
    - Sample data is replaced by real data.
- URL: opening a card navigates to `/requests?request=<id>` (new history
  entry). Refresh reopens the panel. Back and Forward open and close it.
  Closing removes the parameter. Leaving `/requests` closes it.
- Responsive:
  - Wide desktop: board and docked panel side by side, as in the mockup.
    Without a selection, the board uses the full width.
  - Tablet and small desktop: the board scrolls horizontally, and the panel
    opens as an overlay drawer using the existing drawer pattern.
  - Mobile: the metric cards stack, and filters collapse into the existing
    filter pattern. Requests appear as a vertical list grouped under the four
    status headings with counts. The detail opens full screen with a back
    control.
- Accessibility:
  - Cards are keyboard-focusable controls with accessible names ("<title>,
    <customer>, <status>").
  - Columns are labelled regions with their count.
  - Opening the panel moves focus to its heading. Close, Escape (when no
    dialog is open) and Back return focus to the originating card when it is
    still rendered.
  - Dialogs trap focus. Avatars expose the BR-04 tooltip text as their
    accessible name.
  - Status and priority are never conveyed by color alone.
  - Toasts use the existing polite live region.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Request/attachment of another organization or out of scope | `404`; panel "This request isn't available." | None |
| Read role or technician calls a mutation | `403`; existing forbidden handling | None |
| Transition not allowed from the current status, or concurrent conflicting change | `409`; toast with the 409 title; detail and board reload | None |
| Ineligible assignee, foreign branch/technician/customer/contact/property/category/service | `400` on that field | None |
| Technician overlap | `400` `errors.technicianId` | None |
| Request information without email / log response without contact | `400` `errors.body` | None |
| More than 5 attachments, invalid file, body > 26 MB | `400` `attachments`/`attachments[i]`; `413` | None |
| Email send failure after an information request | `200`; failure logged without personal data | Message and transition kept |
| Failure during an internal creation transaction | `500`; save failure toast | None (number not consumed) |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | A public request just submitted (`new`, `public_form`, no branch) | An Owner opens `/requests` | It appears in **New requests** with title, customer, category, date, priority, age per BR-04. Columns show the four statuses with totals; `quoted`/`converted`/`cancelled` requests are absent |
| AC-02 | Requests in several statuses, urgencies and branches, more than 50 in one column | The pipeline is loaded and a column's next page is requested | Cards are ordered per BR-05; each column returns ≤ 50 with its true total; the `status` + `offset` page returns the next cards |
| AC-03 | A Dispatcher with branch A, an Accounting user with branch B, requests in A, B and with no branch | Each loads the pipeline, metrics and an out-of-scope detail | Each sees only in-scope and branchless requests, counts and metrics included; the out-of-scope detail and its attachment return `404` |
| AC-04 | A request of organization X | A member of organization Y calls detail, attachment download and a mutation with its id | All return `404` identical to a nonexistent id; nothing changes |
| AC-05 | An Operations Manager, an Accounting user, a Viewer and a Technician | They call a mutation and open the page | The three read roles get `403` on mutations and see the board and panel with no actions, menu, note input or **Add file**; the Technician gets `403` on reads and sees the forbidden state with no further data requests |
| AC-06 | Requests matching different assignee, category, priority, source, date and search values | Filters and a search (including `REQ-<n>` and `<n>`) are applied, then cleared | Columns and counts show only matches (AND); metrics are unchanged; no matches shows the no-results state with **Clear filters**; an unknown filter id returns `400` |
| AC-07 | Seeded requests, assessments, status history and messages across the current and comparison periods | Metrics are requested | Values and deltas match BR-07, including a null delta when the previous value is 0 and percentage-point conversion delta |
| AC-08 | A request with contact, property, availability, active damage, attachments, notes, messages and history | A card is opened | URL gains `?request=<id>`; the panel shows BR-08 contents and BR-19 activity in order; refresh reopens it; Back closes it; Escape and the close button close it and focus returns to the card |
| AC-09 | A `new` request | A Dispatcher runs Start review, or assigns an eligible member | Status becomes `needs_review`, one history row and one audit row are written, and the card moves column without page reload |
| AC-10 | Requests in each status | An invalid transition is attempted (e.g. ready-for-quote on `cancelled`, move-to-review on `new`), or two conflicting transitions run concurrently | `409` for the invalid one; for the concurrent pair exactly one succeeds; no extra history/audit rows |
| AC-11 | A request with a branch | Assignment to an Operations Manager, to a Dispatcher without that branch, or to a user of another organization; then to an eligible Dispatcher; then the same again | The first three return `400` `errors.assigneeUserId`; the eligible one is stored with `service_request.assigned`; the repeat is a no-op |
| AC-12 | An open request | Priority is changed, and branch is set to an in-scope branch, an out-of-scope branch, and a branch the assignee lacks | Priority and in-scope branch persist with their audit rows and activity entries; the others return `400` `errors.branchId` |
| AC-13 | An open request | A manager adds an internal note; a read role views the panel | The note shows newest first with author and time for every read role; one `internal_note_added` audit row with only `messageId`; no email is sent; empty or > 2000 chars returns `400` |
| AC-14 | A `new` request whose contact has an email | Request information is submitted | A `customer` message is stored, the request moves to `needs_review`, one email per BR-13 is sent after commit, the card shows "Awaiting response" and the metric counts it; if the email throws, the response is still `200` and the log has no recipient or body |
| AC-15 | A request awaiting response; a request without email; a request without contact | Log customer response, and Request information / Log response on the others | The logged response clears "Awaiting response" and appears in the activity; the others return `400` `errors.body` |
| AC-16 | A `needs_review` request without branch and a technician with a scheduled assessment | Schedule assessment with: no branch; a past start; end before start or > 8 h; a technician of another branch; an overlapping slot; then valid data | Invalid inputs return `400` on the matching field; the valid one sets the branch, creates one `scheduled` assessment, moves to `assessment_scheduled`, and the card shows the date/time and technician avatar with tooltip |
| AC-17 | An `assessment_scheduled` request | Reschedule to a valid slot, then Cancel assessment through the confirm dialog | Reschedule updates the same assessment row with status unchanged; cancel sets it `cancelled` and returns the request to `needs_review`; each writes its audit row and activity entry |
| AC-18 | A `needs_review` request; an `assessment_scheduled` request with a future start; one whose start has passed | Mark ready for quote on the first; Complete assessment on the other two; ready-for-quote on an `assessment_scheduled` request; then Create quote and Move back to review in `ready_for_quote` | The first moves to `ready_for_quote` with `service_request.ready_for_quote` and no assessment change. The future one returns `409` with nothing changed. The started one has its assessment set `completed` and moves to `ready_for_quote` with `service_request.assessment_completed`, in one transaction. Ready-for-quote from `assessment_scheduled` returns `409`. Create quote navigates to `/coming-soon/quotes` with no data change. Move back returns the request to `needs_review` |
| AC-19 | An open request with a scheduled assessment | Cancel request is confirmed with a reason, or dismissed with Keep | Confirm: status `cancelled`, `cancelled_at` set, assessment `cancelled`, history reason stored, card leaves the board; Keep: nothing changes. The browser's native confirm is never used |
| AC-20 | A request with 3 attachments | A manager uploads 2 valid files, then 1 more; a renamed text file; a read role downloads an image and a PDF | 2 files store with `uploaded_by_user_id`; the 6th returns `400` `errors.attachments`; the invalid file returns `400` `attachments[i]`; downloads return content with BR-16 headers only to authenticated in-scope readers |
| AC-21 | A manager opens **New request** | They save with a customer out of scope, a contact or property of another customer, an invalid category/service pair, a past or out-of-range date, then valid data | Invalid inputs return `400` on the field; a property in an out-of-scope branch returns `400` `errors.propertyId`; valid data creates `new`, `internal`, next number, the property's branch (null when the property has none), snapshots per BR-18, history and `service_request.created`; no email; it appears in **New requests** and opens in the panel |
| AC-22 | Any successful mutation from the panel | The response arrives | The panel shows the returned detail, the board and metrics refresh without a full page reload, the success toast is announced, and the selected card stays highlighted in its new column |
| AC-23 | Every mutation of AC-09…AC-21 | Audit rows and logs are inspected | One row per operation with the BR-20 action and fields; none contains names, email, phone, address, description, note or message bodies, or file names |
| AC-24 | Desktop, tablet and mobile widths | The page is used | Desktop: board and docked panel; tablet: horizontal board scroll and overlay panel; mobile: list grouped by status and full-screen detail with back control; keyboard operation and focus per the accessibility rules |
| AC-25 | The app shell and a customer detail with a request in Recent work | **Requests** in the sidebar and the request link/**View** are used | Both open `/requests` (the latter with `?request=<id>` and the panel open); `/coming-soon/requests` is no longer linked |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Transition table (allowed/denied pairs, implied transitions), table-driven | AC-09, AC-10, AC-18 |
| Backend unit | Card title/date/avatar derivation and metric delta math (null previous, percentage points) | AC-01, AC-07 |
| Backend integration | Pipeline query: public request in **New requests**, ordering, paging, filters/search (parameterized) | AC-01, AC-02, AC-06 |
| Backend integration | Branch scope by role and cross-organization `404` on read, download and mutation; representative `403` for a read role and Technician | AC-03, AC-04, AC-05 |
| Backend integration | Main transitions with history/audit, invalid `409`, concurrent conflict | AC-09, AC-10, AC-18, AC-19 |
| Backend integration | Assignee/branch eligibility and no-op | AC-11, AC-12 |
| Backend integration | Internal note and information request: message, status change, email after commit, failure tolerance, audit without content | AC-13, AC-14, AC-15, AC-23 |
| Backend integration | Assessment schedule/reschedule/cancel with branch requirement and overlap | AC-16, AC-17 |
| Backend integration | Attachment upload limit, content validation and download headers | AC-20 |
| Backend integration | Internal request creation with cross-customer/out-of-scope ids | AC-21 |
| Authorization/tenant isolation | Covered by the scope/cross-organization group above: one representative per path (read, download, mutation) | AC-03, AC-04, AC-05 |
| Frontend component/service | Grouping into four columns with counts, empty and forbidden states | AC-01, AC-05 |
| Frontend component/service | Filters and debounced search update the query; no-results and Clear filters | AC-06 |
| Frontend component/service | Card opens the panel via `?request=<id>`; Escape/close removes it and restores focus | AC-08 |
| Frontend component/service | Primary transition action (Mark ready for quote / Complete assessment) and per-status footer labels; refreshes detail and board; `409` handling; role-based action visibility | AC-18, AC-22, AC-05 |
| Frontend component/service | Cancel request uses confirm-dialog with reason | AC-19 |
| Browser (final audit) | User visual QA only (no Playwright): desktop board + panel fidelity to Design 1, tablet and mobile layouts, one action flow end to end, attachments preview | AC-08, AC-22, AC-24, AC-25 |

Backend integration uses 10 groups, which exceeds the default of 8 by 2. The
extra groups cover distinct high-risk boundaries this feature creates: branch
scoping of null-branch requests across roles, staff uploads and the download
of binary content, concurrent transitions, and an outbound customer email.
Frontend uses 5 groups. Navigation link changes (AC-25) and responsive layout
(AC-24) are evidenced by user visual QA, not by separate tests.

## Dependencies

- `public-service-request`: request intake, attachments storage, BR-05/BR-07
  validation, numbering. Status AUDITED.
- `authenticated-app-shell`: shell, sidebar, Coming soon routes. Implemented.
- `customer-management` and `customer-property-detail`: customers, contacts,
  properties, branch scope precedent, Recent work links. Implemented.
- `team-technician-management`: `technician_profiles`. Implemented.
- `company-settings-and-branches`: branches, organization timezone and phone.
  Implemented.
- `password-recovery`: `IEmailSender`. Implemented.
- Shared `confirm-dialog` and `drawer-shell` components. Implemented.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Removed 2026-10-02: replaced by BR-18 (branch from the selected property) and OD-19. |
| AS-02 | Filters are page state only. Only the selected request is reflected in the URL (confirmed by user). |
| AS-03 | The mockup's column kebab menus have no defined behavior and are not rendered (confirmed by user). |
| AS-04 | Removed 2026-10-02: replaced by the explicit Complete assessment action (BR-15, OD-20). |
| AS-05 | Removed 2026-10-02: replaced by distinct per-status labels (BR-03, OD-21). |
| AS-06 | Information request emails are sent regardless of the contact's SMS/email preference, as in `public-service-request` AS-05. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Operations Manager access | a) PermissionCatalog read-only · b) full management | No | a) — user 2026-10-02 |
| OD-02 | Viewer access | a) read everything (Customers precedent) · b) none | No | a) — user 2026-10-02 |
| OD-03 | Requests without `branch_id` | a) visible to authorized roles; Dispatcher sets the branch · b) all-branches users only | No | a); Accounting stays read-only — user 2026-10-02 |
| OD-04 | Status transitions | Proposed table | No | Accepted as in States and transitions — user 2026-10-02 |
| OD-05 | "Awaiting response" representation | a) derived from `request_messages` · b) new column | No | a) — user 2026-10-02 |
| OD-06 | Assignee and card avatar | a) `assigned_dispatcher_user_id` · b) any member | No | a), eligible = effective manage permission (Owner, Dispatcher); avatar = assessment technician else assignee, with role tooltip — user 2026-10-02 |
| OD-07 | Priority | a) `urgency` · b) new `priority` column | No | a) — user 2026-10-02 |
| OD-08 | Card title | a) description first line · b) service name · c) new column | No | b), fallback category, then truncated description first line — user 2026-10-02 |
| OD-09 | Schedule assessment | a) create assessment, overlap-only validation · b) also availability | No | a); reschedule keeps the row; cancelling the active assessment returns to `needs_review` — user 2026-10-02 |
| OD-10 | Create quote | a) mark `ready_for_quote`; link to Coming soon · b) minimal quote | No | a) — user 2026-10-02 |
| OD-11 | Request information | a) store + email after commit · b) store only | No | a); `new` → `needs_review` — user 2026-10-02 |
| OD-12 | Customer replies | a) Log customer response · b) out of scope | No | a) — user 2026-10-02 |
| OD-13 | New request scope | a) existing customer/contact/property · b) also new customer · c) hidden | No | a) — user 2026-10-02 |
| OD-14 | Metric definitions | Proposed BR-07 | No | Accepted — user 2026-10-02 |
| OD-15 | Estimated value | a) hidden · b) catalog price · c) "—" | No | a) — user 2026-10-02 |
| OD-16 | Staff attachments | a) upload within 5 total, authenticated · b) view only | No | a) — user 2026-10-02 |
| OD-17 | Audit action prefix | a) `service_request.*` · b) `request.*` | No | a) — user 2026-10-02 |
| OD-18 | Existing links | a) sidebar + Recent work · b) sidebar only | No | a) — user 2026-10-02 |
| OD-19 | The user decided that an internal request inherits the selected property's `branch_id`. However, `properties.branch_id` is nullable, and properties created by the public form have no branch (`public-service-request` BR-10). Which branch does an internal request get when the property has none? | a) `branch_id` null: the request is unassigned like public requests (OD-03), and the Dispatcher sets it later · b) fall back to the customer's branch · c) reject with `400` `errors.propertyId` until the property has a branch | No | a) — user 2026-10-02 |
| OD-20 | Ready for quote from `assessment_scheduled` | Implicit completion · explicit action | No | Explicit **Complete assessment** only. `409` when the assessment has not started. Direct transitions from `new`/`needs_review` never create or complete assessments — user 2026-10-02 |
| OD-21 | Footer label reuse | One "Create quote" label · distinct labels | No | Distinct: Mark ready for quote / Complete assessment / Create quote → Coming soon — user 2026-10-02 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-25 |
| FR-02 | AC-01, AC-02, AC-06 |
| FR-03 | AC-07 |
| FR-04 | AC-08 |
| FR-05 | AC-01, AC-08, AC-22, AC-24 |
| FR-06 | AC-09, AC-10, AC-18 |
| FR-07 | AC-09, AC-11, AC-12 |
| FR-08 | AC-13 |
| FR-09 | AC-14 |
| FR-10 | AC-15 |
| FR-11 | AC-16, AC-17 |
| FR-12 | AC-18 |
| FR-13 | AC-19 |
| FR-14 | AC-20 |
| FR-15 | AC-21 |
| FR-16 | AC-08, AC-17 |
| FR-17 | AC-23 |
| FR-18 | AC-03, AC-04, AC-05 |

## Change log

| Date | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-02 | — → DRAFT | Created; decisions OD-01…OD-18 resolved by user in one round |
| 2026-10-02 | DRAFT → DRAFT | Assumption review: internal request branch from property (BR-18, OD-19 open); explicit Complete assessment with future-start 409 (BR-15, OD-20); distinct footer labels (BR-03, OD-21); AS-02/AS-03 confirmed; AS-01/AS-04/AS-05 removed |
| 2026-10-02 | DRAFT → DRAFT | Resolved OD-19: internal request without property branch stays branchless (BR-18, AC-21) |
| 2026-10-02 | DRAFT → APPROVED | Approved by user via /spec approve |
