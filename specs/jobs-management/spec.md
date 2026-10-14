# Jobs management

| Field    | Value              |
| -------- | ------------------ |
| Feature  | `jobs-management`  |
| Type     | Full-stack         |
| Status   | AUDITED            |
| Created  | 2026-10-14         |
| Updated  | 2026-10-14         |
| Approved | 2026-10-14         |

## Context and objective

`/jobs` is a minimal list introduced by `create-work-order` (BR-21): newest
first, 25 per page, with no filters and only the `draft`/`ready_to_schedule`
labels. This feature replaces it with the operational workspace of Design 30.
Operations staff can follow every work order from creation to completion
through server-computed metrics, tabs, search, filters, sorting, real
pagination and state-aware navigation actions. The page reuses the data and
rules of Create Work Order, Dispatch Calendar, Mobile Job Execution and
Completed Jobs Review. It adds no mutation and no schema change.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Operations Manager (`operations_manager`) | Read the jobs workspace for every active branch; use every navigation action valid for the job's state | Mutate jobs from this page |
| Dispatcher (`dispatcher`) | The same as Owner, limited to branch scope (BR-02) | Anything outside branch scope (`404` for a branch filter) |
| Viewer (`viewer`) | Read the workspace within scope; **View job**, **Open in schedule**, **Review completion** | **Schedule visit** (Manage only) |
| Technician (`technician`), Accounting (`accounting`), unknown roles | — | Every endpoint of this feature (`403`); the existing forbidden state on `/jobs` |

Policies are unchanged from `create-work-order` BR-01: Read = `owner`,
`operations_manager`, `dispatcher`, `viewer`; Manage = `owner`,
`operations_manager`, `dispatcher`.

## Scope

- Replace the `/jobs` page with Design 30. The page has a breadcrumb, a header
  with **Open schedule** and **Ready to schedule**, four metric cards, the
  tabs All jobs / Active / Unscheduled / Completed with counts, search, branch,
  status and priority filters, sort, **Clear filters**, a jobs table with a
  per-row action menu, and server-side pagination.
- Extend `GET /work-orders` with filters, sorting, tab counts and the new row
  shape. Add `GET /work-orders/summary` and `GET /work-orders/options`.
- Compute an operational job status on the server.
- A narrow cross-cutting change on `/schedule`: a `visit` query parameter
  present on the initial load opens that visit's drawer (BR-17).

## Non-goals

- Manual job creation. Jobs still come from approved quotes (`create-work-order`).
- Drafts on `/jobs`. They stay reachable from the quote through
  **Continue draft** (`create-work-order` BR-04).
- Scheduling, rescheduling, assigning or unassigning from this page. Those
  stay in `/schedule`.
- Mobile job execution, billing review actions, invoice generation or
  payments.
- Changes to the job detail page (`/jobs/:id`), the operations dashboard or
  the completed jobs review behavior.
- Cancelling jobs or setting `needs_correction`. No feature sets them today;
  they are displayed only when present.
- Schema changes, exports and bulk actions.

## User flow

1. A Read user opens **Jobs** in the sidebar.
2. The system resolves the branch selection, loads the options, the summary
   and the first page of the **All jobs** tab, and reflects the state in the
   URL.
3. The user switches tabs, searches, filters, sorts or pages. The table, tab
   counts and pagination update from the server. Metrics update only when the
   branch changes.
4. The user selects **Ready to schedule**. The Unscheduled tab opens with the
   status filter cleared.
5. The user opens a job number to see `/jobs/:id`, or uses the row menu to go
   to the schedule or the completion review.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every endpoint must resolve the organization and membership from the session, apply the Read policy and branch scope, and treat out-of-scope or foreign branch ids as unknown (BR-01, BR-02). |
