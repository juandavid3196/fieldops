# Operations dashboard

| Field    | Value                  |
| -------- | ---------------------- |
| Feature  | `operations-dashboard` |
| Type     | Full-stack             |
| Status   | AUDITED                |
| Created  | 2026-10-13             |
| Updated  | 2026-10-14             |
| Approved | 2026-10-13             |

## Context and objective

Owners, Operations Managers and Dispatchers have no single place that shows
how the day is going. They have to cross Jobs, Schedule and Team to find
delayed or unassigned visits and overloaded technicians. This feature adds a
read-only Operations Dashboard (Design 15) at `/dashboard`. It is built only
from existing work orders, visits, assignments, status history, technician
availability and reviews, and it is scoped by organization, branch scope,
the selected branch, filters and time zone. Every action navigates to Jobs,
Schedule or Team. Schedule gains a `visit` query parameter that opens the
dispatch drawer directly.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner, Operations Manager (`DispatchPolicies.Manage`) | Read the dashboard for all active branches of the organization or one branch | Change data from the dashboard |
| Dispatcher (`DispatchPolicies.Manage`) | Read the dashboard for their `organization_user_branches` (all active branches when `is_all_branches`) | See branches, visits or technicians outside their scope (`404` / excluded) |
| Viewer, Accounting, Technician, unknown roles | Keep the existing `/overview` welcome page | Open the dashboard (`403`; page shows the forbidden state) |

## Scope

- `/dashboard` page per Design 15: header, branch selector, date picker,
  filters, KPI row, Today's operation, Needs attention, Technician
  availability, Estimated vs actual duration, Service performance, Live
  operational activity (BR-01 … BR-17).
- Two read endpoints: options and dashboard data, plus a performance endpoint
  for the period selector (API contracts).
- Sidebar: "Dashboard" replaces "Overview" for the Manage roles; `/overview`
  redirects them to `/dashboard` (BR-01).
- Automatic refresh every 60 seconds and manual refresh (BR-16).
- Schedule accepts `visit=<visitId>` to open the dispatch drawer (BR-18).

## Non-goals

- Any write from the dashboard (assign, reassign, check in, contact logging).
  Writes stay in Schedule and Jobs.
- A global branch selector in the shell top bar; the notification bell and
  its counter (unchanged, still Coming soon); the shell search (unchanged).
- GPS, real ETA, traffic data, travel time, or notes such as "Traffic on
  I-35".
- A configurable utilization target ("Target 75–85%").
- New schema, migrations, audit rows or notifications.
- A Reports module, exports or historical analytics beyond BR-13/BR-14.
- Automated browser, E2E or visual tests by agents.

## User flow

1. An Owner, Operations Manager or Dispatcher signs in or opens **Dashboard**
   in the sidebar. `/overview` redirects them to `/dashboard`.
2. The page loads options, resolves branch, date and filters from the URL, and
   shows every card for the selected day.
3. The user changes branch, date, technician or category; the URL updates and
   the cards reload.
4. From a row or alert the user opens the job, opens Schedule on that visit's
   drawer, opens Schedule for the day, calls the customer, or opens Team.
