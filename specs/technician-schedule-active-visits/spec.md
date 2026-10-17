# Technician schedule and active visits

| Field    | Value                               |
| -------- | ----------------------------------- |
| Feature  | `technician-schedule-active-visits` |
| Type     | Full-stack                          |
| Status   | APPROVED                            |
| Created  | 2026-10-17                          |
| Updated  | 2026-10-17                          |
| Approved | 2026-10-17                          |

## Context and objective

Technicians only see today's visits. A visit left `on_the_way`, `in_progress`
or `paused` on an earlier day is invisible, yet it blocks **Start travel**
with a dead-end `409 another_visit_active`. This feature adds the **Schedule**
destination (approved mobile design) with week navigation over past and
future days. It redefines the active-visit guard on real status and open time
entries, returns the blocking visit so the technician can open it, and
surfaces unresolved past active visits on Today and Schedule. It also lets
Manage roles cancel non-completed visits from the dispatch drawer with a
reason. Visits are never hard-deleted.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Technician (`technician`) with an `active` linked profile | Read their own schedule for any week; open the read-only detail of their actively assigned visits, including `cancelled` ones; open the blocking active visit | Start travel on a past or future visit (BR-07 of `mobile-job-details` unchanged); cancel, delete or reschedule visits; see other technicians' visits |
| Technician with an `inactive`/`suspended` profile, or without linked profile | — | Every technician endpoint (`403 technician_inactive` / `404 technician_profile_not_linked`) |
| Owner (`owner`), Operations Manager (`operations_manager`) | Cancel visits of every branch per BR-12; reschedule `scheduled`/`assigned` visits through the existing dispatch flow | Cancel `unscheduled`, `completed`, `needs_correction`, `approved` or `cancelled` visits |
| Dispatcher (`dispatcher`) | The same as Owner within branch scope (`dispatch-calendar` BR-02) | Anything outside branch scope (`404`) |
| Viewer (`viewer`) | Read the dispatch drawer, including the cancellation of a cancelled visit | Cancel (`403`); no **Cancel visit** control |
| Accounting, unknown roles | — | Every endpoint of this feature (`403`) |

No permission matrix grants technicians cancellation or deletion; none is added.

## Scope

- Technician route `/today/schedule` (Schedule) replacing the Schedule Coming
  soon destination, and `GET /technician/schedule`.
- Active-visit guard redefinition and the `activeVisit` payload on
  `409 another_visit_active`, with the **Open active visit** UI.
- Unresolved past active visits banner on `/today` and `/today/schedule`.
- Read-only technician detail of `cancelled` visits and the "Ask dispatch to
  reschedule" state for past `scheduled`/`assigned` visits.
- `POST /dispatch/visits/{visitId}/cancel` and **Cancel visit** in the dispatch
  drawer, creating a replacement `unscheduled` visit for one-time work orders (BR-14).
- Bottom navigation order and highlighting (BR-20).

## Non-goals

- Hard deletion of visits; technician-created visits; technician cancellation.
- Cancelling a whole work order (remains a separate operation, not delivered
  here); cancelling `unscheduled` visits.
- Rescheduling started visits; changing `dispatch-calendar` BR-13 locks.
- Starting travel on a non-today visit; any change to `mobile-job-details`
  BR-07 other than the active-visit guard (BR-05) and its payload.
- Time summary and messages (`technician-time-summary`, `technician-messages`).
- Customer emails or `notifications` rows for cancellation.
- Offline mode, service worker or data caching.

## User flow

1. The technician opens **Schedule**; the current week loads with today
   selected.
2. They move by week, select any day and see that day's visits; tapping a
   visit opens `/today/visits/:visitId`.
3. A past active visit shows the unresolved banner on Today and Schedule;
   **Open active visit** opens it so the technician can finish it.
4. Starting travel while another visit is active shows the blocking visit and
   **Open active visit** instead of a dead-end error.
5. A dispatcher opens a past visit in the dispatch drawer, chooses **Cancel
   visit**, enters a reason and confirms; the visit becomes `cancelled`, a
   replacement `unscheduled` visit appears in Unscheduled jobs and the
   technician sees the cancelled visit read-only with the reason.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every technician endpoint must resolve organization, membership and profile from the session and return only visits actively assigned to the caller (BR-01). |
