# Technician time summary

| Field    | Value                     |
| -------- | ------------------------- |
| Feature  | `technician-time-summary` |
| Type     | Full-stack                |
| Status   | APPROVED                  |
| Created  | 2026-10-17                |
| Updated  | 2026-10-17                |
| Approved | 2026-10-17                |

## Context and objective

Travel, work and pause time is already recorded automatically in
`visit_time_entries` by the technician job flow, but technicians cannot see
it. This feature replaces the **Time** Coming soon destination with the
approved **Time summary**: Day and Week views with totals split into Work,
Travel and Break, a stacked daily chart for the week, entries grouped by day
and a read-only entry detail. It reads only the existing time entries; it
adds no second time source, editing or approval.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Technician (`technician`) with an `active` linked profile | Read their own time entries for any day or week; open an entry's read-only detail; open the related job when it is still visible to them | Create, edit, delete or approve entries; see other technicians' time |
| Technician with an `inactive`/`suspended` profile, or without linked profile | — | The endpoint (`403 technician_inactive` / `404 technician_profile_not_linked`) |
| Owner, Operations Manager, Dispatcher, Viewer, Accounting, unknown roles | — | The endpoint (`403`); existing forbidden state |

## Scope

- Technician route `/today/time` replacing the Time Coming soon destination.
- `GET /technician/time` for Day and Week ranges.
- Read-only entry detail panel; **Open job** link when the visit is visible.

## Non-goals

- Manual entries, editing, deleting, approval, timesheets, payroll or overtime.
- A second time-tracking source, clock-in/out, or entries without a visit.
- Time of other technicians or staff views of technician time.
- Export, offline mode or caching.

## User flow

1. The technician opens **Time**; the Week view of the current week loads.
2. They switch Day/Week and move to previous/next days or weeks.
3. They read totals, the week chart and entries grouped by day.
4. Tapping an entry opens its read-only detail; **Open job** opens
   `/today/visits/:visitId` when available.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The endpoint must resolve organization, membership and profile from the session and return only the caller's own entries (BR-01). |
