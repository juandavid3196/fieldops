# Design 21 — Customer and property detail

| Field    | Value                       |
| -------- | --------------------------- |
| Feature  | `customer-property-detail`  |
| Type     | Full-stack                  |
| Status   | APPROVED                    |
| Created  | 2026-10-01                  |
| Updated  | 2026-10-01                  |
| Approved | 2026-10-01                  |

## Context and objective

Staff need one page per customer that shows who the customer is, where work is
done and what has happened with them. This feature delivers the Design 21
customer detail page at `/customers/:customerId`: header with identity, status,
contact and balance; tabs; multiple properties with a primary property, branch,
service instructions, archive and reactivation; contact, summary, billing,
tags, pinned and append-only internal notes; recent work, upcoming
appointments and an audit-based activity history from real data. Modules that
do not exist yet (requests, quotes, jobs, invoices, schedule) are reachable only
as empty or Coming soon destinations. Every read and write stays confined to the
caller's organization and branch scope.

## Actors and permissions

Same role policy and branch scope as `customer-management` BR-01/BR-02 (OD-15).

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View the detail page; edit, archive and reactivate the customer; add, edit, set primary, archive and reactivate properties; add notes; all branches | — |
| Dispatcher (`dispatcher`) | Same as Owner within assigned branches (all when `is_all_branches`) | Customers outside branch scope (`404`); set a property branch outside scope (`400`) |
| Operations Manager (`operations_manager`) | View the full detail page read-only; all branches | Any mutation (`403`) |
| Accounting (`accounting`) | View read-only within assigned branches (all when `is_all_branches`) | Any mutation (`403`); customers outside branch scope (`404`) |
| Viewer (`viewer`) | View read-only; all branches | Any mutation (`403`) |
| Technician (`technician`) | — | Every endpoint in this spec (`403`); forbidden state |

## Scope

- Route `/customers/:customerId` inside the shell, with breadcrumb, header,
  header actions and the tabs Overview, Requests, Quotes, Jobs, Invoices and
  Activity.
- Header: avatar initials, display name, type and status tags, primary contact
  email, phone and preferred contact, outstanding balance; **Edit customer**,
  **Create** menu, and a more-actions menu with Archive/Reactivate customer.
- Overview: Properties, Recent work, Upcoming appointments; side cards Contact
  details, Customer summary, Billing, Internal notes and Tags.
- Multiple properties per customer: list, view, add, edit, set as primary,
  archive and reactivate, each with its own branch and service instructions.
- Primary property schema support, backfill and migration generation (never
  application), per OD-02 and OD-16.
- Editing the customer reuses the existing Customers drawer and endpoints; its
  address section now targets the primary property (OD-06).
- Pinned internal note (`customers.notes`) and append-only notes
  (`customer_notes`).
- Activity tab from `audit_logs`.
- Customers list navigation changes (OD-14).
- Audit rows for the new mutations.
- Loading, empty, error, not-found, forbidden, read-only, submitting and
  responsive states.

## Non-goals

- Default payment terms (OD-01); any billing configuration.
- Portal status, portal invitation and the customer portal (OD-04).
- **Send message** and any email/SMS/messaging (OD-04).
- Requests, quotes, jobs/work orders, visits scheduling and invoices: no
  creation, editing, list tabs or detail pages; only read-only derived values on
  Overview and Coming soon destinations (OD-09).
- Additional (non-primary) contacts.
- Editing or deleting `customer_notes` entries.
- Physical deletion of customers or properties.
- Property access instructions, latitude/longitude and geocoding.
- The mockup's top-bar branch selector, "Updated just now" indicator and
  notification badge, and any other shell change (as `customer-management`
  OD-17).
- Row "⋯" menus in Recent work (OD-12).
- Mockup sample data.
- Applying migrations.

## User flow

1. A permitted user selects a customer name in the Customers list (or opens
   `/customers/:customerId`); the page loads on the Overview tab.
2. The user reviews header, properties with their history, recent work,
   upcoming appointments and side cards.
3. An Owner or Dispatcher selects **Edit customer** (or a Contact details,
   Internal notes or Tags pencil); the existing drawer opens; saving refreshes
   the page.
4. They select **Add property** (or **Create → Property**), complete the
   property drawer and save; the new property appears in the list.
5. They use a property's **Edit**, **View property** or its actions menu
   (**Set as primary**, **Archive**, **Reactivate**).
6. They type an internal note and select **Add note**; it appears at the top
   of the notes list.
7. They select **Archive customer** in the more-actions menu and confirm, or
   **Reactivate customer**.
