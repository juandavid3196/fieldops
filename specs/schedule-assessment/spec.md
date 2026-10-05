# Schedule assessment

| Field    | Value                 |
| -------- | --------------------- |
| Feature  | `schedule-assessment` |
| Type     | Full-stack            |
| Status   | APPROVED              |
| Created  | 2026-10-05            |
| Updated  | 2026-10-05            |
| Approved | 2026-10-05            |

## Context and objective

`requests-pipeline` lets managers schedule, reschedule, cancel and complete an
assessment from a dialog. The dialog only accepts times and an optional
technician, validates only assessment overlap and does not tell the customer.
This feature replaces that dialog with the Schedule assessment page (Design 2).
The page shows the customer's preferred times, eligible technicians with their
availability, conflicts and workload, a read-only calendar, the visit details,
a customer message preview, and an email notification. The feature reuses the
existing `assessments` entity, endpoints and request transitions. The
assessment stays optional: managers can still go back to the request and mark
it ready for quote directly.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Open the page and the planning endpoints, and schedule, reschedule or cancel assessments for any request in any branch | — |
| Dispatcher (`dispatcher`) | Same as Owner for requests in branch scope (`requests-pipeline` BR-02) | Requests outside scope (`404`) |
| Operations Manager, Accounting, Viewer | Keep their read access to the Requests page and panel (`requests-pipeline` BR-01) | Open this page (forbidden state) or call the planning endpoints or assessment mutations (`403`) |
| Technician (`technician`) and unknown roles | — | Everything in this feature (`403`; forbidden state) |

## Scope

- Page `/requests/:requestId/assessment` inside the app shell. It opens in
  schedule mode or reschedule mode depending on the request status (BR-01).
- Entry points: the panel footer actions **Schedule assessment** and
  **Reschedule** navigate to the page. The previous schedule/reschedule dialog
  is removed.
- Customer availability summary taken from the request.
- Branch selection when the request has no branch.
- Technician cards with slot state and weekly workload (BR-05, BR-06).
- Read-only calendar of the selected technician with Week, Day and Agenda views
  (BR-07).
- Visit form fields:
  - Date, arrival window and estimated duration.
  - Assigned technician.
  - Purpose and internal instructions.
- Customer message preview and notification channels: Email is functional,
  and SMS is shown disabled.
- Schedule, reschedule and cancel the assessment, each with an optional
  customer email.
- Stronger conflict validation (`409`) on the existing schedule and reschedule
  endpoints.
- Request process stepper and the optional-assessment notice.
- Schema amendment: `assessments.purpose` (BR-12).

## Non-goals

- The global Schedule module and its calendar.
- Creating quotes. **Create quote** keeps navigating to `/coming-soon/quotes`.
- Routes, travel time or travel blocks, auto-assignment and technician
  suggestions.
- Skill-based filtering or validation. Skills are shown only.
- SMS delivery, an SMS provider, and rows in `notifications`.
- Completing an assessment with diagnosis or scope. **Complete assessment**
  stays in the request panel, unchanged.
- Assessment numbers. The mockup's "Assessment ID" field is not rendered.
- Selecting a time by clicking the calendar, and drag-and-drop.
- Variable-length arrival windows.
- Changes to the request transition table, the Requests board, metrics, the
  Team pages or their workload calculation.
- Shell changes. The sidebar and top bar stay as implemented, even where the
  mockup shows older labels.

## User flow

1. A manager opens a `new` or `needs_review` request and presses **Schedule
   assessment** (or **Reschedule** on an `assessment_scheduled` request). The
   system navigates to `/requests/<id>/assessment`.
2. The page shows the header, the optional notice, the customer's preferred
   times, the eligible technicians and the form. In reschedule mode, the form
   is filled with the current assessment. If the request has no branch, the
   manager picks one first.
3. The manager picks a technician, date, arrival window and duration. The
   technician cards update their slot state and workload. The calendar shows
   the technician's commitments and the proposed block. The message preview
   updates.
4. The manager enters the purpose and, optionally, internal instructions, and
   chooses whether to email the customer.
5. The manager presses **Schedule assessment** (or **Save changes**). The
   system validates the input and any conflicts, persists the assessment, moves
   a new or under-review request to `assessment_scheduled`, and sends the email
   after commit. It then navigates to `/requests?request=<id>` with a success
   toast.
6. Alternatively, the manager presses **Back to request** or **Cancel** and
   returns to the request without changes. From there the manager can **Mark
   ready for quote**. In reschedule mode, **Cancel assessment** cancels the
   assessment after confirmation.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The frontend must provide `/requests/:requestId/assessment` for managers, in schedule or reschedule mode per BR-01. The panel's **Schedule assessment** and **Reschedule** must navigate to it, and the schedule/reschedule dialog must no longer exist. |