5. Every 60 seconds the data refreshes and "Updated just now" resets.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The frontend must serve `/dashboard` for the Manage roles, show "Dashboard" in the sidebar for them, and redirect `/overview` to `/dashboard` for them while other roles keep `/overview` (BR-01). |
| FR-02 | Every endpoint must resolve the organization from the session, require `DispatchPolicies.Manage`, and apply branch scope to branches, visits, work orders and technicians (BR-02). |
| FR-03 | The page must keep branch, date, technician, category and performance period in the URL, resolve defaults, and compute every day and time in the BR-03 time zone (BR-03, BR-04, BR-05). |
| FR-04 | The KPI row must show Today's jobs, Unassigned, At risk / delayed, Technician utilization and On-time arrival from real data (BR-06 … BR-09). |
| FR-05 | Today's operation must list the selected day's visits with status, time, job, customer, technician, timing and actions (BR-10). |
| FR-06 | Needs attention must list delayed, unassigned, over-estimate and over-capacity items with their navigation actions (BR-11). |
| FR-07 | Technician availability must show each in-scope technician's skills, current status, workload and next available time (BR-12). |
| FR-08 | Estimated vs actual duration must compare average estimated and actual durations per service category over the 7 days ending on the selected date (BR-13). |
| FR-09 | Service performance must show jobs completed, first-time fix rate, customer rating and a daily completed-jobs line for the last 7 or 30 days (BR-14). |
| FR-10 | Live operational activity must list the selected day's derived visit events (BR-15). |
| FR-11 | The page must refresh automatically every 60 seconds and on demand, and show when it was last updated (BR-16). |
| FR-12 | The page must reproduce Design 15 with the documented deviations and the loading, empty, error, permission and responsive states (BR-17). |
| FR-13 | Schedule must open the dispatch drawer for the visit given in `visit` and remove the parameter when the drawer closes (BR-18). |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Route and navigation. `/dashboard` lives in the authenticated shell. Manage roles (`owner`, `operations_manager`, `dispatcher`) see the first sidebar item as **Dashboard** (`/dashboard`, same icon); other roles keep **Overview** (`/overview`). Opening `/overview` as a Manage role redirects to `/dashboard` with `replaceUrl`; other roles see the unchanged welcome page. Opening `/dashboard` as another role shows the existing forbidden state and makes no data request. Frontend visibility is UX only; the backend decides (`403`). | Both |
| BR-02 | Scope (`dispatch-calendar` BR-02). Owner and Operations Manager, and `is_all_branches` members, see all active branches; a Dispatcher sees only `organization_user_branches`. A visit counts when its work order's `branch_id` is in the effective branch set and the work order status is not `draft` or `cancelled`. Technicians count when `status = 'active'` and `branch_id` is in the set. A `branchId` or `technicianId` of another organization or outside scope → `404`, identical to an unknown id. A `categoryId` that is not an active category of the organization → `400 errors.categoryId`. No response contains data of another organization or of an out-of-scope branch. | Backend |
| BR-03 | Branch and time zone. The selector offers **All branches** (only with ≥ 2 in-scope branches) and each active in-scope branch by name. Default: All with ≥ 2 branches, else the only branch. With no branch in scope: "No branches available." and no data request. The effective set is the selected branch or all in-scope branches. Time zone: the selected branch `timezone` falling back to `organizations.timezone`; with All, `organizations.timezone`. The selected day is `[00:00, 24:00)` local of that zone. URL `branch` = branch id, absent for All. | Both |
| BR-04 | Date. The date picker accepts any date from today − 365 to today + 365 days (local), with a **Today** shortcut; URL `date` = `YYYY-MM-DD`, default today. Out-of-range or malformed → `400 errors.date`; the frontend resets to today. The reference instant `ref` is now for today, the end of the day for past dates, and the start of the day for future dates. The header reads "<EEEE, MMM d, yyyy> · <branch name> operations" or "· All branches". | Both |
| BR-05 | Filters. **Filters** opens a panel with **Technician** (All or one active in-scope technician of the effective set, by name) and **Service category** (All or one active organization category, by name), plus **Clear filters**. The button shows the number of active filters. URL `technician`, `category`. Technician limits visit-based figures to visits with an active (`unassigned_at` null) assignment of that technician, and limits utilization, technician rows and capacity alerts to that technician. Category limits visit-based figures to work orders of that `service_category_id`; it does not limit utilization or technician rows. A `404` technician or `400` category from the API resets that filter to All (`replaceUrl`). | Both |
| BR-06 | Day visits. The day's visits are visits in scope (BR-02, BR-05) with `scheduled_start` in the selected day and status not `unscheduled` or `cancelled`. Status groups: **scheduled** = `scheduled`, `assigned`; **active** = `on_the_way`, `in_progress`, `paused`; **completed** = `completed`, `needs_correction`, `approved`. KPI **Today's jobs** (label "Jobs" plus the date when the day is not today): total, and "<c> completed · <a> active · <s> scheduled". KPI **Unassigned**: day visits in `scheduled` (no active assignment), with **Assign now** → `/schedule?branch=<b>&view=day&date=<date>` (`branch` omitted with All). | Both |
| BR-07 | Delay and risk. Limit = `arrival_window_end` when set, else `scheduled_start`. A day visit is **delayed** when its status is `scheduled`, `assigned` or `on_the_way` and `ref` > limit (delay = `ref` − limit), or its status is `in_progress` or `paused` and `ref` > `scheduled_end` (delay = `ref` − `scheduled_end`). It is **at risk** when not delayed, status `scheduled`, and `scheduled_start` − `ref` is between 0 and 60 minutes. Delays are whole minutes rounded down; a delay of 0 minutes is not delayed. KPI **At risk / delayed** = distinct delayed or at-risk visits, with **Review** focusing the Needs attention card. | Backend |
| BR-08 | Utilization. Per technician and day, scheduled and available minutes follow `team-technician-management` BR-04/BR-06 and count scheduled assessments as `dispatch-calendar` BR-05 does. KPI **Technician utilization** = Σ scheduled ÷ Σ available over the counted technicians, rounded to an integer percent; "—" when Σ available = 0. Caption "<x>h scheduled of <y>h available". No target text. | Backend |
| BR-09 | On-time arrival. Arrival limit = `arrival_window_end` when set, else `scheduled_start` + 15 minutes. Eligible: day visits with `actual_started_at` set, plus day visits in `scheduled`, `assigned` or `on_the_way` whose arrival limit < `ref`. On time: `actual_started_at` ≤ arrival limit. KPI = on-time ÷ eligible, integer percent; "—" with caption "No arrivals yet" when eligible = 0. Comparison: the same rule for the same weekday one week earlier (`ref` = end of that day); caption "<+n>% vs last week" / "<−n>% vs last week" / "Same as last week" in percentage points, with an up or down icon plus text; hidden when either side has no eligible visits. | Backend |
| BR-10 | Today's operation. Card title "Today's operation" when the selected date is today, else "Operation · <MMM d>". All day visits, sorted by **Time** (`scheduled_start`) ascending by default, toggled ascending/descending by the column header, ties by work order number; 8 rows per page with a paginator; **View all jobs** → `/jobs`. Columns: **Status** chip (Delayed when delayed, else Scheduled / On the way / In progress / Paused / Completed by status group; text plus icon); **Time** (`h:mm a`); **Job** ("WO-n" and title); **Customer** name; **Technician** (primary active assignment full name, "+n" for other active assignments, "Unassigned" when none); **Timing** (below); **Actions**: one button and a menu. Timing: completed group → "Completed" and "h:mm a" of `actual_completed_at`; `scheduled`/`assigned`/`on_the_way` → "Starts h:mm a" and, when delayed, "+<n> min" (text and icon), else "On time" only when `on_the_way`; `in_progress`/`paused` → "Ends ~h:mm a" where projected end = `actual_started_at` + `pause_seconds` + (`scheduled_end` − `scheduled_start`), and "On time" or "+<n> min" against `scheduled_end`. Button: `scheduled` → **Assign**; `assigned` and delayed → **Reassign** (both → BR-18 link); `on_the_way` with a customer phone → **Contact** (`tel:` of the customer's primary active contact phone); otherwise **View** → `/jobs/<workOrderId>`. Menu: **View job** and **Open in schedule** (BR-18 link). Empty: "No jobs scheduled for this day." with **Open dispatch board**. | Both |
| BR-11 | Needs attention. Items for the selected day only when it is today or past; a future day shows "Nothing needs attention." Types, in this order: (1) delayed visits by delay descending: "<WO-n> delayed by <n> min", subtitle title, time `scheduled_start`; (2) day visits in `scheduled` not already listed as delayed, by `scheduled_start`: "<WO-n> has no technician assigned", subtitle "<title> · Starts at h:mm a"; (3) visits in `in_progress`/`paused` whose work order has `estimated_duration_minutes` and whose elapsed work (`ref` − `actual_started_at` − `pause_seconds`) exceeds it: "<WO-n> estimated duration exceeded", subtitle "<title> · <n> min over estimate", time `actual_started_at`; (4) technicians whose day workload (BR-08) > 100 %: "Technician <full name> at <p>% capacity", subtitle "<x>/<y> hrs scheduled · Consider rebalancing". Action button: visit in `scheduled` → **Assign**, `assigned` → **Reassign** (BR-18 link); other visit items → **View job**; technician → **Balance workload** → `/schedule?branch=<technician branch>&view=day&date=<date>`. Menu: visit items **View job**; technician items **View team** → `/team`. Severity is shown with icon and text, never color alone. The card shows 4 items; **View all (<n>)** expands the card to all items and **Show less** collapses it. Empty: "Nothing needs attention." | Both |
| BR-12 | Technician availability. Counted technicians (BR-05), ordered by workload descending then name; 6 rows with **View team** → `/team`. Columns: initials (profile `color_hex` when set) and full name; **Skills** (up to 2 names, primary first, then name; "+n" for the rest; "—" when none); **Current status**, only when the day is today, else "—": Time off (an active `is_available = false` exception covers now), Off (now outside availability), Break (now inside a break), On job (an active assignment on a visit in `on_the_way`, `in_progress` or `paused`), else Available, each with icon and text; **Workload** bar and integer percent (BR-08; "No availability" when available is 0 and scheduled > 0, "—" when both are 0), bar marked over capacity above 100 % with the text percent; **Next available** (today only, else "—"): "Now" when Available, else the earliest local time ≥ now inside availability and outside breaks, time off, scheduled assessments and active-assignment visit intervals not in the completed group; "—" when none today. Empty: "No active technicians in this branch." with **Go to Team**. | Both |
| BR-13 | Estimated vs actual duration. Window: the 7 local days ending on the selected date. Visits in scope whose `actual_completed_at` is in the window, status in the completed group, with `actual_started_at` set and the work order `estimated_duration_minutes` set. Actual = `actual_completed_at` − `actual_started_at` − `pause_seconds`, in minutes. Per service category: average estimated and average actual, rounded to minutes, shown as "<h>h <m>m" bars with a legend Estimated / Actual and variance "+<n> min" / "−<n> min" / "On estimate" (text, not color alone). Up to 4 categories by visit count, then name. Caption "Last 7 days · <n> visits". Empty: "No completed visits with estimates in the last 7 days." | Both |
| BR-14 | Service performance. Period selector **Last 7 days** / **Last 30 days** (URL `period` = `7`/`30`, default `7`), the local days ending on the selected date. **Jobs completed** "<x>/<y>" and percent ring: y = visits in scope with `scheduled_start` in the period and status not `unscheduled`/`cancelled`; x = those in the completed group. **First-time fix rate**: work orders in scope with `job_type = 'one_time'` and status `completed` or `approved_for_billing` whose latest `actual_completed_at` is in the period; rate = those with exactly one non-cancelled visit and no `visit_status_history` row to `needs_correction` ÷ all of them; integer percent, "—" when none. **Customer rating**: average `invoice_reviews.rating` created in the period on work orders in scope (technician filter: `technician_id`), one decimal, "<r>/5" and "<n> reviews"; "—" with "No reviews" when none. Line chart "Jobs completed (last <7|30> days)": per local day, visits whose `actual_completed_at` falls on it, completed group. Category and technician filters apply as BR-05. | Both |
| BR-15 | Live operational activity. Events of the selected local day for visits in scope, newest first: `visit_status_history` to `on_the_way` → "Technician on the way", "<technician> is on the way to <WO-n>"; to `in_progress` from a status other than `paused` → "Job started", "<technician> started <WO-n>"; to `completed` → "Job completed", "<WO-n> · <title> · <technician>"; `visit_assignments.assigned_at` with `is_primary` → "Technician assigned", "<technician> assigned to <WO-n>", or "Assignment changed", "<WO-n> reassigned to <technician>" when the visit had an earlier primary assignment. `<technician>` is the primary active assignment's full name at read time, or for assignment events that assignment's technician; "A technician" when none. Each row: time `h:mm a`, icon by type, title, subtitle, link to `/jobs/<workOrderId>`. The card shows the 4 newest; **View all** → `/jobs`. Empty: "No activity for this day." The API returns at most 50 events. | Both |
| BR-16 | Refresh. While the tab is visible the dashboard and performance data reload every 60 seconds; when the tab becomes visible after more than 60 seconds hidden, they reload at once. A refresh button reloads on demand. The header shows "Updated just now" (< 1 minute) or "Updated <n> min ago", updated each minute. A failed automatic refresh keeps the shown data and shows "Couldn't refresh." with **Try again**; the next tick retries. Changing branch, date, filters or period cancels in-flight requests. Responses are `Cache-Control: no-store`. | Frontend |
| BR-17 | Layout and accessibility. From 1280 px: KPI row of 5; Today's operation beside Needs attention and Technician availability; the three analytics cards in one row. 768–1279 px: KPIs wrap to 3 + 2; cards stack in two columns. Below 768 px: one column, KPIs 2 per row, tables scroll horizontally inside their card only, no page horizontal scroll. Charts have a text alternative (a visually hidden table of the plotted values). Status, delay, severity and trend are always conveyed by text and icon. Touch targets ≥ 44 px below 768 px. **Open dispatch board** → `/schedule?branch=<b>&view=day&date=<date>` (`branch` omitted with All). | Frontend |
| BR-18 | Schedule visit link. `/schedule?branch=<work order branch>&view=day&date=<visit local date>&visit=<visitId>`. After the Schedule route state is resolved, a `visit` parameter opens the existing dispatch drawer for that visit through `GET /dispatch/visits/{visitId}` with its existing rules (locked visits read-only, Viewer read-only). Closing the drawer removes `visit` from the URL with `replaceUrl`. A `404` shows the existing toast "This visit isn't available." and removes the parameter. Without `visit`, Schedule is unchanged. | Frontend |