| FR-02 | `GET /technician/schedule` must return the requested Monday–Sunday week, its per-day counts and durations and its visits per BR-02–BR-04. |
| FR-03 | The Schedule page must render the approved design with week navigation, day selection, timeline cards, daily summary, time zone notice and states per BR-15–BR-18. |
| FR-04 | Past `scheduled`/`assigned` visits must be shown as needing reschedule and must never be treated as active work (BR-06). |
| FR-05 | The active-visit guard must use status and open time entries only and return the blocking visit (BR-05). |
| FR-06 | Every blocking message must show the active visit and **Open active visit** (BR-19). |
| FR-07 | Unresolved past active visits must be returned and surfaced on Today and Schedule (BR-08, BR-17). |
| FR-08 | The technician detail must serve `cancelled` visits read-only with the cancellation reason (BR-09). |
| FR-09 | Manage roles must cancel eligible visits with a reason, atomically closing time entries, writing history and audit, creating the replacement visit for one-time work orders (skipping recurring occurrences) and updating the work order per BR-10–BR-14. |
| FR-10 | The dispatch drawer must offer **Cancel visit** and show cancellations per BR-21. |
| FR-11 | The technician bottom navigation must follow BR-20. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Technician authorization reuses `technician-todays-jobs` BR-01–BR-03: policy TechnicianSelf; profile resolution with `technician_profile_not_linked` / `technician_inactive` before any read; visibility by an active assignment (`unassigned_at IS NULL`) of the caller's profile in the session organization. No technician or organization id is accepted; extra query parameters are ignored. | Backend |
| BR-02 | Week and time zone: the zone is the profile branch `timezone`, falling back to `organizations.timezone` (`technician-todays-jobs` BR-04). `weekStart` is an ISO date that must be a Monday between 2000-01-03 and 2099-12-28; absent → the Monday of the current local week; otherwise `400 errors.weekStart` "Choose a valid week.". The week is `[weekStart 00:00, weekStart + 7 days 00:00)` local, DST-aware. A visit belongs to the local day containing its `scheduled_start`. | Backend |
| BR-03 | Schedule content: the caller's actively assigned visits with `scheduled_start` in the week and status other than `unscheduled`, including `cancelled`, ordered by `scheduled_start`, work order number, visit number. Each `ScheduleVisit` carries start, end, `durationMinutes` (`scheduled_end − scheduled_start`), status, title, display number, customer display name and `customerType`, one-line address and `needsReschedule` (BR-06). | Backend |
| BR-04 | Day summary: for each of the 7 days, `visitCount` and `scheduledMinutes` over that day's visits excluding `cancelled`. UI text "<n> job(s) · <duration>" with duration "<h>h", "<h>.<d>h" one decimal with trailing ".0" dropped (as `technician-todays-jobs` BR-07); "0 jobs" when none. | Both |
| BR-05 | Active visit: a visit V other than the target, in the session organization, actively assigned to the caller's profile (primary or not) and not `cancelled`, is active when V's status is `on_the_way`, `in_progress` or `paused`, **or** V has a `visit_time_entries` row of the caller's profile with `ended_at IS NULL`. Scheduled date never makes a visit active; a past `scheduled`/`assigned` visit without an open entry is never active. Start travel's `409 another_visit_active` is returned only for this condition and includes `activeVisit = { visitId, displayNumber, title, status, scheduledStart \| null }` of the active visit with the earliest `scheduled_start`, then lowest work order number and visit number. The message stays "You're already traveling to or working on another job.". This supersedes the `on_the_way`/`in_progress` condition of `mobile-job-details` BR-07; its guard order, profile lock and no-write behavior (BR-07, BR-10) are unchanged. | Backend |
| BR-06 | Past not-started visits: a visit with status `scheduled` or `assigned` whose `scheduled_start` is before the current local day start has `needsReschedule = true`. It is shown with "Ask dispatch to reschedule" on its schedule card and detail; the detail shows no **Start travel** (replacing the travel controls of `mobile-job-details` BR-14 for that case). Start travel on it keeps returning `409 visit_not_today`. | Both |
| BR-07 | No date-based guard is added to arrive, start job, pause, resume or completion; past active visits can be finished as today's. | Backend |
| BR-08 | Unresolved past active visits: `GET /technician/today` and `GET /technician/schedule` (additive) return `unresolvedVisits: ActiveVisitSummary[]` = the caller's active visits (BR-05 definition without the target exclusion) whose `scheduled_start` is before the current local day start, ordered as BR-05. `ActiveVisitSummary = { visitId, displayNumber, title, status, scheduledStart }`. | Backend |
| BR-09 | Cancelled detail: `GET /technician/visits/{visitId}` returns `cancelled` visits that are still actively assigned to the caller (superseding the `cancelled` exclusion of `technician-todays-jobs` BR-11 and `mobile-job-details` BR-01 for this read only), with additive `cancellation = { reason, cancelledAt } \| null` from the visit's latest `visit_status_history` row with `to_status = 'cancelled'`, and `needsReschedule`. The page renders read-only: status "Cancelled", the text "This visit was cancelled on <EEE, MMM d> · <h:mm a>." and "Reason: <reason>", no travel, job, task, material, photo, note or completion controls. Every mutation on a `cancelled` visit keeps returning `409 visit_status_invalid`. `unscheduled` visits stay `404`. Completed and approved visits remain read-only as today. | Both |
| BR-10 | Cancel authorization: policy Manage of `dispatch-calendar` (`owner`, `operations_manager`, `dispatcher`), branch scope per `dispatch-calendar` BR-02; out of scope, other organization or nonexistent → identical `404`. Viewer and other roles → `403`. | Backend |
| BR-11 | Cancel input: `reason` trimmed, 10–500 characters (`400 errors.reason` "Enter a reason between 10 and 500 characters."); `updatedAt` must equal the visit's `updated_at` (`409 visit_changed`, as `dispatch-calendar`). Eligible statuses: `scheduled`, `assigned`, `on_the_way`, `in_progress`, `paused`, on any date. Other visit statuses, or a work order `cancelled`, `completed` or `approved_for_billing` → `409 visit_not_cancellable` "This visit can't be cancelled in its current state.". | Backend |
| BR-12 | Cancel effect, one transaction, locking the actively assigned technician profile rows in id order, then the visit row, then re-reading state: (a) every open `visit_time_entries` row of the visit gets `ended_at = now` (strictly after `started_at` per `mobile-job-completion` BR-08); a closed `pause` entry adds its whole seconds to `visits.pause_seconds`; (b) `visits.status = cancelled`, `updated_at = now`; schedule, assignments, checklist, materials, evidence and notes are unchanged; (c) one `visit_status_history` row `<old> → cancelled` with `changed_by_user_id` = actor and `reason`; (d) BR-14 replacement and BR-13 work order rule; (e) one `visit.cancelled` audit row (`entity_type` `visit`, visit id, organization, work order `branch_id`, actor, before/after `{ status }`, metadata `{ workOrderId, visitNumber, replacementVisitId \| null, closedTimeEntries }`); the reason is stored only in the history row, never in audit metadata or logs. No email and no `notifications` row. | Backend |
| BR-13 | Work order after cancel: when its status is `scheduled` or `in_progress` and no non-cancelled visit other than the replacement has status `scheduled`, `assigned`, `on_the_way`, `in_progress` or `paused`, it becomes `ready_to_schedule` with one `work_order.status_changed` audit row (metadata `{ visitId }`); otherwise unchanged. | Backend |
| BR-14 | Replacement visit, only for `one_time` work orders: insert one visit with `visit_number` = current maximum of the work order + 1, status `unscheduled`, no schedule, arrival window, preferred window or dispatch note, one `visit_status_history` row (null → `unscheduled`), and a copy of the cancelled visit's `visit_checklist_items` with `is_completed = false` (label, `is_required`, `template_item_id`, `sort_order`; notes and completion fields empty). Completed items, materials, evidence, notes and time entries stay on the cancelled visit as read-only history. For `recurring` work orders no replacement is created: the cancelled occurrence is skipped, every other occurrence keeps its original `visit_number`, and `dispatch-calendar` BR-18 and `mobile-job-completion` BR-09 apply unchanged (`replacementVisitId` null). | Backend |
| BR-15 | Schedule header and week strip: page title "My schedule"; previous/next week buttons ("Previous week", "Next week"); seven day buttons "EEE" and day number; the selected day is highlighted and exposed as selected; today is marked "Today" to assistive technology. Initial week = current; initial day = today. Changing week selects the same weekday in the new week. Any past or future day is selectable. | Frontend |
| BR-16 | Day view: heading "EEEE, MMM d"; summary chip BR-04; a vertical timeline of the day's visits in BR-03 order: time "h:mm a" on the rail; card with status chip (`technician-todays-jobs` BR-13 texts plus `cancelled` "Cancelled"), duration ("1h 30m", "2h", "45m"), title, "#<prefix>-<n>", customer line with person icon (`person`) or building icon (`company`), address line 1 and "city, state postal"; "Ask dispatch to reschedule" when `needsReschedule`. The whole card is a link to `/today/visits/:visitId`. Cancelled cards are visually muted with the "Cancelled" text. Notice at the bottom: "Your schedule is shown in your branch time zone." All times in the response zone. | Frontend |
| BR-17 | Unresolved banner on `/today` (above the greeting metrics) and `/today/schedule` (above the week strip) when `unresolvedVisits` is not empty: "You have an unfinished job from <EEE, MMM d>" with "<title> · #<prefix>-<n> · <status label>" for the first item, "+<n> more unfinished jobs" when more, and **Open active visit** → `/today/visits/:visitId` of the first. Announced as a status region, not by color alone. Not shown for past `scheduled`/`assigned` visits. | Frontend |
| BR-18 | Schedule states: loading skeleton for strip, summary and cards; empty day "No jobs scheduled for this day." with navigation still enabled; load failure "We couldn't load your schedule. Check your connection and try again." with **Retry**; a failed week change keeps the previous week's data and selection visible with the inline error and Retry; one request at a time per week, stale responses ignored. Unlinked/inactive/forbidden states as `/today`. | Frontend |
| BR-19 | Blocking UI: every `409 another_visit_active` (next job card on Today, job page action bar) shows the message plus a card with the active visit's title, "#<prefix>-<n>", status label and "EEE, MMM d · h:mm a" (or no date when `scheduledStart` is null) and **Open active visit** → `/today/visits/:activeVisitId`. Focus moves to the message; no dead-end text without the action. | Frontend |
| BR-20 | Technician bottom navigation, in order: **Today** (`/today` and its job pages), **Schedule** (`/today/schedule`), **Time**, **Messages**, **More**. Time and Messages keep their Coming soon destinations until their specs replace them. The current destination is visually highlighted and has `aria-current="page"`; Today is not current on `/today/schedule`. The `/today/soon/schedule` Coming soon route redirects to `/today/schedule`. Today, More (notifications, Account settings, Help & support, Sign out) and top bar behavior are unchanged; top bar title "Schedule" on `/today/schedule`. | Frontend |
| BR-21 | Dispatch drawer: for a visit with an eligible status (BR-11) and Manage role, a **Cancel visit** action (also in the read-only drawer of started visits). It opens a dialog "Cancel visit <#WO-n> · Visit <k>?" with the text "The visit stays in history. A new unscheduled visit will be created for pending work." (one-time) or "The visit stays in history. This occurrence will be skipped; other occurrences don't change." (recurring, per `cancelCreatesReplacement`), a required **Reason** field with counter "<n>/500", **Keep visit** and **Cancel visit** (danger). Client validation per BR-11; pending blocks repeats; success closes the drawer, refreshes calendar and Unscheduled panel and shows "Visit cancelled. A new unscheduled visit was created." (one-time) or "Visit cancelled. This occurrence was skipped." (recurring); `409 visit_changed` shows the existing reload message; `409 visit_not_cancellable` shows its message and reloads. A `cancelled` visit's drawer shows "Cancelled on <date time> by <name>" and the reason, read-only. Viewer sees no action. | Frontend |
| BR-22 | Responsive and accessibility: `technician-todays-jobs` BR-19 applies; the week strip fits 375 px without page-level horizontal scroll; day buttons and cards are ≥ 44 × 44 px; the timeline is an ordered list; status never by color alone; the cancel dialog traps and returns focus. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| `scheduled`, `assigned`, `on_the_way`, `in_progress`, `paused` | `cancelled` | Cancel visit | Owner, Operations Manager, Dispatcher (scope) | BR-10, BR-11; `updatedAt` match |
| — | `unscheduled` (replacement) | Cancel visit | Same | BR-14; `one_time` work orders only |
| Work order `scheduled`/`in_progress` | `ready_to_schedule` | Cancel visit | Same | BR-13 |
| `cancelled`, `completed`, `needs_correction`, `approved`, `unscheduled` | — | Cancel visit | — | `409 visit_not_cancellable` |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): tables of
  `technician-todays-jobs`, plus `visit_time_entries` (`technician_id`,
  `ended_at`), `visit_status_history` (`to_status`, `reason`, `changed_at`,
  `changed_by_user_id`), `customers` (`customer_type`), `work_orders`
  (`status`, `job_type`, `recurrence_count`).
