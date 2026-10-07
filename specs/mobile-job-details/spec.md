# Mobile job details

| Field    | Value                |
| -------- | -------------------- |
| Feature  | `mobile-job-details` |
| Type     | Full-stack           |
| Status   | APPROVED             |
| Created  | 2026-10-07           |
| Updated  | 2026-10-07           |
| Approved | 2026-10-07           |

## Context and objective

`technician-todays-jobs` delivered a minimal read-only job page at
`/today/visits/:visitId`. This feature delivers Design 8, **Job details**: the
technician's preparation and travel view with the work, customer, access,
instructions, scope, required skills, planned materials and assessment
evidence, plus the first field transitions: **Start travel** (idempotent
`assigned → on_the_way`, history, travel time entry and a customer email after
commit) and **I've arrived** (closes the travel entry). Starting the job belongs
to Design 9.

## Actors and permissions

| Actor                                                                                   | Can                                                                                                                                                       | Cannot                                                                                       |
| --------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| Technician (`technician`) with an `active` linked profile, **primary** on the visit     | Read the job details and assessment photos of visits actively assigned to them; **Start travel** and **I've arrived** (BR-07–BR-10); Call, Directions, Call office | Start the job, edit tasks/materials/photos, report incidents, message the customer, undo travel |
| Technician with an `active` linked profile, actively assigned but **not primary**       | Read the same job details and photos                                                                                                                      | Start travel or record arrival (`403 not_primary_technician`)                                |
| Technician with `inactive`/`suspended` profile, or without linked profile               | —                                                                                                                                                         | Every endpoint (`403 technician_inactive` / `404 technician_profile_not_linked`)              |
| Owner, Operations Manager, Dispatcher, Viewer, Accounting, unknown roles                | —                                                                                                                                                         | Every endpoint (`403`); existing forbidden state                                             |

## Scope

- Extended `GET /technician/visits/{visitId}` (additive) and the Design 8 page
  at `/today/visits/:visitId` inside the existing technician shell.
- `POST /technician/visits/{visitId}/start-travel` and
  `POST /technician/visits/{visitId}/arrive`.
- `GET /technician/visits/{visitId}/assessment-photos/{photoId}`.
- Customer "on the way" email after commit.
- Read-only Overview, Tasks, Materials and Photos tabs; **View approved
  assessment** panel; **Report delay or issue** panel.
- Today integration: `isPrimary` in `TodayVisit` and **Start travel** on the
  next job card performing the transition (BR-15).

## Non-goals

- GPS, location tracking, ETA, "On time", "Live", distances, travel or drive
  times other than the recorded travel duration, maps, route optimization.
- **Message**, messaging, SMS, `notifications` rows, technician notifications.
- Material availability or readiness ("Ready"), material consumption, quantity
  confirmation.
- **Start job** behavior, pause, completion, checklist completion, evidence
  upload, incidents, sign-off (Design 9 and later).
- An `arrived` status, undoing travel, changing the primary technician.
- Work order status changes; schema changes or migrations.

## User flow

1. On Today, the primary technician taps **Start travel** on the next job card;
   the system records the transition and opens `/today/visits/:visitId`.
2. Alternatively the technician opens the job page and taps **Start travel**
   there; the page updates in place to "On the way".
3. The customer receives an "on the way" email when configured (BR-11).
4. The technician reviews access, instructions, scope, skills, materials,
   tasks and assessment photos, taps **Open directions** or **Call**.
5. On site, the technician taps **I've arrived**; the page shows "Arrived" and
   the travel duration. **Start job** stays disabled.
6. **Report delay or issue** opens a panel with **Call office**.
7. The back button returns to `/today`, which reloads.

## Functional requirements

| ID    | Requirement                                                                                                                                                         |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| FR-01 | Every endpoint must resolve organization, membership and profile from the session and authorize the visit per BR-01; mutations additionally require BR-02.         |
| FR-02 | `GET /technician/visits/{visitId}` must return the additive Design 8 detail per BR-03–BR-05.                                                                         |
| FR-03 | Assessment photos must be served only for the caller's visit per BR-06 and shown read-only.                                                                        |
| FR-04 | **Start travel** must transition `assigned → on_the_way` with history, travel entry and audit, under the guards of BR-07 and BR-08.                                |
| FR-05 | **I've arrived** must close only the open travel entry per BR-09.                                                                                                    |
| FR-06 | Both transitions must be idempotent and serialized per BR-10.                                                                                                        |
| FR-07 | The customer must be emailed after commit only on an effective Start travel per BR-11.                                                                              |
| FR-08 | The page must show the stepper, status banner, header, access card and action bar per BR-12–BR-14.                                                                  |
| FR-09 | Today must use `isPrimary` and perform Start travel from the next job card per BR-15; the back button returns to Today.                                             |
| FR-10 | Tabs, assessment panel and Report delay or issue must be read-only per BR-16–BR-18.                                                                                  |
| FR-11 | The page must reproduce Design 8 inside the Design 7 shell with the deviations, states and accessibility of BR-19 and BR-20.                                        |

