# Mobile job completion

| Field    | Value                   |
| -------- | ----------------------- |
| Feature  | `mobile-job-completion` |
| Type     | Full-stack              |
| Status   | DRAFT                   |
| Created  | 2026-10-07              |
| Updated  | 2026-10-07              |
| Approved | —                       |

## Context and objective

`mobile-job-progress` delivered Design 9 and left
`/today/visits/:visitId/review` as a placeholder. This feature delivers Design
10 and closes the mobile flow of Designs 7–9. In **Review & complete**, the
primary technician checks a summary of the job, records how the customer
acknowledged the service (signature, customer not available, declined to sign
or remote confirmation) and taps **Complete job**. One transaction then closes
the open time entries, completes the visit and stores the acknowledgment,
status history and audit. When this is the work order's last open occurrence,
the same transaction also completes the work order. A signature acknowledges
the service and never confirms payment.

## Actors and permissions

| Actor                                                                               | Can                                                                                 | Cannot                                                                                     |
| ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Technician (`technician`) with an `active` linked profile, **primary** on the visit | Read the review summary; capture the acknowledgment; **Complete job**                | Complete visits of other technicians, take payments, edit an acknowledgment after completion |
| Technician with an `active` linked profile, actively assigned but **not primary**   | Read the review summary                                                              | Capture the acknowledgment or complete (`403 not_primary_technician`)                     |
| Technician with `inactive`/`suspended` profile, or without linked profile           | —                                                                                   | Every endpoint (`403 technician_inactive` / `404 technician_profile_not_linked`)          |
| Owner, Operations Manager, Dispatcher, Viewer, Accounting, unknown roles            | —                                                                                   | Every endpoint (`403`); existing forbidden state                                           |

## Scope

- Additive extension of `GET /technician/visits/{visitId}`
  (`TechnicianVisitDetail`) with completion readiness and the primary
  technician's name.
- `POST /technician/visits/{visitId}/complete` with the acknowledgment.
- Visit completion, closing of open time entries, `customer_signoffs` record,
  status history and audit in one transaction.
- Work order aggregate status on completion, including recurring orders.
- Design 10 at `/today/visits/:visitId/review` (Review step and Customer
  acknowledgment step), replacing the `mobile-job-progress` placeholder.
- Schema amendment SA-03 (approved; see Data and persistence impact).

## Non-goals

- Invoices, payments, prices, totals, tips or collection of any kind.
- Ratings, surveys or customer feedback scores.
- GPS, location capture, maps or route data.
- Messaging, emails, SMS, `notifications` rows or links sent to the customer,
  including for remote confirmation.
- Office review of completed visits (`needs_correction`, `approved`),
  reopening a completed visit, or editing or viewing the acknowledgment after
  completion.
- Selecting an existing customer contact as signer (`signer_contact_id`).
- Incidents (`visit_incidents`) and additional-work requests.
- Browser automation or Playwright by agents.

## User flow

1. On Design 9, with required tasks complete and Before/After photos present,
   the primary technician taps **Review & complete**.
2. The system shows the **Review & complete** step: job, customer, property,
   checklist, materials, evidence, completion note, work time, technician and
   the readiness card.
3. The technician taps **Continue to acknowledgment**.
4. The system shows **Customer acknowledgment** with four methods and the
   fields that apply to the selected method.
5. The technician fills the fields (the customer signs on the device when the
   method is **Signature obtained**) and taps **Complete job**.
6. The system completes the job in one transaction and opens `/today`
   announcing "Job completed.".

## Functional requirements