| FR-02 | The server must compute each job's operational status and current visit, and the frontend must display them without re-deriving them (BR-04, BR-05). |
| FR-03 | The list must support the tabs, search, branch, status and priority filters, sorting and pagination, and return the tab counts (BR-03, BR-06 – BR-10). |
| FR-04 | The summary must return the four metrics for the selected branch (BR-11). |
| FR-05 | The table must show the job, customer and property, schedule, technician, priority, status and last update for each row (BR-12, BR-13). |
| FR-06 | The row menu must offer only the navigation actions valid for the job's state and the caller's permissions, as decided by the server (BR-14). |
| FR-07 | The header actions must navigate to the schedule and apply the Ready to schedule filter (BR-15). |
| FR-08 | The page must keep its state in the URL, normalize invalid values and render the loading, empty, error and forbidden states (BR-16, BR-18). |
| FR-09 | `/schedule` must open the visit drawer for a `visit` query parameter on the initial load (BR-17). |
| FR-10 | The page must reproduce Design 30 with the specified responsive and accessibility behavior (BR-19, BR-20). |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization. The three endpoints use the existing work order Read policy. Other roles get `403` before any read. No endpoint accepts an organization id. `canManage` in the options and the per-row actions come from the Manage policy. Frontend visibility is UX only. | Backend |
| BR-02 | Scope. This rule reuses the existing branch scope of the jobs list. Owner, Operations Manager and `is_all_branches` members see all active branches. A Dispatcher (or other scoped member) sees only their `organization_user_branches`. A job counts when its `branch_id` is in the effective set (the selected branch, or every in-scope branch) and its status is not `draft`. A `branchId` of another organization, inactive, or outside scope → `404`, identical to an unknown id. No response contains data of another organization or of an out-of-scope branch. | Backend |
| BR-03 | Tabs, by work order status. **Active** = `scheduled`, `in_progress`. **Unscheduled** = `ready_to_schedule`. **Completed** = `completed`, `approved_for_billing`. **All jobs** = those three groups plus `cancelled`. Because All includes cancelled jobs, its count can exceed Active + Unscheduled + Completed. The mockup numbers are illustrative only. Tab counts apply branch, search and priority, but not the status filter. | Backend |
| BR-04 | Current visit. The current visit is the job's lowest `visit_number` visit whose status is not `completed`, `needs_correction`, `approved` or `cancelled`. Without one, it is the highest `visit_number` non-cancelled visit. Without one, it is the highest `visit_number` visit. A job with no visits has none. | Backend |
| BR-05 | Operational status (code → label, chip tone). `ready_to_schedule` → `ready_to_schedule` "Ready to schedule" (neutral). `scheduled` → the current visit's `assigned` "Assigned" (purple) or `on_the_way` "On the way" (info), otherwise `scheduled` "Scheduled" (info). `in_progress` → `paused` "Paused" (warning) when the current visit is `paused`, otherwise `in_progress` "In progress" (warning). `completed` → `needs_correction` "Needs correction" (danger) when any non-cancelled visit is `needs_correction`, otherwise `completed` "Completed" (success). `approved_for_billing` → `approved_for_billing` "Approved for billing" (success). `cancelled` → `cancelled` "Cancelled" (neutral). The API returns the code. The frontend maps the code to its fixed label and tone and never derives a status from raw work order or visit fields. | Backend (derivation), Frontend (copy) |
| BR-06 | Status filter. Allowed codes per tab: Active = `scheduled`, `assigned`, `on_the_way`, `in_progress`, `paused`. Unscheduled = `ready_to_schedule`. Completed = `completed`, `needs_correction`, `approved_for_billing`. All = every BR-05 code. A code outside the tab's set → `400 errors.status`. The dropdown offers "All statuses" plus the tab's codes by label. Changing tab resets the status to All statuses. | Both |
| BR-07 | Priority filter. "All priorities", Urgent (`urgent`, 1), High (`high`, 2), Normal (`normal`, 3) and Low (`low`, 4 – 5), mapped with the existing priority codes. An unknown code → `400 errors.priority`. | Both |
| BR-08 | Search `q`. The term is trimmed; empty means no search; more than 100 characters → `400 errors.q`. Matching is case-insensitive. A job matches when the term, without a leading `#`, is `<work order prefix>-<digits>` or plain digits equal to `work_order_number`, or when it is contained in `work_orders.title`, `customers.display_name`, `properties.name`, `properties.address_line1`, `properties.city` or `properties.postal_code`. The frontend sends the term 300 ms after the last keystroke. | Both |
| BR-09 | Sort `sort`. `newest` (default) = `created_at` descending. `oldest` = `created_at` ascending. `updated` = BR-13 updated instant descending. `schedule` = the current visit's `scheduled_start` ascending, jobs without one last. `priority` = priority ascending (urgent first), then `created_at` descending. Ties fall back to `work_order_number` descending. An unknown value → `400 errors.sort`. Labels: Newest, Oldest, Recently updated, Schedule, Priority. | Both |
| BR-10 | Pagination. `page` ≥ 1 and `pageSize` 1 – 100, as today (`400` otherwise). The API default stays 25; the UI always sends `pageSize` and offers 10 (default), 25 and 50 rows per page. A page past the last returns empty `items` with the real `total`. The frontend then requests the last page (`replaceUrl`). The footer shows "<from>–<to> of <total>", **Rows per page**, previous and next buttons, and page numbers. | Both |
| BR-11 | Summary metrics, for the effective branch set only (no tab, search, status or priority). Time zone: the selected branch's `timezone`, falling back to `organizations.timezone`; with All, `organizations.timezone`. Today = `[00:00, 24:00)` local in that zone; `ref` = now. **Active jobs**: the Active tab count, caption "<n> in progress" (`in_progress` jobs). **Ready to schedule**: `ready_to_schedule` jobs, caption "Needs a visit". **Today**: in-scope visits of non-`draft`, non-`cancelled` jobs with `scheduled_start` today and status not `unscheduled` or `cancelled`, caption "<a> assigned · <c> completed" (a = `assigned`, `on_the_way`, `in_progress`, `paused`; c = `completed`, `needs_correction`, `approved`). **Needs attention**: distinct jobs with a today visit that is delayed (`operations-dashboard` BR-07 with `ref` = now) or in `scheduled` (no technician), caption "Delayed or unassigned". Cards are not interactive. | Backend |
| BR-12 | Row content. **Job**: "#<WO-n>" as a link to `/jobs/:id`, with the title below. **Customer & property**: customer display name; property name · "<postal code> <city>" (postal code omitted when null). **Schedule**: the current visit's start–end in the job's branch time zone (fallback organization), as "Today · h:mm–h:mm a", "Tomorrow · …" or "MMM d · …" (with ", yyyy" in another year). When the start and end meridiems differ: "h:mm a–h:mm a". "Not scheduled" without a schedule. **Technician**: the current visit's active (`unassigned_at` null) primary assignment, shown as initials avatar (profile `color_hex` when set) and full name; "Unassigned" with a dash avatar when none. **Priority**: label (High and Urgent emphasized; text is always present). **Status**: BR-05 chip with a dot and the label. | Both |
| BR-13 | Updated. Updated = the greatest of `work_orders.updated_at` and its visits' `updated_at`. Display relative to now in the organization time zone: < 1 min "Just now"; < 60 min "<n> min ago"; same day "Today"; previous day "Yesterday"; otherwise "MMM d" (", yyyy" in another year). The full date and time is the accessible label and the tooltip. | Both |
| BR-14 | Row actions (server-computed `actions`, in this order). `view` **View job** → `/jobs/:id` (always). `schedule_visit` **Schedule visit** → `/schedule?branch=<branchId>&visit=<currentVisitId>` (Manage only, job `ready_to_schedule` with a current visit). `open_in_schedule` **Open in schedule** → `/schedule?branch=<branchId>&view=day&date=<local date of scheduled_start in the branch zone>&visit=<currentVisitId>` (any Read role, current visit has a schedule, job not `ready_to_schedule` or `cancelled`). `review_completion` **Review completion** → `/invoices/review?workOrderId=<workOrderId>` (any Read role, job `completed`). The menu button opens a menu with exactly these items. No action mutates data, and every destination keeps its own authorization. | Backend (eligibility), Frontend (targets) |
| BR-15 | Header. **Open schedule** → `/schedule`, with `branch=<id>` when a single branch is selected. **Ready to schedule** shows the summary `readyToSchedule` badge (hidden at 0). It sets the tab to Unscheduled, clears the status filter and goes to page 1, keeping branch, search, priority and sort. Both are shown to every Read role. | Frontend |
| BR-16 | URL state. `tab` (`all` default, `active`, `unscheduled`, `completed`), `q`, `branch` (absent = default), `status`, `priority`, `sort` (absent = `newest`), `page` (absent = 1), `pageSize` (absent = 10). Reload and back restore the view. Unknown or invalid values are replaced by their defaults with `replaceUrl` before any request. A `404` branch resets to the default branch (`replaceUrl`). Changing the search, a filter, the sort, the tab or the page size resets to page 1. **Clear filters** resets search, status and priority (not tab, branch or sort). Its badge counts the active ones, and it is disabled when none are active. | Frontend |
| BR-17 | Schedule deep link (cross-cutting). When `/schedule` loads with `visit=<visitId>`, the page selects the URL `branch` (or its default) and `view`/`date` when given, and opens that visit's drawer once the visit is loaded. An unknown or out-of-scope visit follows the drawer's existing `404` behavior ("This visit isn't available." toast, drawer closed, `visit` removed). Existing drawer rules (Viewer read-only, unsaved-changes guard) are unchanged. If the current page already does this, no change is required. | Frontend |
| BR-18 | Branch selector. "All branches" appears only with ≥ 2 in-scope branches, followed by each active in-scope branch by name. Default: All with ≥ 2, else the only branch. With no branch in scope: "No branches available." and no summary or list request. | Both |
| BR-19 | Layout (Design 30). Breadcrumb "Operations" (plain text) / "Jobs". Title "Jobs", subtitle "Track work orders from creation through completion.". From 1280 px: header actions right-aligned, four metrics in one row, filters in one row (search, branch, status, priority, sort, Clear filters), and the full table. From 768 to 1279 px: metrics 2 × 2, filters wrap, the table scrolls horizontally inside its card. Below 768 px: metrics one per row, header actions full width and stacked, filters one per row, the table scrolls inside its card, the pagination wraps. No page-level horizontal scroll at 375 px. The design's sample data is never copied. | Frontend |
| BR-20 | Accessibility. Tabs are a tablist whose names include the count. Status and priority are conveyed by text, never color alone. The row menu button's name is "Actions for #<WO-n>". Filters and sort have accessible labels. Metric icons are decorative. Result changes are announced politely ("<total> jobs"). Every control has a visible focus indicator. Touch targets are ≥ 44 px below 768 px. | Frontend |
| BR-21 | Query efficiency. The list and the summary each run a bounded number of database queries, independent of page size and the number of rows (no per-row queries). Counts, filters, sorting and paging run in the database. | Backend |