8. The user opens **Activity** to review the customer's history, or a
   transactional tab to see its Coming soon panel.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/customers/:customerId` inside the shell, with breadcrumb, header, actions and tabs per BR-01/BR-02, keeping the sidebar **Customers** item active. |
| FR-02 | `GET /customers/{id}/detail` must return the header and side-card data (identity, primary contact, display status, balance, summary, billing, tags, pinned note) per BR-03, BR-11, BR-12 and BR-23. |
| FR-03 | **Edit customer** and the Contact details, Internal notes and Tags pencils must open the existing customer drawer; `GET/PUT /customers/{id}` must read and write the primary property's address and service instructions without changing any property branch (BR-08); archive/reactivate must reuse the existing endpoints from the header menu. |
| FR-04 | `GET /customers/{id}/properties` must return active and archived properties with per-property history, and the page must render them per BR-04 and BR-13. |
| FR-05 | `POST` and `PUT /customers/{id}/properties[/{propertyId}]` and `GET /customers/{id}/properties/{propertyId}` must create, edit and read properties validated per BR-05 and BR-06. |
| FR-06 | `POST /customers/{id}/properties/{propertyId}/set-primary` must make an active property the only primary property atomically per BR-07. |
| FR-07 | `POST .../archive` and `.../reactivate` must toggle `properties.is_active` per BR-07, never archiving the primary property. |
| FR-08 | The schema must support exactly one primary property per customer per BR-09; customer creation and CSV import must create their first property as primary, and existing data must be backfilled. |
| FR-09 | The Customer summary and Billing cards must show the values of BR-11 and BR-12. |
| FR-10 | `GET /customers/{id}/recent-work` must return up to 5 recent requests, quotes and jobs per BR-14, rendered in the Recent work table. |
| FR-11 | `GET /customers/{id}/upcoming-appointments` must return up to 5 upcoming visits per BR-15, rendered in Upcoming appointments. |
| FR-12 | `GET` and `POST /customers/{id}/notes` must list and append internal notes per BR-16, and the Internal notes card must show the pinned note and the list. |
| FR-13 | `GET /customers/{id}/activity` must return the customer's audit history per BR-17, rendered in the Activity tab with pagination. |
| FR-14 | Requests, Quotes, Jobs and Invoices tabs, the **Create** menu module items and future-module links must lead to Coming soon panels or routes per BR-18. |
| FR-15 | In the Customers list, the customer name and the duplicate panel's **View existing customer** must navigate to the detail page; row **Edit** keeps opening the drawer (BR-19). |
| FR-16 | Every endpoint must resolve the organization from the session and apply role and branch scope per BR-20. |
| FR-17 | Property mutations and note creation must write audit rows per BR-21. |
| FR-18 | The page must render loading, empty, error, not-found, forbidden, read-only, submitting and responsive states per BR-22 and the UI table. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Page structure: breadcrumb "Customers / <display name>" (Customers links to `/customers`). Tabs are a tablist; the active tab is reflected in the query parameter `tab` (`overview` default, `requests`, `quotes`, `jobs`, `invoices`, `activity`); an unknown value shows Overview. Reloading keeps the tab. | Frontend |
| BR-02 | Role UX: Owner and Dispatcher see **Edit customer**, **Create**, the more-actions menu (**Archive customer** when active, **Reactivate customer** when archived), **Add property**, property **Edit** and actions menu, the card pencils, the note input and **Schedule a job**. Operations Manager, Accounting and Viewer see the same data with none of those controls (property **View property** remains). Technician and unknown roles see the forbidden state "You don't have access to customers." with no further data requests. Frontend visibility is UX only. | Frontend |
| BR-03 | Header: avatar initials (residential: first letters of primary contact first and last name; commercial: first letters of the first two words of the display name, one letter for a single word; upper-case); display name; type tag Residential/Commercial; status tag = display status per `customer-management` BR-04 (Active, Lead, Overdue, Archived; text label, not color only); primary contact email (mailto link), phone formatted as in `customer-management` BR-10 (tel link; omitted when null) and preferred contact "Email", "SMS" or "Email & SMS" with the caption "Preferred contact"; "Outstanding balance" = `customer-management` BR-04 balance. Money on this page uses the organization currency with two decimals. | Both |
| BR-04 | Properties card: title "Properties (<n>)" where n = active properties. Active properties order: primary first, then name A–Z (case-insensitive), then id. Each row shows name, "Primary" tag on the primary, one-line address "<line1>[, <line2>], <city>[, <state>] [<zip>]", Branch name ("—" when null), Service instructions ("None" when empty), **View property**, **Edit** and an actions menu labelled "Actions for <name>" with **Set as primary** (non-primary only) and **Archive** (non-primary only). Below the active list, a collapsed "Archived properties (<n>)" toggle (hidden when 0) lists archived properties with an "Archived" tag, **View property** and a menu with **Reactivate**. | Frontend |
| BR-05 | Property fields (trimmed; empty optional strings stored as null): see Property field validation below. `country_code` = `organizations.country_code` (`US` when null). Server errors are `400` `errors.<field>` with the same messages. | Both |
| BR-06 | Property branch: required; on create, and on edit when the value changes, it must be an active branch of the organization in the caller's branch scope (`400` `errors.branchId` "Choose a branch you have access to."). On edit, an unchanged branch is accepted even when outside the caller's scope or inactive. The property drawer lists `GET /customers/branch-options`; on edit, a current branch not in that list is shown as the selected option with its name. New property default: the customer's branch when it is in the options, else empty. A property's branch is independent of the customer's branch (OD-07). | Both |
| BR-07 | Property state rules: a new property is not primary unless the customer has no active primary property, in which case it becomes primary. **Set as primary** on an active non-primary property clears the previous primary and sets the new one in one transaction; on the current primary → `409` "This property is already the primary property."; on an archived property → `409` "Reactivate this property before making it primary."; a concurrent primary change detected by the unique constraint → `409` "The primary property was changed by someone else. Refresh and try again.". **Archive** (confirmation "Archive <name>?" / "Archived properties are hidden from this customer's active properties. Their history is kept." **Archive** / **Cancel**) on the primary → `409` "Set another property as primary before archiving this one."; on an archived property → `409` "This property is already archived.". **Reactivate** (no confirmation) makes it active and non-primary; on an active property → `409` "This property is already active.". **Edit** of an archived property → `409` "Reactivate this property to edit it.". Each mutation updates `updated_at`. No endpoint deletes properties. Property mutations are allowed on archived customers. | Backend / Both |
| BR-08 | Customer drawer changes (supersede `customer-management` BR-11 for edit): `GET /customers/{id}` returns the primary property's address and service instructions in `property` and `serviceInstructions`; `PUT /customers/{id}` updates only those fields of the primary property and never changes any property's `branch_id` or `name`. When the customer has no active primary property, `GET` returns empty address fields and `PUT` creates a primary property named "Primary property" with the customer's branch (`property.created` audit row). Creation (`POST /customers`, CSV import) is unchanged except that its first property is created with `is_primary = true`. The pencils open the same drawer: Contact details and Tags open it in edit mode; Internal notes opens it in edit mode with focus on the Internal note field. | Backend / Frontend |
| BR-09 | Primary property invariant: at most one property per customer has `is_primary = true` (unique partial index) and a primary property is always active (check constraint). | Backend / Database |
| BR-10 | Customer archive/reactivate from the header uses the existing endpoints, dialog and messages (`customer-management` BR-16); after success the page stays open, header status and menu update, and toast "Customer archived." / "Customer reactivated." shows. Archived customers remain fully viewable and editable. | Frontend |
| BR-11 | Customer summary: **Total jobs** = count of the customer's `work_orders` with status other than `cancelled`; **Lifetime value** = sum of `invoices.amount_paid` for the customer's invoices with status other than `void`, with caption "Since <MMM yyyy>" from `customers.created_at` in the organization timezone; **Last service** = the date ("MMM d, yyyy") of the latest visit with status `completed` or `approved` across the customer's work orders, using `actual_completed_at` (fallback `scheduled_end`), or "No completed service". | Backend / Frontend |
| BR-12 | Billing card: **Outstanding balance** = header balance; **Last invoice** = the customer's most recent invoice with status not `draft` and not `void`, ordered by `issue_date` desc then `created_at` desc, shown as "<invoice_prefix>-<invoice_number>" (link to `/coming-soon/invoices`) with a status tag (Sent, Partially paid, Paid, Overdue), or "None"; **View invoices** → `/coming-soon/invoices`. No payment terms row. | Backend / Frontend |
| BR-13 | Property history (per property): **Last service** = date and first line of `work_orders.scope_snapshot` of the latest `completed`/`approved` visit of work orders with that `property_id` (date rule as BR-11), or "None"; **Next appointment** = earliest visit of those work orders with `scheduled_start` ≥ now and status `scheduled`, `assigned` or `on_the_way`, shown "MMM d, yyyy · h:mm a", or "None". | Backend / Frontend |
| BR-14 | Recent work: the customer's service requests, quotes and work orders (all statuses), merged, ordered by Date desc then id, at most 5. Columns: Type (Request / Quote / Job tag), ID, Title, Status (enum value as sentence case, e.g. "Approved for billing"), Date ("MMM d, yyyy"), Technician, Amount, **View**. Request: ID "REQ-<request_number>", Title = first line of `description`, Date = `created_at`, Technician "—", Amount "—". Quote: ID "<quote_prefix>-<quote_number>", Title = first line of the current version `scope` (version `current_version_no`; request description when no version), Date = current version `sent_at` else `quotes.created_at`, Technician "—", Amount = current version `total` or "—". Job: ID "<work_order_prefix>-<work_order_number>", Title = first line of `scope_snapshot`, Date = completion date of its latest `completed`/`approved` visit (rule as BR-11) else `created_at`, Technician = name of the active primary assignment (`unassigned_at` null) of its latest visit (by `scheduled_start` desc nulls last, then `visit_number` desc) or "—", Amount = total of its source quote version. ID links and **View** go to `/coming-soon/requests`, `/coming-soon/quotes` or `/coming-soon/work-orders`; **View all** → `/coming-soon/work-orders`. Empty: "No work yet" / "Requests, quotes and jobs for this customer will appear here.". | Backend / Frontend |
| BR-15 | Upcoming appointments: the customer's visits with `scheduled_start` ≥ now and status `scheduled`, `assigned` or `on_the_way`, ascending by `scheduled_start` then id, at most 5; each shows date and time range ("MMM d, yyyy · h:mm a – h:mm a", end omitted when null), job ID and title, property name and technician (rule as BR-14) or "Unassigned". **View all** → `/coming-soon/schedule`. Empty: "No upcoming appointments" / "Schedule a job for this customer." with **Schedule a job** → `/coming-soon/schedule` (mutation roles only). | Backend / Frontend |
| BR-16 | Internal notes card: header "Internal notes" with the caption "Not visible to customer". Pinned note = `customers.notes` ("No pinned note" when null), edited only through the drawer. Below, `customer_notes` entries newest first (`created_at` desc, id), 10 per page; each shows the text, author name (`users.first_name last_name`) and date-time in the organization timezone; **Show more** loads the next 10 while more exist; "No notes yet" when none. Mutation roles see the input "Add a note…" and **Add note**: text trimmed, required ("Enter a note."), ≤2,000 chars ("Use 2,000 characters or fewer."); `POST` returns the created entry, which is prepended; the input clears; toast "Note added.". Entries cannot be edited or deleted. Note text never appears in audit rows or logs. | Both |
| BR-17 | Activity: audit rows of the caller's organization with (`entity_type` `customer`, `entity_id` = customer), (`property`, any of its properties) or (`customer_note`, any of its notes), restricted to the actions below; newest first (`occurred_at` desc, id), 20 per page with a paginator; each entry shows the label, actor name ("System" when no actor) and date-time in the organization timezone. Labels: `customer.created` "Customer created"; `customer.updated` "Customer details updated" (generic for every change, including type, tags, contact, primary property address or pinned note; changed field names are not shown); `customer.archived` "Customer archived"; `customer.reactivated` "Customer reactivated"; `property.created` "Property added: <name>"; `property.updated` "Property updated: <name>"; `property.primary_changed` "Primary property changed to <name>"; `property.archived` "Property archived: <name>"; `property.reactivated` "Property reactivated: <name>"; `customer_note.created` "Internal note added". `<name>` is the property's current name. The response never contains `before_data`, `after_data`, metadata values, IP addresses, note text, emails, phones or addresses. Empty: "No activity yet". | Backend / Frontend |
| BR-18 | Future modules: the Requests, Quotes, Jobs and Invoices tabs show a panel "<Module> are coming soon" / "You'll see this customer's <module> here." with a link to `/coming-soon/requests`, `/coming-soon/quotes`, `/coming-soon/work-orders` or `/coming-soon/invoices`; they make no data request. **Create** menu: **Property** (opens the Add property drawer), **Request**, **Quote**, **Job**, **Invoice** (navigate to the matching Coming soon route). | Frontend |
| BR-19 | Customers list (amends `customer-management` BR-09 and BR-15): the Name link navigates to `/customers/:id`; row menu **Edit** / **View** keeps opening the drawer; **View existing customer** keeps the discard confirmation when the drawer is dirty, then navigates to `/customers/:id`. | Frontend |
| BR-20 | Authorization: read endpoints of this spec allow `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`; property mutations and `POST /customers/{id}/notes` allow `owner`, `dispatcher`; `technician` and others → `403`. The customer must belong to the caller's organization and be within branch scope (`customer-management` BR-02) for every endpoint, else `404`. A `propertyId` that does not belong to that customer → `404`. Derived data (recent work, appointments, summary, billing, property history) includes all records of a visible customer regardless of the records' branches, consistent with the existing balance rule. | Backend |
| BR-21 | Audit (`actor_user_id` caller): `property.created` (`entity_type` `property`, `branch_id` property branch, after `{ branchId, isPrimary }`); `property.updated` (metadata `{ changedFields }` names only; no-op → no row); `property.primary_changed` (entity = new primary, before `{ primaryPropertyId }` previous or null, after `{ primaryPropertyId }`); `property.archived` / `property.reactivated` (before/after `{ isActive }`); `customer_note.created` (`entity_type` `customer_note`, `branch_id` customer branch, no note text). Addresses, instructions and note text are never written to audit rows or logs. | Backend |
| BR-22 | Unsaved changes and submission: closing a dirty property drawer asks "Discard unsaved changes?" (**Discard** / **Keep editing**); while saving, buttons show loading, fields are disabled and double submission is prevented; **Add note** is disabled while pending. | Frontend |
| BR-23 | Side cards: **Contact details** shows the primary contact email (mailto link), phone formatted as BR-03 (tel link) or "No phone", and the preferred contact label with the caption "Preferred contact"; **Tags** shows the customer's tags as chips ordered by name, or "No tags". Both pencils follow BR-08 and are hidden for read roles. | Frontend |

### Property field validation (BR-05)

| Field | Rule | Message |
| ----- | ---- | ------- |
| Property name | Required, 1–140 chars | "Enter a property name." / "Use 140 characters or fewer." |
| Address line 1 | Required, 1–180 chars | "Enter an address." / "Use 180 characters or fewer." |
| Address line 2 | Optional, ≤180 chars | "Use 180 characters or fewer." |
| City | Required, 1–100 chars | "Enter a city." / "Use 100 characters or fewer." |
| State | Optional; as `customer-management` BR-10 (US dropdown / free text ≤100) | "Choose a valid state." |
| ZIP code | Optional; as `customer-management` BR-10 | "Enter a valid ZIP code." |
| Branch | BR-06 | "Choose a branch." / BR-06 |
| Service instructions | Optional, ≤2,000 chars | "Use 2,000 characters or fewer." |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | Active, primary | Customer create/import, or Add property when no active primary exists, or `PUT /customers/{id}` with no primary | Owner, Dispatcher (import: Owner) | Valid data |
| — | Active, not primary | Add property | Owner, Dispatcher | An active primary exists |
| Active, not primary | Active, primary | Set as primary (previous primary → Active, not primary) | Owner, Dispatcher | BR-07 |
| Active, not primary | Archived | Archive | Owner, Dispatcher | Not primary |
| Archived | Active, not primary | Reactivate | Owner, Dispatcher | Archived |

Customer lifecycle transitions are unchanged (`customer-management`).

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `customers`, `customer_contacts`, `customer_tags`, `customer_tag_assignments`
  (as `customer-management`); `properties` (`id`, `organization_id`,
  `customer_id`, `branch_id`, `name`, `address_line1`, `address_line2`, `city`,
  `state_region`, `postal_code`, `country_code`, `service_notes`, `is_active`,
  timestamps, new `is_primary`); `customer_notes` (all columns; insert/read);
  read-only: `organizations` (`timezone`, `currency`, `country_code`,
  `quote_prefix`, `work_order_prefix`, `invoice_prefix`), `branches`, `users`
  (names), `organization_users`, `organization_user_branches`,
  `service_requests`, `quotes`, `quote_versions`, `work_orders`, `visits`,
  `visit_assignments`, `technician_profiles`, `invoices`; `audit_logs`
  (insert; read for Activity).
- Not used: `access_instructions`, latitude/longitude, `portal_user_id`,
  `legal_name`, `tax_id`, `billing_address`, payments.
- Schema amendments (authorized, OD-16; the schema doc is amended during
  implementation and a migration generated with `--generate-migration`, never
  applied):
  1. `properties.is_primary boolean NOT NULL DEFAULT false`; backfill: for each
     customer, its oldest active property (`created_at`, then `id`) gets
     `is_primary = true`; then `CONSTRAINT ck_properties_primary_active CHECK
     (NOT is_primary OR is_active)` and `CREATE UNIQUE INDEX
     ux_properties_customer_primary ON properties(customer_id) WHERE
     is_primary`.
  2. `properties` composite FK `FOREIGN KEY(organization_id, branch_id)
     REFERENCES branches(organization_id, id)` (double-FK pattern; property
     branch becomes user-editable per OD-07). Existing rows already satisfy it.
  3. `CREATE INDEX ix_customer_notes_customer ON customer_notes(organization_id,
     customer_id, created_at DESC)`.
- Transactions: set-primary (clear + set + audit) is one transaction; each
  property create/edit/archive/reactivate with its audit row is one
  transaction; note creation with its audit row is one transaction;
  `PUT /customers/{id}` remains one transaction including a created primary
  property (BR-08).
- Persistence changes require `database-reviewer`.

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session
  (active `organization_users` membership), as in existing features.
- Client-provided organization identifiers: never accepted. `customerId`,
  `propertyId` and `branchId` are always verified against the caller's
  organization, branch scope and (for properties) the customer.
- Customer of another organization or outside branch scope on any endpoint:
  `404`, no data change. Property of another customer or organization: `404`.
  Branch of another organization, inactive or out of scope (when set or
  changed): `400` `errors.branchId`.
- Activity, notes, recent work and appointments read only rows of the caller's
  organization linked to the visible customer.
- Permissions per action: BR-20.

## API contracts

Contract status: Final. All endpoints require an authenticated session, the
existing CSRF protection on unsafe methods, and return `401` when
unauthenticated. Validation errors use the existing `400` problem shape with
`errors.<field>`; `409` uses the existing problem shape with the BR-07 message.
Instants are UTC ISO-8601; `timezone` is `organizations.timezone`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/customers/{id}/detail` | — | `200 { id, type, displayName, contact: { firstName, lastName, email, phone?, prefersEmail, prefersSms }, lifecycle, displayStatus, isActive, outstandingBalance, currency, timezone, summary: { totalJobs, lifetimeValue, customerSince, lastServiceAt? }, lastInvoice: { number, status, issueDate? } \| null, tags: [{ id, name }], pinnedNote? }` | `403`, `404` | Read |
| GET | `/customers/{id}/properties` | — | `200 { items: [Property], timezone }` (active and archived; BR-04 order) where Property = `{ id, name, isPrimary, isActive, addressLine1, addressLine2?, city, stateRegion?, postalCode?, branch: { id, name } \| null, serviceInstructions?, lastService: { completedAt, summary } \| null, nextAppointment: { startsAt } \| null }` | `403`, `404` | Read |
| GET | `/customers/{id}/properties/{propertyId}` | — | `200 Property` | `403`, `404` | Read |
| POST | `/customers/{id}/properties` | `{ name, addressLine1, addressLine2?, city, stateRegion?, postalCode?, branchId, serviceInstructions? }` | `201 Property` + `Location` | `400`, `403`, `404` | Mutate |
| PUT | `/customers/{id}/properties/{propertyId}` | Same as POST | `200 Property` | `400`, `403`, `404`, `409` | Mutate |
| POST | `/customers/{id}/properties/{propertyId}/set-primary` | — | `204` | `403`, `404`, `409` | Mutate |
| POST | `/customers/{id}/properties/{propertyId}/archive` | — | `204` | `403`, `404`, `409` | Mutate |
| POST | `/customers/{id}/properties/{propertyId}/reactivate` | — | `204` | `403`, `404`, `409` | Mutate |
| GET | `/customers/{id}/recent-work` | — | `200 { items: [{ type: "request"\|"quote"\|"job", id, number, title, status, date, technicianName?, amount? }], currency, timezone }` | `403`, `404` | Read |
| GET | `/customers/{id}/upcoming-appointments` | — | `200 { items: [{ visitId, startsAt, endsAt?, jobNumber, jobTitle, propertyName, technicianName? }], timezone }` | `403`, `404` | Read |
| GET | `/customers/{id}/notes` | Query `page?` (≥1, default 1) | `200 { items: [{ id, note, authorName, createdAt }], totalCount, page, pageSize: 10, timezone }` | `400` page, `403`, `404` | Read |
| POST | `/customers/{id}/notes` | `{ note }` | `201 { id, note, authorName, createdAt }` | `400` note, `403`, `404` | Mutate |
| GET | `/customers/{id}/activity` | Query `page?` (≥1, default 1) | `200 { items: [{ id, action, subjectName?, actorName?, occurredAt }], totalCount, page, pageSize: 20, timezone }` (BR-17 whitelist only) | `400` page, `403`, `404` | Read |
| GET | `/customers/{id}` | Existing | Existing shape; `property` and `serviceInstructions` from the primary property (BR-08) | Existing | Existing |
| PUT | `/customers/{id}` | Existing | Existing; updates the primary property per BR-08 | Existing | Existing |

