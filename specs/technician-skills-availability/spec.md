# Design 23 — Technician skills and availability

| Field    | Value                               |
| -------- | ----------------------------------- |
| Feature  | `technician-skills-availability`    |
| Type     | Full-stack                          |
| Status   | AUDITED                             |
| Created  | 2026-10-01                          |
| Updated  | 2026-10-01                          |
| Approved | 2026-10-01                          |

## Context and objective

Dispatchers assign visits manually and need trustworthy data on what each
technician can do and when they can work. This feature delivers the Design 23
page at `/team/:technicianId/skills-availability`: standard weekly
availability with breaks and derived capacity, operational skills with levels
and one primary skill, an administrable skill catalog, dated availability
exceptions, calculated upcoming availability and real next-7-days capacity from
assigned visits. Owners and Operations Managers edit; Dispatchers read within
branch scope; Technicians read only their own profile. Every read and write is
confined to the caller's organization and branch scope, saves are atomic and
concurrent edits are detected.

## Actors and permissions

Derived from the permission catalog module `team` and the
`team-technician-management` spec (BR-01, BR-02).

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View the page for any profile; save weekly availability and skills; create, edit, cancel and activate exceptions; manage the skill catalog | — |
| Operations Manager (`operations_manager`) | Same as Owner | — |
| Dispatcher (`dispatcher`) | View the page read-only for profiles in assigned branches (all when `is_all_branches`) | Every mutation and the catalog endpoints (`403`); profiles outside scope (`404`) |
| Technician (`technician`) | View the page read-only for the profile linked to their own membership | Other profiles (`404`); every mutation and the catalog endpoints (`403`) |
| Accounting (`accounting`), Viewer (`viewer`) | — | Every endpoint in this spec (`403`) |

## Scope

- Route `/team/:technicianId/skills-availability` inside the shell, sidebar
  **Team** active.
- Header: breadcrumb, avatar initials, name, profile status tag, "Technician",
  "Home branch: {name}", **View profile**, **Save changes**; tabs Overview,
  Schedule, **Skills & availability** (active), Job history.
- Standard weekly availability: per-day toggle, start, end, one optional break,
  read-only capacity, weekly total, **Copy Monday to weekdays**, **Reset**,
  read-only time zone label.
- Operational skills: search-and-add, level, primary, remove; **Manage skill
  list** catalog dialog.
- Availability exceptions: list, add, edit, cancel, activate.
- Upcoming availability, Next 7 days capacity, Related schedule and the static
  "How assignment matching works" card.
- Atomic save of weekly availability, breaks and skills with optimistic
  concurrency; per-exception concurrency.
- Team drawer **Edit skills** and **Edit availability** navigate to this page.
- Branch scoping, tenant isolation, audit rows.
- Loading, empty, error, forbidden, not-found, read-only, submitting, conflict
  and responsive states.
- Schema amendments (BR-24) and migration generation, never application.

## Non-goals

- Automatic assignment, matching scores, route optimization, payroll, GPS, time
  tracking and ZIP/service-area coverage. Assignment stays manual.
- Creating, moving or reassigning visits; blocking availability edits that
  conflict with assigned visits.
- Editing `capacity_percent`, `years_experience`, profile fields, status or
  account link.
- Multiple windows or multiple breaks per day; overnight windows; multi-day
  exceptions; several active exceptions on one date.
- Physical deletion of skills or exceptions.
- Overview, Schedule and Job history content, the dispatch calendar and the
  technician profile page (Coming soon).
- Team page **Manage skills** and its Availability/Skills tabs (stay Coming
  soon); new entry points for Dispatcher or Technician.
- Top-bar "All branches" selector or other shell changes.
- Applying migrations.

## User flow

1. An Owner opens a technician's Team drawer and selects **Edit skills** or
   **Edit availability**; the system opens this page with saved data.
2. They change days, times and breaks, use **Copy Monday to weekdays** or
   **Reset**, add/remove skills, change levels and the primary skill, then
   select **Save changes**; the system saves everything atomically and
   refreshes derived sections.
3. They select **Add exception**, complete the dialog and save; the exception
   list, upcoming availability and capacity refresh. Row actions edit, cancel
   or activate an exception immediately.
4. They select **Manage skill list**, add, rename, deactivate or activate
   catalog skills; the skill picker refreshes.