## States and transitions

This feature changes no state. Work order and visit statuses keep their
transitions in `create-work-order`, `dispatch-calendar`, the mobile job specs
and `completed-jobs-review`.

## Data and persistence impact

- Tables and columns read (from `docs/database/fieldops-schema.sql`):
  - `work_orders`: `id`, `organization_id`, `branch_id`,
    `work_order_number`, `customer_id`, `property_id`, `title`, `status`,
    `priority`, `created_at`, `updated_at`.
  - `visits`: `id`, `work_order_id`, `visit_number`, `status`,
    `scheduled_start`, `scheduled_end`, `arrival_window_end`, `updated_at`.
  - `visit_assignments`: `visit_id`, `technician_id`, `is_primary`,
    `unassigned_at`.
  - `technician_profiles`: `first_name`, `last_name`, `color_hex`.
  - `customers.display_name`.
  - `properties`: `name`, `address_line1`, `city`, `postal_code`.
  - `branches`: `id`, `name`, `timezone`, `is_active`.
  - `organizations`: `timezone`, `work_order_prefix`.
  - `organization_users.is_all_branches` and `organization_user_branches`, for scope.
- Writes: none.
- Schema amendments required: None.

## Tenant isolation and authorization

- Organization context: resolved from the session membership; every query
  filters by it.