## States and transitions

None. The feature is read-only; Schedule's dispatch transitions are unchanged.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`), read
  only:
  - `organizations` (`timezone`), `branches` (`name`, `timezone`, `is_active`), `organization_users`, `organization_user_branches`.
  - `work_orders` (`branch_id`, `work_order_number`, `title`, `status`,
    `job_type`, `service_category_id`, `estimated_duration_minutes`,
    `customer_id`), `service_categories`.
  - `visits` (`status`, `scheduled_start`, `scheduled_end`,
    `arrival_window_start`, `arrival_window_end`, `actual_started_at`,
    `actual_completed_at`, `pause_seconds`), `visit_assignments`,
    `visit_status_history`.
  - `technician_profiles`, `technician_skills`, `skills`,
    `technician_weekly_availability`, `technician_breaks`,
    `technician_exceptions`, `assessments` (scheduled, for load).
  - `customers` (`display_name`), `customer_contacts` (`phone` of the
    `is_primary`, `is_active` contact).
  - `invoice_reviews` (`rating`, `technician_id`, `work_order_id`,
    `created_at`).
- No writes; no audit rows.
- Schema amendments required: None.
- Indexes relied on: `ix_visits_schedule`, `ix_work_orders_status`, the
  composite `(organization_id, id)` keys. Queries are bounded by the selected
  day or period (≤ 30 days) and the effective branch set; no unbounded scans.

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  query carries an organization id; any such parameter is ignored.
- Client-provided identifiers: `branchId`, `technicianId` (query; `404`
  outside scope or foreign), `categoryId` (query; `400` foreign or inactive),
  `visitId` on Schedule (existing dispatch `404`).
