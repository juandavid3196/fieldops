# Customer related records

| Field    | Value                       |
| -------- | --------------------------- |
| Feature  | `customer-related-records`  |
| Type     | Full-stack                  |
| Status   | APPROVED                    |
| Created  | 2026-10-22                  |
| Updated  | 2026-10-22                  |
| Approved | 2026-10-22                  |

## Context and objective

The customer detail page (`/customers/:customerId`, `customer-property-detail`)
still shows Coming soon panels in its Requests, Quotes, Jobs and Invoices tabs,
and its Activity tab only lists customer, property and note audit rows. Staff
have to leave Customers and search each module manually. This feature turns the
detail page into a complete operational record: four read-only lists of the
customer's requests, quotes, jobs and invoices, an in-place **New request**
flow, and a chronological activity timeline built from the existing audit
history. Every list, counter and event only includes records the caller can
still open in their own module, and Overview applies the same rule.

## Actors and permissions

Module read policies are the existing ones: Customers read (`CustomerView`:
owner, operations_manager, dispatcher, accounting, viewer); Requests and Quotes
read (`ServiceRequestView`: same five roles); Jobs read (`WorkOrderRead`:
owner, operations_manager, dispatcher, viewer); Invoices read
(`BillingReviewRead`: same five roles as Customers); request creation
(`ServiceRequestManage`: owner, dispatcher). Branch scope as
`customer-management` BR-02.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner | All tabs, Activity of every type, **New request**; all branches | — |
| Dispatcher | All tabs, Activity of every type, **New request**; only records of assigned branches (all when `is_all_branches`) | Records outside branch scope; customers outside scope (`404`) |
| Operations Manager | All tabs and Activity of every type, read-only; all branches | **New request** (`403`) |
| Viewer | All tabs and Activity of every type, read-only; all branches | **New request** (`403`) |
| Accounting | Requests, Quotes, Invoices tabs and Activity except Jobs/Visits types, read-only; assigned branches | Jobs tab and job/visit events (`403` on the jobs endpoint); job-derived Overview data; **New request** (`403`) |
| Technician | — | Every endpoint in this spec (`403`); forbidden state (unchanged) |

## Scope

- Requests, Quotes, Jobs and Invoices tabs of `/customers/:customerId` with
  search, status filter, pagination, row navigation to the existing module
  destination, and per-tab loading/empty/error/retry states.
- Invoices summary (Outstanding, Overdue, Paid) over the customer's visible
  invoices.
- **New request** in the Requests tab and **Create → Request** open the
  existing New request drawer inside the customer page with the customer
  preselected and locked.
- Activity timeline replacing the current paginated Activity tab: event
  catalog from `audit_logs`, type and period filters, day groups, cursor-based
  "Load older activity".
- New read-only endpoints under `/customers/{id}`; extended
  `GET /customers/{id}/activity` and `GET /service-requests/customer-options`.
- Record-level visibility (module permission + record branch scope) for the
  tabs, Activity and the Overview operational data, superseding
  `customer-property-detail` BR-20's derived-data clause.
- Responsive compact cards, light/dark mode and English/Spanish copy.

## Non-goals

- Any table, column, index or migration. Portal message text is not stored and
  is never shown (OD-02).
- Multi-select, bulk actions, exports, sorting controls or inline mutations in
  the lists.
- Creating or editing quotes, jobs, invoices or payments from the customer page;
  **Create → Quote / Job / Invoice** and the Overview Coming soon links
  (Recent work, Billing, Upcoming appointments) keep their current destinations.
- Changes to module pages, their detail authorization or their status labels.
- Changes to the Customers list (`/customers`), its metrics, balance column,
  lifecycle or display-status derivation, or to the header display status.
- Changing what the audit history records; adding audit rows.
- Real-time updates of open tabs.
- Mockup sample data and the mockups' "Converted" quote status.

## User flow

1. A permitted user opens `/customers/:customerId` and selects a tab the role
   can read; the tab loads its first page.
2. The user searches, filters by status, changes page size or pages; the list,
   footer and paginator update. Switching to another tab and back keeps each
   tab's own filters and page.
3. The user selects a row (or its number link / chevron) and lands on the
   existing request, quote, job or invoice destination, which authorizes again.
4. An Owner or Dispatcher selects **New request** (or **Create → Request**);
   the drawer opens with the customer locked and its primary contact and
   property selected; on save a toast shows and the Requests tab refreshes.
