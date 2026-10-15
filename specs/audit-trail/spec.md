# Audit trail

| Field    | Value         |
| -------- | ------------- |
| Feature  | `audit-trail` |
| Type     | Full-stack    |
| Status   | APPROVED      |
| Created  | 2026-10-15    |
| Updated  | 2026-10-15    |
| Approved | 2026-10-15    |

## Context and objective

FieldOps already writes `audit_logs` rows from about 100 mutation points, but
nobody can read them. This feature adds a read-only administrative audit trail
at `/audit-log` (design 29): server-computed metrics, search, filters, sorting,
real pagination, a detail drawer and a server-side CSV export. It also
centralizes the action catalog (label, impact, source, financial flag) on the
server, redacts sensitive values everywhere they could be shown, and closes
two coverage gaps: data exports (`export.generated`) and sign-out
(`auth.signed_out`). Historic events stay immutable.

## Actors and permissions

Role access follows the `design-18-users-and-permissions` BR-07 "Audit log" row.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner | View, search, filter and export every visible event (BR-10, BR-11) | Edit or delete events |
| Operations Manager | Same as Owner | Edit or delete events |
| Accounting | View, search, filter and export **financial** events only (BR-09), within branch scope | See or export non-financial events; edit or delete events |
| Dispatcher, Technician, Viewer | — | Any audit-log endpoint (`403`) |
| System | Resolve organization and membership from the session; compute visibility, metrics, filters, paging, redaction and CSV; write `export.generated` and `auth.signed_out` rows | Trust a client organization identifier; update or delete audit rows |

## Scope

- `/audit-log` page in the current shell with metrics, search, filters (date range, user, action, entity, branch, impact), sorting, pagination and CSV export.
- Detail drawer: full information, before/after changes, technical metadata, copyable event ID, "View related activity" and navigation to the related entity when the caller has access.
- Server-side action catalog (label, impact Low/Medium/High, source, financial flag, entity labels).
- Redaction of sensitive values in list, detail, search and CSV.
- `export.generated` rows for every existing CSV data export and for the new audit-log export.
- `auth.signed_out` row on sign-out.
- Index `ix_audit_org_occurred` (schema doc and generated migration, not applied).

## Non-goals

- Custom analytics, change restore, event deletion or editing, retention configuration, PDF export, real-time security monitoring.
- Auditing failed sign-ins (`specs/sign-in/spec.md` BR-13 stays unchanged); a "Failed sign-ins" metric.
- Auditing queries, page opens, downloads of import templates or purely visual actions.
- Changing the shape of existing audit rows written by other features (they are read and redacted as stored).
- A Reports module; "Reports" stays a Coming soon item.
- Reproducing the mockup's sidebar or breadcrumb.

## User flow

