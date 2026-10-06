# Customer quote approval

| Field    | Value                     |
| -------- | ------------------------- |
| Feature  | `customer-quote-approval` |
| Type     | Full-stack                |
| Status   | APPROVED                  |
| Created  | 2026-10-05                |
| Updated  | 2026-10-05                |
| Approved | 2026-10-05                |

## Context and objective

`quote-builder` sends an immutable quote version by email with a secure link
that currently opens a placeholder (`quote-builder` BR-27). This feature
replaces it with the public page of Design 4. Through the link, the customer
reviews exactly that version, selects optional items, and approves, declines
or asks a question. They can also download a PDF of the version. The server
calculates every total and records an auditable response. Approval only
enables a future work order spec, so it creates no work order, visit,
schedule, invoice or payment.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Customer (anonymous link holder) | With a valid token: view the version, its customer-facing data and assessment photos. Select optional items and see server totals. Approve, decline or ask one question. Download the PDF | Sign in, see other versions, quotes or organizations, see costs, margin, internal notes, diagnosis or file names, or send amounts the server trusts |
| Owner, Dispatcher, Operations Manager, Accounting, Viewer | Read the customer response on `/quotes/:quoteId` under the existing `quote-builder` BR-07 read policy | Respond on behalf of the customer |
| Technician and unknown roles | — | Unchanged from `quote-builder` (`403`) |

## Scope

- Public route `/quotes/view` with the token in the URL fragment (BR-01). It
  replaces the `/quote-approval` placeholder, and old links redirect to it
  (BR-02).
- Token resolution with generic, superseded and final states (BR-03, BR-04).
- Public quote content per Design 4 and `quote-builder` BR-23, with photos,
  logo and progress (BR-05 to BR-09).
- Optional item selection with server-side totals (BR-10, BR-11).
- Approve, decline and ask a question, idempotent and concurrency-safe
  (BR-12 to BR-17, BR-26).
- Lazy `expired` transition (BR-18).
- PDF download (BR-19).
- Public rate limiting, no-store and logging rules (BR-20, BR-21).
- Audit (BR-22).
- Internal **Customer response** card and derived "Expired" display on
  `/quotes/:quoteId`, and the send guard for an approved quote (BR-23,
  BR-24).
- Schema amendments (BR-25).

## Non-goals

- Work order, visit, schedule, invoice, deposit or payment creation, and the
  request transition to `converted`.
- A customer portal: navigation, sign-in, quote lists, notifications, the
  avatar or the breadcrumb shown in the mockup header.
- Email or in-app notifications to staff or to the customer about responses.
- Replying to a clarification inside FieldOps. Staff answer offline and may
  revise the quote (`quote-builder` BR-28).
- Electronic signatures (`organizations.require_customer_signature` is not
  read).
- A background expiry job.
- Changes to quote building, calculation rules or tokens other than the link
  path (BR-01).
- Automated browser, E2E or visual tests by agents.

## User flow

1. The customer opens the emailed link `/quotes/view#token=<raw>`.
2. The page keeps the token in tab memory, removes the fragment from the
   address bar and loads the quote.
3. With a valid token, the customer reviews the job summary, photos, price
   details, terms, total and progress.
4. The customer may toggle optional items. The server returns the new
   totals.
5. The customer checks the acceptance box and presses **Approve quote**. The
   page shows the approved state with the approved total and **Download
   PDF**.
6. Alternatively, the customer presses **Ask a question** and sends a
   question. The page confirms it, and approving or declining stays
   available.
7. Alternatively, the customer presses **Decline quote**, enters a reason
   and confirms. The page shows the declined state.
8. An invalid, revoked, expired or cancelled link shows a generic state. A
   link to an older version shows "newer version" text with no quote data.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The public route must read the token from the URL fragment, keep it tab-scoped, remove it from the address bar, and send it to the API only in POST bodies. Old `/quote-approval` links must reach the new route (BR-01, BR-02). |