| ID    | Requirement                                                                                                                       |
| ----- | --------------------------------------------------------------------------------------------------------------------------------- |
| FR-01 | Every endpoint must resolve organization, membership and profile from the session and authorize the visit per BR-01; completion requires BR-02. |
| FR-02 | Completion must be allowed only from the statuses of BR-03 and only when the requirements of BR-04 are met.                       |
| FR-03 | Completion must capture an acknowledgment whose method and fields follow BR-05–BR-07.                                              |
| FR-04 | Completion must close open time entries, complete the visit and record the acknowledgment, history and audit in one transaction per BR-08. |
| FR-05 | Completion must set the work order aggregate status per BR-09, for one-time and recurring orders.                                 |
| FR-06 | Completion must be idempotent and serialized per BR-10.                                                                            |
| FR-07 | The detail must return the completion readiness and summary data of BR-11.                                                        |
| FR-08 | The Review step must render the Design 10 summary and readiness gating of BR-12 and BR-13.                                        |
| FR-09 | The Customer acknowledgment step must render the methods, fields, signature pad and texts of BR-14 and BR-15.                     |
| FR-10 | **Complete job** submission, success and failure handling must follow BR-16.                                                       |
| FR-11 | Non-primary technicians and visits outside `in_progress`/`paused` must follow BR-17.                                               |
| FR-12 | The page must keep the Design 7 shell with the BR-18 deviations and the accessibility of BR-19.                                    |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization reuses `mobile-job-progress` BR-01: policy TechnicianSelf; profile resolution with `technician_profile_not_linked` / `technician_inactive` before any read or write; visibility by active assignment of the caller's profile; `unscheduled`/`cancelled` visits, other technicians' visits, unassigned visits, other organizations and random ids → identical `404` with no foreign data and no write. No organization, technician, signer contact or work order id is accepted from the client. | Backend |
| BR-02 | Primary control: `POST …/complete` requires the caller's active assignment with `is_primary = true`; otherwise `403` `not_primary_technician` "The primary technician manages this job.", no write. Reads are allowed to every active assignee. | Backend |
| BR-03 | Status guard, after BR-01/BR-02 and body validation (BR-06, BR-07), evaluated after locking (BR-10): `completed` → `200 { changed: false, visit }` with no write, for any valid body (an invalid body returns the BR-06/BR-07 `400` first); `in_progress` or `paused` → continue to BR-04; any other status → `409 visit_status_invalid` "This job can't be completed in its current state.", no write. | Backend |
| BR-04 | Requirements, evaluated in the same transaction: every `visit_checklist_items` row of the visit with `is_required = true` has `is_completed = true`, and the visit has at least one `visit_evidence` row with `evidence_type = 'before'` and at least one with `'after'`. Optional tasks and materials never block completion. Otherwise `409 completion_requirements_unmet` "Complete required tasks and add before and after photos before completing this job.", no write. | Backend |
| BR-05 | Acknowledgment methods (API code · Design 10 label · description): `signed` · "Signature obtained" · "Customer signs on this device."; `customer_absent` · "Customer not available" · "Customer was not present on site."; `customer_refused` · "Customer declined to sign" · "Customer reviewed the work but declined to sign."; `remote_confirmation` · "Remote confirmation" · "Acknowledged via email or phone.". Remote confirmation records that the customer confirmed through another channel; the system sends nothing. Exactly one method is required (`400`). | Both |
| BR-06 | Fields per method follow the Acknowledgment field rules table below. A field marked "—" must not be sent for that method (`400`). Text fields are trimmed; an optional empty text → null. Relationship codes and labels: `customer` "Customer", `family_member` "Family member", `tenant` "Tenant", `property_manager` "Property manager", `employee` "Employee", `other` "Other". Validation errors → `400` ProblemDetails with field errors. | Both |
| BR-07 | Signature: one `multipart/form-data` part `signature`, PNG verified by file signature and declared type, 1 byte–512 KiB. Otherwise `400` "Capture the signature again.". The whole request is limited to 1 MiB and is authorized (BR-01, BR-02) before the body is read beyond that limit. The signature is never returned by any endpoint, written to logs or included in audit data. | Backend |
| BR-08 | Completion effect, one transaction: (a) every open `visit_time_entries` row of the visit of type `work` or `pause` gets `ended_at = now` under the `mobile-job-progress` BR-05 rule that `ended_at` is strictly after `started_at`, and a closed `pause` entry adds its whole seconds to `visits.pause_seconds`; (b) `visits.status = completed`, `actual_completed_at = now`, `updated_at = now`, `completion_without_signature_reason` per the mapping below; (c) one `visit_status_history` row `<in_progress \| paused> → completed` (actor, reason null); (d) one `customer_signoffs` row: `acknowledgement_method`, `signer_name`, `signer_relationship`, `signature_content` and `signature_mime_type = 'image/png'` only for `signed`, `review_confirmed`, `comments`, `absence_reason`, `accepted`, `recorded_by_user_id` = actor, `signed_at = now`, `signer_contact_id` null; (e) BR-09; (f) one `visit.completed` audit row (`entity_type` `visit`, visit id, organization, work order `branch_id`, actor, before/after `{ status }`, metadata `{ workOrderId, visitNumber, acknowledgementMethod, hasSignature, workSeconds, pauseSeconds, workOrderStatusChanged }`). Mapping: `accepted` = true for `signed` and `remote_confirmation`, false otherwise; `comments` = the comment for `signed` and `remote_confirmation`; `absence_reason` = the reason for `customer_absent` and `customer_refused`; `completion_without_signature_reason` = the reason or remote comment for every method except `signed` (null for `signed`). No invoice, payment, notification or email is created. Response `200 { changed: true, visit }`. | Backend |
| BR-09 | Work order aggregate, inside the BR-08 transaction: when the work order status is `scheduled` or `in_progress` and, after this completion, (a) the order is `one_time`, or a visit with `visit_number = recurrence_count` exists for the `recurring` order, and (b) every visit of the order that is not `cancelled` is `completed` or `approved`, then `work_orders.status = completed`, `updated_at = now`, with one `work_order.status_changed` audit row (before/after `{ status }`, metadata `{ visitId }`, as `dispatch-calendar` BR-16). Otherwise the work order status is unchanged; a recurring order with pending, unscheduled or not yet materialized occurrences stays `in_progress`. Any other work order status (`completed`, `approved_for_billing`, `cancelled`) is never changed. | Backend |
| BR-10 | Concurrency and idempotency: the completion runs in one transaction that locks the caller's profile row, then the visit row, then the work order row, and re-reads state after locking (`mobile-job-progress` BR-07 order). Concurrent completions produce exactly one effective completion (one history row, one signoff row, one `visit.completed` audit row, at most one `work_order.status_changed` row); the others return `200 changed = false`. Concurrent completions of two visits of the same order serialize on the work order row so that the last one evaluates BR-09 with both completed. `UNIQUE(customer_signoffs.visit_id)` is a backstop; a violation is treated as `changed = false`. Mutations of `mobile-job-progress` that commit after the completion receive its `409 visit_status_invalid`. | Backend |
| BR-11 | Detail (additive to `TechnicianVisitDetail`): `primaryTechnicianName` (first and last name of the active primary assignee); `completion = { requiredTasksComplete, hasBeforePhoto, hasAfterPhoto, ready, completesWorkOrder }`, where `ready` = the BR-04 condition and `completesWorkOrder` = whether BR-09 would complete the work order if this visit were completed now. No acknowledgment data and no signature are returned. | Backend |
| BR-12 | Review step (Design 10 left screen): title "Review & complete", subtitle "Review completed work before capturing customer acknowledgment."; card with "Service" (work order title), "Customer" (name) and "Property" (one-line address); rows "Completion checklist" "<c> of <t> tasks complete." with the badge "<c>/<t>" (check icon when every required task is complete); "Materials used" up to 3 lines "<description> × <quantity>" (planned rows with used quantity > 0, then additional materials; quantity without trailing zeros), "+<n> more" beyond 3, "No materials recorded." when none; "Evidence" "Before photos: <n>" and "After photos: <n>"; "Technician completion note" with the technician notes clamped to two lines, or "No completion note."; "Summary" with "Work time" (labor time of `mobile-job-progress` BR-18, live while `in_progress`) and "Technician" (`primaryTechnicianName`). Checklist, materials, evidence and note rows link to `/today/visits/:visitId` at the Tasks, Materials, Photos or Notes section. **Review & complete** on Design 9 (`mobile-job-progress` BR-19) opens this step instead of the placeholder. | Frontend |
| BR-13 | Readiness card: when `completion.ready`, "Ready to complete" with the items "Checklist complete", "Required evidence attached" and "Materials recorded"; otherwise "Not ready to complete" with "Complete required tasks and add before and after photos" and each item shown as met or not met ("Required tasks incomplete", "Before and after photos missing"). "Materials recorded" is informational: shown as met when at least one material exists, otherwise "No materials recorded" as neutral, never blocking. **Continue to acknowledgment** is enabled only when `completion.ready`. Met and not-met states use icon and text, never color alone. | Frontend |
| BR-14 | Customer acknowledgment step (Design 10 right screen): title "Customer acknowledgment", subtitle "Choose how completion was acknowledged."; a single-choice group with the four BR-05 methods, icons, labels and descriptions, **Signature obtained** selected by default; then the fields of the selected method per the table below, with the labels "Signer name", "Relationship" (select, default "Customer"), "Customer signature", the comment label of the method and the checkbox "Customer confirms the work was reviewed."; "Signer name" is prefilled with the customer name and is editable. The info text "A signature confirms service acknowledgment. It does not confirm payment." is always shown. Actions **Back** (returns to the Review step keeping the entered values) and **Complete job**. Text under the actions: "Completing stops the timer and changes the work order to Completed." when `completion.completesWorkOrder`, otherwise "Completing stops the timer. The work order stays in progress until its remaining visits are completed.". Changing the method keeps values entered for fields that the new method also uses and drops the others. | Frontend |
| BR-15 | Signature pad: drawing area with touch, pen and mouse input, accessible name "Customer signature" and a text status "Signature captured" / "No signature"; **Clear** removes the whole signature; **Redo** removes the last stroke; both are disabled when the pad is empty. The signature is exported as PNG at submission. Rotating or resizing the screen keeps the signature or, if it cannot be kept, clears it and announces "Signature cleared. Ask the customer to sign again.". | Frontend |
| BR-16 | Submission: **Complete job** validates the BR-06 fields on the client with the messages of the field table and moves focus to the first invalid field; while pending, both actions are disabled and **Complete job** shows progress; success (`changed` true or false) opens `/today` and announces "Job completed."; `409 visit_status_invalid` or `completion_requirements_unmet` shows its message, reloads the visit and returns to the Review step; `400` shows the field messages (signature: "Capture the signature again."); other failures show "We couldn't complete this job. Try again." and keep every entered value, including the signature. Entered values are not saved before **Complete job**. | Frontend |
| BR-17 | Route states: the route uses the job page guard; loading, `404` and load errors as `mobile-job-details`. A visit whose status is not `in_progress` or `paused` redirects to `/today/visits/:visitId`. A non-primary technician sees the Review step read-only with the text "The primary technician manages this job." and **Back to job** instead of **Continue to acknowledgment**; the acknowledgment step is never shown to them. A `403 not_primary_technician` on submission shows that text and switches to the read-only Review step. | Frontend |
| BR-18 | Design 10 deviations: Design 7 shell kept (bottom navigation, Today current); top bar as Design 10 with the back button ("Back to job" on the Review step, "Back to review" on the acknowledgment step), "#<prefix>-<n>", the status chip ("In progress" / "Paused") and the labor time; "Customer signature" is required and shown only for **Signature obtained**, so the mockup's "(optional)" suffix is not shown (OD-03); comment labels follow the field table; mockup data are examples. | Frontend |
| BR-19 | Responsive and accessibility: `technician-todays-jobs` BR-19 and `mobile-job-progress` BR-21 apply; the action bar and bottom navigation never cover content at 375 px; the method group is keyboard-operable with radio semantics; field errors are linked to their fields; the signature pad has a visible focus indicator and the text status of BR-15; moving between steps moves focus to the step title; results are announced politely; targets ≥ 44 × 44 px. | Frontend |

