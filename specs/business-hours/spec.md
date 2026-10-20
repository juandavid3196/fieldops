# Business hours

| Field    | Value            |
| -------- | ---------------- |
| Feature  | `business-hours` |
| Type     | Full-stack       |
| Status   | APPROVED         |
| Created  | 2026-10-20       |
| Updated  | 2026-10-20       |
| Approved | 2026-10-20       |

## Context and objective

Branch business hours are stored today in `branches.business_hours` and edited
only inside the branch drawer, while the Administration "Business hours" item
opens a Coming soon page. This feature adds a dedicated weekly editor at
`/admin/business-hours` with its own seven-day contract, saved atomically and
audited, and gives Dispatch a non-blocking warning when a visit is scheduled
outside the branch's hours. Visits can still be scheduled at any time.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Open the page; read and save the business hours of every branch of the session organization | Read or change another organization's branches; supply an organization identifier |
| Viewer (`viewer`) | Open the page read-only; read the business hours of the branches they can access (`company-settings-and-branches` BR-11) | Save (`403`); read branches outside their access (`404`) |
| Other roles (`dispatcher`, `technician`, `accounting`, `operations_manager`) | — | Read or save business hours (`403`); the page shows the forbidden state |
| Owner / Operations Manager / Dispatcher using Dispatch (per `dispatch-calendar`) | See the BR-12 warning when evaluating a visit schedule | — (the warning grants no new permission) |
| Unauthenticated visitor | — | Any endpoint in this spec (`401`); the page redirects to `/auth/sign-in` |
| System | Resolve user, organization and membership from the validated session; write the audit row | Trust client organization identifiers |

## Scope

- `/admin/business-hours` page replacing the `business-hours` Coming soon slug.
- Branch selector limited to accessible branches, read-only time zone.
- Monday–Sunday editor: Open/Closed, one opening time and one closing time.
- `GET` and `PUT /branches/{branchId}/business-hours`, atomic seven-day save,
  optimistic concurrency, audit `branch.business_hours_updated`.
- Loading, empty, error + Retry, forbidden, read-only, dirty/"No unsaved
  changes", submitting, stale, success toast and inline validation states.
- Unsaved-changes confirmation on leave and on branch switch.
- Non-blocking business-hours warning in the Dispatch evaluation and dispatch
  result, shown in the dispatch drawer.

## Non-goals

- Holidays, special dates, multiple shifts per day, lunch breaks, overtime.
- Technician availability or any technician configuration.
- Public booking and capacity.
- Changing the branch drawer, its contract or `company-settings-and-branches`
  behavior; the drawer keeps editing business hours.
- Modifying existing visits when hours change; blocking or requiring a
  justification for visits outside business hours.
- Business-hours indicators on calendar cards, filters or the "With
  conflicts" filter.
- Schema changes or a `branch_business_hours` table.

## User flow

1. Owner opens Administration → Business hours.
2. System loads accessible branches, selects the default (AS-02) and loads its
   hours, time zone and `updatedAt`.
3. Owner toggles days and picks times; inline errors appear next to invalid
   times; the footer changes from "No unsaved changes" to "Unsaved changes".
4. Owner selects **Save changes**; system validates, saves all seven days in
   one transaction, writes one audit row and shows the success toast.
5. **Cancel** restores the loaded values. Leaving the page or switching branch
   with unsaved changes opens the shared discard confirmation.
6. A dispatcher evaluates a visit slot; the drawer shows the BR-12 warning
   when the slot falls outside the branch's hours, and saving stays allowed.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/admin/business-hours` inside the authenticated shell, replacing the `business-hours` Coming soon slug, with the Administration column (current item Business hours), breadcrumb "Administration / Business hours", h1 "Business hours", description and the BR-13 layout and copy. |
