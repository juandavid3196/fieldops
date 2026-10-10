# Customer portal and dashboard

| Field    | Value                       |
| -------- | --------------------------- |
| Feature  | `customer-portal-dashboard` |
| Type     | Full-stack                  |
| Status   | APPROVED                    |
| Created  | 2026-10-10                  |
| Updated  | 2026-10-10                  |
| Approved | 2026-10-10                  |

## Context and objective

Today customers reach FieldOps only through emailed one-off links: the public
request form, the quote page and the invoice payment page. This feature adds
an authenticated customer portal (Design 28). Staff invite a customer's primary
contact. The contact activates a separate portal session linked through
`customer_contacts.portal_user_id`. The contact then sees a dashboard built
only from real data: the quote awaiting action, the invoice due, the next
appointment and its progress, active requests, properties, updates derived from
real events, and recent activity. The portal shell has working Home, Requests,
Appointments, Quotes and Invoices modules. Every card and link reaches a real
page or action, never "Coming soon". The server enforces strict
customer-and-organization isolation for every portal call.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Portal contact (signed in through the portal session) | Read their own customer's properties, requests, appointments, quotes, invoices, payments, reports and derived updates in the session organization. Request a service. Add properties and edit property name, access instructions and primary flag. Respond to quotes, pay invoices, download PDFs, submit one review. Request a reschedule. Send a message. Switch between their active links | See or act on another customer or organization. Edit a property address, archive properties. Cancel or reschedule visits directly. Use internal endpoints with the portal cookie. Choose amounts the server trusts |
| Invited contact (anonymous, valid invitation token) | Activate portal access with a new password, or link an existing account by proving its password | Pick another contact, customer or organization |
| Owner, Dispatcher (`CustomerPolicies.Manage`) | Invite or re-invite the customer's primary contact; remove portal access | Read portal passwords or sessions; act as the contact |
| Other internal read roles (`CustomerPolicies.View`) | See the primary contact's portal status | Invite or remove access (`403`) |
| Technician and unknown roles | — | Unchanged (`403` on customer endpoints) |

## Scope

- Portal authentication: separate cookie and scheme, sign-in, sign-out,
  current session, **Switch account**, per-request revalidation and
  revocation (BR-01 … BR-09).
- Portal invitations: staff invite/resend/remove on the customer detail
  Contact details card; activation for new and existing users (BR-10 …
  BR-14).
- Portal password recovery with portal links, reusing the existing reset
  tokens and confirmation (BR-15).
- Portal shell: header navigation Home, Requests, Appointments, Quotes,
  Invoices; Help; notification bell; user menu (BR-16).
- Dashboard per Design 28 with the property selector and every card (BR-17 …
  BR-26).
- Module pages: Requests (list, detail, new request), Appointments (list,
  detail, reschedule), Quotes (list, review), Invoices (list, view and pay),
  Properties (manage, add), Updates (full feed), Activity (full list) (BR-27 …
  BR-34).
- Portal endpoints that reuse the quote approval and invoice payment use
  cases with session-based authorization (BR-30, BR-31).
- **Send a message** email to the organization (BR-35).
- "Sign in to your portal" links on public pages (BR-36).
- Schema amendments SA-19 … SA-21 (Data and persistence impact).

## Non-goals

- Inviting non-primary contacts (no staff UI lists them); contact
  self-registration.
- Editing a property address, archiving properties or deleting anything from
  the portal.
- Automatic visit rescheduling, a staff reschedule inbox or dispatch
  indicators; cancelling visits from the portal.
- Chat threads, replies inside FieldOps, SMS, push notifications, or writing
  `notifications` rows. The updates feed is derived and read-only.
- Generating public quote or invoice tokens from the portal; converting a
  public token into a session.
- Deep-link return after sign-in (sign-in always lands on Home).
- Customer profile editing, contact preference editing, saved payment
  methods, staff impersonation.
- Changes to the public quote, invoice or request flows other than BR-36 and
  the shared use-case extraction needed by BR-30/BR-31.
- Automated browser, E2E or visual tests by agents.

## User flow

1. An Owner or Dispatcher opens a customer and selects **Invite to portal** on
   the Contact details card. The primary contact receives an email with
   `/portal/activate#token=…`.
2. The contact opens the link. A new user sets a password. An existing user
   enters their current password. The system links the contact, starts a
   portal session and opens Home.
3. Later, the contact signs in at `/portal/sign-in`. The session opens the
   oldest active link. **Switch account** appears when two or more active links
   exist.
4. Home shows the greeting, property selector, **Request a service**, the
   quote awaiting action, the payment due, the selected property, the upcoming
   appointment with progress, active requests, updates and recent activity.
5. From the cards the contact reviews the quote and approves, declines or asks
   a question. The contact can also pay the invoice, open a report or receipt,
   request a reschedule, open requests, manage properties, read all updates,
   call, or send a message.
6. If staff archive the customer, remove portal access, or deactivate the
   user, or the password changes, the next portal request returns `401`. The
   cookie is deleted and the portal returns to sign-in.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The system must authenticate portal contacts with email and password into a portal session separate from the internal session. The session must carry user, contact, customer and organization, chosen server-side (BR-01 … BR-05). |
