# Invoice draft delivery

| Field    | Value                    |
| -------- | ------------------------ |
| Feature  | `invoice-draft-delivery` |
| Type     | Full-stack               |
| Status   | APPROVED                 |
| Created  | 2026-10-08               |
| Updated  | 2026-10-08               |
| Approved | 2026-10-08               |

## Context and objective

`completed-jobs-review` creates a **draft** invoice with frozen quote amounts,
but nobody can open, review or deliver it. This feature delivers Design 12 at
`/invoices/:invoiceId`: a full invoice preview, editable delivery data
(payment terms, recipient and message) saved with optimistic concurrency, a
server-generated PDF, and an idempotent **Send invoice** that freezes the
customer-facing content, moves the invoice `draft → sent`, creates a secure
public link and emails the customer after commit. The customer can open a
strictly read-only public invoice. No payment, SMS or attachment is
processed.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View any invoice and download its PDF in all branches; save the draft; send; resend the email | Record payments, void, edit lines or amounts, send SMS |
| Accounting (`accounting`) | The same as Owner, in branch scope (BR-02) | Anything outside branch scope (`404`); the Owner's Cannot list |
| Operations Manager (`operations_manager`), Viewer (`viewer`) | View invoices and download PDFs in all branches | Save, send or resend (`403`) |
| Dispatcher (`dispatcher`) | View invoices and download PDFs in branch scope | Anything outside branch scope (`404`); save, send or resend (`403`) |
| Technician (`technician`), unknown roles | — | Every internal endpoint (`403`); the existing forbidden state in the app shell |
| Customer (anonymous link holder) | With a valid token: view the frozen sent invoice and download its PDF | Sign in, pay, accept, edit, see drafts, other invoices, internal data or other organizations |

## Scope

- Route `/invoices/:invoiceId` inside the authenticated app shell (Design 12).
- Invoice preview, header, metadata and activity footer.
- **Save draft** of delivery data with optimistic concurrency.
- Internal PDF download for drafts (marked "DRAFT") and sent invoices.
- **Send invoice**: freeze, `draft → sent`, token, audit, email after commit;
  idempotent.
- **Resend email** for sent invoices.
- Public read-only route `/invoices/view` with token, public PDF and logo.
- A "View invoice" action on the `completed-jobs-review` generation toasts
  (BR-27).
- Schema amendments SA-08, SA-09 and SA-10 (approved 2026-10-08) in one
  migration generated and never applied.

## Non-goals

- Payments, online payment, cash recording, accepted payment methods
  persistence or any `payments` / `payment_allocations` write.
- Real SMS, completion report, photo or PDF attachments in the email; changes
  to `IEmailSender`.
- Customer email verification.
- Editing issue date, lines, amounts, discount, taxes or currency.
- Voiding, overdue transitions, credit notes.
- Drafts, Sent, Payments and Overdue list pages (they stay coming-soon).
- Customer acceptance, disputes or questions on the public invoice.
- Browser automation or Playwright by agents.

## User flow

1. Owner or Accounting generates a draft in **Invoices → Needs review** and
   selects **View invoice** on the toast, or opens `/invoices/<id>`.
2. The system shows the invoice preview and the **Send invoice** panel
   prefilled with the default recipient and message.
3. The user changes terms, recipient or message and selects **Save draft**,
   and optionally **Download PDF**.
4. The user selects **Send invoice** and confirms. The system freezes the
   invoice, marks it sent, creates the link and emails the customer after
   commit, then shows the sent state.
5. The customer opens the emailed link and sees the read-only invoice with
   **Download PDF**.
6. When the email failed or the customer needs it again, Owner or Accounting
   selects **Resend email**.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every internal endpoint must resolve organization and membership from the session and apply role permissions and branch scope per BR-01 and BR-02. |
