# Mobile job progress

| Field    | Value                 |
| -------- | --------------------- |
| Feature  | `mobile-job-progress` |
| Type     | Full-stack            |
| Status   | APPROVED              |
| Created  | 2026-10-07            |
| Updated  | 2026-10-07            |
| Approved | 2026-10-07            |

## Context and objective

`mobile-job-details` delivered Design 8 at `/today/visits/:visitId` up to
travel and arrival, with **Start job** disabled. This feature delivers Design
9, **Job in progress**: the primary technician starts the job after arrival,
records work and pause time, completes and comments checklist tasks, records
actual and additional materials, uploads Before/After photos and keeps
technician notes, with a time summary against the estimate. **Review &
complete** opens a placeholder for Design 10; completion is out of scope.

## Actors and permissions

| Actor                                                                               | Can                                                                                                                                                                       | Cannot                                                                                                  |
| ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Technician (`technician`) with an `active` linked profile, **primary** on the visit | Read the in-progress job; **Start job**, **Pause**, **Resume**; complete/uncomplete and comment tasks; add optional tasks; record materials; add/delete photos; save notes | Complete the visit, report incidents, message the customer, change the primary technician, edit other visits |
| Technician with an `active` linked profile, actively assigned but **not primary**   | Read the same job, photos, materials, tasks and notes                                                                                                                     | Every mutation (`403 not_primary_technician`)                                                           |
| Technician with `inactive`/`suspended` profile, or without linked profile           | —                                                                                                                                                                         | Every endpoint (`403 technician_inactive` / `404 technician_profile_not_linked`)                        |
| Owner, Operations Manager, Dispatcher, Viewer, Accounting, unknown roles            | —                                                                                                                                                                         | Every endpoint (`403`); existing forbidden state                                                        |

## Scope

- Additive extension of `GET /technician/visits/{visitId}` (`TechnicianVisitDetail`).
- `POST …/start-job`, `POST …/pause`, `POST …/resume`.
- Task updates and optional task creation.
- Planned material used quantities, additional materials (active catalog
  product or free text) and the material catalog lookup.
- Before/After photo upload, read and delete.
- Technician notes.
- Design 9 rendering of `/today/visits/:visitId` for `in_progress` and
  `paused` visits, **Save and exit**, and the
  `/today/visits/:visitId/review` placeholder.
- Schema amendments SA-01 and SA-02 (approved; see Data and persistence impact).

## Non-goals

- GPS, live travel, distance, ETA, maps or route data.
- Messaging, SMS, `notifications` rows, customer emails for this feature.
- Inventory, stock reservation or consumption, billing decisions, prices shown
  to the technician.
- Voice recognition or dictation.
- Visit completion, customer review or signature, `completion_without_signature_reason`
  (Design 10).
- Incidents (`visit_incidents`), additional-work requests.
- Deleting or reordering tasks, editing task labels, editing planned materials.
- `during`, `incident` and `other` evidence types; captions.
- External file storage.

## User flow

1. On the Design 8 page with arrival recorded, the primary technician taps
   **Start job**; the page becomes **Job in progress** in place.
2. The technician completes tasks, adds comments or optional tasks, adjusts
   used quantities, adds materials, uploads Before/After photos and writes
   notes; each change is saved when made.
3. **Pause** and **Resume** switch between work and pause time.
4. **Save and exit** saves pending notes and opens `/today`.
5. When required tasks and Before/After photos are present, **Review &
   complete** opens the review placeholder.

## Functional requirements

