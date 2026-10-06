# Dispatch calendar

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `dispatch-calendar` |
| Type     | Full-stack          |
| Status   | APPROVED            |
| Created  | 2026-10-06          |
| Updated  | 2026-10-06          |
| Approved | 2026-10-06          |

## Context and objective

`create-work-order` leaves visit #1 of every work order `unscheduled`, and
nothing can schedule it, assign technicians or materialize recurring
occurrences. This feature delivers the Dispatch calendar (Design 6) at
`/schedule`: a per-technician Day/Week calendar, an unscheduled visits panel
and the **Assign work order** drawer. Dispatchers set the date, time, arrival
window, one or more technicians and an internal note. The system evaluates
branch, skills, availability, breaks, time off and overlaps, lets the
dispatcher confirm conflicts with a justification, keeps history and audit,
updates the work order status, materializes the next recurring occurrence and
emails the customer after commit.

## Actors and permissions

Derived from the Design 18 matrix row "Work orders & schedule" and OD-09.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Operations Manager (`operations_manager`) | Read the calendar, panel and drawer for every branch; schedule, reschedule, assign, reassign and unassign visits; confirm conflicts with a justification | Modify visits that started or are closed (BR-13) |
| Dispatcher (`dispatcher`) | The same as Owner, for work orders, visits, technicians and branches in branch scope (BR-02) | Anything outside branch scope (`404`) |
| Viewer (`viewer`) | Read the calendar, panel and a read-only drawer within scope | Evaluation and dispatch endpoints (`403`) |
| Technician (`technician`), Accounting (`accounting`), unknown roles | — | Every endpoint of this feature (`403`; forbidden state) |

## Scope

- Route `/schedule` inside the shell, replacing the `schedule` Coming soon
  destination; the four existing `/coming-soon/schedule` links point to it.
- Day and Week views with Today/previous/next navigation.
- Filters: Branch (required), Team (technician multi-select), Skills and
  Status.
- Unscheduled visits panel with search and All / Today / Overdue.
- Calendar lanes per technician plus an **Unassigned** lane, showing visits,
  scheduled assessments as busy blocks, daily/weekly load, current conflicts
  and the proposed block.
- **Assign work order** drawer: date, start, end, arrival window, 1–5
  technicians with one primary, internal note, live checks, workload impact,
  conflict justification and customer notification.
- Independent scheduling and assignment states; unassigning every technician.
- Visit status history, assignment history, audit and work order aggregate
  status, shown in the existing `/jobs` list and `/jobs/:id` detail (BR-16).
- Progressive materialization of recurring occurrences, idempotent.
- Customer email after commit for scheduling and technician changes; SMS shown
  disabled.
- Schema amendment BR-22.

## Non-goals

- Drag-and-drop, automatic optimization, route planning, GPS, travel times and
  any maps integration. **Map view** is disabled; travel blocks and the
  **Travel** legend item are omitted.
- Inventory or material availability. Planned materials are informational.
- **New job** button (jobs come from approved quotes).
- Returning a visit to `unscheduled`, cancelling visits or work orders, and
  creating extra non-recurring visits.
- Technician notifications, SMS delivery, an SMS provider, rows in
  `notifications`, and automatic arrival reminders. The work order's stored
  communication preferences (including `send_arrival_reminder`) are kept
  unchanged.
- Technician execution, time entries, checklist completion, evidence, actual
  material consumption, sign-off, invoicing and payments.
- A teams entity. **Team** filters by technicians.
- Editing work order fields, availability, exceptions or skills.

## User flow

1. A dispatcher opens **Schedule**; the system shows the default branch's
   current week, the unscheduled panel and the technician lanes.
2. They filter the panel (search, All / Today / Overdue) and select a visit
   card, or select a block in the calendar.
3. The drawer opens prefilled. The calendar shows the proposed block. Each
   change of date, time or technicians re-evaluates checks and workload.
4. With conflicts, the drawer lists them and requires a justification. Without
   conflicts it shows "No scheduling conflicts".
5. The dispatcher saves. In one transaction the system updates the visit, its
   assignments, status history, the work order status, the next recurring
   occurrence and the audit row. After commit it emails the customer when
   BR-17 applies.
6. The calendar and panel refresh and a toast confirms. Later changes to time
   or technicians use the same drawer and the same rules.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The frontend must serve `/schedule` with the sidebar **Schedule** item active, and existing schedule links must point to it (BR-01, BR-20). |