| FR-02 | The invoice detail must return the preview, delivery data, readiness checks and activity per BR-03 to BR-07. |
| FR-03 | **Save draft** must validate and persist delivery data with optimistic concurrency only while the invoice is `draft` per BR-08 to BR-11. |
| FR-04 | The internal PDF must be generated server-side from the stored invoice per BR-12. |
| FR-05 | **Send invoice** must freeze, transition, tokenize and audit in one transaction, idempotently, and email after commit per BR-13 to BR-16. |
| FR-06 | **Resend email** must rotate the token and email again without changing amounts or `sent_at` per BR-17. |
| FR-07 | The public link must resolve a token to exactly one sent invoice and expose only read-only frozen data, PDF and logo per BR-18 to BR-22. |
| FR-08 | The persistence model must apply SA-08 to SA-10 per Data and persistence impact. |
| FR-09 | The internal page must render Design 12 with its states, interactions and deviations per BR-23 to BR-27. |
| FR-10 | The public page must render the read-only invoice per BR-28. |
| FR-11 | Both pages must meet the responsive, accessibility and fidelity rules of BR-29. |
| FR-12 | Audit rows and logs must record invoice operations without free texts, contact data or tokens per BR-30. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Roles (Invoices module). Read policy (`GET` detail, `GET` PDF): `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`. Action policy (`PUT` draft, `POST` send, `POST` resend-email): `owner`, `accounting`. Other roles → `403` before any read or write. No organization id is accepted from the client. | Backend |
| BR-02 | Branch scope (`completed-jobs-review` BR-02) applied to `invoices.branch_id`. An invoice of another organization, outside scope, unknown, or `void` → identical `404` "This invoice isn't available." with no data and no write. | Backend |
| BR-03 | Header and metadata: `<invoice_prefix>-<invoice_number>`, status (`draft` "Draft", `sent` "Sent"; other statuses render read-only with their name), customer display name, issue date, due date, work order `<work_order_prefix>-<number>` with its id, all dates `MMM d, yyyy` in the organization timezone. Activity footer: "Invoice created from completed job <WO-n> · Not yet sent" with `created_at` and creator full name; when sent, "Sent <MMM d, yyyy> at <h:mm a> to <recipient>" instead of "Not yet sent". | Both |
| BR-04 | Preview content. Organization block: logo (or initials), name, `address_line1`, "<city>, <state_region> <postal_code>", phone, email (each line hidden when null). Invoice block: "INVOICE", number, issue date, due date, work order. **Bill to**: customer display name, email and phone (primary active contact, else customer primary values) and billing address lines (`customers.billing_address`, hidden when null). **Service address**: the work order property one-line address. Lines: `invoice_lines` in `sort_order` with Description (`description`, plus the source quote line `description` as secondary text when not empty), Qty (quantity without trailing zeros, with unit unless `unit`), Rate (`unit_price`), Tax (`tax_rate` as "<r>%", "—" when 0), Amount (`line_subtotal`). Totals: Subtotal, "Discount −<amount>" when > 0, tax label (`quote-builder` BR-15 over the invoice lines) and amount, **Total due** (`total`). "Service completion note": the `completion_summary` of the work order's latest completed visit (by `actual_completed_at`), hidden when empty. Footer "Thank you for your business!". Never shown: unit cost, margin, internal notes, billing review note, variances, signature content. | Both |
| BR-05 | Frozen source. While `draft`, Bill to, service address and completion note are read live. When `sent` (and later statuses), they are read only from `invoices.customer_snapshot` (SA-09); later changes to the customer, contacts, property or visits never alter the preview, PDF or public view. Organization block data is read live (AS-02). Amounts and lines always come from `invoices` and `invoice_lines`. | Backend |
| BR-06 | Delivery defaults. When `recipient_email` is null, the detail returns the default recipient: the customer's primary active contact email, else `customers.primary_email`, else empty. When `delivery_message` is null, it returns the template "Hi <contact first name, or customer display name, or 'there'>,\n\nHere's your invoice <INV-n> for <work order title>. Thank you for choosing <organization name>! If you have any questions, please don't hesitate to reach out.\n\nBest regards,\n<organization name>" truncated to 500 characters. `delivery.saved` is false until the first save or send. | Backend |
| BR-07 | Readiness checks (informational, returned by the backend): "Recipient email added" (met when the effective recipient is a valid email) and "Tax checked (<tax label>)" (always met). The mockup's "Completion report attached" and "Customer email verified" are not returned. | Both |
| BR-08 | Delivery field rules (table below). `paymentTerms` changes `invoices.payment_terms` and recomputes `due_date` = `issue_date` + 0, 15 or 30 days. `issue_date` is never changed. Client amounts, dates, statuses or ids in bodies are ignored or rejected as unknown. | Both |
| BR-09 | Save. `PUT /invoices/{id}/draft { paymentTerms, recipientEmail, message, updatedAt }` while `status = draft`. Recipient may be empty on save (stored null). In one transaction, after locking the invoice row: compare `updatedAt`, validate, store `payment_terms`, `due_date`, `recipient_email`, `delivery_message`, `updated_at = now`, audit `invoice.draft_saved`. Response `200 InvoiceDetail`. Sending identical values still succeeds and updates `updated_at` only when something changed (no audit otherwise). | Backend |
| BR-10 | Optimistic concurrency. `invoices.updated_at` is the token for save, send and resend. A mismatch or a lost race → `409 invoice_changed` "This invoice changed. Refresh to see the latest." with no write. Of a concurrent save and send with the same `updatedAt`, exactly one succeeds. | Backend |
| BR-11 | Draft-only edits. `PUT …/draft` on a non-`draft` invoice → `409 invoice_not_draft` "This invoice has already been sent and can't be edited." with no write (checked after locking, before `updatedAt`). | Backend |
| BR-12 | Internal PDF. `GET /invoices/{id}/pdf` returns `application/pdf`, `Content-Disposition: attachment; filename="<INV-n>.pdf"`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`. Generated server-side with the existing PDFsharp-MigraDoc infrastructure (`customer-quote-approval` BR-19) from stored data per BR-04/BR-05, never from browser data. Delivery data (recipient, message) never appears in the PDF. Drafts carry a visible "DRAFT" mark on every page; sent invoices carry no mark. The PDF contains no logo bytes when none exist, no photos and no BR-04 exclusions. | Backend |
| BR-13 | Send request `POST /invoices/{id}/send { paymentTerms, recipientEmail, message, updatedAt }`. Recipient is required on send (`errors.recipientEmail` "Enter the customer's email address."). The page asks first in the dialog "Send invoice?" — "<customer name> will receive <INV-n> for <total> at <recipient>. A sent invoice can't be edited." — **Send invoice** / **Cancel**. | Both |
| BR-14 | Send effect, in order after BR-01/BR-02 and locking the invoice row: (1) `status <> draft` (and not void) → `200 { changed: false, emailStatus: "not_sent", invoice }`, no write, no email; (2) `updatedAt` mismatch → BR-10; (3) invalid body → `400`; (4) in one transaction: store delivery data and terms per BR-09; set `status = sent`, `sent_at = now`, `customer_snapshot` (SA-09) from the live BR-04 data, `updated_at = now`; revoke any unrevoked token of the invoice; create one token (BR-18); audit `invoice.sent`. Any failure rolls back everything. Response `200 { changed: true, emailStatus, invoice: InvoiceDetail }`. | Backend |
| BR-15 | Idempotency and concurrency. Repeated or concurrent sends produce exactly one `draft → sent` transition, one `sent_at`, one active token, one `invoice.sent` audit row and one email; the others return `changed = false` with `emailStatus = not_sent`. | Backend |
| BR-16 | Email after commit through the existing `IEmailSender` (a composer following the quote email precedent). Subject: "<organization name>: invoice <INV-n>". Text and HTML bodies: the stored message (HTML-encoded, line breaks kept), "Invoice <INV-n> · Total due <total> · Due <MMM d, yyyy>", the link "View your invoice: <link>", the organization name and, when set, "Questions? Call us at <organization phone>.". No attachments. `emailStatus` = `sent` or `failed`. On failure only the invoice id and failure category are logged, the response is still `200`, and the send is never rolled back. | Backend |
| BR-17 | Resend. `POST /invoices/{id}/resend-email { updatedAt }` when `status = sent` and a recipient is stored, else `409 invoice_not_sent` "Only sent invoices can be emailed again.". In one transaction: revoke the invoice's unrevoked tokens, create a new token, set `updated_at = now`, audit `invoice.email_resent`. Then the same email (BR-16) with the stored message. `sent_at`, amounts, terms, dates, recipient and snapshot are unchanged. Response `200 { emailStatus, invoice: InvoiceDetail }`. | Backend |
| BR-18 | Token and link (`quote-builder` BR-27 precedent). 32 random bytes from a cryptographic RNG, base64url without padding; only the SHA-256 hex hash is stored in `invoice_access_tokens` (SA-10). `expires_at` = token `created_at` + 365 days. Link: `<first allowed frontend origin>/invoices/view#token=<raw>`. The raw token exists only in the email, never in responses, logs, audit or the UI. | Backend |
| BR-19 | Public resolution. Every public call sends the token in a JSON POST body. The server hashes it and accepts it only when the row exists, `revoked_at` is null, `expires_at` > now, and the invoice is not `draft` or `void`. Any other case, including malformed input → `404 invoice_link_unavailable` "This link isn't available." with no branding or invoice data. | Backend |
| BR-20 | Public content `PublicInvoice`: the BR-04 preview from frozen data (BR-05) plus status label ("Sent"), and `hasLogo`. It never contains organization, invoice, customer, line or work order ids, the work order link, internal notes, delivery message, recipient, audit or creator data. Endpoints: `POST /public/invoice-links/view`, `POST /public/invoice-links/pdf` (BR-12 content without "DRAFT", headers per `customer-quote-approval` BR-07), `POST /public/invoice-links/logo` (`customer-quote-approval` BR-08). | Backend |
| BR-21 | Public hardening (`customer-quote-approval` BR-20/BR-21 precedent): read group (`view`, `logo`) 60 per 5 minutes per client and 600 per minute global; PDF 10 per 5 minutes per client and 100 per minute global; excess → `429` with `Retry-After`. `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, request bodies ≤ 16 KB (`413`). Logs may contain only the invoice id and outcome category. | Backend |
| BR-22 | Public reads write nothing (no status change, no audit, no view tracking). | Backend |
| BR-23 | Route and navigation. `/invoices/:invoiceId` is a lazy route in the app shell for the BR-01 read roles; `technician` gets the existing forbidden state. `/invoices/view` is a public route outside the shell, declared so it is never matched as `/invoices/:invoiceId`. `/invoices/review` keeps precedence. The Invoices navigation group is highlighted; its sub-items are unchanged. Breadcrumb "Billing / Invoices / <INV-n>" (Invoices → `/invoices/review`). The work order link targets `/jobs/<workOrderId>`. | Frontend |
| BR-24 | Internal layout. Header: title "Invoice <INV-n>", status chip (text and icon), metadata row (BR-03), actions **Download PDF**, **Save draft**, **Send invoice** (primary). Left: preview card (BR-04) and activity footer. Right panel "Send invoice", subtitle "Send this invoice to your customer by email.": **Recipient email** (input), **Send SMS notification** toggle off and disabled with "SMS isn't available yet.", **Payment terms** select ("Due upon receipt", "Net 15", "Net 30"), **Due date** read-only, recalculated on terms change; **Accepted payment methods** Card (credit/debit), Bank transfer (ACH), Cash all unchecked and disabled; **Message to customer** textarea with counter "<n>/500"; **Attach completion report** and **Attach before & after photos** toggles off and disabled; the text "Online payments and attachments will be available later."; readiness checks (BR-07) with met/unmet icon and text. Disabled controls are never sent or stored. | Frontend |
| BR-25 | States. Loading: skeleton for header, preview and panel. `404` → "This invoice isn't available." with a link to **Needs review**. Load error → "We couldn't load this invoice. Try again." with **Retry**. Read-only roles (`canAct` false): delivery controls read-only, **Save draft** and **Send invoice** hidden, text "Only owners and accounting can edit and send invoices."; **Download PDF** stays. Sent: panel title "Delivery", fields read-only showing the stored recipient, terms and message, "Sent <date> at <time>", **Send invoice** and **Save draft** hidden, **Resend email** shown for `canAct`. | Frontend |
| BR-26 | Interactions. **Save draft** is disabled while pending or unchanged; success → "Draft saved.". **Send invoice** validates client-side, focuses the first invalid field, then opens the BR-13 dialog. Send with `emailStatus = sent` → "Invoice <INV-n> sent."; `failed` → warning "Invoice sent, but we couldn't email it. Use Resend email to try again."; `changed = false` → "Invoice <INV-n> was already sent." and the page reloads. **Resend email** → "Invoice email sent again." or the failure warning. `409 invoice_changed` / `invoice_not_draft` / `invoice_not_sent` → their messages and reload; `400` → field messages; other failures → "We couldn't complete this action. Try again." keeping every entered value. While a request is pending all actions are disabled and the clicked one shows progress. **Download PDF** shows progress; failure "We couldn't download the PDF. Try again.". Leaving with unsaved changes shows the shared discard-changes dialog. Messages are announced politely. | Frontend |
| BR-27 | `completed-jobs-review` change: the toasts "Draft invoice <number> created." and "Draft invoice <number> already exists." gain the action **View invoice** that opens `/invoices/<invoice id>`. Its generate response is unchanged (it already returns `invoice.id`); nothing else in that feature changes. | Frontend |
| BR-28 | Public page `/invoices/view`. Follows `customer-quote-approval` BR-01: captures `token` from the fragment into tab-scoped storage, replaces the URL with `/invoices/view` before any call, sends it only in POST bodies; without a token shows the generic state with no call. Layout: organization header (logo or initials, name), the BR-04 preview from `PublicInvoice`, **Download PDF**, the text "Online payment will be available soon." and the footer "Powered by FieldOps". No navigation, actions, inputs, payment, acceptance or editing. States: loading skeleton; generic "This invoice link isn't available. It may have expired or been replaced. Please contact the company that sent it."; `429` "Too many attempts. Please wait a few minutes and try again."; error "We couldn't load this invoice. Try again." with **Retry**; PDF failure "We couldn't download the PDF. Please try again.". | Frontend |
| BR-29 | Responsive, accessibility and fidelity. ≥ 1280 px preview and panel side by side as Design 12; < 1280 px panel below the preview; < 768 px header actions wrap to full width and the line table scrolls inside its card; no horizontal page scroll at 375 px. The line table has header cells; status and checks never rely on color alone; dialogs trap and restore focus; field errors are linked to fields; disabled controls expose their explanation; targets ≥ 44 × 44 px on touch layouts. Design 12 deviations: terms list (no "Net 14"); due date read-only; no "Verified" badge; SMS, payment methods and attachment controls disabled and unchecked; checks per BR-07; panel subtitle per BR-24; mockup data are examples. | Frontend |
| BR-30 | Audit (`entity_type` `invoice`, `entity_id` invoice id, organization, invoice `branch_id`, actor). `invoice.draft_saved` (metadata `{ changedFields }` names only); `invoice.sent` (before/after `{ status }`, metadata `{ invoiceNumber, total, currency, notified: "email" }`); `invoice.email_resent` (metadata `{ invoiceNumber }`). Audit rows, logs and problem details never contain the recipient, message, customer names, addresses, phones or tokens. | Backend |

### Delivery field rules

| Field | Rule | Message when invalid |
| ----- | ---- | -------------------- |
| `paymentTerms` | `due_upon_receipt`, `net_15`, `net_30` | "Select payment terms." |
| `recipientEmail` | Trimmed, ≤ 254 characters, valid email format; empty allowed on save (stored null), required on send | "Enter a valid email address." / "Enter the customer's email address." |
| `message` | Trimmed, 1–500 characters, required on save and send | "Enter a message." / "Message must be 500 characters or fewer." |
| `updatedAt` | Required ISO instant equal to the stored value | `409 invoice_changed` |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Invoice `draft` | `draft` (delivery data updated) | Save draft | Owner, Accounting | BR-09–BR-11 |
| Invoice `draft` | `sent` + token + snapshot | Send invoice | Owner, Accounting | BR-13, BR-14 |
| Invoice `sent` | unchanged | Send invoice | Owner, Accounting | `200 changed = false`, no email |
| Invoice `sent` | `sent` (token rotated) | Resend email | Owner, Accounting | BR-17 |
| Invoice not `draft` | unchanged | Save draft | Owner, Accounting | `409 invoice_not_draft` |
| Token active | revoked | Send (earlier tokens) / Resend | Owner, Accounting | Same transaction |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): `organizations`
  (`name`, `email`, `phone`, address columns, `timezone`, `invoice_prefix`,
  `work_order_prefix`), `organization_logos`, `organization_users`,
  `organization_user_branches`, `branches`, `customers`, `customer_contacts`,
  `properties`, `work_orders`, `visits` (`completion_summary`,
  `actual_completed_at`), `quote_lines` (`description` via
  `source_quote_line_id`), `invoices`, `invoice_lines`, `users`.
- Written: `invoices` (`payment_terms`, `due_date`, `recipient_email`,
  `delivery_message`, `status`, `sent_at`, `customer_snapshot`,
  `updated_at`), `invoice_access_tokens`, `audit_logs`.
- Not written: `invoice_lines`, amounts, `issue_date`, `payments`,
  `payment_allocations`, `notifications`, customer, work order or visit
  tables.
- Schema amendments: **approved by the user 2026-10-08** (OD-12).
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model
  and generates one migration only under `--generate-migration`. Agents never
  apply it. Review by `database-reviewer`.
  - SA-08 `invoices`: add `recipient_email varchar(254) NULL` and
    `delivery_message varchar(500) NULL`.
  - SA-09 `invoices`: add `customer_snapshot jsonb NULL` and
    `CHECK (status = 'draft' OR status = 'void' OR customer_snapshot IS NOT
    NULL)`. Shape: `{ billTo: { name, email, phone, addressLines[] },
    serviceAddress, completionNote }`. Existing rows are all `draft`
    (`completed-jobs-review` creates only drafts); if any non-draft,
    non-void row lacks a snapshot, the migration fails instead of altering
    data.
  - SA-10 new `invoice_access_tokens (id uuid PRIMARY KEY DEFAULT
    gen_random_uuid(), organization_id uuid NOT NULL REFERENCES
    organizations(id), invoice_id uuid NOT NULL, token_hash text NOT NULL
    UNIQUE, expires_at timestamptz NOT NULL, revoked_at timestamptz,
    created_by_user_id uuid NOT NULL REFERENCES users(id), created_at
    timestamptz NOT NULL DEFAULT now(), FOREIGN KEY (organization_id,
    invoice_id) REFERENCES invoices(organization_id, id), CONSTRAINT
    ck_invoice_access_tokens_expires_after_created CHECK (expires_at >
    created_at))` with `CREATE INDEX ix_invoice_access_tokens_invoice ON
    invoice_access_tokens(invoice_id) WHERE revoked_at IS NULL`.

## Tenant isolation and authorization

- Organization and membership come from the session; BR-01/BR-02 apply
  before any read or write.
- Client-provided identifiers: `invoiceId` (path) only, authorized against
  the session organization and branch scope; otherwise identical `404`.
  Customer, work order, lines, totals and organization data are resolved
  server-side from the authorized invoice. No organization, customer,
  amount, date, status or number is accepted from the client.
- Public endpoints authorize only by token hash (BR-19); the token's
  `organization_id` and `invoice_id` scope every read, including the logo.
- Actions require the action policy; frontend hiding is UX only.
- Responses use `Cache-Control: no-store`. Logs and audit follow BR-30.

## API contracts

Contract status: Final. Internal base path `/invoices`; public base path
`/public/invoice-links`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/invoices/{invoiceId}` | — | `200 InvoiceDetail` | `403` · `404` | Read |
| GET | `/invoices/{invoiceId}/pdf` | — | `200 application/pdf` (BR-12) | `403` · `404` | Read |
| PUT | `/invoices/{invoiceId}/draft` | `{ paymentTerms, recipientEmail, message, updatedAt }` | `200 InvoiceDetail` | `400` · `403` · `404` · `409 invoice_changed`, `invoice_not_draft` | Action |
| POST | `/invoices/{invoiceId}/send` | `{ paymentTerms, recipientEmail, message, updatedAt }` | `200 { changed, emailStatus: "sent"\|"failed"\|"not_sent", invoice: InvoiceDetail }` | `400` · `403` · `404` · `409 invoice_changed` | Action |
| POST | `/invoices/{invoiceId}/resend-email` | `{ updatedAt }` | `200 { emailStatus: "sent"\|"failed", invoice: InvoiceDetail }` | `403` · `404` · `409 invoice_changed`, `invoice_not_sent` | Action |
| POST | `/public/invoice-links/view` | `{ token }` | `200 PublicInvoice` | `404 invoice_link_unavailable` · `413` · `429` | Anonymous |
| POST | `/public/invoice-links/pdf` | `{ token }` | `200 application/pdf` | `404` · `413` · `429` | Anonymous |
| POST | `/public/invoice-links/logo` | `{ token }` | `200` image | `404` · `413` · `429` | Anonymous |