### Acknowledgment field rules

| Field             | `signed`                  | `customer_absent` | `customer_refused` | `remote_confirmation`     | Format                                          | Message when invalid                              |
| ----------------- | ------------------------- | ----------------- | ------------------ | ------------------------- | ----------------------------------------------- | ------------------------------------------------- |
| `signerName`      | Required                  | —                 | Optional           | Required                  | Trimmed, 1–180 characters                       | Missing or empty: "Enter the signer's name."; over 180: "Use 180 characters or fewer." |
| `relationship`    | Required                  | —                 | —                  | Required                  | One of the BR-06 codes                          | "Select the relationship."                        |
| `signature`       | Required (BR-07)          | —                 | —                  | —                         | PNG, 1 byte–512 KiB                             | "Add the customer's signature."                   |
| `comment`         | Optional                  | Required          | Required           | Required                  | Trimmed, 1–1000 characters (optional: 0–1000)   | Missing or empty: "Enter the reason." (absent, refused) / "Enter how the customer confirmed." (remote); over 1000: "Use 1000 characters or fewer." |
| `reviewConfirmed` | Required, `true`          | —                 | —                  | Required, `true`          | Boolean                                         | "Confirm that the customer reviewed the work."    |
| Comment label     | "Customer comment (optional)" | "Reason"      | "Reason"           | "Confirmation details"    | —                                               | —                                                 |