- Client-provided organization identifiers: none accepted. `branchId` is
  authorized against the caller's scope (BR-02); a foreign or out-of-scope
  value → `404`.
- Jobs, visits, technicians, customers and properties of another organization
  or an out-of-scope branch never appear in items, counts, metrics or options.
- Read policy for all three endpoints. Manage only affects `canManage` and
  `schedule_visit`.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/work-orders/options` | — | `200 { branches: [{ id, name, timezone \| null }], organizationTimezone, canManage }` (active, in scope, by name) | `403` | Read |
| GET | `/work-orders/summary` | Query: `branchId?` | `200 { timezone, activeJobs: { total, inProgress }, readyToSchedule, today: { total, assigned, completed }, needsAttention }` | `400` (malformed id) · `403` · `404` (branch) | Read |
| GET | `/work-orders` | Query: `tab?` (`all` default, `active`, `unscheduled`, `completed`), `q?`, `branchId?`, `status?`, `priority?`, `sort?` (`newest` default), `page?` (1), `pageSize?` (25) | `200 { items: JobRow[], total, counts: { all, active, unscheduled, completed } }` | `400` (`tab`, `q`, `status`, `priority`, `sort`, `page`, `pageSize`, malformed id; ProblemDetails `errors.<field>`) · `403` · `404` (branch) | Read |

`JobRow` = `{ id, displayNumber, title, customerName, property: { name,
postalCode | null, city }, branch: { id, name }, timezone, status (BR-05
code), priority (code), currentVisit: { id, scheduledStart | null,
scheduledEnd | null } | null, technician: { fullName, initials, colorHex |
null } | null, updatedAt, createdAt, actions: ("view" | "schedule_visit" |
"open_in_schedule" | "review_completion")[] }`. `timezone` is the branch zone,
falling back to the organization's. Instants are ISO 8601 with offset. Every
response is `no-store`. `total` is the count of the filtered set, and `items`
holds the requested page. The previous `WorkOrderPage` shape is replaced.
`/jobs` is its only consumer.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/jobs` options | Metric and table skeletons; filters disabled | — | Full-page "We couldn't load jobs. Try again." with **Retry**; neither summary nor list is requested until options load | `403`: the existing forbidden state, no further calls | Branch selector, time zone and `canManage` ready (BR-18) |
| `/jobs` metrics | Four card skeletons | — | "We couldn't load job metrics. Try again." with **Retry** in the metrics row; the table keeps working | `403`: page forbidden state | BR-11 values |
| `/jobs` table | Row skeletons; filters stay usable; the previous rows are replaced | All tab, no search or filters, total 0: "No jobs yet. Jobs are created from approved quotes.". Otherwise: "No jobs match the current view." with **Clear filters** when any filter is active | "We couldn't load jobs. Try again." with **Retry** | `403`: the existing forbidden state, no further calls | BR-12 – BR-14 rows and BR-10 footer |
| No branches | — | "No branches available." | — | — | — |