| FR-02 | The page must show the header, breadcrumb, request status chip, optional notice, customer availability, request summary and request process stepper per BR-02 and BR-03. |
| FR-03 | `GET /service-requests/{id}/assessment/planner` must return the eligible technicians (BR-04), each with its slot state for the requested date, start and duration (BR-05) and its workload for that date's week (BR-06). |
| FR-04 | `GET /service-requests/{id}/assessment/calendar` must return, for one technician and a date range of up to 7 days, its availability, time off and breaks, and its assessment and visit commitments (BR-07). The page must render this read-only in Week, Day and Agenda views together with the proposed block. |
| FR-05 | The form must capture date, arrival window, estimated duration, technician, purpose and internal instructions per BR-08 to BR-12 and BR-20. The existing schedule and reschedule endpoints must accept and persist them. |
| FR-06 | Schedule and reschedule must reject blocking conflicts with `409` and must accept non-blocking warnings (BR-09). A conflicting concurrent booking of the same technician must leave exactly one success (BR-10). |
| FR-07 | The page must show a customer message preview identical to the email body text. When the manager leaves Email on, the system must send that email after commit on schedule, reschedule and cancel (BR-13, BR-14). |
| FR-08 | **Cancel assessment** (page in reschedule mode, and the panel's more-actions menu) must use the confirm dialog with an optional customer email and keep the existing transition back to `needs_review` (BR-15). |
| FR-09 | Activity entries and audit rows for assessment events must state whether the customer was notified, with no message content (BR-16). |
| FR-10 | The request detail's `assessment` object must include the purpose, the internal instructions and the technician id and name (BR-17). |
| FR-11 | Every endpoint must resolve the organization from the session and apply `requests-pipeline` role and branch scope. Client ids must be verified server-side. |
| FR-12 | The assessment must stay optional: from the page, a manager can return to the request without creating an assessment and mark it ready for quote directly through the existing panel action. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Page mode by request status. **Schedule**: `new`, `needs_review`. **Reschedule**: `assessment_scheduled`, with the form prefilled from the active assessment. Every other status shows the state "This request can't have an assessment scheduled." with **Back to request**, and makes no planner calls. An out-of-scope or foreign request shows "This request isn't available." (`404`). Read roles and Technician see the existing forbidden state "You don't have access to schedule assessments." and no data is requested. | Both |
| BR-02 | Header. Breadcrumb **Requests** › `<REQ-n>` › **Schedule assessment** (or **Reschedule assessment**). Title "Schedule assessment" or "Reschedule assessment". Status chip "Request: <status label>" with the `requests-pipeline` labels. Subtitle "Arrange an optional site visit to gather information before preparing a quote." **Back to request** goes to `/requests?request=<id>`. Notice: "**Assessment is optional.** If the request already contains enough information, return to the request and choose **Mark ready for quote** instead." The notice is shown in schedule mode only. | Frontend |
| BR-03 | Request summary: request number, customer (`requests-pipeline` BR-04 customer), property (service address), issue (card title per `requests-pipeline` BR-04). Customer availability card: "Customer availability from request" with the preferred visit text of `requests-pipeline` BR-08 (date + window, ASAP or Flexible, plus scheduling notes), or "No preferred times provided.". It also shows the note "These are preferred times, not a confirmed appointment until you schedule the assessment.". Stepper: Request under review → Assessment scheduled → Assessment completed → Ready for quote. In schedule mode, the first step is done and the second is current. In reschedule mode, the first two are done and the third is current. The footer note reads "Completing the assessment returns the request to Ready for quote. A quote must still be created and sent separately.". | Frontend |
| BR-04 | Eligible technicians: `technician_profiles` of the session organization with `status = active` and `branch_id` equal to the request branch. When the request has no branch, the page first shows a **Branch** select with the active in-scope branches (`requests-pipeline` BR-11). The planner accepts `branchId` only in that case; a `branchId` sent for a request that already has a branch → `400` `errors.branchId` "This request already has a branch.". The schedule body carries it as today. Order: slot state (available, available after, outside availability, conflict, time off), then name. Each card shows name, primary skill name (or no line), slot state chip and workload. With no eligible technicians: "No active technicians in this branch." with a link to Team. A foreign, inactive or other-branch `technicianId` → `400` `errors.technicianId` "Choose an active technician from this request's branch." (existing rule). | Both |
| BR-05 | Slot state of a technician for slot `[start, start + duration)`. Times are org local and converted to instants. Availability uses the technician's branch time zone per `team-technician-management` BR-03/BR-04. First match wins. **Time off**: an active `is_available = false` exception intersects the slot. Chip "Time off", blocking. **Conflict**: a commitment intersects the slot. Commitments are another `scheduled` assessment of the technician (excluding the assessment being rescheduled), or a visit with an active assignment (`visit_assignments.unassigned_at IS NULL`) whose status is not `unscheduled`, `cancelled`, `completed` or `approved`. Chip "Conflict h:mm – h:mm a" with the earliest intersecting commitment's local range, blocking. **Available**: the slot lies entirely inside availability (weekly windows minus breaks, plus available exceptions). Chip "Available". **Available after**: otherwise, the earliest whole-hour start later than the selected start on the same local day for which the slot would be Available. Chip "Available after h:mm a", warning. **Outside availability**: otherwise. Chip "Outside availability", warning. States are never conveyed by color alone. | Backend |
| BR-06 | Workload for the Monday–Sunday local week containing the selected date. It uses the `team-technician-management` BR-06 formula (scheduled ÷ available minutes, rounded, "No availability", "—"). Scheduled minutes count both counted visits and `scheduled` assessments of the technician that start in the week. The assessment being rescheduled is excluded. Shown as "Workload: <n>%", "Workload: No availability" or "Workload: —". The Team pages keep their own calculation. | Backend |
| BR-07 | Calendar (read-only). Week view: Monday–Friday, plus Saturday and Sunday when the technician has availability or commitments on them. Day view: the selected date. Agenda: the commitments of the visible week in chronological order. Previous and next move by one week (or one day in Day view). The range defaults to the selected date. The hour range spans the earliest and latest availability or commitment of the range, defaulting to 8:00 AM–6:00 PM. The calendar shows: commitments with local time range and label (assessment: request title per `requests-pipeline` BR-04; visit: "Visit"); the current assessment in reschedule mode labelled "Current assessment"; the proposed block "Proposed assessment" when date, window and duration are set; and shaded time off and hours outside availability. Clicking does not select anything. Without a technician: "Select a technician to see their schedule.". | Both |
| BR-08 | Date and arrival window. Date: a local date from today onward. Arrival window: 1-hour windows starting on the hour, from 6:00 AM to 7:00 PM, labelled "h:mm a – h:mm a". `scheduled_start` = date + window start. A start that is not on the hour → `400` `errors.start` "Choose an arrival window.". The existing rules still apply: the start must be in the future, the end must fall on the same local day, and the duration must be at most 8 h. | Both |
| BR-09 | Conflicts on schedule and reschedule, checked server-side inside the write transaction with the BR-05 definitions for the required technician (BR-20): Time off → `409` `code` `technician_time_off`, title "This technician is off at that time.". Conflict → `409` `code` `technician_conflict`, title "This technician already has a commitment at that time.". Available after and Outside availability are accepted without error. This replaces the `400` `errors.technicianId` overlap response of `requests-pipeline` BR-15. Format and validation failures remain `400`. Status-transition conflicts keep the `requests-pipeline` `409` title "This request changed. Refresh to see the latest." with `code` `request_changed`. | Backend |
| BR-10 | Schedule and reschedule serialize on that technician's profile row for the duration of the transaction. Of two concurrent bookings whose slots intersect, exactly one succeeds and the other receives the BR-09 `technician_conflict` `409`. | Backend |
| BR-11 | Estimated duration ∈ 30, 60, 90, 120, 180, 240, 480 minutes, labelled "<n> minutes" below 120 and "<h> hours" from 120. `scheduled_end` = `scheduled_start` + duration. Any other `end − start` → `400` `errors.end` "Choose an estimated duration.". The arrival window is not persisted. It is always derived as `scheduled_start` to `scheduled_start` + 1 h. | Both |
| BR-12 | Purpose: required, 1–500 characters after trim → `400` `errors.purpose` "Enter the purpose of the assessment." / "Purpose must be 500 characters or fewer.". Stored in the new column `assessments.purpose`. Internal instructions: optional, ≤ 2000 characters after trim (`400` `errors.internalInstructions` "Internal instructions must be 2000 characters or fewer."), stored in `assessments.internal_notes` (empty → null), labelled "(not visible to customer)". Neither field is ever sent to the customer, logged or audited. `purpose` is nullable in the schema only so existing assessments stay valid. | Both |
| BR-13 | Customer message. Preview and email body text: schedule → "Your assessment visit is scheduled for <EEE, MMM d> between <h:mm a> and <h:mm a>. <Technician full name> will inspect the issue before we prepare your quote."; reschedule → "Your assessment visit has been rescheduled to <EEE, MMM d> between <h:mm a> and <h:mm a>. <Technician full name> will inspect the issue before we prepare your quote."; cancel → "Your assessment visit on <EEE, MMM d> between <h:mm a> and <h:mm a> has been cancelled. We'll contact you if we need to arrange another visit.". Times are the BR-11 arrival window in the organization time zone. The preview updates as the form changes and shows "Complete the date, time and technician to preview the message." until date, window and technician are set. | Both |
| BR-14 | Notification. **Email** toggle: on by default when the request has a recipient email (linked active contact email, else `guest_email`, as in `requests-pipeline` BR-13). Otherwise it is off and disabled with "This customer has no email address.". Contact channel preferences are ignored (`requests-pipeline` AS-06). **SMS** toggle: always off and disabled with "SMS isn't available yet.". The body sends `notifyCustomer` (boolean). When `notifyCustomer` is true and there is no recipient email → `400` `errors.notifyCustomer` "This customer has no email address.". After commit, exactly one email is sent through `IEmailSender`. Subject: "<organization name>: assessment visit for <REQ-n>" (schedule), "<organization name>: assessment visit rescheduled for <REQ-n>" (reschedule) or "<organization name>: assessment visit cancelled for <REQ-n>" (cancel). Text and HTML bodies contain "Hi <contact first name or 'there'>,", the BR-13 text, the organization name and, when set, "Questions? Call us at <organization phone>.". On send failure, only the request id and failure category are logged, and the response is still success. No `notifications` or `request_messages` rows are written, so "Awaiting response" is unaffected. | Both |
| BR-15 | Cancel assessment: the existing confirm dialog "Cancel assessment?" with "The request returns to Needs review.", a "Notify customer by email" checkbox (default and availability per BR-14) and buttons **Cancel assessment** / **Keep**. Body `{ notifyCustomer }`. A missing body means `false`. The transition, history and audit are unchanged from `requests-pipeline` BR-15. On the page, success navigates to `/requests?request=<id>`. In the panel, it refreshes as today. | Both |
| BR-16 | Audit: the existing `service_request.assessment_scheduled` / `assessment_rescheduled` / `assessment_cancelled` rows keep their fields. Their metadata adds `notified` = `"email"` or `"none"`, recording the intent at commit. Activity labels stay as in `requests-pipeline` BR-19. When `notified = "email"`, the detail line ends with " · Customer notified by email". Purpose, instructions, message text and recipient never appear in audit rows or logs. | Backend |
| BR-17 | Detail `assessment` = `{ id, start, end, technician: { id, name } \| null, purpose \| null, internalInstructions \| null }`. Read roles see these fields in the panel's Assessment section (purpose and internal instructions are internal data). The panel shows the purpose under the assessment date/time. | Both |
| BR-18 | Form submission and navigation. Schedule mode: primary **Schedule assessment**. Reschedule mode: primary **Save changes**, plus a separate destructive **Cancel assessment** action (danger style, placed apart from the form's Cancel/Save pair, always behind the BR-15 confirm). **Cancel** and **Back to request** only discard the edit and leave without saving; they never change the assessment. If the form changed, the shared discard-changes dialog asks first. Submit is disabled while in flight and sends one request. Success: navigate to `/requests?request=<id>`, the panel opens with the returned state, and the toast reads "Assessment scheduled.", "Assessment rescheduled." or "Assessment cancelled.". | Frontend |
| BR-19 | Defaults in schedule mode: the date is the customer's preferred date when it is in the future, otherwise the next day. The window is the first window inside the preferred time window when one exists, otherwise 9:00 AM. The duration is 60 minutes. The technician is none. | Frontend |
| BR-20 | The technician is mandatory in this whole flow. POST and PUT `/service-requests/{id}/assessment` require `technicianId`. A missing one → `400` `errors.technicianId` "Choose a technician.". The page shows the same message inline. This replaces the optional `technicianId` of `requests-pipeline` BR-15 for new schedules and reschedules. Existing assessments without a technician stay valid until they are rescheduled. | Both |

## States and transitions

No transition changes. This feature reuses the `requests-pipeline` rows:

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| `new`, `needs_review` | `assessment_scheduled` | Schedule assessment (page) | Owner, Dispatcher in scope | `requests-pipeline` BR-15 plus BR-08, BR-09, BR-11, BR-12, BR-14, BR-20 |
| `assessment_scheduled` | `assessment_scheduled` (assessment row updated) | Save changes (page) | Owner, Dispatcher in scope | Same as above, excluding the assessment itself |
| `assessment_scheduled` | `needs_review` | Cancel assessment (page or panel) | Owner, Dispatcher in scope | Active assessment exists; BR-15 |
| `new`, `needs_review` | `ready_for_quote` | Mark ready for quote (panel, unchanged) | Owner, Dispatcher in scope | No assessment is created or completed |
| `assessment_scheduled` | `ready_for_quote` | Complete assessment (panel, unchanged) | Owner, Dispatcher in scope | `requests-pipeline` BR-15 |

Assessment row: `scheduled` → `cancelled` on cancel. `scheduled` →
`completed` stays as in `requests-pipeline`.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `assessments`: insert; update `scheduled_start`, `scheduled_end`,
    `technician_id`, `internal_notes`, `purpose` (new), `status`,
    `updated_at`. Read for conflicts and workload, using
    `ix_assessments_schedule`.
  - `service_requests`, `request_status_history`, `audit_logs`: as in
    `requests-pipeline` for these transitions.
  - Read only:
    - `technician_profiles`: active, branch, names; the row is locked per
      BR-10.
    - `technician_skills` and `skills`: primary skill name.
    - `technician_weekly_availability`, `technician_breaks`,
      `technician_exceptions`.
    - `visits` and `visit_assignments`: commitments.
    - `branches`: `timezone` and scope.
    - `organizations`: `name`, `phone`, `timezone`, `request_prefix`.
    - `customer_contacts`: email and first name.
- Schema amendment required: **approved by the user 2026-10-05** (OD-04).
  1. Add `assessments.purpose varchar(500)` NULL. It is nullable so existing
     rows stay valid. The API requires it on schedule and reschedule (BR-12).
     There is no backfill and no index.

  Implementation updates `docs/database/fieldops-schema.sql` and the EF model,
  and generates one migration under `--generate-migration`. Agents never apply
  it. The mapping change requires `database-reviewer`.
- `assessments.technician_id` keeps its single-column FK. Organization, branch
  and status are validated in the application layer.
- `notifications` is not used (OD-10).

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  request carries an organization id. Any such field is ignored.
- Client identifiers, verified against the session organization and the
  caller's branch scope:
  - Request id (path).
  - `technicianId`: active, same organization, request branch.
  - `branchId`: only when the request has no branch, per
    `requests-pipeline` BR-11.
- A request of another organization or outside branch scope returns `404`,
  identical to a nonexistent id, on the planner, the calendar and every
  mutation.
- A foreign or ineligible `technicianId` on the planner or calendar returns
  `400` `errors.technicianId`. No commitments or labels of that technician are
  returned.
- Calendar labels expose only request titles of commitments by the selected
  in-branch technician. They contain no customer names or addresses.
- Permissions: planner, calendar and assessment mutations require the Manage
  policy (Owner, Dispatcher). Other roles get `403`. Responses use
  `Cache-Control: no-store`.
- Concurrency: BR-10, plus the existing per-request serialization of
  `requests-pipeline`.

## API contracts

Contract status: Final. The existing paths are extended, not duplicated.
Errors use ProblemDetails. Every `409` carries `code`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/service-requests/{id}/assessment/planner` | Query: `date` (`YYYY-MM-DD`), `start` (`HH:mm`), `durationMinutes`, optional `branchId` (only for branchless requests) | `200 { timezone, branchId, technicians: [{ id, name, initials, primarySkill \| null, slot: { state ("available"/"available_after"/"outside_availability"/"conflict"/"time_off"), from \| null, to \| null, availableAfter \| null, blocking }, workload: { percent \| null, state ("percent"/"no_availability"/"none") } }] }` | `400` (`date`, `start`, `durationMinutes`, `branchId`) · `403` · `404` · `409` `request_changed` (status not schedulable) | Manage |
| GET | `/service-requests/{id}/assessment/calendar` | Query: `technicianId`, `from`, `to` (`YYYY-MM-DD`, inclusive, ≤ 7 days) | `200 { timezone, days: [{ date, availability: [{ start, end }], timeOff: [{ start, end }], breaks: [{ start, end }] }], events: [{ kind ("assessment"/"visit"), start, end, label, isCurrent }] }` | `400` (`technicianId`, `from`, `to`) · `403` · `404` | Manage |
| POST | `/service-requests/{id}/assessment` | `{ start, end, technicianId, branchId?, purpose, internalInstructions?, notifyCustomer }` | `200 RequestDetail` | `400` (`start`, `end`, `technicianId`, `branchId`, `purpose`, `internalInstructions`, `notifyCustomer`) · `403` · `404` · `409` (`technician_conflict`, `technician_time_off`, `request_changed`) | Manage |
| PUT | `/service-requests/{id}/assessment` | `{ start, end, technicianId, purpose, internalInstructions?, notifyCustomer }` | `200 RequestDetail` | As POST, without `branchId` | Manage |
| POST | `/service-requests/{id}/assessment/cancel` | Optional `{ notifyCustomer }` | `200 RequestDetail` | `400` (`notifyCustomer`) · `403` · `404` · `409` `request_changed` | Manage |
| GET | `/service-requests/{id}` | — | `200 RequestDetail` with `assessment` per BR-17 | Unchanged | Read |

- `start`/`end` stay local org-time `YYYY-MM-DDTHH:mm`. Responses carry ISO
  instants with offsets plus `timezone`.
- `technicianId`, `purpose` and `notifyCustomer` are required on POST/PUT. A missing
  `notifyCustomer` → `400`.
- Internal contract: existing `IEmailSender`, unchanged. A new assessment
  notification composer follows the `RequestInformationEmailComposer`
  precedent.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Schedule assessment page | Skeletons for header, availability card, technician cards and form | No eligible technicians: "No active technicians in this branch." with Team link. Branchless request: Branch select first, with technicians and calendar waiting | Request load failure: existing error pattern with **Retry**. `404`: "This request isn't available." with **Back to requests** | Read roles and Technician: forbidden state (BR-01), no further calls. Non-schedulable status: BR-01 state | Header, cards, calendar and prefilled form render |
| Technician cards | Cards keep content with a loading indicator while the slot changes | — | Planner failure: "We couldn't load technician availability." with **Retry**. Form stays usable | — | BR-05 chip and BR-06 workload. The selected card is marked and synced with the Assigned technician select |
| Calendar | Grid skeleton | No technician: BR-07 message. No commitments: grid with availability shading only. Agenda: "No commitments this week." | "We couldn't load the calendar." with **Retry** | — | BR-07 contents and proposed block |
| Form submit | Primary button shows progress and is disabled | — | `400`: inline field errors. `409` `technician_conflict`/`technician_time_off`: inline error under Assigned technician with the 409 title; planner and calendar reload; data kept. `409` `request_changed`: toast with its title, then navigate back to the request. Other: toast "We couldn't save this change. Please try again." with data kept | — | BR-18 navigation and toast |
| Cancel assessment confirm | As form submit | — | As form submit | — | BR-15 |

- Mockup: `design/assets/2- design.png` (Approved).
  - The main area must match it:
    - Breadcrumb, title with status chip, subtitle, **Back to request** and
      the info notice.
    - The "Find a time" card with the customer availability box, technician
      cards, calendar toolbar (previous/next, range label, Week/Day/Agenda)
      and grid.
    - The "Assessment visit" card with the request summary, form fields,
      message preview, notify toggles, buttons, request process stepper and
      footer note.
  - Deviations by decision:
    - The shell stays as implemented.
    - The Assessment ID field is not rendered (OD-05).
    - The notice names **Mark ready for quote** (OD-13).
    - SMS is disabled (OD-10).
    - Travel blocks are not shown (OD-12).
    - An "Outside availability" chip state is added and **Cancel assessment**
      appears in reschedule mode.
    - Sample data is replaced by real data.
- Responsive:
  - Wide desktop: the two cards side by side, as in the mockup.
  - Tablet and small desktop: the cards stack. The form card comes after
    "Find a time", technician cards wrap, and the Week grid scrolls
    horizontally inside its card.
  - Mobile: single column. Technician cards stack. The calendar opens in Day
    view, with Week available through horizontal scroll. The form follows, and
    primary actions stay at the end of the form. There is no horizontal page
    scroll.
- Accessibility:
  - Technician cards form a radio group with names "<name>, <state chip
    text>, workload <value>".
  - Calendar events are listed with time range and label for screen readers.
    The Agenda view is the accessible equivalent.
  - The disabled SMS toggle and the disabled Email toggle expose their helper
    text.
  - The message preview is a polite live region updated on change.
  - Focus moves to the page heading on load. The inline conflict error is
    announced.
  - Chip states are never conveyed by color alone.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Request of another organization or out of scope | `404`; "This request isn't available." | None |
| Read role or Technician calls planner, calendar or a mutation | `403`; forbidden state | None |
| Invalid date, window, duration, purpose, instructions, notify flag | `400` on the field | None |
| Missing technician | `400` `errors.technicianId` "Choose a technician." | None |
| Foreign, inactive or other-branch technician | `400` `errors.technicianId` | None |
| Technician time off / existing commitment | `409` `technician_time_off` / `technician_conflict`; inline error | None |
| Concurrent booking of the same technician and slot | One `200`, one `409` `technician_conflict` | Only the winner persists |
| Request status no longer schedulable | `409` `request_changed`; toast and return to request | None |
| Email send failure after commit (schedule, reschedule or cancel) | `200`; failure logged without personal data | Never rolled back: assessment and transition kept; audit `notified = "email"` |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | A `needs_review` request and an `assessment_scheduled` request in scope | A Dispatcher presses **Schedule assessment** or **Reschedule** in the panel | The browser navigates to `/requests/<id>/assessment` in schedule or reschedule mode (prefilled with the current slot, technician, purpose and instructions); no schedule/reschedule dialog opens |
| AC-02 | A `ready_for_quote`, a `cancelled` request, and a request of another organization | A manager opens the page for each | The first two show "This request can't have an assessment scheduled." with **Back to request** and no planner call; the foreign one shows "This request isn't available." and the planner, calendar and mutations return `404` |
| AC-03 | An Operations Manager, a Viewer and a Technician | They open the page and call the planner, the calendar and POST assessment | The page shows the forbidden state with no data requests; every call returns `403`; the panel shows them no Schedule/Reschedule actions |
| AC-04 | A request with preferred date and window, scheduling notes, customer and property | The page loads | Header, breadcrumb, status chip, notice, availability card, request summary and stepper match BR-02/BR-03; defaults follow BR-19 |
| AC-05 | A branch with active, inactive and other-branch technicians; a branchless request | The planner is requested; the branchless page loads | Only active technicians of the request branch are returned, with primary skill; the branchless page asks for a branch first and lists that branch's technicians once chosen; an out-of-scope `branchId` returns `400` |
| AC-06 | Technicians with: a free in-availability slot; another scheduled assessment overlapping; an active visit overlapping; a time-off exception; a slot in a break with a later free hour; a slot outside all availability | The planner is requested for that slot | States are Available; Conflict with the commitment range; Conflict; Time off; Available after <hour>; Outside availability; ordering follows BR-04 |
| AC-07 | A technician with visits and assessments in the selected week and weekly availability | The planner is requested, then the same request in reschedule mode | Workload equals round(scheduled ÷ available × 100) counting visits and scheduled assessments; in reschedule mode the current assessment is excluded; no availability shows "No availability" |
| AC-08 | A selected technician with commitments, time off and weekend availability | Week, Day and Agenda views are used and the week changes | The calendar shows commitments with labels, the current assessment, the proposed block, shaded unavailability and weekend columns per BR-07; clicking selects nothing; a range over 7 days or a foreign technician returns `400` |
| AC-09 | The form | Invalid inputs are submitted: missing technician, past start, start not on the hour, duration not in the list, end on another day, missing or > 500-char purpose, > 2000-char instructions, missing `notifyCustomer` | Each returns `400` on its field; nothing is persisted |
| AC-10 | A `needs_review` request and a technician whose slot is Outside availability | A valid schedule is submitted with purpose and instructions | One `scheduled` assessment is created with start, end = start + duration, technician, `purpose` and `internal_notes`; the request moves to `assessment_scheduled` with one history row and one audit row; the warning did not block |
| AC-11 | A technician with an overlapping assessment, and another with time off | Schedule and reschedule are submitted for those slots | `409` with `technician_conflict` / `technician_time_off` and the BR-09 titles; nothing changes; the page shows the inline error, reloads planner and calendar, and keeps the form data |
| AC-12 | Two requests and one technician | Two schedules for intersecting slots of that technician run concurrently | Exactly one succeeds; the other returns `409` `technician_conflict`; only one assessment exists for that slot |
| AC-13 | An `assessment_scheduled` request | A valid reschedule to another slot and technician is saved | The same assessment row is updated (times, technician, purpose, instructions), status stays `assessment_scheduled`, one `assessment_rescheduled` audit row is written |
| AC-14 | Date, window, duration and technician are changed on the page | The preview is read | It shows the BR-13 schedule (or reschedule) text with the formatted date, the derived 1-hour window and the technician name, and the placeholder text until date, window and technician are set |
| AC-15 | A request whose contact has an email | Schedule, reschedule and cancel are submitted with Email on | After each commit exactly one email per BR-14 is sent with the matching subject and BR-13 text; audit metadata has `notified = "email"`; activity shows " · Customer notified by email"; no `notifications` or `request_messages` rows are created and "Awaiting response" is unchanged |
| AC-16 | A request with an email, and one without | Email is turned off on the first; the second page loads; `notifyCustomer: true` is posted for the second | The first sends no email and audits `notified = "none"`; the second shows the Email toggle off and disabled with its helper text; the post returns `400` `errors.notifyCustomer`; SMS is always disabled with "SMS isn't available yet." |
| AC-17 | The email sender throws | A schedule, and a cancel, with Email on are submitted | Each response is `200`; the scheduled assessment and its transition, and the cancellation and its transition, persist and are never rolled back; the log contains only the request id and failure category |
| AC-18 | An `assessment_scheduled` request | **Cancel assessment** is confirmed with the checkbox on (page) or off (panel), and dismissed with **Keep** | Confirm: the assessment becomes `cancelled`, the request returns to `needs_review`, the cancellation email is sent only when checked, the page navigates to the request; Keep: nothing changes |
| AC-19 | The form has unsaved changes, in reschedule mode | **Cancel** or **Back to request** is pressed, then the discard dialog is confirmed or dismissed | The shared discard-changes dialog appears (never the cancel-assessment confirm); confirm returns to `/requests?request=<id>` and the assessment stays `scheduled` with no data change; dismiss keeps the page and data. An unchanged form leaves without the dialog. **Cancel assessment** is a separate destructive action |
| AC-20 | A successful schedule | The response arrives | The page navigates to `/requests?request=<id>`, the panel shows the assessment date/time, technician and purpose, the card is in **Assessment scheduled**, and the toast "Assessment scheduled." is announced |
| AC-21 | A `needs_review` request | The manager leaves the page without scheduling and presses **Mark ready for quote** in the panel | The request moves to `ready_for_quote` with no assessment created (existing behavior preserved) |
| AC-22 | Every assessment mutation of this feature | Audit rows and logs are inspected | Each operation writes one row with the existing action and fields plus `notified`; none contains purpose, instructions, message text, recipient email or names |
| AC-23 | Desktop, tablet and mobile widths | The page is used | Layouts follow the responsive rules with no horizontal page scroll; technician cards work as a keyboard radio group; the conflict error and preview updates are announced |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Slot state derivation and "available after" search across windows, breaks, exceptions, commitments and branch time zone (table-driven) | AC-06 |
| Backend unit | Week workload counting visits and assessments, excluding the rescheduled one | AC-07 |
| Backend integration | Planner and calendar: eligible technicians, branchless branch handling, states, labels, range/foreign technician `400` | AC-05, AC-06, AC-08 |
| Backend integration | Schedule/reschedule happy paths with purpose/instructions, warning accepted, field validation (parameterized) | AC-09, AC-10, AC-13 |
| Backend integration | Conflict `409` codes (assessment, visit, time off) and concurrent booking of one technician | AC-11, AC-12 |
| Backend integration | Notification: email after commit for schedule/reschedule/cancel, opt-out, missing email `400`, send failure tolerance, audit `notified` without content | AC-15, AC-16, AC-17, AC-18, AC-22 |
| Authorization/tenant isolation | One cross-organization `404` (planner + mutation) and `403` for a read role and Technician on planner/calendar | AC-02, AC-03 |
| Frontend component/service | Page modes and states (schedule, reschedule prefill, non-schedulable, forbidden, branchless) and panel navigation replacing the dialog | AC-01, AC-02, AC-03, AC-05 |
| Frontend component/service | Form: defaults, derived window/end, preview text, Email/SMS toggle states | AC-04, AC-14, AC-16 |
| Frontend component/service | Submit: success navigation and toast; `409` technician conflict inline with data kept; `request_changed` handling | AC-11, AC-20 |
| Frontend component/service | Cancel assessment confirm with notify checkbox; discard-changes dialog on leave | AC-18, AC-19 |
| Browser (final audit) | User visual QA only (no Playwright): desktop fidelity to Design 2, tablet and mobile layouts, calendar views, one schedule flow end to end | AC-04, AC-08, AC-20, AC-23 |

There are 4 backend integration groups and 4 frontend groups, within the
defaults. AC-21 is existing behavior covered by the `requests-pipeline` tests,
and the concurrency group covers a high-risk boundary this feature creates. The
existing `requests-pipeline` overlap test that expects `400` must be updated to
the BR-09 `409`.

## Dependencies

- `requests-pipeline` (AUDITED): assessment entity and endpoints, transitions,
  panel, confirm dialog and scope rules. This spec supersedes its BR-15
  overlap `400` (now BR-09 `409`) and its schedule/reschedule dialog
  (now FR-01). Every other rule is unchanged.
- `team-technician-management` and `technician-skills-availability`:
  technician profiles, weekly availability, breaks, exceptions, skills,
  BR-03/BR-04/BR-06 time and workload rules, and the availability calculator.
  Implemented.
- `public-service-request`: request availability preferences. AUDITED.
- Shared discard-changes dialog, confirm-dialog and toast patterns. Implemented.
- `IEmailSender` and the `RequestInformationNotifier` precedent. Implemented.
- Schema amendment BR-12 / Data impact item 1 (approved; needs
  `--generate-migration`).

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | The design file path is `design/assets/2- design.png` (with a space); the request named `2-design.png`. |
| AS-02 | Calendar range navigation and view choice are page state only. Only the request id is in the URL. |
| AS-03 | The technician card "Plumbing" line is the profile's primary skill. A technician with no primary skill shows no second line. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Route and entry point | a) dedicated page; remove schedule dialog · b) keep both | No | a) — user 2026-10-05 |
| OD-02 | Page permissions | a) managers only · b) read-only view for read roles | No | a) — user 2026-10-05 |
| OD-03 | Arrival window vs duration | a) fixed 1-hour window, end = start + duration, no schema change · b) persist window end | No | a) — user 2026-10-05 |
| OD-04 | Purpose storage | a) new `assessments.purpose varchar(500)` · b) in `internal_notes` · c) drop field | No | a); migration generated, never applied — user 2026-10-05 |
| OD-05 | Assessment ID | a) not rendered · b) new numbering | No | a) — user 2026-10-05 |
| OD-06 | Conflict enforcement | a) block assessments, active visits, time off; warn outside availability · b) also block outside availability · c) assessment overlap only | No | a), with blocking conflicts returning `409` (not `400`); format/validation stays `400` — user 2026-10-05 |
| OD-07 | Card state and workload | a) slot-based state; weekly workload counting visits and assessments · b) reuse visits-only workload | No | a) — user 2026-10-05 |
| OD-08 | Eligible technicians | a) active, request branch; branch chosen first when missing; required on page, optional in API · b) optional on page | No | a), refined: the technician is mandatory in the whole flow, API included (BR-20) — user 2026-10-05 |
| OD-09 | Time zone | a) organization time for contract; availability converted from branch time · b) branch time throughout | No | a) — user 2026-10-05 |
| OD-10 | Customer notification | a) email after commit, audit only, SMS disabled · b) also `notifications` row · c) hide SMS | No | a) — user 2026-10-05 |
| OD-11 | Cancel from page | a) Cancel assessment with notify checkbox; Cancel discards · b) cancel without notify | No | a) — user 2026-10-05 |
| OD-12 | Calendar | a) read-only Week/Day/Agenda, no travel, no click selection · b) click to fill | No | a) — user 2026-10-05 |
| OD-13 | Copy deviations | a) notice names Mark ready for quote; stepper; Complete stays in panel · b) other | No | a) — user 2026-10-05 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-02, AC-03, AC-19 |
| FR-02 | AC-04, AC-23 |
| FR-03 | AC-05, AC-06, AC-07 |
| FR-04 | AC-08 |
| FR-05 | AC-09, AC-10, AC-13 |
| FR-06 | AC-11, AC-12 |
| FR-07 | AC-14, AC-15, AC-16, AC-17 |
| FR-08 | AC-18 |
| FR-09 | AC-15, AC-22 |
| FR-10 | AC-20 |
| FR-11 | AC-02, AC-03, AC-05 |
| FR-12 | AC-21 |

## Change log

| Date | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-05 | — → DRAFT | Created; decisions OD-01…OD-13 resolved by user in one round (OD-06 with `409` for schedule conflicts) |
| 2026-10-05 | DRAFT → DRAFT | User clarifications: technician mandatory in API (BR-20); cancel assessment vs cancel edit separated (BR-18, AC-19); email failure never rolls back schedule or cancel (AC-17) |
| 2026-10-05 | DRAFT → DRAFT | Validation fixes: FR-12 for optional assessment traced to AC-21; AC-14 aligned with BR-13; planner `branchId` on a branched request → `400` (BR-04); BR-20 in transition guards and error table; BR-20 row moved after BR-19 |
| 2026-10-05 | DRAFT → APPROVED | Approved by user via /spec approve |
