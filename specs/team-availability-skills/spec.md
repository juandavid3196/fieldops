# Team availability and skills tabs

| Field    | Value                       |
| -------- | --------------------------- |
| Feature  | `team-availability-skills`  |
| Type     | Full-stack                  |
| Status   | AUDITED                     |
| Created  | 2026-10-21                  |
| Updated  | 2026-10-21                  |
| Approved | 2026-10-21                  |

## Context and objective

The Team page's **Availability** and **Skills** tabs still open Coming soon.
This feature completes them inside the existing Team page: a Monday–Sunday
availability grid per technician with time off, and a technician × skill
matrix, each with editing drawers for users with Team Manage. Technicians
inherit their home branch business hours until a personal schedule is saved,
time off is stored as full-day exceptions, and skill assignments reuse the
existing catalog. Dispatch keeps its approved conflict behavior and consumes
the same data. Every read and write is confined to the caller's organization
and branch scope.

## Actors and permissions

Reuses `team-technician-management` BR-01 (Read/Mutate) and BR-02 (scope).

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Operations Manager (`operations_manager`) — Team Manage | View both tabs for all branches; edit recurring availability, use branch hours, create and remove time off, replace skills, open the skill catalog | — |
| Dispatcher (`dispatcher`) — Team View | View both tabs for profiles in assigned branches (all when `is_all_branches`) | Any mutation or drawer endpoint (`403`); profiles outside scope (`404`) |
| Technician (`technician`) | Unchanged Team `/team/me` view | Every endpoint in this spec (`403`); self-editing |
| Accounting (`accounting`), Viewer (`viewer`) | — | Every endpoint in this spec (`403`); existing Team forbidden state |

## Scope

- **Availability** and **Skills** tabs rendered inside `/team` (no new route
  or page; tab location unchanged).
- Availability grid: week navigation, **This week**, filters, cells, legend,
  weekly hours, pagination; **Time off** and **Edit availability** drawers.
- Skills matrix: filters, sticky Technician column, count, row actions,
  pagination; **Edit skills** drawer; **Skill catalog** reusing the existing
  Design 23 catalog dialog.
- Branch-hours inheritance (`uses_branch_hours`) applied consistently to Team,
  Design 23 and Dispatch availability calculations.
- Time off create/remove on `technician_exceptions`; Design 23 read-only
  display of multi-day time off.
- Audit rows, branch scope, tenant isolation, UI states, responsive behavior.
- Schema amendment BR-22 and migration generation, never application.

## Non-goals

- Changes to Dispatch conflict codes, justification, filters, calendar,
  cards or drawer (`dispatch-calendar` stays authoritative; Dispatch only reads
  the data this feature maintains).
- Skill levels, experience, certifications, expiry dates or primary-skill
  editing in the new drawers.
- Partial-day exceptions, breaks or `capacity_percent` editing in the new
  drawers (they stay in Design 23).
- Status filter or showing inactive/suspended profiles in these tabs.
- Technician self-editing; changes to the technician shell; mobile calendar
  layouts.
- Changing Team header **Manage skills**, the Team drawer **Edit skills** /
  **Edit availability** destinations (Design 23) or any other existing route.
- New skill tables; physical deletion of skills or exceptions.
- Applying migrations.

## User flow

1. A Team View user opens **Availability**; the system shows the current week
   for active in-scope technicians.
2. They move between weeks, return with **This week** and filter by name,
   branch and availability.
3. A Manage user selects **Edit availability**, chooses a technician, edits
   days (or selects **Use branch hours**) and saves.
4. A Manage user selects **Time off**, chooses a technician, sees their current
   and upcoming time off, adds a range with an optional note or removes one.
5. A Team View user opens **Skills** and filters the matrix by name, branch,
   skill and coverage.
6. A Manage user selects **Edit skills** (header or row menu), searches the
   active catalog, selects several skills and saves; or opens **Skill
   catalog**.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The Team page must render the Availability and Skills tabs in place, replacing their Coming soon links, per BR-01 and the approved designs. |