5. The user opens **Activity**, filters by type and period, follows an event
   link, and selects **Load older activity** to append older events.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The Requests, Quotes, Jobs and Invoices tabs must replace their Coming soon panels with the lists of BR-04 to BR-08, and each tab must be shown only to roles that can read its module (BR-03). |
| FR-02 | `GET /customers/{id}/requests` must return one page of the customer's visible requests per BR-02, BR-04 and BR-05. |
| FR-03 | `GET /customers/{id}/quotes` must return one page of the visible quotes of the customer's requests per BR-02, BR-04 and BR-06. |
| FR-04 | `GET /customers/{id}/jobs` must return one page of the customer's visible jobs per BR-02, BR-04 and BR-07. |
| FR-05 | `GET /customers/{id}/invoices` must return one page of the customer's visible invoices and the Outstanding / Overdue / Paid summary per BR-02, BR-04 and BR-08. |
| FR-06 | **New request** and **Create → Request** must open the existing New request drawer in the customer page with the customer preselected and locked per BR-09, supported by `GET /service-requests/customer-options?customerId=`. |
| FR-07 | `GET /customers/{id}/activity` must return the customer's visible events from existing history with type, period and cursor filters per BR-10 to BR-13, and the Activity tab must render them as a day-grouped timeline with **Load older activity**. |
| FR-08 | Overview operational data must apply record-level visibility per BR-14. |
| FR-09 | Every endpoint must resolve the organization from the session and authorize the customer, the module and each record per BR-01 and BR-02. |
| FR-10 | Each tab must render loading, empty, filtered-empty, error with retry, responsive card, light/dark and localized states per BR-03, BR-15, BR-16 and the UI table. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization per endpoint, in this order: unauthenticated → `401`; role lacks Customers read **or** the module read policy (requests/quotes → `ServiceRequestView`, jobs → `WorkOrderRead`, invoices → `BillingReviewRead`; activity → `CustomerView` only) → `403`; customer unknown, malformed id, of another organization or outside the caller's branch scope (`customer-management` BR-02) → `404`. Invalid query values → `400` `errors.<param>`. Archived customers are readable. | Backend |
| BR-02 | Record visibility. A record is included only when it belongs to the caller's organization, to this customer, and passes the same visibility its module applies: **Request**: `service_requests.customer_id` = customer, `branch_id` null or in scope. **Quote**: its request has `customer_id` = customer and is visible (`quotes-management` BR-02). **Job**: `work_orders.customer_id` = customer, status ≠ `draft`, `branch_id` in scope. **Invoice**: `invoices.customer_id` = customer, status ≠ `void`, `branch_id` in scope. **Visit**: its job is visible. **Payment**: at least one of its `payment_allocations` targets a visible invoice. Counts, totals, summaries and events are computed only from visible records. Destinations keep their own authorization; this feature never widens it. | Backend |
| BR-03 | Tab UX (amends `customer-property-detail` BR-01/BR-18): Requests and Quotes tabs need Requests read, Jobs needs Jobs read, Invoices needs Invoices read; a tab the role cannot read is not rendered and `?tab=<that tab>` shows Overview (as an unknown value). A tab requests its data the first time it is activated, not on page load. Each tab keeps its own search, status, page size and page while the user switches tabs on the same page; leaving the page resets them. Tab filters are not stored in the URL. | Frontend |
| BR-04 | Common list rules. Query: `search?` (trimmed, ≤100 chars, case-insensitive contains, debounced 300 ms client-side; longer → `400 errors.search`), `status?` (one code of the tab's status set; unknown → `400 errors.status`), `page?` (≥1, default 1), `pageSize?` (10, 25 or 50, default 10; other → `400 errors.pageSize`). Changing search, status or page size returns to page 1; a page beyond the last returns empty `items` with correct counts. Response carries `totalCount` (filtered) and `customerTotal` (visible records of the customer, ignoring search/status). Header "<Module> (<customerTotal>)"; footer "Showing x–y of n <noun>" with n = `totalCount`; paginator with page-size selector 10/25/50. Status filter "All statuses" plus the module's official display statuses. Dates are UTC instants or ISO dates plus `timezone`; money uses `currency` with two decimals. Each row is one keyboard-focusable link (number link plus trailing chevron) to the BR-05–BR-08 destination; no checkboxes, row menus or bulk actions. | Both |
| BR-05 | Requests: order `created_at` desc, id. Columns: **Request** "REQ-<request_number>"; **Service** = title rule of `requests-pipeline` BR-04 (catalog service name, else category name, else description first line ≤60 chars with "…"); **Property** = property name, else "—"; **Created** "MMM d, yyyy" in org timezone; **Status** = requests module label/tone for `request_status`. Search matches request number (digits, with or without "REQ-"), service/category name, property name and description. Destination `/requests?request=<id>`. Header action **New request** per BR-09. | Both |
| BR-06 | Quotes: order `created_at` desc, id. Columns: **Quote** "<quote_prefix>-<quote_number>"; **Related request** "REQ-<n> · <service>" (BR-05 service rule); **Total** = shown-version total (`quotes-management` BR-04), "—" when none; **Expires** = shown-version `valid_until` "MMM d, yyyy", "—" when none; **Status** = `quotes-management` BR-03 display status code and label (no "Converted"). Search matches quote number, request number and request service/category name. Destination `/quotes/<id>`. | Both |
| BR-07 | Jobs: order `created_at` desc, id. Columns: **Job** "<work_order_prefix>-<work_order_number>" with `title` underneath; **Property** = property name; **Next visit** = for jobs with status `completed` or `approved_for_billing`, "Completed <MMM d>" using the latest `completed`/`approved` visit `actual_completed_at` (fallback `scheduled_end`); otherwise the earliest visit with `scheduled_start` ≥ now and status `scheduled`, `assigned` or `on_the_way` as "MMM d · h:mm a"; otherwise "—"; **Assigned to** = initials avatar and name of the active primary assignment of the current visit (`jobs-management` BR-04), else "Unassigned"; **Status** = `jobs-management` BR-05 operational status code and label. Search matches job number, title, property name and technician name. Destination `/jobs/<id>`. | Both |
| BR-08 | Invoices: order `issue_date` desc nulls first, then `created_at` desc, id. Columns: **Invoice** "<invoice_prefix>-<invoice_number>"; **Related job** job number; **Issued** / **Due** "MMM d, yyyy" or "—"; **Balance** = `balance_due`; **Status** = invoices module label/tone (Draft, Sent, Partially paid, Paid, Overdue). Search matches invoice number and job number. Destination `/invoices/<id>`. Summary over **all** visible invoices of the customer, independent of search, status and page: **Outstanding** = Σ `balance_due` with status `sent`, `partially_paid` or `overdue`; **Overdue** = Σ `balance_due` with status `overdue`; **Paid** = Σ `amount_paid` of visible invoices. | Both |
| BR-09 | New request: shown in the Requests tab header and as **Create → Request** only to roles with `ServiceRequestManage` and only while the customer is active (archived customers: hidden, as customer options exclude them). Both open the existing New request drawer in the customer page (no navigation) with the customer preselected and not editable, and its primary active contact and primary property selected by the drawer's existing defaults; all other fields, validation and `POST /service-requests` behavior are unchanged. `GET /service-requests/customer-options?customerId=<id>` returns the existing response shape containing only that customer when it is active, of the organization and in branch scope, else an empty list; `customerId` and `search` are mutually exclusive (both → `400 errors.customerId`); malformed id → `400 errors.customerId`. If the option comes back empty the drawer does not open and toast "This customer can't receive new requests right now." shows. On success: drawer closes, existing success toast, Requests tab (and Activity, when loaded) refresh; dirty close keeps the existing discard confirmation. | Both |
| BR-10 | Activity sources: `audit_logs` rows of the caller's organization only; no new table or audit write. An event belongs to the customer when its entity is the customer, one of its properties, notes or contacts, or a request/quote/job/visit/invoice/payment visible under BR-02. Event types and whitelisted actions: see the Activity catalog table. A type is only available, and its events only returned, when the caller has the module read policy it requires (Customer and Messages: Customers read; Requests and Quotes: Requests read; Jobs and Visits: Jobs read; Invoices and Payments: Invoices read). Non-whitelisted actions never appear. | Backend |
| BR-11 | Activity item: type icon and tone, localized title (from the action code), summary per catalog, time "h:mm a" in org timezone, origin (BR-13), and a link to the destination when the catalog defines one and the record is visible (rows are links with a chevron; events without a destination have no chevron). Summaries use the record's **current** values (number, title, total, names); the payment amount is `payments.amount`. Values of `before_data`, `after_data`, `metadata` (except the new-status code of `work_order.status_changed`, used only to pick its title), IP addresses, message text, note text, emails, phones and addresses are never returned. | Both |
| BR-12 | Activity filters and loading: `type?` (`all` default, `customer`, `requests`, `messages`, `quotes`, `jobs`, `visits`, `invoices`, `payments`; a type the caller cannot read or unknown → `400 errors.type`); `period?` (`last_7_days`, `last_30_days`, `last_90_days` default, `last_12_months`, `all`; rolling window ending now; unknown → `400 errors.period`); `before?` opaque cursor from the previous response (invalid → `400 errors.before`). Order `occurred_at` desc, id desc; 20 events per block; response `nextCursor` null when no older event exists in the window. The tab groups events under "Today", "Yesterday" or "MMM d, yyyy" (org timezone); **Load older activity** appends the next block and is hidden when `nextCursor` is null; changing type or period reloads from the newest. The Type select lists only available types. | Both |
| BR-13 | Origin: actor without organization membership, or no actor on a public-link/portal action (`AuditSourceResolver`) → "Customer portal"; staff actor → the user's name; otherwise "System". | Backend / Frontend |
| BR-14 | Overview visibility (supersedes the derived-data clause of `customer-property-detail` BR-20; header identity, display status and lifecycle unchanged): Recent work rows, Upcoming appointments, property Last service / Next appointment, Customer summary (Total jobs, Lifetime value, Last service), Billing (Outstanding balance, Last invoice) and the header Outstanding balance are computed only from records visible under BR-02. For a role without a module's read policy, that module's rows are excluded and its values are omitted, not shown as zero: without Jobs read, job rows of Recent work, the Upcoming appointments card, Total jobs, Last service and property Last service / Next appointment are not rendered; without Requests read, request and quote rows; without Invoices read, Lifetime value, Billing and the header Outstanding balance. Lifetime value = Σ `amount_paid` of visible invoices. | Both |
| BR-15 | Copy and states: statuses always show a text label (never color alone) and reuse the module's existing labels/tones in both languages. Empty (no records): "No requests yet" / "No quotes yet" / "No jobs yet" / "No invoices yet" with "<Module> for this customer will appear here."; filtered empty: "No <module> match your filters." with **Clear filters**; error: "We couldn't load <module>." with **Retry**, affecting only that tab; Activity empty: "No activity in this period." All new copy exists in English and Spanish. | Frontend |
| BR-16 | Layout: ≥768 px as the mockups (tables inside a card; Invoices summary in three columns). <768 px: each row becomes a compact card showing every column value with its label (status chip at the top right, whole card is the link); filters and **New request** stack full-width; Invoices summary one column; Activity meta (time · origin) wraps below the summary; no page-level horizontal scroll; 16 px side gutter. Light and dark mode use existing tokens with contrast-compliant chips. | Frontend |

### Activity catalog (BR-10, BR-11)

| Type | Action | Title | Summary | Link |
| ---- | ------ | ----- | ------- | ---- |
| Customer | `customer.created`, `customer.updated`, `customer.archived`, `customer.reactivated`, `property.created`, `property.updated`, `property.primary_changed`, `property.archived`, `property.reactivated`, `customer_note.created` | Existing `customer-property-detail` BR-17 labels | Property name for property actions, else none | None |
| Requests | `service_request.created`, `.assessment_scheduled`, `.assessment_rescheduled`, `.assessment_cancelled`, `.assessment_completed`, `.information_requested`, `.customer_response_logged`, `.quoted` | Request created / Assessment scheduled / Assessment rescheduled / Assessment cancelled / Assessment completed / Information requested / Customer response logged / Request quoted | "REQ-<n> · <service>" | Request |
| Messages | `portal.message_sent` (contact of this customer) | Customer message | "<contact name> sent a message" ("A contact sent a message" when the contact no longer exists); never the text | None |
| Quotes | `quote.sent`, `.revised`, `.approved`, `.rejected`, `.clarification_requested`, `.expired`, `.cancelled` | Quote sent / Quote revised / Quote approved / Quote rejected / Clarification requested / Quote expired / Quote cancelled | "<quote number> · <shown-version total>" | Quote |
| Jobs | `work_order.created`, `work_order.status_changed` | Job created / "Job <new status label>" (`jobs-management` work order status labels), "Job status updated" when the new status is unavailable | "<job number> · <title>" | Job |
| Visits | `visit.dispatched`, `visit.completed`, `visit.cancelled`, `visit.reschedule_requested` | Job scheduled / Visit completed / Visit cancelled / Reschedule requested | Job scheduled: "<job number> · <MMM d> at <h:mm a>" (current `scheduled_start`; "<job number>" when null); others "<job number> · Visit <visit_number>" | Job |
| Invoices | `invoice.sent`, `invoice.email_resent`, `invoice.bank_transfer_reported` | Invoice sent / Invoice email resent / Bank transfer reported | "<invoice number> · <total>" | Invoice |
| Payments | `payment.recorded`, `payment.refunded` | Payment received / Payment refunded | "<amount> received for <visible invoice numbers, comma-separated>" / "Refund on <visible invoice numbers>" | First visible allocated invoice |

## States and transitions

None. This feature is read-only except the existing request creation, whose
transitions are unchanged (`requests-pipeline`).

## Data and persistence impact

- Tables and columns read (from `docs/database/fieldops-schema.sql`):
  `customers`, `customer_contacts`, `properties`, `customer_notes`,
  `service_requests`, `service_categories`, `catalog_items`, `quotes`,
  `quote_versions`, `work_orders`, `visits`, `visit_assignments`,
  `technician_profiles`, `invoices`, `payments`, `payment_allocations`,
  `audit_logs`, `users`, `organizations` (`timezone`, `currency`, prefixes),
  `organization_users`, `organization_user_branches`, `branches`.
- Writes: none beyond the existing `POST /service-requests`.
- Schema amendments required: None (OD-02, OD-10).
- Persistence review: not required unless implementation changes mappings; any
  index proposal must return to this spec as a decision.

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session
  (active `organization_users` membership).
- Client-provided organization identifiers: never accepted. `customerId` (path
  or query) is verified against the organization and branch scope; record ids
  are never accepted as filters.
- Customer of another organization or out of scope: `404` on every endpoint of
  this spec; `customer-options?customerId=` returns an empty list.
- Records of another organization, another customer, an out-of-scope branch or
  an unreadable module never appear in lists, counts, summaries, events or
  Overview values.
- Permissions per action: BR-01, BR-09, BR-10, BR-14.

## API contracts

Contract status: Final. All endpoints require an authenticated session and
return `401` when unauthenticated; responses are `Cache-Control: no-store`.
Validation errors use the existing `400` problem shape with `errors.<param>`.
Instants are UTC ISO-8601; `timezone` = `organizations.timezone`; `currency` =
`organizations.currency`. Status values are codes; the frontend owns labels.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/customers/{id}/requests` | Query `search?`, `status?` (`request_status`), `page?`, `pageSize?` | `200 { items: [{ id, number, service, propertyName?, createdAt, status }], totalCount, customerTotal, page, pageSize, timezone }` | `400`, `403`, `404` | Customers read + Requests read |
| GET | `/customers/{id}/quotes` | Query `search?`, `status?` (`quotes-management` display codes), `page?`, `pageSize?` | `200 { items: [{ id, number, request: { id, number, service }, total?, validUntil?, status }], totalCount, customerTotal, page, pageSize, currency, timezone }` | `400`, `403`, `404` | Customers read + Requests read |
| GET | `/customers/{id}/jobs` | Query `search?`, `status?` (`jobs-management` operational codes), `page?`, `pageSize?` | `200 { items: [{ id, number, title, propertyName, nextVisitAt?, completedAt?, technician: { name, initials }?, status }], totalCount, customerTotal, page, pageSize, timezone }` | `400`, `403`, `404` | Customers read + Jobs read |
| GET | `/customers/{id}/invoices` | Query `search?`, `status?` (`draft`\|`sent`\|`partially_paid`\|`paid`\|`overdue`), `page?`, `pageSize?` | `200 { items: [{ id, number, job: { id, number }, issueDate?, dueDate?, balanceDue, status }], summary: { outstanding, overdue, paid }, totalCount, customerTotal, page, pageSize, currency, timezone }` | `400`, `403`, `404` | Customers read + Invoices read |
| GET | `/customers/{id}/activity` | Query `type?`, `period?`, `before?` (replaces `page`) | `200 { items: [{ id, type, action, occurredAt, origin: { kind: "customer_portal"\|"staff"\|"system", actorName? }, subject: { number?, title?, service?, amount?, invoiceNumbers?, contactName?, propertyName?, scheduledAt?, visitNumber?, newStatus? }, link: { kind: "request"\|"quote"\|"job"\|"invoice", id }? }], availableTypes: [...], nextCursor?, currency, timezone }` | `400`, `403`, `404` | Customers read (+ per-type module read, BR-10) |
| GET | `/service-requests/customer-options` | Existing `search?`, new `customerId?` (exclusive with `search`) | Existing shape; with `customerId`, 0 or 1 item | `400 errors.customerId`, `403` | Existing (`ServiceRequestManage`) |
| GET | `/customers/{id}/detail`, `/recent-work`, `/upcoming-appointments`, `/properties` | Existing | Existing shapes; values per BR-14; `summary.totalJobs`, `summary.lifetimeValue`, `outstandingBalance` become nullable (null = omitted for the role); `upcoming-appointments` returns empty `items` without Jobs read | Existing | Existing |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Requests / Quotes / Jobs / Invoices tab | Header visible; row skeletons (card skeletons on mobile); Invoices summary skeleton | BR-15 empty and filtered-empty | BR-15 error with **Retry** (keeps filters) | Tab hidden without module read (BR-03); **New request** per BR-09 | Rows, header count, footer, paginator |
| New request drawer (from customer page) | Existing; customer option loading | — | Existing errors; BR-09 empty-option toast | Owner/Dispatcher, active customer | Existing toast; Requests tab refreshes |
| Activity tab | Timeline skeleton; **Load older activity** loading state | BR-15 | "We couldn't load activity." with **Retry**; a failed older block keeps loaded events and shows the error with **Retry** under them | Type options limited per BR-10 | Day-grouped events, links, load older |
| Overview | Existing | Existing | Existing | Values/regions omitted per BR-14 | Existing |

- Mockups (Approved; images are the visual authority):
  `design/assets/Solicitudes del cliente en FieldOps-1.png`,
  `design/assets/Pestaña de presupuestos en FieldOps-2.png`,
  `design/assets/Pestaña Jobs del cliente FieldOps-3.png`,
  `design/assets/Diseño de facturas en FieldOps-4.png`,
  `design/assets/Cronología de actividad del cliente-5.png`.
  Deviations decided by the user: no "Converted" quote status (module labels),
  page-size selector and paginator added, message events without text, origin
  "Customer portal" / staff name / "System".
- Accessibility: tabs stay a tablist; row links have accessible names
  ("<number>, <status>"); status chips carry text; search inputs and selects
  have labels; **Load older activity** keeps focus on the first appended event;
  the summary values have their captions as accessible names.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Customer unknown, malformed, foreign or out of scope | `404`; existing not-found state | None |
| Role lacks Customers or module read | `403`; tab not rendered | None |
| Invalid search, status, page size, type, period or cursor | `400 errors.<param>`; tab error state | None |
| Customer option unavailable for New request | Empty options; BR-09 toast; drawer not opened | None |
| Tab or Activity block load failure | BR-15 / UI table error with **Retry**; other tabs unaffected | None |
| Destination record no longer accessible | Destination's own not-found/forbidden handling | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner on a customer detail page | They open each of Requests, Quotes, Jobs and Invoices | Each shows its list (no Coming soon panel), loading only on first activation, with header count, search, status filter, table, footer and paginator per BR-04 |
| AC-02 | A customer with requests of every status, with and without property, service or category | The Requests tab loads, is searched by number/service/property/description and filtered by status | Rows, order, columns and labels follow BR-05; search and filter results follow BR-04/BR-05; row opens `/requests?request=<id>` |
| AC-03 | Quotes of the customer's requests in every display status, with and without sent versions and expiry | The Quotes tab loads, is searched and filtered | Columns follow BR-06 (related request, shown-version total, expiry, module status, no "Converted"); quotes of other customers' requests never appear; row opens `/quotes/<id>` |
| AC-04 | Draft, scheduled, in-progress, completed and cancelled jobs with and without upcoming visits and assignments | The Jobs tab loads, is searched by number/title/property/technician and filtered | Draft jobs excluded; Next visit shows the upcoming visit, "Completed <MMM d>" or "—"; Assigned to shows avatar+name or "Unassigned"; operational status per BR-07; row opens `/jobs/<id>` |
| AC-05 | Draft, sent, partially paid, paid, overdue and void invoices | The Invoices tab loads and the user searches and filters by status | Void excluded; columns follow BR-08; Outstanding, Overdue and Paid equal the BR-08 sums over all visible invoices and do not change with search, status or page; row opens `/invoices/<id>` |
| AC-06 | 27 matching records in a tab | The user pages, changes page size to 25 and 50, searches and changes status | 10 rows by default; options 10/25/50; any filter or size change returns to page 1; header shows `customerTotal`, footer "Showing x–y of n" shows the filtered count; out-of-range values return `400` |
| AC-07 | Filters set on Requests and different filters on Invoices | The user switches between tabs, then reloads the page | Each tab keeps its own search, status, size and page while switching; after reload filters are reset and the active tab is kept via `?tab` |
| AC-08 | A Dispatcher and an Accounting user scoped to branch X, a customer in X with requests, quotes, jobs, invoices and payments in X and Y (plus requests with null branch) | They open every tab, Activity and Overview | Only X records (and null-branch requests) appear in rows, counts, summaries, events, Recent work, appointments, property history, summary and billing values; Y records never appear; Owner sees both |
| AC-09 | An Accounting user | They open the customer page and call `GET /customers/{id}/jobs` and Activity with `type=jobs`/`visits` | Jobs tab not rendered and `?tab=jobs` shows Overview; jobs endpoint `403`; `type=jobs` and `type=visits` → `400`; Activity `all` contains no job or visit events; Overview omits job rows, Upcoming appointments, Total jobs, Last service and property service history per BR-14 |
| AC-10 | Organizations A and B | A user of A calls every endpoint of this spec with B's customer id, and A's customer has no B data | `404` for B's customer; `customer-options?customerId=<B customer>` returns empty; no B record appears in any list, summary, event or Overview value |
| AC-11 | Each role, including Technician | They call the four list endpoints, Activity and `customer-options?customerId=` | Responses follow BR-01/BR-09 (Technician `403` everywhere; read roles `403` on customer options) |
| AC-12 | An Owner or Dispatcher on an active customer | They select **New request** (and separately **Create → Request**) and submit valid data | The existing drawer opens in place with the customer locked and primary contact/property selected; the request is created for that customer; toast shows; Requests tab refreshes; dirty close asks the existing discard confirmation |
| AC-13 | A read role, an archived customer, and a customer that becomes unavailable before the drawer opens | The Requests tab and Create menu render / **New request** is selected | **New request** and **Create → Request** hidden for read roles and archived customers; the unavailable case shows the BR-09 toast and no drawer; `customerId` with `search` or malformed → `400` |
| AC-14 | Audit rows for every catalog action plus non-whitelisted actions (`visit.paused`, `invoice.draft_saved`, `quote.draft_saved`, `customer.imported`) and rows of other customers | Activity loads with type `all` | Only catalog actions of this customer's visible records appear, newest first, with catalog titles, summaries, links and BR-13 origins; existing customer/property/note events are kept under Customer |
| AC-15 | A `portal.message_sent` row of this customer's contact and staff/system/portal actions | Activity loads | The message event shows "Customer message", "<contact name> sent a message", origin "Customer portal" and no link; the response contains no message text, note text, contact data, IP, `before_data`/`after_data` or metadata values |
| AC-16 | 45 events in the last 90 days and older events | The user loads Activity, selects **Load older activity** twice, then changes type and period | Blocks of 20, 20, 5 appended without duplicates in day groups (Today, Yesterday, dates in org timezone); button hidden when `nextCursor` is null; changing filters reloads from newest; each period and type filters per BR-12; invalid cursor → `400` |
| AC-17 | Slow, failing and empty responses for each tab and Activity | They load, fail and are retried | Skeletons, empty, filtered-empty with **Clear filters**, per-tab error with **Retry** (other tabs unaffected) and Activity older-block error keeping loaded events follow BR-15 and the UI table |
| AC-18 | Viewports ≥768 and <768 px, light and dark mode, English and Spanish | Each tab and Activity render | Layout follows BR-16 (compact cards on mobile, no page-level horizontal scroll); chips and summaries legible in both themes; all copy localized; accessibility constraints hold |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Activity catalog mapping (action → type, required module, link kind) and origin resolution, table-driven; Next visit / completed derivation | AC-04, AC-14, AC-15 |
| Backend integration | Each list endpoint against seeded records: visibility, order, columns, search, status, paging, `customerTotal`; invoice summary independence from filters; activity whitelist, redaction, type/period filters and cursor paging without duplicates; customer-options `customerId`; Overview values under branch scope and Accounting omission | AC-02–AC-06, AC-08, AC-09, AC-13–AC-16 |
| Authorization/tenant isolation | One cross-organization `404` for the customer-scoped read path and customer options; Dispatcher/Accounting branch-scope record filtering across modules (one grouped test); role matrix including Accounting jobs `403` and Technician `403` | AC-08, AC-09, AC-10, AC-11 |
| Frontend component/service | Tab visibility per role and lazy loading; independent per-tab filter state; list query building and states; New request drawer prefill/lock and visibility; Activity grouping, load older and filter reset | AC-01, AC-06, AC-07, AC-09, AC-12, AC-13, AC-16, AC-17 |
| Browser (final audit) | User-supplied visual QA only: mockup fidelity of the five tabs, mobile cards, dark mode, Spanish copy | AC-01, AC-18 |

## Dependencies

- `customer-property-detail` (AUDITED): detail page, tabs, Overview endpoints,
  Activity endpoint. This spec amends BR-01 (tab visibility), BR-17 (Activity
  contract and catalog), BR-18 (transactional tabs and **Create → Request**)
  and BR-20 (derived data visibility).
- `customer-management` (AUDITED): role policy and branch scope.
- `requests-pipeline`, `quotes-management`, `jobs-management`,
  `invoices-payments-management` (implemented): module visibility, status
  labels, destinations and the New request drawer.
- `audit-trail` (implemented): audit catalog and source resolution.
- `customer-portal-dashboard` (implemented): `portal.message_sent` audit rows
  without message text.
- `app-i18n-en-es`, `app-dark-mode` (implemented): localization and theming.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Number formats, money and date formatting reuse the helpers the modules already use. |
| AS-02 | Activity events of records that became invisible (branch change) disappear from the timeline; history is not snapshotted. |
| AS-03 | The cursor is opaque to the client and encodes `occurred_at` and `id`. |
| AS-04 | API route prefix and problem-details shape follow existing endpoints. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Requested design paths do not exist | Use Spanish-named files | No | Use the five Spanish-named files (user, 2026-10-22) |
| OD-02 | Portal message text is not stored | Event without text; schema change | No | Event without text; no schema amendment (user, 2026-10-22) |
| OD-03 | Endpoints | Customer-scoped reads + cursor Activity; `customerId` on module lists | No | Customer-scoped reads under `/customers/{id}`; Activity with `before` cursor (user, 2026-10-22) |
| OD-04 | Tabs without module permission | Hide; permission state | No | Hide tab and exclude its events (user, 2026-10-22) |
| OD-05 | Record-level branch scope and Overview | Tabs only; tabs and Overview | No | Tabs, Activity and Overview operational data; header identity/summary kept; supersedes BR-20 derived-data clause (user, 2026-10-22) |
| OD-06 | New request flow | Drawer in customer page; navigate to `/requests` | No | Drawer in place, customer locked, primary data prefilled; **Create → Request** reuses it (user, 2026-10-22) |
| OD-07 | Activity catalog, periods, block size, summary, origin | As catalog table and BR-12/BR-13 | No | Approved (user, 2026-10-22) |
| OD-08 | Invoices summary basis | All visible; follows filters | No | All visible invoices, independent of search/status (user, 2026-10-22) |
| OD-09 | Counts, search fields, status sets | BR-04–BR-08 | No | Confirmed; Quotes without "Converted" (user, 2026-10-22) |
| OD-10 | Schema impact | None; amendment | No | None required (user, 2026-10-22) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-09 |
| FR-02 | AC-02, AC-06 |
| FR-03 | AC-03 |
| FR-04 | AC-04 |
| FR-05 | AC-05 |
| FR-06 | AC-12, AC-13 |
| FR-07 | AC-14, AC-15, AC-16 |
| FR-08 | AC-08, AC-09 |
| FR-09 | AC-08, AC-10, AC-11 |
| FR-10 | AC-07, AC-17, AC-18 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-22 | — → DRAFT     | Created with the user's decisions OD-01 to OD-10 |
| 2026-10-22 | DRAFT → APPROVED | Approved by user via /spec approve |
