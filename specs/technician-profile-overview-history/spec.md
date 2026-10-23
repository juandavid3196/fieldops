# Technician profile — Overview and Job history

| Field    | Value                                    |
| -------- | ---------------------------------------- |
| Feature  | `technician-profile-overview-history`    |
| Type     | Full-stack                               |
| Status   | APPROVED                                 |
| Created  | 2026-10-22                               |
| Updated  | 2026-10-22                               |
| Approved | 2026-10-22                               |

## Context and objective

The technician profile header already shows the tabs Overview, Schedule,
Skills & availability and Job history. Only Skills & availability has content:
Overview and Job history open Coming soon pages, and Team has no way to open a
profile. This feature adds `/team/:technicianId/overview` and
`/team/:technicianId/job-history`. Overview gives a read-only administrative
and operational summary built only from the derivations Team already approved.
Job history lists every visit the technician took part in, including visits
later reassigned to someone else. It is built from the existing
assignment-history rows and time entries, with period metrics. Team gains entry
points to the profile and keeps its filters when the user returns with Back.
Authorization, branch scope and tenant isolation are the existing Team rules,
and no role gains access.

## Actors and permissions

Policy `TeamView` (`team-technician-management` BR-01) and branch scope
(`team-technician-management` BR-02) are reused unchanged.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Operations Manager (`operations_manager`) | Open Overview and Job history for any profile of the organization; follow job links | Edit profile data from Overview (non-goal) |
| Dispatcher (`dispatcher`) | Same, for profiles whose home branch is in scope. Job history and its metrics include only visits whose work order branch is in scope | Profiles outside scope (`404`) |
| Technician (`technician`), Accounting (`accounting`), Viewer (`viewer`), unknown roles | — | Every endpoint in this spec (`403`); the pages show the existing forbidden state |

## Scope

- Team page (`/team`) entry points: clicking the technician name opens
  Overview, and the row menu gains **View profile**.
- Team list state (search, filters, sort, period, page) moves into the URL
  query.
- Routes `/team/:technicianId` (redirect), `/team/:technicianId/overview` and
  `/team/:technicianId/job-history`, all with the shared profile header and
  tabs.
- The links on the Skills & availability page that point to Coming soon now
  open the new routes.
- Overview: summary row, Profile details with read-only Account access, Today,
  Operational skills, Standard availability and Upcoming time off.
- Additive extension of `GET /team/technicians/{id}`.
- New `GET /team/technicians/{id}/job-history` with period metrics, search,
  status filter, sort and pagination.
- Loading, empty, error, forbidden and not-available states; responsive
  layout, accessibility, light and dark mode, English and Spanish.

## Non-goals

- Editing personal, account, skill or availability data from Overview.
- A per-technician schedule view or a `/team/:technicianId/schedule` route.
  Schedule keeps opening `/schedule`.
- New visit, work order or technician statuses, or a second status, workload
  or capacity formula.
- Exporting, deleting or correcting historical records.
- Changes to the rules of Schedule, Time, Team derivations or Skills &
  availability, other than the Skills & availability navigation targets in
  BR-03.
- Visits the technician is still assigned to that have not finished. They
  belong to Today and Schedule.
- Assessments, which are not jobs.
- Schema changes or migrations.

## User flow

1. An Owner on `/team` (filtered, page 2) clicks a technician's name, or picks
   **View profile** in the row menu. The system opens
   `/team/{id}/overview`.
2. The Overview shows the header, summary row, Profile details, Today, skills,
   standard availability and upcoming time off.
3. They pick **Job history**. The system shows the metrics for the last 90
   days and the first 10 participations, newest first.
4. They search, change Status, Period, Sort or rows per page. The table and
   footer update. The metrics change only with Period.
5. They open a job number. The system navigates to `/jobs/{workOrderId}`, which
   checks access again.
6. They press browser Back twice. Team is restored with the same search,
   filters and page.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The Team page must open a technician's Overview from the technician name and from a new **View profile** row-menu item per BR-01, without changing the other existing actions. |