Errors use ProblemDetails with `code`; `400` includes field `errors`. Money
values are JSON numbers with 2 decimals; instants ISO 8601 UTC; dates ISO
`yyyy-mm-dd`.

`InvoicePreview` (shared by both contracts):

| Field | Shape |
| ----- | ----- |
| `number` | `<prefix>-<n>` |
| `issueDate`, `dueDate` | date |
| `paymentTerms` | `due_upon_receipt` \| `net_15` \| `net_30` |
| `currency`, `timezone` | string |
| `organization` | `{ name, addressLines[], phone \| null, email \| null, hasLogo }` |
| `billTo` | `{ name, email \| null, phone \| null, addressLines[] }` |
| `serviceAddress` | string \| null |
| `workOrderNumber` | `<prefix>-<n>` |
| `lines` | `[{ description, detail \| null, quantity: string, unit, unitPrice, taxRate, amount }]` |
| `totals` | `{ subtotal, discountTotal, taxLabel, taxTotal, total }` |
| `completionNote` | string \| null |

`InvoiceDetail` = `InvoicePreview` + `{ id, status, workOrderId, customerName,
delivery: { recipientEmail, message, saved }, checks: [{ key: "recipient" \|
"tax", met, label }], createdAt, createdByName, sentAt \| null, canAct,
updatedAt }`.