| FR-02 | The server must resolve a token to exactly one sent, immutable version and return the generic, superseded or final state per BR-03 and BR-04, without revealing information about invalid tokens. |
| FR-03 | The page must render the customer-facing content of that version per BR-05 to BR-09, and never internal data. |
| FR-04 | Photos and the logo must be served only through token-protected public endpoints, scoped to the token's quote (BR-07, BR-08). |
| FR-05 | Optional item selection must be validated against the version, and the server must recalculate the totals from frozen line values. Browser amounts must never be trusted (BR-10, BR-11). |
| FR-06 | Approval must require the acceptance checkbox. It must record the version, selection, server totals, time, IP and allowed audit data, and set the quote `approved` with `approved_version_id` (BR-12, BR-13). |
| FR-07 | Declining must require confirmation and a reason, and set the quote `rejected` (BR-14). |
| FR-08 | **Ask a question** must record one clarification per version and set the quote `clarification_requested`. Approval and decline must stay available afterwards (BR-15). |
| FR-09 | Responses must be idempotent and safe under concurrent customer and staff submissions (BR-16, BR-17, BR-26). |
| FR-10 | A sent or clarification-requested quote past its expiry must become `expired` lazily, and never after approval or rejection (BR-18). |
| FR-11 | The customer must be able to download a server-generated PDF of the version, including the approved selection and the response stamp when present (BR-19). |
| FR-12 | Public endpoints must be rate limited, uncached and free of token or free-text logging (BR-20, BR-21). |
| FR-13 | Each response and expiry must write an audit row per BR-22. |
| FR-14 | Staff must see customer responses and the derived "Expired" status on `/quotes/:quoteId`, and sending a revision of an approved quote must be refused (BR-23, BR-24). |
| FR-15 | The BR-25 schema amendments must be reflected in the schema document and persistence model, keeping organization isolation through composite foreign keys. |
| FR-16 | Nothing in this feature may create work orders, visits, schedules, invoices or payments, or change the request status. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Link and token transport. The quote link builder emits `<first allowed frontend origin>/quotes/view#token=<raw>`. The public route `/quotes/view` sits outside the authenticated shell and is declared so that it is never matched as `/quotes/:quoteId`. On load the page follows the invitation precedent: it captures `token` from the fragment into tab-scoped storage, ignores other fragments, and replaces the URL with `/quotes/view` before any API call. Every public API call sends the token in a JSON POST body (`token`). It never goes in a path, query string or header. Without a token, the page shows the generic state (BR-03) with no API call. | Both |
| BR-02 | Old links. `/quote-approval` redirects to `/quotes/view` with its fragment preserved. The placeholder text "This quote will be available soon." is removed. | Frontend |
| BR-03 | Token resolution. The server hashes the token (`quote-builder` BR-27) and looks it up by `token_hash`. The table below defines the outcome per state, in order. The generic state is `404` `code` `quote_link_unavailable` with title "This link isn't available.", and the page shows "This quote link isn't available. It may have expired or been replaced. Please contact the company that sent it." It carries no organization branding or quote data. A malformed token, wrong length or non-base64url input yields the same `404`. | Backend |
| BR-04 | Superseded. When the token's version is not the quote's current sent version (`version_no < current_version_no`), the response is `410` `code` `quote_superseded` with title "A newer version of this quote is available.". The page shows that title and "Check your email for the most recent link from the company." with no quote data, branding or version numbers. This check runs first and applies whether or not the token was revoked or expired and whatever the quote status. An `approved` quote cannot have a later sent version (BR-24), so its approved-version token never reaches this state. | Both |
| BR-05 | Public content. `PublicQuote` contains only the following fields, all read from the token's version and its frozen lines. The header shows the logo (BR-08) or the initials fallback, and the organization name. It never shows navigation, notifications, user menu or breadcrumb. Title "Your quote is ready", with a status chip that has text and an icon: `sent` "Awaiting your approval" (warning), `clarification_requested` "Question sent" (info), `approved` "Approved" (success), `rejected` "Declined" (neutral). Subtitle "Review the proposed work for <scope>.". Meta: "#<Q-n>" Quote number, `sent_at` date Sent date, `valid_until` Valid until, all as "MMM d, yyyy" in the organization time zone. Company card: logo, organization name, "Questions? Call <phone>" as a `tel:` link (the quote branch phone, else the organization phone; the line is hidden when neither exists), and **Ask a question**. Job card: title `scope`, the property address (else the request service address; hidden when neither exists), "Prepared for <customer name>", and the customer message paragraph (hidden when null). **Scope of work**: the names of the non-optional lines in order, each with a check icon. **Assessment photos** (BR-07). **Price details**: regular lines (Description = name, plus description in secondary text when not empty; Qty with unit when not "unit"; Rate = unit price; Amount = `line_subtotal`). Then an "Optional items" section, when any exist, where each optional line has a toggle labelled with its name and its Amount. Then Subtotal, "Discount −<amount>" when > 0, the tax label and amount, and the Total band. **Terms and next steps**: "Payment terms" with the version `terms` text (hidden when null) and "No deposit required."; "What happens next?" with "After approval, our team will contact you to confirm the service date and assigned technician."; "Need a copy?" with **Download PDF**. Footer: "© <year> <organization name>. All rights reserved." and "Powered by FieldOps". Never included: unit cost, margin, internal note, diagnosis, recommended scope, technician, file names, line or catalog ids other than optional line ids, contact data other than the organization phone, and data of other versions. | Both |
| BR-06 | Summary card. "Quote total" shows the current server total (BR-11), "No payment due today", and "Valid until <date>". In `sent` or `clarification_requested`, it shows: the checkbox "I approve the scope of work and agree to the terms."; **Approve quote** with "Approval does not charge your payment method."; **Ask a question** (hidden once a question exists, BR-15); **Decline quote**; and "Your approval will be recorded securely.". In `approved`, it shows "Approved on <date>", the approved total and selected optional items, and **Download PDF**, with no actions. In `rejected`, it shows "Declined on <date>" and "Your decision was sent to <organization name>." with no actions. The optional toggles are read-only in `approved` (showing the approved selection) and in `rejected` (none selected). | Frontend |
| BR-07 | Photos. `PublicQuote.photos` lists `{ id }` of the `assessment_attachments` of the request's latest `completed` assessment (by `completed_at`), in creation order, up to 6. With none, the photos column is hidden. `POST /public/quote-links/photos/{photoId}` returns the binary only when the photo belongs to that list for the token's quote and organization, else the generic `404`. Headers: detected `Content-Type`, `Content-Disposition: inline` without file name, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`. The page renders blobs as object URLs. Each thumbnail opens an accessible full-size viewer with previous/next, and Escape closes it. | Both |
| BR-08 | Logo. `PublicQuote.organization.hasLogo`. `POST /public/quote-links/logo` returns the `organization_logos` content with the BR-07 headers, or the generic `404` when there is none. Without a logo, the header and company card show the organization initials in the brand circle. | Both |
| BR-09 | Progress. Steps with icon, label, date and state: "Request submitted" (request `created_at`, Completed); "Assessment" (the BR-07 assessment `completed_at`, Completed; omitted when there is none); "Quote" (current in `sent` or `clarification_requested`; Completed with "Approved <date>" in `approved`; "Declined <date>" in `rejected`); "Schedule service" ("After approval", tagged "Next"). Step states are conveyed by text, not color alone. | Both |
| BR-10 | Optional selection. `selectedOptionalLineIds` is an array of distinct ids, each of a line of the token's version with `is_optional = true`. An empty array is valid. A duplicate, unknown id, non-optional line or line of another version or organization → `400` `errors.selectedOptionalLineIds` "One or more optional items aren't available.". Defaults: nothing is selected while `sent` or `clarification_requested`, and the selection is not persisted until approval. | Backend |
| BR-11 | Totals with options. The server computes from frozen version values only, never recalculating rates or reading the catalog or organization settings: `subtotal` = version `subtotal` + Σ selected `line_subtotal`; `discount_total` = version `discount_total` (optional lines have no discount share, `quote-builder` BR-13); `tax_total` = version `tax_total` + Σ selected `line_tax`; `total` = `subtotal − discount_total + tax_total`. The tax label follows `quote-builder` BR-15 over non-optional lines plus selected taxable lines. With no selection the totals equal the version totals. `POST /public/quote-links/calculate` persists nothing. The page calls it on each toggle (debounced). While pending, totals keep their value with a loading indicator and **Approve quote** is disabled. Amounts in any request body are ignored. | Backend |
| BR-12 | Approve. `POST /public/quote-links/approve { token, selectedOptionalLineIds, acceptTerms }`. `acceptTerms` must be `true`, else `400` `errors.acceptTerms` "Confirm that you approve the scope of work and agree to the terms.". **Approve quote** is disabled until the box is checked. Allowed when the quote is `sent` or `clarification_requested`, the token resolves (BR-03, BR-04) and has not expired. In one transaction: lock the quote row; insert the `quote_responses` row (`response = approved`, `organization_id`, `quote_version_id`, `responder_name`, `responder_contact_id`, `responded_at = now`, `ip_address`, and the BR-11 `subtotal`, `discount_total`, `tax_total`, `total` recalculated by the server); insert one `quote_response_optional_lines` row per selected line; set `quotes.status = approved`, `approved_version_id` = the token version and `updated_at`; write the audit row (BR-22). Response `200 PublicQuote` in the approved state. The request stays `quoted`. | Backend |
| BR-13 | Responder data. `responder_name` = the request's linked active contact full name, else `guest_name`, else the customer display name, truncated to 180 characters. `responder_contact_id` = that contact or null. `ip_address` = the connection remote address (existing audit precedent), or null. No forwarded-header handling is added. The user agent is truncated to 256 characters and goes only in audit metadata. | Backend |
| BR-14 | Decline. **Decline quote** opens the dialog "Decline this quote?" with "Let <organization name> know why you're declining. This can't be undone.", a required **Reason** textarea, and **Decline quote** / **Cancel**. `POST /public/quote-links/decline { token, reason }`: `reason` 1–1000 characters after trim → `errors.reason` "Tell us why you're declining." / "Reason must be 1,000 characters or fewer.". Allowed in the BR-12 states. In one transaction: lock the quote; insert `quote_responses` (`response = rejected`, `comment` = reason, totals null, BR-13 fields); set `quotes.status = rejected` and `updated_at`; write the audit row. Response `200 PublicQuote` in the declined state. | Both |
| BR-15 | Ask a question. Both **Ask a question** buttons open the dialog "Ask a question" with "<organization name> will contact you by phone or email.", a required **Question** textarea, and **Send question** / **Cancel**. `POST /public/quote-links/clarification { token, message }`: `message` 1–1000 characters after trim → `errors.message` "Enter your question." / "Question must be 1,000 characters or fewer.". Allowed when the quote is `sent` or `clarification_requested`. The first question of a version, in one transaction: lock the quote; insert `quote_responses` (`response = clarification_requested`, `comment` = message, totals null, BR-13 fields); set `quotes.status = clarification_requested` and `updated_at`; write the audit row. A later question on the same version returns `200` with the existing clarification and stores nothing. The page then shows the confirmation "Your question was sent. <organization name> will contact you soon." with the date, hides **Ask a question**, and keeps approving and declining available. | Both |
| BR-16 | Idempotency. After `approved` or `rejected` on a version: repeating the same action returns `200` with the existing response and changes nothing, even with a different selection or reason. A different action (approve ↔ decline, or a question) returns `409` `code` `quote_already_answered` with title "This quote has already been answered.". The page then reloads the quote and shows its final state. | Backend |
| BR-17 | Concurrency. Every response takes a row lock on the quote before reading its status. The BR-25 partial unique indexes are the backstop: a unique violation is resolved by reloading and applying BR-16. Of concurrent different final actions, exactly one succeeds, and the other gets `409`. Concurrent identical actions store one row, and both receive `200`. The frontend disables the submitting control and sends one request at a time. | Both |
| BR-18 | Expiry. When any public endpoint resolves a current-version token whose `expires_at` ≤ now and the quote is `sent` or `clarification_requested`, it sets `quotes.status = expired` and `updated_at` and writes `quote.expired` in one transaction (idempotent under the BR-17 lock). It then returns the generic `404`. `approved` and `rejected` quotes never become `expired`. Their link shows the final state until `expires_at`, and the generic state afterwards. Action endpoints check expiry inside the lock. | Backend |
| BR-19 | PDF. `POST /public/quote-links/pdf { token }` returns `application/pdf` with `Content-Disposition: attachment; filename="<Q-n>-v<versionNo>.pdf"` and the BR-07 cache headers. It is generated server-side with PDFsharp-MigraDoc (MIT) from the stored version and response, never from browser data. The PDF contains the organization name, phone, quote number, version, sent and valid-until dates, customer name, property address, scope, customer message, scope of work, regular lines, optional items, subtotal, discount, tax label and amount, total, terms and "Powered by FieldOps". It contains no photos and no BR-05 exclusions. Before a response, optional items are marked "Optional — not included in the total" with version totals. After approval: selected options are marked "Included", others "Not included", with approved totals and "Approved on <date>". After decline: "Declined on <date>". Available in every state where the link shows content. **Download PDF** shows progress, and a failure shows "We couldn't download the PDF. Please try again.". | Both |
| BR-20 | Rate limiting (fixed window, per client IP plus one global partition, existing public rate-limit precedent). Read group (`view`, `calculate`, `photos`, `logo`): 60 per 5 minutes per client, 600 per minute global. Action group (`approve`, `decline`, `clarification`): 10 per 15 minutes per client, 100 per minute global. PDF: 10 per 5 minutes per client, 100 per minute global. Excess → `429` with `Retry-After`. The page shows "Too many attempts. Please wait a few minutes and try again.". | Both |
| BR-21 | Public hardening. Anonymous endpoints. `Cache-Control: no-store` on JSON, `Referrer-Policy: no-referrer` on public responses. Request bodies limited to 16 KB (`413`). The raw token, its hash, reasons, questions and responder data never appear in logs, problem details, audit or responses. Logs may contain the quote id and outcome category only. Organization and quote ids never appear in public responses. | Backend |
| BR-22 | Audit. `entity_type` `quote`, `entity_id` quote id, `organization_id` and `branch_id` of the quote, `actor_user_id` null, `ip_address` per BR-13. `quote.approved`: before/after `{ status }`, metadata `{ versionNo, total, currency, selectedOptionalLineIds, termsAccepted: true, userAgent }`. `quote.rejected`: before/after `{ status }`, metadata `{ versionNo, userAgent }`. `quote.clarification_requested`: before/after `{ status }`, metadata `{ versionNo, userAgent }`. `quote.expired`: before/after `{ status }`, metadata `{ versionNo }`. Idempotent repeats write nothing. No audit contains reasons, questions, names, contact data or tokens. | Backend |
| BR-23 | Internal view. `QuoteDetail` adds `displayStatus` = `expired` when `status` ∈ `sent`, `clarification_requested` and the current version's `valid_until` end (organization time zone) has passed, else `status`. It also adds `responses`: `[{ versionNo, type, respondedAt, responderName, comment \| null, selectedOptionalLines: [{ name, lineSubtotal }], totals: { subtotal, discountTotal, taxTotal, total } \| null }]`, newest first. `/quotes/:quoteId` shows the chip from `displayStatus` and a **Customer response** card with the selected version's responses: "Approved", "Declined" with the reason, or "Question" with the text, with responder name and date. An approval also shows its selected optional items and approved total. With none: "No response from the customer yet.". Visible to every read role. | Both |
| BR-24 | Approved quotes are final for revisions. `POST /quotes/{id}/send` on a quote whose status is `approved` → `409` `code` `quote_changed` and nothing changes. A revision draft created before the approval stays discardable (`quote-builder` BR-29). `quote-builder` BR-28 already refuses **Revise quote** on `approved`. | Backend |
| BR-25 | Schema amendments, confirmed by the user 2026-10-05 (OD-10). (1) `quote_versions`: add `UNIQUE (organization_id, quote_id, id)`. (2) `quotes`: replace `fk_quotes_approved_version` with `FOREIGN KEY (organization_id, id, approved_version_id) REFERENCES quote_versions(organization_id, quote_id, id)`, so the approved version belongs to the same quote and organization. (3) `quote_lines`: add `UNIQUE (organization_id, quote_version_id, id)`. (4) `quote_responses`: add `organization_id uuid NOT NULL REFERENCES organizations(id)`; replace the single FK with `FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions(organization_id, id)`; add `subtotal`, `discount_total`, `tax_total`, `total numeric(14,2)` with each `>= 0`; add `CONSTRAINT ck_quote_responses_totals CHECK ((response = 'approved') = (subtotal IS NOT NULL AND discount_total IS NOT NULL AND tax_total IS NOT NULL AND total IS NOT NULL))` and `CONSTRAINT ck_quote_responses_totals_null CHECK (response = 'approved' OR (subtotal IS NULL AND discount_total IS NULL AND tax_total IS NULL AND total IS NULL))`; add `UNIQUE (organization_id, quote_version_id, id)`; replace `responder_contact_id`'s single FK with `FOREIGN KEY (organization_id, responder_contact_id) REFERENCES customer_contacts(organization_id, id)`; `CREATE UNIQUE INDEX ux_quote_responses_final ON quote_responses(quote_version_id) WHERE response IN ('approved','rejected')`; `CREATE UNIQUE INDEX ux_quote_responses_clarification ON quote_responses(quote_version_id) WHERE response = 'clarification_requested'`. (5) New `quote_response_optional_lines (organization_id uuid NOT NULL REFERENCES organizations(id), quote_version_id uuid NOT NULL, quote_response_id uuid NOT NULL, quote_line_id uuid NOT NULL, PRIMARY KEY (quote_response_id, quote_line_id), FOREIGN KEY (organization_id, quote_version_id, quote_response_id) REFERENCES quote_responses(organization_id, quote_version_id, id) ON DELETE CASCADE, FOREIGN KEY (organization_id, quote_version_id, quote_line_id) REFERENCES quote_lines(organization_id, quote_version_id, id))`. The selected line's `is_optional = true` and an `approved` parent response are enforced in the application layer. The table has no rows today, so no backfill is needed. No other table changes. | Backend |
| BR-26 | Staff concurrency. Every customer transition (BR-12, BR-14, BR-15, BR-18) sets a new `quotes.updated_at`, so an internal mutation carrying an older `updatedAt` (`quote-builder` BR-22) returns `409` `quote_changed`. Idempotent repeats and refused actions leave `updated_at` unchanged. | Backend |

### Token resolution outcomes (BR-03, BR-04, BR-18)

| Token state | Response |
| ----------- | -------- |
| Missing, malformed or unknown hash | `404` `quote_link_unavailable` |
| Version older than the quote's current sent version | `410` `quote_superseded` |
| Revoked (resend) on the current version | `404` `quote_link_unavailable` |
| Quote `cancelled` or `expired` | `404` `quote_link_unavailable` |
| `expires_at` passed, quote `sent` or `clarification_requested` | Quote → `expired` (BR-18); `404` `quote_link_unavailable` |
| `expires_at` passed, quote `approved` or `rejected` | `404` `quote_link_unavailable`; status unchanged |
| Valid, quote `sent` or `clarification_requested` | `200 PublicQuote` with actions |
| Valid, quote `approved` or `rejected` | `200 PublicQuote`, final state, no actions |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| `sent` | `clarification_requested` | Ask a question | Customer | Valid current token, not expired, first question of the version |
| `sent`, `clarification_requested` | `approved` | Approve | Customer | Valid current token, not expired, `acceptTerms`, valid selection; sets `approved_version_id` |
| `sent`, `clarification_requested` | `rejected` | Decline | Customer | Valid current token, not expired, valid reason |
| `sent`, `clarification_requested` | `expired` | Any public endpoint after `expires_at` | System (lazy) | Current-version token |
| `approved`, `rejected` | — | Any action | Customer | Same action → existing response; different → `409` |
| `approved`, `rejected` | `expired` | — | — | Never |

`quote-builder` transitions are unchanged, except BR-24. The request stays
`quoted` in every case.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `quote_access_tokens`: read by `token_hash`, plus `expires_at`,
    `revoked_at` and `quote_version_id`. Never written.
  - `quotes`: read; lock; update `status`, `approved_version_id` and
    `updated_at`.
  - `quote_versions`, `quote_lines`: read only (immutable).
  - `quote_responses` (amended): insert and read.
  - `quote_response_optional_lines` (new): insert and read.
  - `audit_logs`: insert.
  - Read only: `organizations` (`name`, `phone`, `timezone`,
    `quote_prefix`), `organization_logos`, `branches` (`phone`),
    `service_requests` (`created_at`, `guest_name`, `service_address`),
    `customers`, `customer_contacts`, `properties`, `assessments`,
    `assessment_attachments`.
- Schema amendments required: BR-25, **confirmed by the user 2026-10-05**.
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model,
  and generates one migration under `--generate-migration`. Agents never apply
  it. The mapping change requires `database-reviewer`.
- New backend dependency: PDFsharp-MigraDoc (MIT), authorized by the user
  2026-10-05 (OD-09). It is used only for BR-19.

## Tenant isolation and authorization

- Organization context: resolved only from the token row
  (`quote_access_tokens.organization_id` → version → quote). Public requests
  carry no organization, quote, version or request id. Any such field is
  ignored.
- Client-provided identifiers: `photoId` (path) and `selectedOptionalLineIds`
  (body). Both are verified against the token's organization, quote and
  version. A failure returns the generic `404` (photo) or `400` (selection)
  with no foreign data.
- A token of organization A never returns data, photos, logo or PDF of
  organization B. The composite foreign keys of BR-25 enforce same-organization
  rows for responses, selections and the approved version.
- Quote enumeration: tokens are 256-bit and stored hashed. Invalid, revoked,
  expired and cancelled tokens give one indistinguishable response (BR-03).
  Public responses contain no internal ids except optional line and photo ids
  of the token's own version.
- Internal reads (BR-23) keep the `quote-builder` BR-07 read policy, branch
  scope and cross-organization `404`.

## API contracts

Contract status: Final. All public endpoints are anonymous `POST` with a JSON
body containing `token`, and use the BR-20 policy group and BR-21 headers.
Errors use ProblemDetails with `code` on `404`, `409` and `410`. Money is a
decimal with 2 decimals, quantities have up to 3, dates are `YYYY-MM-DD` and
instants are ISO with offset.

`Totals` = `{ subtotal, discountTotal, taxLabel, taxTotal, total, currency }`.

`PublicQuote` = `{ organization: { name, phone | null, hasLogo }, quote: {
displayNumber, versionNo, status, sentOn, validUntil, scope, customerMessage |
null, terms | null }, customer: { name, address | null }, scopeItems: [string],
lines: [{ id | null, name, description, quantity, unit, unitPrice,
lineSubtotal, isOptional }], versionTotals: Totals, photos: [{ id }], progress:
{ requestSubmittedOn, assessmentCompletedOn | null }, clarification: {
askedOn } | null, response: { type: approved | rejected, respondedOn,
selectedOptionalLineIds, totals: Totals | null } | null }`. `lines[].id` is
non-null only for optional lines. `sentOn`, `requestSubmittedOn`,
`assessmentCompletedOn`, `askedOn` and `respondedOn` are `YYYY-MM-DD` dates
converted by the server to the organization time zone; no instant or time
zone is exposed publicly. `status` ∈ `sent`, `clarification_requested`,
`approved`, `rejected`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/public/quote-links/view` | `{ token }` | `200 PublicQuote` | `404` `quote_link_unavailable` · `410` `quote_superseded` · `429` | Anonymous + token |
| POST | `/public/quote-links/calculate` | `{ token, selectedOptionalLineIds }` | `200 Totals` | `400` `selectedOptionalLineIds` · `404` · `410` · `429` | Anonymous + token |
| POST | `/public/quote-links/approve` | `{ token, selectedOptionalLineIds, acceptTerms }` | `200 PublicQuote` | `400` (`acceptTerms`, `selectedOptionalLineIds`) · `404` · `409` `quote_already_answered` · `410` · `429` | Anonymous + token |
| POST | `/public/quote-links/decline` | `{ token, reason }` | `200 PublicQuote` | `400` `reason` · `404` · `409` · `410` · `429` | Anonymous + token |
| POST | `/public/quote-links/clarification` | `{ token, message }` | `200 PublicQuote` | `400` `message` · `404` · `409` · `410` · `429` | Anonymous + token |
| POST | `/public/quote-links/photos/{photoId}` | `{ token }` | `200` image binary (BR-07) | `404` · `410` · `429` | Anonymous + token |
| POST | `/public/quote-links/logo` | `{ token }` | `200` image binary | `404` · `410` · `429` | Anonymous + token |
| POST | `/public/quote-links/pdf` | `{ token }` | `200 application/pdf` attachment | `404` · `410` · `429` | Anonymous + token |
| GET | `/quotes/{id}` | — | `200 QuoteDetail` + `displayStatus`, `responses` (BR-23) | Unchanged | Read (unchanged) |
| POST | `/quotes/{id}/send` | Unchanged | Unchanged | Adds `409` `quote_changed` when `approved` (BR-24) | Manage (unchanged) |

