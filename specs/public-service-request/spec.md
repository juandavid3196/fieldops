# Public service request

| Field    | Value                    |
| -------- | ------------------------ |
| Feature  | `public-service-request` |
| Type     | Full-stack               |
| Status   | APPROVED                 |
| Created  | 2026-10-01               |
| Updated  | 2026-10-01               |
| Approved | 2026-10-01               |

## Context and objective

Prospective customers need to ask a specific organization for service without
an account. An anonymous visitor opens the organization's public request link
and completes a five-step form (Contact, Property, Service details,
Availability, Review). The visitor then receives a request number on screen
and a confirmation email. The request is stored as `new` in the correct
organization, with a resolved or new Customer, Contact and Property. No User
is created. The request appears immediately in the organization's request
pipeline data (Design 1; the pipeline UI is a separate feature).

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Anonymous visitor | Load the public form of an organization that accepts requests (BR-01); submit one request with up to 5 attachments | Read any request, customer, contact, property, price or cost; select a branch; create an account; choose another organization than the one in the link; exceed rate limits (BR-16) |
| System | Resolve the organization from the slug; resolve or create Customer/Contact/Property; number the request; record history and audit; send the confirmation email | Create `users`, `organization_users` or portal links; compute prices; book technicians or appointments |

## Scope

- Public form configuration endpoint: organization display data and the active
  catalog (categories and services) without prices.
- Public multipart submission endpoint that creates the request, attachments,
  status history and audit in one transaction.
- Customer/Contact/Property resolution by a single exact email match (BR-09).
- Per-organization request numbering (`REQ-<n>`).
- Attachment validation by real content (JPG, PNG, PDF).
- Abuse protection: per-IP and global rate limits, honeypot field, body size limit.
- Confirmation email through `IEmailSender` after commit.
- Public Angular wizard (5 steps + confirmation) outside the authenticated shell.
- Automatic, stable `public_slug` generation at organization registration and
  backfill for existing organizations (BR-21).
- Approved schema amendments (see Data and persistence impact).

## Non-goals

- Customer portal, account creation and the "Sign in to use a saved
  property" flow (that link is not rendered). The header "Sign in" only
  navigates to the existing sign-in page (BR-22).
- Pages behind the header links "Services", "How it works", "Help". These
  links are visual only until their pages exist (BR-22).
- Price calculation or display; technician assignment; appointment booking.
- Branch selection from ZIP or any address data (`service_postal_codes` is not read).
- Pipeline UI (Design 1), request status changes after `new` and staff review of attachments.
- SMS sending. The text-message preference is only stored.
- CAPTCHA.
- Editing the organization slug in Company settings (see OD-10).
- Persisting form drafts across page reloads.

## User flow

1. Visitor opens `/request/<slug>`. System loads the form configuration, or
   shows the unavailable page (BR-01).
2. Contact step: visitor enters first/last name, email, mobile phone and at
   least one update method, then continues.
3. Property step: visitor chooses Home or Business, enters the address and
   optional access instructions, then continues.
4. Service details step: visitor picks a category, then a service or "I'm not
   sure". The visitor describes the problem, picks the urgency, optionally marks
   active damage and adds files, then continues.
5. Availability step: visitor picks As soon as possible, Choose a date (one
   date) or I'm flexible, picks a time window and optionally adds scheduling
   notes, then continues.
6. Review step: system shows all sections with Edit links and the notice that
   submitting does not confirm a price or appointment. The visitor checks the
   consent box and submits.
7. System validates everything, stores the request as `new` and returns the
   request number. After commit, the system sends the confirmation email.