| FR-02 | The system must revalidate every portal request and reject it with `401` plus cookie deletion when any BR-06 condition fails. A password change must invalidate both internal and portal sessions issued before it. |
| FR-03 | The system must let a user with two or more active links switch the session to another of their links, and refuse any contact that is not theirs (BR-07). |
| FR-04 | Staff with Manage permission must be able to invite or re-invite a customer's primary contact and remove portal access. Read roles must see the portal status (BR-10, BR-11, BR-14). |
| FR-05 | An invited contact must be able to activate access as a new user or link an existing user by proving its password, without duplicate users or membership changes (BR-12, BR-13). |
| FR-06 | Portal users must be able to recover their password through portal-specific links (BR-15). |
| FR-07 | The portal shell must provide working navigation to Home, Requests, Appointments, Quotes and Invoices, plus Help, the bell with an unread count, and a user menu (BR-16). |
| FR-08 | The dashboard must show a greeting and a property selector that scopes the cards. It must show the selected property card with **Manage** and **Add property** (BR-17, BR-18, BR-19). |
| FR-09 | The dashboard must show the quote awaiting action with **Review quote** and **Ask a question**, and the invoice with a balance due with **View invoice** (BR-20, BR-21). |
| FR-10 | The dashboard must show the upcoming appointment with schedule, technician, address and progress, with **View details** and **Request reschedule** (BR-22). |
| FR-11 | The dashboard must show active requests, the latest derived updates and recent activity, each with a working **View all** (BR-23 … BR-26). |
| FR-12 | Contacts must be able to submit a service request for their customer and a known or new property, reusing the public wizard rules (BR-28). |
| FR-13 | The Requests, Appointments, Quotes and Invoices modules must provide paginated lists and detail pages limited to the session customer (BR-27, BR-29, BR-30, BR-31). |
| FR-14 | Quote review and invoice view/payment inside the portal must reuse the existing quote approval and invoice payment behavior, authorized by the session and resource id, never by new public tokens (BR-30, BR-31). |
| FR-15 | Contacts must be able to request a reschedule of an eligible visit. The system stores the request and emails the organization without changing the visit (BR-32). |
| FR-16 | Contacts must be able to add properties and edit name, access instructions and primary flag, never the address (BR-33). |
| FR-17 | Contacts must be able to send a message that emails the organization after commit (BR-35). |
| FR-18 | Public request, quote and invoice pages must link to the portal sign-in without turning tokens into sessions (BR-36). |
| FR-19 | Every portal read and write must be limited to the session organization and customer. Foreign or unknown ids must return one identical `404` (BR-37). |
| FR-20 | Every portal page and card must implement the loading, empty, error, session-ended, not-found and responsive states of UI behavior and states. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Separate session. Portal scheme cookie `fieldops_portal_session`: HttpOnly, Secure, SameSite=Strict, Path `/`, no Domain, Data Protection protected. Same lifetimes as the internal session (8 h browser session; Remember me 14 days sliding, 30 days absolute). Claims: `userId`, `contactId`, `customerId`, `organizationId`, `signedInAt`, `rememberMe`. Every `/portal/*` endpoint except the anonymous ones in API contracts authenticates only with this scheme. Every internal endpoint ignores it. An internal cookie never authenticates a portal endpoint. Both cookies may coexist in one browser. | Backend |
| BR-02 | Active link. A link is active when `customer_contacts.portal_user_id` = the user, the contact `is_active`, its customer `is_active` and the organization `is_active`. A user has at most one link per organization (SA-20). | Backend |
| BR-03 | Sign-in. `POST /portal/sessions { email, password, rememberMe }`. Email is normalized like internal sign-in. Success requires the password to verify (existing PBKDF2 hasher, dummy hash for unknown emails), user `status = active`, and ≥ 1 active link. Any failure (unknown email, wrong password, non-active user, no active link) returns the same `401`. Field validation, rate limits and the per-email failure counter are the internal sign-in rules, sharing the same counters. Success sets `last_login_at`, writes audit `portal.signed_in` (organization of the chosen link, `actor_user_id` = user, `entity_type` `customer_contact`, no email), and issues the cookie. | Backend |
| BR-04 | Link selection at sign-in: the active link with the oldest `portal_linked_at`, then lowest contact `id`. | Backend |
| BR-05 | `PortalSession` = `{ user: { firstName, lastName, email }, account: { contactId, customerName, organizationName, hasLogo }, accounts: [{ contactId, customerName, organizationName }] }`. `accounts` lists every active link in BR-04 order. The contact first name for the greeting is `user.firstName`. No organization or customer id appears except `contactId` of the user's own links. | Backend |
| BR-06 | Revalidation on every portal request: ticket readable and within BR-01 lifetime; the user exists with `status = active`; `password_changed_at` is null or ≤ `signedInAt`; the claimed contact is an active link of the user with the claimed customer and organization. Any failure → `401`, `Cache-Control: no-store`, cookie deleted, no redirect. The internal session already applies the `password_changed_at` rule (existing behavior, unchanged). | Backend |
| BR-07 | Switch account. `POST /portal/sessions/current/account { contactId }`. The contact must be one of the caller's active links, else `404` `portal_account_unavailable`. Success reissues `fieldops_portal_session` with the selected contact, customer and organization, keeping the original `signedInAt` and `rememberMe`. It returns `200 PortalSession` and writes audit `portal.account_switched` in the target organization. **Switch account** is shown only when `accounts` has ≥ 2 entries. After a switch the portal reloads Home with the selector reset. | Both |
| BR-08 | Sign-out: `DELETE /portal/sessions/current` → `204`, deletes only the portal cookie. The internal session is untouched. | Backend |
| BR-09 | Portal CSRF and transport follow the internal precedent: SameSite=Strict, JSON or multipart only, configured CORS origins with credentials. Responses are `Cache-Control: no-store`. Logs never contain emails, passwords, tokens, cookies, free text or addresses. | Backend |
| BR-10 | Portal status of a customer's primary contact (`GET /customers/{id}/detail` adds `contact.portalStatus`): `active` when the contact has `portal_user_id` set (with `portalLinkedOn`); else `invited` when an open invitation exists (not accepted, not revoked, unexpired), with `invitationExpiresOn`; else `not_invited`. The Contact details card shows "Portal access" with "Active since <date>", "Invitation sent · expires <date>" or "Not invited". | Both |
| BR-11 | Invite. `POST /customers/{id}/portal-invitation` (Manage). Requires the customer and its primary contact active, a contact email, and `portalStatus` ≠ `active`. Otherwise `409` `portal_invite_unavailable`. In one transaction: revoke any open invitation of the contact, insert a `customer_portal_invitations` row (32 random bytes base64url, SHA-256 hex stored, `email` = lowercased contact email, `expires_at` = now + 7 days), and write audit `portal.invitation_sent` (no email). After commit, email the contact: subject "<organization name> invited you to your customer portal", body with first name, organization name, the link `<first allowed frontend origin>/portal/activate#token=<raw>`, and the expiry date. A send failure is logged by category only and the response stays `200` with the BR-10 status. **Invite to portal** (not invited) and **Resend invitation** (invited) call this endpoint. | Both |
| BR-12 | Invitation validation. `POST /portal/invitations/validate { token }` returns `200 { organizationName, firstName, email, accountExists, lastNameRequired }` when the token is usable. `lastNameRequired` is true when the contact's `last_name` is null or blank. Usable means: hash found, not accepted, not revoked, unexpired, contact active and still the customer's primary contact with `lower(email)` = invitation email, `portal_user_id` null, customer and organization active. Every other case returns one identical `410` `portal_invitation_unavailable`. `accountExists` is true when a user with that email exists. Token transport follows the invitation precedent: the fragment is captured into tab memory, the URL is replaced by `/portal/activate`, and the token is sent only in POST bodies. | Both |
| BR-13 | Activation. `POST /portal/invitations/accept { token, password, lastName }` when no user has the email: password rules of the existing invitation acceptance. When the contact's `last_name` is null or blank, the page shows a required **Last name** field and `lastName` is required: 1–100 characters after trim (the `users.last_name` rule), else `400` `errors.lastName` "Enter your last name." / "Last name must be 100 characters or fewer.". Otherwise `lastName` is ignored. Creates `users` (contact first name, the contact last name or the given `lastName`, email, `status = active`, `email_verified_at` = now, PBKDF2 hash) and, when it was required, stores the same trimmed `lastName` on the contact in the same transaction. `POST /portal/invitations/accept-existing { token, password }` when a user exists: verifies that user's password (wrong → `401` generic, counted by the sign-in throttle). It never asks for or changes a last name: the existing user keeps theirs, and a contact without a last name stays unchanged. User not `active`, or the user already linked to another contact of the organization → `409` `portal_invitation_ineligible`. Both run in one transaction locking the invitation (`FOR UPDATE`): recheck BR-12, set `portal_user_id`, `portal_linked_at` = now, `accepted_at`, audit `portal.access_activated`. They never create a second user, and never change `organization_users`, `users.status` or other links. They return `200 PortalSession` with a new portal cookie for this link (`rememberMe = false`), issued only after commit. Rate limit: 20 per IP per 5 minutes across the three endpoints. | Both |
| BR-14 | Remove portal access. `DELETE /customers/{id}/portal-access` (Manage) on the primary contact. In one transaction: set `portal_user_id` and `portal_linked_at` to null, revoke open invitations, write audit `portal.access_removed`. Returns `200` with the BR-10 status. Idempotent when already `not_invited`. It does not delete or change the user, its internal memberships or its links in other organizations. The next request of an affected portal session fails BR-06. The card shows **Remove portal access** with confirmation "Remove portal access? <name> will be signed out of the portal." | Both |
| BR-15 | Password recovery. `POST /portal/password-resets { email }` follows the existing `/password-resets` rules exactly (neutral `202`, limits shared with the internal endpoint, token table, 30 min, single use, email after commit). Eligibility additionally requires ≥ 1 active link. The email link is `<origin>/portal/reset-password#token=<raw>`. The portal page validates and confirms through the existing `/password-resets/validate` and `/password-resets/confirm`. On success it shows "Your password was changed." with **Sign in** to `/portal/sign-in`. "Forgot password?" on portal sign-in opens `/portal/forgot-password`. | Both |
| BR-16 | Shell. Header: organization logo (or initials) and organization name; navigation Home `/portal`, Requests `/portal/requests`, Appointments `/portal/appointments`, Quotes `/portal/quotes`, Invoices `/portal/invoices`, with the current section marked; **Help** navigates to the Need help card on Home; the bell shows the BR-25 unread count (hidden at 0, "9+" above 9) and opens `/portal/updates`; the user menu shows avatar initials and full name, **Switch account** (BR-07) and **Sign out**. No item leads to "Coming soon" or an internal route. | Frontend |
| BR-17 | Greeting. "Good morning, <firstName>" (00:00–11:59), "Good afternoon, <firstName>" (12:00–17:59), "Good evening, <firstName>" (18:00–23:59), using the browser local time. Subtitle "Here's what's happening with your services.". | Frontend |
| BR-18 | Property selector. Options: each active property of the customer as "<name> — <address line 1>", primary first, then by name. When there are ≥ 2 properties, "All properties" is added first. Default: the primary property, else the first option. The selection is passed as `propertyId` (absent for All). It scopes the quote, invoice, appointment, requests and activity cards: an item belongs to a property through `service_requests.property_id`, `quotes.property_id`, or the work order `property_id` for visits, invoices and activity. Updates and the property card ignore it. **Request a service** opens `/portal/requests/new` with the selected property preselected. A `propertyId` that is not an active property of the session customer → `404`. | Both |
| BR-19 | Your property card: the selected property, or with All the primary (else first). Name, "Primary" tag when primary, address line 1, "City, ST ZIP", "Last service: <date>" (the latest `actual_completed_at` date, organization time zone, of a visit in `completed`, `needs_correction` or `approved` on a work order of that property; hidden when none). **Manage** → `/portal/properties`; **Add property** → `/portal/properties/new`. With no active properties: "No properties yet." and **Add property**. | Both |
| BR-20 | Action required card. Candidates: quotes of the customer in `sent` or `clarification_requested` whose current version `valid_until` (end of day, organization time zone) has not passed or is null. Pick the earliest `valid_until` (nulls last), then the newest `sent_at`. Shows "Action required", "Quote ready", the version `scope`, "<Q-n>", "Total <version total>", "Expires <valid_until>" (hidden when null), and the notice "Approving this quote does not confirm an appointment. Our team will contact you to schedule the work.". **Review quote** → `/portal/quotes/:id`. **Ask a question** opens the BR-30 question dialog and is replaced by "Question sent" when the quote is `clarification_requested`. With more candidates, "+<n> more quotes" links to `/portal/quotes`. Empty: "No quotes need your attention." | Both |
| BR-21 | Payment due card. Candidates: invoices of the customer in `sent`, `partially_paid` or `overdue` with `balance_due > 0`. Pick the earliest `due_date` (nulls last), then the lowest invoice number. Shows "Payment due", "Invoice <INV-n>", the work order title, the balance due, and "Due <date>". The date is styled and labelled "Overdue" when `status = overdue` or `due_date` < today (organization time zone). **View invoice** → `/portal/invoices/:id`. Extra candidates: "+<n> more invoices" → `/portal/invoices`. Empty: "You're all paid up." | Both |
| BR-22 | Upcoming appointment. Candidates: visits of non-cancelled work orders of the customer with `scheduled_start` set and status `scheduled`, `assigned`, `on_the_way`, `in_progress` or `paused`. Visits in `on_the_way`, `in_progress` or `paused` come first; then the earliest `scheduled_start` ≥ start of today (organization time zone). Shows the work order title, "Work order <WO-n>", date ("EEEE, MMM d, yyyy"), arrival window when set (else scheduled start–end) with caption "Arrival window" only for the window, technician full name of the active primary assignment with tag "Assigned" (else "Technician to be assigned"), property name and address line 1, and a progress stepper (BR-38). Info notice: before `on_the_way` with a technician, "We'll let you know when <technician first name> is on the way."; `on_the_way` "<first name> is on the way."; `in_progress`/`paused` "Work is in progress."; no technician: "We'll let you know once a technician is assigned.". **View details** → `/portal/appointments/:visitId`. **Request reschedule** follows BR-32 and shows "Reschedule requested" instead while a request is pending. Empty: "No upcoming appointments." with **Request a service**. | Both |
| BR-23 | Active requests card: requests of the customer in `new`, `needs_review`, `assessment_scheduled`, `ready_for_quote` or `quoted`, newest first, up to 3. Row: "<REQ-n>", title (catalog item name, else category name, else "Service request"), status chip (BR-39), "Submitted <date>", **View** → `/portal/requests/:id`. **View all** → `/portal/requests`. Empty: "No active requests." | Both |
| BR-24 | Updates feed (derived, read-only, not property-scoped), events of the session customer within the last 90 days, newest first. (a) quote sent: current version `sent_at` of quotes in any non-draft, non-cancelled status, "Quote <Q-n> ready", subtitle scope; (b) technician assigned: active primary `visit_assignments.assigned_at`, "<technician full name> assigned to <WO-n>", subtitle work order title; (c) on the way: `visit_status_history` to `on_the_way`, "<first name> is on the way", subtitle work order title; (d) visit completed: history to `completed`, "<work order title> completed", subtitle "<WO-n>"; (e) invoice sent: `invoices.sent_at` of non-void invoices, "Invoice <INV-n> sent", subtitle work order title; (f) receipt available: payments of the customer with `receipt_number` and `status` ≠ `refunded`, at `created_at`, "Receipt <receipt number> available", subtitle work order title of the first allocated invoice. Each event links to its quote, appointment, invoice or invoice page. Time label: relative under 24 h ("1h ago", "Just now" < 1 min), "Yesterday", else "MMM d, yyyy". Home shows 3 with **View all** → `/portal/updates` (up to 50). Empty: "No updates yet." | Both |
| BR-25 | Unread updates. `unreadCount` = BR-24 events with occurrence > `customer_contacts.portal_updates_seen_at` (all events when null). Opening `/portal/updates` calls `POST /portal/updates/seen`, which sets `portal_updates_seen_at` = now for the session contact and returns `204`. The bell then shows no count. Home shows a dot on events newer than the seen time. | Both |
| BR-26 | Recent activity: work orders of the customer in `completed` or `approved_for_billing`, ordered by their latest completed visit `actual_completed_at` desc. Home shows 5; `/portal/activity` is paginated (20). Columns: Service (work order title), Date (completion date), Reference, Amount, Status, Actions. With a non-void, non-draft invoice: Amount = invoice total. `paid` → Reference "<WO-n>", Status "Completed + Paid", actions **View report** and **Receipt** (latest payment with a receipt number allocated to the invoice; hidden when none). `sent`/`partially_paid`/`overdue` → Reference "<INV-n>", Status "Invoice due", action **View invoice**. Without such an invoice: Reference "<WO-n>", Amount "—", Status "Completed", action **View report**. Status chips use text and icon, never color alone. Empty: "No completed services yet." | Both |
| BR-27 | Requests module. `/portal/requests`: all requests of the customer (property-filterable with the BR-18 selector), newest first, 20 per page, columns number, title, property, status chip, submitted date, **View**. **Request a service** button. Detail `/portal/requests/:id`: number, title, category, service, description, urgency, active damage, property, preferred availability, submitted date, status chip, and the linked non-draft, non-cancelled quote ("<Q-n>" with status, linking to `/portal/quotes/:id`) when present. No attachments, internal notes, messages or assignees. | Both |
| BR-28 | New request. `/portal/requests/new` reuses the public wizard steps without the Contact step: Property, Service details, Availability, Review. The Property step offers the customer's active properties (preselected per BR-18) or **Add a new property** with the public address fields and rules. `GET /portal/service-request-form` returns the public form configuration of the session organization. When it has no requestable service, the page shows "Online requests aren't available right now. Call <phone>." and no form. `POST /portal/service-requests` (multipart, same part names, field rules, attachment rules, honeypot and body limit as the public endpoint) takes `property: { propertyId } \| { newProperty: { propertyType, addressLine1, addressLine2, city, state, postalCode, accessInstructions } }`. Customer and contact come from the session. A `propertyId` that is not an active property of the session customer → `400` `property.propertyId` "Select one of your properties.". A new property is created per public BR-10. The request stores `source = 'portal'`, the contact's name, email and phone as guest snapshot, the session `customer_id`/`contact_id`, status `new`, the next request number, history and audit (`actor_user_id` = portal user) in one transaction. The confirmation email follows public BR-19. Response `201 { requestId, requestNumber }`. The confirmation screen shows the number with **View request** and **Back to home**. Rate limit: 5 per user per 15 minutes, 100 global per hour. | Both |
| BR-29 | Appointments module. `/portal/appointments` has tabs **Upcoming** (BR-22 statuses, ascending) and **Past** (`completed`, `needs_correction`, `approved`, `cancelled` with `scheduled_start` set, descending), property-filterable, 20 per page. Columns: date, time, service, work order, technician, property, status (BR-38 label, or "Cancelled"). Detail `/portal/appointments/:visitId`: the BR-22 fields, the stepper, reschedule state and action, and for completed visits **View report**. Never internal notes, dispatch notes, checklist, materials or time entries. | Both |
| BR-30 | Quotes module. `/portal/quotes`: quotes of the customer in `sent`, `clarification_requested`, `approved`, `rejected` or `expired`, newest `sent_at` first, property-filterable, 20 per page. Columns: number, scope, property, total, status (`sent` "Awaiting your approval", `clarification_requested` "Question sent", `approved` "Approved", `rejected` "Declined", expired per below "Expired"), valid until, **Review**. `/portal/quotes/:id` renders the existing public quote content and actions (customer-quote-approval BR-05 … BR-17, BR-19), inside the portal shell, for the quote's current sent version (the approved version once approved), through `/portal/quotes/{id}/…` endpoints. Responder data: `responder_name` = contact full name, `responder_contact_id` = session contact. Audit rows are the same actions with `actor_user_id` = portal user and metadata `channel: "portal"`. Expiry: a quote in `sent` or `clarification_requested` whose current version `valid_until` has passed (organization time zone) is shown read-only as "Expired" with "This quote has expired. Contact <organization name> for an updated quote." Its actions return `409` `quote_expired`. The portal never writes the `expired` status. `draft`, `cancelled` and other customers' quotes → `404`. No public token is created, read or revoked. | Both |
| BR-31 | Invoices module. `/portal/invoices`: invoices of the customer except `draft` and `void`, newest issue date first, property-filterable, 20 per page. Columns: number, service, issue date, due date, total, balance due, status chip. `/portal/invoices/:id` renders the existing public invoice and payment experience (customer-invoice-payments: view, PDF, card payment, status polling, bank transfer notice, cash, receipt, completion report, photos, review) inside the portal shell, through `/portal/invoices/{id}/…` endpoints with the same rules, idempotency, webhook reconciliation and limits keyed by user. Draft, void and other customers' invoices → `404`. No public token is created, read or revoked. **View report** for work orders without an invoice uses `GET /portal/work-orders/{id}/completion-report` (same PDF as the public completion report; available when the work order is `completed` or `approved_for_billing`, else `404`). | Both |
| BR-32 | Reschedule request. Eligible when the visit belongs to the customer, its status is `scheduled` or `assigned`, `scheduled_start` > now, and no pending request exists. Pending means a `visit_reschedule_requests` row whose `original_scheduled_start` equals the visit's current `scheduled_start`. Dialog "Request a reschedule": **Preferred date** (tomorrow … today + 90 days, organization time zone), **Time window** (`morning`, `afternoon`, `evening`, `any`, public BR-07 bounds), **Reason** (1–500 characters after trim). `POST /portal/appointments/{visitId}/reschedule-requests` locks the visit, rechecks eligibility, inserts the row with the current `scheduled_start`, writes audit `visit.reschedule_requested` (no reason text), and returns `201 { requestedOn }`. After commit, it emails the organization `email` (else the main branch email; when neither exists, it logs the category only): subject "Reschedule requested for <WO-n>", body with customer name, work order, current date and window, preferred date and window, and reason. The visit is never changed. Errors: not eligible → `409` `reschedule_not_available`; pending → `409` `reschedule_already_requested`. The appointment then shows "Reschedule requested · <date>" until the visit's `scheduled_start` changes. | Both |
| BR-33 | Properties. `/portal/properties` lists active properties with primary tag, address, last service and **Edit**. `/portal/properties/new` (**Add property**): `name` 1–140, `addressLine1`, `addressLine2`, `city`, `stateRegion`, `postalCode` per public BR-04 and its field table (`stateRegion` takes the public `state` rule), `countryCode` must be `US`, `accessInstructions` ≤ 1000. `POST /portal/properties` creates `branch_id` null and sets `is_primary` only when the customer has no active primary property. Edit (`PATCH /portal/properties/{propertyId}`) accepts only `name` (1–140), `accessInstructions` (≤ 1000, null clears), `isPrimary` (`true` makes it primary and clears the previous primary in the same transaction; `false` or absent changes nothing) and the required `updatedAt`. A `updatedAt` different from the stored value → `409` `property_changed` "This property changed. Reload and try again.". Address fields are displayed read-only with "To change this address, contact <organization name>.", and any other field in the body (address included) → `400`. Rate limit for creation: 10 per user per 15 minutes. Audit `property.created` / `property.updated` with `actor_user_id` = portal user and no address text. | Both |
| BR-34 | Pagination and dates. Lists accept `page` ≥ 1 and return `{ items, page, pageSize: 20, total }`. Dates are `YYYY-MM-DD` and times `HH:mm` in the organization time zone, computed by the server. Money uses 2 decimals with the invoice or quote currency. | Both |
| BR-35 | Need help and Send a message. Card "Need help?" "We're here for you." with **Call <phone>** (`tel:`; phone of the customer's branch, else the organization; hidden when none) and **Send a message**. The dialog has **Message** 1–1000 characters after trim. `POST /portal/messages { message }` writes audit `portal.message_sent` (length only), then, after commit, emails the organization email (else the main branch email) with reply-to the contact email, subject "Message from <customer name>", body with contact name, email, phone and the message. Response `202`. Not configured (no organization or main branch email) → **Send a message** hidden and the endpoint returns `409` `messaging_unavailable`. A send failure is logged by category. Rate limit: 5 per user per 15 minutes. Success: "Your message was sent. <organization name> will reply by email or phone." | Both |
| BR-36 | Public links. The public request form header "Sign in", the public quote page and the public invoice page show "Sign in to your portal" → `/portal/sign-in`. Nothing is sent with the link. A public token never creates or extends a session. | Frontend |
| BR-37 | Isolation. Every portal query is filtered by session `organization_id` and `customer_id`. Path or body ids (`propertyId`, request, visit, quote, invoice, work order, payment, attempt, photo, contact) are verified against them. Unknown ids, ids of another customer in the same organization, and ids of another organization return one identical `404` `portal_resource_unavailable` with no foreign data. Only `contactId` (BR-07) and documented ids of the customer's own resources appear in responses. | Backend |
| BR-38 | Progress stepper: Scheduled (`scheduled`, `assigned`), On the way (`on_the_way`), In progress (`in_progress`, `paused`), Completed (`completed`, `needs_correction`, `approved`). Past steps show complete, the current step is marked current, and states are conveyed by text and icon, not color alone. | Both |
| BR-39 | Customer-facing request status: `new` "Submitted", `needs_review` "Under review", `assessment_scheduled` "Assessment scheduled", `ready_for_quote` "Preparing quote", `quoted` "Quote ready", `converted` "Job created", `cancelled` "Cancelled". | Both |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Not invited | Invited | Invite to portal | Owner, Dispatcher | BR-11 |
| Invited | Invited (new token) | Resend invitation | Owner, Dispatcher | BR-11; previous token revoked |
| Invited | Active | Activation (new or existing user) | Invited contact | BR-12, BR-13 |
| Invited, Active | Not invited | Remove portal access | Owner, Dispatcher | BR-14 |
| Portal session | Ended | Sign-out, BR-06 failure, switch reissue | Contact, system | BR-06 … BR-08 |
| Reschedule — | Pending | Reschedule request | Contact | BR-32 |
| Reschedule pending | Superseded | Visit `scheduled_start` changes | Staff (existing flows) | Derived, no write |

Quote and invoice transitions are those of `customer-quote-approval` and
`customer-invoice-payments`, unchanged, plus BR-30's `409 quote_expired`.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `users`: read; insert on activation; `last_login_at` on sign-in;
    `password_hash`/`password_changed_at` via the existing reset.
  - `customer_contacts`: read; write `portal_user_id`, `portal_linked_at`,
    `portal_updates_seen_at`, and `last_name` only on BR-13 activation when
    it was missing.
  - `customers`, `organizations`, `organization_logos`, `branches`: read.
  - `properties`: read; insert and update (`name`, `access_instructions`,
    `is_primary`).
  - `service_requests`, `request_attachments`, `request_status_history`:
    read; insert (BR-28).
  - `service_categories`, `catalog_items`: read.
  - `quotes`, `quote_versions`, `quote_lines`, `quote_responses`,
    `quote_response_optional_lines`, `assessments`, `assessment_attachments`:
    as `customer-quote-approval`.
  - `work_orders`, `visits`, `visit_assignments`, `visit_status_history`,
    `technician_profiles`, `visit_evidence`, `customer_signoffs`: read.
  - `invoices`, `invoice_lines`, `payments`, `payment_allocations`,
    `invoice_payment_attempts`, `invoice_reviews`: as
    `customer-invoice-payments`.
  - `password_reset_tokens`: as `password-recovery`.
  - `audit_logs`: insert.
  - Not written: `notifications`, `organization_users`,
    `quote_access_tokens`, `invoice_access_tokens`, `visits`.
- Schema amendments required (**confirmed by the user 2026-10-10**, OD-14;
  implementation updates
  `docs/database/fieldops-schema.sql` first and generates one migration under
  `--generate-migration`, never applied; mapping reviewed by
  `database-reviewer`):
  - **SA-19** `customer_contacts`: add `portal_linked_at timestamptz`, add
    `portal_updates_seen_at timestamptz`, add
    `CONSTRAINT ck_customer_contacts_portal_link CHECK ((portal_user_id IS NULL) = (portal_linked_at IS NULL))`.
    Existing rows have null `portal_user_id`, so no backfill is needed. A
    migration over rows with `portal_user_id` set fails instead of inventing
    values.
  - **SA-20** `CREATE UNIQUE INDEX ux_customer_contacts_org_portal_user ON customer_contacts(organization_id, portal_user_id) WHERE portal_user_id IS NOT NULL;`
  - **SA-21** New tables:
    - `customer_portal_invitations (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), contact_id uuid NOT NULL, email varchar(254) NOT NULL, token_hash text NOT NULL UNIQUE, invited_by_user_id uuid NOT NULL REFERENCES users(id), expires_at timestamptz NOT NULL, accepted_at timestamptz, revoked_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts(organization_id, id), CONSTRAINT ck_customer_portal_invitations_expires_after_created CHECK (expires_at > created_at))`
      with `CREATE UNIQUE INDEX ux_customer_portal_invitations_open ON customer_portal_invitations(contact_id) WHERE accepted_at IS NULL AND revoked_at IS NULL;`
    - `visit_reschedule_requests (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), visit_id uuid NOT NULL, contact_id uuid NOT NULL, original_scheduled_start timestamptz NOT NULL, preferred_date date NOT NULL, time_window varchar(20) NOT NULL CHECK (time_window IN ('morning','afternoon','evening','any')), reason varchar(500) NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY (organization_id, visit_id) REFERENCES visits(organization_id, id), FOREIGN KEY (organization_id, contact_id) REFERENCES customer_contacts(organization_id, id))`
      with `CREATE UNIQUE INDEX ux_visit_reschedule_requests_pending ON visit_reschedule_requests(visit_id, original_scheduled_start);`
  - `service_requests.source` takes the new value `portal` (varchar, no
    constraint change).
- Indexes relied on: `ix_requests_customer`, `ix_properties_customer`,
  `ix_payments_customer_date`, `ix_invoices_status_due`, the composite
  `(organization_id, id)` keys. Dashboard queries must stay bounded per
  customer (no organization-wide scans per request). See the
  `pending-payments-list-regression` risk in Dependencies.

## Tenant isolation and authorization

- Organization context: resolved server-side only from the validated portal
  ticket, rechecked by BR-06. Customer and contact come from the same
  ticket. No portal request body or header carries an organization or
  customer id. Any such field is ignored.
- Client-provided identifiers: `contactId` (switch account; must be the
  caller's own active link) and resource ids per BR-37, always verified
  against the session organization and customer.
- Another customer's resource (same or other organization): identical `404`
  `portal_resource_unavailable`.
- Cookie separation: internal cookie on `/portal/*` → `401`; portal cookie
  on internal endpoints → `401` (unauthenticated).
- Invitations: organization and contact come only from the token row.
  Invalid tokens are indistinguishable (`410`).
- Staff endpoints (BR-10, BR-11, BR-14) use the existing customer policies
  (View/Manage), branch scope and cross-organization `404`.
- Webhook authorization is unchanged (Stripe signature).

## API contracts

Contract status: Final. Errors use ProblemDetails with `code` on `401`
(none), `404`, `409`, `410`; `400` carries field `errors`. Portal JSON
responses are `no-store`. Permission column: **Portal** = valid portal
session (BR-06); **Anon** = anonymous.

`PortalSession` per BR-05. `Page<T>` = `{ items: T[], page, pageSize, total }`.
`Property` = `{ id, name, addressLine1, addressLine2 | null, city,
stateRegion, postalCode, countryCode, accessInstructions | null, isPrimary,
lastServiceOn | null, updatedAt }`. `lastServiceOn` (BR-19) and `updatedAt`
are read-only. `updatedAt` is the concurrency value echoed by `PATCH`.
`RequestProperty` = `Property` without `lastServiceOn` and `updatedAt`, or
`null` when the request has no property. `RequestAvailability` = `{ mode:
asap | date | flexible, dates: [{ date: YYYY-MM-DD, window: morning |
afternoon | evening | any }], window: morning | afternoon | evening | any,
notes: string | null } | null`. It maps the stored public availability exactly
(public BR-07): `mode` = `dateMode`. `dates` holds exactly one element
`{ date: preferredDate, window }` only when `mode = date` and is empty for
`asap` and `flexible`. `window` = `timeWindow` and is always present.
`notes` = `schedulingNotes`. It is `null` only when the request has no stored
availability. The public form is not extended to several dates.

Resource identifiers travel only in the path (`quoteId`, `invoiceId`,
`photoId`, `paymentId`, `attemptId`, `visitId`, `requestId`, `propertyId` of
`PATCH`). The rule applies only to the primary identifier of the addressed
resource (OD-15). List filters (`propertyId`, `page`, `scope`) travel in the
query string. Command data (`selectedOptionalLineIds`, the new-request
`property.propertyId`, `idempotencyKey`) travels in the body. Portal bodies
never carry `token`. `GET` reads or downloads; `POST` performs an action. Portal
quote and invoice endpoints reuse the public use cases and keep their rules,
rate-limit groups (keyed by user) and headers, unless this table states
otherwise.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/portal/sessions` | `{ email, password, rememberMe }` | `200 PortalSession` + cookie | `400` · `401` · `429` | Anon |
| GET | `/portal/sessions/current` | — | `200 PortalSession` | `401` | Portal |
| DELETE | `/portal/sessions/current` | — | `204` | — | Anon (clears cookie) |
| POST | `/portal/sessions/current/account` | `{ contactId }` | `200 PortalSession` + reissued cookie | `401` · `404 portal_account_unavailable` | Portal |
| POST | `/portal/invitations/validate` | `{ token }` | `200 { organizationName, firstName, email, accountExists, lastNameRequired }` | `410 portal_invitation_unavailable` · `429` | Anon |
| POST | `/portal/invitations/accept` | `{ token, password, lastName }` (`lastName` only when required) | `200 PortalSession` + cookie | `400` · `409 portal_invitation_ineligible` · `410` · `429` | Anon |
| POST | `/portal/invitations/accept-existing` | `{ token, password }` | `200 PortalSession` + cookie | `400` · `401` · `409` · `410` · `429` | Anon |
| POST | `/portal/password-resets` | `{ email }` | `202` | `429` | Anon |
| GET | `/portal/dashboard?propertyId=` | — | `200 { organization: { name, phone \| null, canReceiveMessages }, properties: [{ id, name, addressLine1, addressLine2 \| null, city, state, postalCode, isPrimary, lastServiceOn \| null }], selectedPropertyId \| null, actionQuote: { id, displayNumber, scope, total, currency, validUntil \| null, status, moreCount } \| null, paymentDue: { id, displayNumber, title, balanceDue, currency, dueDate \| null, isOverdue, moreCount } \| null, upcomingAppointment: Appointment \| null, activeRequests: [RequestRow], updates: { items: [Update], unreadCount }, recentActivity: [ActivityRow] }` | `401` · `404` | Portal |
| GET | `/portal/requests?propertyId=&page=` | — | `200 Page<RequestRow>`; `RequestRow` = `{ id, displayNumber, title, propertyName \| null, status, statusLabel, submittedOn }` | `400` · `401` · `404` | Portal |
| GET | `/portal/requests/{requestId}` | — | `200 { …RequestRow, categoryName, serviceName \| null, description, urgency, hasActiveDamage, property: RequestProperty, availability: RequestAvailability, quote: { id, displayNumber, status } \| null }` | `401` · `404` | Portal |
| GET | `/portal/service-request-form` | — | `200` public form configuration shape | `401` · `404 requests_unavailable` | Portal |
| POST | `/portal/service-requests` | multipart per BR-28 | `201 { requestId, requestNumber }` | `400` · `401` · `404` · `413` · `415` · `429` | Portal |
| GET | `/portal/appointments?scope=upcoming\|past&propertyId=&page=` | — | `200 Page<Appointment>`; `Appointment` = `{ visitId, workOrderId, workOrderNumber, title, date, startTime, endTime, arrivalWindow: { start, end } \| null, technician: { fullName, firstName } \| null, property: { name, addressLine1 }, status, progressStep, canRequestReschedule, rescheduleRequestedOn \| null, reportAvailable }` | `400` · `401` · `404` | Portal |
| GET | `/portal/appointments/{visitId}` | — | `200 Appointment` | `401` · `404` | Portal |
| POST | `/portal/appointments/{visitId}/reschedule-requests` | `{ preferredDate, timeWindow, reason }` | `201 { requestedOn }` | `400` · `401` · `404` · `409 reschedule_not_available`, `reschedule_already_requested` · `429` | Portal |
| GET | `/portal/quotes?propertyId=&page=` | — | `200 Page<{ id, displayNumber, scope, propertyName \| null, total, currency, status, validUntil \| null }>` (`status` includes derived `expired`) | `400` · `401` · `404` | Portal |
| GET | `/portal/quotes/{quoteId}` | — | `200 PublicQuote`; `status` may also be `expired` (read-only, no actions) | `401` · `404 portal_resource_unavailable` | Portal |
| GET | `/portal/quotes/{quoteId}/pdf` | — | `200 application/pdf` attachment (customer-quote-approval BR-19) | `401` · `404` · `429` | Portal |
| GET | `/portal/quotes/{quoteId}/photos` | — | `200 [{ photoId }]` (the customer-quote-approval BR-07 list) | `401` · `404` · `429` | Portal |
| GET | `/portal/quotes/{quoteId}/photos/{photoId}/content` | — | `200` image (customer-quote-approval BR-07 headers) | `401` · `404` · `429` | Portal |
| POST | `/portal/quotes/{quoteId}/approve` | `{ selectedOptionalLineIds, acceptTerms }` | `200 PublicQuote` | `400` (`acceptTerms`, `selectedOptionalLineIds`) · `401` · `404` · `409 quote_already_answered`, `quote_expired` · `429` | Portal |
| POST | `/portal/quotes/{quoteId}/reject` | `{ reason }` | `200 PublicQuote` (decline use case) | `400 reason` · `401` · `404` · `409 quote_already_answered`, `quote_expired` · `429` | Portal |
| POST | `/portal/quotes/{quoteId}/clarification` | `{ message }` | `200 PublicQuote` | `400 message` · `401` · `404` · `409 quote_already_answered`, `quote_expired` · `429` | Portal |
| POST | `/portal/quotes/{quoteId}/calculate` | `{ selectedOptionalLineIds }` | `200 Totals` | `400 selectedOptionalLineIds` · `401` · `404` · `429` | Portal |
| GET | `/portal/invoices?propertyId=&page=` | — | `200 Page<{ id, displayNumber, title, issueDate, dueDate \| null, total, balanceDue, currency, status }>` | `400` · `401` · `404` | Portal |
| GET | `/portal/invoices/{invoiceId}` | — | `200 PublicInvoice` (customer-invoice-payments BR-03) | `401` · `404 portal_resource_unavailable` | Portal |
| GET | `/portal/invoices/{invoiceId}/pdf` | — | `200 application/pdf` | `401` · `404` · `429` | Portal |
| GET | `/portal/invoices/{invoiceId}/payments/{paymentId}/receipt` | — | `200 application/pdf` | `401` · `404` · `429` | Portal |
| GET | `/portal/invoices/{invoiceId}/completion-report` | — | `200 application/pdf` | `401` · `404` · `429` | Portal |
| GET | `/portal/invoices/{invoiceId}/photos` | — | `200 [{ photoId, type, caption, takenOn }]` | `401` · `404` · `429` | Portal |
| GET | `/portal/invoices/{invoiceId}/photos/{photoId}/content` | — | `200 image/jpeg\|image/png` | `401` · `404` · `429` | Portal |
| POST | `/portal/invoices/{invoiceId}/payment-attempts` | `{ idempotencyKey }` | `201`/`200 { attemptId, clientSecret \| null, amount, currency, status }` (public card-intent use case and replay) | `400` · `401` · `404` · `409 invoice_not_payable`, `card_unavailable`, `payment_in_progress`, `idempotency_conflict` · `429` · `502 payment_provider_unavailable` | Portal |
| GET | `/portal/invoices/{invoiceId}/payment-attempts/{attemptId}` | — | `200` public payment status body (customer-invoice-payments BR-16) | `401` · `404` · `429` | Portal |
| POST | `/portal/invoices/{invoiceId}/bank-transfer-notice` | `{ idempotencyKey }` | `201`/`200 { changed, reportedOn }` | `400` · `401` · `404` · `409 invoice_not_payable`, `bank_transfer_unavailable` · `429` | Portal |
| POST | `/portal/invoices/{invoiceId}/review` | `{ rating, comment }` | `201 { submitted: true }` | `400` · `401` · `404` · `409 review_not_available`, `review_exists` · `429` | Portal |
| GET | `/portal/work-orders/{id}/completion-report` | — | `200 application/pdf` | `401` · `404` | Portal |
| GET | `/portal/organization/logo` | — | `200` image | `401` · `404` | Portal |
| GET | `/portal/properties` | — | `200 [Property]` (active properties, primary first, then by name) | `401` | Portal |
| POST | `/portal/properties` | `{ name, addressLine1, addressLine2, city, stateRegion, postalCode, countryCode, accessInstructions }` | `201 Property` | `400` · `401` · `429` | Portal |
| PATCH | `/portal/properties/{propertyId}` | `{ name, accessInstructions, isPrimary, updatedAt }` | `200 Property` | `400` · `401` · `404` · `409 property_changed` | Portal |
| GET | `/portal/updates` | — | `200 { items: [Update], unreadCount }`; `Update` = `{ type, title, subtitle, occurredAt, isUnread, target: { kind: quote\|appointment\|invoice, id } }` | `401` | Portal |
| POST | `/portal/updates/seen` | — | `204` | `401` | Portal |
| GET | `/portal/activity?page=&propertyId=` | — | `200 Page<ActivityRow>`; `ActivityRow` = `{ workOrderId, title, completedOn, reference, amount \| null, currency, status: completed\|paid\|invoice_due, invoiceId \| null, receiptPaymentId \| null }` | `400` · `401` · `404` | Portal |
| POST | `/portal/messages` | `{ message }` | `202` | `400` · `401` · `409 messaging_unavailable` · `429` | Portal |
| GET | `/customers/{id}/detail` | — | Adds `contact.portalStatus`, `portalLinkedOn \| null`, `invitationExpiresOn \| null` | Unchanged | View (unchanged) |
| POST | `/customers/{id}/portal-invitation` | — | `200 { portalStatus, portalLinkedOn, invitationExpiresOn }` | `403` · `404` · `409 portal_invite_unavailable` | Manage |
| DELETE | `/customers/{id}/portal-access` | — | `200` same body | `403` · `404` | Manage |

- The existing `/password-resets/validate` and `/password-resets/confirm`
  are reused unchanged.
- Internal contracts: the quote approval and invoice payment use cases are
  invoked through an access resolver that yields the same resolved
  quote/invoice context from either a token (public) or a portal session plus
  id (portal). Public behavior is unchanged. `IEmailSender` is reused.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Portal sign-in, forgot/reset password | Submit button busy | — | Internal sign-in and password-recovery messages; `429` with minutes | — | Home; reset confirmation with **Sign in** |
| Activation `/portal/activate` | Validating skeleton | — | `410`: "This invitation isn't available. Ask <organization> to send a new one." (organization unknown → "Ask the company to send a new one."); `401` "The password is incorrect."; `409` "This account can't be linked. Contact the company."; other "We couldn't activate your access. Try again." | — | Home |
| Shell | Session check before render | — | — | `401` anywhere → `/portal/sign-in` with "Your session ended. Sign in again." | — |
| Home | Per-card skeletons in the Design 28 layout | Per BR-19 … BR-26 | Page alert "We couldn't load your dashboard." with **Try again**; selector kept | `404` selector property → reset to default | Cards per BR-17 … BR-26 |
| Lists (Requests, Appointments, Quotes, Invoices, Activity, Updates, Properties) | Table skeleton | "No <items> yet." (requests and appointments include **Request a service**) | Inline error with **Try again** | — | Paginated table |
| Detail pages (request, appointment, quote, invoice) | Skeleton | — | Inline error with **Try again** | `404` → "This item isn't available." with **Back to <module>** | Content and actions per BR-27 … BR-31 |
| Dialogs (Ask a question, Reschedule, Send a message, Remove access) | Submit busy, single submission | — | Field errors; `409` messages; "Something went wrong. Try again." | — | Confirmation message, card state updated |
| New request, property forms | Submit busy | — | Field errors; `429`; generic | — | Confirmation / back to list |
| Staff Contact details card | Card skeleton (existing) | — | "We couldn't update portal access. Try again." | Actions hidden for read roles | Status line updated |

- Mockups: `design/assets/28-design.png` (Approved, mandatory for the
  dashboard and shell). Module lists, detail pages, sign-in, activation,
  properties and dialogs have no mockup. They follow the Design 28 header,
  cards and typography with existing FieldOps/PrimeNG patterns, and this
  spec's text is the reference, accepted by the user 2026-10-10; they reuse
  the Design 28 visual system and existing components, and `ui-designer` is
  consulted only on a real conflict. Quote and invoice detail reuse the approved
  Designs 4 and the customer-invoice-payments designs inside the portal
  shell, replacing their public header. Mockup data are examples.
- Design deviations: the FieldOps logo is replaced by the organization logo
  or initials and name; the bell opens the Updates page (no dropdown);
  "Help" scrolls to Need help; card overflow links "+<n> more" are added.
- Responsive: ≥ 1280 px matches Design 28 (three columns: quote/payment/
  property; appointment/requests/updates; activity/help). 768–1279 px: two
  columns, property and help after the primary cards. < 768 px: one column
  in the order greeting, selector, **Request a service**, action quote,
  payment due, appointment, requests, updates, property, activity, help. The
  navigation collapses into a menu button with a drawer, and the activity
  table becomes stacked rows. No horizontal page scroll at 375 px; touch
  targets ≥ 44 × 44 px.
- Accessibility: the selector, stepper, chips and unread dots expose text
  (not color alone); the bell has an accessible label with the count;
  dialogs trap focus and return it; loading and alerts are announced
  (`aria-live` polite).

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Wrong credentials, no active link, inactive user | `401` generic sign-in message | Failure counter |
| Session revoked (BR-06) | `401`, cookie deleted, sign-in with "Your session ended. Sign in again." | None |
| Foreign or unknown resource id | `404 portal_resource_unavailable`, "This item isn't available." | None |
| Invitation unusable | `410`, activation unavailable message | None |
| Existing account wrong password | `401`, "The password is incorrect." | Failure counter |
| Existing account ineligible | `409`, ineligible message | None |
| Invite not possible | `409 portal_invite_unavailable`, "Portal access can't be sent for this contact." | None |
| Quote expired in portal | `409 quote_expired`, "This quote has expired. Contact <organization name> for an updated quote." | None |
| Reschedule not eligible / already requested | `409`, "This appointment can't be rescheduled online. Call <phone>." / "You already asked to reschedule this appointment." | None |
| Messaging unavailable | `409`, button hidden | None |
| Email send failure (invite, request, reschedule, message) | Success response unchanged; logged by category | Committed data kept |
| Rate limited | `429` + `Retry-After`, "Too many attempts. Please wait a few minutes and try again." | None |
| Address field sent on property edit | `400`, "The address can't be changed online." | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | A user with one active link; separately unknown email, wrong password, non-active user, and a user with no active link | `POST /portal/sessions` | Valid credentials return `200 PortalSession` with `fieldops_portal_session` (HttpOnly, Secure, SameSite=Strict), `last_login_at` and audit written. Every failure case returns the identical `401`, and the throttle applies `429` |
| AC-02 | A portal cookie and an internal cookie | Each calls the other area's endpoint | Portal cookie on an internal endpoint → `401`. Internal cookie on `/portal/dashboard` → `401`. A portal-only user gets the generic `401` from internal sign-in. Portal sign-out leaves the internal session valid |
| AC-03 | An active portal session | Before the next request: the customer is archived, the contact deactivated, access removed, the user disabled, the organization deactivated, or the password reset | Every case returns `401` and deletes the portal cookie. The password reset also invalidates the earlier internal session |
| AC-04 | A user with active links in organizations A (oldest) and B | Sign-in, then switch to B, then switch with a contact of another user | Sign-in opens A with `accounts` listing both. Switching to B reissues the cookie with B's customer, keeps `signedInAt`, and the dashboard shows only B's data. Another user's contact or an inactive link → `404 portal_account_unavailable`. With one link, no **Switch account** is shown |
| AC-05 | A customer with an active primary contact with email | An Owner selects **Invite to portal**, then **Resend invitation** | One open invitation exists, the earlier one is revoked, the email with `/portal/activate#token=` is sent after commit, and the card shows "Invitation sent · expires <date>". A Viewer sees the status with no actions and gets `403` on the endpoint. A contact without email or an already active link → `409` |
| AC-06 | A valid invitation for an email with no user, once for a contact with a last name and once for a contact without | The contact validates and accepts with a valid password (and, without a last name, first omits and then provides **Last name**) | One `users` row is created active, the contact gets `portal_user_id` and `portal_linked_at`, and the invitation is accepted. Without a last name, `validate` returns `lastNameRequired: true`, omission → `400 lastName`, and the given value is stored on both user and contact in the same transaction. The cookie is issued after commit and Home opens. Reusing the token → `410` |
| AC-07 | A valid invitation for an email of an existing staff user | `accept-existing` with a wrong password, then the correct one | Wrong → `401`, nothing linked. Correct → the same `users.id` is linked, with no new user and unchanged `organization_users` and `users.status`. Linking a user already linked to another contact of that organization → `409` |
| AC-08 | Invitation tokens that are unknown, expired, revoked, accepted, or whose contact became inactive, non-primary, linked, or had its email changed | `validate` | All return the identical `410 portal_invitation_unavailable` |
| AC-09 | A linked contact | Staff select **Remove portal access** and confirm | `portal_user_id`/`portal_linked_at` are null, open invitations are revoked, audit is written, and the card shows "Not invited". The user, its internal membership and its link in another organization remain valid |
| AC-10 | A portal user | Requests a reset on `/portal/forgot-password` and confirms the emailed link | The response is the neutral `202`. The link targets `/portal/reset-password`. After confirming, the page offers **Sign in** to the portal. A user with no active link receives no email |
| AC-11 | Two customers in organization A and one in B, each with properties, requests, quotes, visits, invoices, payments | Customer A1's session calls every portal read and action endpoint with A2's and B's ids (property, request, visit, quote, invoice, work order, payment, photo) | Every call returns the identical `404 portal_resource_unavailable`, nothing changes, and lists and the dashboard contain only A1's data |
| AC-12 | A customer with several quotes, invoices and visits across two properties | `GET /portal/dashboard` with All and with each property | The action quote, payment due, appointment, active requests and activity follow the BR-18 scoping and the selection and ordering rules of BR-20 … BR-23 and BR-26, including `moreCount`, overdue and empty (`null`/`[]`) cases. Expired-by-date quotes, draft/void invoices and cancelled work orders never appear |
| AC-13 | Visits in each status | Dashboard and appointments render | The progress step follows BR-38, the technician comes from the active primary assignment, the info notice follows BR-22, and Past/Upcoming follow BR-29 |
| AC-14 | Events of every BR-24 type, some older than 90 days, some of another customer, and `portal_updates_seen_at` set between them | `GET /portal/updates`, then `POST /portal/updates/seen` | Only the customer's events within 90 days appear, newest first, with BR-24 titles and targets. `unreadCount` counts events after the seen time. After `seen` the count is 0 and the bell hides |
| AC-15 | Home on desktop at ≥ 1280 px | The page loads | It matches Design 28: greeting by local time, property selector (All only with ≥ 2 properties), **Request a service**, cards and the activity table with BR-26 statuses and actions, Need help with call and message. Every card link reaches a real portal page or action, none "Coming soon" |
| AC-16 | Home with no data (new customer with one property) and a failing dashboard request | The page loads | Each card shows its BR-19 … BR-26 empty text. A failure shows the dashboard error with **Try again**, which reloads |
| AC-17 | Home, Requests and Quotes at 768 px and 375 px | The pages load | Layout follows the responsive rules: drawer navigation, stacked activity rows, no horizontal scroll at 375 px, accessible bell, stepper and chips |
| AC-18 | A contact on `/portal/requests/new` with a selected property | Submits a valid request with an existing property, and another with a new property | `201` with the request number, `source = portal`, session customer and contact, guest snapshot from the contact, history, audit and email. The new property is created per public BR-10. A foreign `propertyId` → `400 property.propertyId` |
| AC-19 | The Requests module | Lists are opened and a request is viewed | Paginated rows, property filter, BR-39 labels, detail with the linked quote link, and no internal data |
| AC-20 | A `sent` quote of the customer | **Review quote** → approve with options; another quote → **Ask a question** from the dashboard; another → decline | The behavior equals customer-quote-approval (server totals, idempotency, `409 quote_already_answered`), with responder = session contact and audit `channel: portal`. No quote access token row is created or revoked |
| AC-21 | A quote in `sent` whose `valid_until` passed | The list and detail are opened and approve is called | Shown as "Expired", read-only with the BR-30 message. Approve → `409 quote_expired`. `quotes.status` is unchanged |
| AC-22 | A `sent` invoice of the customer | **View invoice** → card payment confirmed by webhook; another invoice → bank transfer notice; a paid invoice → receipt, completion report and review | The behavior equals customer-invoice-payments, including one pending attempt and webhook-only success. Receipts, report and photos download only for the customer's own resources. No invoice access token row is created or revoked |
| AC-23 | A scheduled visit in the future with no pending request | The contact submits a reschedule; then submits again; then staff change `scheduled_start`; also a visit `on_the_way` | First → `201`, row stored, email sent after commit, visit unchanged, card shows "Reschedule requested". Second → `409 reschedule_already_requested`. After the schedule change a new request is allowed. `on_the_way` → `409 reschedule_not_available`. Invalid date/window/reason → field errors |
| AC-24 | The Properties pages of a customer with a primary property | Add a property; edit it with name, access instructions and `isPrimary: true`; repeat with a stale `updatedAt`; send an address field on edit | The new property is created non-primary with `countryCode` US. The edit saves name and instructions and makes it primary while clearing the previous primary in one transaction. Stale `updatedAt` → `409 property_changed`. The address field → `400` and the address is unchanged |
| AC-25 | An organization with email, and one without organization or main branch email | **Send a message** with a valid and an empty message | With email: `202`, audit without text, email after commit with reply-to the contact. Empty → `400`. Without email: the button is hidden and the endpoint returns `409 messaging_unavailable` |
| AC-26 | The public request form, quote page and invoice page | They load | Each shows "Sign in to your portal" → `/portal/sign-in`. No portal session is created from a token |
| AC-27 | Any portal page with an expired session, and a detail page with a foreign id | The page loads | Expired → `/portal/sign-in` with "Your session ended. Sign in again.". Foreign id → "This item isn't available." with **Back to <module>** |
| AC-28 | Portal endpoints with rate limits | Limits are exceeded | `429` + `Retry-After` with the shared attempts message. Logs and audit contain no emails, passwords, tokens, messages, reasons or addresses |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Dashboard selection rules (BR-20 … BR-23, BR-26) as table-driven pure functions; visit/request label mapping (BR-38, BR-39); reschedule eligibility and pending derivation (BR-32) | AC-12, AC-13, AC-23 |
| Backend integration | Portal authentication: sign-in success/generic failures, cookie separation both ways, switch account, revalidation revocation table (archive, contact inactive, remove access, user disabled, password reset) | AC-01 … AC-04, AC-09 |
| Backend integration | Invitations: invite/resend/permission, accept new, accept existing (no duplicate user, memberships unchanged), unusable token table | AC-05 … AC-08 |
| Backend integration | Dashboard aggregation with property scoping and updates unread/seen | AC-12, AC-14 |
| Backend integration | Portal quote approve/expired and portal invoice card intent through the shared use cases, no token rows written | AC-20 … AC-22 |
| Backend integration | Portal request creation, reschedule, property create/edit, message | AC-18, AC-23 … AC-25 |
| Authorization/tenant isolation | One grouped, parameterized cross-customer (same organization) and cross-organization denial across each distinct portal resolution path (property, request, visit, quote, invoice, work order, payment) | AC-11 |
| Frontend component/service | Portal session service/guard (401 redirect, switch account visibility); Home states and property selection; reschedule and message dialogs; request wizard without contact step and property choice; activation page token handling | AC-04, AC-15, AC-16, AC-18, AC-23, AC-25, AC-27 |
| Browser (final audit) | User-supplied visual QA: Home fidelity to Design 28, responsive 1280/768/375, navigation with no "Coming soon", quote and invoice inside the portal shell | AC-15, AC-17, AC-26 |

Backend integration targets 10–12 methods in total, grouping related
scenarios into parameterized or table-driven methods, and does not repeat
validations already proven by the public-request, quote-approval,
invoice-payment, sign-in and password-recovery precedents. Justification: this
feature creates a new authentication scheme, a new tenant authorization path
for every customer-facing resource and session revocation, all mandatory
security boundaries. Frontend stays within 5–6 methods. No Playwright.

The spec has 28 active ACs, above the target of 25. Justification: AC-01 … AC-11
cover the separate authentication, invitation, revocation and isolation
boundaries, which are security risks and cannot be merged without losing
independent evidence. The remaining ACs already group each dashboard card
family and module.

## Dependencies

- `sign-in`, `password-recovery`, `invitation-acceptance` (session,
  throttling, reset and token precedents) — implemented.
- `public-service-request`, `customer-quote-approval`,
  `customer-invoice-payments` — implemented. Their use cases are reused
  through the access resolver, and public behavior must stay unchanged.
- `customer-property-detail` — implemented. The Contact details card gains
  portal status and actions (supersedes its OD-04 exclusion for the primary
  contact only).
- `mobile-job-progress` / dispatch flows — implemented. They are the source of visit
  statuses and assignments.
- Known risk: the payments list regression noted for
  `customer-invoice-payments` (re-audit owed). Portal payment/receipt
  queries must be customer-bounded and must not reuse an organization-wide
  slow query.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Portal routes live under `/portal`, with their own guard and shell, separate from the internal and technician shells. |
| AS-02 | The invitation email and the reschedule/message emails use the existing `IEmailSender` after commit, without templates beyond the text in this spec. |
| AS-03 | Technician display name comes from `technician_profiles` (first and last name). |
| AS-04 | Property selector state lives only in the page (no browser storage). |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Activation | Staff invitation · self-claim · both | No | Staff invitation (user, 2026-10-10) |
| OD-02 | Users and cookies | Reuse `users` with a separate cookie · forbid dual role | No | Reuse `users`, separate `fieldops_portal_session` (user, 2026-10-10) |
| OD-03 | Password recovery | Reuse with portal links · exclude | No | Reuse with portal links and redirects; a password change revokes both sessions (user, 2026-10-10) |
| OD-04 | Multiple organizations/contacts | Oldest only · oldest + Switch account · one link | No | One link per organization, oldest at sign-in, **Switch account** with ≥ 2 active links (user, 2026-10-10) |
| OD-05 | Quote/invoice in portal | Session-authorized endpoints · minted tokens · split spec | No | Session-authorized endpoints reusing the use cases, no new tokens (user, 2026-10-10) |
| OD-06 | Minimal lists | Lists + details · lists + side panel | No | Lists + detail pages (user, 2026-10-10) |
| OD-07 | Properties | Add + limited edit · read-only · full edit | No | Add; edit name, access instructions, primary; never the address (user, 2026-10-10) |
| OD-08 | Reschedule | Store + email · + dispatch indicator · omit | No | Store + email to the organization; visit unchanged (user, 2026-10-10) |
| OD-09 | Messages and notifications | Derived feed + seen column + email message · no badge · write notifications | No | Derived read-only feed, unread via `portal_updates_seen_at`, **Send a message** emails after commit (user, 2026-10-10) |
| OD-10 | Public links | Sign-in link · none | No | "Sign in to your portal" link; tokens never become sessions (user, 2026-10-10) |
| OD-11 | Revocation | Revalidation + Remove access · revalidation only | No | Both; removal never affects the user or other links (user, 2026-10-10) |
| OD-12 | Property selector | Scopes cards with All · property card only | No | Scopes cards, default primary, All with ≥ 2 (user, 2026-10-10) |
| OD-13 | Contact without last name (`users.last_name` is NOT NULL, `customer_contacts.last_name` is nullable) | A: invite refused with `409 portal_invite_unavailable` · B: activation asks for the last name when missing | No | B: new-user activation requires **Last name** and stores it on user and contact in one transaction (user, 2026-10-10) |
| OD-14 | Confirmation of the exact SA-19, SA-20 and SA-21 DDL | Confirm · change | No | Confirmed as written; `fieldops-schema.sql` is updated first, then one migration is generated and never applied (user, 2026-10-10) |
| OD-15 | Conflicts between the 2026-10-10 contract instructions and approved behavior: (a) the quote list omits `calculate`, which customer-quote-approval BR-11 requires to show server totals when optional items are toggled; (b) the invoice list omits `review`, which customer-invoice-payments and AC-22 include; (c) the given `availability` shape (`mode: asap\|dates\|flexible`, `dates[]`, `notes`) does not match the public form, which stores one `dateMode` (`asap\|date\|flexible`), at most one `preferredDate`, a `timeWindow` in every mode, and `schedulingNotes`; (d) "ids never in query or body" versus list filters (`propertyId`, `page`) and selection data (`selectedOptionalLineIds`, new-request `propertyId`) | Recommended: (a) keep `POST /portal/quotes/{quoteId}/calculate`; (b) keep `POST /portal/invoices/{invoiceId}/review`; (c) `availability` = `{ mode: asap\|dates\|flexible, dates: [{ date, window }], window, notes \| null }`, where `dates` has 0 items unless `mode = dates` (exactly 1, public BR-07 range) and `window` always carries the public time window; (d) the rule applies to addressed resources, so filters stay in the query and selections in the body · Alternatives: drop (a)/(b), or a different shape for (c) | No | (a) keep `calculate` for server totals before approval; (b) keep `review`; (c) `{ mode: asap\|date\|flexible, dates, window, notes }` with exactly one `dates` element only for `date`, empty otherwise, `window` always required, public form not extended; (d) only the primary resource identifier goes in the path, filters in the query, command data in the body (user, 2026-10-10) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03, AC-27 |
| FR-03 | AC-04 |
| FR-04 | AC-05, AC-09 |
| FR-05 | AC-06, AC-07, AC-08 |
| FR-06 | AC-10 |
| FR-07 | AC-15, AC-17 |
| FR-08 | AC-12, AC-15 |
| FR-09 | AC-12, AC-15, AC-20 |
| FR-10 | AC-13, AC-15 |
| FR-11 | AC-12, AC-14, AC-15 |
| FR-12 | AC-18 |
| FR-13 | AC-13, AC-19 |
| FR-14 | AC-20, AC-21, AC-22 |
| FR-15 | AC-23 |
| FR-16 | AC-24 |
| FR-17 | AC-25 |
| FR-18 | AC-26 |
| FR-19 | AC-11, AC-28 |
| FR-20 | AC-16, AC-17, AC-27 |

## Change log

| Date | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-10 | — → DRAFT | Created from Design 28 with the user's decisions OD-01 … OD-12 |
| 2026-10-10 | DRAFT → DRAFT | Revised after validation: per-endpoint portal quote/invoice contracts, `Property`, `RequestProperty`, `RequestAvailability` and id transport defined; OD-13 and OD-14 opened as blocking |
| 2026-10-10 | DRAFT → DRAFT | Revised: SA-19…SA-21 confirmed (OD-14); OD-13 B (last name at activation); portal quote/invoice paths per user; `Property` with `stateRegion`, `updatedAt` and PATCH concurrency; property creation without primary flag; mockup-less screens accepted; integration target 10–12; OD-15 opened for contract conflicts |
| 2026-10-10 | DRAFT → DRAFT | Revised: OD-15 resolved (portal `calculate` and `review` kept, `RequestAvailability` defined, identifier transport rule) |
| 2026-10-10 | DRAFT → APPROVED | Approved by user via /spec approve |