- Internal contracts: `IQuoteLinkBuilder` emits the BR-01 link. Response
  handling reuses the `quote-builder` calculator conventions for rounding and
  the tax label. A PDF renderer interface lives in Application with its
  PDFsharp-MigraDoc implementation in Infrastructure.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/quotes/view` | Skeletons for header, title row, cards, summary and progress | No photos: column hidden. No optional items: section hidden. No discount: row hidden | Generic and superseded states (BR-03, BR-04) as a centered card under a neutral FieldOps header. `429`: BR-20 text. Other failure: "We couldn't load this quote." with **Try again** | Public | BR-05 to BR-09 |
| Optional toggles and totals | Totals keep their value with an indicator; **Approve quote** disabled | — | `400` or failure: toggle reverts and "We couldn't update the total. Please try again." | — | BR-11 totals |
| Approve / Decline / Ask dialogs | Button progress, disabled, one request | — | `400`: inline field errors, focus on the field. `409`: reload and final state with the toast "This quote has already been answered.". `404`/`410`: generic or superseded state. Other: "We couldn't send your response. Please try again." with input kept | — | BR-06 approved/declined state; BR-15 confirmation |
| PDF download | Link progress | — | BR-19 failure text | — | File download |
| `/quotes/:quoteId` Customer response card | Part of the existing skeleton | "No response from the customer yet." | Existing error pattern | Read roles | BR-23 |

- Mockup: `design/assets/4-design.png` (Approved). The page must reproduce
  it: the two-column layout with the main column (company card, job card with
  scope of work and assessment photos, Price details, Terms and next steps)
  and the right column (Quote total with approval controls, Quote progress),
  the title row with chip and the meta trio, the colors, spacing, type scale,
  icons and the footer. It uses the existing FieldOps tokens, Inter,
  PrimeIcons and current components.
  - Deviations by decision:
    - The header keeps only the logo and organization name. There is no
      navigation, bell, avatar or breadcrumb (OD-12).
    - "Optional items" with toggles and the discount row are added when
      present (BR-05).
    - "Prepared for <customer name>" is added to the job card.
    - Scope of work lists the non-optional line names (OD-03).
    - The mockup's tax "$22.28" follows per-line rounding (`quote-builder`
      AC-09).
    - The approved, declined, question-sent, generic and superseded states
      reuse the same cards and tokens.
    - Sample data is replaced by real data.
- Responsive:
  - Desktop: two columns as in the mockup. The right column is sticky only
    when its height fits the viewport.
  - Tablet: the main column cards are full width. The job card stacks its
    three sections, and Quote total and Quote progress sit side by side below
    the title row, before the cards.
  - Mobile, single column in this order: header, title, chip and meta;
    Quote total with actions; company card; job summary; scope; photos (a
    horizontal scroll strip inside the card); Price details as stacked rows
    (description, then "Qty × Rate" and Amount); Optional items; totals;
    Terms and next steps; Quote progress; footer. No horizontal page scroll.
- Accessibility:
  - Focus goes to the `h1` on load and on each state change.
  - The totals region is a polite live region.
  - Optional toggles have accessible names with the item name and amount.
  - The acceptance checkbox is labelled by its text, and the disabled
    **Approve quote** exposes "Check the box to approve.".
  - Dialogs trap focus and return it to the trigger.
  - The photo viewer has alt text "Assessment photo <n> of <total>".
  - Chips and progress states never rely on color alone.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Missing, malformed, unknown, revoked, cancelled or expired token | `404` `quote_link_unavailable`; generic state | `expired` transition only per BR-18 |
| Token of a superseded version | `410` `quote_superseded`; newer-version state | None |
| Photo not of the token's quote, or no logo | `404` | None |
| Invalid selection, missing acceptance, invalid reason or question | `400` on the key | None |
| Different action after approval or decline | `409` `quote_already_answered`; reload to final state | None |
| Same action repeated | `200` with the existing response | None |
| Rate limit exceeded | `429` + `Retry-After`; BR-20 text | None |
| Body over 16 KB | `413` | None |
| Send of a revision after approval | `409` `quote_changed` | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ---- | ---- | ---- |
| AC-01 | A quote sent with this feature, and an older email link `/quote-approval#token=<raw>` | The link builder output is inspected, and both links are opened | The link is `/quotes/view#token=<raw>`. Both land on `/quotes/view` with the fragment removed from the address bar before the first API call. The token travels only in POST bodies, and the route does not open the authenticated `/quotes/:quoteId` |
| AC-02 | Tokens in each BR-03 table state (missing, malformed, unknown, revoked by resend, cancelled quote, past expiry with `sent`, past expiry with `approved`) | `view` is called and the page renders | Each returns the same `404` `quote_link_unavailable` body and the generic state with no branding or quote data. The `sent` quote becomes `expired` with one `quote.expired` audit row. The `approved` quote stays `approved` |
| AC-03 | A quote with versions 1 and 2 sent | The version-1 token is used on `view`, an action and `pdf` | Each returns `410` `quote_superseded`. The page shows "A newer version of this quote is available." with no quote data, and nothing changes |
| AC-04 | A valid token to a `sent` version with regular, optional and discounted lines, a customer message, terms, photos, a logo and a branch phone | The page loads | It renders BR-05 to BR-09: header with logo and name only, chip "Awaiting your approval", "#Q-n", dates, company card with the branch phone `tel:` link, job card with scope, address, customer, message, scope-of-work names, photos, price details with the Optional items section and discount row, terms, totals, progress and footer, matching Design 4 |
| AC-05 | The same version | The `view` response and the page DOM are inspected | They contain no unit cost, margin, internal note, diagnosis, recommended scope, technician, file names, contact data other than the organization phone, organization, quote, version or catalog ids, or data of other versions |
| AC-06 | After the version was sent, the catalog prices, organization tax rate, currency and prefix change | The page and PDF are loaded | Lines, rates, totals, currency, terms and dates equal the frozen version. Only the display prefix follows `quote-builder` BR-06 |
| AC-07 | A token of organization A and photo, logo and optional line ids of organization B, and of another quote of A | Photos and the logo are requested and a calculate is sent with those ids | Own photos return the BR-07 headers. Foreign photo ids return `404`. Foreign or other-version line ids return `400` `selectedOptionalLineIds`. No foreign data is returned. Without a logo, the initials fallback shows |
| AC-08 | The `quote-builder` AC-09 lines plus optional lines of 120.00 (taxable, 8.25 %) and 40.00 (non-taxable), with a 10.00 discount on the regular lines | `calculate` runs with none, one and both options selected, then with a duplicate, a regular line id and client amounts in the body | Totals with none equal the version totals. With the 120.00 option, subtotal +120.00, tax +9.90 and total recalculated. With both, subtotal +160.00 and tax still +9.90, and the label stays "Tax (8.25%)" because the 40.00 line is non-taxable. The discount is unchanged. The invalid inputs return `400`, client amounts are ignored, and nothing is persisted |
| AC-09 | The page with optional items | Options are toggled | Totals update only from `calculate` responses. **Approve quote** is disabled while a calculation is pending. A failed calculation reverts the toggle and shows the BR-11 error. The totals change is announced |
| AC-10 | A `sent` quote | Approve is attempted unchecked (button disabled; API with `acceptTerms = false`), then submitted with the box checked and one option selected | The unchecked call returns `400` `errors.acceptTerms`. The approval stores one `quote_responses` row (`approved`, organization, version, responder name and contact per BR-13, IP, server totals) and one `quote_response_optional_lines` row. The quote is `approved` with `approved_version_id` = that version and one `quote.approved` audit row with the BR-22 metadata. The page shows "Approved on <date>" with the approved total and **Download PDF** |
| AC-11 | The approval of AC-10 | The database is inspected | The request is still `quoted`, and no work order, visit, assessment, invoice, payment or notification row exists |
| AC-12 | A `sent` quote | Decline is opened and confirmed with an empty reason, then a 1001-character reason, then a valid one | The first two return `400` `errors.reason` and change nothing. The valid one stores a `rejected` response with the reason as comment and null totals, sets `rejected`, writes `quote.rejected` without the reason, sets a new `quotes.updated_at`, and shows the declined state with no actions. A staff **Revise quote** with the `updatedAt` read before the decline returns `409` `quote_changed` |
| AC-13 | A `sent` quote | A question is sent, sent again, and then the quote is approved | The first question stores one `clarification_requested` response, sets the status, writes the audit row, and shows the confirmation with **Ask a question** hidden. The second returns `200` and stores nothing. Approval then succeeds from `clarification_requested` |
| AC-14 | An approved quote, and separately a declined one | The same action is repeated (approve with a different selection; decline with another reason), and then a different action and a question are sent | Repeats return `200` with the original response unchanged and no new rows or audit. Different actions and questions return `409` `quote_already_answered`. The page reloads into the final state with the toast |
| AC-15 | A `sent` quote | Two approvals, two declines, and one approval with one decline run concurrently | Identical pairs store exactly one response, and both receive `200`. The mixed pair yields exactly one final response, and the other gets `409`. The quote status matches the stored response |
| AC-16 | An `approved` and a `rejected` quote whose `valid_until` passes | Their link is opened before and after `expires_at` | Before, the final state shows. After, the generic `404`. The status never becomes `expired` |
| AC-17 | A `sent` version before a response, and the same quote after approval with one option | The PDF is downloaded in each state | Each is `application/pdf` attachment `<Q-n>-v<n>.pdf` with `no-store`, with the BR-19 content. Before: optional items marked not included, with version totals. After: the selected option "Included", approved totals and "Approved on <date>". No BR-05 exclusion appears |
| AC-18 | A client | It exceeds each BR-20 group limit | The excess request returns `429` with `Retry-After`, and the page shows the BR-20 text. Other groups for the same client keep working within their limits |
| AC-19 | Every public operation of this feature, including failures | Logs, audit rows and problem details are inspected | None contains the raw token, its hash, reasons, questions, responder names or contact data. Audit rows have null actor, IP, branch and the BR-22 metadata only |
| AC-20 | A `sent` quote whose `valid_until` has passed, and a quote approved while its version-2 revision is a draft | An Owner opens `/quotes/<id>`, and sends the revision | The first shows the chip "Expired" from `displayStatus` (the stored status is unchanged until a public endpoint touches it). The second's send returns `409` `quote_changed` with nothing changed, and its draft can be discarded |
| AC-21 | Quotes with an approval (with options), a decline and a question | An Owner and a Viewer open `/quotes/<id>`, and a Dispatcher of another branch and an Owner of another organization request it | Owner and Viewer see the **Customer response** card per BR-23. The others receive `404` |
| AC-22 | The BR-25 amendments applied | Rows violating them are inserted directly: an approved response without totals; a rejected response with totals; a second final response for a version; a selection line of another version; `approved_version_id` of another quote | Each is rejected by the database |
| AC-23 | Desktop, tablet and mobile widths | The page is used in the `sent`, approved, declined, question-sent, generic and superseded states | Layouts follow the responsive rules with no horizontal page scroll. Price details become stacked rows on mobile. The BR-05 and BR-09 accessibility rules hold, and the page matches Design 4 apart from the listed deviations |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Option totals calculator, table-driven: none/one/all options, discount unchanged, mixed-rate tax label, frozen values only | AC-08 |
| Backend integration | Token resolution table: generic `404` cases, superseded `410`, lazy expiry with audit, approved/rejected never expiring | AC-02, AC-03, AC-16 |
| Backend integration | Public content: field exclusions, frozen data after settings changes, photo and logo scoping and headers | AC-04, AC-05, AC-06, AC-07 |
| Backend integration | Responses: approve with selection and audit, decline, clarification repeat, idempotent repeats and `409`, concurrent mixed and identical submissions, no downstream rows, request stays `quoted` | AC-10, AC-11, AC-12, AC-13, AC-14, AC-15 |
| Backend integration | PDF content before and after approval (text extraction), and the send guard after approval | AC-17, AC-20 |
| Backend integration | Schema constraints of BR-25 through direct inserts | AC-22 |