`PublicInvoice` = `InvoicePreview` + `{ status: "sent" }` (BR-20 exclusions).

Internal contracts: the existing `IEmailSender` (unchanged), a new invoice
email composer and link builder following the quote precedents, and the
existing MigraDoc PDF infrastructure extended with an invoice document.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Invoice page | Skeleton (BR-25) | N/A | `404` text; load error + Retry | Technician → forbidden; read-only roles per BR-25 | BR-24 content; sent state per BR-25 |
| Send panel actions | Progress on the clicked action | N/A | BR-26 messages | Hidden for read-only roles | BR-26 messages |
| Send dialog | Progress on confirm | N/A | BR-26 | Action roles only | BR-26 |
| Public invoice | Skeleton | N/A | Generic, `429`, error + Retry | Anonymous; token only | BR-28 content |

- Mockups: `design/assets/12-design.png` (Approved; deviations in BR-29).
  The public page has no mockup and reuses the Design 12 preview card inside
  the `customer-quote-approval` public layout.
- Responsive and accessibility constraints: BR-29.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Role without read permission | `403`; forbidden state | None |
| Read-only role calls an action | `403`; "Only owners and accounting can edit and send invoices." | None |
| Foreign, out-of-scope, unknown or void invoice | Identical `404`; "This invoice isn't available." | None |
| Invalid delivery field | `400` with field errors | None |
| Stale `updatedAt` or lost race | `409 invoice_changed` | None |
| Save on a sent invoice | `409 invoice_not_draft` | None |
| Resend on a draft | `409 invoice_not_sent` | None |
| Send on a sent invoice | `200 changed = false`; "Invoice <INV-n> was already sent." | None, no email |
| Email failure after send or resend | `200 emailStatus = failed`; warning toast | Never rolled back |
| Failure inside the send transaction | `500`; "We couldn't complete this action. Try again." | Full rollback |
| Invalid, revoked, expired, draft or void token | `404 invoice_link_unavailable`; generic text | None |
| Public rate limit | `429`; "Too many attempts…" | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Members of every role, including dispatcher and accounting with and without `is_all_branches` | They call each internal endpoint (table-driven) | Read roles get `200` within branch scope; technician and unknown roles get `403`; operations manager, viewer and dispatcher get `403` on save, send and resend with no write |
| AC-02 | Invoices of another organization, outside branch scope, void, and random ids | They are requested or mutated | Identical `404` with no foreign data and no write (one representative case per path family: detail/PDF and mutation) |
| AC-03 | A draft invoice with discount, mixed tax rates, line descriptions, billing address and a completion summary | The detail is loaded | Header, metadata, preview, totals, tax label, completion note, activity footer and checks follow BR-03, BR-04 and BR-07; no excluded data is returned |
| AC-04 | Drafts with and without a primary contact, customer email or stored delivery data | The detail is loaded | Default recipient, template message (with fallbacks and 500-character truncation) and `saved` follow BR-06 |
| AC-05 | A draft | Save is sent with each terms value, valid and invalid recipient and message values (table-driven) | Terms, recomputed due date, unchanged issue date, stored recipient/message, `400` field errors, audit `invoice.draft_saved` without texts follow BR-08, BR-09 and the field rules |
| AC-06 | A draft and a sent invoice | Save with a stale `updatedAt`, concurrent save and send with the same `updatedAt`, and save on the sent invoice are sent | `409 invoice_changed` with no write; exactly one of the concurrent pair succeeds; `409 invoice_not_draft` on the sent invoice |
| AC-07 | A draft and a sent invoice | The internal PDF is downloaded | `application/pdf` with BR-12 headers and filename; the draft has the "DRAFT" mark and the sent one does not; content follows BR-04/BR-05 |
| AC-08 | A ready draft | Send succeeds | Delivery data stored, `status = sent`, `sent_at` set, snapshot stored, one active token stored only as hash with 365-day expiry, audit `invoice.sent`; amounts, lines and issue date unchanged; email sent after commit with BR-16 subject, body and link; `emailStatus = sent` |
| AC-09 | A ready draft | Send is posted twice sequentially and twice concurrently | Exactly one transition, one `sent_at`, one active token, one audit row and one email; other calls return `changed = false`, `emailStatus = not_sent` |
| AC-10 | A draft with no recipient, invalid fields, and a failure injected after the status update | Send is posted | `400` with field errors and no write; the injected failure rolls back status, snapshot, token and audit |
| AC-11 | The email sender throws | Send and then Resend are posted | Send returns `200 emailStatus = failed` with the invoice sent and token persisted; logs contain only invoice id and failure category. Resend revokes the earlier token, creates a new one, writes `invoice.email_resent`, keeps `sent_at`, amounts and snapshot, and sends the email; resend on a draft returns `409 invoice_not_sent` |
| AC-12 | A sent invoice | Customer, contact, property and visit summary change afterwards | Detail, internal PDF and public view still show the snapshot values (BR-05) |
| AC-13 | Tokens: valid, revoked by resend, expired, malformed, of a draft and of a void invoice | Public view, PDF and logo are requested | Valid returns frozen `PublicInvoice`, PDF and logo with BR-20/BR-21 headers and no ids or internal data; every other case returns identical `404 invoice_link_unavailable`; no write occurs |
| AC-14 | Public endpoints under load and oversized bodies | Requests exceed limits | `429` with `Retry-After` per BR-21; bodies over 16 KB → `413` |
| AC-15 | Every operation of this feature | Audit rows and logs are inspected | Actions and fields follow BR-30; none contains recipient, message, names, addresses, phones or tokens; the raw token appears only in the email |
| AC-16 | The EF model and the authoritative schema | The migration is generated | SA-08, SA-09 and SA-10 match exactly; the migration is generated but not applied; `database-reviewer` approves |
| AC-17 | Each role signed in to the app shell | `/invoices/<id>`, `/invoices/review` and `/invoices/view` are opened | Route access, forbidden state for technician, breadcrumb, work order link, navigation highlight and public route precedence follow BR-23 |
| AC-18 | Detail responses for a draft, a sent invoice, `404`, an error and a read-only role | The internal page renders | Header, preview, Send panel with disabled SMS, payment methods and attachments, checks, counter and BR-25 states follow BR-24 and BR-25 |
| AC-19 | A draft opened by Owner | Terms change, save, invalid fields, send confirmation, send outcomes (`sent`, `failed`, `changed = false`), `409` responses and leaving with unsaved changes occur | Due date recalculation, validation focus, dialog text, messages, reload, pending state, value retention and the discard dialog follow BR-26 |
| AC-20 | Sent invoices with email success and failure | **Resend email** and **Download PDF** are used | Messages and progress follow BR-26 |
| AC-21 | Completed jobs review generation returning `201` and `200 changed = false` | The toast's **View invoice** is used | `/invoices/<invoice id>` opens; the rest of `completed-jobs-review` is unchanged (BR-27) |
| AC-22 | A link with a valid token, without a token, with an unavailable token and rate limited | `/invoices/view#token=…` is opened | The fragment is removed before any call, the token is sent only in POST bodies, and content, Download PDF, the payment note and generic/`429`/error states follow BR-28; no action or input exists |
| AC-23 | Design 12 at 1440, 1024 and 375 px and the public page at 375 px, keyboard only | The pages are reviewed (user visual QA) | Layout, deviations, focus, linked errors and non-color indicators follow BR-29 |