8. Confirmation screen shows the request number, the confirmation-email
   notice, next steps and the help phone, with "Return to home" and "Submit
   another request".

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The system must expose, anonymously, the form configuration of an organization identified by its public slug: organization name, phone, website, request prefix, active categories, and per category its active service-type catalog items (id and name only). |
| FR-02 | The system must return one identical not-found response for unknown slugs and for organizations that do not accept public requests (BR-01), on both public endpoints. |
| FR-03 | The frontend must present a five-step wizard with per-step validation (BR-03…BR-08), Back navigation, a "Your request" summary panel with Edit links for completed steps, and a Review step that lists every section with Edit links. Entered data is preserved across steps. |
| FR-04 | The frontend must let the visitor choose "I'm not sure" instead of a service. This clears and disables service selection while the category stays required. |
| FR-05 | The frontend must let the visitor add and remove up to 5 JPG/PNG/PDF files of at most 10 MB each and 25 MB in total, and reject other files with a message before submission. |
| FR-06 | The Review step must require the consent checkbox, show the price/appointment notice and submit once (no duplicate submission while in flight). |
| FR-07 | The system must validate the complete submission server-side and create one `service_requests` row: status `new`, source `public_form`, next organization request number, `branch_id` null, with all submitted service, availability and contact snapshot data (BR-12). |
| FR-08 | The system must resolve the Customer and Contact by BR-09, always create a new Property (BR-10), and never create or link a User. |
| FR-09 | The system must store each attachment with its validated real content type, sanitized file name, size and content, linked to the request and organization. |
| FR-10 | The system must perform numbering, customer/contact/property creation, request, attachments, status history and audit in one transaction. Any failure persists nothing. |
| FR-11 | The system must record a status history row (none → `new`) and one audit row for the creation, both without personal data (BR-15). |
| FR-12 | After a successful commit, the system must send one confirmation email to the submitted address through `IEmailSender`. An email failure must not change the response and must be logged without personal data. |
| FR-13 | The system must enforce the per-IP and global rate limits, the honeypot rule and the body size limit (BR-16…BR-18). |
| FR-14 | The frontend must show the confirmation screen with request number, email notice, next steps and help phone. It must provide "Return to home" and "Submit another request" (resets the wizard). |
| FR-15 | The frontend must show distinct loading, unavailable, configuration-error, submission-error and rate-limit states without losing entered data (except unavailable). |
| FR-16 | The system must assign every organization a unique `public_slug` when it is created (and backfill existing organizations) per BR-21. The slug must stay unchanged when the organization is renamed. |
| FR-17 | The public form header must show the organization name and phone, a "Sign in" action that opens the existing sign-in page, and the non-interactive "Services", "How it works" and "Help" labels (BR-22). |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | An organization accepts public requests when its `public_slug` equals the path slug (case-insensitive, compared lowercased), `is_active = true`, an active main branch, and at least one active category with one or more active service-type catalog items. Otherwise both public endpoints return the not-found response of FR-02. | Backend |
| BR-02 | The organization is resolved only from the slug. Request bodies contain no organization or branch identifiers; any such extra field is ignored. | Backend |
| BR-03 | Contact fields: see Field validation table. At least one of `prefersEmail` / `prefersSms` is true. | Both |
| BR-04 | Property fields: see Field validation table. Address is US-only: `state` is a two-letter USPS code (50 states + DC), `postalCode` is `^\d{5}(-\d{4})?$`, stored `country_code` is `US`. | Both |
| BR-05 | `categoryId` is required and must be an active category of the resolved organization. If `notSure` is false, `serviceId` is required and must be an active `service`-type catalog item of that organization whose `category_id` equals `categoryId`. If `notSure` is true, `serviceId` must be absent. A category or service that is unknown, inactive, the wrong type or from another organization produces the same field error. | Both |
| BR-06 | `description` is required, 1–1000 characters after trim. `urgency` ∈ `standard` \| `urgent` \| `emergency` (UI default `standard`). `hasActiveDamage` is a boolean (default false). | Both |
| BR-07 | Availability: `dateMode` ∈ `asap` \| `date` \| `flexible`. `preferredDate` is required only when `dateMode = date` and must be within today … today + 90 days in the organization timezone; otherwise it must be absent. `timeWindow` ∈ `morning` (08:00–12:00) \| `afternoon` (12:00–17:00) \| `evening` (17:00–20:00) \| `any` (08:00–20:00). `schedulingNotes` ≤ 1000 characters. | Both |
| BR-08 | `consent` must be true. | Both |
| BR-09 | Customer/Contact resolution: count active `customer_contacts` of the organization whose `lower(email)` equals the lowercased submitted email and whose customer is active. **Exactly one** → link the request to that contact and its customer, without modifying either. **Zero or more than one** → create a new Customer (`type` = `person` for Home, `company` for Business; `display_name` = "First Last"; `primary_email`, `primary_phone`; `branch_id` = active main branch) and a new primary Contact (names, email, phone, `prefers_email`, `prefers_sms`). The response is identical in both cases. | Backend |
| BR-10 | A new Property is always created for the resolved customer: `name` = `Home` or `Business`, address fields, `country_code = US`, `access_instructions`, `branch_id` null. `is_primary` is true only when the customer has no active primary property. | Backend |
| BR-11 | Request number = the organization's `next_request_number`, read and incremented under a row lock in the same transaction. Displayed as `<request_prefix>-<number>` (e.g. `REQ-1048`). Numbers are unique and gap-free per organization for committed requests. | Backend |
| BR-12 | Stored request data: `customer_id`, `contact_id`, `property_id`, `category_id`, `catalog_item_id` (null when not sure), `guest_name` ("First Last"), `guest_email`, `guest_phone`, `service_address` snapshot (line1, line2, city, state, postalCode, countryCode, propertyType), `description`, `urgency`, `has_active_damage`, `availability_preferences` (`dateMode`, `preferredDate`, `timeWindow`, `schedulingNotes`), `consent_at` = server time. When `dateMode = date`, `preferred_start`/`preferred_end` = date + window bounds in the organization timezone; otherwise both null. | Backend |
| BR-13 | Attachments: 0–5 files; each 1 byte–10 MB (10 485 760 bytes); total ≤ 25 MB (26 214 400 bytes). Type is detected from content signature: JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, PDF `25 50 44 46 2D`. The detected type must match the file extension (`.jpg`/`.jpeg`, `.png`, `.pdf`, case-insensitive). The stored `mime_type` is the detected one. | Both (content detection Backend) |
| BR-14 | Stored file name: directory parts and control characters removed, trimmed, truncated to 255 characters; empty result → `attachment`. `uploaded_by_user_id` is null. | Backend |
| BR-15 | Status history row: `from_status` null, `to_status` `new`, `changed_by_user_id` null, `reason` null. Audit row: `action` `service_request.created`, `entity_type` `service_request`, `entity_id` = request id, `actor_user_id` null, `branch_id` null, `ip_address` null, `after_data` = `{ requestNumber, status, source, urgency, hasActiveDamage, attachmentCount }`. Neither row nor any log contains names, email, phone, address, description, notes or file names. | Backend |
| BR-16 | Rate limits (in-memory, per `RemoteIpAddress`, every request counts): submission 5 per IP per 15 minutes and 100 global per hour; form configuration 60 per IP per 5 minutes and 600 global per minute. Rejection: `429` with `Retry-After`. | Backend |
| BR-17 | Honeypot: the submission carries a `website` field that the UI keeps hidden and empty. A non-empty value returns the generic `400` (no field errors) and persists nothing. | Both |
| BR-18 | The submission request body is limited to 26 MB (27 262 976 bytes). A larger body returns `413`. | Backend |
| BR-19 | Confirmation email: subject `We received your request <REQ-n>`. Text and HTML bodies contain the contact's first name, organization name, request number, the three next steps and the notice "Submitting this request does not confirm a price or appointment." No address, description or attachments. It is sent after commit. On failure the system logs the request id and failure category only. | Backend |
| BR-20 | Form drafts are kept in memory only (no browser storage). A page reload restarts the wizard. | Frontend |
| BR-21 | Slug generation from the organization `name`: (1) Unicode decomposition with diacritics removed (`Café Ñandú` → `cafe nandu`); (2) lowercase; (3) every run of characters outside `a-z0-9` becomes one `-`; (4) leading/trailing `-` trimmed; (5) empty result → `organization`. Uniqueness: the base is used if free, otherwise the lowest free `-2`, `-3`, …. The total length including the suffix is ≤ 60: the base is truncated to `60 − length(suffix)` and trailing `-` trimmed. Uniqueness is enforced by the database unique index. A unique violation from a concurrent registration retries with the next suffix, up to 10 attempts; after that the registration fails with its existing `500` behavior, rolled back. The slug is stored once at creation and never regenerated, including when the name changes. Backfill applies the same rules to existing organizations in `created_at`, `id` order. | Backend |
| BR-22 | Public header: "Sign in" navigates to the existing sign-in route. "Services", "How it works" and "Help" render as plain, non-focusable text with no navigation. | Frontend |