Integration volume justification: the groups above may exceed the default
3–8 backend integration methods. The feature exposes a public bearer secret,
financial totals, multi-row response persistence and concurrent submissions,
all boundaries `CLAUDE.md` requires to be evidenced. Equivalent inputs are
table-driven.
| Authorization/tenant isolation | One cross-organization token/photo/selection case, the internal response card's cross-organization and out-of-scope `404`, rate limits, and log/audit content | AC-07, AC-18, AC-19, AC-21 |
| Frontend component/service | Fragment capture and URL replacement with no API call before it; the `/quote-approval` redirect; state rendering (actions, final, generic, superseded); totals only from the server and disabled approve while pending; dialog validation and `409` reload | AC-01, AC-02, AC-03, AC-09, AC-10, AC-12, AC-13, AC-14 |
| Frontend component/service | Internal Customer response card and `displayStatus` chip | AC-20, AC-21 |
| Visual QA (user-supplied) | Design 4 match, responsive layouts, accessibility and every page state | AC-04, AC-23 |

## Dependencies

- `quote-builder` (AUDITED): versions, frozen lines and totals, tokens, link
  builder, email, the `/quotes/:quoteId` view and the calculator conventions.
  BR-01, BR-02, BR-23 and BR-24 change it.
- `requests-pipeline` (IMPLEMENTED): request data, the `quoted` status and
  the read policy.