## Testing requirements

This feature changes a public secret (token), sensitive outbound email,
optimistic concurrency and an idempotent financial document transition.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Due date from terms; default message template and truncation; token generation/hash format | AC-04, AC-05, AC-08 |
| Backend integration | Detail and preview content, defaults, exclusions; internal PDF draft vs sent | AC-03, AC-04, AC-07 |
| Backend integration | Save validation, audit and concurrency (stale, concurrent save/send, not draft) | AC-05, AC-06 |
| Backend integration | Send effects, idempotency/concurrency, rollback, email after commit and failure, resend | AC-08, AC-09, AC-10, AC-11 |
| Backend integration | Snapshot freezing and public endpoints (token states, content, headers, limits) | AC-12, AC-13, AC-14 |
| Authorization/tenant isolation | Role matrix; one representative cross-tenant and out-of-scope `404` per path family; audit/log content | AC-01, AC-02, AC-15 |
| Persistence | Model matches SA-08–SA-10; `database-reviewer` approval; migration generated, not applied | AC-16 |
| Frontend component/service | Invoice page states and read-only role; save/send/resend flows and messages; public page token handling and states; toast action in billing review; routes | AC-17, AC-18, AC-19, AC-20, AC-21, AC-22 |
| User visual QA | Fidelity, responsive layouts, keyboard and focus | AC-23 |