1. An Owner, Operations Manager or Accounting member selects **Audit log** in the sidebar.
2. System shows today's metrics and today's events, newest first, 25 per page.
3. The member changes search/filters and selects **Apply filters**, sorts by a column, or changes page/page size; the list reloads from the server.
4. The member selects **View** on a row; the drawer shows the event in full.
5. In the drawer the member copies the event ID, opens the related entity (when allowed) or selects **View related activity** to filter the list to that entity.
6. The member selects **Export CSV**; System downloads the file built from the current filters and sort and records `export.generated`.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | `GET /audit-log` must return the caller's visible events (BR-09 to BR-11) filtered, sorted and paged per BR-03 to BR-05, with row content per BR-06. |
| FR-02 | `GET /audit-log/summary` must return the four BR-07 metrics for the current local day within the caller's visibility, independent of filters. |
| FR-03 | `GET /audit-log/filters` must return the filter options per BR-08, limited to the caller's visibility. |
| FR-04 | `GET /audit-log/{id}` must return one visible event with changes, metadata and target per BR-13; invisible or unknown ids return `404`. |
| FR-05 | `GET /audit-log/export` must return a CSV of the filtered, sorted events per BR-14 and then record `export.generated` per BR-16. |
| FR-06 | The backend must own the action catalog per BR-01 and BR-02; unknown action codes degrade per BR-02. |
| FR-07 | Sensitive values must be redacted per BR-12 in list, summary text, detail, search and CSV. |
| FR-08 | Access must follow BR-09 (roles), BR-10 (financial-only), BR-11 (branch scope) and organization isolation. |
| FR-09 | Audit events must be immutable through the application (BR-17). |
| FR-10 | Every existing CSV data export must record `export.generated` after success per BR-16. |
| FR-11 | Sign-out with a valid session must record `auth.signed_out` per BR-18. |
| FR-12 | The schema doc and a generated, unapplied migration must add `ix_audit_org_occurred` per Data impact. |
| FR-13 | The page must render metrics, search, filters, table, sorting, pagination and states per BR-19 to BR-21 and the UI table. |
| FR-14 | The drawer must render event detail and actions per BR-22. |
| FR-15 | The route and sidebar item must follow BR-23. |
| FR-16 | Timestamps must be stored and transported in UTC and presented in the organization time zone per BR-24. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Catalog: a backend-defined, read-only map from each action code written by the application to `{ label, impact, source, isFinancial }`, plus entity-type labels. It covers every action code currently written (including `auth.signed_in`, `auth.signed_out`, `export.generated` and the portal/payment/webhook codes). Labels are sentence case, e.g. `payment.recorded` "Payment recorded", `work_order.status_changed` "Status changed", `visit.assigned` "Assignment changed". Entity labels, e.g. `service_request` "Request", `quote` "Quote", `work_order` "Work order", `visit` "Visit", `customer` "Customer", `invoice` "Invoice", `payment` "Payment", `catalog_item` "Product or service", `organization_user` "User access", `user_invitation` "Invitation", `technician_profile` "Team member", `organization` "Settings", `branch` "Branch", `user` "User account", `export` "Export". | Backend |
| BR-02 | Impact. **High**: `user.access_updated`, `user.suspended`, `user.reactivated`, `user.invitation_revoked`, `organization.settings_updated`, `organization.bank_details_updated`, `branch.set_as_main`, `payment.recorded`, `payment.refunded`, `invoice.payment_applied`, `invoice.refund_applied`, `customer.archived`, `customer.imported`, `catalog_item.imported`, `quote.cancelled`. **Medium**: `export.generated`, `user.invited`, `branch.created`, `branch.updated`, `invoice.created`, `invoice.sent`, `quote.sent`, `quote.approved`, `quote.rejected`, `quote.revised`, `work_order.created`, any code ending in `.status_changed`, `.assigned`, `.cancelled`, `.deactivated`, `.archived`, `.branch_changed`, `visit.completed`, `catalog_item.updated`, `technician_profile.updated`. **Low**: every other catalog code. The explicit High list takes precedence over the Medium patterns (e.g. `customer.archived` and `quote.cancelled` are High). Source is resolved per row, in this order: (1) `actor_user_id` is a user without a membership in the event's organization (a customer portal contact) → "Customer"; (2) `actor_user_id` is null and the code is one of `service_request.created`, `quote.approved`, `quote.rejected`, `quote.clarification_requested`, `invoice.bank_transfer_reported`, `invoice_payment_attempt.created`, `visit.reschedule_requested`, `customer.message_sent` (public-link actions) → "Customer"; (3) any other null `actor_user_id` (e.g. `payment_intent.*`, `quote.expired`, webhooks) → "System"; (4) an organization member with a visit field-progress code (`visit.travel_started`, `visit.on_the_way`, `visit.arrived`, `visit.job_started`, `visit.started`, `visit.paused`, `visit.resumed`, `visit.material_recorded`, `visit.evidence_added`, `visit.evidence_deleted`, `visit.completed`) → "Mobile app"; (5) otherwise "Web app". `isFinancial` is true for codes starting `invoice.`, `payment.`, `payment_intent.`, `invoice_payment_attempt.`, `work_order.billing_`, and for `export.generated` whose `reportType` is `invoices`, `payments` or `billing_review`. An action code missing from the catalog shows its code humanized ("foo.bar_baz" → "Foo bar baz"), impact Low, source per the same per-row order, `isFinancial` false. | Backend |
| BR-03 | Filters (query): `from`, `to` (`yyyy-MM-dd`, local days in the organization time zone, inclusive; default both = today; `from > to`, invalid date or a span over 366 days → `400 errors.from`/`errors.to`); `q` (trimmed, ≤ 100 chars else `400 errors.q`; empty = none); `actorId` (uuid of a user with a membership in the organization, or `system` for null actor; otherwise `400 errors.actorId`); `action` (catalog code visible to the caller, else `400 errors.action`); `entityType` (catalog entity type, else `400 errors.entityType`); `entityId` (uuid, only with `entityType`, else `400 errors.entityId`); `branchId` (active or inactive branch of the organization within the caller's scope; foreign, unknown or out-of-scope → `404 branch_not_found`); `impact` (`low`, `medium`, `high`, else `400 errors.impact`). All filters combine with AND. | Backend |
| BR-04 | Search `q`: case-insensitive substring match on the actor's full name, the action label, the entity label, the resolved reference (BR-06) and the IP address text; `AUD-<n>` (case-insensitive) also matches event id `n` exactly. Never matches emails, phone numbers, `before_data`, `after_data` or `metadata` values. | Backend |
| BR-05 | Sorting and paging: `sort` = `occurredAt` (default) or `impact`; `direction` = `desc` (default) or `asc`; ties by `occurred_at` desc then `id` desc (impact sort orders High > Medium > Low for `desc`). Unknown → `400 errors.sort`/`errors.direction`. `page` ≥ 1 (default 1, else `400 errors.page`); `pageSize` ∈ {25, 50, 100} (default 25, else `400 errors.pageSize`). A page beyond the last returns empty `items` with the correct `total`. Only the requested page is loaded; the client never receives all events. | Backend |
| BR-06 | Row content: `eventId` "AUD-<id>"; `occurredAt` (UTC); actor = current full name of `actor_user_id` (members and portal contacts), or "Customer" for BR-02 rule (2) rows, or "System"; action label; entity label; reference = current display value of the entity (`REQ-<n>`, quote/work-order/invoice/payment numbers in their existing display formats, a `visit` shows its work order number `WO-<n>`, customer display name, product/service name, member, team member or contact full name, a `user_invitation` shows the invitee's full name (never the email), branch name, "—" when unresolvable or for `user`/`export`); summary (BR-15); branch name or "All branches" when `branch_id` is null; source (BR-02); IP address text or null; impact. | Backend |
| BR-07 | Summary for the current local day (organization time zone) within the caller's visibility: `eventsToday` (count), `activeUsersToday` (distinct non-null actors), `highImpactToday` (High-impact count), `accessChangesToday` (codes starting `auth.` or `user.`). Labels "Events today", "Users active", "High-impact changes", "Access changes". Not interactive and not affected by filters. | Both |
| BR-08 | Filter options: `actions` (catalog codes visible to the caller, label-sorted), `entityTypes` (catalog entity types visible to the caller), `users` (members of the organization in any membership status, full name sorted, plus "System"), `branches` (branches within the caller's scope, active and inactive, name-sorted). Defaults "All users", "All actions", "All entities", "All branches", "All levels". | Both |
| BR-09 | Roles: `owner`, `operations_manager`, `accounting` may call every audit-log endpoint; `dispatcher`, `technician`, `viewer` receive `403` on all of them. The role is resolved from the session membership, never from the client. | Backend |
| BR-10 | Accounting sees, searches, counts, opens and exports only events whose catalog entry has `isFinancial = true`. A non-financial id requested by Accounting returns `404`; a non-financial `action` filter returns `400 errors.action`. | Backend |
| BR-11 | Branch scope: a member with `is_all_branches = true` sees events of any branch and events with null `branch_id`. A branch-limited member sees only events whose `branch_id` is in their `organization_user_branches`; null-branch events are hidden. Applies to list, summary, filters, detail and export. Organization isolation: only rows with the session `organization_id` are ever read; another organization's id returns `404`. | Backend |
| BR-12 | Redaction, applied recursively (objects and arrays) to `before_data`, `after_data` and `metadata` before any value leaves the server: a key containing (case-insensitive) `password`, `token`, `secret`, `hash`, `apikey`, `api_key`, `authorization`, `cookie`, `cvc`, `cvv`, `cardnumber`, `card_number`, `iban`, `accountnumber`, `account_number`, `routing` → `"[redacted]"`; a key containing `email` or `phone` with a non-null value → `"[changed]"`. Keys `last4`/`cardLast4` are kept. Redacted values are never searchable (BR-04), never appear in summaries (BR-15) and never in CSV. | Backend |
| BR-13 | Detail adds: actor `{ id, name, roleName }` (current role name of the actor's membership, null if none); `changes` = one row per key in the union of redacted before/after, `{ field (key), label (humanized key), before, after }`, values as display strings ("—" for null/absent), empty when both sides are null; `metadata` = redacted key/value pairs plus `eventId`, `entityType`, `entityId`, `actorUserId`, `branchId`, `source`; `target` per BR-25. | Backend |
| BR-14 | CSV `GET /audit-log/export`: same query as the list without `page`/`pageSize`, same sort; `text/csv; charset=utf-8`; file `audit-log-<yyyy-mm-dd>.csv` (organization date). Columns (exact headers): `Event ID`, `Timestamp (local)` (organization local `yyyy-MM-dd HH:mm:ss`), `Timestamp (UTC)` (ISO 8601), `Time zone` (IANA), User, Action, Entity, Reference, Summary, Branch, Source, IP address, Impact. Before/after values and metadata are not exported. Escaping and the 5,000-row limit (`409 export_too_large` "Narrow the filters to export 5,000 rows or fewer.") follow `completed-jobs-review` BR-21. | Backend |
| BR-15 | Summary text: when before and after both have changed keys, "<label>: <before> → <after>" for the first changed key in key order, plus " (+<n> more)" when more keys changed; when only after exists, "<action label> <reference>" (reference omitted when "—"); otherwise the action label. Values come from the redacted data, so emails/phones appear only as `[changed]` and secrets as `[redacted]`. Truncated to 160 characters with "…". | Backend |
| BR-16 | `export.generated` is inserted only after an export's CSV has been fully built for a `200` response and before it is returned: `entity_type = 'export'`, `entity_id` null, `actor_user_id` = caller, `ip_address` = client IP, `branch_id` = the export's single branch filter or null, `before_data` null, `after_data` null, `metadata` `{ reportType, filters, rowCount }`. `reportType` ∈ `audit_log`, `invoices`, `payments`, `billing_review`, `business_overview`, `catalog_items`. `filters` holds normalized filter values (ids, codes, dates, sort) and `hasSearch` (boolean) instead of the search text; no names, emails or free text. If the insert fails, the export returns `500` and no file. `400`, `403`, `404` and `409` responses write nothing. Import-template downloads are not exports. | Backend |
| BR-17 | Immutability: no endpoint updates or deletes `audit_logs`; `PUT`, `PATCH` and `DELETE` on `/audit-log` and `/audit-log/{id}` return `405` and change nothing. The UI shows no edit or delete control and the drawer note "Audit events are read-only and cannot be edited or deleted." | Both |
| BR-18 | `DELETE /sessions/current` with a valid session inserts `auth.signed_out`: `entity_type = 'user'`, `entity_id` and `actor_user_id` = session user, `organization_id` = session organization, `ip_address` = client IP, `branch_id`/`before_data`/`after_data` null, `metadata` `{}`. Without a valid session the existing response is unchanged and nothing is written. If the insert fails the sign-out still completes (cookie cleared, existing response) and the failure is logged without user or session data. | Backend |
| BR-19 | Page copy: title "Audit trail", subtitle "Review who changed what and when across your organization.", **Export CSV**, metric cards per BR-07, search placeholder "Search user, entity, reference, or event ID…", filter labels Date, User, Action, Entity, Branch, Impact, **Apply filters**, **Reset**, count "<total> events", columns Timestamp, User, Action, Entity, Reference, Summary, Branch, Source / IP, Impact, Details ("View"), footer "Showing <a>–<b> of <total> events", page sizes "25 / page", "50 / page", "100 / page". Source / IP shows the IP when present, else the source label. Impact tags High (warning), Medium (caution), Low (success) always with text. | Frontend |
| BR-20 | Filter interaction: search and filter edits apply only on **Apply filters** or Enter in the search field, resetting to page 1. **Reset** restores BR-03 defaults, default sort and page 1, and reloads. Sorting (Timestamp and Impact headers) and paging reload immediately. Filters, sort, page and page size are reflected in the URL query and restored on load; invalid URL values fall back to defaults. | Frontend |
| BR-21 | Export: uses the applied (not unsaved) filters and sort; the button shows busy state and is disabled while exporting; `409` shows "Narrow the filters to export 5,000 rows or fewer."; other failures show "We couldn't export the audit log. Try again."; success downloads the file with the server filename. | Frontend |
| BR-22 | Drawer (title "Audit event", 400px desktop, full width below 640px): impact tag, local timestamp, actor initials/name/role, Action, Entity + reference + "Open <entity label>" link only when `target` is not null, Branch, Source, IP address ("—" when null), "Change summary" table (Field, Before, After; hidden when `changes` is empty), "Metadata" key/value list, **Copy event ID** (copies `AUD-<id>`, toast "Event ID copied"), **View related activity** (shown when `entityId` is not null; closes the drawer and applies `entityType` + `entityId` with the date range widened to the last 366 days), BR-17 note and "Events are retained according to your organization's data policy.". Close by ✕ or Escape returns focus to the row's **View**. Opening loads `GET /audit-log/{id}`; `404` shows "This event is no longer available." | Frontend |
| BR-23 | Route `/audit-log` inside the authenticated shell. Sidebar item "Audit log" (history icon) as an independent item after "Reports" in the Finance group, shown only to `owner`, `operations_manager`, `accounting` (UX only). The route is not role-guarded; any `403` renders the existing in-page forbidden state. | Frontend |
| BR-24 | Storage and API timestamps are UTC (ISO 8601). The list response carries `timeZone` (IANA `organizations.timezone`); table shows "MMM d, yyyy" over "h:mm:ss a", drawer "MMM d, yyyy 'at' h:mm:ss a", both in that zone. Date filters and "today" use the same zone. | Both |
| BR-25 | Target: `{ kind, id }` or null, computed for the caller's role and existing module access. Kinds and destinations follow the notifications-center target mapping where it exists: `service_request` (requests pipeline with the request opened), `quote` (`/quotes/<id>`), `work_order` (`/jobs/<id>`), `visit` (its work order), `customer` (`/customers/<id>`), `invoice` (`/invoices/<id>`), `payment` (its invoice), `catalog_item` (Products and services), `technician_profile` (team member), `organization_user`/`user_invitation` (Users & permissions), `organization`/`branch` (Company settings). Null when the entity no longer resolves, the caller's role cannot open that module, or the entity is outside the caller's branch scope. | Backend |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`): `audit_logs` (all columns; read, and insert for `export.generated`/`auth.signed_out`), `organizations.timezone`, `organization_users` (`organization_id`, `user_id`, `role_id`, `status`, `is_all_branches`), `organization_user_branches`, `roles`, `users` (`first_name`, `last_name`), `branches` (`id`, `organization_id`, `name`, `is_active`), and the reference sources: `service_requests`, `quotes`, `work_orders`, `visits`, `customers`, `invoices`, `payments`, `catalog_items`, `technician_profiles`, `user_invitations` (identifiers/numbers/names only).
- Schema amendment (approved by user 2026-10-15): `CREATE INDEX ix_audit_org_occurred ON audit_logs(organization_id, occurred_at DESC, id DESC);` added to the schema doc; migration generation authorized, application not. Existing indexes `ix_audit_entity` and `ix_audit_actor` are unchanged.
- No other table or column changes. No audit row is updated or deleted.

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership; every query is restricted to that `organization_id`.
- Client-provided organization identifiers: never accepted. Client `branchId`, `actorId`, `entityId` and `{id}` are validated against the session organization and the caller's scope (BR-03, BR-11).
- Event of another organization, outside branch scope, or non-financial for Accounting: `404`.
- Permissions: BR-09 roles; BR-10 financial-only for Accounting; BR-11 branch scope. Frontend visibility is UX only.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/audit-log` | Query `from`, `to`, `q`, `actorId`, `action`, `entityType`, `entityId`, `branchId`, `impact`, `sort`, `direction`, `page`, `pageSize` | `200 { items: [{ id, eventId, occurredAt, actor: { id \| null, name }, action: { code, label }, entity: { type, label, id \| null }, reference, summary, branch: { id, name } \| null, source, ipAddress \| null, impact }], total, page, pageSize, timeZone }` | `400` (`errors.<param>`), `401`, `403`, `404 branch_not_found` | BR-09–BR-11 |
| GET | `/audit-log/summary` | — | `200 { eventsToday, activeUsersToday, highImpactToday, accessChangesToday, timeZone }` | `401`, `403` | BR-09–BR-11 |
| GET | `/audit-log/filters` | — | `200 { actions: [{ code, label }], entityTypes: [{ code, label }], users: [{ id \| "system", name }], branches: [{ id, name, isActive }] }` | `401`, `403` | BR-09–BR-11 |
| GET | `/audit-log/{id}` | `id` (long) | `200` list item + `{ actor: { id, name, roleName }, changes: [{ field, label, before, after }], metadata: [{ key, value }], target: { kind, id } \| null, timeZone }` | `401`, `403`, `404` | BR-09–BR-11 |
| GET | `/audit-log/export` | List query without `page`, `pageSize` | `200 text/csv` (BR-14), then BR-16 row | `400`, `401`, `403`, `404 branch_not_found`, `409 export_too_large`, `500` | BR-09–BR-11 |
| PUT / PATCH / DELETE | `/audit-log`, `/audit-log/{id}` | — | — | `405` | — |
| GET (existing) | `/invoices/export`, `/invoices/payments/export`, `/billing-review/export`, `/business-overview/export`, `/catalog-items/export` | Unchanged | Unchanged, plus BR-16 row | Unchanged, plus `500` when the audit insert fails | Unchanged |
| DELETE (existing) | `/sessions/current` | Unchanged | Unchanged, plus BR-18 row | Unchanged | Unchanged |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Audit trail page | Metric and row skeletons; filters disabled until options load | "No audit events match these filters." with **Reset** | "We couldn't load audit events." with **Try again** (metrics and list fail independently) | `403` → existing in-page forbidden state; `401` → existing session-expired flow | Metrics, list, pagination |
| Drawer | Section skeletons | Change summary hidden when no changes | "We couldn't load this event." with **Try again**; `404` → "This event is no longer available." | — | BR-22 content |
| Export | Button busy and disabled | — | BR-21 messages | — | File downloaded |

- Mockups: `design/assets/29-design.png` (Approved). The mockup's old sidebar, breadcrumb and "Failed sign-ins" card are replaced by the current shell, no breadcrumb and "Access changes".
- Responsive and accessibility: below 1024px the filters wrap and the table scrolls horizontally inside its card (no page horizontal scroll at 320px); metric cards stack to 2 then 1 columns. Sortable headers expose `aria-sort`; impact and action tags carry text; the drawer is a labelled dialog with focus trap and focus return; copy confirmation is announced.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid filter/sort/paging | `400 errors.<param>`; invalid URL values never reach the API because the page falls back to defaults (BR-20) | None |
| Out-of-scope/foreign branch filter | `404 branch_not_found`; page shows the load error | None |
| Event invisible to caller | `404`; drawer "This event is no longer available." | None |
| Disallowed role | `403`; forbidden state | None |
| Export over 5,000 rows | `409 export_too_large`; BR-21 message | None |
| Export audit insert fails | `500`; "We couldn't export the audit log. Try again." | None (no file, no row) |
| Edit/delete attempt via API | `405` | None |
| Sign-out audit insert fails | Sign-out completes as today; failure logged without user/session data | Session cleared; no audit row |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner and events across days, actors, actions, entities, branches and impacts | `GET /audit-log` with defaults, then with every filter combined | Defaults return today's events (organization time zone) newest first, 25 per page; combined filters return only matching rows; `total` is correct |
| AC-02 | 60 visible events | Sort by `impact` and by `occurredAt asc`; request pages with sizes 25/50/100 and a page beyond the last | Order follows BR-05 including ties; page slices and `total` correct; beyond-last returns empty `items` with `total` |
| AC-03 | An Owner | Invalid `from`/`to`/span, `q` > 100, unknown `action`/`entityType`/`impact`/`sort`/`direction`, bad `actorId`, `entityId` without `entityType`, `page` 0, `pageSize` 30 (parameterized) | `400` with the BR-03/BR-05 error key; out-of-scope `branchId` → `404 branch_not_found` |
| AC-04 | Events today and yesterday, mixed impacts and `auth.*`/`user.*` codes | `GET /audit-log/summary` with list filters active on the page | Four BR-07 values count only today's visible events and ignore filters |
| AC-05 | Rows with a resolvable entity, a null actor, a customer-originated code, a null IP and an unknown action code | They are listed | Actor, action/entity labels, reference, summary, branch ("All branches" when null), source, IP and impact follow BR-01, BR-02, BR-06 and BR-15; the unknown code is humanized with Low impact |
| AC-06 | Rows whose before/after/metadata contain password, token, card number, IBAN, email and phone keys (nested) | Listed, opened and exported | Secrets show `[redacted]`, email/phone `[changed]`, `last4` kept, in summary, detail and CSV; no original value appears anywhere in the responses |
| AC-07 | The rows of AC-06 and named actors | Search by a redacted value, an email, an actor name, a reference and `AUD-<id>` | Redacted values and emails match nothing; name, reference and event id match per BR-04 |
| AC-08 | Members of each role | Call every audit-log endpoint (parameterized) | Owner and Operations Manager succeed; Dispatcher, Technician and Viewer get `403` |
| AC-09 | An Accounting member and financial and non-financial events | List, summary, filters, detail of a non-financial id, a non-financial `action` filter and export | Only financial events appear and are counted/exported; the non-financial id → `404`; the non-financial action filter → `400 errors.action` |
| AC-10 | A branch-limited Accounting member and events in its branch, another branch and with null branch | List, summary, filters, detail and export | Only the in-scope branch's events are visible; others and null-branch events are absent; their ids → `404`; the other branch filter → `404 branch_not_found` |
| AC-11 | Events in two organizations | An Owner of organization A lists, opens B's event id and exports | B's events never appear; B's id → `404` |
| AC-12 | An existing event | `PUT`, `PATCH`, `DELETE` on `/audit-log/{id}` and `/audit-log` | `405`; the row is unchanged; the UI shows no edit or delete control |
| AC-13 | An event with before/after/metadata and an Operations Manager, then an Accounting member for a work-order event they cannot open | `GET /audit-log/{id}` | Detail has actor role, `changes` rows, redacted metadata with technical keys and a `target`; `target` is null when the caller cannot open the module or entity per BR-25 |
| AC-14 | Visible events with commas, quotes, newlines and formula-leading text | `GET /audit-log/export` with filters and sort, and with more than 5,000 matches | CSV has BR-14 columns, filters/sort applied, escaping per `completed-jobs-review` BR-21, server filename; > 5,000 → `409 export_too_large` and no `export.generated` row |
| AC-15 | Each of the six exports (parameterized), one with a search term | Exported successfully, then with a `400` filter, and with the audit insert failing | Success writes exactly one `export.generated` per BR-16 (`reportType`, normalized filters, `hasSearch` without the text, `rowCount`); `400` writes none; insert failure → `500`, no file, no row |
| AC-16 | A signed-in member, then a request without a session | `DELETE /sessions/current` | One `auth.signed_out` row per BR-18 for the session; without a session nothing is written and the response is unchanged |
| AC-17 | The changed persistence | The schema doc and migrations are inspected | `ix_audit_org_occurred (organization_id, occurred_at DESC, id DESC)` is in the schema doc and in a generated migration that changes nothing else; it was not applied |
| AC-18 | The page | It loads, the list is empty, and a load fails | Skeletons, then BR-19 content; empty message with **Reset**; error with **Try again** that reloads only the failed part |
| AC-19 | Filters edited | Edit without applying, then **Apply filters**, Enter in search, **Reset**, reload with query params, invalid query values | Nothing reloads until Apply/Enter; Apply resets to page 1; Reset restores defaults; URL reflects and restores state; invalid values fall back to defaults |
| AC-20 | 120 events | Sort by Timestamp and Impact headers, change page and page size | Each action calls the server with the new `sort`/`direction`/`page`/`pageSize`; headers expose `aria-sort`; footer shows "Showing <a>–<b> of <total> events" |
| AC-21 | A row | **View**, then copy event ID, open the entity, **View related activity**, Escape | Drawer per BR-22; clipboard receives `AUD-<id>` with "Event ID copied"; Open link shown only with a target and navigates to it; related activity filters by entity with the widened date range; Escape closes and focuses **View** |
| AC-22 | Applied filters and unsaved edits | **Export CSV** succeeds, returns `409`, and fails | Request uses applied filters/sort; button busy/disabled during export; file downloaded; BR-21 messages on `409` and failure |
| AC-23 | Members of each role | The shell renders and each opens `/audit-log` directly | "Audit log" appears after "Reports" only for Owner, Operations Manager and Accounting; other roles see the forbidden state after the `403` |
| AC-24 | Events near midnight UTC and an organization in `America/Bogota` | The table, drawer, date filter and CSV are checked | Times shown in the organization zone per BR-24; "today" and date filters use local days; CSV has local and UTC timestamps and the zone |
| AC-25 | The implementation and `29-design.png` at 1440×900 with the drawer open, plus 1024, 640, 390 and 320 widths and a keyboard pass (USER VISUAL QA REPORT) | They are compared | Layout, metric cards, filters, table, tags, pagination and drawer match the mockup within the current shell; only the documented replacements differ; no page horizontal scroll; focus order, `aria-sort`, dialog focus trap and return work |

## Testing requirements

25 active ACs. Backend integration exceeds the default range because the feature changes authorization/branch isolation, sensitive-data redaction and audit writes in existing export and sign-out endpoints (distinct high-risk behaviors). No browser automation by agents.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | (1) Catalog resolution: impact/source/financial rules and unknown-code fallback (table-driven); (2) redaction of nested objects/arrays and summary text (table-driven). ≤ 2 methods | AC-05, AC-06 |
| Backend integration | (1) List filters/defaults/time zone; (2) sort/paging/validation (parameterized); (3) summary and filter options; (4) redaction + search across list/detail/CSV; (5) role matrix incl. `405` (parameterized); (6) Accounting financial-only + branch scope + cross-tenant; (7) detail + target; (8) audit-log CSV incl. limit; (9) `export.generated` across six exports (parameterized) incl. insert failure; (10) `auth.signed_out`. ≤ 10 methods | AC-01 to AC-16, AC-24 |
| Authorization/tenant isolation | Included above: one cross-tenant denial, one out-of-scope branch denial, one non-financial denial, role `403` table | AC-08 to AC-11 |
| Persistence review | `database-reviewer` on the index migration and schema doc | AC-17 |
| Frontend component/service | (1) Apply/Reset/URL state and request params; (2) sort/paging requests and states (loading/empty/error/forbidden); (3) drawer content, copy, target link, related activity, focus return; (4) export busy/409/error; (5) sidebar visibility by role. ≤ 5 methods | AC-18 to AC-23, AC-12 (UI) |
| Manual (USER VISUAL QA REPORT) | Desktop side-by-side, responsive widths and keyboard pass | AC-25, AC-24 (presentation) |

## Dependencies

- `specs/sign-in/spec.md` (AUDITED): session contract; unchanged (failed sign-ins remain unaudited).
- `specs/design-18-users-and-permissions/spec.md`: roles, branch scope and the BR-07 "Audit log" matrix row.
- `specs/notifications-center/spec.md`: time-zone, filter and target-mapping precedents.
- `specs/invoices-payments-management/spec.md` and `specs/completed-jobs-review/spec.md`: CSV precedent (escaping, 5,000-row limit).
- Existing export endpoints listed in API contracts; `DELETE /sessions/current`.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The catalog is code (not database rows); adding a future action code means adding a catalog entry. |
| AS-02 | References and actor names are resolved at read time and reflect current values, not values at event time. |
| AS-03 | Existing rows written without an IP keep a null IP; the UI falls back to the source label. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Failed sign-ins | Do not audit / audit known users | No | Not audited; metric replaced by "Access changes"; `auth.signed_out` added; sign-in spec unchanged (2026-10-15) |
| OD-02 | Access and export | All with access export / Owner-only export | No | Owner and Operations Manager all events; Accounting financial only (view and export); others `403` (2026-10-15) |
| OD-03 | Null-branch events for branch-limited members | Hidden / visible | No | Hidden; only all-branches members see them (2026-10-15) |
| OD-04 | Navigation | `/audit-log` own item / under Reports / under Administration | No | `/audit-log` with an independent sidebar item (2026-10-15) |
| OD-05 | Index | Add `ix_audit_org_occurred` / none | No | Add; generate migration, do not apply (2026-10-15) |
| OD-06 | Email/phone values | `[changed]` / shown | No | `[changed]`; never in summaries, search or CSV (2026-10-15) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02, AC-03, AC-05 |
| FR-02 | AC-04 |
| FR-03 | AC-09, AC-10 |
| FR-04 | AC-13 |
| FR-05 | AC-14 |
| FR-06 | AC-05 |
| FR-07 | AC-06, AC-07 |
| FR-08 | AC-08, AC-09, AC-10, AC-11 |
| FR-09 | AC-12 |
| FR-10 | AC-15 |
| FR-11 | AC-16 |
| FR-12 | AC-17 |
| FR-13 | AC-18, AC-19, AC-20, AC-22, AC-25 |
| FR-14 | AC-21 |
| FR-15 | AC-23 |
| FR-16 | AC-24 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-15 | — → DRAFT     | Created |
| 2026-10-15 | DRAFT → DRAFT | Validation fixes: High precedence over Medium patterns and per-row source resolution (BR-02); visit/invitation references (BR-06); exact CSV headers (BR-14); aligned invalid-filter error row with BR-20 |
| 2026-10-15 | DRAFT → APPROVED | Approved by user via /spec approve |