5. A Dispatcher or the linked Technician opens the URL and sees the page
   read-only.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/team/:technicianId/skills-availability` inside the shell with the BR-20 header, tabs and navigation, and the Team drawer **Edit skills** / **Edit availability** must navigate to it. |
| FR-02 | `GET /team/technicians/{id}/skills-availability` must return the profile header, version, time zone, weekly availability, skills, exceptions and the derived upcoming availability, capacity and today summary for permitted callers per BR-01/BR-02. |
| FR-03 | The page must edit weekly availability per BR-05–BR-08, including **Copy Monday to weekdays** and **Reset**, with capacity and weekly total per BR-07. |
| FR-04 | The page must add, remove and change the level of skills and keep exactly one primary skill per BR-09–BR-10. |
| FR-05 | `PUT /team/technicians/{id}/skills-availability` must save weekly availability, breaks and skills in one transaction with optimistic concurrency per BR-11. |
| FR-06 | Exception endpoints must create, edit, cancel and activate exceptions per BR-12–BR-15 with per-exception concurrency, and the page must list them per BR-16. |
| FR-07 | Skill catalog endpoints and the **Manage skill list** dialog must list, create, rename, deactivate and activate skills per BR-17. |
| FR-08 | The system must derive upcoming availability, next-7-days capacity and the today summary per BR-04, BR-18 and BR-19. |
| FR-09 | Every endpoint must resolve the organization from the session and apply role, branch and own-profile scope per BR-01–BR-02. |
| FR-10 | The page must render loading, empty, error, forbidden, not-found, read-only, submitting, conflict, unsaved-changes and responsive states per BR-21–BR-22 and the UI table. |
| FR-11 | Mutations must write audit rows per BR-23. |
| FR-12 | The schema must be amended per BR-24 and a migration generated, never applied. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Role policy: `GET /team/technicians/{id}/skills-availability` allows `owner`, `operations_manager`, `dispatcher` and `technician`. `PUT .../skills-availability`, all exception mutations and all `/team/skills` endpoints allow `owner`, `operations_manager`. Any other role/endpoint combination → `403`. Frontend visibility is UX only. | Backend |
| BR-02 | Scope: Owner/Operations Manager and `is_all_branches` members see all branches; Dispatcher sees profiles whose `branch_id` is in `organization_user_branches`; Technician sees only the profile whose `organization_user_id` is their membership. Another organization's profile, out-of-scope profile or another technician's profile → `404` "This technician profile isn't available." An exception id not belonging to the route's profile → `404`. A skill id of another organization → `400` `errors.skills` / `404` on catalog routes. | Backend |
| BR-03 | Time basis: the technician's home branch `timezone`, falling back to `organizations.timezone`; shown read-only as "Time zone: (UTC±hh:mm) {name}" for the current offset. "Today" is local midnight to midnight. `day_of_week` 0 = Sunday … 6 = Saturday; the table lists Monday first. Times use "h:mm a", dates "MMM d, yyyy" in the exception list and "EEE, MMM d" in upcoming availability. | Both |
| BR-04 | Effective availability for a date: the weekly window minus its break; an active `is_available = false` exception removes its interval; an active `is_available = true` exception adds the portions of its interval not already in the window. Available minutes weight weekly-window minutes by `capacity_percent / 100`; exception-added minutes count at 100 %. Cancelled exceptions are ignored. Consistent with `team-technician-management` BR-04. | Backend |
| BR-05 | Weekly table: seven rows Monday–Sunday. Available **On** requires Start and End; **Off** stores no row for that day and shows "—" and "0h". Times are 30-minute steps from 12:00 AM to 11:30 PM; Start < End. At most one window per day. | Both |
| BR-06 | Break: "No break" or one range of 30 or 60 minutes starting on a 30-minute step, strictly inside the day's window (break start > window start, break end < window end). Changing Start/End so the break no longer fits clears it to "No break" client-side; the server rejects an out-of-window break. | Both |
| BR-07 | Capacity (read-only) = (window − break) hours × `capacity_percent / 100`, shown with up to one decimal ("8h", "7.5h"); footer "{N} available hours per week" = sum of the seven days. A day that stays On keeps its stored `capacity_percent`; a newly enabled day uses 100. | Both |
| BR-08 | **Copy Monday to weekdays** copies Monday's toggle, Start, End and break to Tuesday–Friday in the unsaved form; it never copies `capacity_percent` (each copied day keeps its stored value, or 100 when newly enabled, per BR-07). **Reset** restores the weekly table to the last saved state. Neither persists until **Save changes**. | Frontend |
| BR-09 | Skills: the picker searches active organization skills not already assigned (case-insensitive contains); **Add skill** focuses the picker. A newly added skill gets level Intermediate (3); level is required (BR-25 labels of `team-technician-management`: 1 Beginner, 2 Basic, 3 Intermediate, 4 Advanced, 5 Expert). Remove (×) takes effect on save. Order: primary first, then name A–Z. Assignments to inactive skills are neither shown nor submitted and are preserved by save, except that their `is_primary` is cleared when the submitted set has a primary. | Both |
| BR-10 | Primary: when the submitted skill set is non-empty it must contain exactly one `isPrimary` (`400` `errors.skills` "Choose one primary skill."). Client-side, the first skill added to an empty set becomes primary; removing the primary promotes the remaining skill first by name; **Set as primary** on another row moves the badge. | Both |
| BR-11 | Save: `PUT .../skills-availability` with `version` (the profile `updated_at` loaded) replaces the profile's weekly rows and breaks and its active-skill assignments in one transaction and sets `technician_profiles.updated_at`. Stale `version` or a concurrent write → `409` "This technician was updated by someone else. Reload to see the latest changes."; nothing saved. Any validation failure → `400` with field errors; nothing saved. Duplicate days or skills, skills not active in the organization → `400`. Saving unchanged data → `200` with no row written, `version` not advanced and no audit. `years_experience` of kept skills is preserved. | Backend |
| BR-12 | Exception fields — see Exception validation table. Stored as `starts_at`/`ends_at` instants computed from the local date and times in the BR-03 time zone; Unavailable all day = local midnight to next local midnight; `is_available` = true only for Extended availability. Create sets `status = active`. | Both |
| BR-13 | Exception type is derived, not stored: `is_available = true` → **Extended availability** (blue); `false` spanning the full local day → **Unavailable** (red, time "All day"); otherwise **Partially unavailable** (amber). | Both |
| BR-14 | One active exception per technician and local date. Create, edit or activate that would produce a second active exception on a date → `409` "This technician already has an active exception on this date." Exception mutations are serialized per technician so concurrent requests for the same date produce exactly one success. | Backend |
| BR-15 | Exception lifecycle: edit only `active` exceptions whose date ≥ today; cancel `active` → `cancelled` and activate `cancelled` → `active` only when date ≥ today. Past date → `409` "Past exceptions can't be changed."; editing a cancelled exception → `409` "Activate this exception before editing it."; same-status cancel/activate → `200` no-op without audit. Each mutation sends the exception's `version` (`updated_at`); mismatch → `409` "This exception was changed by someone else. Reload to see the latest version." Cancel asks for confirmation "Cancel this exception?". | Both |
| BR-16 | Exception list: exceptions with local date ≥ today, active and cancelled, ordered by date then start. Columns Date, Availability (BR-13 tag), Time ("h:mm a – h:mm a" or "All day"), Reason, Status (Active green dot / Cancelled grey dot), Actions menu for editors: **Edit** and **Cancel** (active) or **Activate** (cancelled). Empty: "No upcoming exceptions." | Both |
| BR-17 | Catalog dialog (editors): all organization skills by name with status; **Add skill** and inline edit of Name (required, trimmed, ≤120) and Description (optional, ≤500); **Deactivate** / **Activate**. Names are unique per organization case-insensitively ("A skill with this name already exists."), checked before save and enforced by the BR-24 index; a constraint race returns the same `400`. Deactivation keeps existing technician and work-order assignments; inactive skills cannot be added (BR-09). Same-status calls → `204` no-op. Empty catalog: "No skills defined yet." | Both |
| BR-18 | Upcoming availability: **Today** — "Available now" when now is inside effective availability and not in an active-assignment visit (status not `unscheduled`/`cancelled`/`completed`/`approved`) nor a break; otherwise "Next available h:mm a" for the first such minute today; otherwise "Unavailable" (red tag). **Tomorrow** — effective windows joined with " and " or "Unavailable". Then up to 3 dates within the next 30 days (after tomorrow) whose availability is reduced by an active exception: windows plus **Limited** (amber) for partial, "Unavailable" (red) for full-day. Extended-availability days are not listed. | Backend / Frontend |
| BR-19 | Next 7 days capacity (local days today … today+6): Available = BR-04 minutes; Scheduled = durations of visits with an active assignment (`visit_assignments.unassigned_at IS NULL`) for this technician, status not `unscheduled`/`cancelled`, clipped to the period; Remaining = max(Available − Scheduled, 0); Utilization = round(Scheduled ÷ Available × 100), "—" when Available = 0. The bar shows Utilization and the remainder, capped at 100 %; above 100 % the value and bar are red. Values in hours with up to one decimal. Note "Existing jobs are edited in the Dispatch Calendar." Related schedule: **Today** jobs count and "{N}% booked" (same rules for today; "—" when no availability) and **Open dispatch calendar**. | Backend / Frontend |
| BR-20 | Navigation: breadcrumb **Team** → `/team`; breadcrumb name, **View profile** and **Overview** tab → `/coming-soon/technician-profile` ("Technician profile"); **Schedule** tab and **Open dispatch calendar** → `/coming-soon/schedule`; **Job history** tab → `/coming-soon/technician-job-history` ("Job history"). Team drawer **Edit skills** / **Edit availability** → this page. "How assignment matching works" is static text: "Work order defines required skills.", "Technician profile provides skills and availability.", "Dispatcher compares matches and assigns manually.", "FieldOps never assigns a technician automatically." | Frontend |
| BR-21 | Role UX: editors see **Save changes**, editable controls, **Copy Monday to weekdays**, **Reset**, picker, **Add skill**, **Manage skill list**, **Add exception** and row actions. Dispatcher and Technician see the same data read-only with those controls hidden and no catalog request. Accounting, Viewer and unknown roles see "You don't have access to Team." without further requests. `404` → "This technician profile isn't available." with a link to `/team`. | Frontend |
| BR-22 | Unsaved changes: **Save changes** is disabled until the weekly/skills form is dirty and valid; leaving the page while dirty asks via the shared discard dialog. Exception and catalog dialogs ask the same when closed dirty. While saving, buttons show loading, controls are disabled and double submission is prevented. After a `409` conflict the form is kept and the message offers **Reload**. | Frontend |
| BR-23 | Audit (`actor_user_id` caller, `branch_id` profile branch): `technician_profile.skills_availability_updated` (entity profile; metadata `{ changedSections }` from `availability`, `skills`); `technician_exception.created` / `.updated` / `.cancelled` / `.activated` (entity exception; metadata `{ technicianId }`); `skill.created` / `.updated` / `.activated` / `.deactivated` (entity skill, no branch). Exception reasons are not written to audit rows or logs. No-ops write no rows. | Backend |
| BR-24 | Schema amendment (approved by the user 2026-10-01): `ALTER TABLE technician_exceptions ADD COLUMN status varchar(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','cancelled')), ADD COLUMN created_at timestamptz NOT NULL DEFAULT now(), ADD COLUMN updated_at timestamptz NOT NULL DEFAULT now()`; `CREATE UNIQUE INDEX ux_skills_org_normalized_name ON skills(organization_id, lower(name))`. `docs/database/fieldops-schema.sql` is updated accordingly. Migration generated with `--generate-migration`, never applied; it fails rather than deduplicating when case-duplicate skill names exist. | Backend |
| BR-25 | Legacy normalization: when a day has more than one `technician_weekly_availability` row or a window has more than one break, the GET returns only the earliest-starting window of that day (with its `capacity_percent`) and that window's earliest-starting break; a save (BR-11) replaces all of the profile's weekly rows and breaks with the submitted ones, removing the extras. | Backend |

### Exception validation

| Field | Required | Rules | Message |
| ----- | -------- | ----- | ------- |
| Date | Yes | Local date ≥ today (BR-03) | "Choose today or a future date." |
| Type | Yes | Extended availability, Partially unavailable, Unavailable all day | "Choose an availability type." |
| Start / End (all-day) | Must be omitted for Unavailable all day | Any value sent → `400` `errors.start` | "Times aren't allowed for an all-day exception." |
| Start / End | Yes, except Unavailable all day | 30-minute steps 12:00 AM–11:30 PM; Start < End | "Choose a start and end time." / "End time must be after start time." |
| Reason | Yes | Trimmed, ≤200 | "Enter a reason." / "Use 200 characters or fewer." |

Server errors are `400` `errors.<field>` with the same messages, shown under
the fields. Weekly errors use `errors.weeklyAvailability[{dayOfWeek}].<field>`
with "Choose a start and end time.", "End time must be after start time." and
"The break must fit inside the working hours."

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | exception active | Create | Owner, Operations Manager | BR-12, BR-14 |
| exception active | exception active | Edit | Owner, Operations Manager | Date ≥ today, version, BR-14 |
| exception active | exception cancelled | Cancel | Owner, Operations Manager | Date ≥ today, version |
| exception cancelled | exception active | Activate | Owner, Operations Manager | Date ≥ today, version, BR-14 |
| skill active | skill inactive | Deactivate | Owner, Operations Manager | — |
| skill inactive | skill active | Activate | Owner, Operations Manager | — |

## Data and persistence impact

- Tables used: `technician_profiles` (`id`, `organization_id`, `branch_id`,
  `organization_user_id`, names, `status`, `updated_at`), `branches`
  (`name`, `timezone`), `organizations` (`timezone`),
  `technician_weekly_availability`, `technician_breaks`, `technician_skills`,
  `skills`, `technician_exceptions`, `visits`, `visit_assignments`,
  `organization_users`, `organization_user_branches`, `roles`, `audit_logs`.
- Writes: `technician_weekly_availability`, `technician_breaks`,
  `technician_skills`, `technician_profiles.updated_at`, `technician_exceptions`,
  `skills`, `audit_logs`.
- Child tables without `organization_id` are reached only through a profile or
  skill already verified in the caller's organization.
- Schema amendments required: BR-24 (approved 2026-10-01; migration generation
  authorized, application forbidden).

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session's
  active membership.
- Client-provided organization identifiers: never accepted. Technician,
  exception and skill ids are verified against the caller's organization,
  branch scope and, for Technicians, their own membership.
- Profile of another organization, out of scope or not the Technician's own:
  `404`, no change. Exception of another profile: `404`. Skill of another
  organization in a save: `400` `errors.skills`; on catalog routes: `404`.
- Permissions per action: BR-01; scope: BR-02.

## API contracts

Contract status: Final. All endpoints require an authenticated session, the
existing CSRF protection on unsafe methods, and return `401` when
unauthenticated. Validation errors use the existing `400` problem shape with
`errors.<field>`. Times are `HH:mm` local strings; dates `yyyy-MM-dd` local;
instants UTC ISO-8601; `version` is an opaque string.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/team/technicians/{id}/skills-availability` | — | `200 { technician: { id, firstName, lastName, profileStatus, branch: { id, name } }, version, timezone, timezoneLabel, weeklyAvailability: [{ dayOfWeek, start, end, breakStart?, breakEnd?, capacityPercent }], skills: [{ skillId, name, proficiency, isPrimary }], exceptions: [{ id, date, kind: "extended"\|"partial"\|"unavailable", start?, end?, reason, status: "active"\|"cancelled", version }], upcoming: [{ date, label: "today"\|"tomorrow"\|"date", state: "available_now"\|"next_available"\|"windows"\|"unavailable", nextAvailableAt?, windows: [{ start, end }], limited }], capacity: { availableMinutes, scheduledMinutes, remainingMinutes, utilizationPercent? }, today: { jobs, bookedPercent? } }` | `403`, `404` | Read |
| PUT | `/team/technicians/{id}/skills-availability` | `{ version, weeklyAvailability: [{ dayOfWeek, start, end, breakStart?, breakEnd? }], skills: [{ skillId, proficiency, isPrimary }] }` (omitted days are Off) | `200` (GET shape) | `400`, `403`, `404`, `409` | Mutate |
| POST | `/team/technicians/{id}/exceptions` | `{ date, kind, start?, end?, reason }` | `201` (exception shape) | `400`, `403`, `404`, `409` | Mutate |
| PUT | `/team/technicians/{id}/exceptions/{exceptionId}` | `{ version, date, kind, start?, end?, reason }` | `200` (exception shape) | `400`, `403`, `404`, `409` | Mutate |
| POST | `/team/technicians/{id}/exceptions/{exceptionId}/cancel` | `{ version }` | `200` (exception shape) | `403`, `404`, `409` | Mutate |
| POST | `/team/technicians/{id}/exceptions/{exceptionId}/activate` | `{ version }` | `200` (exception shape) | `403`, `404`, `409` | Mutate |
| GET | `/team/skills` | — | `200 [{ id, name, description?, isActive }]` by name | `403` | Mutate |
| POST | `/team/skills` | `{ name, description? }` | `201 { id, name, description?, isActive }` | `400`, `403` | Mutate |
| PUT | `/team/skills/{skillId}` | `{ name, description? }` | `200` (skill shape) | `400`, `403`, `404` | Mutate |
| POST | `/team/skills/{skillId}/deactivate` | — | `204` | `403`, `404` | Mutate |
| POST | `/team/skills/{skillId}/activate` | — | `204` | `403`, `404` | Mutate |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Skills & availability page | Skeleton header and cards | Weekly all Off ("0 available hours per week"); "No skills assigned yet."; "No upcoming exceptions."; capacity zeros with Utilization "—" | Page error with **Retry**; save errors under fields; `409` conflict message with **Reload** | Forbidden state; read-only for Dispatcher/Technician; `404` not-available state | Toast "Changes saved."; derived sections refresh |
| Exception dialog ("Add exception" / "Edit exception") | — | — | Field errors; `409` inline message, form kept | Editors only | Toast "Exception added." / "Exception updated."; list and derived sections refresh |
| Cancel / activate exception | Action loading | — | `409` toast with message | Editors only | Toast "Exception cancelled." / "Exception activated." |
| Manage skill list dialog | Skeleton rows | "No skills defined yet." | Field errors; retry on load failure | Editors only | Toast "Skill saved."; picker refreshes |

- Mockups: `design/assets/23-design.png` (Approved). Exception and catalog
  dialogs follow existing FieldOps dialog patterns (user decision, no
  ui-designer consultation).
- Responsive: below desktop the right column stacks under the left; the weekly
  table and exception table scroll horizontally inside their cards; header
  actions wrap. Toggles carry On/Off text; tags carry text, not color alone;
  the capacity bar carries text percentages; every control has a label.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Stale profile version on save | `409` "This technician was updated by someone else. Reload to see the latest changes." | None |
| Invalid weekly window/break or skills | `400` field messages | None |
| Second active exception on a date | `409` "This technician already has an active exception on this date." | None |
| Past exception changed | `409` "Past exceptions can't be changed." | None |
| Edit of cancelled exception | `409` "Activate this exception before editing it." | None |
| Stale exception version | `409` "This exception was changed by someone else. Reload to see the latest version." | None |
| Duplicate skill name | `400` "A skill with this name already exists." | None |
| Profile out of scope / other organization / not own | `404` "This technician profile isn't available." | None |
| Role not permitted | `403`; forbidden state or toast | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner on the Team drawer of a profile | They select **Edit skills** or **Edit availability** | `/team/{id}/skills-availability` renders in the shell with the Design 23 header, tabs, weekly table, skills, exceptions, upcoming availability, capacity, matching and related schedule cards; sidebar Team active |
| AC-02 | Owner on the page | They select breadcrumb items, **View profile**, each tab and **Open dispatch calendar** | Each navigates to its BR-20 destination |
| AC-03 | A profile with a home branch timezone, and one whose branch has none | The page loads | Times, "today" and the time zone label use the branch timezone, else the organization timezone; days list Monday–Sunday with saved windows, breaks and capacity per BR-05–BR-07 |
| AC-04 | An editor changes days (table-driven: toggle Off, new day On, shifted times, break no longer fitting, `capacity_percent` 50 day) | Values change | Capacity per day and the weekly total recompute per BR-07; a non-fitting break clears; **Save changes** becomes enabled only when dirty and valid |
| AC-05 | A dirty weekly table where Wednesday has `capacity_percent` 50 and Friday is Off | **Copy Monday to weekdays**, then **Reset** | Tuesday–Friday take Monday's toggle, times and break; Wednesday keeps 50 % and Friday uses 100 % in capacity; Reset restores the last saved table; nothing is persisted |
| AC-06 | An editor | They add a skill from the picker, change its level, set it primary and remove another | Picker excludes assigned and inactive skills; new skill defaults to Intermediate; exactly one Primary badge; removing the primary promotes the next by name |
| AC-07 | Valid weekly and skill changes, and a technician with an inactive-skill assignment | **Save changes** | `200`; rows and breaks replaced, skills replaced, inactive assignment preserved, profile version advanced; toast; `.skills_availability_updated` audit lists changed sections; an unchanged save writes nothing, keeps the version and writes no audit; a profile with legacy duplicate windows/breaks loads per BR-25 and is normalized by the save |
| AC-08 | Invalid saves (table-driven: Start ≥ End, non-30-minute time, break outside window, duplicate day, zero or two primaries, duplicate or foreign/inactive skill, missing level) | **Save changes** | `400` with BR-10/BR-11 field errors; no rows changed (verified transactionally, including when the invalid item is the last one) |
| AC-09 | Two editors loaded the same version | Both save | The first succeeds; the second gets `409` with the conflict message, nothing saved; the page keeps the form and offers **Reload** |
| AC-10 | An editor | They create each exception type with valid fields (table-driven, including a DST-change date) | `201`; `starts_at`/`ends_at` match the local date/times; derived kind, tag and time text follow BR-12/BR-13; list, upcoming and capacity refresh; `technician_exception.created` audit without reason |
| AC-11 | Invalid exception input (table-driven per Exception validation) | It is submitted | `400` with the field messages; nothing saved |
| AC-12 | An active exception on a date | A second create, an edit moving another exception to that date, or activating a cancelled one on that date; and two concurrent creates | Each is `409` with the BR-14 message; concurrently exactly one succeeds |
| AC-13 | Active, cancelled and past exceptions | Edit, cancel and activate are attempted, including stale version and same-status calls | Allowed transitions succeed with audit rows; past → `409`; editing cancelled → `409`; stale version → `409`; same-status → `200` no-op without audit; the list shows only date ≥ today, ordered, with Active/Cancelled status |
| AC-14 | Weekly windows, breaks, `capacity_percent`, exceptions of each type (one cancelled) and visits in various statuses and assignment states at a fixed clock (parameterized) | Page data loads | Upcoming availability rows follow BR-18 (Today state, Tomorrow windows, up to 3 limited/unavailable dates, extended days omitted) |
| AC-15 | Same parameterized data, including visits crossing the 7-day boundary, unassigned assignments and over-booking | Page data loads | Available, Scheduled, Remaining, Utilization and Related schedule follow BR-19; Utilization "—" with no availability; > 100 % red with bar capped |
| AC-16 | An editor with an empty or populated catalog | They add, rename, deactivate and activate skills, including a case-only duplicate name | Catalog operations follow BR-17; duplicate → `400`; deactivated skill leaves the picker but stays on existing technicians; audit rows written; catalog index rejects a concurrent duplicate with the same `400` |
| AC-17 | A Dispatcher assigned to branch A, and an Operations Manager | Each opens profiles in branches A and B | The Dispatcher reads branch A read-only and gets `404` for B; the Operations Manager reads both |
| AC-18 | A Technician with a linked profile | They open their own page and another technician's page | Own page read-only with no edit controls and no catalog request; the other → `404` not-available state |
| AC-19 | A profile, exception or skill of organization B | A user of organization A reads or mutates it, or saves with an organization B skill id | `404` (or `400` `errors.skills` for the skill in a save); no data change |
| AC-20 | Each role | It calls every endpoint (parameterized) | Access follows BR-01; Accounting/Viewer `403` everywhere and see the forbidden state with no further requests; Dispatcher/Technician `403` on every mutation and catalog endpoint |
| AC-21 | The page as Owner | Requests are pending, fail or return empty data | Skeletons, page error with **Retry** and the empty texts render per the UI table |
| AC-22 | A dirty page, dirty dialog and a save in progress | The user navigates away, closes the dialog, and clicks save twice | The discard dialog appears; only one request is sent and controls are disabled while saving |
| AC-23 | The generated migration | It is reviewed against BR-24 | It adds exactly the three `technician_exceptions` columns with the check and the `skills` case-insensitive unique index; existing exceptions become `active`; the schema doc matches; the migration is not applied |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Availability/capacity/upcoming calculator (breaks, capacity percent, exception kinds, cancelled ignored, visit clipping, DST and non-UTC timezone, kind derivation), table-driven; weekly/skills validation rules | AC-08, AC-10, AC-14, AC-15 |
| Backend integration | Atomic save with rollback and version conflict; exception lifecycle, one-active-per-date including concurrency, past/stale guards; catalog uniqueness incl. index race; audit rows | AC-07–AC-13, AC-16 |
| Authorization/tenant isolation | Role matrix (parameterized); Dispatcher branch scope; Technician own-only; one cross-organization denial per resource path (profile, exception, skill) | AC-17–AC-20 |
| Frontend component/service | Weekly table derivations, copy/reset; skill primary rules; role-based read-only/forbidden/not-found; save/discard/conflict and empty/error states | AC-04–AC-06, AC-09, AC-18, AC-20–AC-22 |
| Persistence review | `database-reviewer` on mappings and migration | AC-23 |
| Manual visual QA (user) | Fidelity to Design 23, navigation, dialogs, responsive | AC-01–AC-03 |

The backend unit count may reach the upper default because the derivation
calculator and the save/exception guards are the feature's main non-trivial
logic; concurrency (AC-09, AC-12, AC-16) is required by the approved scope.

## Dependencies

- `team-technician-management` (profiles, BR-01/BR-02 scope, derivation rules,
  drawer entry points) — audited.
- Supersedes `team-technician-management` BR-24/AC-24 only for the drawer
  **Edit skills** and **Edit availability** destinations (now this page per
  BR-20); its other BR-24 Coming soon destinations are unchanged. Final audit
  of this feature treats that change as intended, not a regression.
- Shared discard dialog — implemented.
- Coming soon page with slugs — implemented.
- Visits/work orders creation — future; capacity reads whatever exists.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Saving, editing exceptions and catalog changes are allowed for inactive or suspended profiles; the header shows their status tag. |
| AS-02 | Dispatcher and Technician reach the page by direct URL only; no new entry point is added for them in this spec (decision 11a). |
| AS-03 | Catalog renames follow last-write-wins; the skills table has no version column and the catalog has no concurrency requirement beyond name uniqueness. |
| AS-04 | Removed 2026-10-01: behavioral; promoted to BR-25. |
| AS-05 | The time zone label is informational and not selectable; no technician-level time zone exists. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Exception status | Schema columns / delete | No | Add `status`, `created_at`, `updated_at`; migration generated, never applied (2026-10-01) |
| OD-02 | Exception type | Derived / column | No | Derived from `is_available` and duration (2026-10-01) |
| OD-03 | Capacity | Read-only / hours / percent | No | Read-only net hours × `capacity_percent`; new rows 100 (2026-10-01) |
| OD-04 | Time zone | Home branch / main branch | No | Home branch, organization fallback (2026-10-01) |
| OD-05 | Skill catalog | Dialog / ui-designer / Coming soon | No | Dialog without ui-designer; case-insensitive unique index (2026-10-01) |
| OD-06 | Primary and level | Exactly one / at most one | No | Level required, default Intermediate; exactly one primary when skills exist (2026-10-01) |
| OD-07 | Exception rules | — | No | Single local date, 30-minute steps, one active per date, reason required, immediate actions, no past edits (2026-10-01) |
| OD-08 | Save scope | Weekly + skills / include exceptions | No | Weekly, breaks and skills atomic; exceptions independent (2026-10-01) |
| OD-09 | Upcoming availability | — | No | Approved as BR-18 (2026-10-01) |
| OD-10 | Next 7 days | Full days / from now | No | Full local days, approved as BR-19 (2026-10-01) |
| OD-11 | Navigation | Update Team / leave | No | Team drawer Edit skills/Edit availability → this page; others Coming soon (2026-10-01) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03, AC-14, AC-15 |
| FR-03 | AC-03, AC-04, AC-05 |
| FR-04 | AC-06 |
| FR-05 | AC-07, AC-08, AC-09 |
| FR-06 | AC-10, AC-11, AC-12, AC-13 |
| FR-07 | AC-16 |
| FR-08 | AC-14, AC-15 |
| FR-09 | AC-17, AC-18, AC-19, AC-20 |
| FR-10 | AC-09, AC-18, AC-20, AC-21, AC-22 |
| FR-11 | AC-07, AC-10, AC-13, AC-16 |
| FR-12 | AC-13, AC-16, AC-23 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-01 | — → DRAFT     | Created with user decisions OD-01–OD-11 |
| 2026-10-01 | DRAFT → DRAFT | Revised after validation: copy excludes capacity_percent (BR-08), unchanged save keeps version (BR-11), all-day exception rejects times, AS-04 promoted to BR-25, Team BR-24 supersession recorded |
| 2026-10-01 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-01 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-01 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