| FR-02 | `GET /team/availability` must return one page of active in-scope technicians with seven day cells and weekly hours for a Monday-start week, filtered per BR-03–BR-06. |
| FR-03 | The Availability tab must navigate weeks, return to the current week and render cells, legend, weekly hours and pagination per BR-02–BR-06. |
| FR-04 | Effective recurring availability must come from the technician's own weekly rows or, when `uses_branch_hours`, from the home branch business hours, everywhere availability is computed (BR-07). |
| FR-05 | The Edit availability drawer and `PUT /team/technicians/{id}/availability` must save one interval or unavailable per day with optimistic concurrency, no-op detection and break/capacity preservation per BR-08–BR-11. |
| FR-06 | **Use branch hours** must remove the personal override per BR-12. |
| FR-07 | The Time off drawer and endpoints must create full-day time off ranges and remove them per BR-13–BR-16, with time off taking precedence over recurring availability. |
| FR-08 | `GET /team/skills-matrix` and the Skills tab must render the matrix with filters, counts, sticky Technician column and pagination per BR-17–BR-19. |
| FR-09 | The Edit skills drawer and `PUT /team/technicians/{id}/skill-assignments` must replace a technician's active skill set atomically per BR-20–BR-21. |
| FR-10 | Every endpoint must resolve the organization from the session and apply Team Read/Manage and branch scope per BR-23. |
| FR-11 | Mutations must write the audit rows of BR-24. |
| FR-12 | Both tabs and drawers must render loading, empty, filtered-empty, error, conflict, submitting and unsaved-changes states and the responsive behavior of BR-25–BR-26. |
| FR-13 | The schema must be amended per BR-22 and a migration generated, never applied; Design 23 must follow the cross-spec effects of BR-27. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Tabs: **Team members (N)**, **Availability**, **Skills** switch content in place on `/team`; the selected tab is kept in the `tab` query parameter (`availability`, `skills`) so reload and back keep it. The Team header, metrics, alerts and coverage are unchanged. Header actions of each tab (**Time off**, **Edit availability**, **Skill catalog**, **Edit skills**, row **⋯** menu) are shown only to Manage users. | Frontend |
| BR-02 | Time basis: each technician's dates use their home branch (`technician_profiles.branch_id`) `timezone`, falling back to `organizations.timezone`. The week is the calendar dates Monday–Sunday given by `weekStart` (`yyyy-MM-dd`, must be a Monday, else `400 errors.weekStart`); each row evaluates those dates in its own time zone. **This week** = the week containing today in `organizations.timezone` and is the default. Range label "MMM d – d, yyyy" (or "MMM d – MMM d, yyyy" across months/years); column headers "EEE d"; times "h:mm a". | Both |
| BR-03 | Rows: only `status = active` profiles in scope, ordered by first then last name A–Z (case-insensitive, `id` tie-breaker). Page size 10, 1-based; footer "Showing x – y of n technicians"; a page beyond the last returns empty `items` with correct `totalCount`. Each row shows initials avatar (profile `color_hex` when set), full name and home branch name. | Both |
| BR-04 | Cell state for a local date, by precedence: **Time off** when an active `is_available = false` exception covers the entire local day; otherwise **Available** "h:mm a – h:mm a" with the day's recurring interval (BR-07; for legacy multi-window days the earliest-starting window, per Design 23 BR-25); otherwise **Unavailable**. Partial-day and extended exceptions do not change the cell. Legend: Available, Time off, Unavailable; cells carry text, not color alone. | Both |
| BR-05 | Weekly hours = sum over the seven dates of the displayed Available interval durations (breaks and `capacity_percent` are not applied; Time off and Unavailable count 0), shown as whole or one-decimal hours ("45 h", "37.5 h"). | Both |
| BR-06 | Filters (changing any returns to page 1; search debounced 300 ms): **Search** trimmed, ≤ 100 chars, case-insensitive contains on first, last and full name (> 100 → `400 errors.search`); **Branch** All or one in-scope active branch (out of scope/foreign → `400 errors.branchId`); **Availability** for the displayed week: All · Available this week (≥ 1 Available cell) · Has time off this week (≥ 1 Time off cell) · No availability this week (no Available cell). | Both |
| BR-07 | Recurring source: `uses_branch_hours = true` → each weekday's interval is the home branch `business_hours` entry (`business-hours` BR-05; absent day = unavailable; `{}` = unavailable all week) with `capacity_percent` 100 and no breaks; `false` → the profile's `technician_weekly_availability` rows and breaks. This replaces the weekly-window input of `team-technician-management` BR-04, `technician-skills-availability` BR-04 and therefore Dispatch availability checks; exception handling in those rules is unchanged. A branch business-hours change immediately affects inheriting technicians. | Backend |
| BR-08 | Edit availability drawer: technician select (active in-scope profiles, searchable by name); once chosen it loads `GET /team/technicians/{id}/availability`, showing "Using {branch} hours" or "Custom schedule", the read-only time zone label "Time zone: (UTC±hh:mm) {name}" for the current offset (as Design 23 BR-03) and seven rows Monday–Sunday each with Available/Unavailable and Start/End selectors (30-minute steps 12:00 AM–11:30 PM; a loaded off-step value is added as an extra option and preserved). An inheriting technician's rows are prefilled with the branch hours. **Save** is enabled only when dirty and valid. Switching technician or closing while dirty asks via the shared discard dialog. | Frontend |
| BR-09 | Availability validation (see Availability validation table). The request carries exactly seven distinct days. Any failure → `400` with field errors; nothing saved. | Both |
| BR-10 | Save `PUT .../availability` `{ updatedAt, days }`: `updatedAt` must equal `technician_profiles.updated_at` (else `409 stale` "This technician was updated by someone else. Reload to see the latest changes."). In one transaction holding the profile row lock: set `uses_branch_hours = false`; for each Unavailable day delete its rows and breaks; for each Available day keep one row with the submitted times, preserving its `capacity_percent` when the day already had a row (else 100) and keeping only breaks strictly inside the new interval (others deleted); extra legacy windows of the day are deleted; set `updated_at`. When the profile is already custom and the submitted days equal the stored effective days → `200` with no write, `updated_at` unchanged and no audit. An inheriting profile's save always creates the override. Success → `200` (GET shape). | Backend |
| BR-11 | Concurrency token: `technician_profiles.updated_at`, advanced by every effective availability save, **Use branch hours** and skills save (and by Design 23 saves). | Backend |
| BR-12 | **Use branch hours** (shown only for custom schedules; confirm "Use {branch} hours? This removes {name}'s custom schedule."): `POST .../availability/use-branch-hours` `{ updatedAt }` with the BR-10 stale check deletes the profile's weekly rows and breaks, sets `uses_branch_hours = true` and `updated_at`; already inheriting → `200` no-op without audit. | Both |
| BR-13 | Time off drawer: technician select as BR-08; list of the technician's time off (active `is_available = false` exceptions covering whole local days) whose end date ≥ today, ordered by start, showing "MMM d, yyyy – MMM d, yyyy" (single date when equal), the note and **Remove**; empty "No upcoming time off."; and an add form (see Time off validation). Notes are returned only by Manage endpoints. | Both |
| BR-14 | Create `POST .../time-off`: stores `technician_exceptions` with `is_available = false`, `starts_at` = local midnight of start date, `ends_at` = local midnight after end date (BR-02 time zone, DST-correct), `reason` = trimmed note or null, `status = active`. Any active exception of the technician intersecting the range → `409 time_off_overlap` "This technician already has time off or an exception on these dates."; time off mutations are serialized per technician (profile row lock) so concurrent overlapping creates yield exactly one success. | Backend |
| BR-15 | Remove `POST .../time-off/{timeOffId}/retire` `{ updatedAt }` (confirm "Remove this time off?"): allowed while the time off is active and its end date ≥ today. Start date ≥ today → `status = cancelled`. Start date < today → `ends_at` = local midnight of today (history kept through yesterday, today and later released), stays active. Ended or cancelled → `409 time_off_closed` "This time off has already ended or was removed."; `updatedAt` mismatch → `409 stale` "This time off was changed by someone else. Reload to see the latest version."; an id not belonging to the technician or not a full-day `is_available = false` exception → `404`. | Backend |
| BR-16 | Precedence: active time off overrides recurring availability in the grid (BR-04) and, through the existing exception rules, in Team, Design 23 and Dispatch calculations. | Backend |
| BR-17 | Skills matrix: columns Technician (sticky while scrolling horizontally; name sort A–Z default, header toggles Z–A), Home branch, one column per active organization skill ordered by name, **Skills** (count of the technician's active-skill assignments), Actions (Manage only, **⋯** → **Edit skills** for that row). Assigned cell: teal check with accessible label "{skill}: assigned"; unassigned: dash with "{skill}: not assigned". Archived-skill assignments are not shown or counted. Rows, ordering tie-breakers, page size and footer as BR-03. Note under filters: "Skills help Dispatch match technicians to the right jobs." | Both |
| BR-18 | Matrix filters (page 1 on change): Search and Branch as BR-06; **Skill** All or one active skill → technicians having it (inactive/foreign → `400 errors.skillId`); **Coverage** All · With skills (count ≥ 1) · Without skills (count = 0). | Both |
| BR-19 | Empty catalog: the matrix shows technician, branch and count columns with "No skills defined yet." and, for Manage, **Skill catalog**. **Skill catalog** opens the existing Design 23 catalog dialog (`technician-skills-availability` BR-17) unchanged; closing it refreshes the matrix columns. | Frontend |
| BR-20 | Edit skills drawer: technician select as BR-08 (preselected from a row action); loads `GET .../skill-assignments`; searchable (case-insensitive contains, debounced 300 ms) multi-select checklist of active catalog skills with assigned ones checked and a "{n} selected" count; archived skills never listed. **Save** enabled only when dirty; switch/close while dirty → discard dialog. | Frontend |
| BR-21 | Save `PUT .../skill-assignments` `{ updatedAt, skillIds }` with the BR-10 stale check, in one transaction: duplicate ids → `400 errors.skillIds` "Choose each skill once."; any id not an active skill of the organization (archived, foreign, unknown) → `400 errors.skillIds` "Choose active skills from the catalog."; nothing saved. Then remove active-skill assignments not submitted, insert new ones with `proficiency = 3`, `is_primary = false`, `years_experience` null; kept assignments keep proficiency, experience and primary; archived-skill assignments are preserved untouched. If the previous primary was an active skill that was removed and active assignments remain, the first remaining active skill by name becomes primary. Set `updated_at`. Same set → `200` no-op, `updated_at` unchanged, no audit. The `(technician_id, skill_id)` primary key backs uniqueness. | Backend |
| BR-22 | Schema amendment (approved by the user 2026-10-21): `ALTER TABLE technician_profiles ADD COLUMN uses_branch_hours boolean NOT NULL DEFAULT true;` backfill `UPDATE technician_profiles SET uses_branch_hours = false WHERE EXISTS (SELECT 1 FROM technician_weekly_availability w WHERE w.technician_id = technician_profiles.id);` `ALTER TABLE technician_exceptions ALTER COLUMN reason TYPE varchar(250);`. `docs/database/fieldops-schema.sql` is updated. Migration generated with `--generate-migration`, never applied. | Backend |
| BR-23 | Authorization: grid and matrix GETs require Team Read; drawer GETs and all mutations require Team Manage (BR-01 of `team-technician-management`). Scope per its BR-02: a profile of another organization, outside the caller's branch scope or not `active` → `404` "This technician profile isn't available.", identical to unknown; a time off id of another technician → `404`. | Backend |
| BR-24 | Audit (`actor_user_id` caller, `branch_id` profile branch, entity as listed): `technician.availability_updated` (entity profile; before/after only changed days `{ dayOfWeek, isAvailable, start, end }` and `usesBranchHours`; metadata `{ changedDays, source: "custom" \| "branch_hours" }`, written by save and **Use branch hours**); `technician.time_off_created` (entity exception; metadata `{ technicianId, startDate, endDate }`); `technician.time_off_removed` (entity exception; metadata `{ technicianId, mode: "cancelled" \| "shortened", startDate, endDate, previousEndDate? }`); `technician.skills_updated` (entity profile; metadata `{ addedSkillIds, removedSkillIds }`). Notes are never written to audit rows or logs. No-ops write nothing. Design 23 events are unchanged. | Backend |
| BR-25 | UI states per the UI table; after a `409 stale` the drawer keeps the form and shows the message with **Reload**, which reloads the technician's data; while saving, buttons show loading, controls are disabled and double submission is prevented. Success toasts: "Availability saved.", "Branch hours applied.", "Time off added.", "Time off removed.", "Skills saved."; the grid/matrix refresh. | Frontend |
| BR-26 | Responsive and accessibility: desktop shows the approved tables; below 1024 px the grid and matrix scroll horizontally inside their card with the Technician column sticky; filters and actions wrap; drawers are full screen below 768 px; no page-level horizontal scroll at 375 px. Drawers trap focus and return it to the opener; every control has a label. | Frontend |
| BR-27 | Cross-spec effects on Design 23 (`technician-skills-availability`): its weekly table shows the inherited branch hours when `uses_branch_hours` (BR-07) and its save sets `uses_branch_hours = false`; its exception list shows active multi-day time off (> 1 local day) whose end date ≥ today, including ranges that started before today, as read-only rows with kind `time_off`, the date range and no actions, ordered by start date with the other exceptions; its exception create/edit/activate also returns its BR-14 `409` when the date intersects any active time off range; single-day full-day exceptions keep Design 23 behavior and also appear as time off here. | Both |

### Availability validation

| Field | Rule | Key / message |
| ----- | ---- | ------------- |
| `days` | Exactly seven entries, each `dayOfWeek` `monday`…`sunday` once | `days` "Enter a valid value." |
| `isAvailable` | Required boolean | `days.{day}.isAvailable` "This field is required." |
| `start` / `end` (available) | Required, `HH:mm` 24-hour `00:00`–`23:59` | `days.{day}.start|end` "This field is required." / "Enter a valid value." |
| `end` | Strictly after `start` on the same day (no midnight crossing) | `days.{day}.end` "End time must be after start time." |
| `start` / `end` (unavailable) | Must be null/absent | `days.{day}` "Enter a valid value." |

### Time off validation

| Field | Required | Rule | Message |
| ----- | -------- | ---- | ------- |
| Technician | Yes | Active, in scope (BR-23) | `404` state |
| Start date | Yes | `yyyy-MM-dd`, ≥ today in the technician's time zone | "Choose today or a future date." |
| End date | Yes | `yyyy-MM-dd`, ≥ start date | "End date can't be before start date." |
| Note | No | Trimmed, ≤ 250; empty → null | "Use 250 characters or fewer." |

Server errors are `400` `errors.<field>` (`startDate`, `endDate`, `note`).

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Inheriting branch hours | Custom schedule | Save availability (here or Design 23) | Owner, Operations Manager | `updatedAt` current, valid days |
| Custom schedule | Inheriting branch hours | Use branch hours | Owner, Operations Manager | `updatedAt` current |
| — | Time off active | Create | Owner, Operations Manager | BR-14 no overlap, start ≥ today |
| Time off active (not started) | Cancelled | Remove | Owner, Operations Manager | `updatedAt`, end ≥ today |
| Time off active (started) | Active, ends today 00:00 | Remove | Owner, Operations Manager | `updatedAt`, end ≥ today |

## Data and persistence impact

- Tables used: `technician_profiles` (`id`, `organization_id`, `branch_id`,
  names, `status`, `color_hex`, `uses_branch_hours`, `updated_at`), `branches`
  (`name`, `timezone`, `business_hours`, `is_active`), `organizations`
  (`timezone`), `technician_weekly_availability`, `technician_breaks`,
  `technician_exceptions`, `technician_skills`, `skills`,
  `organization_users`, `organization_user_branches`, `roles`, `audit_logs`.
- Writes: `technician_profiles` (`uses_branch_hours`, `updated_at`),
  `technician_weekly_availability`, `technician_breaks`,
  `technician_exceptions`, `technician_skills`, `audit_logs`.
- Child tables without `organization_id` are reached only through a profile or
  skill already verified in the caller's organization.
- Schema amendments required: BR-22 (approved 2026-10-21; migration generation
  authorized, application forbidden).

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session's
  active membership.
- Client-provided organization identifiers: never accepted. Technician, branch,
  skill and time off ids are verified against the caller's organization and
  branch scope.
- Profile of another organization or outside scope: `404`, no change. Time off
  of another technician: `404`. Foreign or archived skill in a save: `400
  errors.skillIds`; foreign branch/skill filter: `400`.
- Permissions per action and scope: BR-23.

## API contracts

Contract status: Final. All endpoints require an authenticated session and the
existing CSRF protection on unsafe methods; `401` when unauthenticated.
Validation errors use the existing `400` problem shape with `errors.<field>`.
Times `HH:mm` local; dates `yyyy-MM-dd` local; `updatedAt` is an opaque
string. Stale/closed/overlap `409`s carry `code` and the BR message.

`AvailabilityDay` = `{ dayOfWeek: "monday"…"sunday", isAvailable, start | null, end | null }`.
`TimeOff` = `{ id, startDate, endDate, note | null, updatedAt }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/team/availability` | Query `weekStart`, `search?`, `branchId?`, `availability? (all\|available\|time_off\|none)`, `page?` | `200 { weekStart, items: [{ technicianId, firstName, lastName, colorHex \| null, branch: { id, name }, timezone, usesBranchHours, days: [{ date, state: "available"\|"time_off"\|"unavailable", start \| null, end \| null }], weeklyMinutes }], totalCount, page, pageSize }` | `400`, `403` | Read |
| GET | `/team/technicians/{id}/availability` | — | `200 { technicianId, firstName, lastName, branch: { id, name }, timezone, timezoneLabel, usesBranchHours, updatedAt, days: AvailabilityDay[7], timeOff: TimeOff[] }` (`timeOff` per BR-13) | `403`, `404` | Manage |
| PUT | `/team/technicians/{id}/availability` | `{ updatedAt, days: AvailabilityDay[7] }` | `200` (GET shape) | `400`, `403`, `404`, `409 stale` | Manage |
| POST | `/team/technicians/{id}/availability/use-branch-hours` | `{ updatedAt }` | `200` (GET shape) | `403`, `404`, `409 stale` | Manage |
| POST | `/team/technicians/{id}/time-off` | `{ startDate, endDate, note? }` | `201 TimeOff` | `400`, `403`, `404`, `409 time_off_overlap` | Manage |
| POST | `/team/technicians/{id}/time-off/{timeOffId}/retire` | `{ updatedAt }` | `200 { timeOff: TimeOff \| null }` (`null` when cancelled) | `403`, `404`, `409 stale\|time_off_closed` | Manage |
| GET | `/team/skills-matrix` | Query `search?`, `branchId?`, `skillId?`, `coverage? (all\|with\|without)`, `sort? (asc\|desc)`, `page?` | `200 { skills: [{ id, name }], items: [{ technicianId, firstName, lastName, colorHex \| null, branch: { id, name }, skillIds, skillCount }], totalCount, page, pageSize }` | `400`, `403` | Read |
| GET | `/team/technicians/{id}/skill-assignments` | — | `200 { technicianId, firstName, lastName, updatedAt, skillIds }` (active skills only) | `403`, `404` | Manage |
| PUT | `/team/technicians/{id}/skill-assignments` | `{ updatedAt, skillIds }` | `200` (GET shape) | `400`, `403`, `404`, `409 stale` | Manage |

Reused unchanged: `GET /team/technicians` (drawer technician select, active
profiles, search) and the `/team/skills` catalog endpoints. Design 23
`GET .../skills-availability` adds `kind: "time_off"` and `endDate` to
exception items per BR-27.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Availability tab | Skeleton rows | "No active technicians yet." | "We couldn't load availability. Try again." with **Retry** | View: no header actions; Accounting/Viewer: existing Team forbidden state | Grid, legend, weekly hours, paginator |
| Availability filtered-empty | — | "No technicians match these filters." with **Clear filters** | — | — | — |
| Skills tab | Skeleton rows | "No active technicians yet."; empty catalog per BR-19 | "We couldn't load skills. Try again." with **Retry** | View: no header actions or Actions column | Matrix, paginator |
| Skills filtered-empty | — | "No technicians match these filters." with **Clear filters** | — | — | — |
| Edit availability drawer | Skeleton after technician chosen | "Choose a technician to edit availability." | Field errors; load error with **Retry**; `409` message with **Reload** | Manage only | "Availability saved." / "Branch hours applied." |
| Time off drawer | Skeleton list | "No upcoming time off." | Field errors; `409` inline message (overlap, closed, stale with **Reload**) | Manage only | "Time off added." / "Time off removed." |
| Edit skills drawer | Skeleton checklist | "No skills defined yet."; search "No skills match." | Load error with **Retry**; field errors; `409` with **Reload** | Manage only | "Skills saved." |

- Mockups: `design/assets/Disponibilidad semanal del equipo técnico.png`
  (Availability) and `design/assets/Matriz de habilidades del equipo.png`
  (Skills) — Approved. Drawers follow existing FieldOps drawer patterns.
- Responsive and accessibility: BR-26.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Stale profile `updatedAt` | `409 stale` "This technician was updated by someone else. Reload to see the latest changes." | None |
| Invalid days | `400` field messages | None |
| Overlapping time off | `409 time_off_overlap` "This technician already has time off or an exception on these dates." | None |
| Remove ended/cancelled time off | `409 time_off_closed` "This time off has already ended or was removed." | None |
| Stale time off | `409 stale` "This time off was changed by someone else. Reload to see the latest version." | None |
| Duplicate / inactive / foreign skill | `400 errors.skillIds` | None |
| Invalid `weekStart`, filter values | `400` field key | None |
| Profile out of scope / foreign / inactive | `404` "This technician profile isn't available." | None |
| Role not permitted | `403`; forbidden state or toast | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner on `/team` | They select **Availability**, **Skills**, reload, and go back | Each tab renders in place with the approved layout; `tab` query keeps the tab; no Coming soon navigation; Team header and metrics unchanged |
| AC-02 | Active, inactive and suspended profiles in branches with different time zones (one without, using the organization's) | The Availability tab loads this week | Only active profiles appear, ordered and paged per BR-03; the range label and "EEE d" headers follow BR-02; previous/next move by one week and **This week** returns; a non-Monday `weekStart` → `400` |
| AC-03 | Profiles with custom days, inherited branch hours, a full-day time off spanning the week boundary, a single-day full-day exception, a partial exception and a branch with `{}` hours (parameterized, including a DST week) | The grid loads | Each cell is Time off / interval / Unavailable per BR-04 and BR-07; partial exceptions don't change cells; weekly hours follow BR-05 ("45 h" for 8:00–17:00 × 5) |
| AC-04 | The grid (table-driven: search, branch, each Availability option, out-of-scope branch, 101-char search) | Filters are applied | Rows match BR-06 for the displayed week; page resets to 1; filtered-empty with **Clear filters**; invalid values → `400` |
| AC-05 | Inheriting and custom technicians with visits | Team list, Design 23 page and Dispatch evaluation compute availability | Inheriting technicians use branch hours (capacity 100, no breaks) and custom ones their rows; a branch-hours change is reflected for inheriting technicians only; Dispatch conflict codes and justification behave as `dispatch-calendar` |
| AC-06 | A Manage user opens **Edit availability** for an inheriting technician | They pick the technician, change Tuesday and save | Days were prefilled with branch hours and labeled "Using {branch} hours"; `200`; `uses_branch_hours = false`; seven-day effective schedule persisted; `updated_at` advanced; `technician.availability_updated` with changed days and `source: "custom"`; grid refreshes |
| AC-07 | A custom technician with breaks and `capacity_percent` 50 on Monday, a legacy second window on Wednesday | They shorten Monday so one break no longer fits, keep Tuesday, turn Thursday Unavailable and save | Monday keeps 50 % and only the fitting break; Wednesday keeps one window; Thursday's row and breaks are deleted; a newly enabled day uses 100 |
| AC-08 | A custom technician | The same days are saved again | `200`; no row written, `updated_at` unchanged, no audit |
| AC-09 | Invalid availability (table-driven per Availability validation: missing time, malformed, end ≤ start, `23:30`→`00:30`, times on an unavailable day, six days, duplicate day) | It is submitted | `400` with the keyed messages; nothing saved |
| AC-10 | Two Manage users loaded the same technician; or a skills save happened after loading | The second saves availability or uses branch hours | `409 stale`; nothing saved; the drawer keeps the form and **Reload** refetches |
| AC-11 | A custom technician, and an inheriting one | **Use branch hours** is confirmed | Custom: rows and breaks deleted, `uses_branch_hours = true`, audit with `source: "branch_hours"`; inheriting: `200` no-op without audit; the button is hidden for inheriting technicians |
| AC-12 | A Manage user in the Time off drawer | They add a multi-day range with a note, and a single day without note, in a DST-change week | `201`; `starts_at`/`ends_at` are local midnights in the technician's time zone; note stored in `reason` (null when empty); list shows the ranges; grid cells show Time off; `technician.time_off_created` audit without the note |
| AC-13 | Invalid or conflicting time off (table-driven: past start, end < start, 251-char note, overlap with time off, overlap with a partial exception, two concurrent overlapping creates) | They are submitted | `400` with field messages, or `409 time_off_overlap`; concurrently exactly one succeeds |
| AC-14 | Time off not started, started yesterday, already ended, cancelled, and with a stale `updatedAt` | **Remove** is confirmed | Not started → cancelled; started → `ends_at` = today 00:00 local, active, past days kept; ended/cancelled → `409 time_off_closed`; stale → `409 stale`; `technician.time_off_removed` with `mode` and dates, no note |
| AC-15 | Active skills A–E, an archived skill, technicians with assignments | The Skills tab loads, scrolls horizontally and sorts | Columns per BR-17 by name; check/dash with accessible labels; counts exclude archived; Technician column stays fixed while scrolling; sort toggles A–Z/Z–A; pagination per BR-03 |
| AC-16 | The matrix (table-driven: search, branch, skill, each Coverage option, inactive/foreign `skillId`) | Filters are applied | Rows match BR-18; page resets; filtered-empty; invalid skill → `400` |
| AC-17 | A Manage user opens **Edit skills** from a row menu | They search, check two skills, uncheck the primary and save | Archived skills not listed; `200`; new assignments with proficiency 3 and not primary; removed primary promotes the first remaining active skill by name; archived assignments untouched; `technician.skills_updated` lists added/removed ids; matrix refreshes |
| AC-18 | Edit skills (table-driven: duplicate id, archived skill, foreign skill, failure on the last id, unchanged set, stale `updatedAt`) | It is saved | `400 errors.skillIds` with nothing changed (transactional); unchanged → `200` no-op, no audit, `updated_at` kept; stale → `409` with **Reload** |
| AC-19 | An empty catalog, and a Manage user | The Skills tab loads and **Skill catalog** is used to add a skill | "No skills defined yet." with **Skill catalog**; the existing catalog dialog opens; after closing the new column appears |
| AC-20 | A Dispatcher assigned to branch A, and an Operations Manager | Each loads both tabs, filters by branch B, and calls each drawer GET and mutation | Dispatcher sees only branch A rows, no actions, `400` for branch B filter, `403` on drawer GETs and mutations; the Operations Manager sees both branches and edits |
| AC-21 | A profile, time off or skill of organization B; an out-of-scope profile; an inactive profile | A Manage user of organization A reads or mutates them | `404` (or `400 errors.skillIds` for the skill in a save); time off of another technician `404`; no data change |
| AC-22 | Each role | It calls every endpoint (parameterized) | Read endpoints allow Owner, Operations Manager, Dispatcher; Manage endpoints Owner, Operations Manager; Technician, Accounting, Viewer `403` |
| AC-23 | Both tabs and drawers | Requests are pending, fail, return empty; a drawer is dirty; save is clicked twice | Skeletons, error with **Retry**, empty texts; closing or switching technician while dirty opens the discard dialog; one request is sent with controls disabled |
| AC-24 | Design 23 page for an inheriting technician with a multi-day time off and a single-day full-day exception | It loads, saves and creates an exception inside the time off range | Weekly table shows branch hours; save sets `uses_branch_hours = false`; multi-day time off shows read-only as `time_off` with range; single-day keeps Design 23 actions; the overlapping exception → `409` |
| AC-25 | The generated migration | It is reviewed against BR-22 | It adds `uses_branch_hours` with the backfill (profiles with rows → false) and widens `reason` to 250; the schema doc matches; the migration is not applied |
| AC-26 | Viewports 1280, 1024, 768 and 375 px (manual QA) | Both tabs and drawers are used | Layout per BR-26: tables scroll inside the card with sticky Technician column, no page-level horizontal scroll, full-screen drawers below 768 px |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------- |
| Backend unit | Week cell/hours calculator (inheritance, precedence, legacy windows, DST, non-UTC zone, cross-week time off); availability save diff (capacity/break preservation, no-op); time off retire mode | AC-03, AC-07, AC-08, AC-14 |
| Backend integration | Grid and matrix filters/paging; availability save/use-branch-hours with stale and rollback; time off create/overlap incl. concurrency and retire; skills replace transactional with primary promotion and no-op; audit rows without notes; inheritance consumed by Team, Design 23 and Dispatch evaluation | AC-02–AC-14, AC-16–AC-18, AC-24 |
| Authorization/tenant isolation | Role matrix (parameterized); Dispatcher branch scope; one cross-organization denial per path (profile, time off, skill) | AC-20–AC-22 |
| Frontend component/service | Tab switching with query; grid/matrix rendering and filters; drawer dirty/discard, conflict Reload, double submit; skill search/selection; role-based actions | AC-01, AC-06, AC-10, AC-15, AC-17, AC-19, AC-23 |
| Persistence review | `database-reviewer` on mappings and migration | AC-25 |
| Manual visual QA (user) | Fidelity to the two mockups, drawers, responsive | AC-01, AC-03, AC-15, AC-26 |

Backend integration may exceed 8 methods: the feature introduces a transversal
availability source (BR-07) that must be proven at Team, Design 23 and
Dispatch boundaries, plus two concurrency-sensitive write paths (BR-10,
BR-14) required by the approved scope. 26 active ACs: two consumers' regression
(AC-05, AC-24) and the migration (AC-25) are distinct high-risk behaviors.

## Dependencies

- `team-technician-management` (AUDITED): Team page, policies, scope, list
  endpoint. Supersedes its BR-24/AC-24 only for the **Availability** and
  **Skills** tab destinations, and its BR-04 weekly-window input (BR-07).
- `technician-skills-availability` (AUDITED): data model, catalog dialog and
  endpoints. Affected by BR-07 and BR-27 (inheritance, time off display,
  range overlap); must be re-validated and re-approved after this spec.
- `dispatch-calendar` (AUDITED): unchanged behavior; consumes BR-07 and time
  off through its existing rules; must be re-validated for BR-07.
- `business-hours` (AUDITED): `branches.business_hours` format (BR-05) and
  validation precedent.
- Shared discard dialog and Team pagination — implemented.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The drawer technician select lists active in-scope profiles via the existing `GET /team/technicians` search. |
| AS-02 | The Team drawer **Edit skills** / **Edit availability** and header **Manage skills** keep their current destinations. |
| AS-03 | Technician names in both tabs are plain text (no navigation). |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Design paths | Existing images / rename | No | Use the two existing images (2026-10-21) |
| OD-02 | Dispatch warnings | Change Dispatch / keep | No | Keep `dispatch-calendar` unchanged; this spec only maintains data (2026-10-21) |
| OD-03 | Coexistence with Design 23 | Preserve / weighted hours | No | Preserve capacity and fitting breaks; new skills Intermediate; weekly hours without breaks (2026-10-21) |
| OD-04 | Branch-hours inheritance | Global column / view-only | No | `uses_branch_hours`, applied to Team, Design 23 and Dispatch (2026-10-21) |
| OD-05 | Time off storage and removal | Reuse exceptions; reason 250; retire semantics | No | Reuse `technician_exceptions`, `reason` → 250; started time off shortened to yesterday, not started cancelled (2026-10-21) |
| OD-06 | Inactive technicians | Active only / status filter | No | Active only, no extra filter (2026-10-21) |
| OD-07 | Filter options | Proposed / custom | No | Proposed options (2026-10-21) |
| OD-08 | Concurrency and buttons | Proposed / custom | No | `updatedAt` on skills too; catalog dialog and row menu for Manage (2026-10-21) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-03, AC-04 |
| FR-03 | AC-02, AC-03, AC-04 |
| FR-04 | AC-03, AC-05, AC-06 |
| FR-05 | AC-06, AC-07, AC-08, AC-09, AC-10 |
| FR-06 | AC-11 |
| FR-07 | AC-12, AC-13, AC-14 |
| FR-08 | AC-15, AC-16, AC-19 |
| FR-09 | AC-17, AC-18 |
| FR-10 | AC-20, AC-21, AC-22 |
| FR-11 | AC-06, AC-11, AC-12, AC-14, AC-17 |
| FR-12 | AC-10, AC-23, AC-26 |
| FR-13 | AC-24, AC-25 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-21 | — → DRAFT     | Created with user decisions OD-01–OD-08 |
| 2026-10-21 | DRAFT → DRAFT | Revised after validation: BR-27 lists multi-day time off by end date ≥ today (including started ranges); BR-08 defines the time zone label format |
| 2026-10-21 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-21 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-21 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