Backend integration may reach the 8-method default: send idempotency and
rollback are financial-document integrity, and public token resolution is a
public-secret boundary.

## Dependencies

- `completed-jobs-review` (AUDITED): draft invoices, SA-04/SA-05, terms codes,
  generation toast (BR-27 change).
- `quote-builder`: email-after-commit, token, link-builder and optimistic
  concurrency precedents.
- `customer-quote-approval`: public token transport, hardening, rate limits,
  logo endpoint and MigraDoc PDF infrastructure.
- `mobile-job-completion` / `mobile-job-progress`: `visits.completion_summary`.
- `authenticated-app-shell`: shell, navigation and forbidden state.
- Schema amendments SA-08–SA-10 (approved 2026-10-08).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | `/jobs/:id` takes the work order id; non-job roles that open the link get that page's existing permission behavior. |
| AS-02 | The organization block is read live, as the quote PDF and public quote do; organization identity changes after sending are rare and outside the snapshot. |
| AS-03 | Invoice currency equals the frozen quote currency set at generation; formatting follows the existing money formatter. |
| AS-04 | `invoice_lines.description` holds the quote line name (`completed-jobs-review` AS-04); the secondary text comes from the immutable source quote line. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Terms and due date | Existing 3 terms, computed due date / custom term | No | 2026-10-08: 3 terms; due date computed, read-only; issue date fixed |
| OD-02 | Recipient and message | Editable stored email + message / contacts only | No | 2026-10-08: editable email (default primary contact, else customer email); message required 1–500 with editable template; both stored (SA-08) |
| OD-03 | Verified badge and checks | Computed checks / hide | No | 2026-10-08: no Verified badge; checks "Recipient email added" and "Tax checked"; completion report check hidden |
| OD-04 | Unsupported options | Disabled, not persisted / store payment methods | No | 2026-10-08: SMS, payment methods and attachments visible, disabled, never persisted; no `IEmailSender` change |
| OD-05 | Completion note | Show latest summary / hide | No | 2026-10-08: latest completed visit `completion_summary`, hidden when empty |
| OD-06 | Freezing | Snapshot / live | No | 2026-10-08: `customer_snapshot` at send (SA-09) |
| OD-07 | Idempotency and resend | With resend / without | No | 2026-10-08: repeated send `changed = false` without email; resend rotates token, keeps amounts and original `sent_at` |
| OD-08 | Public link | Read-only page / placeholder | No | 2026-10-08: `/invoices/view` strictly read-only with PDF; 365-day token; no payment, acceptance or editing (SA-10) |
| OD-09 | Internal PDF | DRAFT mark / identical | No | 2026-10-08: drafts marked "DRAFT" |
| OD-10 | Entry point | Toast action / navigate / none | No | 2026-10-08: **View invoice** action on the generation toasts |
| OD-11 | Permissions | — | No | 2026-10-08: read roles incl. viewer with branch scope; save, send, resend Owner and Accounting |
| OD-12 | Migration | — | No | 2026-10-08: SA-08–SA-10 in one migration generated with `--generate-migration`, never applied |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03, AC-04 |
| FR-03 | AC-05, AC-06 |
| FR-04 | AC-07 |
| FR-05 | AC-08, AC-09, AC-10, AC-11, AC-12 |
| FR-06 | AC-11 |
| FR-07 | AC-13, AC-14 |
| FR-08 | AC-16 |
| FR-09 | AC-17, AC-18, AC-19, AC-20, AC-21 |
| FR-10 | AC-22 |
| FR-11 | AC-23 |
| FR-12 | AC-15 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-08 | — → DRAFT     | Created from Design 12 with decisions OD-01–OD-12 resolved by the user |
| 2026-10-08 | DRAFT → APPROVED | Approved by user via /spec approve |