## Business and validation rules

| ID    | Rule                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         | Enforced by |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------- |
| BR-01 | Authorization reuses `technician-todays-jobs` BR-01 (policy TechnicianSelf), BR-02 (profile resolution, `technician_profile_not_linked`, `technician_inactive` checked before any read or write) and BR-03 (visibility by active assignment of the caller's profile; any other visit → identical `404` with no foreign data). The visit must not be `unscheduled` or `cancelled` (else `404`), on any date. No technician or organization id is accepted from the client.                                                                                                                                                                                                                                                                                                                                         | Backend     |
| BR-02 | Primary control: only the technician whose active assignment has `is_primary = true` may call start-travel or arrive. An actively assigned non-primary technician → `403` with `code` `not_primary_technician`, message "The primary technician manages travel for this job.", no write. The detail returns `isPrimary` for the caller.                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Backend     |
| BR-03 | Detail content (additive to `TechnicianVisitDetail`): `isPrimary`; `customerType` (`person` → "Residential", `company` → "Commercial"); `access` = property `access_instructions` (null allowed), `contactPreference` from the customer's primary active contact (`prefers_email` and `prefers_sms` → `email_or_sms`, only email → `email`, only SMS → `sms`, no such contact → null), `activeDamage` = `has_active_damage` of the work order's originating service request (work order → quote version → quote → request); `instructions` = work order `internal_instructions` (null allowed); `scope` = `scope_snapshot`; `requiredSkills` = names of `work_order_required_skills` ordered by name; `officePhone` = `organizations.phone` (null allowed); BR-04 travel; BR-05 lists; `assessment` per BR-06 or null. | Backend     |
| BR-04 | Travel: `travel = { startedAt \| null, arrivedAt \| null, durationMinutes \| null }` from the visit's most recent `visit_time_entries` row with `entry_type = 'travel'`: `startedAt` = `started_at`, `arrivedAt` = `ended_at`, `durationMinutes` = whole minutes of `ended_at − started_at` rounded down (null while open). Arrival is recorded exactly when that entry has `ended_at`; there is no `arrived` status.                                                                                                                                                                                                                                                                                                                                                                                         | Backend     |
| BR-05 | Lists: `plannedMaterials` = `work_order_planned_materials` ordered by `sort_order`, then description: `{ description, quantity, unit, source }`; `tasks` = the visit's `visit_checklist_items` ordered by `sort_order`, then label: `{ id, label, isRequired, isCompleted }`. No readiness or availability is computed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       | Backend     |
| BR-06 | Assessment: the latest `completed` assessment (by `completed_at`) of the originating service request, the same selection `quote-builder` uses for the assessment shown while quoting: `{ completedAt, diagnosis \| null, recommendedScope \| null, photos: [{ id }] }` with `assessment_attachments` ordered by `created_at`; `internal_notes` is never returned. `GET …/assessment-photos/{photoId}` authorizes the visit per BR-01 and returns the image only when the photo belongs to that assessment of the same organization; otherwise identical `404`. The response carries the stored image content type (`image/jpeg` or `image/png`), `Content-Disposition: inline`, `X-Content-Type-Options: nosniff` and `Cache-Control: no-store`.                                                                                          | Backend     |
| BR-07 | Start travel guards, evaluated after BR-01 and BR-02 in this order: status `on_the_way` → `200` with `changed = false` and no write, email or audit (idempotent, any date); status other than `assigned` → `409 visit_status_invalid` "Travel cannot be managed for this job in its current state."; `scheduled_start` not inside today per `technician-todays-jobs` BR-04 (profile branch zone, organization fallback) → `409 visit_not_today` "Travel can only be started on the day of the visit."; another visit actively assigned to the caller (primary or not) with status `on_the_way` or `in_progress` → `409 another_visit_active` "You're already traveling to or working on another job.". No write on any error.                                                                       | Backend     |
| BR-08 | Start travel effect, one transaction: `visits.status = on_the_way`, `updated_at = now`; one `visit_status_history` row (`assigned → on_the_way`, `changed_by_user_id` = actor, reason null, `changed_at` = now); one `visit_time_entries` row (`technician_id` = caller's profile, `entry_type = 'travel'`, `started_at` = now, `ended_at` null); one `visit.travel_started` audit row (`entity_type` `visit`, visit id, organization, work order `branch_id`, actor, before/after `{ status }`, metadata `{ workOrderId, visitNumber, notified ("email" \| "none") }`). The work order status is unchanged. Response `200 { changed: true, visit: TechnicianVisitDetail }`.                                                                                                                                 | Backend     |
| BR-09 | Arrive: requires status `on_the_way`. The visit's open travel entry (`ended_at IS NULL`) of the caller → set `ended_at = now`, `updated_at` of the visit = now, one `visit.arrived` audit row (metadata `{ workOrderId, visitNumber, travelMinutes }`), `200 { changed: true, visit }`. Only that entry is changed; the status stays `on_the_way` and no history row is written. Status `on_the_way` with the travel entry already closed → `200 { changed: false, visit }` with no write. Any other status, `on_the_way` without a travel entry, or `on_the_way` whose open travel entry belongs to another technician's profile → `409 visit_status_invalid` "Travel cannot be managed for this job in its current state.". No date restriction. Arrival never sends email.                                                                                                     | Backend     |
| BR-10 | Concurrency: each mutation locks the caller's profile row, then the visit row, in that order, inside its transaction, and re-reads status and travel entry after locking. Concurrent identical requests produce exactly one transition, one history row, one travel entry (or one closure), one audit row and at most one email; the others return `200 changed = false`. Concurrent Start travel on two visits of the same technician serialize on the profile lock: one succeeds, the other returns `409 another_visit_active`. Dispatch is already blocked for `on_the_way` visits (`dispatch-calendar` BR-13).                                                                                                                                                                                          | Backend     |
| BR-11 | Customer email, only after the commit of an effective Start travel (`changed = true`), never on idempotent retries, errors or arrival. Sent through the existing email delivery used by `dispatch-calendar` BR-17 when the work order has `send_arrival_reminder = true` and the customer's primary active contact has an email. Subject: "<organization name>: your technician is on the way for <WO-n>". Body: "Hi <contact first name or 'there'>," then "Your technician is on the way for <title> at <property address_line1>.", "Scheduled arrival window: <h:mm a> – <h:mm a>." (or "Scheduled arrival: <h:mm a>." when equal; omitted when null) in the profile branch zone, "Your technician: <primary technician full name>." when `send_technician_details`, the organization name and "Questions? Call us at <organization phone>." when set. Never contains ETA, instructions, dispatch note, access details or technician contact data. A send failure logs only the visit id and failure category; the response stays success. No `notifications` rows. | Backend     |
| BR-12 | Stepper "Scheduled → On the way → Arrived → In progress": `assigned` → Scheduled current; `on_the_way` without arrival → Scheduled done, On the way current; `on_the_way` with arrival → On the way done, Arrived current; `in_progress`, `paused` → first three done, In progress current; `completed`, `needs_correction`, `approved` → all done. Each step exposes its state (done/current/upcoming) as text to assistive technology. Status banner: `on_the_way` without arrival → "On the way • Started <h:mm a>"; with arrival → "Arrived • <h:mm a>" and "Travel time <n> min"; `assigned` → no banner; other statuses → the `technician-todays-jobs` BR-13 status label.                                                                                                   | Frontend    |
| BR-13 | Header card: title, priority chip (Urgent/High only, as `technician-todays-jobs` BR-10), "#<prefix>-<n>", customer name, one-line address, "<h:mm a> – <h:mm a>" with "Scheduled window", the arrival window line of `technician-todays-jobs` BR-10, **Open directions** and **Call** per `technician-todays-jobs` BR-12. Access details card: "Access details" with the Residential/Commercial chip; access instructions line (hidden when null); "Contact preference: Email" / "Text message" / "Email or text message" (hidden when null); when `activeDamage`, a warning "Active damage reported by the customer. Check the site before starting." with a warning icon and text; with no lines, "No access details provided.". All times in the response time zone.                                   | Frontend    |
| BR-14 | Action bar, fixed above the bottom navigation. Primary technician: `assigned` → **Start travel** (primary) and disabled **Start job** with "Available after arrival is recorded"; `on_the_way` without arrival → **I've arrived** (primary) and the same disabled Start job; `on_the_way` with arrival → disabled "Arrived <h:mm a>" and disabled Start job with "Starting the job will be available soon."; other statuses → no travel or Start job controls and the text "Travel cannot be managed for this job in its current state.". Non-primary → no travel controls, the text "The primary technician manages travel for this job." and Start job disabled per the same arrival rule. A pending action shows progress and blocks repeats. A `409` shows its message inline and reloads the visit; other failures show "We couldn't update this job. Try again." and keep the page. **Report delay or issue** is always shown below. | Frontend    |
| BR-15 | Today integration: `TodayVisit` gains `isPrimary`. On the next job card, **Start travel** is shown only when the visit is `assigned` and `isPrimary`; it calls start-travel, then opens the job page on success; a failure shows the BR-14 message inline on the card and stays. Otherwise the card shows only **View job details**. Route rows and View job details keep navigating without writes. The job page back button (top bar, accessible name "Back to Today's jobs") opens `/today`, which loads fresh data.                                                                                                                                                                                                                             | Frontend    |
| BR-16 | Tabs (Overview default, Tasks, Materials, Photos), keyboard-operable with tab semantics, all read-only. **Overview**: "Technician instructions" (instructions, then "Dispatch note" when set; section hidden when both null); "Scope of work" as a bulleted list of the non-empty scope lines with a leading `-`, `*` or `•` removed; "Required skills" chips (hidden when none) with **View approved assessment** when an assessment exists; "Assessment photos (<n>)" thumbnails (hidden when 0); "Planned materials" with "<n> planned materials" ("1 planned material"), up to 3 rows "<description> · <quantity> <unit>" with the source label ("Truck stock", "Warehouse", "To purchase") and the note "Confirm quantities after starting the job." (hidden when 0). **Tasks**: "<completed> of <total> tasks", each label with "Required" when required and a disabled checkbox reflecting `isCompleted`, note "Tasks can be completed after starting the job."; empty "No tasks for this job.". **Materials**: every planned material as on Overview; empty "No planned materials.". **Photos**: every assessment photo; empty "No assessment photos.". | Frontend    |
| BR-17 | Photos and assessment panel: thumbnails load from the BR-06 endpoint with alt text "Assessment photo <k> of <n>"; activating one opens a full-size viewer with close and previous/next controls and focus returned on close; a failed image shows "Photo unavailable.". **View approved assessment** opens a panel "Approved assessment" with "Completed <EEE, MMM d>", "Diagnosis" and "Recommended scope" (each hidden when null) and the photos.                                                                                                                                                                                                                                                                                                                                                        | Frontend    |
| BR-18 | **Report delay or issue** opens a panel titled "Report delay or issue" with **Call office** (`tel:` to `officePhone`, hidden when null), the text "Issue reporting is coming soon." and **Close**. No request is sent.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      | Frontend    |
| BR-19 | Design 8 deviations: technician shell of Design 7 kept (bottom navigation with Today current); top bar shows the back button and "Job details" with the existing bell and avatar and no ⋮ menu; no "Live" chip, "Estimated arrival", "On time", "22 min • 8.4 mi", **Message**, readiness "Ready" or map. "Material readiness" becomes "Planned materials". Data in the mockup are examples.                                                                                                                                                                                                                                                                                                                                                                                                     | Frontend    |
| BR-20 | Responsive and accessibility: `technician-todays-jobs` BR-19 applies; the action bar and bottom navigation never cover content at 375 px; status changes after an action are announced politely; banner and stepper never rely on color alone; targets ≥ 44 × 44 px; visible focus.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | Frontend    |

## States and transitions

| From                       | To                         | Trigger      | Actor              | Guard                                                                                  |
| -------------------------- | -------------------------- | ------------ | ------------------ | -------------------------------------------------------------------------------------- |
| `assigned`                 | `on_the_way`               | Start travel | Primary technician | BR-01, BR-02, BR-07; today; no other `on_the_way`/`in_progress` visit of the caller    |
| `on_the_way`               | `on_the_way` (no change)   | Start travel | Primary technician | Idempotent `200 changed = false`                                                       |
| `on_the_way` (travel open) | `on_the_way` (arrived)     | I've arrived | Primary technician | BR-09; closes only the open travel entry; no history row                               |
| `on_the_way` (arrived)     | `on_the_way` (no change)   | I've arrived | Primary technician | Idempotent `200 changed = false`                                                       |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): all tables of
  `technician-todays-jobs`, plus `visit_assignments.is_primary`,
  `visit_time_entries`, `visit_checklist_items`, `work_order_planned_materials`
  (`description`, `quantity`, `unit`, `source`, `sort_order`),
  `work_order_required_skills`, `skills` (`name`), `work_orders`
  (`internal_instructions`, `scope_snapshot`, `quote_version_id`,
  `send_arrival_reminder`, `send_technician_details`), `quote_versions`,
  `quotes` (`request_id`), `service_requests` (`has_active_damage`),
  `assessments` (`status`, `completed_at`, `diagnosis`, `recommended_scope`),
  `assessment_attachments`, `customers` (`type`), `customer_contacts`
  (`first_name`, `email`, `prefers_email`, `prefers_sms`), `properties`
  (`access_instructions`), `organizations` (`name`, `phone`).
- Written: `visits` (`status`, `updated_at`), `visit_status_history`,
  `visit_time_entries` (insert; `ended_at` update), `audit_logs`.
- Not written: `notifications`, `work_orders`, `visit_assignments`, checklist,
  materials, evidence, incidents.
- Schema amendments required: None. No migration.

## Tenant isolation and authorization

- Organization and profile resolved from the session (BR-01).
- Client-provided identifiers: `visitId` and `photoId` (path), authorized
  against the session organization, an active assignment of the caller's
  profile and, for photos, the visit's assessment; otherwise identical `404`.
- Mutations require the primary assignment (BR-02); frontend hiding is UX only.
- Responses use `Cache-Control: no-store`. Logs and audit never contain
  addresses, phones, emails, customer names, instructions, notes or the email
  body.

## API contracts

Contract status: Final. Errors use ProblemDetails with `code` where listed.

`TodayVisit` (from `technician-todays-jobs`) gains `isPrimary`.

`TechnicianVisitDetail` = existing fields & `{ isPrimary, customerType,
access: { instructions | null, contactPreference: "email" | "sms" |
"email_or_sms" | null, activeDamage }, instructions | null, scope,
requiredSkills: string[], officePhone | null, travel: { startedAt | null,
arrivedAt | null, durationMinutes | null }, plannedMaterials: [{ description,
quantity, unit, source }], tasks: [{ id, label, isRequired, isCompleted }],
assessment: { completedAt, diagnosis | null, recommendedScope | null, photos:
[{ id }] } | null }`.

`TravelResult` = `{ changed, visit: TechnicianVisitDetail }`.

| Method | Path                                                     | Request | Success              | Errors                                                                                                                                                         | Permission     |
| ------ | -------------------------------------------------------- | ------- | -------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------- |
| GET    | `/technician/today`                                      | —       | `200` as `technician-todays-jobs`, with `visits: TodayVisit[]` including `isPrimary` | `403` (role; `technician_inactive`) · `404 technician_profile_not_linked`                                                                 | TechnicianSelf |
| GET    | `/technician/visits/{visitId}`                           | —       | `200 TechnicianVisitDetail` | `403` (role; `technician_inactive`) · `404` (BR-01; `technician_profile_not_linked`)                                                                    | TechnicianSelf |
| POST   | `/technician/visits/{visitId}/start-travel`              | —       | `200 TravelResult`   | `403` (role; `technician_inactive`; `not_primary_technician`) · `404` · `409` (`visit_status_invalid`, `visit_not_today`, `another_visit_active`)               | TechnicianSelf |
| POST   | `/technician/visits/{visitId}/arrive`                    | —       | `200 TravelResult`   | `403` (role; `technician_inactive`; `not_primary_technician`) · `404` · `409 visit_status_invalid`                                                              | TechnicianSelf |
| GET    | `/technician/visits/{visitId}/assessment-photos/{photoId}` | —     | `200` image bytes (BR-06) | `403` (role; `technician_inactive`) · `404`                                                                                                              | TechnicianSelf |

Mutations rely on the existing cookie session (`HttpOnly`, `Secure`,
`SameSite=Strict`); no new CSRF mechanism is introduced. The `POST` requests
carry no body.

## UI behavior and states

| Screen                   | Loading                                                   | Empty                                         | Error                                                                                                                                       | Permission                                                         | Success      |
| ------------------------ | --------------------------------------------------------- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------ | ------------ |
| `/today/visits/:visitId` | Skeleton stepper, header, access card and tabs            | Per-tab empty texts (BR-16)                   | `404`: "This job isn't available." with **Back to Today's jobs**; load failure: "We couldn't load this job. Try again." with **Retry**; action errors per BR-14 | As `technician-todays-jobs`; non-primary read-only (BR-14)         | BR-12–BR-18  |
| `/today` next job card   | Start travel shows progress                               | As `technician-todays-jobs`                   | BR-15 inline message                                                                                                                         | Start travel hidden for non-primary or non-`assigned`              | BR-15        |

- Mockups: `design/assets/8-design.png` (Approved; primary reference) with the
  BR-19 deviations; shell from `design/assets/7-design.png`.
- Responsive and accessibility: BR-20.

## Error behavior

| Case                                                              | Response / message shown                                                       | Data change |
| ----------------------------------------------------------------- | ------------------------------------------------------------------------------ | ----------- |
| Visit not visible to caller, `unscheduled`/`cancelled`, foreign photo | `404`; "This job isn't available." / "Photo unavailable."                      | None        |
| Non-primary mutation                                              | `403 not_primary_technician`; "The primary technician manages travel for this job." | None        |
| Start travel from a status other than `assigned`/`on_the_way`; arrive without open or closed travel on `on_the_way` | `409 visit_status_invalid`; "Travel cannot be managed for this job in its current state." | None |
| Start travel on another day                                       | `409 visit_not_today`; "Travel can only be started on the day of the visit."   | None        |
| Another visit `on_the_way`/`in_progress`                          | `409 another_visit_active`; "You're already traveling to or working on another job." | None   |
| Repeat Start travel / I've arrived                                | `200 changed = false`                                                          | None        |
| Email send failure                                                | `200`; visit id and failure category logged                                    | Saved       |
| Other mutation failure                                            | "We couldn't update this job. Try again."                                      | None        |

## Acceptance criteria

| ID    | Given                                                                                                                                                                      | When                                                                                  | Then                                                                                                                                                                                                                                                                                  |
| ----- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-01 | Another technician's visit, a visit unassigned from the caller, a visit of another organization and a random id                                                            | Detail, start-travel, arrive and the photo endpoint are called for each                | Every call returns the identical `404` with no foreign data and no write                                                                                                                                                                                                             |
| AC-02 | Other roles (parameterized), an unlinked technician and an inactive/suspended technician                                                                                     | They call start-travel and arrive                                                      | Other roles `403`; unlinked `404 technician_profile_not_linked`; inactive/suspended `403 technician_inactive`; no write                                                                                                                                                              |
| AC-03 | A visit with a primary and a non-primary active assignee                                                                                                                    | Both open the detail; the non-primary calls start-travel and arrive                    | Both read the detail with their `isPrimary`; the non-primary gets `403 not_primary_technician` with no write, and the page shows "The primary technician manages travel for this job." without travel controls                                                                        |
| AC-04 | A visit with instructions, dispatch note, multi-line scope with bullet prefixes, two required skills, planned materials of each source, checklist items, a completed assessment with photos, access instructions, a contact preferring email and SMS, an active-damage request and a company customer | The detail is loaded                                                                   | The response and page contain BR-03–BR-06 values: "Commercial", contact preference "Email or text message", the damage warning, bullets without prefixes, skills by name, materials with source labels, tasks in order, assessment diagnosis/scope without `internal_notes`              |
| AC-05 | A minimal visit: no instructions, note, skills, materials, tasks, assessment, access instructions, primary contact or office phone; person customer                         | The page renders                                                                        | "Residential"; "No access details provided."; empty-state texts in Tasks, Materials and Photos; Overview hides empty sections; no **View approved assessment**; **Call office** hidden                                                                                               |
| AC-06 | A photo of the visit's assessment, a photo of another assessment of the same organization and a photo of another organization                                              | The photo endpoint is called                                                           | Only the first returns the image with its content type, `inline`, `nosniff` and `no-store`; the others return `404`                                                                                                                                                                  |
| AC-07 | A primary technician with an `assigned` visit today                                                                                                                         | Start travel is called                                                                 | `200 changed = true`; status `on_the_way`; one history row `assigned → on_the_way` by the actor; one open `travel` entry for the caller's profile; one `visit.travel_started` audit row without sensitive data; work order status unchanged                                             |
| AC-08 | A visit already `on_the_way`                                                                                                                                                | Start travel is called again (also on a later date)                                    | `200 changed = false`; no new history, time entry, audit row or email                                                                                                                                                                                                                |
| AC-09 | Visits in each status other than `assigned`/`on_the_way`, an `assigned` visit of yesterday and tomorrow, and an `assigned` visit while the caller has another `on_the_way` and another `in_progress` visit (table-driven) | Start travel is called                                                                 | `409 visit_status_invalid` with "Travel cannot be managed for this job in its current state.", `visit_not_today` or `another_visit_active` respectively; no write                                                                                                                     |
| AC-10 | An `on_the_way` visit with an open travel entry                                                                                                                             | Arrive is called twice                                                                 | The first returns `200 changed = true`, sets only that entry's `ended_at`, keeps status `on_the_way`, writes one `visit.arrived` audit row and no history row; `travel.durationMinutes` = floored minutes; the second returns `200 changed = false` with no write                     |
| AC-11 | An `assigned` visit, an `in_progress` visit and an `on_the_way` visit without travel entry                                                                                  | Arrive is called                                                                       | Each returns `409 visit_status_invalid` with the BR-09 message and no write                                                                                                                                                                                                          |
| AC-12 | Two concurrent start-travel requests for the same visit, two concurrent arrive requests, and concurrent start-travel for two `assigned` visits of the same technician       | They run concurrently                                                                  | Same visit: exactly one `changed = true`, one history row, one travel entry (or one closure), one audit row, at most one email; two visits: one succeeds and the other returns `409 another_visit_active`                                                                            |
| AC-13 | Work orders with `send_arrival_reminder` true/false, contact with/without email, `send_technician_details` true/false, an arrival window and a failing sender (table-driven, fake `IEmailSender`) | Start travel succeeds, is repeated and arrive is called                               | Exactly one email after commit only when the reminder is on and an email exists, with the BR-11 subject/body (technician line per flag, no ETA or sensitive data); none on repeat or arrive; a send failure still returns `200` and logs only visit id and category                    |
| AC-14 | Visits in `assigned`, `on_the_way` without and with arrival, `in_progress` and `completed`                                                                                  | The page renders                                                                       | Stepper states and banner texts follow BR-12, with step states exposed as text                                                                                                                                                                                                       |
| AC-15 | The primary technician on `assigned`, `on_the_way` without arrival, `on_the_way` with arrival and `paused`                                                                  | The action bar renders and Start travel / I've arrived are used                         | Controls and helper texts follow BR-14; Start job is always disabled; a successful action updates the page in place and announces it; a pending action blocks repeats; a `409` shows its message and reloads; another failure shows "We couldn't update this job. Try again."        |
| AC-16 | Today with an `assigned` next visit where the caller is primary, one where they are not, and an `on_the_way` next visit                                                     | Start travel / View job details are used, start-travel succeeding once and failing once | Start travel appears only for the first; success opens the job page after the POST; failure shows the inline message and stays; the other cards only navigate; the job page back button opens `/today` with fresh data                                                              |
| AC-17 | The job page with materials, tasks and photos                                                                                                                               | Each tab, a thumbnail and **View approved assessment** are used                         | Tabs show BR-16 read-only content with disabled checkboxes; the photo viewer opens with alt texts, previous/next and close returning focus; the panel shows completed date, diagnosis, recommended scope and photos                                                                  |
| AC-18 | A visit with and without office phone                                                                                                                                       | **Report delay or issue** is used                                                       | The panel shows **Call office** as a `tel:` link (hidden without phone), "Issue reporting is coming soon." and **Close**; no request is sent                                                                                                                                         |
| AC-19 | The job page with coordinates and a primary contact phone, and a variant without either                                                                                     | Header actions render                                                                   | **Open directions** and **Call** follow `technician-todays-jobs` BR-12 (coordinates or encoded address, new tab announced; `tel:` or hidden); no Message, Live, ETA, distance or ⋮ menu exists                                                                                       |
| AC-20 | Viewports 375 px and 1280 px                                                                                                                                                | The page is used by keyboard and touch                                                  | Design 8 layout inside the Design 7 shell with BR-19 deviations; no page-level horizontal scroll; nothing hidden behind the action bar or bottom navigation; centered ≤ 480 px column; targets ≥ 44 px; visible focus; status not conveyed by color alone                            |

## Testing requirements

| Level                          | Behavior or risk to prove                                                                                                                                                                                                                                  | Evidence for                     |
| ------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------- |
| Authorization/tenant isolation | One parameterized integration test: detail, start-travel, arrive and photo `404` for foreign/unassigned/other-organization visits and foreign photos; non-primary `403`; inactive `403` on mutations (role `403` reuses existing TechnicianSelf evidence) | AC-01, AC-02, AC-03, AC-06       |
| Backend integration            | Start travel effect and idempotent repeat; table-driven guards                                                                                                                                                                                             | AC-07, AC-08, AC-09              |
| Backend integration            | Arrive effect, idempotent repeat and invalid states                                                                                                                                                                                                        | AC-10, AC-11                     |
| Backend integration            | Concurrency: same-visit double start and double arrive; two visits of one technician                                                                                                                                                                      | AC-12                            |
| Backend integration            | Email after commit, table-driven with fake `IEmailSender`, including failure and no email on retries/arrival                                                                                                                                               | AC-13                            |
| Backend integration            | Detail content mapping (rich and minimal visit)                                                                                                                                                                                                            | AC-04, AC-05                     |
| Frontend component/service     | Job page: stepper/banner states, action bar per role and status with in-place update and error handling, tabs and empty states, photo viewer and assessment panel, Report delay panel; Today card Start travel flow (3–6 methods)                          | AC-03, AC-05, AC-14–AC-19        |
| User visual QA (manual)        | Design 8 fidelity, shell, responsive layout, focus, announcements                                                                                                                                                                                          | AC-20                            |

Backend integration stays within 3–8 methods; backend unit 0 unless a
non-trivial helper is extracted. Agents run no browser automation or
Playwright.

## Dependencies

- `technician-todays-jobs` (AUDITED): technician shell, `/today`, BR-01–BR-04,
  BR-10, BR-12, BR-13, BR-19, `TodayVisit` and `TechnicianVisitDetail`.
- `dispatch-calendar` (APPROVED): active and primary assignments, BR-13 lock of
  started visits, customer email composer precedent.
- `create-work-order` (AUDITED): planned materials, required skills, checklist
  items copied to visits, notification preferences.
- `schedule-assessment` (AUDITED) and `quote-builder` (AUDITED): completed
  assessment, diagnosis, recommended scope and inline-stored photos.

## Assumptions

| ID    | Assumption (minor, non-behavioral)                                                                                                         |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| AS-01 | At most one `travel` entry exists per visit because Start travel only leaves `assigned` and dispatch cannot revert `on_the_way`.            |
| AS-02 | `send_arrival_reminder` is the work order's "on the way" email switch; no scheduled reminder job is introduced.                            |
| AS-03 | Work order status moves to `in_progress` only when the job starts (Design 9), not on travel.                                               |
| AS-04 | Travel duration is derived from `started_at` and `ended_at`; no column stores it.                                                          |

## Open decisions

| ID    | Question                         | Options                                                         | Blocking | Resolution                                                                                                         |
| ----- | -------------------------------- | --------------------------------------------------------------- | -------- | ------------------------------------------------------------------------------------------------------------------ |
| OD-01 | Recording arrival                | Travel time entry · history row · schema amendment              | No       | `travel` entry opened by Start travel, closed by I've arrived; no schema change (BR-04, BR-09) — user 2026-10-07  |
| OD-02 | Start job                        | Visible disabled · hidden                                       | No       | Visible disabled with arrival-dependent helper (BR-14) — user 2026-10-07                                           |
| OD-03 | Report delay or issue            | Panel with Call office · `visit_incidents` · hidden             | No       | Panel with Call office and coming-soon text, no write (BR-18) — user 2026-10-07                                    |
| OD-04 | States, concurrency, audit       | Strict guards · relaxed                                          | No       | `assigned` only, today only, one active visit, row locks, audit, no undo (BR-07–BR-10) — user 2026-10-07           |
| OD-05 | Multiple technicians             | Primary only · any assignee                                     | No       | Primary controls travel; others read-only (BR-02) — user 2026-10-07                                               |
| OD-06 | Customer notification            | `send_arrival_reminder` · `notify_customer_when_scheduled`      | No       | `send_arrival_reminder` + contact email, only on effective transition (BR-11) — user 2026-10-07                    |
| OD-07 | Today Start travel and return    | Transition then navigate · navigate only                        | No       | Transition then navigate; back to fresh `/today` (BR-15) — user 2026-10-07                                         |
| OD-08 | Tabs and design adaptations      | Full read-only tabs · without Photos                            | No       | All tabs read-only with assessment photos and panel; omissions per BR-19 (BR-16, BR-17) — user 2026-10-07          |

## Traceability

| FR    | AC                                |
| ----- | --------------------------------- |
| FR-01 | AC-01, AC-02, AC-03               |
| FR-02 | AC-04, AC-05                      |
| FR-03 | AC-06, AC-17                      |
| FR-04 | AC-07, AC-09                      |
| FR-05 | AC-10, AC-11                      |
| FR-06 | AC-08, AC-10, AC-12               |
| FR-07 | AC-13                             |
| FR-08 | AC-14, AC-15, AC-19               |
| FR-09 | AC-16                             |
| FR-10 | AC-17, AC-18                      |
| FR-11 | AC-05, AC-20                      |

## Change log

| Date       | Status change | Reason  |
| ---------- | ------------- | ------- |
| 2026-10-07 | — → DRAFT     | Created |
| 2026-10-07 | DRAFT → DRAFT | Revised after validation: `GET /technician/today` contract row with `isPrimary`; CSRF statement corrected to the existing `SameSite=Strict` cookie session; dependency statuses; BR-09 open travel entry of another profile → `409`; implementation references replaced by spec rules (BR-06, BR-11) |
| 2026-10-07 | DRAFT → APPROVED | Approved by user via /spec approve |