## States and transitions

| From                         | To                        | Trigger      | Actor              | Guard                                         |
| ---------------------------- | ------------------------- | ------------ | ------------------ | --------------------------------------------- |
| `in_progress` / `paused`     | `completed`               | Complete job | Primary technician | BR-01–BR-07                                   |
| `completed`                  | unchanged                 | Complete job | Primary technician | `200 changed = false`                         |
| Any other visit status       | unchanged                 | Complete job | Primary technician | `409 visit_status_invalid`                    |
| Work order `scheduled` / `in_progress` | `completed`     | Complete job | Primary technician | BR-09 (last occurrence, no open visit)        |
| Work order `in_progress`     | unchanged                 | Complete job | Primary technician | BR-09 not met (recurring with pending visits) |
| Open `work` / `pause` entry  | closed                    | Complete job | Primary technician | BR-08                                         |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): all tables of
  `mobile-job-progress`, plus `work_orders` (`job_type`, `recurrence_count`,
  `status`), `visits` of the same work order (`visit_number`, `status`),
  `visit_assignments` and `technician_profiles` / `users` for the primary
  technician's name.
- Written: `visits` (`status`, `actual_completed_at`, `pause_seconds`,
  `completion_without_signature_reason`, `updated_at`),
  `visit_status_history`, `visit_time_entries` (`ended_at`),
  `customer_signoffs`, `work_orders` (`status`, `updated_at` only per BR-09),
  `audit_logs`.