| FR-02 | The Team page must keep its list state in the URL query so that Back and reload restore it, per BR-02. |
| FR-03 | The frontend must serve the profile routes, header, breadcrumb and tabs, and update the Skills & availability navigation, per BR-03 and BR-04. |
| FR-04 | `GET /team/technicians/{id}` must return the BR-05 additive fields. The Overview summary row and Today card must show Team's existing status, job, workload and next-available derivations per BR-06 and BR-07. |
| FR-05 | The Overview must show Profile details, Account access, Operational skills, Standard availability and Upcoming time off, read-only, per BR-08 to BR-11. |
| FR-06 | `GET /team/technicians/{id}/job-history` must return one row per visit participation with outcome, participation date and time logged, per BR-12 to BR-15. |
| FR-07 | The endpoint must return the period metrics of BR-16, independent of search and status filter. |
| FR-08 | The Job history tab must provide search, status, period and sort filters, plus pagination and URL state, per BR-17 to BR-19. |
| FR-09 | Job numbers must link to the job detail only when the caller can read it, per BR-20. |
| FR-10 | Every endpoint must resolve the organization from the session and apply `TeamView` and the branch scope, per BR-21. |
| FR-11 | The pages must render the loading, empty, error, forbidden, not-available and inactive-profile states of BR-22 and the UI table. |
| FR-12 | The pages must meet the responsive, accessibility, theme and language constraints of BR-23. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Team entry points. <ul><li>In the Team members table, the technician name (link-styled) navigates to `/team/{id}/overview` instead of opening the drawer. The table has no avatar column, and none is added.</li><li>The row menu gains **View profile**, placed right after **View**, for every role that sees the menu (Owner, Operations Manager, Dispatcher). It navigates to `/team/{id}/overview`.</li><li>**View** still opens the detail drawer.</li><li>**Edit**, **Link account** / **Unlink account** and **Deactivate** / **Activate**, the drawer actions (**Edit skills**, **Edit availability**, **View schedule**, **Edit profile**) and the Team alerts are unchanged.</li><li>The existing unsaved-changes guard of Team still applies when the user leaves it.</li></ul> | Frontend |
| BR-02 | Team URL state. <ul><li>Query parameters (absent means the default): `q` (search text, trimmed), `branch` (branch id), `skill` (skill id), `status` (a `StatusFilter` code; default `all_active`), `account` (`all` default, `linked`, `not_linked`), `sort` (`name_asc` default, `name_desc`), `period` (`today` default, `week`), `page` (≥ 1, default 1), plus the existing `tab`.</li><li>Every change to a filter, search, sort, period or page writes the URL, replacing the current entry while the user types and pushing a new entry for other changes.</li><li>A filter, search, period or sort change resets `page` to 1, as today.</li><li>Opening `/team` with parameters applies them before the first list request.</li><li>An invalid value, or a branch or skill id not in the Team options, is replaced by its default (`replaceUrl`).</li><li>Browser Back from a profile and reload both restore the list exactly.</li><li>Team list and metric semantics (`team-technician-management` BR-08 to BR-11) are unchanged.</li></ul> | Frontend |
| BR-03 | Routes and navigation. <ul><li>`/team/:technicianId` redirects to `/team/:technicianId/overview` (`replaceUrl`).</li><li>`/team/:technicianId/overview` and `/team/:technicianId/job-history` render inside the shell with sidebar **Team** active and the Team role rules.</li><li>`/team/:technicianId/skills-availability` keeps its path and behavior.</li><li>Tabs: **Overview** → `/team/{id}/overview`; **Schedule** → `/schedule`, unchanged; **Skills & availability** → `/team/{id}/skills-availability`; **Job history** → `/team/{id}/job-history`.</li><li>On the Skills & availability page, the breadcrumb name, **View profile** and the **Overview** tab now go to `/team/{id}/overview`, and the **Job history** tab to `/team/{id}/job-history`. This supersedes `technician-skills-availability` BR-20 only for those targets. Its **Schedule** tab and **Open dispatch calendar** are unchanged.</li><li>The `technician-profile` and `technician-job-history` Coming soon slugs are no longer linked from Team pages.</li></ul> | Frontend |
| BR-04 | Shared header (Overview and Job history): <ul><li>Breadcrumb `Team` (→ `/team`) / `<full name>` (→ Overview) / `<current tab label>`.</li><li>Avatar with initials (first letter of first and last name).</li><li>Full name, and the profile status tag: Active (green), Inactive (grey) or Suspended (grey), with text.</li><li>"Technician" and "Home branch: <name>".</li><li>The four tabs, with the current one marked active (`aria-current="page"`).</li></ul> The header has no edit or save actions. The header data comes from the BR-05 response, and Job history reuses the same request for its header. | Frontend |
| BR-05 | `GET /team/technicians/{id}` additive fields (existing fields, semantics and drawer behavior unchanged): <ul><li>`usesBranchHours`: the profile flag of `team-availability-skills`.</li><li>`today.currentJob` gains `visitStatus` = the active visit status (`on_the_way`, `in_progress` or `paused`).</li><li>`upcomingTimeOff` per BR-11, or `null`.</li></ul> `availability` keeps returning the effective weekly availability, meaning the branch business hours when `usesBranchHours`. | Backend |
| BR-06 | Summary row, using the existing response for `period=today` with no new calculation: <ul><li>**Current job**: `today.currentJob.label`, or "None".</li><li>**Jobs today**: `today.jobs`.</li><li>**Workload**: Team BR-06 text ("N%", "No availability" or "—").</li><li>**Next available**: Team BR-07 text ("Now", "h:mm a", "EEE, MMM d h:mm a" or "—").</li></ul> Each value equals what the Team list shows for the same technician at the same moment. Inactive and Suspended profiles show "—" for Jobs today, Workload and Next available, and "None" for Current job. | Both |
| BR-07 | Today card: <ul><li>The Team BR-05 status tag (Available, On job, Break, Time off, Off, Inactive or Suspended) with the BR-05 tones and text.</li><li>When the status is On job: the current job label and the visit status as text ("On the way", "In progress" or "Paused").</li><li>**Workload**: label, value and a bar with the Team BR-06 fill and over-100 % treatment.</li><li>**View schedule** → `/schedule`.</li></ul> | Frontend |
| BR-08 | Profile details (read-only): <ul><li>Email, Mobile phone, Profile ID (`employee_code`) and Home branch, with "—" when empty.</li><li>**Account access**, when linked: Linked account (user email) with a "Read-only" tag, Role (role name), and the note "Account permissions are managed in Settings.".</li><li>**Account access**, when unlinked: "No linked user account".</li></ul> No link, unlink, edit or Settings action is shown, and profile notes are never shown. | Frontend |
| BR-09 | Operational skills: <ul><li>Active skills only (inactive or archived catalog skills are excluded, as today), each as "<name> · <level label>" using the `team-technician-management` BR-25 labels, plus a "Primary" badge on the primary skill. Primary first, then name A–Z.</li><li>No skills: "No skills assigned yet."</li><li>The whole block is one link to `/team/{id}/skills-availability` with the accessible name "Operational skills, open Skills & availability". Every `TeamView` role can read that page.</li></ul> | Both |
| BR-10 | Standard availability: <ul><li>The effective weekly availability, Monday first. Consecutive days with identical windows are grouped per `team-technician-management` BR-13 ("Monday – Thursday" "7:00 AM – 4:00 PM"), and days without a window show "Off".</li><li>When `usesBranchHours`, the note "Uses branch business hours".</li><li>No window on any day: "No availability set".</li><li>A link to Skills & availability.</li></ul> | Frontend |
| BR-11 | Upcoming time off: <ul><li>The technician's exception with `status = 'active'`, `is_available = false` and `ends_at` > now, with the earliest `starts_at`. A current absence is included; cancelled and extended-availability exceptions are excluded.</li><li>Shape `{ date, allDay, start?, end?, reason }`, with dates and times local to the home-branch time zone (Team BR-03), `allDay` per `technician-skills-availability` BR-13 and `reason` as stored.</li><li>Shown as "EEE, MMM d, yyyy", then "All day" or "h:mm a – h:mm a", then the reason.</li><li>When there is none: "No upcoming time off".</li><li>A **Manage availability** link to Skills & availability is shown in both cases.</li></ul> | Both |
| BR-12 | Participation. The technician took part in a visit when there is at least one `visit_assignments` row (active or with `unassigned_at` set) or one `visit_time_entries` row with that `technician_id`. The visit must belong to a work order of the session organization whose `branch_id` is in the caller's scope; out-of-scope visits are excluded from rows and metrics. Each participating visit gives exactly one row, however many assignment or time rows exist. `assessments` are not included. | Backend |
| BR-13 | Outcome, first match wins: <ol><li>**Cancelled**: visit status `cancelled`.</li><li>**Completed**: visit status in the completed group (`completed`, `needs_correction`, `approved`; `operations-dashboard` BR-06), and the technician has an active assignment (`unassigned_at IS NULL`).</li><li>**Reassigned**: the technician has no active assignment on the visit.</li><li>Otherwise (still assigned, not finished): the visit is **excluded** from rows and metrics.</li></ol> Tags: Completed green, Reassigned amber, Cancelled grey, always with text. | Backend / Frontend |
| BR-14 | Participation date and period. <ul><li>Participation date = local date (home-branch time zone, Team BR-03) of `scheduled_start`, else `actual_started_at`, else the technician's earliest `visit_time_entries.started_at` on the visit, else the technician's latest `visit_assignments.assigned_at`.</li><li>Periods, as local days ending today: `30d` "Last 30 days" (today − 29 … today), `90d` "Last 90 days" (default; today − 89 … today), `12m` "Last 12 months" (today − 12 months + 1 day … today), `all` "All time" (no lower bound).</li><li>Rows with a participation date after today are excluded in every period.</li></ul> DST-aware. | Backend |
| BR-15 | Time logged per row = Σ of the technician's own `visit_time_entries` on that visit with `entry_type` `work` or `travel` (no `pause`), whatever their dates. An open entry counts `now − started_at`, as `technician-time-summary` BR-03 does. Entries of other technicians never count. Shown as "<h>h <m>m", "<m>m" under one hour and "0m" for zero (`technician-time-summary` BR-07 format). | Backend / Frontend |
| BR-16 | Period metrics, over all rows of the selected period (BR-12 to BR-14) and independent of `search`, `status`, sort and page: <ul><li>**Completed jobs** = rows with outcome Completed.</li><li>**On-time** = on time ÷ (on time + late) among Completed rows with `actual_started_at`. Arrival limit and on-time result per `operations-dashboard` BR-09 and `reports-workspace` BR-11. Integer percent; "—" when the denominator is 0.</li><li>**Hours logged** = Σ row time logged (BR-15), in "<h>h <m>m".</li><li>**Avg. duration** = mean of `actual_completed_at − actual_started_at − pause_seconds` (`operations-dashboard` BR-13) over Completed rows with both timestamps, rounded to whole minutes, shown as "<h>h <m>m"; "—" when there are none.</li></ul> | Backend / Frontend |
| BR-17 | Filters and sort. <ul><li>`search`: trimmed, ≤ 100 characters. Case-insensitive contains over the job display number (the existing Jobs display number, e.g. "JOB-0834"), the work order title, the property name and the property city. Empty means all.</li><li>`status`: `all` "All statuses" (default), `completed`, `reassigned`, `cancelled`.</li><li>`period` per BR-14.</li><li>`sort`: `newest` "Newest first" (default) or `oldest` "Oldest first", by participation date, then `scheduled_start`, then visit id in the same direction, so paging is stable.</li><li>`page` ≥ 1 and `pageSize` ∈ {10, 25, 50}; defaults 1 and 10.</li><li>Invalid values → `400` `errors.<field>`.</li><li>A page past the last returns empty `items` with the real `total`.</li><li>Filtering, sorting and paging run in the database with a bounded number of queries, whatever the page size.</li></ul> | Backend |
| BR-18 | Job history UI. <ul><li>Summary cards in the BR-16 order.</li><li>A card titled "Job history" with the subtitle "Jobs and visits this technician worked on."</li><li>Filter row: search (placeholder "Search jobs", accessible label "Search jobs", debounced as the Team search is), Status, Period, Sort.</li><li>An info note: "History includes jobs this technician worked on, even if they were reassigned later."</li><li>Columns:<ul><li>**Job**: the display number as a link when `canOpen`, otherwise plain text, with the title below and "Visit <k>" when the work order has more than one visit.</li><li>**Property**: "<property name> · <city>".</li><li>**Visit date**: "MMM d, yyyy".</li><li>**Time logged**.</li><li>**Outcome** tag.</li><li>A trailing chevron, shown only when `canOpen`, with the same destination as the job link.</li></ul></li><li>Footer "Showing x–y of n jobs" ("Showing 0–0 of 0 jobs" when empty), a paginator, and rows per page 10, 25 or 50.</li></ul> | Frontend |
| BR-19 | Job history URL state: `q`, `status`, `period`, `sort`, `page`, `pageSize` (absent = default). Search, status, period, sort and page-size changes reset `page` to 1. Reload and Back restore the view. Invalid values are replaced by defaults (`replaceUrl`) before the request. A page past the last requests the last page (`replaceUrl`). | Frontend |
| BR-20 | Job links. Each row returns `workOrderId` and `canOpen`. `canOpen` is true only when the caller satisfies `WorkOrderRead` and the work order is readable under the caller's branch scope; otherwise it is false. A link opens `/jobs/{workOrderId}`, and that page keeps its own authorization and branch-scope checks (`404` handling unchanged). | Both |
| BR-21 | Authorization and scope. <ul><li>Both endpoints require `TeamView`, and the organization comes from the session membership; no organization id is accepted.</li><li>The technician must belong to the organization and have a home branch in the caller's scope (Team BR-02). Otherwise the response is `404` "This technician profile isn't available.", identical for unknown, foreign and out-of-scope ids. The frontend shows the existing not-available state with a link to `/team`.</li><li>Roles without `TeamView` get `403` before any read. The frontend shows "You don't have access to Team." and sends no request.</li><li>Inactive and Suspended profiles can be read like active ones.</li></ul> | Backend / Frontend |
| BR-22 | States: see the UI behavior table. <ul><li>A failure of the profile request shows "We couldn't load this technician." with **Retry**.</li><li>A failure of the job-history request keeps the header and shows "We couldn't load job history." with **Retry**.</li><li>Zero rows with a search or status filter: "No jobs match these filters." with **Clear filters**, which resets search and status and keeps the period.</li><li>Zero rows without a search or status filter: "No jobs in this period.", or "This technician has no job history yet." when the period is All time.</li></ul> | Frontend |
| BR-23 | Responsive and accessibility. <ul><li>Summary rows: four columns on desktop and 13" laptop, two on tablet, one on mobile.</li><li>Overview: two columns on desktop and laptop (details, skills and availability on the left; Today and Upcoming time off on the right), stacked to one column below the existing tablet breakpoint.</li><li>Job history: the filters wrap; below 768 px the table becomes one card per row with labelled fields.</li><li>Tabs scroll horizontally without truncating labels.</li><li>No page horizontal overflow at 375 px. Touch targets ≥ 44 px on mobile.</li><li>Every control is reachable and operable by keyboard with a visible focus. Links, the menu item, selects and the paginator have accessible names. Status, outcome and workload carry text, never color alone.</li><li>All copy exists in English and Spanish through the existing i18n scopes, and dates follow the active language.</li><li>Light and dark mode use the existing tokens.</li></ul> | Frontend |

