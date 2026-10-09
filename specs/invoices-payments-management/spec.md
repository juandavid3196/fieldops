# Invoices and payments management

| Field    | Value                           |
| -------- | ------------------------------- |
| Feature  | `invoices-payments-management`  |
| Type     | Full-stack                      |
| Status   | AUDITED                         |
| Created  | 2026-10-08                      |
| Updated  | 2026-10-09                      |
| Approved | 2026-10-08                      |

## Context and objective

Invoices can be generated (`completed-jobs-review`) and sent
(`invoice-draft-delivery`), but there is no place to follow balances, due
dates and money received, and `/invoices` only redirects to the review queue.
This feature delivers Design 24 at `/invoices`: a billing hub with balance
metrics, an aging summary, recent payments, and tabs for invoices, payments
and completed jobs ready to invoice, with search, filters, pagination and
CSV export. Owners and accounting record **external** payments (money already
received outside FieldOps), partial or full, in one idempotent transaction
that updates the invoice balance and status, and optionally email a receipt
after commit. No card, refund or online payment is processed.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View the hub, invoices, payments, metrics, aging and the ready tab; export; record external payments, in all branches | Process cards, refund, void or edit payments, edit invoices |
| Accounting (`accounting`) | The same as Owner, in branch scope (BR-02) | Anything outside branch scope (`404`); the Owner's Cannot list |
| Operations Manager (`operations_manager`), Viewer (`viewer`) | View and export everything in the hub, in all branches | Record payments (`403`) |
| Dispatcher (`dispatcher`) | View and export everything in the hub, in branch scope | Anything outside branch scope (`404`); record payments (`403`) |
| Technician (`technician`), unknown roles | — | Every endpoint (`403`); the existing forbidden state in the app shell. A technician may still be named as **Received by** (BR-16) |

## Scope

- Route `/invoices` (Design 24) inside the authenticated app shell, replacing
  its redirect to `/invoices/review`.
- Metrics (Outstanding, Overdue, Draft, Paid this month, Average time to pay),
  aging summary and recent payments.
- Tabs **Invoices**, **Payments** and **Completed jobs ready to invoice**,
  with search, filters, pagination and CSV export (invoices and payments).
- **Record external payment** drawer: partial or full payment, atomic invoice
  balance/status update, idempotency, optimistic concurrency, audit and an
  optional receipt email after commit.
- Navigation changes to the Invoices group (BR-27), the Design 12 breadcrumb
  (BR-28) and work-order preselection in `/invoices/review` (BR-29).
- Schema amendments SA-11, SA-12 and SA-13 (approved 2026-10-08) in one
  migration generated and never applied.

## Non-goals

- Card processing, online payments, payment links, refunds, credit notes,
  voiding, editing or deleting payments.
- Payments not applied to exactly one invoice; unapplied credit; overpayment.
- Persisting the `overdue` enum value or any scheduled status job.
- Manual invoice creation (**New invoice** opens the ready tab, BR-24).
- Bulk selection or bulk actions.
- Invoice view tracking ("Viewed …").
- Resending receipts; receipt PDFs or attachments.
- Changes to `/invoices/:invoiceId` other than its breadcrumb, and any change
  to the public invoice page.
- A branch selector in the app shell header.
- Browser automation or Playwright by agents.

## User flow

1. A read role opens **Invoices** in the navigation; the system shows metrics,
   the **Invoices** tab, aging summary and recent payments.
2. The user searches, filters (date range, status, customer, branch), pages,
   switches tabs or exports CSV.
3. Owner or Accounting opens a row's actions menu and selects **Record
   payment** on a sent, partially paid or overdue invoice.
4. The drawer shows the invoice, customer and outstanding balance; the user
   enters amount, date, method, reference, received by, receipt option and
   note, and selects **Record external payment**.
5. The system records the payment and updates the invoice in one
   transaction, emails the receipt after commit when requested, announces the
   outcome and refreshes the hub.
6. In **Completed jobs ready to invoice**, a row opens `/invoices/review` with
   that job selected to generate its draft invoice.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every endpoint must resolve organization and membership from the session and apply role permissions and branch scope per BR-01 and BR-02. |