| ID    | Requirement                                                                                                                            |
| ----- | -------------------------------------------------------------------------------------------------------------------------------------- |
| FR-01 | Every endpoint must resolve organization, membership and profile from the session and authorize the visit per BR-01; mutations require BR-02. |
| FR-02 | **Start job** must transition the visit and, when applicable, the work order per BR-03 and BR-04.                                     |
| FR-03 | **Pause** and **Resume** must switch time entries and status per BR-05.                                                                |
| FR-04 | All mutations must be idempotent where stated, serialized and allowed only in the statuses of BR-06 and BR-07.                        |
| FR-05 | Tasks must be completable, uncompletable, commentable and extendable with optional tasks per BR-08.                                    |
| FR-06 | Planned material used quantities and additional materials must be recorded per BR-09 and BR-10.                                       |
| FR-07 | Before/After photos must be uploaded, served and deleted per BR-11 and BR-12.                                                          |
| FR-08 | Technician notes must be saved per BR-13.                                                                                              |
| FR-09 | The detail must return the progress data of BR-14.                                                                                     |
| FR-10 | The page must render Design 9 with header, status banner, tabs, sections and time summary per BR-15–BR-18.                             |
| FR-11 | **Save and exit** and **Review & complete** must behave per BR-19.                                                                     |
| FR-12 | The page must keep the Design 7 shell, the BR-20 deviations and the accessibility of BR-21.                                            |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization reuses `mobile-job-details` BR-01: policy TechnicianSelf; profile resolution with `technician_profile_not_linked` / `technician_inactive` before any read or write; visibility by active assignment of the caller's profile; `unscheduled`/`cancelled` visits, other technicians' visits, unassigned visits, other organizations and random ids → identical `404` with no foreign data. Task, material, planned-material, evidence and catalog identifiers in paths or bodies are resolved only within the authorized visit (catalog: the session organization); otherwise the identical `404`. No organization or technician id is accepted from the client. | Backend |
| BR-02 | Primary control: every mutation in this spec requires the caller's active assignment with `is_primary = true`; otherwise `403` `not_primary_technician` "The primary technician manages this job.", no write. Reads are allowed to every active assignee. | Backend |
| BR-03 | **Start job** guards, after BR-01/BR-02, in order: status `in_progress` or `paused` → `200 { changed: false, visit }`, no write; status `on_the_way` without a closed `travel` entry (`mobile-job-details` BR-04), or any other status → `409 visit_status_invalid` "This job can't be started in its current state.". No date restriction. | Backend |
| BR-04 | **Start job** effect, one transaction: `visits.status = in_progress`, `actual_started_at = now`, `updated_at = now`; one `visit_status_history` row `on_the_way → in_progress` (actor, reason null); one `visit_time_entries` row (`technician_id` = caller's profile, `entry_type = 'work'`, `started_at = now`, `ended_at` null); when the work order status is `scheduled`, `work_orders.status = in_progress`, `updated_at = now` (any other work order status unchanged); one `visit.job_started` audit row (`entity_type` `visit`, visit id, organization, work order `branch_id`, actor, before/after `{ status }`, metadata `{ workOrderId, visitNumber, workOrderStatusChanged }`). No email. Response `200 { changed: true, visit }`. | Backend |
| BR-05 | **Pause**: status `in_progress` → close the caller's open `work` entry (`ended_at = now`), insert an open `pause` entry, `status = paused`; status `paused` → `200 changed = false`. **Resume**: status `paused` → close the open `pause` entry, add its whole seconds to `visits.pause_seconds`, insert an open `work` entry, `status = in_progress`; status `in_progress` → `200 changed = false`. Each effective transition writes one `visit_status_history` row (reason null), updates `updated_at` and writes one `visit.paused` / `visit.resumed` audit row (metadata `{ workOrderId, visitNumber }`). No reason is requested. Any other status, or an expected open entry that is missing → `409 visit_status_invalid` "This job can't be paused or resumed in its current state.". At most one open `work` or `pause` entry exists per visit. A closed entry's `ended_at` is strictly after its `started_at`. | Backend |
| BR-06 | Editable statuses: task, material, photo and note mutations are allowed only when the visit is `in_progress` or `paused`; otherwise `409 visit_status_invalid` "This job can't be updated in its current state.", no write. | Backend |
| BR-07 | Concurrency: every mutation runs in one transaction that locks the caller's profile row, then the visit row, and re-reads state after locking. Concurrent identical transitions produce exactly one effective change (one history row, one entry change, one audit row); the others return `200 changed = false`. Concurrent changes to the same task, planned material or material apply in lock order; the last committed value wins. | Backend |
| BR-08 | Tasks: `PATCH` accepts `isCompleted` and/or `notes` (at least one). Completing sets `is_completed = true`, `completed_by_user_id` = actor, `completed_at = now`; uncompleting clears all three; sending the current value is a no-op. `notes`: trimmed, 0–1000 characters, empty → null. **Add task**: `label` trimmed 1–240 characters, creates a row with `is_required = false`, `is_completed = false`, `template_item_id` null, `sort_order` = current maximum + 1 (0 when none); at most 50 tasks per visit (`409 task_limit_reached` "This job already has the maximum number of tasks."). Validation errors → `400` ProblemDetails with field errors. | Backend |
| BR-09 | Planned materials: `PUT` sets `usedQuantity` for a `work_order_planned_materials` row of the visit's work order: decimal 0–99999.999, at most 3 decimals. `0` deletes the visit's `visit_materials` row linked to that planned material (if any); a positive value creates or updates exactly one linked row (`planned_material_id`, `description` and `unit` from the planned row, `catalog_item_id` from the planned row, `unit_cost` = that catalog item's `unit_cost` or 0, `billable = false`). Without a row the used quantity is 0. | Backend |
| BR-10 | Additional materials: `POST` with `quantity` (> 0, ≤ 99999.999, ≤ 3 decimals) and either `catalogItemId` of an active `product` catalog item of the organization (`description` = name, `unit` = item unit, `unit_cost` = item `unit_cost`) or free text `description` (trimmed 1–240) and `unit` (trimmed 1–40) with `unit_cost = 0`; never both or neither (`400`). A `catalogItemId` that is not an active `product` of the organization (inactive, `service`, another organization's or unknown) → identical `404`, no write. `planned_material_id` null, `billable = false`. `PUT` on an additional material sets `quantity` with the same limits, `0` deletes it. At most 50 additional materials per visit (`409 material_limit_reached` "This job already has the maximum number of materials."). The catalog lookup returns at most 20 active `product` items of the organization whose name or SKU contains the search text (case-insensitive, 2–80 characters), ordered by name: `{ id, name, unit }`; no prices. Every material change writes one `visit.material_recorded` audit row (metadata `{ workOrderId, visitNumber, materialId, plannedMaterialId \| null, quantityBefore, quantityAfter }`). | Backend |
| BR-11 | Photos: `POST` multipart with one file and `type` `before` \| `after`. Accepted content: JPEG or PNG verified by file signature and declared type, 1 byte–10 MiB, at most 20 photos per visit (`409 evidence_limit_reached` "This job already has the maximum number of photos."). Invalid type or size → `400` with "Upload a JPEG or PNG image up to 10 MB.". Stored in `visit_evidence` with `content`, `mime_type`, `size_bytes`, a sanitized `file_name` (path segments and control characters removed, ≤ 255 characters), `evidence_type`, `uploaded_by_user_id` = actor; `storage_key` null; `caption` null. Writes one `visit.evidence_added` audit row (metadata `{ workOrderId, visitNumber, evidenceId, evidenceType, sizeBytes }`). `DELETE` removes the photo and writes one `visit.evidence_deleted` audit row; deleting a missing photo → identical `404`. | Backend |
| BR-12 | Photo read: `GET …/evidence/{evidenceId}` for any active assignee per BR-01, only for evidence of that visit; otherwise identical `404`. Response: the stored content type, `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`. Content is never included in JSON responses. | Backend |
| BR-13 | Technician notes: `PUT` `{ notes }` trimmed, 0–4000 characters, empty → null, stored in `visits.completion_summary`; `updated_at = now`. Notes are never written to logs or audit metadata. | Backend |
| BR-14 | Detail (additive to `TechnicianVisitDetail` of `mobile-job-details`): `actualStartedAt \| null`; `time = { workSeconds, pauseSeconds, activeEntry: { type: "work" \| "pause", startedAt } \| null, estimatedMinutes \| null }` where `workSeconds` / `pauseSeconds` are the whole seconds of the visit's closed `work` / `pause` entries and `estimatedMinutes` = work order `estimated_duration_minutes`, else the scheduled window length in minutes, else null; `tasks[]` gain `notes \| null`, `completedAt \| null`, ordered by `sort_order`, then label; `plannedMaterials[]` gain `id` and `usedQuantity`; `additionalMaterials: [{ id, description, quantity, unit, catalogItemId \| null }]` in creation order; `evidence: [{ id, type: "before" \| "after", createdAt }]` ordered by `created_at`; `technicianNotes \| null`. Mutation responses return `200 { changed, visit }` for transitions and `200 TechnicianVisitDetail` (`201` for creations) otherwise. | Backend |
| BR-15 | Rendering: `in_progress` and `paused` visits render Design 9 (title "Job in progress"); all other statuses keep Design 8 (`mobile-job-details` BR-12–BR-18). Stepper: Scheduled, On the way, Arrived done, In progress current (both statuses). Status banner: `in_progress` → "In progress • <labor time>" and "Started <h:mm a>" with **Pause** and **Report issue**; `paused` → "Paused • <labor time>" with **Resume** and **Report issue**. **Report issue** opens the `mobile-job-details` BR-18 panel. Header card: title, "#<prefix>-<n>", customer, one-line address, **Open directions** per `technician-todays-jobs` BR-12. On Design 8, **Start job** becomes enabled for the primary technician when arrival is recorded (replacing "Starting the job will be available soon."); a successful start updates the page in place and announces it. Labor time format: "<h>h <m>m", or "<m>m" under one hour. | Frontend |
| BR-16 | Tabs Tasks (default), Materials, Photos, Notes, keyboard-operable with tab semantics; each tab shows its section and the time summary. **Tasks**: "Job completion" card "<completed> of <total> tasks completed" with progress bar and percentage (rounded down); "All required tasks complete" when every required task is complete, otherwise "<n> required tasks remaining" ("1 required task remaining"). "Task checklist" rows: checkbox with the label, "Required" for required tasks, completion time "<h:mm a>" when complete and a comment control that opens an editor with the existing comment and **Save** / **Cancel**; a commented task shows a comment indicator. **Add task** opens a label field with **Add** / **Cancel**. **View original scope** opens a panel "Original scope" with the scope lines of `mobile-job-details` BR-16. Empty: "No tasks for this job." with **Add task**. | Frontend |
| BR-17 | **Materials**: "Materials used" with columns Planned and Used; each planned row shows description, planned quantity and unit, and a stepper (−, value, +) with step 1 that accepts typed decimals per BR-09, never below 0; additional rows show description, unit, "Added" and the same stepper (reaching 0 asks "Remove <description>?" with **Remove** / **Cancel**). Indicator: "Matches planned quantities" when every planned row's used equals planned and no additional material exists; otherwise "Differs from planned quantities". **Add material** opens a panel with a catalog search (results show name and unit) and a "Not in catalog" option with description and unit; both require a quantity. Empty: "No materials recorded." with **Add material**. **Photos**: "Photos & evidence" thumbnails with "Before" / "After" labels, alt text "<Before\|After> photo <k> of <n>", viewer and failure text as `mobile-job-details` BR-17, delete from the viewer with confirmation "Delete this photo?"; **Add photo** asks Before or After, then opens the device picker (camera allowed); upload shows progress; indicator "Before and after photos added" when both exist, otherwise "Add at least one before and one after photo"; empty "No photos yet.". **Notes**: "Technician notes" multiline field with counter "<n>/4000" and the text "Visible to office; include in completion report"; saved when the field loses focus with "Saved" feedback. | Frontend |
| BR-18 | Time summary: "Labor time" = `workSeconds` plus, while `activeEntry.type = work`, the elapsed time since `activeEntry.startedAt`, refreshed at least every minute, with a "Live" label only while `in_progress`; "Break time" = `pauseSeconds` plus the elapsed active pause; "Estimated" from `estimatedMinutes` ("<h>h", "<h>h <m>m" or "<m>m"; section hidden when null); deviation chip from labor minutes (whole minutes of labor time, rounded down) versus `estimatedMinutes`: "<n> min under estimate" when lower, "<n> min over estimate" when higher, "On estimate" when equal (hidden when null). Times shown in the response time zone. | Frontend |
| BR-19 | Action bar (Design 9), primary technician: **Review & complete** enabled only when every required task is complete and at least one Before and one After photo exist, with the text "Next: customer review and signature"; disabled otherwise with "Complete required tasks and add before and after photos"; activating it opens `/today/visits/:visitId/review`, which shows "Review and completion is coming soon." and **Back to job**, and changes no data. **Save and exit** saves pending notes (on failure stays with "We couldn't save your notes. Try again.") and then opens `/today`. Non-primary: no mutation controls, read-only checkboxes, steppers and notes, text "The primary technician manages this job." and **Back to Today's jobs**. Any mutation: pending state blocks repeats; `409` shows its message inline and reloads the visit; `400` shows the field message; other failures show "We couldn't update this job. Try again." and keep local input. | Frontend |
| BR-20 | Design 9 deviations: Design 7 shell kept (bottom navigation, Today current); top bar shows the back button ("Back to Today's jobs") and "Job in progress" with bell and avatar, no ⋮ menu; no "22 min • 8.4 mi", microphone, Message, maps or readiness; mockup data are examples. | Frontend |
| BR-21 | Responsive and accessibility: `technician-todays-jobs` BR-19 and `mobile-job-details` BR-20 apply; the action bar and bottom navigation never cover content at 375 px; status, completion and deviation are never conveyed by color alone; steppers expose name and value ("Used quantity for <description>"); dialogs trap and return focus; results of actions are announced politely; targets ≥ 44 × 44 px. | Frontend |

## States and transitions

| From                       | To                       | Trigger    | Actor              | Guard                                         |
| -------------------------- | ------------------------ | ---------- | ------------------ | --------------------------------------------- |
| `on_the_way` (arrived)     | `in_progress`            | Start job  | Primary technician | BR-01–BR-03; closed travel entry              |
| `in_progress` / `paused`   | unchanged                | Start job  | Primary technician | `200 changed = false`                         |
| `in_progress`              | `paused`                 | Pause      | Primary technician | BR-05; open work entry                        |
| `paused`                   | `in_progress`            | Resume     | Primary technician | BR-05; open pause entry                       |
| `paused` / `in_progress`   | unchanged                | Pause / Resume repeated | Primary technician | `200 changed = false`            |
| Work order `scheduled`     | `in_progress`            | Start job  | Primary technician | BR-04; same transaction                       |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): all tables of
  `mobile-job-details`, plus `visits` (`actual_started_at`, `pause_seconds`,
  `completion_summary`), `visit_time_entries`, `visit_checklist_items`
  (`notes`, `completed_at`, `completed_by_user_id`), `visit_materials`,
  `visit_evidence`, `catalog_items` (`type`, `name`, `sku`, `unit`,
  `unit_cost`, `is_active`), `work_orders` (`status`,
  `estimated_duration_minutes`).
- Written: `visits` (`status`, `actual_started_at`, `pause_seconds`,
  `completion_summary`, `updated_at`), `visit_status_history`,
  `visit_time_entries`, `visit_checklist_items`, `visit_materials`,
  `visit_evidence`, `work_orders` (`status`, `updated_at` only per BR-04),
  `audit_logs`.
- Not written: `notifications`, `visit_incidents`, `customer_signoffs`,
  `work_order_planned_materials`, `visit_assignments`, inventory or billing.
- Schema amendments required: **approved by the user 2026-10-07** (OD-01,
  OD-03). Implementation updates `docs/database/fieldops-schema.sql` and the
  EF model, and generates one migration only under `--generate-migration`.
  Agents never apply it. Review by `database-reviewer`.
  - SA-01 `visit_evidence`: add `content bytea`; `storage_key` becomes
    nullable; add `CHECK (content IS NOT NULL OR storage_key IS NOT NULL)`,
    `CHECK (mime_type IN ('image/jpeg','image/png'))` and
    `CHECK (size_bytes <= 10485760)`. Existing rows, if any, keep their
    `storage_key`.
  - SA-02 `visit_materials`: add `planned_material_id uuid NULL REFERENCES
    work_order_planned_materials(id) ON DELETE SET NULL` and a unique index on
    `(visit_id, planned_material_id) WHERE planned_material_id IS NOT NULL`.

## Tenant isolation and authorization

- Organization, membership and profile resolved from the session (BR-01).
- Client-provided identifiers: `visitId`, `taskId`, `plannedMaterialId`,
  `materialId`, `evidenceId` (path) and `catalogItemId` (body), each
  authorized against the session organization, an active assignment of the
  caller's profile and the visit; otherwise identical `404`.
- Mutations require the primary assignment (BR-02); frontend hiding is UX only.
- Uploads are authorized before the body is read beyond the size limit and are
  validated by signature, declared type and size (BR-11).
- Responses use `Cache-Control: no-store`. Logs and audit never contain
  addresses, phones, emails, customer names, notes, task comments, file names
  or file content.

## API contracts

Contract status: Final. Errors use ProblemDetails with `code` where listed.
The first row is `/technician/visits/{visitId}`; every other path is relative
to it. Permission TechnicianSelf.
Common errors for every row: `403` (role; `technician_inactive`) · `404`
(BR-01; `technician_profile_not_linked`). Mutations additionally: `403
not_primary_technician`.

`TechnicianVisitDetail` = `mobile-job-details` detail & BR-14 fields.
`VisitActionResult` = `{ changed, visit: TechnicianVisitDetail }`.

| Method | Path                                  | Request                                                                 | Success                       | Additional errors                                                     |
| ------ | ------------------------------------- | ----------------------------------------------------------------------- | ----------------------------- | --------------------------------------------------------------------- |
| GET    | `/technician/visits/{visitId}`        | —                                                                       | `200 TechnicianVisitDetail`   | —                                                                     |
| POST   | `/start-job`                          | —                                                                       | `200 VisitActionResult`       | `409 visit_status_invalid`                                            |
| POST   | `/pause`                              | —                                                                       | `200 VisitActionResult`       | `409 visit_status_invalid`                                            |
| POST   | `/resume`                             | —                                                                       | `200 VisitActionResult`       | `409 visit_status_invalid`                                            |
| PATCH  | `/tasks/{taskId}`                     | `{ isCompleted?, notes? }`                                              | `200 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`                                    |
| POST   | `/tasks`                              | `{ label }`                                                             | `201 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`, `task_limit_reached`              |
| PUT    | `/planned-materials/{plannedMaterialId}` | `{ usedQuantity }`                                                   | `200 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`                                    |
| POST   | `/materials`                          | `{ quantity, catalogItemId }` or `{ quantity, description, unit }`      | `201 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`, `material_limit_reached`          |
| PUT    | `/materials/{materialId}`             | `{ quantity }` (additional materials only; `0` deletes)                 | `200 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`                                    |
| GET    | `/material-catalog?search=`           | —                                                                       | `200 [{ id, name, unit }]`    | `400`                                                                 |
| POST   | `/evidence`                           | `multipart/form-data`: `file`, `type`                                   | `201 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`, `evidence_limit_reached`          |
| GET    | `/evidence/{evidenceId}`              | —                                                                       | `200` image bytes (BR-12)     | —                                                                     |
| DELETE | `/evidence/{evidenceId}`              | —                                                                       | `200 TechnicianVisitDetail`   | `409 visit_status_invalid`                                            |
| PUT    | `/notes`                              | `{ notes }`                                                             | `200 TechnicianVisitDetail`   | `400` · `409 visit_status_invalid`                                    |

A linked planned material belongs only to `PUT /planned-materials/…`;
`PUT /materials/{materialId}` on a linked row → identical `404`. Mutations rely
on the existing cookie session (`HttpOnly`, `Secure`, `SameSite=Strict`); no
new CSRF mechanism is introduced.

## UI behavior and states

| Screen                          | Loading                                                                 | Empty                                     | Error                                                                                                         | Permission                                              | Success      |
| ------------------------------- | ----------------------------------------------------------------------- | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- | ------------ |
| `/today/visits/:visitId` (Design 9) | As `mobile-job-details`; per-action pending; upload progress        | Per-tab empty texts (BR-16, BR-17)        | As `mobile-job-details` for load; action errors per BR-19; photo "Photo unavailable."                        | Non-primary read-only (BR-19); other roles as today     | BR-15–BR-19  |
| `/today/visits/:visitId/review` | —                                                                       | —                                         | —                                                                                                             | Same route guard as the job page                        | BR-19        |

- Mockups: `design/assets/9-design.png` (Approved; primary reference) with the
  BR-20 deviations; Design 8 states from `design/assets/8-design.png`; shell
  from `design/assets/7-design.png`.
- Responsive and accessibility: BR-21.

## Error behavior

| Case                                                                 | Response / message shown                                                       | Data change |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------------ | ----------- |
| Visit, task, material, planned material, evidence or catalog item not visible to the caller | `404`; "This job isn't available." / "Photo unavailable." | None        |
| Non-primary mutation                                                 | `403 not_primary_technician`; "The primary technician manages this job."        | None        |
| Start job before arrival or from another status                       | `409 visit_status_invalid`; "This job can't be started in its current state." | None        |
| Pause/resume from an invalid status or missing open entry             | `409 visit_status_invalid`; "This job can't be paused or resumed in its current state." | None |
| Edit outside `in_progress`/`paused`                                   | `409 visit_status_invalid`; "This job can't be updated in its current state."  | None        |
| Task, material or photo limit                                         | `409 task_limit_reached` / `material_limit_reached` / `evidence_limit_reached` with BR-08/BR-10/BR-11 messages | None |
| Invalid field                                                         | `400` with field message; photo: "Upload a JPEG or PNG image up to 10 MB."     | None        |
| Repeated Start job, Pause or Resume                                   | `200 changed = false`                                                          | None        |
| Notes save failure on Save and exit                                   | "We couldn't save your notes. Try again."; stays on the page                    | None        |
| Other mutation failure                                                | "We couldn't update this job. Try again."                                      | None        |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Another technician's visit, an unassigned visit, a visit of another organization and a random id; a task, material, planned material and photo of another visit; a catalog item of another organization | Every endpoint of this spec is called with them | Identical `404` with no foreign data and no write |
| AC-02 | Other roles (parameterized), an unlinked and an inactive/suspended technician, and an actively assigned non-primary technician | They call the mutations | Roles `403`; unlinked `404 technician_profile_not_linked`; inactive `403 technician_inactive`; non-primary `403 not_primary_technician`; no write; the non-primary can read the detail and photos |
| AC-03 | A primary technician on an `on_the_way` visit with closed travel, whose work order is `scheduled` | Start job is called twice | First `200 changed = true`: status `in_progress`, `actual_started_at`, one history row, one open `work` entry, work order `in_progress`, one `visit.job_started` audit row without sensitive data; second `200 changed = false` with no write |
| AC-04 | Visits `assigned`, `on_the_way` with open travel, `completed`; a work order already `in_progress` | Start job is called | `409 visit_status_invalid` for the three visits with no write; for the already `in_progress` work order the start succeeds and the work order is unchanged with `workOrderStatusChanged = false` |
| AC-05 | An `in_progress` visit | Pause, Pause, Resume, Resume are called | Pause closes the work entry, opens a pause entry, status `paused`, one history and audit row; repeated Pause `changed = false`; Resume closes the pause entry, adds its seconds to `pause_seconds`, opens a work entry, status `in_progress`; repeated Resume `changed = false`; never more than one open entry |
| AC-06 | Visits `on_the_way`, `completed` and a `paused` visit without an open pause entry | Pause, Resume and every edit endpoint are called (table-driven) | `409 visit_status_invalid` with the BR-05/BR-06 message and no write; edits succeed on `paused` visits |
| AC-07 | Concurrent identical Start job, Pause and Resume requests on one visit | They run concurrently | Exactly one `changed = true`, one history row, one entry change and one audit row per transition; the others `changed = false` |
| AC-08 | A visit with required and optional tasks | Tasks are completed, uncompleted, commented with 1000 and 1001 characters, and an optional task is added with valid, empty and 241-character labels and at the 50-task limit | Completion sets/clears completer and time; comments saved trimmed or rejected with `400`; the new task is optional and last; invalid labels `400`; limit `409 task_limit_reached` |
| AC-09 | Planned materials and a visit without recorded materials | Used quantities 0, 1.5, 2 and 1.2345 are set, then 0 again | Positive values create/update exactly one linked row with planned description/unit and `billable = false`; 1.2345 → `400`; 0 deletes the row; one `visit.material_recorded` audit row per change |
| AC-10 | An active product, an inactive product, a service item, another organization's item and free text | Additional materials are added and updated (table-driven), including both/neither source, 0 quantity and the 50 limit | Active product and free text create rows with the BR-10 values; inactive, service and foreign items return the identical `404` with no write; both/neither `400`; `PUT` 0 deletes; limit `409 material_limit_reached` |
| AC-11 | A catalog with matching active and inactive products and services | The catalog lookup is called with 1, 2 and 81 characters and a matching text | 1 and 81 characters `400`; the match returns ≤ 20 active products `{ id, name, unit }` ordered by name without prices |
| AC-12 | JPEG and PNG files, a PNG renamed as JPEG, a GIF, a text file named `.jpg`, a 10 MiB + 1 file, and the 20-photo limit | They are uploaded as Before or After | Valid images `201` stored with content and sanitized file name and one audit row; mismatched or invalid content and oversize `400` with the BR-11 message; limit `409 evidence_limit_reached`; no write on errors |
| AC-13 | Stored photos of the visit, of another visit and of another organization | They are read and deleted | Read returns the stored type with `inline`, `nosniff`, `no-store` only for the visit's photo; others `404`; delete removes the photo with one audit row; deleting again `404` |
| AC-14 | Notes of 4000 and 4001 characters and whitespace only | Notes are saved | 4000 saved trimmed in `completion_summary`; 4001 `400`; whitespace → null; no note text in logs or audit |
| AC-15 | Visits with closed and open work/pause entries, with and without estimate and scheduled window | The detail is loaded | `time`, `activeEntry`, `estimatedMinutes` (estimate, then window, then null), tasks with notes and completion time, planned materials with `usedQuantity`, additional materials, evidence and notes follow BR-14 |
| AC-16 | The Design 8 page with arrival recorded for the primary and the non-primary technician | **Start job** is used | Primary: Start job enabled, success turns the page into Design 9 in place with an announcement, `409` shows its message and reloads; non-primary: Start job not available |
| AC-17 | Design 9 for `in_progress` and `paused` | The page renders | Stepper, banner texts and Pause/Resume controls follow BR-15; Report issue opens the existing panel; labor and break times, Live label, estimate and deviation follow BR-18 |
| AC-18 | Tasks with required items incomplete and complete, with and without comments, and no tasks | The Tasks tab is used | Completion card, remaining/complete texts, completion times, comment editor and indicator, Add task and View original scope follow BR-16; empty state shown |
| AC-19 | Planned rows matching and differing, additional materials and no materials | The Materials tab is used | Steppers never go below 0; reaching 0 on an additional material asks to remove it; indicator, Add material panel with catalog search and Not in catalog, and empty state follow BR-17 |
| AC-20 | Photos with and without Before/After, notes and a failing save | The Photos and Notes tabs are used | Add photo asks Before/After, shows progress and the indicator; viewer delete asks for confirmation; notes save on blur with counter and "Saved"; failures follow BR-19 |
| AC-21 | Required tasks incomplete or photos missing, then all present; notes unsaved; a non-primary technician | **Review & complete**, **Save and exit** and the back button are used | Review & complete disabled with the BR-19 text until ready, then opens the review placeholder without writes; Save and exit saves notes then opens `/today`, or stays on failure; non-primary sees read-only controls and the BR-19 text |
| AC-22 | Viewports 375 px and 1280 px | The page is used by keyboard and touch | Design 9 inside the Design 7 shell with BR-20 deviations; no page-level horizontal scroll; nothing hidden behind the action bar or bottom navigation; targets ≥ 44 px; visible focus; dialogs trap and return focus; status and deviation not conveyed by color alone |

## Testing requirements

| Level                          | Behavior or risk to prove                                                                                                                         | Evidence for              |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------- |
| Authorization/tenant isolation | One parameterized integration test over all endpoints: foreign/unassigned/other-organization visits and nested ids `404`; non-primary `403`; inactive `403` (role `403` reuses existing TechnicianSelf evidence) | AC-01, AC-02 |
| Backend integration            | Start job effect, work order rule, idempotency and guards                                                                                          | AC-03, AC-04              |
| Backend integration            | Pause/resume entries, `pause_seconds`, idempotency and status guards (table-driven)                                                                | AC-05, AC-06              |
| Backend integration            | Concurrency of transitions                                                                                                                         | AC-07                     |
| Backend integration            | Tasks and materials (planned, additional, catalog lookup) table-driven with audit                                                                  | AC-08–AC-11               |
| Backend integration            | Photo upload validation, storage, read headers and delete; notes; detail mapping                                                                   | AC-12–AC-15               |
| Persistence review             | SA-01 and SA-02 mapping and migration (`database-reviewer`); migration generated only with the flag and never applied                              | SA-01, SA-02              |
| Frontend component/service     | Start job from Design 8; Design 9 banner/stepper/time summary; tabs with tasks, materials, photos, notes; action bar gating, Save and exit and error handling (3–6 methods) | AC-16–AC-21 |
| User visual QA (manual)        | Design 9 fidelity, shell, responsive layout, focus, announcements                                                                                  | AC-22                     |

Backend integration stays within 3–8 methods; backend unit 0–4 only for
non-trivial helpers (time totals, quantity validation, image signature
checks). Agents run no browser automation or Playwright.

## Dependencies

- `mobile-job-details` (AUDITED): Design 8 page, travel and arrival
  (BR-04, BR-09), detail contract, panels, photo viewer, action bar. BR-15
  supersedes its BR-14 for **Start job** after arrival (enabled instead of
  disabled with "Starting the job will be available soon.").
- `technician-todays-jobs` (AUDITED): technician shell, `/today`, BR-12,
  BR-19.
- `create-work-order` (AUDITED): planned materials, checklist items,
  `estimated_duration_minutes`.
- `products-services` (APPROVED): catalog items for the additional material
  lookup.

## Assumptions

| ID    | Assumption (minor, non-behavioral)                                                                                                   |
| ----- | ------------------------------------------------------------------------------------------------------------------------------------ |
| AS-01 | Time entries belong to the primary technician; other assignees record no time in this feature.                                      |
| AS-02 | `visits.completion_summary` is the technician notes field that Design 10 will show in the completion report.                         |
| AS-03 | Photos are stored inline as in `assessment_attachments`; no image resizing or thumbnail generation is performed.                     |
| AS-04 | Material `unit_cost` is captured for later costing only; it is never shown to the technician.                                        |

## Open decisions

| ID    | Question                       | Options                                                                  | Blocking | Resolution                                                                                                         |
| ----- | ------------------------------ | ------------------------------------------------------------------------ | -------- | ------------------------------------------------------------------------------------------------------------------ |
| OD-01 | Photo storage                  | Schema amendment with inline content · external storage · no photos       | No       | Amendment SA-01 with inline content (assessment precedent) — user 2026-10-07                                       |
| OD-02 | Technician notes               | `visits.completion_summary` · new column                                 | No       | `completion_summary` (BR-13) — user 2026-10-07                                                                     |
| OD-03 | Materials used                 | Amendment `planned_material_id` · match by catalog/description           | No       | Amendment SA-02; additional materials from active catalog products or free text with unit; `billable = false` (BR-09, BR-10) — user 2026-10-07 |
| OD-04 | Who can modify                 | Primary only · primary transitions and any assignee edits                 | No       | Primary only; other assignees read-only (BR-02) — user 2026-10-07                                                  |
| OD-05 | Start job and work order       | Move work order from `scheduled` to `in_progress` · leave unchanged        | No       | Move from `scheduled` in the same transaction; no email (BR-03, BR-04) — user 2026-10-07                           |
| OD-06 | Pause and resume               | Status `paused` with pause entries · entries only                         | No       | `paused` status, entries, history, no reason; edits allowed while paused (BR-05, BR-06) — user 2026-10-07          |
| OD-07 | Tasks                          | Complete/uncomplete, comment, add optional · complete only                | No       | Complete/uncomplete, comment, add optional tasks, View original scope (BR-08, BR-16) — user 2026-10-07             |
| OD-08 | Review & complete              | Gated route placeholder · disabled                                        | No       | Gated by required tasks and Before/After photos; placeholder route without writes (BR-19) — user 2026-10-07        |
| OD-09 | Time summary, saving, other UI | As proposed · alternatives                                                | No       | Live client timer, estimate fallback to window, save per action, notes on blur/Save and exit, photo delete, existing Report issue panel, Design 9 omissions (BR-15–BR-20) — user 2026-10-07 |

## Traceability

| FR    | AC                          |
| ----- | --------------------------- |
| FR-01 | AC-01, AC-02                |
| FR-02 | AC-03, AC-04, AC-16         |
| FR-03 | AC-05, AC-06                |
| FR-04 | AC-03, AC-05, AC-06, AC-07  |
| FR-05 | AC-08, AC-18                |
| FR-06 | AC-09, AC-10, AC-11, AC-19  |
| FR-07 | AC-12, AC-13, AC-20         |
| FR-08 | AC-14, AC-20                |
| FR-09 | AC-15                       |
| FR-10 | AC-17, AC-18, AC-19, AC-20  |
| FR-11 | AC-21                       |
| FR-12 | AC-22                       |

## Change log

| Date       | Status change | Reason  |
| ---------- | ------------- | ------- |
| 2026-10-07 | — → DRAFT     | Created |
| 2026-10-07 | DRAFT → DRAFT | Revised after validation: identical `404` for inactive, service or foreign catalog items (BR-10, AC-10); detail `GET` path; SA-02 `ON DELETE SET NULL`; supersession of `mobile-job-details` BR-14 noted; labor minutes rounded down for the deviation (BR-18) |
| 2026-10-07 | DRAFT → APPROVED | Approved by user via /spec approve |