## States and transitions

None. This feature is read-only and changes no state.

## Data and persistence impact

- Tables read, from `docs/database/fieldops-schema.sql`:
  - `technician_profiles` (`id`, `organization_id`, `branch_id`, `organization_user_id`, names, `email`, `phone`, `employee_code`, `status`, `uses_branch_hours`)
  - `branches` (`name`, `timezone`, business hours), `organizations` (`timezone`, `work_order_prefix`)
  - `organization_users`, `organization_user_branches`, `roles`, `users` (account email)
  - `technician_skills`, `skills`
  - `technician_weekly_availability`, `technician_breaks`, `technician_exceptions`
  - `work_orders` (`id`, `organization_id`, `branch_id`, `work_order_number`, `title`, `property_id`)
  - `properties` (`name`, `city`)
  - `visits` (`id`, `work_order_id`, `visit_number`, `status`, `scheduled_start`, `actual_started_at`, `actual_completed_at`, `pause_seconds`, `arrival_window_end`)
  - `visit_assignments` (`visit_id`, `technician_id`, `assigned_at`, `unassigned_at`)
  - `visit_time_entries` (`visit_id`, `technician_id`, `started_at`, `ended_at`, `entry_type`)
- Discovery confirmed that reassignment can be represented without a schema
  change. Dispatch closes assignments by setting `unassigned_at` and never
  deletes `visit_assignments` rows, and time entries keep their
  `technician_id`.