- Not written: `notifications`, `invoices`, `invoice_lines`, `payments`,
  `payment_allocations`, `visit_incidents`, `visit_evidence`,
  `visit_checklist_items`, `visit_materials`, `visits.review_notes`,
  `reviewed_by_user_id`, `reviewed_at`.
- Schema amendment required: **approved by the user 2026-10-07** (OD-02).
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model,
  and generates one migration only under `--generate-migration`. Agents never
  apply it. Review by `database-reviewer`.
  - SA-03 `customer_signoffs`: add
    `acknowledgement_method varchar(30) NOT NULL CHECK (acknowledgement_method IN ('signed','customer_absent','customer_refused','remote_confirmation'))`;
    `signer_relationship varchar(40) NULL CHECK (signer_relationship IN ('customer','family_member','tenant','property_manager','employee','other'))`;
    `signature_content bytea NULL`;
    `signature_mime_type varchar(40) NULL CHECK (signature_mime_type = 'image/png')`;
    `review_confirmed boolean NOT NULL DEFAULT false`;
    `recorded_by_user_id uuid NOT NULL REFERENCES users(id)`;
    `CHECK ((signature_content IS NULL) = (signature_mime_type IS NULL))`;
    `CHECK ((acknowledgement_method = 'signed') = (signature_content IS NOT NULL OR signature_storage_key IS NOT NULL))`;
    `CHECK (signature_content IS NULL OR octet_length(signature_content) <= 524288)`.
    No feature writes `customer_signoffs` today. The migration adds the
    `NOT NULL` columns without defaults (except `review_confirmed`). If any
    row exists, it fails instead of inventing values.

## Tenant isolation and authorization

- Organization, membership and profile resolved from the session (BR-01).
- Client-provided identifiers: only `visitId` (path), authorized against the
  session organization and an active assignment of the caller's profile;
  otherwise identical `404`. The work order, its sibling visits and the
  signoff are resolved server-side from the authorized visit.
- Completion requires the primary assignment (BR-02); frontend hiding is UX
  only.
- The signature upload is authorized before the body is read beyond the size
  limit and is validated by signature, declared type and size (BR-07).
- Responses use `Cache-Control: no-store`. Logs and audit never contain signer
  names, relationships, comments, reasons, addresses, customer names, notes or
  signature content.

## API contracts

Contract status: Final. Errors use ProblemDetails with `code` where listed.
The first row is `/technician/visits/{visitId}`; every other path is relative to it. Permission
TechnicianSelf. Common errors for every row: `403` (role; `technician_inactive`)
· `404` (BR-01; `technician_profile_not_linked`).

`TechnicianVisitDetail` = `mobile-job-progress` detail & BR-11 fields.
`VisitActionResult` = `{ changed, visit: TechnicianVisitDetail }`.

| Method | Path        | Request                                                                                                                         | Success                     | Errors                                                                                              | Permission     |
| ------ | ----------- | ------------------------------------------------------------------------------------------------------------------------------- | --------------------------- | --------------------------------------------------------------------------------------------------- | -------------- |
| GET    | `/technician/visits/{visitId}` | —                                                                                                                               | `200 TechnicianVisitDetail` | —                                                                                                   | TechnicianSelf |
| POST   | `/complete` | `multipart/form-data`: `method`, `signerName?`, `relationship?`, `comment?`, `reviewConfirmed?`, `signature?` (file), per BR-05–BR-07 | `200 VisitActionResult`     | `400` · `403 not_primary_technician` · `409 visit_status_invalid`, `completion_requirements_unmet` | TechnicianSelf |

Mutations rely on the existing cookie session (`HttpOnly`, `Secure`,
`SameSite=Strict`); no new CSRF mechanism is introduced.

## UI behavior and states

| Screen                                              | Loading                                      | Empty                                                         | Error                                                                          | Permission                                                             | Success     |
| --------------------------------------------------- | -------------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------ | ---------------------------------------------------------------------- | ----------- |
| `/today/visits/:visitId/review` — Review            | As `mobile-job-details` job page             | "No materials recorded.", "No completion note." (BR-12)       | Load as `mobile-job-details`; not ready per BR-13                              | Non-primary read-only (BR-17); other roles existing forbidden state    | BR-12, BR-13 |
| `/today/visits/:visitId/review` — Acknowledgment    | **Complete job** pending (BR-16)             | —                                                             | Field, `409`, `403` and other errors per BR-16/BR-17                           | Primary only (BR-17)                                                   | BR-14–BR-16 |