- Branch scope per BR-02 for every figure, list, technician and event. With
  All, only in-scope branches contribute.
- Policy: `DispatchPolicies.Manage` on every endpoint; other roles `403`.
- Responses expose customer name and, only for `on_the_way` rows, the primary
  contact phone, as Schedule already does; never emails, addresses beyond the
  existing pages, notes or review comments.

## API contracts

Contract status: Final. Errors use ProblemDetails; `400` carries `errors`.
Responses are `no-store`. Times are ISO instants with offset plus `timezone`;
dates `YYYY-MM-DD`. Durations are minutes. A branch `timezone` of `null`
falls back to `organizationTimezone` (BR-03); the `timezone` of the dashboard
response is always the resolved, non-null zone. `nextAvailable` is `null` when
`currentStatus` is not `available` and no later free time exists today; the
UI shows "Now" when `currentStatus = available`.

`DayVisit` = `{ visitId, workOrderId, displayNumber, title, customerName,
customerPhone | null, branchId, status, statusGroup
(scheduled/active/completed), isDelayed, delayMinutes | null, isAtRisk,
scheduledStart, scheduledEnd, actualCompletedAt | null, projectedEnd | null,
technician: { id, fullName } | null, otherTechnicianCount }`.
`customerPhone` is non-null only when `status = on_the_way`.