### Field validation

| Step | Field | Required | Rule |
| ---- | ----- | -------- | ---- |
| Contact | `firstName` | Yes | 1–100 chars after trim |
| Contact | `lastName` | Yes | 1–100 chars after trim |
| Contact | `email` | Yes | ≤ 254 chars, valid email, stored trimmed |
| Contact | `phone` | Yes | ≤ 40 chars; 10–15 digits after removing spaces, `()`, `-`, `+`, `.` |
| Contact | `prefersEmail`, `prefersSms` | At least one true | Boolean |
| Property | `propertyType` | Yes | `home` \| `business` |
| Property | `addressLine1` | Yes | 1–180 chars |
| Property | `addressLine2` | No | ≤ 180 chars |
| Property | `city` | Yes | 1–100 chars |
| Property | `state` | Yes | BR-04 |
| Property | `postalCode` | Yes | BR-04 |
| Property | `accessInstructions` | No | ≤ 1000 chars |
| Service | `categoryId`, `serviceId`, `notSure` | BR-05 | BR-05 |
| Service | `description`, `urgency`, `hasActiveDamage` | BR-06 | BR-06 |
| Service | `attachments` | No | BR-13 |
| Availability | `dateMode`, `preferredDate`, `timeWindow`, `schedulingNotes` | BR-07 | BR-07 |
| Review | `consent` | Yes | BR-08 |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | `new` | Public submission committed | Anonymous visitor (system) | BR-01…BR-18 pass |