- Written: `visits` (`status`, `pause_seconds`, `updated_at`; replacement
  insert), `visit_time_entries` (`ended_at`), `visit_status_history`,
  `visit_checklist_items` (replacement copies), `work_orders` (`status`,
  `updated_at` per BR-13), `audit_logs`.
- Not written: `visit_assignments`, `visit_materials`, `visit_evidence`,
  `notifications`. No row is ever deleted.
- Schema amendments required: None.

## Tenant isolation and authorization

- Organization, membership and profile come from the session (BR-01); staff
  scope per `dispatch-calendar` BR-02.
- Client-provided identifiers: `visitId` (path), authorized per BR-01 for
  technicians and per BR-10 for staff; otherwise identical `404`.
  `weekStart` is a date only. No organization or technician id is accepted.
- `activeVisit` and `unresolvedVisits` contain only visits actively assigned
  to the caller in the session organization.
- Responses use `Cache-Control: no-store`. Logs never contain cancellation
  reasons, addresses or customer names.

## API contracts

Contract status: Final. Errors use ProblemDetails with `code` where listed.
Common technician errors: `403` (role; `technician_inactive`) · `404
technician_profile_not_linked`.

`ScheduleVisit = { visitId, displayNumber, title, status, start, end,
durationMinutes, customerName, customerType ("person" | "company"), address: {
line1, city, stateRegion | null, postalCode | null }, needsReschedule }`.
`ActiveVisitSummary = { visitId, displayNumber, title, status, scheduledStart |
null }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/technician/schedule` | Query `weekStart?` | `200 { timezone, today, weekStart, days: [{ date, visitCount, scheduledMinutes }], visits: ScheduleVisit[], unresolvedVisits: ActiveVisitSummary[] }` | `400 errors.weekStart` · common | TechnicianSelf |
| GET | `/technician/today` | Unchanged | Additive `unresolvedVisits: ActiveVisitSummary[]` | Unchanged | TechnicianSelf |
| GET | `/technician/visits/{visitId}` | Unchanged | Additive `cancellation \| null`, `needsReschedule`; `cancelled` visits served (BR-09) | Unchanged | TechnicianSelf |
| POST | `/technician/visits/{visitId}/start-travel` | Unchanged | Unchanged | `409 another_visit_active` gains `activeVisit: ActiveVisitSummary` (BR-05); others unchanged | TechnicianSelf |
| POST | `/dispatch/visits/{visitId}/cancel` | `{ reason, updatedAt }` | `200 { visit: VisitDispatchDetail, replacementVisitId \| null, workOrderStatus }` | `400 errors.reason` · `403` · `404` · `409` (`visit_changed`, `visit_not_cancellable`) | Manage |
| GET | `/dispatch/visits/{visitId}` | Unchanged | Additive `cancellation: { reason, cancelledAt, cancelledByName } \| null`, `canCancel`, `cancelCreatesReplacement` (`job_type = 'one_time'`) | Unchanged | Read |