- `visit_assignments` and `visit_time_entries` have no `organization_id`. They
  are reached only through a technician verified in the caller's organization
  and a visit whose work order belongs to that organization.
- Writes: none. Schema amendments required: None.

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session's
  active membership.
- Client-provided organization identifiers: never accepted. `technicianId` is
  verified against the organization and branch scope (BR-21), and work order
  links against `WorkOrderRead` and scope (BR-20).
- A technician of another organization or out of scope: `404`, with no data.
  Visits whose work order is in another organization or an out-of-scope branch
  are never returned or counted.
- Permission: `TeamView` for both endpoints. The policy is unchanged and no
  role gains access.

## API contracts

Contract status: Final. Both endpoints require an authenticated session, return
`401` when unauthenticated and are `Cache-Control: no-store`. Instants are UTC
ISO-8601, dates are local `yyyy-MM-dd` and times are local `HH:mm`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/team/technicians/{id}` (existing; additive) | Existing `period` query | `200` existing `TechnicianDetail` plus `usesBranchHours: boolean`, `today.currentJob.visitStatus: "on_the_way"\|"in_progress"\|"paused"` (when `currentJob` is non-null), `upcomingTimeOff: { date, allDay, start?, end?, reason } \| null` | `400`, `403`, `404` | `TeamView` |
| GET | `/team/technicians/{id}/job-history` | Query `search?`, `status?` (`all`\|`completed`\|`reassigned`\|`cancelled`), `period?` (`30d`\|`90d`\|`12m`\|`all`), `sort?` (`newest`\|`oldest`), `page?`, `pageSize?` (10\|25\|50) | `200 { timezone, period: { from?, to }, summary: { completedJobs, onTimePercent?, loggedMinutes, avgDurationMinutes? }, items: [{ visitId, workOrderId, displayNumber, title, visitNumber, visitCount, property: { name, city }, participationDate, loggedMinutes, outcome: "completed"\|"reassigned"\|"cancelled", canOpen }], total, page, pageSize }` (`period.from` absent for `all`; nullable metrics absent or null mean "—") | `400` `errors.<field>`, `403`, `404` | `TeamView` |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Team (changes only) | Existing | Existing | Existing | Existing | URL restores state (BR-02); name and **View profile** open Overview (BR-01) |
| Overview | Skeleton header, summary row and cards | "No skills assigned yet."; "No availability set"; "No upcoming time off"; "No linked user account"; Current job "None" | "We couldn't load this technician." with **Retry** | Forbidden state without requests; `404` not-available state with link to Team | BR-04 to BR-11; Inactive/Suspended per BR-06 |
| Job history | Header skeleton (if not loaded), summary and table skeletons; filters disabled until first response | BR-22 texts; summary zeros and "—" | "We couldn't load job history." with **Retry**; header kept | As Overview | BR-16 to BR-19 |

- Mockups (Approved):
  `design/assets/Perfil de técnico FieldOps en modo oscuro.png` (Overview) and
  `design/assets/Historial de trabajos del técnico.png` (Job history). Light
  mode, Spanish, mobile cards and the empty, error and loading states follow
  existing FieldOps patterns and tokens.
- Responsive and accessibility constraints: BR-23.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Technician unknown, foreign or out of scope | `404`; "This technician profile isn't available." with link to Team | None |
| Role without `TeamView` | `403`; "You don't have access to Team." | None |
| Invalid job-history query | `400` `errors.<field>`. The UI never sends one: it replaces invalid URL values (BR-19) | None |
| Job no longer readable when opened | The Jobs detail shows its own not-found state | None |
| Network or server failure | BR-22 error with **Retry** | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Owner, Operations Manager and Dispatcher on `/team` (parameterized) | They click a technician name, open the row menu, and pick **View profile** and then **View** | The name and **View profile** navigate to `/team/{id}/overview`. **View** opens the drawer. The other menu items and drawer actions are unchanged for each role |
| AC-02 | Team with search, branch, skill, status, account, sort, week period and page 2 applied | They open a profile and press Back, then reload; separately, they open a URL with invalid values | The list, request and URL are restored identically after Back and reload. Invalid values are replaced by defaults (`replaceUrl`) before the first list request. Filter changes reset page 1 |
| AC-03 | A permitted user | They open `/team/{id}`, then use the breadcrumb, each tab and the Skills & availability page links | The redirect goes to Overview. Tabs, breadcrumb and Skills & availability links follow BR-03 (Schedule → `/schedule`). The active tab has `aria-current="page"`. The header follows BR-04 |
| AC-04 | Technicians at a fixed clock who are Available, On job (`on_the_way`, `in_progress`, `paused`), on Break, on Time off and Off (parameterized) | Overview and the Team list load | Summary row and Today status equal the Team list values for the same technician (BR-06, BR-07). On job shows the current job and visit status text. The existing drawer response fields are unchanged |
| AC-05 | A linked technician with all fields, and an unlinked one with empty email, phone and profile ID | Overview loads | Profile details and Account access follow BR-08 ("—", "Read-only" tag, role, settings note, "No linked user account"). No edit, link, unlink or notes appear |
| AC-06 | The Today card | They activate **View schedule** | The status tag, workload bar (including over 100 % and "No availability") and visit status follow BR-07, and navigation goes to `/schedule` |
| AC-07 | A technician with active skills (one primary), a skill whose catalog entry is inactive, and a technician with no skills | Overview loads and the skills block is activated | Only active skills are shown, with level labels and a primary-first order. "No skills assigned yet." appears when there are none. The block navigates to Skills & availability |
| AC-08 | A technician with own hours (one day off), one inheriting branch hours, and one with no availability | Overview loads | Grouped days, "Off", the "Uses branch business hours" note and "No availability set" follow BR-10 |
| AC-09 | Exceptions: a past one, a cancelled one, an extended-availability one, a current partial absence and a later all-day absence | Overview loads, before and after the current absence ends | `upcomingTimeOff` is the earliest qualifying absence per BR-11 (the current one first, then the all-day one). With none qualifying, "No upcoming time off" appears. The link opens Skills & availability |
| AC-10 | An Inactive and a Suspended technician with past visits | Overview and Job history load | The header shows the status tag. Summary values follow BR-06 with "—". Job history and metrics are available read-only |
| AC-11 | Visits where the technician completed the work, was unassigned and replaced, was assigned twice to the same visit, logged time without an active assignment, was on a cancelled visit, and is still assigned to a scheduled and an in-progress visit | Job history loads with `all` | One row per visit with BR-13 outcomes: Completed, Reassigned (kept after reassignment, with its own logged time), one row for the double assignment, Reassigned, Cancelled. Still-assigned unfinished visits are absent. Work orders with several visits show "Visit k" |
| AC-12 | Work, travel and pause entries of the technician and of another technician on the same visit, plus one open entry | Job history loads | Row time logged = the technician's work + travel only, with the open entry counted to now, formatted per BR-15 |
| AC-13 | Visits whose participation date comes from each fallback, in a non-UTC home-branch time zone, at the boundaries of each period and after today | Each period is requested | Membership follows BR-14: inclusive local bounds, the 90-day default, All time without a lower bound, and future-dated rows excluded |
| AC-14 | A period with completed (on-time, late, no start), reassigned and cancelled rows | The period changes, then search and status change | Completed jobs, On-time, Hours logged and Avg. duration follow BR-16 and change with the period only. "—" appears when there is no denominator |
| AC-15 | More than 50 rows | Search (number, title, property, city, any case), status, sort and page size 10/25/50 are combined (parameterized), including a page past the last; invalid parameters are sent | Results, order with stable tie-breakers, `total` and the footer "Showing x–y of n jobs" follow BR-17 and BR-18. Invalid parameters → `400` `errors.<field>`. The query count does not grow with page size |
| AC-16 | A Dispatcher scoped to branch A viewing a branch-A technician who also worked on branch-B work orders, and an Owner | Each loads Job history and follows a job link | The Dispatcher sees neither rows nor metrics from branch B, and the Owner sees both. `canOpen` follows BR-20, rows with `canOpen = false` render plain text, and the link opens `/jobs/{workOrderId}` |
| AC-17 | A technician of another organization, and a branch-B technician for the branch-A Dispatcher | Both endpoints are called and the pages are opened | `404` for both endpoints with no data. The pages show the not-available state with a link to Team |
| AC-18 | Technician, Accounting and Viewer members (parameterized) | They call both endpoints and open both routes | `403` before any read. The forbidden state is shown with no further requests. The Team policy is unchanged |
| AC-19 | Job history with non-default filters and page 3 | They reload, navigate away and back, change a filter, and open a URL with invalid values or a page past the last | The URL state follows BR-19: restored, page reset to 1 on filter change, invalid values replaced and last page requested (`replaceUrl`) |
| AC-20 | Pending, failing and empty responses (no history in All time, no rows in the period, no rows for a search or status) | Overview and Job history render | Skeletons, errors with **Retry** (header kept on Job history), and the BR-22 empty texts with **Clear filters** keeping the period |
| AC-21 | Desktop, 13" laptop, tablet and 375 px mobile | Both tabs render in English and Spanish | Summary rows show 4, 2 and 1 columns. Overview stacks. The table becomes cards on mobile. Tabs scroll horizontally without truncation. The page has no horizontal overflow |
| AC-22 | Keyboard-only use in light and dark mode | The user moves through Team entry points, tabs, cards, filters, links and the paginator | Every control is reachable with visible focus and an accessible name. Status, outcome and workload carry text. Copy is translated in both languages and colors use the theme tokens |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Participation classification: outcome precedence, exclusion of unfinished assigned visits, participation date fallback and period bounds in a non-UTC zone, table-driven. Only if this logic lives in a pure application helper; otherwise covered by integration. ≤ 2 methods | AC-11, AC-13 |
| Backend integration | Job history: reassignment kept, double assignment collapsed, time-entry-only participation, time logged (work + travel, open entry), metrics independent of search and status, filters, sort, paging and `400`s. Detail endpoint additive fields (`usesBranchHours`, `visitStatus`, `upcomingTimeOff` selection) | AC-04, AC-09, AC-11–AC-15 |
| Authorization/tenant isolation | One cross-organization `404` on the job-history path; Dispatcher out-of-scope technician `404`; branch-B work orders excluded for Dispatcher; `403` for a role without `TeamView` (parameterized) | AC-16–AC-18 |
| Frontend component/service | Team: name and **View profile** navigation, and URL state restore with invalid-value replacement. Profile routes: redirect, tabs and Skills & availability links. Overview: BR-06 to BR-11 rendering with empty and inactive variants. Job history: URL state, page reset, `canOpen` rendering, empty, filtered-empty and error states | AC-01–AC-03, AC-05, AC-07–AC-10, AC-16, AC-19, AC-20 |
| Manual visual QA (user) | Fidelity to both mockups, light/dark, en/es, responsive breakpoints, mobile cards, keyboard and focus | AC-03, AC-06, AC-21, AC-22 |

Backend integration may reach the upper default (8). Reassignment preservation
and branch-scoped exclusion are the feature's distinct data-integrity and
authorization risks.

## Dependencies

- `team-technician-management` (BR-01 to BR-07, BR-12, BR-13, BR-25; Team page,
  drawer and detail endpoint) — audited.
- `team-availability-skills` (`uses_branch_hours`, effective weekly
  availability) — implemented.
- `technician-skills-availability` (page, exceptions, BR-13 kind; BR-20 targets
  partly superseded by BR-03) — audited.
- `operations-dashboard` BR-06, BR-09, BR-13 and `reports-workspace` BR-11
  (status groups, on-time, duration) — audited.
- `technician-time-summary` BR-03 and BR-07 (open entries, duration format) —
  audited.
- Dispatch reassignment that sets `unassigned_at` (`dispatch-calendar`) —
  implemented.
- Jobs detail `/jobs/:id` with `WorkOrderRead` — implemented.
- `app-i18n-en-es` and `app-dark-mode` infrastructure — implemented.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The avatar uses initials only. No technician photo source is added. |
| AS-02 | The breadcrumb **Team** link opens `/team` with defaults. Restoring Team state relies on browser Back (BR-02). |
| AS-03 | The job display number reuses the existing Jobs display format and is not redefined here. |
| AS-04 | The Job history header reuses the BR-05 detail request, so no separate header endpoint is added. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Schedule tab route | Keep `/schedule` / new redirect route | No | Keep `/schedule`; no `/team/:id/schedule` (user, 2026-10-22) |
| OD-02 | Today status vocabulary | Team BR-05 plus visit status / new 7-state scale | No | Team BR-05; On job adds the active visit status (user, 2026-10-22) |
| OD-03 | Unfinished assigned visits in history | Exclude / extra outcome | No | Exclude (user, 2026-10-22) |
| OD-04 | Hours logged | Work + travel / all entry types | No | Work + travel, no pause (user, 2026-10-22) |
| OD-05 | Team state on Back | URL query / in-memory | No | URL query (user, 2026-10-22) |
| OD-06 | Out-of-scope work orders | Exclude rows and metrics / show without link | No | Exclude (user, 2026-10-22) |
| OD-07 | Row grain | One row per visit / per work order | No | One row per visit with "Visit k" (user, 2026-10-22) |
| OD-08 | Dates, fallbacks, sort, URL state, Skills & availability links, Team entry points | As proposed | No | Confirmed as BR-01–BR-03, BR-14, BR-17, BR-19 (user, 2026-10-22) |
| OD-09 | Schema change | Migration / none | No | None; reassignment already representable (discovery, 2026-10-22) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02 |
| FR-03 | AC-03 |
| FR-04 | AC-04, AC-06, AC-10 |
| FR-05 | AC-05, AC-07, AC-08, AC-09 |
| FR-06 | AC-11, AC-12, AC-13 |
| FR-07 | AC-14 |
| FR-08 | AC-15, AC-19 |
| FR-09 | AC-16 |
| FR-10 | AC-16, AC-17, AC-18 |
| FR-11 | AC-10, AC-17, AC-18, AC-20 |
| FR-12 | AC-21, AC-22 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-22 | — → DRAFT     | Created with user decisions OD-01–OD-08 |
| 2026-10-22 | DRAFT → APPROVED | Approved by user via /spec approve |