| FR-02 | The endpoint must return the Day or Week range, totals per type and per day, and entries per BR-02–BR-05. |
| FR-03 | The page must provide Day and Week views with range navigation per BR-06. |
| FR-04 | The page must show the totals card and, in Week view, the stacked daily chart per BR-07. |
| FR-05 | The page must list entries grouped by day per BR-08 and open a read-only detail per BR-09. |
| FR-06 | The page must provide loading, empty, error and permission states and the accessibility of BR-10 and BR-11. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization: policy TechnicianSelf and profile resolution as `technician-todays-jobs` BR-01/BR-02. Entries are the `visit_time_entries` rows whose `technician_id` is the caller's profile and whose visit belongs to the session organization, regardless of current assignment or visit status (own history is kept after reassignment or cancellation). No technician or organization id is accepted; extra parameters are ignored. | Backend |
| BR-02 | Range and zone: zone = profile branch `timezone`, falling back to `organizations.timezone`. `view` = `day` \| `week` (default `week`); `date` = ISO date between 2000-01-01 and 2099-12-31 (default the current local date). Day = `[date 00:00, +1 day)` local; Week = Monday–Sunday containing `date`. DST-aware. Invalid → `400 errors.view` / `errors.date` "Choose a valid date.". | Backend |
| BR-03 | Membership and duration: an entry belongs to the local day containing its `started_at` and is counted wholly in that day. Duration = `ended_at − started_at` in whole seconds; an open entry (`ended_at` null) counts `now − started_at` at response time and is flagged `isOpen`. | Backend |
| BR-04 | Types and totals: `travel` → Travel, `work` → Work, `pause` → Break. `totals = { workSeconds, travelSeconds, breakSeconds, totalSeconds }` with total = the three summed; `days[]` has one row per day of the range (7 for week, 1 for day) with the same four fields and `date`, zeros when empty. Totals derive only from `visit_time_entries`; `visits.pause_seconds` is not added. | Backend |
| BR-05 | Entries: `TimeEntry = { id, type, startedAt, endedAt \| null, durationSeconds, isOpen, visit: { visitId, displayNumber, title, status, visitNumber }, visitAccessible }`. `visitAccessible` = the visit is readable through `GET /technician/visits/{visitId}` (active assignment, not `unscheduled`; `cancelled` per `technician-schedule-active-visits` BR-09). Entries ordered by `started_at` descending; days in the UI from most recent to oldest. | Backend |
| BR-06 | Header: "Time summary"; Day/Week toggle (Week default); previous/next buttons ("Previous day"/"Next day" or "Previous week"/"Next week"); range label "EEE, MMM d" (day) or "MMM d – d" / "MMM d – MMM d" across months, with the year appended when not the current year. Switching to Day keeps the selected date (today when the week contains it, else the week's Monday); switching to Week shows the week containing the selected date. Future ranges are allowed and empty. | Frontend |
| BR-07 | Totals card: total "<h>h <m>m" ("<m>m" under one hour, "0m" when zero) with "Total recorded"; three tiles Work, Travel, Break with icons and durations. Week view: a stacked bar per day Mon–Sun (Work, Travel, Break segments, each with a distinct fill and a legend), the day total above each bar and the weekday below; bars scale to the week's highest day; a zero day shows an empty baseline with "0m". Day view: no chart. | Frontend |
| BR-08 | Entries list: one section per day with entries, heading "Today · EEEE", "Yesterday · EEEE" or "EEEE, MMM d" and the day total on the right; rows show the type icon, title (work order title for Work/Travel; "Break" with the work order title as secondary line for Break), type chip ("Work", "Travel", "Break"), "#<prefix>-<n>", "h:mm a – h:mm a" (open: "h:mm a – now" and an "In progress" chip), duration and a chevron. Rows are buttons opening BR-09. Info notice below: "Time is recorded automatically while you travel, work or pause a job." | Frontend |
| BR-09 | Entry detail (panel, read-only): type, work order title and "#<prefix>-<n>", "Visit <k>", local date "EEE, MMM d, yyyy", start, end (or "In progress"), duration, and the text "Time entries can't be edited here. Contact your manager for corrections."; **Open job** → `/today/visits/:visitId` only when `visitAccessible`; **Close**. No edit or delete control. | Frontend |
| BR-10 | States: loading skeleton for totals, chart and list; empty range "No time recorded for this day." / "No time recorded for this week." with totals at 0m and navigation enabled; failure "We couldn't load your time. Check your connection and try again." with **Retry**, a failed navigation keeps the previous data with the inline error; stale responses ignored. Unlinked, inactive and forbidden states as `/today`. Open entries refresh their durations at least every minute while visible. | Frontend |
| BR-11 | Navigation and accessibility: bottom navigation per `technician-schedule-active-visits` BR-20 with **Time** at `/today/time` current; `/today/soon/time` redirects to `/today/time`; top bar title "Time". The chart has a text alternative per day ("Mon: total 4h 20m, work 3h 40m, travel 30m, break 10m") and is never the only source of values; type is conveyed by text, not color alone; the detail panel traps and returns focus; targets ≥ 44 × 44 px; `technician-todays-jobs` BR-19 applies. | Frontend |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): `visit_time_entries`
  (`id`, `visit_id`, `technician_id`, `started_at`, `ended_at`,
  `entry_type`), `visits` (`status`, `visit_number`, `work_order_id`),
  `visit_assignments`, `work_orders` (`work_order_number`, `title`),
  `organizations` (`timezone`, `work_order_prefix`), `branches` (`timezone`),
  `technician_profiles`, `organization_users`.
- Writes: none. No audit rows.
- Schema amendments required: None.

## Tenant isolation and authorization

- Organization, membership and profile come from the session (BR-01).
- Client-provided values: `view` and `date` only; no identifiers.
- Another technician's or organization's entries are never returned.
- `Cache-Control: no-store`. Logs never contain work order titles.

## API contracts

Contract status: Final. Errors use ProblemDetails.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/technician/time` | Query `view?`, `date?` | `200 { timezone, today, view, from, to, totals, days: [{ date, workSeconds, travelSeconds, breakSeconds, totalSeconds }], entries: TimeEntry[] }` | `400` (`errors.view`, `errors.date`) · `403` (role; `technician_inactive`) · `404 technician_profile_not_linked` | TechnicianSelf |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/today/time` | Skeleton totals, chart, rows | BR-10 empty texts, totals 0m | BR-10 error with **Retry**, previous data kept | As `/today` | BR-06–BR-08 |
| Entry detail panel | — | — | — | — | BR-09 |

- Mockups: `design/assets/Resumen semanal del tiempo del técnico.png`
  (Approved). Deviations: Break rows carry their work order (pause entries
  belong to a visit); the device status bar and bell badge are not part of
  this spec; mockup data are examples.