A page beyond the last returns empty `items` with the correct `totalCount`.
A `customerId` or `propertyId` that is not a valid UUID returns `404`, the same as an unknown id.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Detail page | Header and card skeletons; each Overview region (properties, recent work, appointments, notes) has its own skeleton | Per region: "No properties yet" / "Add a property to schedule work at it." (with **Add property** for mutation roles); BR-14, BR-15, BR-16 empties | `404` or malformed id: "Customer not found" / "This customer doesn't exist or you don't have access to it." with **Back to customers**; detail failure: "We couldn't load this customer." with **Retry**; region failure: "We couldn't load <properties / recent work / appointments / notes>." with **Retry** | Technician/unknown: forbidden state; read roles: no actions (BR-02) | Data renders |
| Activity tab | List skeleton | "No activity yet" | "We couldn't load activity." with **Retry** | Read roles | Entries and paginator |
| Property drawer | Edit/view: form skeleton until the property loads | — | Load failure with **Retry**; `404`: toast "This property is no longer available." and drawer closes; save: field errors, `409` message toast, or "We couldn't save the property. Try again." (data kept) | View mode for read roles (title "Property details", fields read-only, history shown, footer **Close**) | Toast "Property added." / "Property updated."; drawer closes; properties and Activity refresh |
| Property actions | Menu action disabled while pending | — | `409` message toast and list refresh; other failure "We couldn't update the property. Try again." | Hidden for read roles | Toast "Primary property updated." / "Property archived." / "Property reactivated."; list refreshes |
| Customer drawer (from detail) | Existing | — | Existing | Existing | Existing toast; header, side cards and properties refresh |
| Notes | **Show more** loading | BR-16 | Add failure: field error or toast "We couldn't add the note. Try again." (text kept) | Input hidden for read roles | BR-16 |