| FR-02 | The page must list the branches returned by the existing `GET /branches` (already scoped by BR-11), select the AS-02 default, and show the selected branch's time zone read-only. |
| FR-03 | `GET /branches/{branchId}/business-hours` must return the API contract body for a branch of the session organization the caller can access, with exactly seven days in Monday–Sunday order. |
| FR-04 | `PUT /branches/{branchId}/business-hours` must validate BR-01–BR-05, check BR-06, store all seven days and `updated_at` in one transaction together with the BR-08 audit row, and return `200` with the GET body. |
| FR-05 | The backend must authorize both endpoints per BR-09 and resolve organization and membership only from the session. |
| FR-06 | The editor must let each day be Open or Closed with one opening and one closing time, validate inline per BR-01–BR-03, and never send times for closed days. |
| FR-07 | The page must track dirty state against the loaded values: Save and Cancel follow BR-11; Cancel restores loaded values; leaving the route or switching branch while dirty uses the shared discard confirmation. |
| FR-08 | The page must implement the loading, empty, error, forbidden, read-only, submitting, stale and success states of the UI table. |
| FR-09 | The Dispatch evaluation and dispatch save responses must include the BR-12 `businessHours` result, and the dispatch drawer must show it as a non-blocking warning. |
| FR-10 | Saving business hours must not modify any visit, assignment or schedule. |
| FR-11 | The page must render stacked day rows on mobile with Save changes visible at the end of the form (BR-14). |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | `days` contains exactly seven entries, one per `dayOfWeek` (`monday` … `sunday`, lowercase), no duplicates, no unknown values, no extra properties. Violations → `400` key `days` "Enter a valid value." | Backend |
| BR-02 | Open day (`isOpen = true`): `openingTime` and `closingTime` are required, format `HH:mm` 24-hour, `00:00`–`23:59`. Missing → "This field is required."; malformed → "Enter a valid value." Keys `days.{dayOfWeek}.openingTime` / `days.{dayOfWeek}.closingTime`. | Both |
| BR-03 | `closingTime` must be strictly after `openingTime` on the same day; intervals crossing midnight are not allowed. Violation → key `days.{dayOfWeek}.closingTime` "Closing time must be after opening time." (`ApiErrorService` accepts this message). | Both |
| BR-04 | Closed day (`isOpen = false`): `openingTime` and `closingTime` must be `null` or absent; any value → `400` key `days.{dayOfWeek}` "Enter a valid value." Closed days are never stored with times. | Both |
| BR-05 | Storage reuses `branches.business_hours` in the canonical format of `company-settings-and-branches` BR-03 / `organization-onboarding` BR-15: object keyed by open weekdays `{ "start", "end" }`, closed days omitted, all closed = `{}`. One entry per weekday is guaranteed by the object keys. Time zone stays only in `branches.timezone`. | Backend |
| BR-06 | `PUT` carries the branch `updatedAt` last read. Missing/malformed → `400` key `updatedAt` "Enter a valid value."; different from stored `updated_at` → `409` stale without field errors and nothing changes. The check and the write are atomic, so of two concurrent saves with the same `updatedAt` exactly one succeeds. Shares the branch `updated_at` with the branch drawer. | Backend |
| BR-07 | A `PUT` whose seven days equal the stored hours (after canonicalization) and whose `updatedAt` matches returns `200` with the current body, does not change `updated_at` and writes no audit row. | Backend |
| BR-08 | Audit row on every effective change: action `branch.business_hours_updated`, `entity_type` `branch`, `entity_id` and `branch_id` = branch id, `organization_id` and `actor_user_id` from the session, `ip_address` = client IP, `before_data` / `after_data` containing only the changed days keyed by `dayOfWeek` with `{ isOpen, openingTime, closingTime }`, `metadata` `{ "changedDays": [...] }` in Monday–Sunday order. No contact data, request bodies or other branch fields are recorded or logged. | Backend |
| BR-09 | Authorization reuses `company-settings-and-branches` BR-10/BR-11: **CompanySettingsView** (`owner`, `viewer`) for GET; **CompanySettingsManage** (`owner`) for PUT. Other roles → `403`; no session → `401`. A branch of another organization, a non-existent branch, or a branch outside a viewer's access → `404`. Any organization identifier in path, query or body is ignored. GET returns `canManage = true` only for the manage policy. | Backend |
| BR-10 | Time selectors list 30-minute steps in 12-hour display (opening `12:00 AM`–`11:30 PM`, closing `12:30 AM`–`11:30 PM`); values are sent as `HH:mm`. A loaded time not on a 30-minute step is added as an extra option for that field so it is shown and preserved unchanged. Reopening a closed day proposes `08:00`–`17:00`; a closed day shows disabled selectors with those placeholder values. | Frontend |
| BR-11 | Footer shows "No unsaved changes" when the form equals the loaded values and "Unsaved changes" otherwise. Save changes and Cancel are disabled while clean or submitting. Save with client errors sends nothing, shows the inline errors and focuses the first invalid field. | Frontend |
| BR-12 | Dispatch `businessHours` result for the evaluated/saved slot, computed from the visit's branch hours on the visit's local date in the branch time zone: `{ outside, dayOfWeek, isOpen, openingTime \| null, closingTime \| null }`. `outside = true` when the day is closed, `start < openingTime` or `end > closingTime`. When the branch has no time zone the result is `null` and no warning shows. It is not a `Conflict`, never appears in `conflicts`, never requires a justification and never blocks the save. Drawer copy (warning banner): closed → "Outside business hours: {Branch name} is closed on {Day}."; open → "Outside business hours: {Branch name} is open {opening}–{closing} on {Day}." (12-hour times). | Both |
| BR-13 | Page copy: description "Set the standard operating hours for each branch."; fields "Branch", "Time zone"; time zone shown as `{IANA id} (GMT±H[:MM])` using its current offset, or "Not set" when null; helper "Business hours follow the selected branch time zone."; info "Business hours define when this branch normally operates. Technician availability is managed separately."; section "Weekly hours" / "Set one opening period for each day."; day labels Monday–Sunday; toggle labels "Open"/"Closed"; separator "to"; note "Visits can still be scheduled outside business hours, but Dispatch will see a warning."; buttons **Cancel**, **Save changes**; inactive branches suffixed " (Inactive)" in the selector. | Frontend |
| BR-14 | Responsive/accessibility: below the existing mobile breakpoint each day is a stacked row (day + toggle, then opening and closing selectors) and the footer with Save changes stays at the end of the form (not hidden). Each toggle and selector has an accessible name including the day; inline errors are linked to their field. | Frontend |
| BR-15 | Messages (by `ApiError.kind`): forbidden "You don't have access to business hours."; read-only "You have view-only access to business hours."; branches load error "We couldn't load branches." + **Retry**; hours load error "We couldn't load business hours." + **Retry**; stale `409` "This record was changed by someone else. Reload to see the latest version." + **Reload**; `404` "This branch is no longer available." and the branch list reloads; empty "No branches available."; success toast "Business hours saved for {Branch name}"; other failures show `ApiError.message`. | Frontend |