- Mockups: `design/assets/10-design.png` (Approved; primary reference) with the
  BR-18 deviations; Design 9 from `design/assets/9-design.png`; shell from
  `design/assets/7-design.png`.
- Responsive and accessibility: BR-19.

## Error behavior

| Case                                                     | Response / message shown                                                                          | Data change |
| -------------------------------------------------------- | ------------------------------------------------------------------------------------------------- | ----------- |
| Visit not visible to the caller                          | `404`; "This job isn't available."                                                                | None        |
| Non-primary completion                                   | `403 not_primary_technician`; "The primary technician manages this job."                           | None        |
| Visit not `in_progress`/`paused`/`completed`             | `409 visit_status_invalid`; "This job can't be completed in its current state."                   | None        |
| Required tasks incomplete or Before/After photo missing  | `409 completion_requirements_unmet`; BR-04 message                                                | None        |
| Invalid or disallowed acknowledgment field               | `400` with the field-table message                                                                | None        |
| Invalid, oversized or non-PNG signature                  | `400`; "Capture the signature again."                                                             | None        |
| Repeated or concurrent completion                        | `200 changed = false`; client opens `/today`                                                      | None        |
| Progress edit after completion                           | `409 visit_status_invalid` (`mobile-job-progress` BR-06)                                          | None        |
| Other failure                                            | "We couldn't complete this job. Try again."; entered values kept                                  | None        |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Another technician's visit, an unassigned visit, a visit of another organization and a random id | The detail and `POST …/complete` are called | Identical `404` with no foreign data and no write |
| AC-02 | Other roles (parameterized), an unlinked and an inactive/suspended technician, and an actively assigned non-primary technician | They call `POST …/complete` | Roles `403`; unlinked `404 technician_profile_not_linked`; inactive `403 technician_inactive`; non-primary `403 not_primary_technician`; no write; the non-primary can read the detail with `completion` |
| AC-03 | A ready `in_progress` one-time visit with an open `work` entry | It is completed with a valid `signed` acknowledgment | `200 changed = true`; the work entry is closed; visit `completed` with `actual_completed_at`; one history row `in_progress → completed`; one signoff row with method, name, relationship, PNG content, `review_confirmed = true`, `accepted = true`, `recorded_by_user_id`; `completion_without_signature_reason` null; one `visit.completed` audit row whose data contain no name, relationship, comment or signature; no invoice, payment or notification row |
| AC-04 | A ready `paused` visit with an open `pause` entry | It is completed | The pause entry is closed, its whole seconds are added to `pause_seconds`, the history row is `paused → completed`, and no entry of the visit remains open |
| AC-05 | A completed visit | Completion is called again with the same and with a different valid body, and Start job, Pause, a task edit and a photo upload are called | Completion `200 changed = false` with no write; Start job and edits `409 visit_status_invalid` per `mobile-job-progress` |
| AC-06 | Visits `assigned`, `on_the_way`, `needs_correction` and `approved` (table-driven) | Completion is called with a valid body | `409 visit_status_invalid` with the BR-03 message and no write |
| AC-07 | Ready visits altered to have one required task incomplete, no Before photo, no After photo, and only optional tasks incomplete (table-driven) | Completion is called | The first three `409 completion_requirements_unmet` with no write; the last succeeds |
| AC-08 | Each method with its required fields missing, empty, too long, a disallowed field sent, an unknown relationship, `reviewConfirmed = false` and no method (table-driven) | Completion is called | `400` with the field-table messages and no write; valid bodies of each method succeed |
| AC-09 | Valid `customer_absent`, `customer_refused` (with and without name) and `remote_confirmation` acknowledgments | Each visit is completed | Signoff rows follow the BR-08 mapping: `accepted` false/false/true, `absence_reason` set for absent and refused, `comments` set for remote, no signature, `completion_without_signature_reason` = reason or remote comment |
| AC-10 | A JPEG, a JPEG renamed as PNG, a text file named `.png`, an empty file and a PNG of 512 KiB + 1 byte, sent as `signature` with the `signed` method | Completion is called | `400` "Capture the signature again." with no write; a valid PNG of 512 KiB succeeds |
| AC-11 | A one-time work order `in_progress` with one visit | The visit is completed | Work order `completed` with `updated_at` and one `work_order.status_changed` audit row; `workOrderStatusChanged = true` in the `visit.completed` audit metadata |
| AC-12 | Recurring orders (table-driven): visit 1 of 3 completed with visit 2 `unscheduled`; visit 3 of 3 completed while visit 2 is `in_progress`; then visit 2 completed; visit 2 of 2 completed with visit 1 `cancelled`; an order already `approved_for_billing` | The visits are completed | Order stays `in_progress`, stays `in_progress`, becomes `completed`, becomes `completed`, stays `approved_for_billing`; `completesWorkOrder` in the detail matched each outcome beforehand |
| AC-13 | Concurrent completions of one visit, and concurrent completions of the last two open visits of one recurring order | They run concurrently | One visit: exactly one `changed = true`, one history, one signoff and one audit row; two visits: both complete and the order becomes `completed` exactly once with one `work_order.status_changed` row |
| AC-14 | A ready visit, a not-ready visit and a visit with a primary technician | The detail is loaded | `completion` flags, `ready` and `primaryTechnicianName` follow BR-11; no acknowledgment or signature data are returned |
| AC-15 | Design 9 with required tasks complete and Before/After photos | **Review & complete** is used | The Review step opens at `/today/visits/:visitId/review` (no placeholder) with title, subtitle, service, customer, property, checklist "<c> of <t> tasks complete." and badge, materials lines with "+<n> more", evidence counts, note clamped or "No completion note.", work time and technician per BR-12; rows open the job page at their section |
| AC-16 | Ready and not-ready visits, with and without materials | The Review step renders | Readiness card texts and item states follow BR-13; "Materials recorded" never blocks; **Continue to acknowledgment** is enabled only when ready |
| AC-17 | The acknowledgment step | Each method is selected | Methods, icons, labels and descriptions per BR-05; fields and comment labels per the field table; "Signer name" prefilled and "Relationship" defaulting to "Customer"; the payment info text is always shown; the footer text follows `completesWorkOrder`; switching methods keeps shared values |
| AC-18 | The signature pad with **Signature obtained** | The customer draws, the technician uses **Redo** and **Clear**, and the screen is rotated | Redo removes the last stroke, Clear removes all, both disabled when empty; the text status follows; a lost signature is announced with the BR-15 text |
| AC-19 | Invalid fields for each method | **Complete job** is used | Field messages per the field table appear next to their fields and focus moves to the first invalid field; no request is sent |
| AC-20 | Valid acknowledgments and server responses success, `changed = false`, `409` (both codes), `400` and a network failure | **Complete job** is used | Pending disables both actions; success and `changed = false` open `/today` announcing "Job completed."; `409` shows its message, reloads and returns to Review; `400` shows field messages; other failures show "We couldn't complete this job. Try again." and keep every value including the signature |
| AC-21 | A non-primary technician, a `403 not_primary_technician` on submission, and visits `on_the_way`, `completed` and `needs_correction` | The review route is opened or submitted | Non-primary sees the read-only Review step with "The primary technician manages this job." and **Back to job**, never the acknowledgment step; the 403 switches to that state; the other statuses redirect to `/today/visits/:visitId` |
| AC-22 | Viewports 375 px and 1280 px | Both steps are used by keyboard and touch | Design 10 inside the Design 7 shell with the BR-18 deviations; no page-level horizontal scroll; nothing hidden behind the action bar or bottom navigation; radio semantics for methods; focus moves to each step title; visible focus; targets ≥ 44 px; states not conveyed by color alone |