Mutations rely on the existing cookie session and CSRF protections; nothing
new is introduced.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/today/schedule` | Skeleton strip, summary, cards | "No jobs scheduled for this day." (BR-18) | BR-18 error with **Retry**, previous data kept | As `/today` | BR-15–BR-17 |
| `/today` banner | Part of page load | Hidden | As page | As `/today` | BR-17 |
| `/today/visits/:visitId` | Unchanged | — | Unchanged; `409 another_visit_active` per BR-19 | Unchanged | BR-06, BR-09 |
| Dispatch drawer cancel dialog | Confirm button progress; inputs disabled | — | `400` inline; `409` per BR-21 | Viewer: no action | BR-21 |

- Mockups: `design/assets/Agenda móvil del técnico FieldOps.png` (Approved;
  Schedule). Shell from `design/assets/7-design.png`. Mockup data are examples;
  the bell keeps its current behavior in this spec.
- Responsive and accessibility: BR-22.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid `weekStart` | `400 errors.weekStart` "Choose a valid week." | None |
| Another active visit on Start travel | `409 another_visit_active` + `activeVisit`; message and **Open active visit** | None |
| Start travel on a past `scheduled`/`assigned` visit | `409 visit_not_today`; UI shows "Ask dispatch to reschedule" and no Start travel | None |
| Mutation on a `cancelled` visit | `409 visit_status_invalid` | None |
| Cancel with invalid reason | `400 errors.reason` | None |
| Cancel of an ineligible visit or closed work order | `409 visit_not_cancellable` | None |
| Cancel with stale `updatedAt` | `409 visit_changed`; reload message | None |
| Cancel out of scope / other organization / unknown | `404` | None |
| Schedule load failure | BR-18 error, previous data kept | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Visits in the week assigned to the caller (including `cancelled` and `unscheduled`), to another technician, unassigned from the caller and of another organization | `GET /technician/schedule` is called, including with a foreign `technicianId` parameter | Only the caller's actively assigned non-`unscheduled` visits are returned in BR-03 order with `ScheduleVisit` fields; the parameter is ignored; no foreign data |
| AC-02 | A profile branch zone different from the organization and UTC, a branch without zone, a DST week, visits at local Monday 00:00 and Sunday 23:59 and just outside; `weekStart` absent, a Tuesday, malformed and out of range (table-driven) | The schedule is requested | Week boundaries and day membership follow BR-02 in the right zone; absent → current week; invalid values `400 errors.weekStart` |
| AC-03 | Days with active and `cancelled` visits of known durations and an empty day | The schedule loads | `days` counts and minutes exclude `cancelled`; the chip shows "<n> jobs · <duration>" per BR-04; an empty day shows "No jobs scheduled for this day." and navigation stays enabled |
| AC-04 | The caller has only a past `assigned` visit and a past `scheduled` visit, no open entries, and a today `assigned` visit | Start travel is called on the today visit and on the past visit | The today visit starts (`200 changed = true`); the past visit returns `409 visit_not_today`; neither past visit is reported active; both show `needsReschedule = true` |
| AC-05 | Another visit of the caller in `on_the_way`, `in_progress`, `paused`; an `assigned` visit with an open `work` entry of the caller; a `cancelled` visit and a visit unassigned from the caller with open entries (table-driven) | Start travel is called on a today `assigned` visit | The first four return `409 another_visit_active` with `activeVisit` of BR-05 (earliest by schedule when several) and no write; the cancelled and unassigned cases do not block |
| AC-06 | A `409 another_visit_active` on the Today next card and on the job page | The error is shown and **Open active visit** is used | The message, the active visit card (title, number, status, date/time) and **Open active visit** appear with focus on the message; the action opens `/today/visits/:activeVisitId` |
| AC-07 | A past `paused` visit, a past `on_the_way` visit, a today `in_progress` visit and a past `assigned` visit | `/today` and `/today/schedule` load | `unresolvedVisits` lists exactly the two past active visits in BR-05 order; the banner shows the first with "+1 more unfinished jobs" and **Open active visit**; no banner when only past `assigned` visits exist |
| AC-08 | A past `in_progress` visit opened from the banner | The primary technician pauses, resumes and completes it | The actions succeed without a date error (BR-07) and the banner disappears on the next load |
| AC-09 | A `cancelled` visit still assigned to the caller, a cancelled visit of another technician, and an `unscheduled` visit | The technician detail is requested and the page opened; a mutation is attempted on the cancelled visit | The own cancelled visit returns `200` with `cancellation` and renders read-only with date, reason and no action controls; the others `404`; the mutation `409 visit_status_invalid` |
| AC-10 | A past `assigned` visit opened in the job page | The page renders | "Ask dispatch to reschedule" is shown and **Start travel** is absent; on the schedule card the same text appears |
| AC-11 | An Owner, a Dispatcher in scope, a Dispatcher out of scope, a Viewer, Accounting, a Technician and another organization's visit (parameterized) | Cancel is posted | Owner and in-scope Dispatcher succeed; out-of-scope and foreign `404`; Viewer, Accounting and Technician `403`; no write on failures |
| AC-12 | Visits in each status, a closed work order and a stale `updatedAt`; reasons of 9, 10, 500 and 501 characters (table-driven) | Cancel is posted | Eligible statuses on any date succeed; other statuses and closed work orders `409 visit_not_cancellable`; stale `409 visit_changed`; 9 and 501 `400 errors.reason`; no write on errors |
| AC-13 | An `in_progress` visit with an open `work` entry and a `paused` visit with an open `pause` entry, each with completed and pending checklist items, materials and photos, on one-time work orders | Cancel succeeds | Status `cancelled`; open entries closed (pause seconds added); one history row with reason; one `visit.cancelled` audit row without the reason; a replacement `unscheduled` visit with next `visit_number`, null-to-unscheduled history and only the pending checklist items; completed items, materials, photos and entries stay on the cancelled visit; assignments unchanged; no email or notification |
| AC-14 | A one-time work order `in_progress` whose only active visit is cancelled, one with another `assigned` visit, and a recurring work order (occurrences 1 completed, 2 `assigned`, 3 `unscheduled`) whose occurrence 2 is cancelled | Cancel succeeds | The first becomes `ready_to_schedule` with a `work_order.status_changed` audit row; the second keeps its status; the recurring order gets no replacement (`replacementVisitId` null), occurrences 1 and 3 keep their `visit_number`, and it becomes `ready_to_schedule` per BR-13 |
| AC-15 | Concurrent cancel and start-travel/pause on the same visit, and two concurrent cancels | They run | Lock order serializes them; exactly one cancellation, one replacement and one audit row; the losing technician action returns `409 visit_status_invalid` and the losing cancel `409 visit_changed` or `visit_not_cancellable` |
| AC-16 | The dispatch drawer for an eligible past visit of a one-time and of a recurring work order, a started visit, a cancelled visit, and as Viewer | **Cancel visit** is used with invalid then valid reason, and drawers are opened | The dialog follows BR-21 with counter, validation and the one-time or recurring text and toast; success closes, refreshes calendar and Unscheduled panel and shows the toast; the started visit's read-only drawer offers the action; the cancelled drawer shows date, name and reason; Viewer sees no action |
| AC-17 | The Schedule page | Weeks are changed backward and forward, days selected, a visit card tapped, and a week load fails then is retried | Strip, selection and heading follow BR-15/BR-16; cards show BR-16 content in the response zone with the notice; tapping opens `/today/visits/:visitId`; failure keeps the prior week with the error and Retry reloads |
| AC-18 | The technician shell | Each bottom destination is opened, including `/today/soon/schedule` | Order Today · Schedule · Time · Messages · More; the current item is highlighted with `aria-current`; Today not current on Schedule; the old Schedule Coming soon route redirects; More, bell, Account settings, Help & support and Sign out unchanged |
| AC-19 | Viewports 375 px and 1280 px | Schedule is used by keyboard and touch | Approved design layout; no page-level horizontal scroll; targets ≥ 44 px; visible focus; ordered timeline; status not by color alone; centered column ≤ 480 px on wide screens |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Active-visit selection and week boundary computation only if implemented as separate pure helpers | AC-02, AC-05 |
| Backend integration | Schedule selection, ordering, week/zone boundaries and day totals (table-driven) | AC-01–AC-03 |
| Backend integration | Active-visit guard matrix, `activeVisit` payload, past `assigned` non-blocking and `unresolvedVisits` | AC-04, AC-05, AC-07, AC-08 |
| Backend integration | Cancel effects, replacement, work order rule, validation and concurrency | AC-12–AC-15 |
| Authorization/tenant isolation | Schedule ignores foreign ids; cancelled detail `404` for others; cancel role/scope/foreign matrix (parameterized) | AC-01, AC-09, AC-11 |
| Frontend component/service | Schedule page (strip, selection, cards, states, retry); unresolved banner; blocking card with Open active visit; cancelled/needs-reschedule detail; dispatch cancel dialog; bottom navigation (3–6 methods) | AC-03, AC-06, AC-07, AC-09, AC-10, AC-16–AC-18 |
| User visual QA (manual) | Schedule design fidelity, responsive layout, focus | AC-19 |

Backend integration stays within 3–8 methods; the security-relevant cancel
concurrency (AC-15) may justify one extra method. Agents run no browser
automation.

## Dependencies

- `technician-todays-jobs`, `mobile-job-details`, `mobile-job-progress`,
  `mobile-job-completion` (AUDITED): technician shell, detail contract, travel
  guards, time entries, completion closure rule.
- `dispatch-calendar` (APPROVED): drawer, Manage policy, scope, `visit_changed`
  concurrency, BR-13 locks, BR-18 recurrence.
- `jobs-management` (AUDITED): current visit and Unscheduled tab reflect the
  replacement visit without change.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Visits do not cross local midnight (`dispatch-calendar` AS-01). |
| AS-02 | A week returns at most a few dozen visits; no paging. |
| AS-03 | The replacement visit has no preferred window; dispatch sets it when scheduling. |
| AS-04 | Cancellation keeps assignments so the technician keeps read-only access to the cancelled visit. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Recurring work orders: `visit_number` is the occurrence index, so a max+1 replacement would break BR-18/BR-09 | Skip occurrence · replacement with rule changes · block | No | Skip the occurrence, no replacement, other occurrences keep `visit_number` (BR-14) — user 2026-10-17 |
| OD-02 | Split and order | One spec · three specs | No | Three specs; order Schedule → Time → Messages — user 2026-10-17 |
| OD-03 | Time zone | Profile branch · work order branch | No | Profile branch, organization fallback — user 2026-10-17 |
| OD-04 | Past `scheduled`/`assigned` start | Blocked · allowed | No | Blocked, "Ask dispatch to reschedule", never active — user 2026-10-17 |
| OD-05 | Active visit definition and payload | — | No | BR-05 — user 2026-10-17 |
| OD-06 | Banner | Past active only · all past | No | Past active only, Today and Schedule — user 2026-10-17 |
| OD-07 | Cancellation | — | No | Dispatch drawer, Manage + scope, five statuses any date, reason 10–500, entries closed, history and audit, replacement with pending checklist only; work order cancel separate — user 2026-10-17 |
| OD-08 | Cancelled visits for technician | Visible and readable · visible only | No | Visible, excluded from totals, read-only detail with reason — user 2026-10-17 |
| OD-09 | Work order and audit after cancel | — | No | `scheduled`/`in_progress` without other active or scheduled visits → `ready_to_schedule` (BR-13); reason only in `visit_status_history`, `visit.cancelled` audit without reason, no email or notification (BR-12) — user 2026-10-17 |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-09 |
| FR-02 | AC-01, AC-02, AC-03 |
| FR-03 | AC-03, AC-17, AC-19 |
| FR-04 | AC-04, AC-10 |
| FR-05 | AC-04, AC-05 |
| FR-06 | AC-06 |
| FR-07 | AC-07, AC-08 |
| FR-08 | AC-09 |
| FR-09 | AC-11, AC-12, AC-13, AC-14, AC-15 |
| FR-10 | AC-16 |
| FR-11 | AC-18 |

## Change log

| Date       | Status change | Reason  |
| ---------- | ------------- | ------- |
| 2026-10-17 | — → DRAFT     | Created |
| 2026-10-17 | DRAFT → DRAFT | Revised: OD-01 resolved (recurring cancellation skips the occurrence without replacement; FR-09, BR-14, BR-21, dispatch detail `cancelCreatesReplacement`, States, AC-14, AC-16); BR-05 supersession of `mobile-job-details` BR-07 made explicit; OD-09 records the confirmed work order and audit rules |
| 2026-10-17 | DRAFT → APPROVED | Approved by user via /spec approve |