Later request transitions belong to the pipeline feature.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `organizations`: read `id`, `name`, `phone`, `website`, `timezone`, `is_active`, and the new columns. Lock the row and increment `next_request_number`. Organization registration writes `public_slug` once (BR-21). Settings updates never write it.
  - `branches`: read the active main branch (`is_main`, `is_active`).
  - `service_categories`, `catalog_items`: read active rows (`type = service`). Never expose `unit_price`, `unit_cost`, `tax_rate`.
  - `customers`, `customer_contacts`: read for BR-09; insert new rows.
  - `properties`: read the primary flag; insert one row.
  - `service_requests`, `request_attachments`, `request_status_history`, `audit_logs`: insert.
  - Not written: `users`, `organization_users`, `notifications`, `request_messages`.
- Constraints relied on: composite `(organization_id, id)` FKs on every tenant
  relation; `UNIQUE(organization_id, request_number)`;
  `ux_customer_contacts_primary`; `ux_properties_customer_primary`;
  `ix_requests_pipeline`.
- Schema amendments required: **approved by the user 2026-10-01** (OD-01,
  OD-03, OD-05). Implementation updates `docs/database/fieldops-schema.sql`
  and the EF model, and generates one migration only under
  `--generate-migration`. Agents never apply it. Review by `database-reviewer`.
  1. `organizations`:
     - `public_slug varchar(60) NOT NULL` (added nullable, backfilled per BR-21, then set NOT NULL), unique index `ux_organizations_public_slug`, `CHECK (public_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(public_slug) BETWEEN 1 AND 60)`.
     - `request_prefix varchar(20) NOT NULL DEFAULT 'REQ'`.
     - `next_request_number bigint NOT NULL DEFAULT 1`, backfilled to `max(request_number) + 1` per organization where requests exist.
  2. `service_requests`:
     - `catalog_item_id uuid`, `FOREIGN KEY (organization_id, catalog_item_id) REFERENCES catalog_items(organization_id, id)`.
     - `urgency varchar(20) NOT NULL DEFAULT 'standard' CHECK (urgency IN ('standard','urgent','emergency'))`.
     - `has_active_damage boolean NOT NULL DEFAULT false`.
     - `availability_preferences jsonb`.
     - `consent_at timestamptz`.
  3. `request_attachments`:
     - Add `content bytea`.
     - Make `storage_key` nullable (non-destructive).
     - `CHECK (content IS NOT NULL OR storage_key IS NOT NULL)`.
     - `CHECK (mime_type IN ('image/jpeg','image/png','application/pdf'))`.
     - `CHECK (size_bytes <= 10485760)`.
  4. Slug provisioning: backfill of existing organizations inside the same
     migration and generation at registration, per BR-21 (OD-10).
- Domain changes: request creation from a public submission; request number
  allocation on Organization; Customer/Contact/Property factory use for public
  intake.

## Tenant isolation and authorization

- Organization context: resolved server-side only from `public_slug` (BR-01, BR-02). No session is read. An authenticated caller is treated as anonymous.
- Client-provided organization identifiers: none accepted. `categoryId` and `serviceId` are client identifiers and are verified against the resolved organization (BR-05).
- Category/service of another organization: `400` with the same field error as a nonexistent id; nothing persisted.
- Unknown slug, inactive organization or organization not accepting requests: identical `404` ProblemDetails (FR-02).
- BR-09 matches contacts only inside the resolved organization. The response never reveals whether a customer or contact existed.
- Every inserted row carries the resolved `organization_id`. Composite FKs reject cross-tenant links.
- Permissions: anonymous for both endpoints. Responses: `Cache-Control: no-store`.