## Testing requirements

| Level                          | Behavior or risk to prove                                                                                                          | Evidence for        |
| ------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- | ------------------- |
| Authorization/tenant isolation | One parameterized integration test on detail and completion: foreign/unassigned/other-organization visits `404`; non-primary `403`; inactive `403` (role `403` reuses existing TechnicianSelf evidence) | AC-01, AC-02 |
| Backend integration            | Completion effect from `in_progress` and `paused`: entries, `pause_seconds`, status, history, signoff, audit without sensitive data, no billing rows; repeat `changed = false`; post-completion edits `409` | AC-03, AC-04, AC-05 |
| Backend integration            | Status and requirement guards (table-driven)                                                                                        | AC-06, AC-07        |
| Backend integration            | Acknowledgment matrix: validation, disallowed fields, per-method mapping and signature file validation (table-driven)               | AC-08, AC-09, AC-10 |
| Backend integration            | Work order aggregate for one-time and recurring orders, detail `completion` and `completesWorkOrder` (table-driven)                 | AC-11, AC-12, AC-14 |
| Backend integration            | Concurrency on one visit and on two visits of one recurring order                                                                   | AC-13               |
| Backend unit                   | Acknowledgment field matrix and work order aggregate rule, only if implemented as non-trivial domain helpers (0–2 methods)          | BR-06, BR-09        |
| Persistence review             | SA-03 mapping, CHECK constraints and migration (`database-reviewer`); migration generated only with the flag and never applied      | SA-03               |
| Frontend component/service     | Review step rendering and readiness gating; acknowledgment fields per method and client validation; signature pad Clear/Redo; submission success/error handling; non-primary and status redirects (3–6 methods) | AC-15–AC-21 |
| User visual QA (manual)        | Design 10 fidelity, shell, responsive layout, signature on touch device, focus and announcements                                    | AC-22               |