## States and transitions

Not applicable: business hours are a configuration value without lifecycle
states.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `branches` (`id`, `organization_id`, `name`, `timezone`, `business_hours`,
  `is_active`, `is_main`, `updated_at`), `organization_user_branches` and
  memberships (read for BR-11 access), `audit_logs` (insert per BR-08),
  `visits` and dispatch tables (read only for BR-12).
- Schema amendments required: None. The requested `branch_business_hours`
  table is not needed because `branches.business_hours` already models one
  entry per weekday (BR-05).
- No migration.

## Tenant isolation and authorization

- Organization context: resolved from the validated session on every request.
- Client-provided organization identifiers: none accepted; `branchId` in the
  path is authorized against the session organization and BR-11 access.
- Branch of another organization or outside a viewer's access: `404`, no data
  or audit change.
- Permissions: BR-09. Frontend `canManage`, read-only state and hidden save are
  UX only.
- Dispatch BR-12 uses the visit's branch already authorized by
  `dispatch-calendar`; it adds no new data exposure beyond that branch's hours.

## API contracts

Contract status: Final.

`BusinessHoursDay` = `{ dayOfWeek: "monday"…"sunday", isOpen: boolean, openingTime: "HH:mm" | null, closingTime: "HH:mm" | null }`.
`BusinessHoursBody` = `{ branchId, branchName, timezone | null, updatedAt, canManage, days: BusinessHoursDay[7] }` (Monday–Sunday; closed days have `null` times).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/branches/{branchId}/business-hours` | — | `200 BusinessHoursBody` | `401` · `403` · `404` | View |
| PUT | `/branches/{branchId}/business-hours` | JSON `{ updatedAt, days: BusinessHoursDay[7] }` | `200 BusinessHoursBody` | `400` ValidationProblemDetails (BR-01–BR-04, BR-06 keys) · `401` · `403` · `404` · `409` stale · `413` · `415` | Manage |
| POST | `/dispatch/visits/{visitId}/evaluation` | Unchanged | `200` existing body + `businessHours: BusinessHoursCheck \| null` | Unchanged | Unchanged |
| PUT | `/dispatch/visits/{visitId}` | Unchanged | `200 VisitDispatchResult` + `businessHours: BusinessHoursCheck \| null` | Unchanged | Unchanged |

`BusinessHoursCheck` = `{ outside, dayOfWeek, isOpen, openingTime | null, closingTime | null }` (BR-12).

- `updatedAt` uses the existing branch format (ISO 8601 UTC, microsecond
  precision). Request size limit and `Cache-Control: no-store` follow
  `BranchesController`.
- The page reads the branch list from the existing `GET /branches`.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Business hours page | Skeleton for the branch card and seven day rows while branches or hours load | "No branches available." when `GET /branches` returns none | Branches or hours load error + **Retry**; stale `409` + **Reload** with edits kept; `404` message and branch list reload; field errors inline | Forbidden state for `403`; read-only (disabled controls, no Cancel/Save, view-only message) when `canManage = false` | Toast BR-15; form adopts the returned values; footer "No unsaved changes" |
| Dispatch drawer | Unchanged | Unchanged | Unchanged | Unchanged | BR-12 warning banner when `outside = true`; save allowed |

- Submitting: Save changes shows progress; inputs and branch selector locked.
- Mockups: `design/assets/Business hours FieldOps.png` (Approved). Its
  breadcrumb "Settings" and its sidebar are superseded by FR-01 and the
  current shell.
- Responsive and accessibility constraints: BR-14.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid day structure | `400` key `days` | None |
| Open day missing/malformed time | `400` field key; inline error | None |
| Closing not after opening | `400` `days.{day}.closingTime`; inline error | None |
| Closed day with times | `400` `days.{day}` | None |
| Missing/malformed `updatedAt` | `400` key `updatedAt` | None |
| Stale `updatedAt` | `409`; stale message + **Reload** | None |
| Viewer or other role saves | `403` | None |
| Foreign / inaccessible / unknown branch | `404`; BR-15 message | None |
| Load failure | Error + **Retry** | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A signed-in Owner at desktop width | They open Administration → Business hours | `/admin/business-hours` renders in the shell with the Administration column (Business hours current), breadcrumb "Administration / Business hours", h1, description and BR-13 layout matching the approved mockup; the Coming soon slug no longer applies |
| AC-02 | A branch storing Monday–Friday 08:00–17:00, Saturday 08:00–12:00, Sunday closed | Owner calls `GET /branches/{id}/business-hours` | `200` with branch id, name, time zone, `updatedAt`, `canManage = true` and seven days in order with Sunday `isOpen = false` and `null` times |
| AC-03 | An Owner of organization A and a branch of organization B | `GET` or `PUT` business hours with B's branch id | `404`; B's branch and audit rows unchanged |
| AC-04 | A Viewer limited to branch X | They call GET for X, GET for another branch Y, and PUT for X | GET X `200` with `canManage = false`; GET Y `404`; PUT `403`; nothing changes |
| AC-05 | A `dispatcher` and an unauthenticated caller | They call GET and PUT | `403` and `401` respectively; no change |
| AC-06 | An Owner with the current `updatedAt` | `PUT` changes Saturday closing to 13:00 and closes Wednesday | `200` with the new days; `branches.business_hours` stores the canonical BR-05 object without Wednesday; `updated_at` advanced; one `branch.business_hours_updated` audit row with `branch_id`, before/after for only Wednesday and Saturday and `metadata.changedDays = ["wednesday","saturday"]`, without other branch data |
| AC-07 | For each rule in BR-01–BR-04 and `updatedAt`, a body failing only that rule | `PUT` | `400` with that key and message; no row or audit change |
| AC-08 | A stored `updated_at` different from the sent `updatedAt`, and separately two concurrent saves with the same current `updatedAt` | `PUT` | Stale request → `409` without field errors and no change; concurrent case → exactly one `200`, one `409`, one audit row |
| AC-09 | A body identical to the stored hours with the current `updatedAt` | `PUT` | `200`; `updated_at` unchanged; no audit row |
| AC-10 | A branch with scheduled visits | Owner saves new hours that exclude those visits' times | No visit, assignment or schedule row changes |
| AC-11 | A Viewer with access to two branches, one inactive and one main | The page loads | The selector lists only those two branches, the inactive one suffixed " (Inactive)"; the main branch is selected; the time zone shows read-only as `{IANA} (GMT±H)` or "Not set" |
| AC-12 | The page loading branches or hours, then a failed load | Loading and failure occur | The skeleton shows while loading; the failure shows the BR-15 message and **Retry**, which reloads; a Viewer with no accessible branches sees "No branches available." |
| AC-13 | A `dispatcher` and a Viewer | They open the page | The dispatcher sees the forbidden state; the Viewer sees disabled controls, the view-only message and no Cancel/Save |
| AC-14 | An Owner editing a day | They turn a day Closed, then Open again | Closed shows "Closed" and disabled selectors; reopening proposes 8:00 AM–5:00 PM; the saved request carries `null` times for every closed day |
| AC-15 | An Owner editing hours | A closing time is not after the opening time, or a time is cleared on an open day, and Save is pressed | The BR-02/BR-03 message appears next to that field, nothing is sent and focus moves to the first invalid field; server field errors map to the same fields |
| AC-16 | A loaded time of 08:15 | The page renders | The selector shows 8:15 AM as an extra option alongside the 30-minute steps; saving other days keeps `08:15` |
| AC-17 | A clean form | The Owner edits a time, then presses Cancel | Footer goes from "No unsaved changes" to "Unsaved changes" with Save/Cancel enabled; Cancel restores loaded values and the clean state |
| AC-18 | A dirty form | The Owner navigates away or selects another branch | The shared discard confirmation opens; Keep editing stays with edits; Discard completes the navigation or loads the other branch |
| AC-19 | A dirty valid form | The Owner saves successfully | Inputs and selector lock while submitting; the toast "Business hours saved for {Branch name}" appears; the form shows the returned values and "No unsaved changes" |
| AC-20 | A save returning stale `409` or `404` | The page handles it | `409`: stale message and **Reload** with edits kept; Reload fetches fresh hours and clears dirty. `404`: BR-15 message and the branch list reloads |
| AC-21 | Mobile width | The page renders | Each day is a stacked row; Save changes is visible at the end of the form; controls have day-specific accessible names |
| AC-22 | A branch with a time zone, open Monday 08:00–17:00 and closed Sunday | Dispatch evaluates Monday 16:00–18:00, Sunday 10:00–11:00, and Monday 09:00–10:00 | The first two return `businessHours.outside = true` (with the day's hours / closed) and the third `false`; none adds a `conflicts` entry; saving the outside slots succeeds without justification and the result carries the same `businessHours` |
| AC-23 | The dispatch drawer with an outside slot, and a branch without time zone | The slot is evaluated | The BR-12 warning banner shows the closed/open copy and the save stays enabled; for the branch without time zone `businessHours` is `null` and no warning shows |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Seven-day validation/canonicalization table (BR-01–BR-04, round-trip with stored format); BR-12 outside classification (closed day, start before, end after, inside). ≤2 methods, parameterized | AC-07, AC-16, AC-22 |
| Backend integration | GET contract; PUT success with canonical storage and audit diff; no-op; parameterized validation; stale and concurrent `409`; visits untouched. ≤5 methods | AC-02, AC-06–AC-10 |
| Authorization/tenant isolation | One cross-tenant `404`; Viewer scope `404` and PUT `403`; other role `403`; `401`. ≤2 methods | AC-03, AC-04, AC-05 |
| Backend integration — Dispatch | Evaluation and save carry `businessHours`, outside never becomes a conflict or blocks; `null` without time zone. 1 method | AC-22, AC-23 |
| Frontend component/service | Page states (loading, error + Retry, empty, forbidden, read-only); branch default/inactive label/time zone; closed/reopen and request mapping incl. off-step time; inline validation and focus; dirty/Cancel/discard on leave and branch switch; save success, stale and `404`; drawer warning banner. ≤6 methods | AC-11–AC-20, AC-23 |
| User visual QA | Mockup fidelity, desktop and mobile stacked layout, keyboard/focus | AC-01, AC-21 |

The concurrent stale case is covered because the same branch value is written
by two editors (this page and the branch drawer).

## Dependencies

- `company-settings-and-branches` (AUDITED): branch model, BR-10/BR-11
  policies and access, `updatedAt` concurrency, audit pattern, branch drawer.
- `organization-onboarding` BR-15: canonical `business_hours` format and
  `BusinessHoursValidator`.
- `dispatch-calendar` (AUDITED): evaluation/save contracts and drawer.
- `authenticated-app-shell` and `design-18-users-and-permissions`: shell,
  Administration column, Coming soon replacement precedent.
- Shared discard-changes dialog.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The selector includes inactive accessible branches labelled " (Inactive)"; inactive branches can be edited, as in the drawer. |
| AS-02 | Default branch: the main branch when accessible, otherwise the first by name. |
| AS-03 | Visits use the branch's local date and times as stored by Dispatch; BR-12 compares those directly with the branch hours. |
| AS-04 | The old `/coming-soon/business-hours` URL behaves like any unknown Coming soon slug once removed from the module list. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Route | `/admin/business-hours` · `/administration/business-hours` | No | `/admin/business-hours` (admin convention) — user 2026-10-20 |
| OD-02 | Permissions | Reuse company settings policies · add roles | No | Reuse CompanySettingsView/Manage with BR-11 scope; Viewer read-only — user 2026-10-20 |
| OD-03 | Duplicate editing with the branch drawer | Keep both · remove from drawer | No | Keep both, sharing `updatedAt` — user 2026-10-20 |
| OD-04 | Concurrency | `updatedAt` check · last write wins | No | `updatedAt` optimistic check, `409` + Reload — user 2026-10-20 |
| OD-05 | Time selector | 30-min · 15-min · free input | No | 30-minute dropdown, off-step loaded values preserved — user 2026-10-20 |
| OD-06 | Dispatch warning | Evaluation/result field + drawer banner · also calendar cards | No | Field on evaluation and save result, drawer banner only, not a conflict, `null` without time zone — user 2026-10-20 |
| OD-07 | No-op save | No audit · always audit | No | `200`, no audit, `updated_at` unchanged — user 2026-10-20 |
| OD-08 | Mockup path | Copy · reference existing | No | Copied to `design/assets/Business hours FieldOps.png` by user authorization — 2026-10-20 |
| OD-09 | Persistence | New `branch_business_hours` table · reuse `branches.business_hours` | No | Reuse existing model per request ("reutilizar el modelo existente") — 2026-10-20 |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-11 |
| FR-03 | AC-02 |
| FR-04 | AC-06, AC-07, AC-08, AC-09 |
| FR-05 | AC-03, AC-04, AC-05 |
| FR-06 | AC-14, AC-15, AC-16 |
| FR-07 | AC-17, AC-18 |
| FR-08 | AC-12, AC-13, AC-19, AC-20 |
| FR-09 | AC-22, AC-23 |
| FR-10 | AC-10 |
| FR-11 | AC-21 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-20 | — → DRAFT     | Created |
| 2026-10-20 | DRAFT → APPROVED | Approved by user via /spec approve |