| FR-02 | The overview must return metrics, aging summary and recent payments per BR-03 to BR-06. |
| FR-03 | The invoice list must return visible non-void invoices with display status, last activity, filters, search, sort and pagination per BR-03, BR-07 and BR-08. |
| FR-04 | The payment list must return visible payments with filters, search, sort and pagination per BR-09. |
| FR-05 | Invoice and payment lists must be exportable to CSV per BR-10. |
| FR-06 | Recording an external payment must validate, guard, number, persist and apply the payment and update the invoice atomically per BR-11 to BR-15 and the payment field rules. |
| FR-07 | Recording must be idempotent and serialized per BR-14. |
| FR-08 | A requested receipt must be emailed after commit per BR-17. |
| FR-09 | Audit rows and logs must follow BR-18. |
| FR-10 | The persistence model must apply SA-11 to SA-13 per Data and persistence impact. |
| FR-11 | The hub page must render Design 24 with its tabs, states and interactions per BR-19 to BR-24. |
| FR-12 | The drawer must render and submit per BR-25 and BR-26. |
| FR-13 | Navigation, the Design 12 breadcrumb and review preselection must follow BR-27 to BR-29. |
| FR-14 | The page must meet the responsive, accessibility and fidelity rules of BR-30. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Roles. Read policy (overview, options, customers, invoice list, payment list, exports): `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`. Action policy (`POST` payment): `owner`, `accounting`. Other roles → `403` before any read or write. No organization id is accepted from the client. | Backend |
| BR-02 | Branch scope (`completed-jobs-review` BR-02) applied to `invoices.branch_id`; a payment is visible when the invoice of its allocation is visible. An invoice, branch or customer filter of another organization, outside scope or unknown → identical `404` with no foreign data and no write. Lists, counts, metrics, aging, recent payments and exports include only visible rows. `void` invoices and their payments never appear in the hub; a `void` invoice in a payment path → `404`. | Backend |
| BR-03 | Display status, computed at read time in `organizations.timezone`: an invoice with stored `sent` or `partially_paid` and `due_date` < today is **overdue** with `daysOverdue` = today − `due_date`; otherwise the display status equals the stored status (`draft`, `sent`, `partially_paid`, `paid`). The stored status is never set to `overdue` by this feature. Labels: "Draft", "Sent", "Partially paid", "Paid", "Overdue <n> day(s)". | Both |
| BR-04 | Metrics (respect branch scope and the `branchId` filter only): **Outstanding** = Σ `balance_due` of stored `sent` and `partially_paid`; **Overdue** = the same Σ restricted to overdue invoices; **Draft** = Σ `total` of `draft`; **Paid this month** = Σ `payment_allocations.amount` of payments whose `paid_at` local date is in the current month; **Average time to pay** = mean of (local date of the latest allocated payment `paid_at` − `issue_date`) in days over `paid` invoices whose latest payment local date is within the last 90 days (today − 89 … today), rounded to 1 decimal, `null` when none. Money in the organization currency. | Backend |
| BR-05 | Aging summary over stored `sent` and `partially_paid` (same filters as BR-04), Σ `balance_due` per bucket by days past `due_date`: **Current** (not past due), **1 – 30 days**, **31 – 60 days**, **60+ days** (61 or more). | Backend |
| BR-06 | Recent payments: the 5 latest visible payments (same filters as BR-04) by `paid_at` desc then `payment_number` desc, each `{ paymentId, paidDate, customerName, invoiceId, invoiceNumber, amount, method }`. **View all** opens the Payments tab. | Both |
| BR-07 | Invoice list. Filters, all optional, combined with AND: `search` trimmed 0–100 characters, case-insensitive substring of customer display name, or an invoice or work order number written as digits or `<prefix>-<digits>`; `from`/`to` ISO dates on `issue_date` inclusive (`from` ≤ `to`, else `400`); `status` = `draft`, `sent`, `partially_paid`, `paid` or `overdue` matching the BR-03 display status; `customerId` (customer of the organization); `branchId` (branch in scope). Sort: `issue_date` desc, then `invoice_number` desc. `page` ≥ 1, page size 20; a page beyond the last returns empty `items` with `total`. Invalid values → `400` with field errors. | Backend |
| BR-08 | Invoice row: `id`, number `<invoice_prefix>-<n>`, customer id and display name, work order id and `<work_order_prefix>-<n>`, `issueDate`, `dueDate`, `total`, `balanceDue`, `currency`, display status and `daysOverdue`, `lastActivity`, `updatedAt`, `canRecordPayment` (action policy and stored status `sent` or `partially_paid`). `lastActivity`: `paid` → `{ kind: "paid", date }` (latest payment local date); `partially_paid` → `{ kind: "payment", date }` (latest payment); `sent` → `{ kind: "sent", date }` (`sent_at` local date); `draft` → `null`. Labels "Paid <MMM d, yyyy>", "Payment <MMM d>", "Sent <MMM d, yyyy>", "—". | Both |
| BR-09 | Payment list. Filters: `search` (payment, invoice number as digits or `<prefix>-<digits>`, customer display name substring, `external_reference` substring), `from`/`to` on the `paid_at` local date, `method` (payment method enum), `branchId`. Sort `paid_at` desc, `payment_number` desc; page size 20 per BR-07. Row: `id`, number `<payment_prefix>-<n>`, `paidDate`, customer name, invoice id and number, `method`, `reference`, `amount`, `currency`, `receivedByName`. Notes are never returned in lists. | Backend |
| BR-10 | CSV exports `GET /invoices/export` (BR-07 filters, no `page`) and `GET /invoices/payments/export` (BR-09 filters), sorted as their lists, `text/csv; charset=utf-8`, files `invoices-<yyyy-mm-dd>.csv` / `payments-<yyyy-mm-dd>.csv` (organization date). Invoice columns: Invoice, Customer, Work order, Branch, Invoice date, Due date, Status (BR-03 label), Total, Amount paid, Balance, Currency. Payment columns: Payment, Date, Customer, Invoice, Method, Reference, Amount, Currency, Received by. Escaping and the 5,000-row limit (`409 export_too_large` "Narrow the filters to export 5,000 rows or fewer.") follow `completed-jobs-review` BR-21. Notes, emails, addresses and audit data are never exported. | Backend |
| BR-11 | Record request `POST /invoices/{invoiceId}/payments { idempotencyKey, amount, paidDate, method, reference, receivedByUserId, sendReceipt, note, updatedAt }` per the payment field rules. Invalid body → `400` with field errors and no write. Client amounts other than `amount`, statuses, numbers, customer or currency are rejected as unknown fields. | Both |
| BR-12 | Guards, after BR-01/BR-02 and BR-11 and after locking the invoice row, in order: (1) a payment with this `idempotencyKey` exists in the organization → BR-14; (2) stored status not `sent` or `partially_paid` → `409 invoice_not_payable` "This invoice can't receive payments."; (3) `updatedAt` ≠ stored → `409 invoice_changed` "This invoice changed. Refresh to see the latest."; (4) `amount` > `balance_due` → `400 errors.amount` "Amount can't exceed the outstanding balance of <balance>."; (5) `paidDate` < `issue_date` → `400 errors.paidDate` "Payment date can't be before the invoice date.". | Backend |
| BR-13 | Effect, one transaction (lock order: invoice row, then organization row): (a) payment number = `organizations.next_payment_number`, incremented by 1, never reused (a rollback restores it); (b) one `payments` row: organization, invoice `customer_id`, `payment_number`, `method`, `amount`, invoice `currency`, `paid_at` = 00:00 of `paidDate` in the organization timezone, `external_reference` (null when empty), `notes` (null when empty), `recorded_by_user_id` = actor, `received_by_user_id`, `idempotency_key`; (c) one `payment_allocations` row for the full amount to the invoice; (d) invoice `amount_paid += amount`, `balance_due -= amount`, `status` = `paid` when `balance_due` = 0, else `partially_paid`, `updated_at = now`; (e) audit per BR-18. Any failure rolls back everything, including the counter. Response `201 { changed: true, emailStatus, payment: PaymentRow, invoice: { id, status, amountPaid, balanceDue, updatedAt } }`. | Backend |
| BR-14 | Idempotency and concurrency. An existing payment with the same `idempotencyKey` and identical invoice, `amount`, `paidDate`, `method`, `reference`, `receivedByUserId` and `note` → `200 { changed: false, emailStatus: "not_sent", payment, invoice }` with no write and no email, even if `updatedAt` is stale. The same key with any difference → `409 idempotency_conflict` "This payment request was already used for different details.", no write. Repeated or concurrent requests with one key produce exactly one payment, one number, one allocation, one balance change, one audit pair and at most one email; the SA-12 unique violation is treated as the `changed = false` path. Concurrent requests with different keys and the same `updatedAt` on one invoice: exactly one succeeds, the other gets `409 invoice_changed`. Σ allocations never exceeds the invoice total. | Backend |
| BR-15 | Status transitions: `sent` → `partially_paid` (partial) or `paid` (full); `partially_paid` → `partially_paid` or `paid`. An overdue invoice keeps its stored status and changes the same way; overdue remains a BR-03 presentation. | Backend |
| BR-16 | Received by: `receivedByUserId` must be a user with an `active` `organization_users` row in the session organization, any role (no branch restriction). Otherwise `400 errors.receivedByUserId` "Select who received the payment." with no data about that id. The options list (BR-19 `members`) contains these users by full name; the drawer defaults to the current user. | Both |
| BR-17 | Receipt. When `sendReceipt` is true, after commit, through the existing `IEmailSender` (composer following the invoice email precedent) to `invoices.recipient_email`. Subject "<organization name>: payment receipt <PAY-n>". Text and HTML bodies: "We received your payment. Thank you!", "Payment <PAY-n> · <amount> · <MMM d, yyyy> · <method label>", "Invoice <INV-n> · Remaining balance <balance>", the organization name and, when set, "Questions? Call us at <organization phone>.". Never the reference, note, received-by or recorder. No attachments. `emailStatus` = `sent` or `failed`; `not_sent` when `sendReceipt` is false, the recipient is null, or `changed = false`. On failure only the payment id and failure category are logged; the response stays `201` and the payment is never rolled back. | Backend |
| BR-18 | Audit (organization, invoice `branch_id`, actor): `payment.recorded` (`entity_type` `payment`, payment id; after `{ amount, currency, method }`; metadata `{ paymentNumber, invoiceId, receiptRequested }`) and `invoice.payment_applied` (`entity_type` `invoice`, invoice id; before/after `{ status, amountPaid, balanceDue }`; metadata `{ paymentId }`). Audit rows, logs and problem details never contain the reference, note, recipient email, customer names or the idempotency key. | Backend |
| BR-19 | Options `GET /invoices/options`: `{ timezone, currency, paymentPrefix, branches: [{ id, name }] (in scope), members: [{ userId, name }] (BR-16; returned only when `canAct`, else empty), canAct }`. Customer filter `GET /invoices/customers?search=`: up to 20 customers of the organization with at least one visible non-void invoice, by display name, `search` substring 0–100 characters. | Backend |
| BR-20 | Route `/invoices` is a lazy route in the app shell for BR-01 read roles; `technician` gets the existing forbidden state. `/invoices/review`, `/invoices/view` and `/invoices/:invoiceId` keep working. Query params `tab` (`invoices` default, `payments`, `ready`) and, on the Invoices tab, `status` (BR-07 values) are read on load and kept in sync with the UI; an invalid value falls back to the default. | Frontend |
| BR-21 | Header: back link "Billing" (→ `/invoices`), title "Invoices & payments", subtitle "Track invoices, outstanding balances, due dates, and customer payments.", **Export** menu ("Invoices CSV", "Payments CSV" — each with its tab's current filters) and **New invoice** (BR-24). Metric cards per BR-04 with labels "Outstanding", "Overdue", "Draft", "Paid this month", "Average time to pay" ("<n> days", "—" when null). Below the tabs: "Aging summary" (four BR-05 cards with text labels) and "Recent payments" (Date, Customer, Invoice #, Amount, Method; invoice number links to `/invoices/<id>`; **View all**; empty "No payments recorded yet."). | Frontend |
| BR-22 | Invoices tab: filters search "Search invoices, customers, or work orders..." (debounced), date range on invoice date (empty by default), "Status" ("All statuses", "Draft", "Sent", "Partially paid", "Paid", "Overdue"), "Customer" (searchable, BR-19), "Branch" (shown only when more than one branch is in scope), **Clear filters**. Columns: Invoice # (link to `/invoices/<id>`), Customer, Work order (link to `/jobs/<workOrderId>`), Invoice date, Due date, Amount, Balance, Status (chip with icon and text), Last activity (BR-08), Actions menu: **View invoice**, **Record payment** (only when `canRecordPayment`), **Download PDF** (`invoice-draft-delivery` internal PDF). Footer "Showing <a> – <b> of <n> invoices" and paging. Changing a filter resets to page 1. The Branch filter also drives metrics, aging and recent payments. | Frontend |
| BR-23 | Payments tab: search "Search payments, invoices, customers, or references...", date range on payment date, "Method" ("All methods" + BR field labels), "Branch" (as BR-22), **Clear filters**; columns Date, Payment #, Customer, Invoice # (link), Method (tag with text), Reference ("—" when null), Amount, Received by; paging as BR-22. Ready tab ("Completed jobs ready to invoice" with a count badge): reads `GET /billing-review/queue` with `completed=all`, `tab=ready`, `page` and the hub `branchId`; the badge shows its `tabs.ready`; columns Work order, Customer, Job, Completed, Approved total; each row's **Review and invoice** opens `/invoices/review?workOrderId=<id>`; empty "No completed jobs are ready to invoice. Jobs with variances or missing requirements stay in Needs review." with a link to `/invoices/review`. | Frontend |
| BR-24 | States and other actions. Loading: skeletons for metrics, table, aging and recent payments independently. Empty (no invoices at all with no filters): "No invoices yet. Generate invoices from completed jobs." with **Completed jobs** opening the ready tab; filtered empty: "No invoices match these filters." with **Clear filters**; payments equivalents "No payments recorded yet." / "No payments match these filters.". Errors: "We couldn't load invoices. Try again." / "We couldn't load payments. Try again." / "We couldn't load billing metrics. Try again." each with **Retry**. **New invoice** opens the ready tab. Export: download; `409 export_too_large` shows its message; other failures "We couldn't export. Try again.". **Download PDF** (row menu): the row shows progress while downloading; failure "We couldn't download the PDF. Try again.". Options (BR-19) failure: the Branch filter is hidden, **Record payment** is shown disabled with "We couldn't load payment options. Try again." and **Retry** (reloads options); lists, metrics and the ready tab still load. Customer filter search failure: the selector shows "We couldn't load customers." and keeps the current selection. Read-only roles see no **Record payment**. Messages are announced politely. | Frontend |
| BR-25 | Drawer "Record external payment" (opened only for `canRecordPayment` rows): info "Use this form only for money already received outside FieldOps. No card will be charged."; read-only Invoice, Customer, Outstanding balance (from the row); fields per the payment field rules with labels **Amount** (currency prefix, default = balance), **Payment date** (default today, organization timezone; picker limited to issue date … today), **Payment method** (no default; helper "Select how the payment was received."), **Reference number** (required marker shown only for methods that require it; helper "Required for external card payments, bank transfers, and checks."), **Received by** (default current user; helper "Person who received the payment."), **Send receipt** (switch, default on; helper "Send a receipt to the customer."; disabled and off with "This invoice has no recipient email." when the row has no recipient), **Internal note** (placeholder "Add an internal note (optional)...", helper "Visible only to your team.", counter "<n>/500"); actions **Cancel** and **Record external payment** (primary); footer "This creates a payment record and updates the invoice balance. It does not process, authorize, or capture a card.". A new `idempotencyKey` is generated each time the drawer opens and reused for retries of that submission. | Frontend |
| BR-26 | Drawer interactions: client validation focuses the first invalid field; while pending, fields and actions are disabled and the primary shows progress. `201` → close, toast "Payment <PAY-n> recorded." plus "Receipt sent to the customer." when `emailStatus = sent`, or warning "Payment recorded, but we couldn't email the receipt." when `failed`; `200 changed = false` → close, "This payment was already recorded."; both reload metrics, aging, recent payments and the active list. `409 invoice_changed` / `invoice_not_payable` → their messages, close and reload; `409 idempotency_conflict` and other failures → "We couldn't record this payment. Try again." keeping values (network failures retry with the same key); `400` → field messages. **Cancel**, Escape or close with changed values shows the shared discard-changes dialog. | Frontend |
| BR-27 | Navigation: the **Invoices** group links to `/invoices` with children **All invoices** (`/invoices`), **Needs review** (`/invoices/review`), **Drafts** (`/invoices?status=draft`), **Sent** (`/invoices?status=sent`), **Payments** (`/invoices?tab=payments`) and **Overdue** (`/invoices?status=overdue`), replacing the coming-soon items. The child matching the current path and query is marked current; the group stays highlighted under `/invoices`. `/invoices` no longer redirects. | Frontend |
| BR-28 | `invoice-draft-delivery` change: the Design 12 breadcrumb item "Invoices" links to `/invoices`. Nothing else on that page changes. | Frontend |
| BR-29 | `completed-jobs-review` change: `/invoices/review?workOrderId=<id>` sets the "Completion date" filter to "All time", selects that work order and loads its detail; when the detail returns `404` the existing "This job is no longer in the review queue." state is shown. Without the param the page is unchanged. No backend change. | Frontend |
| BR-30 | Responsive, accessibility and fidelity. ≥ 1280 px layout as Design 24 (five metric cards in a row; aging and recent payments side by side; drawer at the right edge overlaying the page); 768–1279 px metrics wrap to rows, aging and recent payments stack; < 768 px metrics one or two per row, filters stack, tables scroll inside their cards, the drawer is full width; no horizontal page scroll at 375 px. Tabs use tab semantics; tables have header cells; statuses, methods and aging never rely on color alone; the actions menu and drawer are keyboard operable; the drawer traps and restores focus; field errors are linked; targets ≥ 44 × 44 px on touch layouts. Design 24 deviations: no row checkboxes; no "Online customer payments…" link; no "Viewed" activity; "Online card" is not a method; reference helper includes checks; header branch selector replaced by the in-page Branch filter; sidebar per BR-27; mockup data are examples. | Frontend |

### Payment field rules

| Field | Rule | Message when invalid |
| ----- | ---- | -------------------- |
| `idempotencyKey` | Required UUID | `400` "Try again." (not shown as a field) |
| `amount` | Number with ≤ 2 decimals, 0.01 – `balance_due` | "Enter an amount greater than 0." / BR-12 (4) |
| `paidDate` | ISO date, `issue_date` … today (organization timezone) | "Payment date can't be in the future." / BR-12 (5) |
| `method` | `cash` "Cash", `bank_transfer` "Bank transfer", `card_external` "External card payment", `check` "Check", `other` "Other" | "Select a payment method." |
| `reference` | Trimmed, ≤ 160 characters; required for `card_external`, `bank_transfer`, `check`; empty → null | "Enter the reference number." / "Use 160 characters or fewer." |
| `receivedByUserId` | BR-16 | "Select who received the payment." |
| `sendReceipt` | Boolean; true is ignored (`not_sent`) when the invoice has no recipient | — |
| `note` | Trimmed, 0–500 characters, empty → null | "Use 500 characters or fewer." |
| `updatedAt` | Required ISO instant equal to the stored invoice value | `409 invoice_changed` |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Invoice `sent` | `partially_paid` | Record payment < balance | Owner, Accounting | BR-11, BR-12 |
| Invoice `sent` / `partially_paid` | `paid` | Record payment = balance | Owner, Accounting | BR-11, BR-12 |
| Invoice `partially_paid` | `partially_paid` | Record payment < balance | Owner, Accounting | BR-11, BR-12 |
| Invoice `draft`, `paid` | unchanged | Record payment | Owner, Accounting | `409 invoice_not_payable` |
| Any | unchanged | Same `idempotencyKey` repeated | Owner, Accounting | `200 changed = false` |
| Stored `sent` / `partially_paid` | displayed overdue | `due_date` < today (read time) | — | BR-03; nothing stored |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): `organizations`
  (`timezone`, `currency`, `name`, `phone`, `invoice_prefix`,
  `work_order_prefix`, `payment_prefix`), `organization_users`,
  `organization_user_branches`, `branches`, `users`, `customers`,
  `work_orders`, `invoices`, `payments`, `payment_allocations`; the
  billing-review queue through its existing endpoint.