Backend integration stays within 3–8 methods. Agents run no browser
automation or Playwright.

## Dependencies

- `mobile-job-progress` (AUDITED by user override; its audit reported backend
  integration tests not executed): Design 9, time entries, `pause_seconds`,
  evidence, materials, notes, labor time, BR-07 locking, and the review
  placeholder that this feature replaces (supersedes its BR-19 placeholder
  text).
- `mobile-job-details` (AUDITED): job page, guard, load and error states,
  Design 8 rendering of `completed` visits.
- `technician-todays-jobs` (AUDITED): `/today`, shell, completed metrics.
- `dispatch-calendar` (AUDITED): recurring materialization (BR-18) and the
  `work_order.status_changed` audit shape (BR-16).
- `create-work-order` (AUDITED): `job_type`, `recurrence_count`.

## Assumptions

| ID    | Assumption (minor, non-behavioral)                                                                                                     |
| ----- | -------------------------------------------------------------------------------------------------------------------------------------- |
| AS-01 | `signer_contact_id` stays null; the signer is identified by name and relationship only.                                               |
| AS-02 | The signature is stored inline like SA-01 evidence; no image processing beyond the PNG export is performed.                           |
| AS-03 | The "Technician completion note" is `visits.completion_summary` (`mobile-job-progress` AS-02).                                        |
| AS-04 | `signed_at` records when the acknowledgment was stored, which is the completion time.                                                 |

## Open decisions

| ID    | Question                                   | Options                                                                    | Blocking | Resolution                                                                                                     |
| ----- | ------------------------------------------ | -------------------------------------------------------------------------- | -------- | -------------------------------------------------------------------------------------------------------------- |
| OD-01 | Design 10 source                           | Commit the image · draft without it                                        | No       | `design/assets/10-design.png` committed by the user (7ea8dcd) — user 2026-10-07                                |
| OD-02 | Acknowledgment persistence                 | Amendment SA-03 on `customer_signoffs` · existing columns only             | No       | SA-03 with inline signature content (Data and persistence impact) — user 2026-10-07                           |
| OD-03 | Fields per method and remote confirmation  | Proposed matrix with remote recorded by technician · remote via link       | No       | Proposed matrix; signature required for `signed`; remote recorded by the technician, nothing sent (BR-05, BR-06) — user 2026-10-07 |
| OD-04 | Work order aggregate status                | Complete on last open occurrence · never automatic                         | No       | Complete when no non-cancelled visit remains open and the last occurrence exists (BR-09) — user 2026-10-07     |
| OD-05 | Defaults                                   | Complete from `in_progress`/`paused`; server-enforced requirements; repeat `changed = false`; locks; audit without sensitive data; office review out of scope | No | Confirmed (BR-03, BR-04, BR-08, BR-10) — user 2026-10-07 |

## Traceability

| FR    | AC                          |
| ----- | --------------------------- |
| FR-01 | AC-01, AC-02                |
| FR-02 | AC-06, AC-07                |
| FR-03 | AC-08, AC-09, AC-10         |
| FR-04 | AC-03, AC-04                |
| FR-05 | AC-11, AC-12                |
| FR-06 | AC-05, AC-13                |
| FR-07 | AC-14                       |
| FR-08 | AC-15, AC-16                |
| FR-09 | AC-17, AC-18                |
| FR-10 | AC-19, AC-20                |
| FR-11 | AC-21                       |
| FR-12 | AC-22                       |

## Change log

| Date       | Status change | Reason  |
| ---------- | ------------- | ------- |
| 2026-10-07 | — → DRAFT     | Created |
| 2026-10-07 | DRAFT → DRAFT | Revised after validation: BR-03 repeat applies to any valid body (invalid body `400` first); max-length messages for `signerName` and `comment`; explicit detail path in API contracts; AC-05 wording |