| FR-02 | Every endpoint must resolve the organization from the session and apply role and branch scope to visits, work orders, technicians and branches (BR-01, BR-02). |
| FR-03 | The calendar must show Day and Week views per technician with visits, assessments, availability, workload and current conflicts, filtered per BR-03–BR-06. |
| FR-04 | The unscheduled panel must list unscheduled visits with search and All / Today / Overdue (BR-07). |
| FR-05 | The drawer must load a visit's dispatch data and evaluate a proposed schedule and technicians without writing (BR-08, BR-09, BR-10). |
| FR-06 | Dispatch must validate input, reject hard violations and require a justification for conflicts (BR-11, BR-12, BR-13). |
| FR-07 | Dispatch must atomically update schedule, arrival window, note, assignments, visit status, history, work order status and audit, with concurrency control (BR-14, BR-15, BR-16, BR-19). |
| FR-08 | Scheduling a recurring occurrence for the first time must materialize the next occurrence exactly once (BR-18). |
| FR-09 | The customer must be emailed after commit when BR-17 applies, with SMS disabled. |
| FR-10 | The page must reproduce Design 6 with the specified states and responsive behavior (BR-20, BR-21). |
| FR-11 | The schema must be amended per BR-22, and visit #1 of new work orders must carry the preferred window. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Policies. **Manage** = `owner`, `operations_manager`, `dispatcher`: evaluation and dispatch endpoints. **Read** = Manage + `viewer`: options, calendar, unscheduled list and visit dispatch detail. Other roles → `403`. The sidebar **Schedule** item is shown to every authenticated role; roles without Read get the existing forbidden state on `/schedule`. Frontend visibility is UX only. | Both |
| BR-02 | Scope (`requests-pipeline` BR-02). Owner, Operations Manager, Viewer and `is_all_branches` members see all active branches; a Dispatcher sees only `organization_user_branches`. A visit is visible when its work order's `branch_id` is in scope. A visit, branch or technician of another organization or outside scope → `404`, identical to a nonexistent id, with no foreign data. Only visits of work orders whose status is not `draft` are visible. | Backend |
| BR-03 | Branch and time zone. **Branch** is a single required selection of active in-scope branches, default the main branch when in scope, else the first by name. All dates and times of the page, the drawer and the email use the selected branch `timezone`, falling back to `organizations.timezone`. The API returns instants as ISO with offset plus `timezone`. `branch`, `view` and `date` are kept in the URL query so reload and back keep them. With no branch in scope: "No branches available." | Both |
| BR-04 | Views. **Week** = Monday–Sunday containing `date`; Saturday and Sunday columns are collapsed when no displayed technician has availability and no visit or assessment falls on them. **Day** = one local day on a horizontal hour axis from the earliest availability start to the latest end of the displayed technicians (whole hours; 8:00 AM–5:00 PM when none). **Today** returns to the current local date; arrows move by one day or one week. The current day is highlighted and, in Day view, a current-time line is drawn. | Frontend |
| BR-05 | Lanes and blocks. Lanes = active technician profiles of the branch, ordered by name, plus any technician holding an active assignment on a displayed visit (tagged "Inactive" or "Other branch"), and an **Unassigned** lane on top for `scheduled` visits without technicians. Each technician lane shows initials (profile `color_hex` when set), full name, primary skill name (or no line), and load "<scheduled>h / <available>h" for the day (Day) or week (Week) with a bar: < 60 % teal, 60–100 % amber, > 100 % red; available 0 with scheduled > 0 → "No availability". Load uses `team-technician-management` BR-04/BR-06 and counts scheduled assessments like `schedule-assessment` BR-06. Blocks: visits of the branch whose schedule intersects the range and whose status is not `unscheduled`/`cancelled`, in every assigned technician's lane, showing "h:mm – h:mm", work order title, "#<WO-n>" and the property street line; scheduled assessments of the lane's technician as read-only "Assessment" blocks; time off and out-of-availability periods shaded. A visit with current conflicts shows the warning icon and "(Conflict)" text, never color alone. Legend: Scheduled, Proposed, Conflict, Current time. | Both |
| BR-06 | Filters. **Team**: "All" or a multi-select of the branch's active technicians; it limits technician lanes (foreign, out-of-scope or other-branch ids in the query are ignored). **Skills**: "All" or one active organization skill; it limits lanes to technicians having it and panel cards to visits whose work order requires it (foreign or inactive → `400 errors.skillId`). **Status**: All, Unassigned (`scheduled`), Assigned (`assigned`), With conflicts. **With conflicts** is computed from the current schedules, assignments, availability, breaks, time off, skills and overlaps (BR-10), never from audit rows. Filters never change the BR-05 load figures. | Both |
| BR-07 | Unscheduled panel. Visits with status `unscheduled` of visible work orders in the branch whose status is `ready_to_schedule`, `scheduled` or `in_progress`. Filters: **All**; **Today** = the visit's preferred window intersects the local current day; **Overdue** = `preferred_end` < now. Visits without a preferred window appear only under All. Search (trimmed, ≤ 100 characters, case-insensitive contains, 300 ms debounce) matches "WO-n", title, customer name and property address. Order: overdue first, then `preferred_start` ascending (nulls last), priority, work order number. Page size 25 with **Load more**. The header count is the total for the current filter. Card: drag-handle icon (decorative), title, "#<WO-n>", priority with icon and text, estimated duration ("—" when null), customer name, property address, preferred window ("EEE, MMM d, h:mm a – h:mm a" or "No preferred date"), required skill chips, and "Visit <k> of <n>" for recurring orders. Overdue cards show an "Overdue" tag. | Both |
| BR-08 | Drawer load. `GET /dispatch/visits/{visitId}` returns the header (#WO-n, visit status chip, title, "Visit <k> of <n>" when recurring), customer (initials, name, primary active contact phone as `tel:` link or hidden, property address), current schedule, arrival window, active assignments with primary, dispatch note, required skills, planned materials count, the branch's active technicians with primary skill, the notification defaults (BR-17), `canManage`, `isLocked` and `updatedAt`. Prefill for an unscheduled visit: date = local date of `preferred_start` when today or later, else empty; start = `preferred_start` time when set; end = start + `estimated_duration_minutes` (60 when null); arrival window `start_plus_2h`; no technicians; empty note. | Both |
| BR-09 | Evaluation. `POST /dispatch/visits/{visitId}/evaluation` takes a proposed `date`, `start`, `end`, `technicianIds` and `primaryTechnicianId`, applies BR-11/BR-12 hard validation and returns, without writing: per technician the BR-10 checks; the skills check for the selection; per technician "<jobs> jobs · <x>h / <y>h after assignment" for the proposed day and the previous and next commitment of that day ("Previous job: #<WO-n> ends h:mm a" / "No previous job"; "Next job: #<WO-n> starts h:mm a" / "No next job"); the conflict list; and a ranking of the branch's active technicians for the slot where **Best match** marks the one with no conflicts (including skills when evaluated alone) and the lowest resulting load, ties by name, or none. The drawer re-evaluates 300 ms after each change of date, start, end or technicians. It shows "Planned materials: <n>" as information, never as a check. | Both |
| BR-10 | Conflicts per technician for slot `[start, end)`, codes and drawer texts: **`missing_skills`**: the union of the selected technicians' skills does not cover the work order's required skills → "Missing skills: <names>" (pass: "Skills match (<names>)", or "No required skills"); reported once for the selection. **`time_off`**: an active `is_available = false` exception intersects the slot → "Time off". **`break`**: a break of that day intersects the slot → "On break h:mm – h:mm a". **`outside_availability`**: otherwise, the slot is not entirely inside availability (`team-technician-management` BR-04) → "Outside availability". Pass for the three availability codes: "Available". **`overlap`**: another visit with an active assignment of that technician and status not `unscheduled`, `cancelled`, `completed` or `approved`, or a `scheduled` assessment of the technician, intersects the slot → "Overlaps #<WO-n> h:mm – h:mm a" or "Overlaps assessment h:mm – h:mm a" (pass: "No overlapping jobs"). The visit being dispatched is excluded. Each conflict is shown with an icon and text. | Backend |
| BR-11 | Field validation (table below). Failures → `400` ProblemDetails `errors.<field>`, inline with focus on the first error. | Both |
| BR-12 | Hard technician rules, never overridable. A technician id of another organization or outside the caller's scope → `404`. A technician in scope that is not `active` or whose `branch_id` differs from the work order's branch → `400 errors.technicianIds` "Choose active technicians from this work order's branch.". Duplicate ids, more than 5 ids, or a `primaryTechnicianId` not among them → `400`. | Backend |
| BR-13 | Locked visits. A visit whose status is `on_the_way`, `in_progress`, `paused`, `completed`, `needs_correction`, `approved` or `cancelled`, or whose work order is `cancelled`, `completed` or `approved_for_billing` → `409 visit_locked` "This visit has started or is closed and can't be changed.". The drawer opens read-only with that note. An override never bypasses this rule, BR-02 or BR-12. | Both |
| BR-14 | Dispatch. `PUT /dispatch/visits/{visitId}` inside one transaction: lock the visit row and check `updatedAt` (`409 visit_changed`); lock the technician profile rows of the old and new selection in id order; re-check BR-13; evaluate BR-10 for the new slot and selection. With conflicts and no `overrideReason` → `409 scheduling_conflicts` with the conflict list and no write. With conflicts and a valid `overrideReason` (10–500 characters after trim) the save proceeds. Without conflicts any `overrideReason` is ignored. Then: set `scheduled_start`, `scheduled_end`, `arrival_window_start`, `arrival_window_end` and `dispatch_note`; set `unassigned_at = now` on removed assignments; insert assignments for added technicians (`assigned_by_user_id` = actor); set `is_primary` so exactly the chosen technician is primary; set the status per BR-15 with a `visit_status_history` row when it changes (actor, reason null); apply BR-18 and BR-16; write BR-19; set `updated_at`. A request identical to the current state → `200` with no writes and no email. Success → `200 VisitDispatchResult`. Two concurrent saves that overlap the same technician serialize: the second sees the first and returns `409 scheduling_conflicts` unless it carries an `overrideReason`. | Backend |
| BR-15 | Visit status. With schedule and no active assignment → `scheduled`; with schedule and ≥ 1 active assignment → `assigned`. Technicians require a schedule: every dispatch carries `date`, `start` and `end` (BR-11), and the drawer keeps **Assign to technicians** disabled with "Set a date and time before assigning technicians." until all three are set. Removing every technician from an `assigned` visit → `scheduled`. A scheduled visit cannot return to `unscheduled` in this feature. | Both |
| BR-16 | Work order status. In the same transaction, a work order in `ready_to_schedule` becomes `scheduled` when at least one non-cancelled visit has a schedule; a work order in `scheduled` returns to `ready_to_schedule` when none has. Other work order statuses are not changed. Each change writes a `work_order.status_changed` audit row (before/after `{ status }`, metadata `{ visitId }`). The jobs list and detail show "Scheduled" for `scheduled` and the visits as "Visit #<k> · <status label>". | Both |
| BR-17 | Customer notification. Drawer section **Customer notification**: **Email customer when scheduled** (default = work order `notify_customer_when_scheduled`), **Send SMS to customer** (always unchecked and disabled, "SMS isn't available yet."), **Send technician details to customer** (default = `send_technician_details`, enabled only while email is checked). Without a primary active contact email, email is unchecked and disabled with "This customer has no email address."; `notifyCustomer = true` then → `400 errors.notifyCustomer`. After commit, exactly one email is sent through `IEmailSender` when `notifyCustomer` is true and either the schedule or arrival window changed (including the first schedule), or the set of technicians changed, at least one technician remains assigned and `sendTechnicianDetails` is true. Removing every technician without a schedule change sends nothing. Subject: "<organization name>: visit scheduled for <WO-n>" (first schedule), "<organization name>: visit rescheduled for <WO-n>" (schedule change) or "<organization name>: technician update for <WO-n>" (technicians only). Body: "Hi <contact first name or 'there'>," then "Your service visit for <title> is scheduled for <EEE, MMM d> with arrival between <h:mm a> and <h:mm a>." (or "…with arrival at <h:mm a>." for `at_start`), plus "Your technician: <full names, primary first>." when `sendTechnicianDetails` and technicians exist, the organization name and "Questions? Call us at <organization phone>." when set. It never contains the dispatch note, the override reason or technician contact data. A send failure logs only the visit id and failure category and the response stays success. No `notifications` rows are written. The work order preferences are never changed. | Both |
| BR-18 | Recurring occurrences. When visit *k* of a `recurring` work order receives a schedule for the first time (from `unscheduled`), *k* < `recurrence_count`, and no visit *k+1* exists, the same transaction inserts visit *k+1* (`unscheduled`, no schedule) with a `visit_status_history` row (null → `unscheduled`), `visit_checklist_items` copied from the work order tasks as in `create-work-order` BR-18, and `preferred_start`/`preferred_end` = visit *k*'s arrival window local times on the local date of *k* plus the interval: weekly +7 days, biweekly +14 days, monthly +1 calendar month, quarterly +3 calendar months, clamped to the last day of the month, converted in the branch time zone. Rescheduling *k* never moves or creates another occurrence. `UNIQUE(work_order_id, visit_number)` is the backstop; a unique violation is treated as already materialized. No visit beyond `recurrence_count` is created. | Backend |
| BR-19 | Audit. One `visit.dispatched` row per effective save: `entity_type` `visit`, the visit id, organization, the work order `branch_id`, actor. Before/after `{ status, scheduledStart, scheduledEnd, arrivalWindowStart, arrivalWindowEnd, technicianIds, primaryTechnicianId }`. Metadata `{ workOrderId, visitNumber, noteChanged, conflicts: [{ technicianId \| null, code }], overrideReason \| null, notified ("email" \| "none"), materializedVisitId \| null }`. It never contains the note text, customer contact data or the email body. Assignment history is kept in `visit_assignments` (`assigned_at`, `assigned_by_user_id`, `unassigned_at`); status history in `visit_status_history`. | Backend |
| BR-20 | Layout (Design 6). Header "Dispatch calendar", subtitle "Schedule and assign work orders to your team", **Today**, previous/next with the range label ("MMM d – d, yyyy" or "EEE, MMM d, yyyy"), **Day**/**Week** toggle, **Map view** disabled with "Map view isn't available yet.". Filter row: Branch, Team, Skills, Status. Left: **Unscheduled jobs** panel. Center: calendar with legend. Right: drawer **Assign work order** (close button; header; Customer; Schedule with date, **Start time\***, **End time\***, **Preferred arrival window**; **Assign to technicians** multi-select with a **Primary** choice and the checks; **Workload and route impact** per BR-09 without travel minutes; status banner "No scheduling conflicts" (success) or "<n> scheduling conflicts" (warning) with **Justification** required; **Customer notification**; **Internal note (optional)** "Add a note for the technician…"; footer **Cancel** and the primary button). Primary label: technicians selected → "Assign", none → "Schedule", visit already scheduled → "Update", each suffixed " & notify" when BR-17 will send. While the drawer is open the proposed block is drawn dashed in the selected technicians' lanes (or Unassigned) and the calendar moves to the range containing the proposed date. Selecting a card or block opens the drawer; **Cancel** or close with unsaved changes opens the existing discard-changes dialog. Viewers see the drawer read-only with only **Close**. The `/coming-soon/schedule` links in Team ("View schedule"), Skills & availability ("Schedule", "Open dispatch calendar") and the customer appointments card point to `/schedule`. | Frontend |
| BR-21 | Responsive and accessibility. ≥ 1280 px: three areas as Design 6 (panel · calendar · drawer, the drawer overlaying the calendar's right side when open). 768–1279 px: the panel collapses behind an **Unscheduled (<n>)** toggle, the calendar fills the width and scrolls horizontally inside its card, the drawer overlays. < 768 px: Day view by default, filters in a collapsible section, the panel above the calendar, the calendar scrolling inside its card, the drawer full screen with a fixed bottom action bar; no page-level horizontal scroll at 375 px. Cards and blocks are keyboard-focusable buttons whose accessible name includes time, title, WO number and status/conflict; the drawer traps focus and returns it to the opener; the live check results are announced politely; every control keeps a visible focus indicator. | Frontend |
| BR-22 | Schema amendment, as defined in [BR-22 Schema amendment](#br-22-schema-amendment). | Backend |

### Field validation (BR-11)

| Field | Rule | Error (`errors.<key>`) |
| ----- | ---- | ---------------------- |
| `date` | Required, `YYYY-MM-DD`; when `date`, `start` or `end` differ from the stored schedule (or none is stored), today or later in the branch time zone | "Select a date." / "Select today or a later date." |
| `start`, `end` | Required `HH:mm` in 15-minute steps; same local day; `end` > `start`; duration 15 min–12 h; when `date`, `start` or `end` differ from the stored schedule (or none is stored), the start instant must be later than now. A visit whose stored start has passed but has not started can still change technicians, note or notification without changing its schedule | "Select a start time." / "Select an end time." / "End time must be after start time." / "Select a time in the future." |
| `arrivalWindow` | `at_start` (= start), `start_plus_1h`, `start_plus_2h` (default), `around_1h` (start − 1 h to start + 1 h); must stay on the same local day | "Select an arrival window." |
| `technicianIds` | 0–5 distinct ids; BR-12 | "Select up to 5 technicians." · BR-12 text |
| `primaryTechnicianId` | Required when `technicianIds` is non-empty and one of them; null otherwise | "Choose a primary technician." |
| `dispatchNote` | 0–1000 characters after trim (empty → null) | "Note must be 1000 characters or fewer." |
| `overrideReason` | Null, or 10–500 characters after trim | "Enter at least 10 characters." / "Reason must be 500 characters or fewer." |
| `notifyCustomer`, `sendTechnicianDetails` | Booleans, required; BR-17 | "This customer has no email address." |
| `updatedAt` | Required | — |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Visit `unscheduled` | `scheduled` | Dispatch with schedule, no technicians | Manage | BR-11–BR-14 |
| Visit `unscheduled` | `assigned` | Dispatch with schedule and technicians | Manage | BR-11–BR-14 |
| Visit `scheduled` | `assigned` | Technicians added | Manage | BR-12–BR-14 |
| Visit `assigned` | `scheduled` | Every technician removed | Manage | BR-14 |
| Visit `scheduled`/`assigned` | same | Reschedule, reassign, note change | Manage | BR-14 |
| Visit locked statuses | — | Dispatch | Manage | `409 visit_locked` (BR-13) |
| Visit (none) | `unscheduled` (*k+1*) | First schedule of recurring visit *k* | Manage | BR-18 |
| Work order `ready_to_schedule` | `scheduled` | A visit gains a schedule | Manage | BR-16 |
| Work order `scheduled` | `ready_to_schedule` | No non-cancelled visit keeps a schedule | Manage | BR-16 (not reachable through this feature's UI, kept for consistency) |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `visits` (amended): read; update schedule, arrival window, note, status,
    `updated_at`; insert occurrence *k+1*.
  - `visit_assignments` (amended indexes): insert; set `unassigned_at`;
    update `is_primary`.
  - `visit_status_history`, `visit_checklist_items`: insert.
  - `work_orders`: read; `status` between `ready_to_schedule` and
    `scheduled`; `recurrence_frequency`, `recurrence_count`,
    `notify_customer_when_scheduled`, `send_technician_details` read only.
  - `audit_logs`: insert.
  - Read only: `work_order_required_skills`, `work_order_checklist_templates`,
    `work_order_planned_materials` (count), `technician_profiles`,
    `technician_skills`, `skills`, `technician_weekly_availability`,
    `technician_breaks`, `technician_exceptions`, `assessments`, `branches`,
    `organizations`, `organization_users`, `organization_user_branches`,
    `customers`, `customer_contacts`, `properties`.
  - Not written: `notifications`, `visit_time_entries`, `visit_materials`,
    work order preference columns.
- Schema amendment BR-22, confirmed by the user 2026-10-06 (OD-11).
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model
  and generates one migration under `--generate-migration`. Agents never apply
  it. The mapping change requires `database-reviewer`.

### BR-22 Schema amendment

- `visits` adds:
  - `preferred_start timestamptz`, `preferred_end timestamptz` with
    `CHECK ((preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end))`.
  - `arrival_window_start timestamptz`, `arrival_window_end timestamptz` with
    `CHECK ((arrival_window_start IS NULL) = (arrival_window_end IS NULL))` and
    `CHECK (arrival_window_start IS NULL OR (scheduled_start IS NOT NULL AND arrival_window_start <= scheduled_start AND scheduled_start <= arrival_window_end))`.
  - `dispatch_note varchar(1000)`.
  - `CHECK ((scheduled_start IS NULL) = (scheduled_end IS NULL))`.
  - Index `ix_visits_org_status_preferred ON visits(organization_id, status, preferred_start)`.
- `visit_assignments`: drop `UNIQUE(visit_id, technician_id, unassigned_at)`
  (ineffective with NULLs) and add
  `CREATE UNIQUE INDEX ux_visit_assignments_active ON visit_assignments(visit_id, technician_id) WHERE unassigned_at IS NULL` and
  `CREATE UNIQUE INDEX ux_visit_assignments_primary ON visit_assignments(visit_id) WHERE is_primary AND unassigned_at IS NULL`.
- Data: the migration backfills visit #1 `preferred_start`/`preferred_end`
  from its work order. `create-work-order` creation (BR-18 of that spec) also
  copies the work order preferred window to visit #1 from now on, with no other
  observable change.
- `visit_assignments` has no `organization_id`. The application verifies that
  visit and technician belong to the session organization (schema invariant 1
  and 4).

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  query or body carries an organization id; any such field is ignored.
- Client identifiers verified server-side: `visitId` (path, `404`), `branchId`
  (query, `404` outside scope), `technicianIds`/`primaryTechnicianId` (`404`
  foreign or out of scope; `400` per BR-12), Team filter ids (ignored when not
  eligible), `skillId` (`400`).
- A visit, branch or technician of another organization or outside branch
  scope → `404` with no foreign data; lists never include them.
- An override never bypasses tenant/branch isolation, BR-12 or BR-13.
- Policies per BR-01. Responses use `Cache-Control: no-store`.
- Concurrency: visit row lock + `updatedAt`, technician profile row locks in id
  order, the BR-22 partial unique indexes and `UNIQUE(work_order_id,
  visit_number)`.

## API contracts

Contract status: Final. Errors use ProblemDetails; every `409` carries `code`.
Dates `YYYY-MM-DD`, times `HH:mm` (branch local), instants ISO with offset.

`Conflict` = `{ technicianId | null, code ("missing_skills"/"time_off"/"break"/"outside_availability"/"overlap"), from | null, to | null, label }`.

`Check` = `{ passed, code | null, label }` (`code` is a `Conflict` code when
not passed; `label` is the BR-10 drawer text).

`CalendarVisit` = `{ visitId, workOrderId, displayNumber, title, street, status,
start, end, technicianIds, primaryTechnicianId | null, conflicts: Conflict[] }`.

`VisitDispatchDetail` = `{ visitId, visitNumber, recurrence: { count } | null,
status, updatedAt, canManage, isLocked, timezone, workOrder: { id,
displayNumber, title, priority, estimatedDurationMinutes, requiredSkills: [{
id, name }], plannedMaterialsCount }, customer: { name, initials, phone | null,
address, hasEmail }, values: { date | null, start | null, end | null,
arrivalWindow, technicianIds, primaryTechnicianId | null, dispatchNote | null,
notifyCustomer, sendTechnicianDetails }, technicians: [{ id, name, initials,
colorHex | null, primarySkill | null }] }`.

`DispatchBody` = `{ date, start, end, arrivalWindow, technicianIds,
primaryTechnicianId | null, dispatchNote | null, notifyCustomer,
sendTechnicianDetails, overrideReason | null, updatedAt }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/dispatch/options` | — | `200 { branches: [{ id, name, timezone, isMain }], defaultBranchId \| null, skills: [{ id, name }] }` | `403` | Read |
| GET | `/dispatch/calendar` | Query: `branchId`, `view` (`day`/`week`), `date`, `technicianIds[]?`, `skillId?`, `status?` (`all`/`unassigned`/`assigned`/`conflicts`) | `200 { timezone, from, to, days: [date], technicians: [{ id, name, initials, colorHex, primarySkill, tag \| null ("inactive"/"other_branch"), load: { scheduledMinutes, availableMinutes, state ("percent"/"no_availability"/"none") }, days: [{ date, availability: [{ start, end }], breaks: [{ start, end }], timeOff: [{ start, end }] }], assessments: [{ start, end }] }], visits: CalendarVisit[] }` | `400` (`branchId` missing or malformed, `view`, `date`, `skillId`, `status`) · `403` · `404` (branch) | Read |
| GET | `/dispatch/unscheduled` | Query: `branchId`, `filter` (`all`/`today`/`overdue`), `search?`, `skillId?`, `page`, `pageSize` (25, max 100) | `200 { items: [{ visitId, visitNumber, recurrenceCount \| null, workOrderId, displayNumber, title, priority, estimatedDurationMinutes, customerName, address, preferredStart \| null, preferredEnd \| null, isOverdue, skills: [{ id, name }] }], total }` | `400` (`branchId` missing or malformed, `filter`, `search`, `skillId`, paging) · `403` · `404` (branch) | Read |
| GET | `/dispatch/visits/{visitId}` | — | `200 VisitDispatchDetail` | `403` · `404` | Read |
| POST | `/dispatch/visits/{visitId}/evaluation` | `{ date, start, end, technicianIds, primaryTechnicianId \| null }` | `200 { conflicts: Conflict[], skills: Check, checks: [{ technicianId, availability: Check, overlap: Check }], impact: [{ technicianId, jobs, scheduledMinutes, availableMinutes, previous \| null, next \| null }], ranking: [{ technicianId, bestMatch, conflictCount, loadPercent \| null }] }` | `400` (BR-11/BR-12) · `403` · `404` · `409 visit_locked` | Manage |
| PUT | `/dispatch/visits/{visitId}` | `DispatchBody` | `200 VisitDispatchResult = { visit: VisitDispatchDetail, workOrderStatus, notified, materializedVisitId \| null }` | `400` (BR-11 keys) · `403` · `404` · `409` (`visit_locked`, `visit_changed`, `scheduling_conflicts` with `conflicts: Conflict[]`) | Manage |
| GET | `/work-orders`, `/work-orders/{id}` | Unchanged | Status label "Scheduled"; visits list shows each visit status (BR-16) | Unchanged | Read (unchanged) |

- Internal: existing `IEmailSender`, unchanged; a visit notification composer
  follows the `schedule-assessment` composer precedent.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/schedule` | Skeleton filters, panel cards and lanes | No branches: "No branches available.". No technicians: "No active technicians in this branch." with **Go to Team**. No visits in range: lanes render empty | "We couldn't load the schedule. Try again." with **Retry**; `404` branch → falls back to the default branch | Technician/Accounting: forbidden state, no further calls | BR-04, BR-05 |
| Unscheduled panel | Card skeletons; **Load more** progress | All: "No unscheduled visits."; Today: "No visits due today."; Overdue: "No overdue visits."; search: "No visits match your search." | "We couldn't load unscheduled visits." with **Retry** | — | BR-07 |
| Drawer | Section skeletons; checks show progress while evaluating | No technicians in branch: "No active technicians in this branch." | Load `404`: toast "This visit isn't available." and drawer closes. Evaluation failure: "We couldn't check availability." with **Retry**; save stays enabled | Viewer: read-only, **Close** only. Locked: read-only with the BR-13 note | BR-08, BR-09 |
| Drawer save | Primary button progress; inputs, **Cancel** and close disabled; one request at a time | — | `400`: inline errors, focus first. `409 scheduling_conflicts`: conflicts refresh, **Justification** focused with "Enter a reason to confirm despite the conflicts.". `409 visit_changed`: "This visit was changed by someone else. Reload to see the latest version." with **Reload**, input kept. `409 visit_locked`: BR-13 note, drawer read-only. Other: "We couldn't save this visit. Try again." with input kept | — | Drawer closes; calendar and panel refresh; toast "Visit scheduled." / "Visit assigned." / "Visit updated.", plus " Customer notified by email." when sent |

- Mockups: `design/assets/6-design.png` (Approved; primary reference). Data in
  it are examples. Deviations fixed by this spec: no travel blocks or travel
  minutes, **Map view** disabled, no **New job**, "Planned materials" instead
  of "Materials available", SMS disabled, multi-technician selection, the
  **Unassigned** lane and the justification field.
- Responsive and accessibility: BR-21.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Visit, branch or technician of another organization or out of scope | `404` | None |
| Inactive or other-branch technician, too many/duplicate ids, wrong primary | `400 errors.technicianIds` / `errors.primaryTechnicianId` | None |
| Past date/time, invalid times or window, note or reason too long | `400 errors.<key>` | None |
| Email requested without customer email | `400 errors.notifyCustomer` | None |
| Visit started/closed or work order closed | `409 visit_locked` | None |
| Stale `updatedAt` | `409 visit_changed` | None |
| Conflicts without justification | `409 scheduling_conflicts` + list | None |
| Conflicts with justification | `200`; conflicts and reason in audit | Saved |
| Unchanged request | `200`, no audit, no email | None |
| Email send failure | `200`; visit id and category logged | Saved |
| Role without permission | `403` | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | Users of every role | They open **Schedule** from the sidebar and the Team, Skills & availability and customer appointments links | Links open `/schedule` with **Schedule** active. Owner, Operations Manager, Dispatcher and Viewer see the page; Technician and Accounting see the forbidden state and the API returns `403` |
| AC-02 | A branch with technicians, availability, breaks, time off, scheduled and assigned visits, a multi-technician visit, an assessment and an unassigned scheduled visit | The Week view of the default branch is opened | Lanes, the Unassigned lane, blocks with time, title, WO number and street, the assessment block, shading and legend match BR-05 and Design 6. The multi-technician visit appears in each lane. Load figures and bar tones match BR-05. Empty weekend columns are collapsed |
| AC-03 | The same data | Day view, **Today**, previous/next and a reload are used | Day view shows the hour axis and current-time line; navigation moves by a day or week; `branch`, `view` and `date` survive reload. Times use the branch time zone |
| AC-04 | Two branches in different time zones and a Dispatcher scoped to one | Branch, Team, Skills and Status filters are applied, including With conflicts | Branch lists only in-scope branches; Team and Skills limit lanes and cards; Status limits blocks; With conflicts lists exactly the visits that currently have BR-10 conflicts, including one made conflicting by a later time-off exception. Load figures do not change with filters. An inactive or foreign `skillId` returns `400` |
| AC-05 | Unscheduled visits that are overdue, due today, future, without preferred window, of another branch and of a draft work order | The panel is used with All, Today, Overdue, a search and **Load more** | Each filter lists exactly the BR-07 set in BR-07 order with the count; cards show BR-07 content including "Visit k of n" and "Overdue"; other-branch and draft visits never appear; the empty texts show |
| AC-06 | An unscheduled visit with a preferred window and duration | A card is selected | The drawer shows BR-08 content and prefill (end = start + duration, window start + 2 h), the proposed block is drawn dashed in the Unassigned lane, and nothing is written |
| AC-07 | The drawer of a visit whose work order requires two skills | Technicians, date and times are changed | Within 300 ms of the change the checks, impact lines (jobs, hours after assignment, previous and next job) and Best match update per BR-09/BR-10; no travel minutes appear; "Planned materials: n" is informational; the proposed block follows the selected lanes and date; results are announced |
| AC-08 | An unscheduled visit without conflicts | It is saved with a schedule and no technicians, then with two technicians, then all technicians are removed | Statuses go `unscheduled` → `scheduled` → `assigned` → `scheduled` with one `visit_status_history` row per change; assignment rows record `assigned_at`, actor and `unassigned_at`; exactly one active primary; one `visit.dispatched` audit row per save matching BR-19 without note text |
| AC-09 | A `ready_to_schedule` work order | Its first visit is scheduled | The work order becomes `scheduled` with one `work_order.status_changed` audit row; `/jobs` shows "Scheduled" and the job detail shows "Visit #1 · Scheduled" |
| AC-10 | Each conflict type (table-driven: missing skills by the selection, time off, break, outside availability, overlapping visit, overlapping assessment) | Dispatch is posted without, then with, a justification | Without: `409 scheduling_conflicts` with the matching codes and no write; the drawer focuses **Justification**. With a valid reason: `200`, the visit is saved, and the audit stores the conflicts and the reason. A reason of 9 characters → `400`. A selection whose combined skills cover the requirement has no `missing_skills` |
| AC-11 | A technician of another organization, one outside the Dispatcher's scope, an inactive one and one of another in-scope branch | Each is sent in evaluation and dispatch, with and without `overrideReason` | The first two return `404` with no foreign data; the others return `400 errors.technicianIds`; nothing is written, regardless of the override |
| AC-12 | Visits `in_progress`, `completed`, `cancelled`, and a visit of a cancelled work order | Dispatch is posted with an override, and the drawer is opened | Each returns `409 visit_locked` with no change; the drawer is read-only with the BR-13 note |
| AC-13 | The BR-11 table, and an `assigned` visit whose stored start passed without starting | Each invalid value is posted (table-driven), including a past start on a schedule change and `notifyCustomer` without customer email; then only the passed visit's technicians are changed; the drawer is opened without a date | Each invalid value returns `400` with the listed key and message, inline with focus on the first error, and nothing changes. The technician-only change on the passed visit succeeds. Without date and times, **Assign to technicians** is disabled with the BR-15 text |
| AC-14 | A saved visit | It is saved with a stale `updatedAt`, and the same body is saved twice | Stale → `409 visit_changed`, reload message, input kept. The identical repeat returns `200` with no audit, history or email |
| AC-15 | Two concurrent dispatches assigning the same technician to overlapping slots without justification | Both run | Exactly one succeeds; the other returns `409 scheduling_conflicts`; the technician has one active assignment in that slot; duplicate active assignment or primary rows are rejected by the BR-22 indexes |
| AC-16 | A weekly recurring work order with 3 occurrences | Visit 1 is scheduled, rescheduled, saved again concurrently, then visits 2 and 3 are scheduled | Exactly one visit 2 exists after the first schedule, `unscheduled`, with preferred window = visit 1 window + 7 days in branch time, checklist items and history; rescheduling or repeating creates nothing; scheduling visit 3 creates no visit 4. `materializedVisitId` and the audit metadata reflect each creation |
| AC-17 | Monthly and quarterly recurring orders whose occurrence falls on Jan 31 and across a DST change | The next occurrence is materialized | Dates clamp to the month's last day and keep the local window time across DST |
| AC-18 | A customer with email, the drawer defaults from the work order | A first schedule with technician details, a reschedule, a technician change with details on, a technician change with details off, removal of every technician with details on, a note-only change, a save with email unchecked, and a send failure occur | Exactly one email per BR-17 case (first schedule, reschedule, technician change with details on) and none for the others; subjects and body match BR-17, with technician names only when details are on, never the note or reason; the failure still returns `200` and logs only the visit id and category; no `notifications` rows; work order preferences unchanged |
| AC-19 | A customer without email | The drawer opens | Email is unchecked and disabled with "This customer has no email address."; SMS is always disabled with "SMS isn't available yet."; the primary label has no " & notify" |
| AC-20 | The drawer in each mode | Technicians, email and schedule are combined | The primary label follows BR-20 ("Assign", "Schedule", "Update", with " & notify" exactly when an email will be sent); the banner shows "No scheduling conflicts" or "<n> scheduling conflicts" |
| AC-21 | A Viewer | They open the calendar, panel and a visit | Read data render; the drawer is read-only with **Close** only; evaluation and dispatch return `403` |
| AC-22 | An Owner of another organization and a Dispatcher of branch A | They request the calendar, panel and visit detail of branch B and of the other organization | `404` for branch and visit; lists never contain foreign or out-of-scope visits or technicians; out-of-scope Team ids are ignored |
| AC-23 | A drawer with unsaved changes | **Cancel**, close, another card and a sidebar link are used; the save button is double-clicked | Each opens the discard dialog; confirming discards. One request is sent and the controls stay disabled while pending. Success closes the drawer, refreshes calendar and panel and shows the BR-20 toast |
| AC-24 | Viewports of 1440, 1024 and 375 px | The page is used by keyboard | 1440 shows the Design 6 three-area layout; 1024 collapses the panel behind its toggle; 375 shows Day view, collapsible filters, full-screen drawer with bottom bar, no page-level horizontal scroll. Cards and blocks are focusable with descriptive names, the drawer traps and returns focus, focus is always visible |
| AC-25 | Existing visit #1 rows and a newly created work order with a preferred window | The migration is generated and a work order is created | The migration contains the BR-22 columns, checks, index changes and the visit #1 backfill; the new visit #1 carries the work order's preferred window and appears under Today/Overdue accordingly |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Next-occurrence calculation (weekly, biweekly, monthly/quarterly clamp, DST) and slot conflict classification against availability, breaks, time off and commitments | AC-10, AC-16, AC-17 |
| Backend integration | Reads: calendar lanes, load, current conflicts and With conflicts; unscheduled filters and order | AC-02, AC-04, AC-05 |
| Backend integration | Dispatch lifecycle: statuses, assignment and status history, primary, work order status, audit, unchanged repeat | AC-08, AC-09, AC-14 |
| Backend integration | Conflicts table-driven without/with justification; hard technician and locked-visit rules unaffected by override | AC-10, AC-11, AC-12 |
| Backend integration | Validation table-driven (BR-11) | AC-13 |
| Backend integration | Recurrence materialization and idempotency, including concurrent saves | AC-16 |
| Backend integration | Concurrency: overlapping concurrent assignments; partial unique indexes | AC-15 |
| Backend integration | Notification cases after commit with a fake `IEmailSender`, including failure | AC-18 |
| Authorization/tenant isolation | One cross-organization `404` (visit, technician, branch), Dispatcher out-of-scope `404` and list exclusion, `403` for Viewer on mutations and for Technician/Accounting | AC-01, AC-11, AC-21, AC-22 |
| Frontend component/service | Calendar rendering of lanes, blocks, load and views; panel filters and search; drawer prefill, evaluation display, justification requirement, button labels, notification toggles, `409` handling and discard guard; Viewer read-only; repointed links | AC-01, AC-03, AC-06, AC-07, AC-19, AC-20, AC-21, AC-23 |
| Persistence review | BR-22 mapping and migration via `database-reviewer` (generated, never applied) | AC-25 |
| User visual QA (manual) | Design 6 fidelity and responsive layouts | AC-02, AC-24 |

Backend integration has 8 groups including authorization (default ceiling).
The recurrence and conflict risk justify the two unit groups. Agents run no
browser automation or Playwright.

## Dependencies

- `create-work-order` (AUDITED): work orders, visit #1, tasks, recurrence and
  communication preferences; its create path copies the preferred window to
  visit #1 (BR-22).
- `team-technician-management` (AUDITED): profiles, availability and load
  formulas (BR-03, BR-04, BR-06).
- `technician-skills-availability` (AUDITED): weekly availability, breaks,
  exceptions and skills.
- `schedule-assessment` (AUDITED): assessment commitments, conflict precedent,
  `IEmailSender` composer and profile-row locking.
- `requests-pipeline` (AUDITED): branch scope.
- `authenticated-app-shell` (AUDITED): sidebar, Coming soon, forbidden and
  not-found states; the shared discard-changes dialog.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Times use 15-minute steps; visits do not cross local midnight (confirmed 2026-10-06). |
| AS-02 | Default end = start + estimated duration, or 60 minutes when null (confirmed 2026-10-06). |
| AS-03 | Arrival window presets are the four BR-11 values, default start + 2 h (confirmed 2026-10-06). |
| AS-04 | Load bar thresholds < 60 % teal, 60–100 % amber, > 100 % red (confirmed 2026-10-06). |
| AS-05 | Week runs Monday–Sunday with empty weekends collapsed; Day axis defaults to 8:00 AM–5:00 PM (confirmed 2026-10-06). |
| AS-06 | The Unassigned lane holds scheduled visits without technicians (confirmed 2026-10-06). |
| AS-07 | Technician lanes use initials and the profile `color_hex` when set. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Visit states | Independent schedule/assignment · assign without schedule | No | Schedule required for technicians; `unscheduled`/`scheduled`/`assigned` (BR-15) — user 2026-10-06 |
| OD-02 | Unschedule | Unassign only · add Unschedule | No | Unassign only; no return to `unscheduled` — user 2026-10-06 |
| OD-03 | Work order aggregate | Any scheduled visit · all scheduled | No | Any non-cancelled scheduled visit (BR-16) — user 2026-10-06 |
| OD-04 | Recurrence | Next on first schedule · horizon | No | Next occurrence on first schedule, idempotent (BR-18) — user 2026-10-06 |
| OD-05 | Conflicts and override | All operational conflicts overridable · time off hard | No | Overridable with 10–500 character justification by Manage roles; foreign/out-of-scope technician `404`; inactive/other-branch `400`; started/closed visit `409`; past time `400`; override never bypasses isolation or locks (BR-10–BR-14) — user 2026-10-06 |
| OD-06 | Multiple technicians | 1–5, union of skills · each needs all skills | No | 1–5 with one primary; union covers skills; availability per technician (BR-10, BR-12) — user 2026-10-06 |
| OD-07 | Notifications | Customer email only · plus technicians | No | Customer email after commit; SMS disabled; no `notifications` rows; arrival reminder out of scope, preferences kept (BR-17) — user 2026-10-06 |
| OD-08 | Map view, travel, materials, New job | As proposed · other | No | Map view disabled; no travel; "Planned materials" informational; no New job (BR-09, BR-20) — user 2026-10-06 |
| OD-09 | Permissions | Viewer reads · Viewer `403` | No | Manage = Owner/OM/Dispatcher; Viewer reads (BR-01) — user 2026-10-06 |
| OD-10 | Filters and time zone | Required branch, Team as technicians · Team disabled | No | Required branch with its time zone; Team = technician multi-select; With conflicts computed from current state (BR-03, BR-06) — user 2026-10-06 |
| OD-11 | Schema | Amendment as proposed · compute window on read | No | BR-22, replacing the ineffective unique constraint with partial indexes — user 2026-10-06 |
| OD-12 | Existing schedule links | Repoint · keep | No | Repoint to `/schedule` (BR-20) — user 2026-10-06 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-01, AC-11, AC-21, AC-22 |
| FR-03 | AC-02, AC-03, AC-04 |
| FR-04 | AC-05 |
| FR-05 | AC-06, AC-07 |
| FR-06 | AC-10, AC-11, AC-12, AC-13 |
| FR-07 | AC-08, AC-09, AC-14, AC-15 |
| FR-08 | AC-16, AC-17 |
| FR-09 | AC-18, AC-19 |
| FR-10 | AC-20, AC-23, AC-24 |
| FR-11 | AC-25 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-06 | — → DRAFT | Created |
| 2026-10-06 | DRAFT → DRAFT | Revised after validation: technician-without-schedule rule made UI-only (BR-15, AC-13); past-date/time rules apply only to schedule changes (BR-11, AC-13); no technician-change email when every technician is removed (BR-17, AC-18); `branchId` 400s and `Check` shape in contracts; Jobs status display added to Scope |
| 2026-10-06 | DRAFT → APPROVED | Approved by user via /spec approve |