- Mockups: `design/assets/21-design.png` (Approved; the image is the visual
  authority). Deviations decided by the user: no Default payment terms row
  (OD-01), no Portal status or **Send message** (OD-04), pencils open the
  drawer (OD-05), property actions menu and archived properties section added
  (OD-02, OD-08), transactional tabs show Coming soon (OD-09), no Recent work
  row menus (OD-12), shell elements unchanged.
- Property drawer: titles "Add property" / "Edit property" / "Property
  details"; fields in Property field validation order; footer **Cancel** and
  **Add property** / **Save changes**.
- Responsive: ≥1280 px as mockup (main column plus right side column);
  768–1279 px single column with the side cards after the Overview main
  content in mockup order, header contact items wrap; <768 px header actions
  stack full-width (more-actions stays an icon button), tabs scroll
  horizontally, property rows stack their fields, the Recent work table
  scrolls inside its card (no page-level horizontal scroll), drawers
  full-width, 16 px side gutter.
- Accessibility: tabs are a tablist; icon-only buttons have accessible names
  ("More actions", "Edit contact details", "Edit internal notes", "Edit
  tags", "Actions for <property name>"); statuses use text labels; the note
  input has a label; drawers trap focus and return it to the trigger; field
  errors are linked to inputs.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Customer of another org, out of scope, unknown or malformed id | `404`; not-found state | None |
| Property of another customer/org | `404`; toast "This property is no longer available." | None |
| Invalid property field or note | `400` `errors.<field>`; messages under fields | None |
| Property branch out of scope/foreign/inactive (set or changed) | `400` `errors.branchId` | None |
| Archive primary, set primary on primary/archived, edit archived, repeat archive/reactivate, concurrent primary change | `409` with BR-07 message; list refreshes | None |
| Mutation by read role / any call by Technician | `403`; UI never offers it; forbidden state for Technician | None |
| Failure inside set-primary | `500` generic toast | Transaction rolled back; previous primary kept |
| Region load failure | Region error with **Retry**; other regions unaffected | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner and an existing customer | They open `/customers/:customerId` | The page renders in the shell with breadcrumb, header, actions and tabs; sidebar Customers active; `?tab=activity` reload keeps Activity, unknown `tab` shows Overview |
| AC-02 | Residential and commercial customers with varied contacts (with and without phone), preferences, tags (including none), invoices and statuses | The header and side cards render | Initials, name, type tag, display-status tag, email/phone links, preferred contact label and outstanding balance follow BR-03; Contact details ("No phone") and Tags ("No tags") follow BR-23 |
| AC-03 | Owner, Dispatcher, a read role and a Technician | They open the page | Controls follow BR-02: mutation roles see all actions; read roles see data with no mutation controls; Technician sees the forbidden state and no data requests are made |
| AC-04 | A customer with several active and archived properties and work at them | Overview loads | Properties card follows BR-04 (count, order, Primary tag, address, branch, instructions, archived toggle) and each property shows Last service and Next appointment per BR-13 |
| AC-05 | An Owner/Dispatcher with valid property data | They add a property | It is created active and not primary (or primary when the customer had no active primary), with org country, chosen branch and instructions; `property.created` audit row; `201`; toast; list and count refresh |
| AC-06 | Invalid values per Property field validation (table-driven) | The user submits, and the API is called directly | Client shows field messages and does not submit; server returns `400` with the same `errors.<field>` and persists nothing |
| AC-07 | A Dispatcher scoped to branch X and a property currently in branch Y | They create or edit properties choosing branches | Branch Y, a foreign branch or an inactive branch is rejected with `400 errors.branchId` when set or changed; saving the property with its unchanged branch Y succeeds; the drawer shows Y as the selected option |
| AC-08 | An active property | An Owner/Dispatcher edits and saves it | Fields update; `property.updated` lists changed field names only; a no-op save writes no audit row; editing an archived property returns `409` |
| AC-09 | A customer with primary property A and active property B | The user selects **Set as primary** on B | B becomes the only primary and A stays active non-primary in one transaction; `property.primary_changed` audit row; list reorders; repeating on B returns `409`; the customer drawer now shows B's address |
| AC-10 | Primary property A, active B and archived C | The user archives B (after confirmation), reactivates C, and tries to archive A | B is archived and moves to the archived section; C becomes active non-primary; archiving A returns `409` "Set another property as primary before archiving this one."; repeats return `409`; audit rows written; nothing is deleted |
| AC-11 | A test database migrated with the generated migration, holding customers with one or more properties created before it, and new customers created via drawer or CSV import | The data is inspected and invalid writes are attempted | Each pre-existing customer's oldest active property is primary and no other is; new customers' first property is primary; a second primary for one customer, an archived primary and a property branch of another organization are rejected by the database |
| AC-12 | A customer whose primary property is in branch Y and customer branch X | An Owner edits the customer in the drawer, changing address, instructions and customer branch to Z | Only the primary property's address and instructions change; no property's branch or name changes; customer branch becomes Z; a customer without an active primary gets a new primary property on save |
| AC-13 | The detail page of an active customer | A mutation role uses **Edit customer**, each pencil and the more-actions menu | The existing drawer opens (Internal notes pencil focuses the Internal note field); saving refreshes header, side cards and properties; archive (with confirmation) and reactivate update status and menu without leaving the page |
| AC-14 | Seeded work orders (including cancelled), visits and invoices (including draft and void) | Overview loads | Total jobs, Lifetime value with "Since <MMM yyyy>", Last service, Outstanding balance and Last invoice (number, status, link) follow BR-11/BR-12; "No completed service" and "None" when no data; no payment terms row |
| AC-15 | Seeded requests, quotes and jobs with technicians and versions | Overview loads | Recent work shows at most 5 rows ordered and formatted per BR-14 with org prefixes, "REQ-" requests, technician and amounts; links go to the matching Coming soon routes; empty state when none |
| AC-16 | Visits in the past, future and in excluded statuses | Overview loads | Upcoming appointments shows at most 5 future `scheduled`/`assigned`/`on_the_way` visits ascending per BR-15 with org-timezone times; empty state with **Schedule a job** (mutation roles only) when none |
| AC-17 | A customer with a pinned note and 12 notes | A mutation role views notes, selects **Show more** and adds a note (also an empty and a 2,001-char note) | Pinned note shows; 10 newest then 2 more with author and date; a valid note is prepended, input cleared, toast shown and `customer_note.created` written without text; invalid notes return `400` with BR-16 messages; read roles see no input; no edit/delete exists |
| AC-18 | A customer with 25 whitelisted audit rows (customer, property, note) plus other rows (`customer.imported`, other customers, other orgs) | The user opens Activity and pages | 20 then 5 entries newest first with BR-17 labels, current property names, actor names and org-timezone dates; non-whitelisted and foreign rows never appear; the response contains no before/after data, metadata values, note text or contact data |
| AC-19 | The detail page | The user opens Requests, Quotes, Jobs and Invoices tabs and the **Create** menu items | Tabs show the BR-18 Coming soon panel without data requests and link to the matching route; **Create → Property** opens Add property; other items navigate to Coming soon |
| AC-20 | The Customers list and its drawer with a duplicate match | The user selects a customer name, row **Edit**, and **View existing customer** with a dirty form | Name navigates to `/customers/:id`; **Edit** opens the drawer; **View existing customer** asks the discard confirmation, then navigates to the detail page |
| AC-21 | Organizations A and B | A user of A calls every endpoint of this spec with B's customer id, or with A's customer and B's or another A customer's property id | `404` for customers and properties; no data of B appears in detail, properties, recent work, appointments, notes or activity; nothing changes |
| AC-22 | A Dispatcher and an Accounting user scoped to branch X, customers in X and Y | They call the detail endpoints | Customer in Y → `404` on every endpoint; customer in X → `200` including derived data of records in other branches |
| AC-23 | Each role | They call read and mutation endpoints of this spec | Responses follow BR-20: read roles `200` on reads and `403` on property mutations and note creation; Technician `403` everywhere |
| AC-24 | Slow, failing, `404` and empty responses, and a dirty property drawer | The page, regions, drawer and actions load or submit | Skeletons, per-region errors with **Retry** not affecting other regions, not-found state, empty states, submitting/disabled states, toasts and discard confirmation follow the UI table and BR-22 |
| AC-25 | Viewports ≥1280, 768–1279 and <768 px | The page and drawers render | Layout follows the responsive rules without page-level horizontal scroll; accessible names, focus handling and text status labels follow the accessibility constraints |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Property primary/archive/edit state guards (table-driven, BR-07); activity action whitelist and label subject mapping | AC-08, AC-09, AC-10, AC-18 |
| Backend integration | Migrated test database: backfilled primary and database rejection of second/archived primary and foreign branch; property create/edit/set-primary/archive/reactivate with `409`s, audit rows and set-primary atomicity; branch rule on create/edit; customer `GET/PUT` primary-property semantics and branch untouched; detail summary/billing, recent work, appointments and property history against seeded requests/quotes/work orders/visits/invoices; notes paging and creation; activity paging, whitelist and redaction; first property primary on create/import | AC-04, AC-05, AC-07–AC-12, AC-14–AC-18 |
| Authorization/tenant isolation | One cross-organization denial per path (customer sub-resources, foreign property id, property of another customer); Dispatcher branch-scope `404`; role matrix read vs mutate including Technician `403` | AC-21, AC-22, AC-23 |
| Persistence review | `database-reviewer` on the migration: backfill before constraint/index creation, check constraint, unique partial index, composite FK, notes index | AC-11 |
| Frontend component/service | Detail page role UX, tab/URL sync, Coming soon tabs and Create menu; per-region states and not-found; property drawer validation, branch option handling and actions menu; notes add/show more; list name navigation | AC-01, AC-03, AC-06, AC-07, AC-17, AC-19, AC-20, AC-24 |
| Browser (final audit) | User-supplied visual QA only: header and Overview fidelity, property flows, responsive layouts | AC-02, AC-13, AC-25 |

## Dependencies

- `customer-management` (AUDITED): customer drawer, endpoints, role policy,
  branch scope, display status and balance rules. This spec amends its BR-09
  and BR-15 navigation (BR-19) and its BR-11 edit mapping (BR-08). Its mockup
  `design/assets/20-design.png` is currently deleted in the working tree
  (unrelated to this spec).
- Authenticated app shell and Coming soon routes (`authenticated-app-shell`) —
  implemented; slugs `requests`, `quotes`, `work-orders`, `schedule`,
  `invoices` exist.
- Branches and user branch assignments — implemented.
- Shared discard-changes dialog — implemented.
- Requests, quotes, work orders, visits and invoices tables — mapped; no
  feature creates their data yet, so derived values are empty until those
  modules exist.
- Schema amendment and migration generation authorized (OD-16).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Customers created by CSV import before this feature have no `customer.created` activity entry (import writes one organization-level row). |
| AS-02 | Property edits apply last-write-wins; only the primary switch relies on the unique constraint for concurrency. |
| AS-03 | API route prefix and problem-details shape follow existing endpoints. |
| AS-04 | Technician names come from `technician_profiles.first_name`/`last_name`. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Default payment terms (absent in schema) | Omit; add column | No | Omit until the billing spec (user, 2026-10-01) |
| OD-02 | Primary property (absent in schema) | `is_primary` + index + backfill + Set as primary; derive oldest | No | Add `properties.is_primary`, unique partial index, backfill and **Set as primary** (user, 2026-10-01) |
| OD-03 | Internal notes model | Pinned `customers.notes` + append-only `customer_notes`; one only | No | `customers.notes` pinned; **Add note** appends to `customer_notes` (user, 2026-10-01) |
| OD-04 | Portal status and Send message | Omit; placeholders | No | Omit both (user, 2026-10-01) |
| OD-05 | Card pencils | Existing drawer; inline editing | No | Open the existing drawer (user, 2026-10-01) |
| OD-06 | Drawer property section with multiple properties | Primary property; remove | No | `GET/PUT /customers/{id}` work with the primary property (user, 2026-10-01) |
| OD-07 | Property branch | Per property; always customer's | No | Each property keeps its own branch; customer branch changes do not modify properties (user, 2026-10-01) |
| OD-08 | Property lifecycle | No archive; archive/reactivate | No | Archive/Reactivate added; primary (and therefore the last active property) cannot be archived without a replacement (user, 2026-10-01) |
| OD-09 | Transactional tabs | Coming soon; real lists | No | Coming soon; Overview uses real data (user, 2026-10-01) |
| OD-10 | Activity tab | Audit-based history; Coming soon | No | Real history from `audit_logs`, 20 per page, readable action, actor, org-timezone date, no sensitive values (user, 2026-10-01) |
| OD-11 | Summary calculations | BR-11 | No | Confirmed (user, 2026-10-01) |
| OD-12 | Recent work and appointments | BR-14, BR-15; no row menus | No | Confirmed, including Last invoice and Outstanding balance definitions (user, 2026-10-01) |
| OD-13 | Create menu | Property + Coming soon modules | No | Confirmed (user, 2026-10-01) |
| OD-14 | List navigation | Name and View existing → detail; Edit → drawer | No | Confirmed (user, 2026-10-01) |
| OD-15 | Permissions and audit | BR-20, BR-21; mutations allowed on archived customers | No | Confirmed (user, 2026-10-01) |
| OD-16 | Schema amendment and migration | Authorize; defer | No | Authorized: amend schema doc and generate migration with `--generate-migration`; never apply (user, 2026-10-01) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-14 |
| FR-03 | AC-12, AC-13 |
| FR-04 | AC-04 |
| FR-05 | AC-05, AC-06, AC-07, AC-08 |
| FR-06 | AC-09 |
| FR-07 | AC-10 |
| FR-08 | AC-11 |
| FR-09 | AC-14 |
| FR-10 | AC-15 |
| FR-11 | AC-04, AC-16 |
| FR-12 | AC-17 |
| FR-13 | AC-18 |
| FR-14 | AC-19 |
| FR-15 | AC-20 |
| FR-16 | AC-03, AC-21, AC-22, AC-23 |
| FR-17 | AC-05, AC-08, AC-09, AC-10, AC-17 |
| FR-18 | AC-24, AC-25 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-01 | — → DRAFT     | Created from Design 21 with the user's decisions OD-01 to OD-16 |
| 2026-10-01 | DRAFT → DRAFT | Revised after validation: side-card empty states (BR-23: "No phone", "No tags"), explicit generic `customer.updated` activity label, malformed ids return `404`, AC-11 restated as observable migrated-database behavior |
| 2026-10-01 | DRAFT → APPROVED | Approved by user via /spec approve |