- Mockups: `design/assets/30-design.png` (Approved). The sidebar and top bar
  are the existing app shell. The sample rows and numbers are illustrative.
- Responsive and accessibility: BR-19, BR-20.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid URL value (tab, status for tab, priority, sort, page, pageSize, q > 100) | Replaced by the default with `replaceUrl` before the request; a server `400` resets the offending parameter the same way | None |
| `branch` foreign, inactive or out of scope | `404`; the page resets to the default branch (`replaceUrl`) | None |
| Page past the last | Empty items; the page requests the last page (`replaceUrl`) | None |
| Role without Read | `403`; forbidden state | None |
| Options request fails (network or `5xx`) | Full-page "We couldn't load jobs. Try again." with **Retry**; no summary or list request | None |
| Network or `5xx` on summary or list | Metrics or table error state with **Retry** | None |
| `/schedule?visit=` unknown or out of scope | Existing toast "This visit isn't available."; drawer closed | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Users with each role | They call the three endpoints and open `/jobs` | Owner, Operations Manager, Dispatcher and Viewer get `200`; Technician, Accounting and unknown roles get `403` and the forbidden state with no further calls; `canManage` is true only for Manage roles |
| AC-02 | Jobs in two organizations and in two branches, and a Dispatcher scoped to one branch | The Dispatcher lists, counts and summarizes, and passes the other branch's or organization's id | Only in-scope jobs appear in items, counts and metrics; the foreign and out-of-scope `branchId` return `404`, identical to an unknown id; options list only in-scope active branches |
| AC-03 | Jobs in every work order status, including `draft` and `cancelled` | Each tab is listed | Tab membership and counts follow BR-03; drafts never appear; All includes cancelled jobs, so its count may exceed the sum of the other three |
| AC-04 | Jobs whose visits cover every BR-05 case (one or several visits, recurring with a next unscheduled visit, a `paused` visit, a `needs_correction` visit, no visits) | The list is returned | Each row's `status` and `currentVisit` follow BR-04 and BR-05 |
| AC-05 | Jobs of every operational status | The status filter is applied per tab, including a code outside the tab | Only matching rows are returned; tab counts ignore the status filter; an out-of-tab code returns `400 errors.status`; the dropdown offers only the tab's statuses and resets on tab change |
| AC-06 | Jobs with distinct numbers, titles, customers, property names, addresses, cities and postal codes | Searching "WO-7", "#wo-7", "7", part of a customer name and part of a city, and a 101-character term | Matches follow BR-08 case-insensitively; the 101-character term returns `400 errors.q`; the frontend debounces by 300 ms |
| AC-07 | Jobs of each priority | The priority filter is applied, including an unknown code | Rows and counts follow BR-07; an unknown code returns `400 errors.priority` |
| AC-08 | Jobs with distinct created, updated, scheduled and priority values, including unscheduled ones | Each sort is requested | Order follows BR-09, including nulls last for `schedule` and the tie-breaker; an unknown sort returns `400` |
| AC-09 | 23 jobs in scope | Pages are requested with sizes 10, 25 and 50, a page past the last, and invalid page values | Items, `total` and the footer "<from>–<to> of <total>" follow BR-10; past the last page the UI moves to the last page; invalid values return `400` |
| AC-10 | Jobs and visits around the boundaries of today in the selected branch time zone, delayed, on-time and unassigned visits, and a second branch | The summary is requested for one branch and for All | Active jobs, in-progress, Ready to schedule, Today (assigned/completed split) and Needs attention (distinct jobs) follow BR-11 in the right time zone; the tab, search, status and priority do not change them |
| AC-11 | Rows with and without a schedule, today, tomorrow and other dates and years, with and without a primary active technician, and with a removed assignment | The table renders | Job link, customer and property, schedule text in the branch time zone, technician avatar and name or "Unassigned", priority and status chip follow BR-12 |
| AC-12 | Jobs updated just now, minutes ago, earlier today, yesterday, and in another year, and a visit updated after its work order | The table renders | The Updated text and its accessible full date follow BR-13 using the latest of the work order and visit instants |
| AC-13 | Jobs in each status, with and without a scheduled current visit, viewed by a Manage role and by a Viewer | The row menus are opened | Items match the server `actions` exactly as in BR-14; Viewer never sees **Schedule visit**; cancelled jobs offer only **View job** |
| AC-14 | Row menu items and the job number | Each is activated | **View job** and the number open `/jobs/:id`; **Schedule visit**, **Open in schedule** and **Review completion** navigate to the BR-14 URLs with the right branch, local date, visit and work order ids |
| AC-15 | `/schedule` opened with `branch` and `visit` (scheduled and unscheduled), and with an unknown or out-of-scope `visit` | The page loads | The drawer opens for the visit (read-only for a Viewer); the unknown visit shows "This visit isn't available." and removes `visit`, per BR-17 |
| AC-16 | The workspace with a branch selected, and with All | **Open schedule** and **Ready to schedule** are used | **Open schedule** opens `/schedule` with the branch only when one is selected; **Ready to schedule** shows the summary badge (hidden at 0) and switches to Unscheduled, clears status and goes to page 1, keeping branch, search, priority and sort |
| AC-17 | A URL with every parameter set, another with invalid values, and a `404` branch | The page is loaded, reloaded and navigated back | Valid state is restored; invalid values and the `404` branch are replaced by defaults with `replaceUrl` before or after the request per BR-16; changes reset to page 1; **Clear filters** resets search, status and priority, shows their count and is disabled with none |
| AC-18 | Organizations with one, two and zero in-scope branches | The page loads | The branch selector and defaults follow BR-18; with zero, "No branches available." shows and neither summary nor list is requested |
| AC-19 | Loading, empty (no jobs), filtered empty, options failure, list failure and summary failure | The page renders each case | The UI table states and copy appear; an options failure shows the full-page error with **Retry** and requests neither summary nor list; a summary failure shows its error with **Retry** while the table still works; **Retry** reloads only the failed part |
| AC-20 | Design 30 at 1280, 1024 and 375 px | The page is reviewed | Layout follows BR-19 with no page-level horizontal scroll at 375 px; accessibility follows BR-20 (tab names with counts, text-conveyed status and priority, menu button names, polite result announcement, focus and touch targets) |
| AC-21 | A page of 50 rows with several visits and assignments per job | The list and summary are requested | Each runs the same bounded number of queries as for one row (BR-21) |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Table-driven current-visit and operational-status derivation, and action eligibility by status, schedule and `canManage` (BR-04, BR-05, BR-14). Search term parsing, only if it is a separate utility. | AC-04, AC-13 |
| Backend integration | Grouped list flow: tabs and counts, drafts excluded, cancelled in All, status per tab and out-of-tab `400`, search, priority, sorts, pagination and validation errors. Summary metrics in the branch time zone with delayed and unassigned visits. Row content: schedule, technician, updated instant, actions. Bounded query count. Expected 4 – 6 methods. | AC-03 – AC-12, AC-21 |
| Authorization/tenant isolation | One test covering role `403`s and `canManage`, a Dispatcher's scope over items, counts and metrics, and one representative cross-organization and out-of-scope `branchId` `404`. | AC-01, AC-02 |
| Frontend component/service | URL state ↔ request mapping, normalization, page reset and **Clear filters**. **Ready to schedule** and **Open schedule** behavior. Row menu built from `actions` with BR-14 targets and the status label map. Table and metrics states (empty, filtered empty, partial failure, forbidden, no branches). `/schedule` `visit` deep link opening the drawer, only if code changes there. Expected 4 – 6 methods. | AC-05, AC-09, AC-13 – AC-19 |
| Browser (final audit) | User-supplied visual QA of Design 30 at 1280, 1024 and 375 px, and the deep link to `/schedule`. No agent-driven browser automation. | AC-11, AC-12, AC-15, AC-20 |