- Responsive and accessibility: BR-11.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid `view` or `date` | `400`; the page falls back to the current week | None |
| Role other than technician | `403`; forbidden state | None |
| Unlinked / inactive profile | `404` / `403` with the `/today` messages | None |
| Load failure | BR-10 error with **Retry** | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Entries of the caller (including on a visit later reassigned and on a cancelled visit), of another technician and of another organization | `GET /technician/time` is called, with a foreign `technicianId` parameter | Only the caller's entries are returned, including the reassigned and cancelled ones; the parameter is ignored; no foreign data |
| AC-02 | Other roles (parameterized), an unlinked and an inactive technician | They call the endpoint and open `/today/time` | `403`, `404 technician_profile_not_linked`, `403 technician_inactive` with the corresponding states and no data |
| AC-03 | A branch zone different from the organization and UTC, a DST week, entries starting at local 23:50 and ending after midnight, and Monday 00:00 / Sunday 23:59 boundaries; invalid `view`/`date` (table-driven) | Day and week are requested | Ranges and day membership by `started_at` follow BR-02/BR-03; the cross-midnight entry counts wholly on its start day; invalid values `400` |
| AC-04 | Closed work, travel and pause entries of known lengths and one open work entry | The week is requested | Per-type and per-day totals and `totalSeconds` follow BR-04 with the open entry counted to now and `isOpen = true`; `pause_seconds` is not double counted |
| AC-05 | Entries on a visit actively assigned, unassigned from the caller, `cancelled` and assigned | Entries are returned | `visitAccessible` is true for the assigned and cancelled-still-assigned visits and false for the unassigned one; entries ordered by `started_at` descending |
| AC-06 | The Time page | Day/Week is toggled and previous/next used across a month and year boundary | The range label, selected date and request follow BR-06; Week is default; future ranges show the empty state |
| AC-07 | A week with data on 5 days and an empty day | The Week view renders | The totals card, stacked bars with day totals, zero baseline, legend and per-day text alternative follow BR-07; Day view shows no chart |
| AC-08 | Entries of today, yesterday and an earlier day, including Break and an open entry | The list renders and a row is opened | Sections, headings, day totals and row content follow BR-08; the open entry shows "now" and "In progress"; the detail follows BR-09 with **Open job** only when `visitAccessible` and no edit control |
| AC-09 | An empty week, a failing load and a failing navigation | The page is used and **Retry** pressed | Empty texts with 0m totals; error with Retry that reloads; failed navigation keeps the previous data with the inline error |
| AC-10 | The technician shell | **Time** and `/today/soon/time` are opened | Time is current with `aria-current`; the Coming soon route redirects; other destinations unchanged |
| AC-11 | Viewports 375 px and 1280 px | The page is used by keyboard and touch | Approved design layout; no page-level horizontal scroll; targets ≥ 44 px; visible focus; type not by color alone; panel traps and returns focus |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Range/day bucketing and duration totals only if implemented as a pure helper | AC-03, AC-04 |
| Backend integration | Own-entries selection, zone and DST boundaries, totals with open entry, `visitAccessible` (table-driven) | AC-01, AC-03–AC-05 |
| Authorization/tenant isolation | Foreign technician/organization entries never returned; role/unlinked/inactive reuse TechnicianSelf evidence with one representative check | AC-01, AC-02 |
| Frontend component/service | View toggle and navigation, totals and chart text alternative, grouped list, detail panel, states and redirect (3–6 methods) | AC-06–AC-10 |
| User visual QA (manual) | Design fidelity, chart rendering, responsive, focus | AC-11 |

Backend integration stays within 3–8 methods. Agents run no browser automation.

## Dependencies

- `technician-schedule-active-visits` (DRAFT): bottom navigation order (BR-20)
  and cancelled-visit readability used by `visitAccessible`. Implemented first.
- `mobile-job-details`, `mobile-job-progress`, `mobile-job-completion`
  (AUDITED): source of travel, work and pause entries.
- `technician-todays-jobs` (AUDITED): shell, profile resolution, zone.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | A week holds at most a few hundred entries; no paging. |
| AS-02 | The entry detail uses the list payload; no separate endpoint. |
| AS-03 | Chart fills use existing design tokens for Work, Travel and Break. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Time rules | — | No | Monday–Sunday, profile zone, grouping by `started_at`, open entries to now with "In progress", own history kept after reassignment, job link only when visible, pauses as Break linked to the job — user 2026-10-17 |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03, AC-04, AC-05 |
| FR-03 | AC-06, AC-10 |
| FR-04 | AC-07 |
| FR-05 | AC-08 |
| FR-06 | AC-02, AC-09, AC-11 |

## Change log

| Date       | Status change | Reason  |
| ---------- | ------------- | ------- |
| 2026-10-17 | — → DRAFT     | Created |
| 2026-10-17 | DRAFT → APPROVED | Approved by user via /spec approve |