- Written: `organizations.next_payment_number`, `payments`,
  `payment_allocations`, `invoices` (`amount_paid`, `balance_due`, `status`,
  `updated_at`), `audit_logs`.
- Not written: `invoice_lines`, invoice amounts other than `amount_paid` /
  `balance_due`, `invoice_access_tokens`, `notifications`,
  `payments.receipt_storage_key`, work orders, visits.
- Schema amendments: **approved by the user 2026-10-08** (OD-01). The
  implementation first updates `docs/database/fieldops-schema.sql`, then the
  EF model, and generates one migration only under `--generate-migration`.
  Agents never apply it. Review by `database-reviewer`.
  - SA-11 `organizations`: add `payment_prefix varchar(20) NOT NULL DEFAULT
    'PAY'` and `next_payment_number bigint NOT NULL DEFAULT 1`.
  - SA-12 `payments`: add `received_by_user_id uuid NOT NULL REFERENCES
    users(id)`, `idempotency_key uuid NOT NULL` and `UNIQUE(organization_id,
    idempotency_key)`. If `payments` has rows, the migration fails instead of
    inventing values.
  - SA-13 indexes: `CREATE INDEX ix_payment_allocations_invoice ON
    payment_allocations(invoice_id)`, `CREATE INDEX ix_payments_org_paid_at
    ON payments(organization_id, paid_at DESC)`, `CREATE INDEX
    ix_invoices_org_issue_date ON invoices(organization_id, issue_date)`.