## Dependencies

- `create-work-order` (jobs list and detail, Read/Manage policies, BR-04 **Continue draft**): implemented.
- `dispatch-calendar` (`/schedule` URL parameters and drawer): implemented.
- `operations-dashboard` BR-07 (delay definition), reused for Needs attention: implemented.
- `completed-jobs-review` (`/invoices/review?workOrderId=` preselection): implemented.
- Mobile job specs (`mobile-job-details`, `mobile-job-progress`, `mobile-job-completion`), the source of the visit and work order execution statuses: implemented.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Chip tones reuse the existing status chip tokens; no new colors are introduced. |
| AS-02 | Technician initials use the same rule as the dispatch calendar lanes. |
| AS-03 | The current jobs error and empty copy are kept where the BR text matches it. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Status shown per job | Server operational status from work order + current visit · work order status only | No | Operational status, computed on the server; the frontend only displays the label (user 2026-10-14) |
| OD-02 | Drafts on `/jobs` | Exclude · include in All | No | Excluded; still reachable through **Continue draft** on the quote (user 2026-10-14) |
| OD-03 | Tabs and counts | BR-03 / BR-06 definition | No | Accepted; All includes cancelled; mockup numbers illustrative (user 2026-10-14) |
| OD-04 | Metrics | BR-11 definitions | No | Accepted (user 2026-10-14) |
| OD-05 | Computed columns | BR-12 / BR-13 | No | Accepted (user 2026-10-14) |
| OD-06 | Search, sort, page sizes, URL | BR-08 – BR-10, BR-16 | No | Accepted (user 2026-10-14) |
| OD-07 | Row menu | Navigation only, server-computed actions | No | Accepted; authorizes the `/schedule?visit=` drawer deep link; Review completion → `/invoices/review?workOrderId=` (user 2026-10-14) |
| OD-08 | Sliders button | Clear filters · remove | No | Clear filters with active-filter badge (user 2026-10-14) |
| OD-09 | Contract | Extend list + summary + options | No | Accepted; Read policy unchanged (user 2026-10-14) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-04 |
| FR-03 | AC-03, AC-05, AC-06, AC-07, AC-08, AC-09, AC-21 |
| FR-04 | AC-10, AC-21 |
| FR-05 | AC-11, AC-12 |
| FR-06 | AC-13, AC-14 |
| FR-07 | AC-16 |
| FR-08 | AC-17, AC-18, AC-19 |
| FR-09 | AC-15 |
| FR-10 | AC-20 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-14 | — → DRAFT     | Created |
| 2026-10-14 | DRAFT → DRAFT | Validation findings: options loading/error state (UI table, error behavior, AC-19); scope columns in data impact; API vs UI page size clarified; AS-03 without code constant |
| 2026-10-14 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-14 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-14 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