## API contracts

Contract status: Final. Paths follow the existing no-prefix routing. Errors use
ProblemDetails. `400` responses carry field errors keyed by the request field
paths below, except BR-17. CORS uses the existing policy; `Retry-After` is
already exposed.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/public/organizations/{slug}/service-request-form` | — | `200 { organizationName, phone, website, requestPrefix, timezone, categories: [{ id, name, services: [{ id, name }] }] }`; only categories with ≥ 1 active service are listed; both levels are ordered by name | `404` (FR-02) · `429` + `Retry-After` · `500` | Anonymous |
| POST | `/public/organizations/{slug}/service-requests` | `multipart/form-data`: part `request` (JSON) and 0–5 parts `attachments` (files). JSON: `{ contact: { firstName, lastName, email, phone, prefersEmail, prefersSms }, property: { propertyType, addressLine1, addressLine2, city, state, postalCode, accessInstructions }, service: { categoryId, serviceId, notSure, description, urgency, hasActiveDamage }, availability: { dateMode, preferredDate, timeWindow, schedulingNotes }, consent, website }` | `201 { requestNumber }` (e.g. `"REQ-1048"`) | `400` (field errors; generic for BR-17 and malformed multipart/JSON) · `404` (FR-02) · `413` (BR-18) · `415` (not multipart) · `429` + `Retry-After` · `500` (rolled back) | Anonymous |

- Field error keys: `contact.firstName` … `availability.schedulingNotes`, `consent`, `attachments` (count/total), `attachments[i]` (per-file size/type).
- `preferredDate` is ISO `YYYY-MM-DD`. `phone`, `website` and the address fields are returned as stored.
- Internal contract: existing `IEmailSender`, unchanged.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Form shell (`/request/:slug`) | Skeleton of header, stepper and card while configuration loads | — (BR-01 makes an empty catalog unavailable) | Configuration load failure (non-404): message "We couldn't load this form." with Retry. `404`: unavailable page "This request form isn't available." with no form | Public; no auth guard; no authenticated shell | Header shows organization name and phone; step 1 shown |
| Contact | — | Fields empty, Email preference checked by default | Inline field errors; "Select at least one method." | — | Continue to Property |
| Property | — | Home selected by default; State select of USPS codes | Inline errors | — | Continue to Service details |
| Service details | — | No category selected; Standard urgency; no files | Inline errors; file rejection message naming the file and reason (type, > 10 MB, > 5 files, > 25 MB total) | — | Continue to Availability; emergency hint shown beside urgency |
| Availability | — | As soon as possible + Morning preselected | Date required/out of range errors | — | Continue to Review; notice "We'll confirm the appointment time after reviewing your request." |
| Review | Submit button shows progress and is disabled while submitting | — | `400`: return to the first step with errors and show them; `413`: attachment-size message on Service details; `429`: "Too many requests. Please try again later."; network/`500`/other: "We couldn't submit your request. Please try again." Data retained in all cases | — | Navigate to Confirmation |
| Confirmation | — | — | — | — | "Request received", "Thanks, <first name>. We'll send a confirmation to <email>.", request number, next steps, help phone with request number, Return to home (organization website if set, otherwise restarts the form), Submit another request (clears data, step 1) |

- The "Your request" summary panel shows "Not submitted" and per section either "Not added yet" or the entered summary, with Edit for completed steps. Edit from the panel or the Review step opens that step with data intact. Continuing proceeds through the following steps with data intact.
- "Continuing as guest" notice is shown on the steps per the mockups, without sign-in links.
- Mockups (Approved): `design/assets/27-contact.png`, `27-property.png`, `27-service-details.png`, `27-availability.png`, `27-review.png`, `27-confirmation.png`. Deviations, by decision: the FieldOps brand and phone are replaced by the organization name and phone. "Sign in" opens the existing sign-in page, and the nav links are visual only (BR-22). Availability allows one date (OD-08). Confirmation copy says "We'll send a confirmation" (OD-06).
- Responsive: below the large breakpoint the summary panel stacks below the step card; option tiles wrap; stepper labels may hide but the current step stays announced.
- Accessibility: on step change, focus moves to the step heading and the stepper exposes the current step. On failed Continue/Submit, focus moves to the first invalid field. Option tiles are radio/checkbox groups operable by keyboard. The file drop zone has a keyboard-operable browse button. Each file remove button has an accessible name including the file name. Character counters are announced politely.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Unknown/inactive/not-accepting slug | `404` identical body; unavailable page | None |
| Invalid field (BR-03…BR-08) | `400` field errors; UI returns to first failing step | None |
| Category/service foreign or inactive | `400` on `service.categoryId`/`service.serviceId` | None |
| Attachment type/size/count/total invalid | `400` on `attachments`/`attachments[i]` | None |
| Body > 26 MB | `413`; attachment-size message | None |
| Honeypot filled | `400` generic | None |
| Rate limit exceeded | `429` + `Retry-After`; rate-limit message | None |
| Failure during transaction | `500` generic; generic submission error | None (number not consumed) |
| Email send failure after commit | `201` as success; logged without personal data | Request stays committed |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An organization accepting requests, with inactive categories, inactive services and product items | The form configuration is requested | `200` with name, phone, website, prefix, timezone and only active categories that have active services, each with only active service-type items. No price, cost or tax fields. `Cache-Control: no-store` |
| AC-02 | Slugs that are unknown, belong to an inactive organization, have no active main branch, or have no active services | Either public endpoint is called | Identical `404` ProblemDetails for all cases. The UI shows the unavailable page and no form |
| AC-03 | The wizard on any step | Continue is pressed with invalid or missing fields (Field validation table) | The step does not advance, inline errors show, and focus moves to the first invalid field. Contact requires at least one update method |
| AC-04 | Data entered in several steps | The visitor uses Back, or Edit from the summary panel or Review | The target step opens with all entered data intact. The summary panel and Review reflect the current values of every completed section |
| AC-05 | Service details step with a category selected | The visitor selects "I'm not sure" | Service selection is cleared and disabled. Continue requires only category, description and urgency. Review shows the category without a service |
| AC-06 | Service details step | The visitor adds a 6th file, a file > 10 MB, a non-JPG/PNG/PDF file, or files exceeding 25 MB in total | The file is rejected with a message naming it and the reason. Accepted files list name and size and can be removed |
| AC-07 | Availability step | "Choose a date" is selected | A date within today … today + 90 days is required. As soon as possible and I'm flexible require no date. A time window is always required |
| AC-08 | Review step | The visitor submits without consent, or submits and presses Submit again while in flight | Without consent, submission is blocked with an error. While in flight, the button is disabled and only one request is sent. The price/appointment notice is visible |
| AC-09 | A valid submission whose email matches no contact | It is submitted | `201 { requestNumber: "REQ-<n>" }`. One request: `new`, `public_form`, `branch_id` null, with BR-12 data. Customer is created (type per Home/Business, main branch), plus a primary Contact and a primary Property. No `users` or `organization_users` rows are created |
| AC-10 | Exactly one active contact of an active customer in the organization has the email (any case) | A valid submission is made | The request links that contact and customer, and both stay unchanged. A new Property is created, primary only if the customer had no active primary property. Response shape equals AC-09 |
| AC-11 | Two active contacts share the email, or the only match is in another organization | A valid submission is made | New Customer, Contact and Property are created. No existing record is linked or modified. Response shape equals AC-09 |
| AC-12 | Two organizations, one with prior requests | Requests are submitted, including two concurrently to the same organization | Each organization numbers from its own `next_request_number`. Concurrent submissions receive distinct consecutive numbers. The other organization's counter is unchanged |
| AC-13 | Submissions violating server rules (table-driven over the Field validation table, `notSure` with a `serviceId`, date outside range, `consent` false) | They are submitted | `400` with field errors at the documented keys. No row is inserted and the counter is unchanged |
| AC-14 | A `categoryId` or `serviceId` from another organization, inactive, a product, or a service from another category | It is submitted | `400` on that field, identical to a nonexistent id. Nothing persisted |
| AC-15 | Attachments: a valid JPG, PNG and PDF; a text file renamed `.jpg`; a PNG named `.pdf`; an empty file; a path-like file name | They are submitted | Invalid content or extension mismatch → `400` on `attachments[i]` and nothing persisted. Valid files are stored with the detected mime type, size, content, sanitized name and null uploader |
| AC-16 | Attachments exceeding 10 MB each, 6 files, or 25 MB total; a body > 26 MB | They are submitted | `400` on `attachments[i]`/`attachments` for limits. `413` for the oversize body. Nothing persisted |
| AC-17 | A failure injected after customer creation and before commit | A valid submission is made | `500` generic. No customer, contact, property, request, attachment, history or audit row exists. `next_request_number` is unchanged. No email is sent |
| AC-18 | A committed submission | History and audit are inspected | One history row none → `new` with null user. One audit row per BR-15. Neither contains name, email, phone, address, description, notes or file names |
| AC-19 | A committed submission | The email sender succeeds, or throws | One email per BR-19 to the submitted address after commit. If sending throws, the response is still `201`, the request remains, and the log has no recipient or body |
| AC-20 | One IP | It sends a 6th submission within 15 minutes, or a 61st form configuration request within 5 minutes | `429` with `Retry-After` and nothing persisted. The UI shows the rate-limit message and keeps the data |
| AC-21 | A submission with a non-empty `website` field | It is submitted | `400` generic without field errors and nothing persisted |
| AC-22 | A successful submission | The confirmation screen shows | It shows the request number, "We'll send a confirmation to <email>", next steps and the help phone. "Submit another request" returns to an empty step 1. "Return to home" goes to the organization website, or restarts the form if none |
| AC-23 | The Review step | Submission returns `400`, `413`, `500` or a network error | The UI shows the matching message from the UI states table. On `400` it opens the first failing step with server field errors. Entered data is retained |
| AC-24 | Organization names covering accents, symbols only, an empty result, over-60 characters, existing collisions and two concurrent registrations with the same name; and existing organizations at migration | Organizations are registered, or the backfill runs, and an organization is later renamed through Company settings | Slugs follow BR-21 (e.g. `Café & Co.` → `cafe-co`; symbols only → `organization`; collision → `cafe-co-2`; length ≤ 60 including suffix). Concurrent registrations both succeed with distinct slugs. Every existing organization gets a unique slug. Renaming leaves the slug unchanged |
| AC-25 | The public form is loaded | The header is inspected and used | It shows the organization name and phone. "Sign in" opens the existing sign-in page. "Services", "How it works" and "Help" are non-interactive text |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | File signature detection with extension match and name sanitization (table-driven). Availability → `preferred_start`/`preferred_end` in the organization timezone. Slug normalization and suffix truncation (table-driven) | AC-15, AC-24, BR-07, BR-12, BR-21 |
| Backend integration | Registration stores the slug with collision suffix, including two concurrent registrations; a rename through Company settings keeps the slug | AC-24 |
| Backend integration | Form configuration filtering and identical 404 cases (parameterized) | AC-01, AC-02 |
| Backend integration | Happy path with new records: request, customer, contact, property, attachments, history, audit, no user, email sent after commit | AC-09, AC-15, AC-18, AC-19 |
| Backend integration | Contact resolution: single match reused unchanged vs multiple/other-org matches create new (parameterized) | AC-10, AC-11 |
| Backend integration | Validation failures and cross-organization category/service (table-driven), with persistence and counter unchanged | AC-13, AC-14 |
| Backend integration | Attachment content/limit failures and 413 (table-driven) | AC-15, AC-16 |
| Backend integration | Transaction rollback on injected failure, and email-failure tolerance | AC-17, AC-19 |
| Backend integration | Concurrent numbering across two organizations | AC-12 |
| Backend integration | Rate limit 429 and honeypot | AC-20, AC-21 |
| Authorization/tenant isolation | Covered by AC-02, AC-11 (other-org email) and AC-14 (other-org catalog ids). One representative case each | AC-02, AC-11, AC-14 |
| Frontend component/service | Step validation and navigation with data preserved (Back/Edit) | AC-03, AC-04 |
| Frontend component/service | "I'm not sure" and availability date rules | AC-05, AC-07 |
| Frontend component/service | Client attachment rules | AC-06 |
| Frontend component/service | Submission: consent, single in-flight request, success → confirmation, error mapping (400/413/429/500), reset | AC-08, AC-20, AC-22, AC-23 |
| Frontend component/service | Unavailable/configuration-error states; header Sign in navigation and non-interactive labels | AC-02, AC-25 |
| Browser (final audit) | User visual QA: full happy path on desktop and narrow width; one server-validation failure | AC-04, AC-22, AC-23 |

The backend integration tests exceed the default of 8 test methods by up to 2 (10 groups). The extra groups cover distinct high-risk boundaries this
feature creates: anonymous tenant resolution, uploads, multi-record rollback,
concurrent numbering and concurrent slug uniqueness at registration. The
migration backfill for existing organizations is reviewed by
`database-reviewer`, not by a separate test.

## Dependencies

- `products-services` (catalog categories and service items) — implemented.
- `customer-management`, `customer-property-detail` (Customer/Contact/Property domain) — implemented.
- `company-settings-and-branches` (main branch, organization phone/website/timezone) — implemented.
- `password-recovery` (`IEmailSender`) — implemented.
- Existing rate limiting infrastructure (`ApiRateLimitingExtensions`) — implemented.
- Schema amendments 1–4 — approved (OD-01, OD-03, OD-05, OD-10).
- `organization-onboarding` (registration) — implemented. Modified here only to
  generate `public_slug`; its contract is unchanged.
- Pipeline (Design 1) — future consumer of `status = new` rows; not required.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | The header shows the organization name and phone. "Sign in to use a saved property" is not rendered (confirmed by user). Header link behavior is defined by BR-22. |
| AS-02 | Review edits are client-side only. The server revalidates the complete submission (confirmed by user). |
| AS-03 | Request number counters start at 1, as the existing quote/work order/invoice counters do. |
| AS-04 | Addresses are US-only, matching the mockup's State select and ZIP field. |
| AS-05 | The confirmation email is always sent to the submitted email, regardless of update preference. SMS preference is stored only. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | How is the organization resolved publicly? | a) `public_slug` · b) UUID in URL · c) host | No | a) — user 2026-10-01 |
| OD-02 | Branch of a new Customer? | a) active main branch; request/property branch null · b) other | No | a) — user 2026-10-01 |
| OD-03 | Attachment storage? | a) bytea in `request_attachments` · b) local file storage · c) S3 | No | a) with max 5 files, 10 MB each, 25 MB total — user 2026-10-01 |
| OD-04 | Duplicate handling? | a) reuse by email · b) always new · c) also reuse property | No | a) only when exactly one active contact matches; multiple matches create new records — user 2026-10-01 |
| OD-05 | New intake fields? | a) explicit columns + availability jsonb + request counter/prefix · b) one jsonb · c) other | No | a) — user 2026-10-01 |
| OD-06 | Email failure behavior? | a) commit then send; log failure · b) roll back | No | a) — user 2026-10-01 |
| OD-07 | Abuse protection? | a) rate limits + honeypot + body limit, no CAPTCHA · b) CAPTCHA | No | a) — user 2026-10-01 |
| OD-08 | Number of preferred dates? | a) up to 3 · b) one | No | b) one, as in the mockup — user 2026-10-01 |
| OD-09 | "I'm not sure" scope? | a) category required, service optional · b) also category optional | No | a) — user 2026-10-01 |
| OD-10 | How do organizations get a `public_slug`? Without one, no organization can receive public requests. | a) Automatic generation at registration plus migration backfill; no editing UI · b) a) plus an editable slug field in Company settings · c) Slugs set manually outside the product | No | a) with stable slugs, accent-free normalization, `organization` fallback, ≤ 60 chars including suffix, DB unique index and concurrent-collision retry (BR-21) — user 2026-10-01 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02 |
| FR-03 | AC-03, AC-04 |
| FR-04 | AC-05 |
| FR-05 | AC-06 |
| FR-06 | AC-08 |
| FR-07 | AC-07, AC-09, AC-12, AC-13, AC-14 |
| FR-08 | AC-09, AC-10, AC-11 |
| FR-09 | AC-15, AC-16 |
| FR-10 | AC-17 |
| FR-11 | AC-18 |
| FR-12 | AC-19 |
| FR-13 | AC-16, AC-20, AC-21 |
| FR-14 | AC-22 |
| FR-15 | AC-02, AC-20, AC-23 |
| FR-16 | AC-24 |
| FR-17 | AC-25 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-01 | — → DRAFT | Created |
| 2026-10-01 | DRAFT → DRAFT | Resolved OD-10 (slug generation BR-21, FR-16, AC-24); header Sign in and visual-only links (BR-22, FR-17, AC-25) |
| 2026-10-01 | DRAFT → APPROVED | Approved by user via /spec approve |