- `paid_at` stores 00:00 of the payment date in the organization timezone
  (OD-02); every read converts it back to that local date.
- Cross-row invariants 7 and 8 of the schema (allocations ≤ payment and
  balance; `amount_paid`/`balance_due` updated transactionally) are enforced
  by BR-13/BR-14.

## Tenant isolation and authorization

- Organization and membership come from the session; BR-01/BR-02 apply
  before any read or write.
- Client-provided identifiers: `invoiceId` (path), `branchId` and
  `customerId` (query) are authorized against the session organization and
  branch scope; otherwise identical `404`. `receivedByUserId` (body) is
  validated as an active member of the session organization (`400`, BR-16).
  `idempotencyKey` is looked up only within the session organization.
  Customer, currency, balance, number and branch are resolved server-side
  from the authorized invoice.
- The ready tab reuses `/billing-review/queue` and its existing
  authorization.
- Recording requires the action policy; frontend hiding is UX only.
- Responses use `Cache-Control: no-store`. Logs and audit follow BR-18.

## API contracts

Contract status: Final. Base path `/invoices`; the internal invoice path
parameter is constrained to a GUID so the literal segments below never match
`/invoices/{invoiceId}`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/invoices/options` | — | `200` BR-19 options | `403` | Read |
| GET | `/invoices/customers` | Query `search` | `200 [{ id, name }]` (≤ 20) | `400` · `403` | Read |
| GET | `/invoices/overview` | Query `branchId` | `200 { metrics: { outstanding, overdue, draft, paidThisMonth, averageDaysToPay \| null, currency }, aging: { current, days1To30, days31To60, days60Plus }, recentPayments: [BR-06] }` | `400` · `403` · `404` | Read |
| GET | `/invoices` | Query `search`, `from`, `to`, `status`, `customerId`, `branchId`, `page` | `200 { items: [InvoiceRow], page, pageSize, total }` | `400` · `403` · `404` | Read |
| GET | `/invoices/export` | Same query without `page` | `200 text/csv` (BR-10) | `400` · `403` · `404` · `409 export_too_large` | Read |
| GET | `/invoices/payments` | Query `search`, `from`, `to`, `method`, `branchId`, `page` | `200 { items: [PaymentRow], page, pageSize, total }` | `400` · `403` · `404` | Read |
| GET | `/invoices/payments/export` | Same query without `page` | `200 text/csv` (BR-10) | `400` · `403` · `404` · `409 export_too_large` | Read |
| POST | `/invoices/{invoiceId}/payments` | `{ idempotencyKey, amount, paidDate, method, reference, receivedByUserId, sendReceipt, note, updatedAt }` | `201 { changed: true, emailStatus: "sent"\|"failed"\|"not_sent", payment: PaymentRow, invoice: { id, status, amountPaid, balanceDue, updatedAt } }` · `200 { changed: false, emailStatus: "not_sent", payment, invoice }` | `400` · `403` · `404` · `409 invoice_not_payable`, `invoice_changed`, `idempotency_conflict` | Action |

`InvoiceRow` = `{ id, number, customerId, customerName, workOrderId,
workOrderNumber, branchName, issueDate, dueDate, total, balanceDue, currency,
status: "draft"|"sent"|"partially_paid"|"paid"|"overdue", storedStatus,
daysOverdue | null, lastActivity: { kind: "paid"|"payment"|"sent", date } |
null, hasRecipient, updatedAt, canRecordPayment }`.

`PaymentRow` = `{ id, number, paidDate, customerName, invoiceId,
invoiceNumber, method, reference | null, amount, currency, receivedByName }`.

Errors use ProblemDetails with `code`; `400` includes field `errors`. Money
values are JSON numbers with 2 decimals; instants ISO 8601 UTC; dates ISO
`yyyy-mm-dd` in the organization timezone. Existing reused contracts:
`GET /billing-review/queue` (unchanged), `GET /invoices/{invoiceId}/pdf`
(unchanged), `IEmailSender` (unchanged).

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Metrics, aging, recent payments | Skeletons (BR-24) | Zero values / "No payments recorded yet." | Metrics error + Retry | Technician → forbidden | BR-21 |
| Options and customer filter | Branch filter and customer selector pending | Customer selector "No customers found." | BR-24 options error + Retry (Branch hidden, Record payment disabled); "We couldn't load customers." | Read roles; `members` only for `canAct` | BR-19 data in filters and drawer |
| Invoices tab | Table skeleton | BR-24 empty / filtered empty | BR-24 error + Retry; Download PDF failure message | **Record payment** hidden for read-only roles | BR-22 |
| Payments tab | Table skeleton | BR-24 empty / filtered empty | BR-24 error + Retry | Read roles | BR-23 |
| Ready tab | Table skeleton | BR-23 empty text | "We couldn't load completed jobs. Try again." + Retry | Read roles | BR-23 |
| Payment drawer | Progress on submit | N/A | BR-26 messages | Action roles only | BR-26 |

- Mockups: `design/assets/24-design.png` (Approved; deviations in BR-30).
- Responsive and accessibility constraints: BR-30.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Role without read permission | `403`; forbidden state | None |
| Read-only role records a payment | `403`; "Only owners and accounting can record payments." | None |
| Foreign, out-of-scope, unknown or void invoice; foreign branch or customer filter | Identical `404`; "This invoice isn't available." | None |
| Invalid filter or body | `400` with field errors | None |
| Invoice draft or paid | `409 invoice_not_payable` | None |
| Stale `updatedAt` or lost race | `409 invoice_changed` | None |
| Amount over balance / date before issue date | `400` field error | None |
| Same key, same details | `200 changed = false`; "This payment was already recorded." | None, no email |
| Same key, different details | `409 idempotency_conflict`; generic retry message | None |
| Receipt email failure | `201 emailStatus = failed`; warning toast | Never rolled back |
| Failure inside the transaction | `500`; "We couldn't record this payment. Try again." | Full rollback, counter unchanged |
| Export over 5,000 rows | `409 export_too_large` | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Members of every role, including dispatcher and accounting with and without `is_all_branches` | They call each endpoint (table-driven) | Read roles get data limited to branch scope; technician and unknown roles get `403`; operations manager, viewer and dispatcher get `403` on `POST` payments with no write |
| AC-02 | Invoices, branches and customers of another organization or outside scope, a void invoice and random ids | They are used in the payment path and in filters | Identical `404` with no foreign data and no write (one representative case per path family: payment path, filter) |
| AC-03 | Invoices in every stored status across branches, with due dates before, on and after today in a non-UTC timezone, and payments in and out of the current month and 90-day window | The overview is loaded with and without `branchId` | Metrics, average time to pay (and `null`), aging buckets (boundaries 0, 1, 30, 31, 60, 61 days) and the 5 recent payments follow BR-03 to BR-06; void and out-of-scope data are excluded |
| AC-04 | The same data | The invoice list is loaded with search (name, `INV-12`, `12`, `WO-7`), date range, each status including `overdue`, customer, branch and pages | Results, display status, `daysOverdue`, `lastActivity`, `canRecordPayment`, sort, page size 20, empty page beyond the last and `400` for invalid values follow BR-07 and BR-08 |
| AC-05 | Payments with every method, references and receivers | The payment list is loaded with search, date range, method, branch and pages | Rows, filters, sort and paging follow BR-09; notes are never returned |
| AC-06 | Filtered invoice and payment sets, a cell starting with `=`, more than 5,000 matching rows | Each export is requested | Columns, filenames, filters, sort and escaping follow BR-10; over 5,000 → `409 export_too_large` |
| AC-07 | A `sent` invoice | A partial payment and then a payment for the remaining balance are recorded | First: one payment numbered from `next_payment_number` (incremented), one allocation, `amount_paid`/`balance_due` updated, status `partially_paid`, `paid_at` = local midnight; second: status `paid`, balance 0; audit `payment.recorded` and `invoice.payment_applied` per BR-18 for each |
| AC-08 | A sent invoice | The same request is posted twice sequentially and twice concurrently with one key; the same key with a different amount; two different keys concurrently with the same `updatedAt` | One key yields exactly one payment, number, allocation, balance change, audit pair and email, the others `200 changed = false` without email; different details → `409 idempotency_conflict`; of the two keys exactly one succeeds and the other gets `409 invoice_changed`; Σ allocations ≤ total |
| AC-09 | Invoices `draft`, `paid` and `sent`, and invalid bodies (table-driven per the payment field rules: amount 0, over balance, 3 decimals, future date, date before issue date, missing method, missing reference for card/transfer/check, reference over 160, inactive or foreign received-by user, note over 500, stale `updatedAt`) | Payments are posted | `409 invoice_not_payable`, `400` field errors or `409 invoice_changed` follow BR-11, BR-12 and BR-16 with no write; cash/other without reference succeed |
| AC-10 | A sent invoice and a failure injected after the allocation insert | A payment is posted | Payment, allocation, invoice amounts and status, audit and the counter are rolled back; a later success uses the unconsumed number |
| AC-11 | Sent invoices with and without recipient, `sendReceipt` true and false, and an email sender that throws | Payments are recorded | Receipt sent after commit with BR-17 subject and body (no reference, note or receiver) when requested and possible; `emailStatus` `sent`, `failed` or `not_sent` per BR-17; the failure keeps the payment and logs only payment id and category |
| AC-12 | Every operation of this feature | Audit rows and logs are inspected | Actions and fields follow BR-18; none contains reference, note, recipient email, customer names or the idempotency key |
| AC-13 | The EF model and the authoritative schema | The migration is generated | `fieldops-schema.sql` contains SA-11, SA-12 and SA-13 exactly; the model matches; the migration is generated but not applied; `database-reviewer` approves |
| AC-14 | Each role signed in to the app shell | `/invoices`, `/invoices?status=overdue`, `/invoices?tab=payments`, `/invoices/review`, `/invoices/view` and `/invoices/<id>` are opened and the navigation is used | Route access, forbidden state for technician, query-param sync and fallback, the Invoices group children with current marking, and unaffected existing routes follow BR-20 and BR-27 |
| AC-15 | Overview, list, options and customer-search responses with data, empty unfiltered, empty filtered and errors | The hub renders and filters, tabs and pages change | Header, metrics, aging, recent payments, Invoices and Payments tabs, Branch visibility, reset to page 1 and all states follow BR-21 to BR-24, including the options failure (Branch hidden, Record payment disabled with Retry) and the customer-search failure text |
| AC-16 | Rows in each display status for an action role and a read-only role, and a PDF download that succeeds and fails | The actions menu is opened and its actions used | View invoice, Download PDF (progress and failure message per BR-24) and **Record payment** only when `canRecordPayment` follow BR-22; read-only roles never see **Record payment** |
| AC-17 | A ready-queue response with items, empty, and an error, and a `branchId` selected | The ready tab is opened and a row's action used | The badge equals `tabs.ready` from `completed=all&tab=ready`; columns, empty text with the Needs review link, error state and navigation to `/invoices/review?workOrderId=<id>` follow BR-23; **New invoice** opens this tab |
| AC-18 | An owner opening the drawer on partially paid invoices with and without recipient | The drawer is rendered and filled | Read-only data, defaults (amount = balance, today, current user, receipt on/disabled), reference-required marker per method, date limits, counter, info texts and client validation with focus follow BR-25 and the field rules |
| AC-19 | Responses `201` (`sent`, `failed`, `not_sent`), `200 changed = false`, `409` codes, `400` and network failure | The drawer is submitted, and cancelled with changes | Toasts, close and reload, value retention, same-key retry, pending state and the discard dialog follow BR-26 |
| AC-20 | The Design 12 page and `/invoices/review?workOrderId=<id>` for a ready job, a job outside the 30-day default and an invoiced job | They are opened | The breadcrumb "Invoices" targets `/invoices` (BR-28); the review page sets "All time", selects and loads the job, or shows its existing `404` state (BR-29); without the param the review page is unchanged |
| AC-21 | Export succeeding, too large and failing on each tab | **Export** options are used | Download with current filters or BR-24 export messages |
| AC-22 | Design 24 at 1440, 1024 and 375 px, keyboard only | The hub and drawer are reviewed (user visual QA) | Layout, deviations, tab semantics, menu and drawer keyboard access, focus trap/restore, linked errors and non-color indicators follow BR-30 |

## Testing requirements

This feature changes financial integrity (balance, status, numbering),
idempotency/concurrency of a multi-record write and outbound email.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Display status/overdue days, aging buckets and average time to pay (table-driven, timezone boundaries); payment field rules incl. reference by method | AC-03, AC-09 |
| Backend integration | Overview metrics/aging/recent payments; invoice and payment lists with filters, search and paging | AC-03, AC-04, AC-05 |
| Backend integration | Exports (columns, escaping, limit) | AC-06 |
| Backend integration | Record partial then full payment: rows, numbering, balances, status, `paid_at`, audit; receipt sent/failed/not_sent | AC-07, AC-11, AC-12 |
| Backend integration | Idempotency, concurrency and rollback | AC-08, AC-10 |
| Backend integration | Guards and validation (table-driven) | AC-09 |
| Authorization/tenant isolation | Role matrix; one representative cross-tenant and out-of-scope `404` per path family; audit/log content | AC-01, AC-02, AC-12 |
| Persistence | Schema doc and model match SA-11–SA-13; `database-reviewer`; migration generated, not applied | AC-13 |
| Frontend component/service | Hub: tabs, query sync, filters, states, actions menu by permission, ready tab; drawer: defaults, validation, outcomes, same-key retry, discard; navigation; breadcrumb and review preselection | AC-14, AC-15, AC-16, AC-17, AC-18, AC-19, AC-20, AC-21 |
| User visual QA | Fidelity, responsive layouts, keyboard and focus | AC-22 |

Backend integration may reach the 8-method default: payment idempotency,
concurrency and rollback are financial integrity and need their own evidence.

## Dependencies

- `invoice-draft-delivery` (AUDITED): sent invoices, `recipient_email`,
  internal PDF, invoice email precedent, Design 12 page (BR-28 change).
- `completed-jobs-review` (AUDITED): branch scope, queue endpoint with
  `tab=ready`, CSV precedent, `/invoices/review` (BR-29 change).
- `authenticated-app-shell`: shell, navigation and forbidden state.
- Shared discard-changes dialog.
- Schema amendments SA-11–SA-13 (approved 2026-10-08).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Invoice currencies equal the organization currency, so metrics are single-currency sums (`completed-jobs-review` AS-03). |
| AS-02 | No `payments` rows exist before this feature (no prior feature writes them), so SA-12 adds NOT NULL columns without backfill. |
| AS-03 | "Billing" back link and page title follow Design 24; the sidebar label stays "Invoices". |
| AS-04 | `hasRecipient` reflects `invoices.recipient_email`; every sent invoice has one (`invoice-draft-delivery` BR-13). |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Schema amendments and order | Approve now; implementation updates schema first / user amends first | No | 2026-10-08: SA-11–SA-13 approved; implementation updates `fieldops-schema.sql` first, then one migration with `--generate-migration`, never applied |
| OD-02 | Payment date storage | `paid_at` local midnight / new date column | No | 2026-10-08: `paid_at` at 00:00 organization timezone, displayed as date |
| OD-03 | Payable invoices and amounts | — | No | 2026-10-08: `sent`/`partially_paid` only; 0.01 – balance; no overpayment; date issue date … today |
| OD-04 | Idempotency and concurrency | — | No | 2026-10-08: client key per drawer + `updatedAt`; one transaction; number assigned inside, never reused |
| OD-05 | Methods and reference | — | No | 2026-10-08: schema enum labels; reference required for card_external, bank_transfer, check; note ≤ 500 |
| OD-06 | Received by | — | No | 2026-10-08: any active member; default current user |
| OD-07 | Receipt | — | No | 2026-10-08: default on; after commit to invoice recipient; no resend |
| OD-08 | Overdue | Derived / stored | No | 2026-10-08: derived at read time; partially paid overdue stays stored `partially_paid` |
| OD-09 | Metrics and aging | — | No | 2026-10-08: as BR-04/BR-05; branch scope and Branch filter only; void excluded |
| OD-10 | Permissions | — | No | 2026-10-08: read roles per BR-01 with branch scope; record Owner and Accounting |
| OD-11 | Header branch selector | — | No | 2026-10-08: in-page Branch filter |
| OD-12 | Navigation | — | No | 2026-10-08: `/invoices` hub; children per BR-27; Design 12 breadcrumb to `/invoices` |
| OD-13 | Ready tab content | All queue / ready only / link | No | 2026-10-08: only `ready` items (`tab=ready`), count = ready items; others stay in `/invoices/review`; rows preselect the job there |
| OD-14 | Unsupported mockup elements | — | No | 2026-10-08: as BR-30 deviations; New invoice opens ready tab; date range empty by default |
| OD-15 | Payments tab and export | — | No | 2026-10-08: as BR-09, BR-10, BR-23; recent payments 5 with View all |
| OD-16 | Invoice detail | — | No | 2026-10-08: only the breadcrumb changes; Record payment lives in the hub; public page unchanged |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03 |
| FR-03 | AC-04 |
| FR-04 | AC-05 |
| FR-05 | AC-06 |
| FR-06 | AC-07, AC-09, AC-10 |
| FR-07 | AC-08 |
| FR-08 | AC-11 |
| FR-09 | AC-12 |
| FR-10 | AC-13 |
| FR-11 | AC-15, AC-16, AC-17, AC-21 |
| FR-12 | AC-18, AC-19 |
| FR-13 | AC-14, AC-20 |
| FR-14 | AC-22 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-08 | — → DRAFT     | Created from Design 24 with decisions OD-01–OD-16 resolved by the user |
| 2026-10-08 | DRAFT → DRAFT | Revised after validation: Download PDF progress/failure and options/customer-search failure states (BR-24, UI states, AC-15, AC-16) |
| 2026-10-08 | DRAFT → APPROVED | Approved by user via /spec approve |