- `schedule-assessment` (AUDITED) and `quote-builder` BR-01 to BR-03:
  completed assessments and photos.
- `company-settings-and-branches` (implemented): organization name, phone,
  logo, time zone and prefix, and the branch phone.
- `public-service-request` (implemented): public endpoint and rate-limit
  precedent, and the service address.
- `products-services` (implemented): line types only. The catalog is not
  read.
- Future work order spec: consumes `approved_version_id` and moves the
  request to `converted`.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | The organization phone shown is the quote branch phone when set, else the organization phone (confirmed 2026-10-05). |
| AS-02 | Static copy ("No payment due today", "Approval does not charge your payment method.", "What happens next?", "No deposit required.") reflects the existing non-goals for payments and deposits. |
| AS-03 | Photos are not embedded in the PDF to keep it small. They stay available on the page. |
| AS-04 | The subtitle uses the frozen `scope` verbatim ("Review the proposed work for <scope>.") instead of the mockup's lowercase phrasing. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Link format | `/quotes/view#token=` · `/quotes/view/:token` | No | Fragment, POST bodies, old links redirected (BR-01, BR-02) — user 2026-10-05 |
| OD-02 | Assessment photos | All photos of the latest completed assessment · manager selection · none | No | All photos of the latest completed assessment; diagnosis stays internal (BR-07) — user 2026-10-05 |
| OD-03 | Scope of work list | Non-optional line names · recommended scope · omit | No | Non-optional line names (BR-05) — user 2026-10-05 |
| OD-04 | Link after a final response | Final state until expiry · consumed | No | Final state with PDF until `expires_at` (BR-06, BR-18) — user 2026-10-05 |
| OD-05 | Clarification | One per version, approve/decline still allowed · many | No | One per version, idempotent; approve/decline remain (BR-15) — user 2026-10-05 |
| OD-06 | Decline reason | Required free text · preset reasons | No | Required free text 1–1000 (BR-14) — user 2026-10-05 |
| OD-07 | Expiry | Lazy · background job | No | Lazy on public access, derived display internally (BR-18, BR-23) — user 2026-10-05 |
| OD-08 | Staff visibility | Card on `/quotes/:id` · plus email · none | No | Customer response card, no notifications (BR-23) — user 2026-10-05 |
| OD-09 | PDF | PDFsharp-MigraDoc · QuestPDF · browser print | No | PDFsharp-MigraDoc, dependency authorized (BR-19) — user 2026-10-05 |
| OD-10 | Persistence package | As proposed · adjusted | No | Confirmed with precisions: options of the same immutable version and `is_optional`; server-calculated totals only for `approved`; same action → existing, different → `409`; approved/rejected never expire; `approved_version_id` of the same quote; composite FKs; request stays `quoted` (BR-10 to BR-18, BR-25) — user 2026-10-05 |
| OD-11 | Audit data | Version, time, IP, user agent, terms acceptance, option ids | No | Confirmed (BR-13, BR-22) — user 2026-10-05 |
| OD-12 | Header | Brand only · mockup portal header | No | Brand only, no navigation, avatar or breadcrumb (BR-05) — user 2026-10-05 |
| OD-13 | Rate limits | BR-20 values | No | Confirmed (BR-20) — user 2026-10-05 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-03, AC-16 |
| FR-03 | AC-04, AC-05, AC-06, AC-23 |
| FR-04 | AC-07 |
| FR-05 | AC-07, AC-08, AC-09 |
| FR-06 | AC-10, AC-22 |
| FR-07 | AC-12 |
| FR-08 | AC-13 |
| FR-09 | AC-12, AC-14, AC-15 |
| FR-10 | AC-02, AC-16 |
| FR-11 | AC-17 |
| FR-12 | AC-18, AC-19 |
| FR-13 | AC-10, AC-12, AC-13, AC-19 |
| FR-14 | AC-20, AC-21 |
| FR-15 | AC-22 |
| FR-16 | AC-11 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-05 | — → DRAFT | Created |
| 2026-10-05 | DRAFT → DRAFT | Revised after validation: organization-local public dates, connection remote IP, `updated_at` on every customer transition (BR-26), declined copy, superseded precedence, wording cleanups, integration volume justification |
| 2026-10-05 | DRAFT → APPROVED | Approved by user via /spec approve |