`Attention` = `{ type (delayed/unassigned/over_estimate/over_capacity),
visitId | null, workOrderId | null, displayNumber | null, title | null,
visitStatus | null, branchId, at | null, minutes | null, technician: { id,
fullName, scheduledMinutes, availableMinutes, percent } | null }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/operations-dashboard/options` | — | `200 { branches: [{ id, name, timezone: string \| null }], organizationTimezone, technicians: [{ id, fullName, branchId }], categories: [{ id, name }] }` | `403` | Manage |
| GET | `/operations-dashboard` | Query: `branchId?`, `date`, `technicianId?`, `categoryId?` | `200 { timezone, date, reference, kpis: { jobs: { total, completed, active, scheduled }, unassigned, atRiskOrDelayed, utilization: { scheduledMinutes, availableMinutes, percent \| null }, onTimeArrival: { percent \| null, eligible, previousPercent \| null } }, visits: DayVisit[], attention: Attention[], technicians: [{ id, fullName, initials, colorHex \| null, skills: [string], extraSkillCount, currentStatus \| null (time_off/off/break/on_job/available), scheduledMinutes, availableMinutes, percent \| null, nextAvailable \| null }], durations: { visitCount, categories: [{ categoryId, name, visitCount, estimatedMinutes, actualMinutes }] }, activity: [{ type (on_the_way/started/completed/assigned/reassigned), at, visitId, workOrderId, displayNumber, title, technicianName \| null }] }` | `400` (`date`, `categoryId`, malformed ids) · `403` · `404` (branch, technician) | Manage |
| GET | `/operations-dashboard/performance` | Query: `branchId?`, `date`, `period` (`7`/`30`), `technicianId?`, `categoryId?` | `200 { from, to, jobsCompleted: { completed, total }, firstTimeFix: { fixed, total }, rating: { average \| null, count }, daily: [{ date, completed }] }` | `400` (`date`, `period`, `categoryId`) · `403` · `404` (branch, technician) | Manage |
| GET | `/dispatch/visits/{visitId}` | Unchanged | Unchanged (opened by BR-18) | Unchanged | Read (unchanged) |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/dashboard` | Header controls disabled until options load; per-card skeletons in the Design 15 layout | No branches: "No branches available." Per card per BR-10 … BR-15 | Options or dashboard failure: page alert "We couldn't load the dashboard." with **Try again** (controls kept). Performance failure: card-level "We couldn't load performance." with **Try again**. Refresh failure per BR-16. `404`/`400` filter → reset per BR-04/BR-05 | Other roles: existing forbidden state, no request. `401`: existing session-ended handling | Cards per BR-06 … BR-15 |
| Schedule with `visit` | Existing drawer loading | — | Existing `404` toast, parameter removed | Existing (Viewer read-only) | Drawer open on the visit |

- Mockups: `design/assets/15-design.png` (Approved). Data in it are examples.
  Deviations fixed by this spec: the branch selector sits in the page header,
  not the top bar; "Updated …" sits beside the page title with a refresh
  button; **Today ▾** is a date picker; **Filters** contains Technician and
  Service category; "ETA / variance" is **Timing** with derived values; the
  utilization caption shows hours instead of a target; "Check in" is
  **View job**; no traffic note; the bell and its count are unchanged.
- Responsive and accessibility: BR-17.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Role without Manage | `403`; forbidden state | None |
| Branch or technician foreign or out of scope | `404`; frontend resets to default / All | None |
| Category foreign or inactive | `400 errors.categoryId`; filter reset to All | None |
| Date out of range or malformed; period not 7/30 | `400`; frontend resets to today / 7 | None |
| Load failure | "We couldn't load the dashboard." with **Try again** | None |
| Automatic refresh failure | Data kept; "Couldn't refresh." with **Try again** | None |
| Schedule `visit` unknown, foreign or out of scope | Existing toast "This visit isn't available."; parameter removed | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An Owner, Operations Manager or Dispatcher signed in | They open `/overview` or select the first sidebar item | The browser is on `/dashboard` (the `/overview` entry replaced), the item reads "Dashboard" and is current; a Viewer, Accounting or Technician still sees "Overview" and the welcome page on `/overview` |
| AC-02 | A Viewer, Accounting or Technician | They call any `/operations-dashboard*` endpoint, or open `/dashboard` | The API returns `403`; the page shows the forbidden state and sends no data request |
| AC-03 | Organizations A and B with visits today; a Dispatcher of A scoped to branch A1 of A1/A2 | They load the dashboard with All, then with `branchId` = A2, a branch of B, or a technician of B | All includes only A1 visits, technicians and events, and the selector lists only A1 (no All); A2, B's branch and B's technician return `404` with no foreign data |
| AC-04 | An Owner with branches in different time zones and visits near local midnight | They load a date for one branch, then for All | Day membership, times and dates follow the branch time zone, and the organization time zone for All |
| AC-05 | A user changes branch, date, filters and period | They reload or go back | Branch, date, technician, category and period are restored from the URL; the filter button shows the active count; **Clear filters** resets both filters; an invalid date resets to today and a `404`/`400` filter resets to All |
| AC-06 | Today's visits in each status, including `unscheduled`, `cancelled` and draft/cancelled work orders | The dashboard loads | Today's jobs counts only BR-06 visits with the completed/active/scheduled split; Unassigned counts `scheduled` visits and **Assign now** opens Schedule for the branch and day |
| AC-07 | Visits before and after their arrival limit or scheduled end, and an unassigned visit starting in 30 minutes | The dashboard loads for today, a past day and a future day | Delayed and at-risk follow BR-07 with `ref` per BR-04 (nothing delayed on a future day); the KPI counts each visit once; **Review** focuses Needs attention |
| AC-08 | Technicians with availability, breaks, time off, assigned visits and scheduled assessments | The dashboard loads | Utilization equals Σ scheduled ÷ Σ available per BR-08 with the hours caption, "—" when nothing is available; the technician filter limits it to that technician and the category filter does not change it |
| AC-09 | Visits started on time, started late, and not started past their limit, today and on the same weekday last week | The dashboard loads | On-time arrival and the "vs last week" caption follow BR-09; with no eligible visits it shows "—" and "No arrivals yet", and the comparison is hidden when either week has none |
| AC-10 | Day visits in every status group, delayed and not | Today's operation renders for today and for another date | The title reads "Today's operation" for today and "Operation · <MMM d>" otherwise; rows are sorted by time (toggle reverses), 8 per page; status, technician ("Unassigned", "+n"), Timing texts and the action button follow BR-10; **Contact** is a `tel:` link only for an `on_the_way` visit with a phone |
| AC-11 | A delayed, an unassigned, an over-estimate visit and an over-capacity technician | Needs attention renders | Items appear in BR-11 order and copy with **Assign**/**Reassign**/**View job**/**Balance workload** targets; 4 are shown and **View all (n)** expands; a future day shows "Nothing needs attention." |
| AC-12 | Technicians on a job, on a break, on time off, outside availability and free | Technician availability renders for today and for another day | Current status, workload (including "No availability") and next available follow BR-12 for today; other days show "—" for current status and next available; rows sort by workload and **View team** opens `/team` |
| AC-13 | Visits completed in the last 7 days across 5 categories, some without an estimate | Estimated vs actual renders | Only visits with estimates and start times count; per-category averages, durations and variances follow BR-13 for the top 4 categories |
| AC-14 | Completed one-time work orders with one visit, with two visits, and with a `needs_correction` history; reviews in and out of the period | Service performance loads for 7 and then 30 days | Jobs completed, first-time fix rate, rating with count and the daily line follow BR-14 for each period; recurring work orders are excluded from the fix rate |
| AC-15 | Status history and assignment changes on today's visits and on other days | Live activity renders | Only the selected day's events appear, newest first, with BR-15 titles and subtitles (assigned vs reassigned, "A technician" fallback); 4 shown, **View all** opens `/jobs` |
| AC-16 | Any card with no data for the selection | The dashboard loads | The card shows its BR-10 … BR-15 empty message and actions; with no in-scope branch the page shows "No branches available." and requests no data |
| AC-17 | The dashboard is open in a visible tab | 60 seconds pass, the tab returns after being hidden, the user selects refresh, or a refresh fails | Data reloads per BR-16 and "Updated …" resets; a failed refresh keeps the data and shows "Couldn't refresh." with **Try again** |
| AC-18 | The dashboard API fails on first load | The page loads | "We couldn't load the dashboard." with **Try again** replaces the cards and the controls stay usable; a performance-only failure affects only its card |
| AC-19 | A dashboard link with `visit` to an assignable visit, a locked visit and an unknown visit | Schedule opens | The drawer opens on the visit (locked read-only); closing it removes `visit` from the URL; the unknown visit shows "This visit isn't available." and the parameter is removed; Schedule without `visit` is unchanged |
| AC-20 | The dashboard at 1280 px, 1024 px and 375 px | The page renders | Layout follows BR-17 and Design 15 with the documented deviations; no page horizontal scroll; status, delay, severity and trend show text and icon; charts expose a text alternative (user visual QA) |
| AC-21 | **Open dispatch board**, row **View**, menu **Open in schedule** and **View job** | The user selects them | They navigate to `/schedule` with branch/view/date (and `visit` where BR-10/BR-11 state) or `/jobs/<workOrderId>`, and the dashboard writes nothing |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Delay/at-risk/timing and on-time arrival calculations at boundaries (`ref` for past/today/future, window vs scheduled start, 0-minute delay); next-available computation across breaks, time off and visits (parameterized) | FR-04, FR-05, FR-07; AC-07, AC-09, AC-10, AC-12 |
| Backend integration | One grouped dashboard happy path (KPIs, visits, attention, technicians, durations, activity) on seeded data; time-zone day boundary for a branch and All; performance endpoint for 7/30 with fix rate, rating and daily series; filters and validation (`date`, `period`, `categoryId`) | AC-04, AC-06, AC-08, AC-11, AC-13, AC-14, AC-15 |
| Authorization/tenant isolation | Representative: Dispatcher scope (All limited, out-of-scope branch `404`), cross-organization branch and technician `404` without foreign rows, `403` for Viewer/Technician | AC-02, AC-03 |
| Frontend component/service | Dashboard page: URL state, defaults and resets, filter count, forbidden/no-branch/error states; refresh timer and visibility behavior; card rendering of derived texts and actions (table-driven); `/overview` redirect and nav label by role; Schedule `visit` opens and clears the drawer | AC-01, AC-05, AC-10, AC-11, AC-16, AC-17, AC-18, AC-19, AC-21 |
| Browser (user visual QA) | Design 15 fidelity with deviations, responsive breakpoints, text-plus-icon states, chart text alternative | AC-20 |

## Dependencies

Statuses verified 2026-10-13 from each spec's metadata.

| Spec | Status | Used for |
| ---- | ------ | -------- |
| `dispatch-calendar` | APPROVED | Policies, branch scope, load rules, `/dispatch/visits/{visitId}` and the drawer reused by BR-18 |
| `team-technician-management` | AUDITED | BR-04/BR-06 availability and load |
| `technician-skills-availability` | AUDITED | Skills, weekly availability, breaks, exceptions |
| `schedule-assessment` | AUDITED | BR-06 scheduled assessments in load |
| `technician-todays-jobs` | AUDITED | Visit status changes and history |
| `mobile-job-progress` | AUDITED | `actual_started_at`, `pause_seconds`, status history |
| `mobile-job-completion` | APPROVED | `actual_completed_at`, completion status |
| `customer-invoice-payments` | AUDITED | `invoice_reviews` |
| `authenticated-app-shell` | AUDITED | Sidebar and `/overview` (superseded only for the Manage-role label and redirect of BR-01) |

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | No Design 15 element targets Requests; the dashboard therefore links only to Jobs, Schedule and Team. |
| AS-02 | The work order `estimated_duration_minutes` is the per-visit estimate for BR-11 and BR-13. |
| AS-03 | "Arrival" means the start of work (`actual_started_at`); no separate arrival timestamp exists. |
| AS-04 | A day's visit volume per organization is small enough to return all day visits in one response; the table paginates client-side. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Route and nav | `/dashboard` + redirect; replace `/overview` | No | `/dashboard`, `/overview` redirects Manage roles (user, 2026-10-13) |
| OD-02 | Roles | Manage roles; plus Viewer | No | Owner, Operations Manager, Dispatcher (user, 2026-10-13) |
| OD-03 | Branch selection | Page selector with All; single branch; global shell | No | Page selector with All, in URL (user, 2026-10-13) |
| OD-04 | Date control | Today/Yesterday/Tomorrow; any date | No | Any date with picker (user, 2026-10-13) |
| OD-05 | Filters | Remove; technician and category | No | Technician and category (user, 2026-10-13) |
| OD-06 | Delay/risk definition | BR-07 proposal; other | No | BR-07 (user, 2026-10-13) |
| OD-07 | ETA column | Timing with derived values; keep ETA label | No | Timing (user, 2026-10-13) |
| OD-08 | On-time arrival | With weekly comparison; without; remove | No | With comparison (user, 2026-10-13) |
| OD-09 | Utilization target | No target; fixed 75–85 % | No | No target (user, 2026-10-13) |
| OD-10 | Duration comparison window | Last 7 days; selected day | No | Last 7 days (user, 2026-10-13) |
| OD-11 | First-time fix rate | Remove; BR-14 definition | No | BR-14 definition (user, 2026-10-13) |
| OD-12 | Reassign/assign target | Schedule day; Schedule with `visit` drawer | No | `visit` parameter on Schedule (user, 2026-10-13) |
| OD-13 | Activity source | Status history and assignments; audit logs | No | Status history and assignments (user, 2026-10-13) |
| OD-14 | Refresh | 60 s auto plus manual; manual only | No | 60 s auto plus manual (user, 2026-10-13) |
| OD-15 | Bell counter | Out of scope; include | No | Out of scope (user, 2026-10-13) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-02, AC-03 |
| FR-03 | AC-04, AC-05 |
| FR-04 | AC-06, AC-07, AC-08, AC-09 |
| FR-05 | AC-10, AC-21 |
| FR-06 | AC-11 |
| FR-07 | AC-12 |
| FR-08 | AC-13 |
| FR-09 | AC-14 |
| FR-10 | AC-15 |
| FR-11 | AC-17 |
| FR-12 | AC-16, AC-18, AC-20 |
| FR-13 | AC-19, AC-21 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-13 | — → DRAFT | Created |
| 2026-10-13 | DRAFT → DRAFT | Revised after validation: Today's operation title for other dates, nullable branch `timezone` with fallback, verified dependency statuses, removed `nextAvailableNow` |
| 2026-10-13 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-14 | APPROVED → AUDITED | User override: final-audit returned AUDIT FAIL and the user asked to mark the spec audited. Full validation passed (frontend 71 files / 630 tests, build, lint, format; backend format, build, unit, integration). Open findings, all frontend visual polish: (Important) the Today's operation status chip renders no icon, against BR-10/BR-17/AC-20 "text plus icon"; (Minor) technician current status uses a colored dot plus text instead of an icon (BR-12); (Minor) the over-estimate variance colour uses an unspecified 15 % threshold. User visual QA PASS (verbal, frontend fingerprint 4bc449621cc57fd948651c7338690b9cde2555db). |
