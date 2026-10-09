# Customer invoice payments

| Field    | Value                       |
| -------- | --------------------------- |
| Feature  | `customer-invoice-payments` |
| Type     | Full-stack                  |
| Status   | APPROVED                    |
| Created  | 2026-10-09                  |
| Updated  | 2026-10-09                  |
| Approved | 2026-10-09                  |

## Context and objective

`invoice-draft-delivery` emails customers a secure, strictly read-only link to
a sent invoice, and `invoices-payments-management` lets staff record payments
received outside FieldOps. Customers still cannot pay online. This feature
turns the public invoice page into a complete payment flow. The customer reviews
the invoice, service, completion report and before/after photos, then pays the
full balance by card through Stripe, reports a bank transfer, or arranges cash.
The page shows processing, failure and success states, lets the customer
download the invoice and receipt, and accepts one optional service review. The
Stripe webhook is the only source of truth for card outcomes. It records the
payment, receipt and balance idempotently and reconciles partial and full
refunds. FieldOps never receives or stores card numbers, CVV or other sensitive
card data.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Customer (anonymous link holder) | With a valid token: view the invoice, service details, completion report and before/after photos; download invoice and receipt PDFs; pay the full balance by card; report a bank transfer; see cash instructions; submit one review once the invoice is paid | Sign in, choose an amount, save payment methods, refund, see other invoices, internal data, ids of other resources or other organizations |
| Stripe (webhook caller) | Deliver signed events that confirm, fail, cancel or refund card payments | Act without a valid signature; change anything other than the attempt, payment and invoice of its event |
| Owner (`owner`) | Read the masked bank transfer details and replace them in Company settings; everything granted by `invoices-payments-management` | See the stored account number in full; refund or cancel card payments from FieldOps |
| Accounting (`accounting`) | Everything granted by `invoices-payments-management` | Record an external payment while an online card payment is pending (`409`); bank transfer details (`403`) |
| Other internal roles | Unchanged from `invoices-payments-management` and `company-settings-and-branches` | Bank transfer details endpoints (`403`) |

## Scope

- Public route `/invoices/view` (existing token flow) replaced by the payment
  experience in the approved designs: invoice, service details, progress
  timeline, payment panel with **Credit or debit card**, **Bank transfer
  (ACH)** and **Cash**, processing, failure, success/receipt, review and the
  neutral unavailable state, desktop and mobile.
- Stripe card payments (PaymentIntents, Payment Element, signed webhooks)
  behind a payment gateway abstraction, for the full outstanding balance.
- Payment attempts with `pending`, `succeeded`, `failed`,
  `partially_refunded` and `refunded`; payments with `succeeded`,
  `partially_refunded` and `refunded` plus `refunded_amount`.
- Webhook-driven success, failure, cancellation and partial/full refund
  reconciliation, idempotent per event and per state.
- One pending card attempt per invoice; pending expiry after 30 minutes with
  confirmed cancellation; external payment recording blocked while pending.
- Receipt numbering `RCT-<invoice number>-<nn>`, receipt PDF on demand and
  receipt email after commit.
- Customer bank transfer notice; cash instructions.
- Public completion report PDF and before/after photo gallery.
- One immutable service review per work order.
- Encrypted bank transfer details managed by the Owner in Company settings.
- Adjustments to `invoices-payments-management`: net amounts, refund status,
  `card_online` method, optional Received by, receipt numbers for recorded
  payments and the `payment_in_progress` guard (BR-31).
- Schema amendments SA-14 to SA-18 in one migration generated and never
  applied.

## Non-goals

- The authenticated customer portal, its sign-in, navigation, invoice list,
  requests or appointments; "Back to invoices" and every portal link in the
  mockups.
- Saved payment methods, wallets, ACH debit through Stripe, Stripe Connect
  onboarding, multi-currency, surcharges or tips.
- Customer-chosen or partial online amounts.
- Refunds, cancellations or disputes initiated from FieldOps; dispute
  (chargeback) handling.
- Automatic reconciliation of reported bank transfers; recording cash online.
- Removing bank transfer details (only replacing them).
- An internal page to read reviews; editing or deleting reviews.
- Resending receipts; receipt PDF attachments; storing generated PDFs.
- Receipts for payments recorded before this feature.
- Background jobs or schedulers (expiry is evaluated lazily, BR-14).
- Browser automation or Playwright by agents.

## User flow

1. The customer opens the emailed link. The page removes the token from the
   URL and shows the invoice with its status, timeline, amount due, lines,
   totals, service details and the payment panel.
2. The customer optionally downloads the invoice PDF, opens the completion
   report or views before/after photos.
3. The customer chooses **Credit or debit card**, enters the card in the
   Stripe-hosted fields and selects **Pay <balance>**. The system creates one
   attempt, the browser confirms it with Stripe, and the panel shows
   **Processing your payment** while repeated submissions are blocked.
4. Stripe calls the webhook. On success, the system records the payment,
   receipt, balance and status once, emails the receipt and the page shows
   **Payment successful**. On failure, the page shows **Payment wasn't
   completed** with **Try payment again** and **Choose another payment
   method**, and the invoice is unchanged.
5. Alternatively, the customer chooses **Bank transfer (ACH)**, copies the
   instructions and selects **I sent the transfer**, or chooses **Cash** and
   contacts the company.
6. After payment, the customer downloads the receipt and invoice and may
   submit one review or select **Maybe later**.
7. Invalid, expired, revoked, draft or void links show the neutral
   unavailable state.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every public endpoint must authorize only by token and scope every read and write to the token's organization and invoice per BR-01 and BR-02. |
| FR-02 | The public view must return the invoice, payment state, service details, payment options and review availability per BR-03 to BR-05. |
| FR-03 | Card payments must create at most one pending attempt per invoice through the gateway for the full balance per BR-06 to BR-08. |
| FR-04 | The webhook must verify, deduplicate and apply Stripe events idempotently per BR-09 and BR-10. |
| FR-05 | A succeeded event must record the payment, receipt, allocation, balance, status and audit in one transaction and email the receipt after commit per BR-11 and BR-12. |
| FR-06 | Failed, cancelled and expired attempts must release the invoice only after the provider confirms cancellation per BR-13 and BR-14. |
| FR-07 | Partial and full refunds must apply only the new refunded delta to payment, attempt and invoice per BR-15. |
| FR-08 | Attempt status must be queryable by the page per BR-16. |
| FR-09 | Bank transfer notices and cash instructions must follow BR-17 and BR-18. |
| FR-10 | Invoice, receipt and completion report PDFs and before/after photos must be served per BR-19 to BR-21. |
| FR-11 | One review per work order must be accepted per BR-22. |
| FR-12 | Public endpoints must be rate limited and hardened per BR-23. |
| FR-13 | Bank transfer details must be stored encrypted and managed per BR-24 and BR-25. |
| FR-14 | Audit rows and logs must follow BR-26. |
| FR-15 | The public page must render the approved designs with their states, interactions and deviations per BR-27 to BR-30 and BR-32. |
| FR-16 | `invoices-payments-management` must follow the BR-31 adjustments. |
| FR-17 | The persistence model must apply SA-14 to SA-18 per Data and persistence impact. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Token resolution follows `invoice-draft-delivery` BR-18/BR-19 unchanged: token only in JSON POST bodies; hash lookup; accepted only when the row exists, `revoked_at` is null, `expires_at` > now (365 days from creation) and the invoice is neither `draft` nor `void`. Every other case, including malformed input, returns the identical `404 invoice_link_unavailable` with no branding, organization or invoice data and no write. A paid or refunded invoice stays accessible while its token is valid. | Backend |
| BR-02 | Public scoping. `attemptId`, `paymentId` and `photoId` sent with a token are accepted only when they belong to the token's invoice (photo: a visit of the invoice's work order); otherwise the same `404 invoice_link_unavailable`. No organization, customer, invoice, amount, currency or status is accepted from the client. Public responses expose only opaque `attemptId`, `paymentId` and `photoId` values; never organization, customer, invoice, line, work order, visit, user or provider ids. | Backend |
| BR-03 | `PublicInvoice` (extends `invoice-draft-delivery` `PublicInvoice`, frozen preview unchanged): `status` (`sent`, `partially_paid`, `paid`), `overdue` and `daysOverdue` (`invoices-payments-management` BR-03), `amountPaid` (net, BR-31), `balanceDue`, `customerFirstName` (first word of the snapshot Bill to name for `person` customers, else null), `timeline` (`serviceCompletedOn` = local date of the latest completed visit `actual_completed_at`; `sentOn`; `paidOn` = local date of the latest succeeded payment while `status = paid`, else null; `receiptOn` = same as `paidOn` when that payment has a receipt), `service` (`title` = work order title, `workOrderNumber`, `completionNote`, `technicianName` = primary assigned technician of the latest completed visit or null, `serviceDate` = `serviceCompletedOn`, `hasCompletionReport` = a completed visit exists, `photoCount` per BR-21), `paymentOptions` (BR-04; null unless `status` is `sent` or `partially_paid` and `balanceDue` > 0), `activeAttempt` (`{ attemptId, status: "pending" }` of a pending card attempt, else null), `transferReportedOn` (BR-17), `payments` (BR-05), `review` (`{ available, submitted }`, BR-22), `receiptEmail` (the invoice `recipient_email`). | Backend |
| BR-04 | `paymentOptions`: `card { available, publishableKey }` — available when Stripe secret, publishable and webhook keys are configured; `publishableKey` null otherwise. `bankTransfer` — when bank details are configured: `{ available: true, bankName, accountNumber (full, decrypted), routingNumber, reference: <INV-n>, amount: balanceDue }`; otherwise `{ available: false }`. `cash { phone, email }` from the organization (null when unset). Bank details are returned only here, only while `paymentOptions` is not null. | Backend |
| BR-05 | `payments`: succeeded, partially refunded and refunded payments allocated to the invoice, by `paid_at` desc, each `{ paymentId, number: <PAY-n>, receiptNumber \| null, paidOn, method, methodLabel, amount, refundedAmount, status }`. `methodLabel`: `card_online` → "<Brand> ending in <last4>" (brand capitalized; "Card ending in <last4>" when unknown); other methods use the `invoices-payments-management` labels. Never reference, note, received-by or recorder. | Backend |
| BR-06 | Card attempt request `POST /public/invoice-links/payments/card-intent { token, idempotencyKey }`. After BR-01 and locking the invoice row, in order: (1) an attempt with this `idempotencyKey` exists for this invoice → `200 { attemptId, clientSecret, amount, currency, status }` with its current `status`; `clientSecret` is retrieved again from the provider only while the attempt is `pending` and has an intent, otherwise it is `null` (the page then follows BR-29 for that status); the same key on another invoice → `409 idempotency_conflict`; (2) stored status not `sent`/`partially_paid` or `balance_due` = 0 → `409 invoice_not_payable` "This invoice can't be paid online."; (3) card not available (BR-04) → `409 card_unavailable`; (4) a pending card attempt exists → BR-14 expiry evaluation, then, if still pending, `409 payment_in_progress` "A payment is already in progress for this invoice."; (5) insert attempt `{ method: card_online, status: pending, amount: balance_due, currency: invoice currency, idempotency_key, expires_at: now + 30 min }` and commit. Then, outside the transaction, the gateway creates a PaymentIntent for exactly that amount and currency (automatic payment methods limited to cards), with provider idempotency key = attempt id and metadata `{ organizationId, invoiceId, attemptId }` (metadata stays inside Stripe), and stores `provider_payment_intent_id`. Gateway failure → attempt `failed` with `failure_category = provider_unavailable`, response `502 payment_provider_unavailable`. Success `201 { attemptId, clientSecret, amount, currency, status: "pending" }`. | Backend |
| BR-07 | Card data boundary. Card number, expiry, CVV and postal code are collected only by the Stripe-hosted Payment Element and sent directly from the browser to Stripe; cardholder name is passed to Stripe as billing details from the browser. FieldOps endpoints never accept, log or store them; FieldOps stores only `card_brand` and `card_last4` read from the succeeded event. `clientSecret` is returned only to the token holder, never logged, audited or persisted. | Both |
| BR-08 | Amount rule. Online card payments always equal the full `balance_due` at attempt creation; there is no client amount. Partial balances arise only from external payments or refunds. | Backend |
| BR-09 | Webhook `POST /webhooks/stripe` (anonymous, raw body ≤ 64 KB else `413`). The `Stripe-Signature` header is verified with the configured webhook secret and Stripe's default timestamp tolerance; missing or invalid → `400`, nothing stored. Handled types: `payment_intent.succeeded`, `payment_intent.payment_failed`, `payment_intent.canceled`, `charge.refunded`; other types → `200`, nothing stored. Each handled event is matched to its attempt by `provider_payment_intent_id`, falling back to `metadata.attemptId` with matching organization and invoice; unmatched → `200`, event stored with outcome `ignored`. | Backend |
| BR-10 | Event idempotency. Each handled event is processed in one transaction that inserts `payment_webhook_events (provider, provider_event_id)` first; a duplicate id → `200`, no effect. Effects are also state-idempotent: a succeeded event for an attempt already `succeeded`/refunded, a failure for a non-pending attempt, or a refund whose cumulative amount ≤ the stored `refunded_amount` changes nothing. Lock order: attempt row, invoice row, organization row. Any failure rolls back everything, including the event row and counters, and returns `500` so Stripe retries. | Backend |
| BR-11 | Success effect (`payment_intent.succeeded`, attempt `pending` or `failed`, event amount and currency equal to the attempt): (a) payment number from `organizations.next_payment_number` (incremented); (b) receipt sequence = 1 + the number of receipts already issued for the invoice, assigned under the invoice lock, receipt number `RCT-<invoice_number>-<sequence two digits minimum>`, unique per organization; (c) one `payments` row: method `card_online`, `status succeeded`, amount, currency, `paid_at = now`, `card_brand`, `card_last4`, `receipt_number`, `recorded_by_user_id` and `received_by_user_id` null, `idempotency_key` = attempt idempotency key, invoice `customer_id`; (d) one allocation for the full amount; (e) invoice `amount_paid += amount`, `balance_due -= amount`, `status` = `paid` when `balance_due` = 0 else `partially_paid`, `updated_at = now`; (f) attempt `succeeded` with `payment_id`; (g) audit (BR-26). If the invoice is `void`, not `sent`/`partially_paid`, or `balance_due` < amount, or the event amount/currency differs from the attempt: no payment is applied, the event is stored with outcome `needs_attention`, the attempt stays unchanged, an audit row `invoice_payment_attempt.needs_attention` is written and the condition is logged by attempt id and category. | Backend |
| BR-12 | Receipt email after commit through `IEmailSender`, to `invoices.recipient_email` when present, following `invoices-payments-management` BR-17 with subject "<organization name>: payment receipt <RCT-…>" and the lines "Receipt <RCT-…> · Payment <PAY-n>", "<amount> · <MMM d, yyyy> · <methodLabel>", "Invoice <INV-n> · Remaining balance <balance>". No link and no attachment. Success sets `payments.receipt_sent_at`; failure is logged by payment id and category and never rolls back. Webhook replays never resend. | Backend |
| BR-13 | Failure and cancellation. `payment_intent.payment_failed` for a pending attempt: store `failure_category` (`card_declined`, `insufficient_funds`, `expired_card`, `incorrect_cvc`, `authentication_failed`, `processing_error`, `other`, mapped from the provider error code) and request cancellation of the PaymentIntent from the gateway. Only when the gateway confirms the intent is `canceled` (or it already is) does the attempt become `failed`; if cancellation is refused because the intent succeeded or is processing, or the gateway is unavailable, the attempt stays `pending` until a later event. `payment_intent.canceled` → `failed` (category kept, else `canceled`). A `failed` attempt never changes the invoice. A later `succeeded` event for a `failed` attempt still applies BR-11 (money was captured). | Backend |
| BR-14 | Pending lock and expiry. While a card attempt is `pending`, new card attempts (BR-06) and external payments (BR-31) for the invoice return `409 payment_in_progress`. A pending attempt older than its `expires_at` is evaluated lazily whenever one of those requests, the public view or the status endpoint touches it: the gateway is asked to cancel the intent; only a confirmed cancellation sets `failed` with category `expired` and releases the lock; otherwise it stays `pending`. An attempt without `provider_payment_intent_id` past `expires_at` becomes `failed` (`provider_unavailable`). At most one pending card attempt per invoice is also guaranteed by a database constraint. | Backend |
| BR-15 | Refunds (`charge.refunded`, attempt `succeeded` or `partially_refunded`): `cumulative` = the charge's total refunded amount; `delta` = `cumulative` − payment `refunded_amount`; `delta` ≤ 0 → no effect. Otherwise, in one transaction: payment and attempt `refunded_amount = cumulative`, status `refunded` when `cumulative` = amount else `partially_refunded`; invoice `amount_paid -= delta`, `balance_due += delta`, `updated_at = now`; invoice status recomputed when it is not `void`: `amount_paid` = 0 → `sent`, 0 < `amount_paid` < `total` → `partially_paid`, = `total` → `paid` (a `paid` invoice reopens). Allocations keep the gross amount; net = `amount − refunded_amount`. Audit per BR-26. No email. | Backend |
| BR-16 | Status `POST /public/invoice-links/payments/status { token, attemptId }` → `200 { attemptId, status, failureCategory \| null, payment: BR-05 item \| null, invoice: { status, amountPaid, balanceDue } }`. It writes only through BR-14 expiry evaluation. | Backend |
| BR-17 | Bank transfer notice `POST /public/invoice-links/payments/bank-transfer-notice { token, idempotencyKey }`: requires `paymentOptions` not null and bank details configured (else `409 invoice_not_payable` / `409 bank_transfer_unavailable`). When a `pending` bank transfer attempt already exists for the invoice → `200 { changed: false, reportedOn }`. Otherwise insert attempt `{ method: bank_transfer, status: pending, amount: balance_due, currency, idempotency_key, expires_at: null }`, audit `invoice.bank_transfer_reported`, then after commit email `organizations.email` (skipped when null) "Bank transfer reported for <INV-n>" with "A customer reported a bank transfer of <amount> for invoice <INV-n> on <MMM d, yyyy>. Confirm it in your bank before recording the payment." → `201 { changed: true, reportedOn }`. No balance or status change; it never blocks other payments and is never reconciled automatically. `transferReportedOn` = the local date of that pending attempt. | Backend |
| BR-18 | Cash: no endpoint and no write; the page shows contact instructions from `paymentOptions.cash`. | Frontend |
| BR-19 | Public PDFs (headers per `invoice-draft-delivery` BR-20): invoice `POST /public/invoice-links/pdf` (unchanged); receipt `POST /public/invoice-links/receipt { token, paymentId }` for a payment with `receipt_number`, filename `<RCT-…>.pdf`, generated on demand with MigraDoc from stored data: organization block, "RECEIPT", receipt number, payment number, paid date, Bill to (snapshot), invoice number, method label, amount, refunded amount and net when > 0, invoice total, total paid and balance at generation time, and "Thank you for your payment!". Never reference, note, recorder or provider ids. Payments without a receipt number → `404`. | Backend |
| BR-20 | Completion report `POST /public/invoice-links/completion-report { token }` (`hasCompletionReport` else `404`), filename `<WO-n>-completion-report.pdf`, generated on demand: organization block, work order number and title, service address (snapshot), completion date, technician name, completion summary, checklist items with completed/not completed text, sign-off acknowledgement method label, signer name and signed date. Never the signature image, internal notes, incidents, materials, costs or time entries. | Backend |
| BR-21 | Photos: `POST /public/invoice-links/photos { token }` → `[{ photoId, type: "before"\|"after", caption \| null, takenOn }]` from `visit_evidence` with `evidence_type` `before` or `after` of the work order's completed or approved visits, before first then by `created_at`; `takenOn` is the `created_at` date in the organization timezone; `photoCount` is their number. `POST /public/invoice-links/photos/content { token, photoId }` returns the image with its stored MIME type and the BR-23 headers. `during`, `incident` and `other` evidence are never returned. | Backend |
| BR-22 | Review `POST /public/invoice-links/review { token, rating, comment }`: allowed only when the invoice is `paid` (else `409 review_not_available`) and no review exists for its work order (else `409 review_exists`). `rating` integer 1–5 ("Select a rating."); `comment` trimmed 0–500 characters, empty → null ("Use 500 characters or fewer."). Insert `invoice_reviews` with the work order, invoice, `technician_id` = primary assigned technician of the latest completed visit or null, rating, comment. Concurrent submissions create exactly one review (the other `409 review_exists`). `201 { submitted: true }`. Reviews are immutable. `review.available` = invoice `paid` and no review; `review.submitted` = a review exists. | Backend |
| BR-23 | Public hardening (`invoice-draft-delivery` BR-21 extended). Per client per 5 minutes: read group (`view`, `logo`, `photos`, `photos/content`) 60; PDFs (`pdf`, `receipt`, `completion-report`) 10; `payments/status` 60; `payments/card-intent` 5. Per invoice: `card-intent` 10 per hour; `bank-transfer-notice` and `review` 3 per hour. Global limits as the precedent. Excess → `429` with `Retry-After`. Bodies ≤ 16 KB (`413`). `Cache-Control: no-store`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`. The webhook has no rate limit (BR-09 signature and size apply). | Backend |
| BR-24 | Bank details storage. Account number encrypted with AES-256-GCM using a 32-byte key from configuration (never in source, logs or reports), stored with its nonce and tag in `bank_account_number_ciphertext`; `bank_account_last4` is stored for masking. Plaintext exists only in memory while saving and while building BR-04. Missing key: bank endpoints return `503 bank_details_unavailable` and bank transfer is unavailable on the public page. | Backend |
| BR-25 | Bank details API (Owner only, others `403`): `GET /organization-settings/bank-details` → `{ configured, bankName, accountNumberMasked: "•••• <last4>", routingNumber, updatedAt }` (nulls when not configured). `PUT` same path `{ bankName, accountNumber, routingNumber, updatedAt }`: `bankName` trimmed 1–120 ("Enter the bank name."); `accountNumber` digits only 4–17, required when not configured, empty keeps the stored one ("Enter a valid account number."); `routingNumber` exactly 9 digits ("Enter a 9-digit routing number."); `updatedAt` = `bank_details_updated_at` (null when not configured) else `409 bank_details_changed`. Response as GET. The full account number is never returned by internal endpoints. | Backend |
| BR-26 | Audit (organization, invoice `branch_id`, `actor_user_id` null for customer and webhook actions; Owner for bank details): `invoice_payment_attempt.created` (metadata `{ attemptId, method, amount }`), `payment.recorded` (after `{ amount, currency, method }`, metadata `{ paymentNumber, receiptNumber, invoiceId, source: "online" }`), `invoice.payment_applied` (before/after `{ status, amountPaid, balanceDue }`), `invoice_payment_attempt.failed` (metadata `{ attemptId, failureCategory }`), `invoice_payment_attempt.needs_attention` (metadata `{ attemptId, category }`), `payment.refunded` (before/after `{ status, refundedAmount }`), `invoice.refund_applied` (before/after `{ status, amountPaid, balanceDue }`, metadata `{ paymentId }`), `invoice.bank_transfer_reported` (metadata `{ amount }`), `invoice.review_submitted` (metadata `{ rating }`), `organization.bank_details_updated` (metadata `{}`). Audit rows, logs and problem details never contain tokens, client secrets, provider ids, raw events, card brand/last4, bank name/account/routing data, review comments, emails or customer names. Logs may contain attempt, payment or invoice ids, event type and outcome category. | Backend |
| BR-27 | Page structure (`/invoices/view`, token handling per `invoice-draft-delivery` BR-28, kept in tab-scoped storage across the Stripe authentication redirect). Header: organization logo or initials and name only. Title "Invoice <INV-n>" with status chip ("Sent", "Partially paid", "Paid", "Overdue"; text and icon), subtitle "<service title> · Completed <MMM d, yyyy>" (completion part hidden when null), **Download PDF**. Timeline steps "Service completed", "Invoice sent", "Payment", "Receipt", each with date or "Pending" and complete/current/pending indicators with text. Left card: "Amount due" (balance), "Due date: <MMM d, yyyy>" and "Payment due in <n> days" / "Due today" / "<n> days overdue"; organization block; lines and totals (frozen preview); "Service details" with Work order (text, not a link), Completion note, **View completion report** (opens the PDF) and **View before & after photos** (gallery dialog with type label, caption and keyboard navigation; hidden when `photoCount` = 0; report button hidden when unavailable). These elements are identical across payable, processing and failure states. | Frontend |
| BR-28 | Payment panel (shown when `paymentOptions` is not null): "Pay invoice", "Pay securely online using your preferred payment method.", method selector **Credit or debit card**, **Bank transfer (ACH)**, **Cash** ("Arrange with <organization name>"); unavailable methods are disabled with "Card payments aren't available right now." / "Bank transfer isn't available for this invoice."; the first available method is selected. Card: **Cardholder name** (required, 1–120), Stripe Payment Element styled with the page tokens (card number, expiry, CVC, postal code), primary **Pay <balance>** with lock icon, "Secure encrypted payment", "You will receive a receipt by email after payment." (only with `receiptEmail`). Bank transfer: "Bank transfer instructions", "Send a payment using your bank's online bill pay or ACH transfer with the following details.", rows Bank name, Account number (full), Routing number, Payment reference, Amount, each with a copy button announcing "Copied"; "Use the invoice number as the payment reference."; primary **I sent the transfer**; info "Bank transfers may take 1–3 business days to appear."; after reporting, "Transfer reported on <MMM d, yyyy>. We'll confirm it once it arrives." replaces the button; "Prefer cash? Contact <organization name> to arrange payment.". Cash: "Contact <organization name> to arrange a cash payment." with phone (`tel:`) and email (`mailto:`) links when set. Footer link "I have a question about this invoice" → `mailto:<organization email>?subject=Invoice <INV-n>` (hidden without email). | Frontend |
| BR-29 | Card interactions. **Pay** first validates cardholder name and the Payment Element; then calls `card-intent` with an `idempotencyKey` generated when the card form is shown and reused for retries of the same submission (network failures retry with the same key); then confirms with Stripe (redirect only when authentication requires it, returning to `/invoices/view`). The `attemptId` is kept in tab-scoped storage with the token before confirming. On return, before any call, the page removes every query parameter Stripe adds (`payment_intent`, `payment_intent_client_secret`, `redirect_status`) by replacing the URL with `/invoices/view`; it never reads, sends, stores or logs them and resumes polling with the stored `attemptId` (or `activeAttempt` from the view). A `card-intent` replay returning `clientSecret = null` follows its `status` exactly like a polled status. From the first click until a final status, the panel shows the overlay "Processing your payment" / "Please keep this page open. This may take a few moments.", every input and method is disabled and the button reads "Processing <balance>" so no second submission is possible. The page polls `payments/status` every 2 s for up to 60 s (also on load when `activeAttempt` exists): `succeeded` → success view (BR-30); `failed` → error alert "Payment wasn't completed" with the category message (declined/insufficient funds/incorrect CVC/expired card/authentication: "Your card was declined. Check the details or try another payment method."; `expired`, `canceled`: "The payment session expired. Try again."; others: "We couldn't process your payment. Try again or use another payment method."), cardholder name kept, primary **Try payment again** (new key) and link **Choose another payment method** (focuses the method selector); still `pending` after 60 s → "We're still confirming your payment. You can safely close this page; we'll email your receipt." with no retry. `409 payment_in_progress` → the same still-confirming message; `409 invoice_not_payable` → reload; `502` → "We couldn't start the payment. Try again."; `429` → precedent message. Bank notice: progress on the button, `201`/`200` → reported text, failures "We couldn't send your notice. Try again.". | Frontend |
| BR-30 | Success and paid view (after a succeeded status, or on load when `status = paid`): status chip "Paid", all timeline steps complete; success card "Payment successful", "Thank you, <first name>. Your payment has been confirmed." (without name: "Thank you. Your payment has been confirmed."), the latest payment net amount, "Paid on <MMM d, yyyy>"; rows Receipt number, Transaction reference (`<PAY-n>`), Payment method (`methodLabel`, card brand mark when known), Email confirmation "A receipt was sent to <receiptEmail>" (only when present); **Download receipt** (primary, hidden without receipt number) and **Download invoice**. "Payment summary": Invoice, Service, Work order, Subtotal, Tax, "Total paid" (net amount paid), Balance, and refunded amounts per payment when > 0. Review card (BR-22 `available`): "How was your service?", Technician and Service date (rows hidden when null), "Your feedback helps us improve.", 5-star radio group (required), comment "Tell us about your experience (optional)" with counter "<n>/500", **Submit review** (progress while pending), **Maybe later** (hides the card for this tab); success replaces the card with "Thanks for your feedback!"; `409 review_exists` → the same thanks text; other failures "We couldn't send your review. Try again." keeping values. Info band "What happens next?" / "This invoice is now closed. Your receipt is available anytime." with no button. A `partially_paid` or refunded invoice returns to the payable layout with the payments listed under "Payment summary". | Frontend |
| BR-31 | `invoices-payments-management` adjustments: (a) metrics, aging, average time to pay, recent payments and payment lists/exports use net amounts (`amount − refunded_amount`); (b) payment rows add `status` and `refundedAmount`, show "Refunded" / "Partially refunded" tags (text, not color only) and net amount; payment CSV adds column "Status"; (c) method `card_online` labelled "Online card", not selectable in the drawer, filterable in the Payments tab; (d) `receivedByName` is null for online payments and shown "—"; (e) recording an external payment assigns the next receipt number (BR-11 b) under the existing invoice lock; (f) after the existing BR-12 guard (2), a pending card attempt (after BR-14 evaluation) → `409 payment_in_progress` "An online card payment is in progress for this invoice. Try again in a few minutes.", shown by the drawer as its message, closing and reloading. Nothing else changes. | Both |
| BR-32 | Company settings section "Bank transfer details" on `/admin/company` for the Owner only (hidden for other roles), following the existing Design 17 card and field patterns: **Bank name**, **Account number** (shows "•••• <last4>" with **Replace** revealing an empty input; required when not configured), **Routing number**, **Save bank details** (progress, success "Bank details saved."), field errors from BR-25, `409` "These details changed. Refresh to see the latest.", `503` "Bank details can't be saved right now.", helper "Customers see these details on unpaid invoices." Unsaved changes use the shared discard-changes dialog. | Frontend |
| BR-33 | Responsive, accessibility and fidelity. ≥ 1280 px: invoice card and payment panel side by side as the desktop designs; 768–1279 px: panel below the invoice card; < 768 px (mobile design): header logo and name, title block, "Amount due" card, the invoice lines/totals and service details collapsed in an "Invoice summary" accordion, payment panel, **Pay** button sticky at the bottom with "Secure encrypted payment"; success view stacks success card, details, downloads and review; no horizontal page scroll at 375 px. Status, timeline, method selection, payment status and stars never rely on color alone; the method selector is a radio group; the processing overlay is announced (`aria-live` polite) and sets the panel busy; alerts are announced; the photo dialog and errors follow the existing focus rules; targets ≥ 44 × 44 px on touch layouts. Design deviations: no portal navigation, avatar, breadcrumbs, "Back to invoices", "your portal" text or "Save this payment method"; organization header replaces the FieldOps logo; card fields are the Stripe Payment Element; work order is not a link; neutral state per BR-34; mockup data are examples. | Frontend |
| BR-34 | Neutral unavailable state (any `404 invoice_link_unavailable`, or no token): FieldOps brand header, document-with-broken-link illustration, "This invoice is no longer available", "This link can no longer be used. Contact the company if you need a new invoice link.", lock line "For your protection, invoice details are not shown." No contact button, phone, organization name or invoice data. `429` → "Too many attempts. Please wait a few minutes and try again."; load error → "We couldn't load this invoice. Try again." with **Retry**. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | Attempt card `pending` | **Pay** | Customer | BR-06 |
| Attempt card `pending` | `succeeded` + payment `succeeded` | `payment_intent.succeeded` | Stripe | BR-10, BR-11 |
| Attempt card `failed` | `succeeded` + payment | `payment_intent.succeeded` | Stripe | BR-11, BR-13 |
| Attempt card `pending` | `failed` | `payment_failed` + confirmed cancel; `canceled`; expiry + confirmed cancel | Stripe / system | BR-13, BR-14 |
| Attempt / payment `succeeded`, `partially_refunded` | `partially_refunded` / `refunded` | `charge.refunded` with positive delta | Stripe | BR-15 |
| Invoice `sent` / `partially_paid` | `paid` | Card success | Stripe | BR-11 |
| Invoice `paid` / `partially_paid` | `partially_paid` / `sent` | Refund delta | Stripe | BR-15 |
| — | Attempt bank transfer `pending` | **I sent the transfer** | Customer | BR-17 |
| — | Review created | **Submit review** | Customer | BR-22 |
| Invoice with pending card attempt | unchanged | External payment | Owner, Accounting | `409 payment_in_progress` |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): `organizations`,
  `organization_logos`, `invoices`, `invoice_lines`, `invoice_access_tokens`,
  `payments`, `payment_allocations`, `work_orders`, `visits`,
  `visit_assignments`, `technician_profiles`, `visit_checklist_items`,
  `visit_evidence`, `customer_signoffs`, `customers`.
- Written: `invoice_payment_attempts`, `payment_webhook_events`, `payments`,
  `payment_allocations`, `invoices` (`amount_paid`, `balance_due`, `status`,
  `updated_at`), `organizations` (`next_payment_number`, bank details),
  `invoice_reviews`, `audit_logs`.
- Not written: `invoice_lines`, `invoice_access_tokens`, invoice amounts other
  than `amount_paid`/`balance_due`, visits, evidence, sign-offs,
  `notifications`, `payments.receipt_storage_key`.
- Schema amendments: **approved by the user 2026-10-09** (OD-15).
  Implementation updates `docs/database/fieldops-schema.sql` first, then the
  EF model, and generates one migration only under `--generate-migration`.
  Agents never apply it. Review by `database-reviewer`.
  - SA-14 enums: `ALTER TYPE payment_method ADD VALUE 'card_online'`;
    `CREATE TYPE payment_status AS ENUM ('succeeded','partially_refunded','refunded')`;
    `CREATE TYPE payment_attempt_status AS ENUM ('pending','succeeded','failed','partially_refunded','refunded')`.
    Constraints and index predicates in the same migration compare
    `method::text`, never the new enum literal.
  - SA-15 `payments`: add `status payment_status NOT NULL DEFAULT 'succeeded'`,
    `refunded_amount numeric(14,2) NOT NULL DEFAULT 0`, `receipt_number
    varchar(60)`, `receipt_sent_at timestamptz`, `card_brand varchar(20)`,
    `card_last4 char(4)`; `received_by_user_id` becomes nullable; `UNIQUE
    (organization_id, receipt_number)`; checks `refunded_amount BETWEEN 0 AND
    amount`, `(status = 'succeeded') = (refunded_amount = 0)`, `(status =
    'refunded') = (refunded_amount = amount)`, `(method::text = 'card_online')
    = (received_by_user_id IS NULL)`, `(method::text = 'card_online') =
    (card_last4 IS NOT NULL)`. Existing rows keep `succeeded`, 0 and null
    receipt number.
  - SA-16 `invoice_payment_attempts (id uuid PRIMARY KEY DEFAULT
    gen_random_uuid(), organization_id uuid NOT NULL REFERENCES
    organizations(id), invoice_id uuid NOT NULL, method payment_method NOT
    NULL CHECK (method::text IN ('card_online','bank_transfer')), status
    payment_attempt_status NOT NULL DEFAULT 'pending', amount numeric(14,2)
    NOT NULL CHECK (amount > 0), currency char(3) NOT NULL, idempotency_key
    uuid NOT NULL, provider_payment_intent_id varchar(255) UNIQUE,
    payment_id uuid, failure_category varchar(40), refunded_amount
    numeric(14,2) NOT NULL DEFAULT 0 CHECK (refunded_amount >= 0 AND
    refunded_amount <= amount), expires_at timestamptz, created_at
    timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL
    DEFAULT now(), FOREIGN KEY (organization_id, invoice_id) REFERENCES
    invoices(organization_id, id), FOREIGN KEY (organization_id, payment_id)
    REFERENCES payments(organization_id, id), UNIQUE (organization_id,
    idempotency_key), CHECK ((status = 'succeeded' OR status =
    'partially_refunded' OR status = 'refunded') = (payment_id IS NOT
    NULL)))`; `CREATE UNIQUE INDEX ux_invoice_payment_attempts_pending ON
    invoice_payment_attempts(invoice_id, method) WHERE status = 'pending'`;
    `CREATE INDEX ix_invoice_payment_attempts_invoice ON
    invoice_payment_attempts(invoice_id, created_at DESC)`.
  - SA-17 `payment_webhook_events (id uuid PRIMARY KEY DEFAULT
    gen_random_uuid(), provider varchar(20) NOT NULL DEFAULT 'stripe',
    provider_event_id varchar(255) NOT NULL, event_type varchar(100) NOT
    NULL, organization_id uuid REFERENCES organizations(id), attempt_id uuid
    REFERENCES invoice_payment_attempts(id), outcome varchar(40) NOT NULL
    CHECK (outcome IN ('applied','no_effect','ignored','needs_attention')),
    received_at timestamptz NOT NULL DEFAULT now(), UNIQUE (provider,
    provider_event_id))`. No event payload is stored.
  - SA-18 `organizations`: add `stripe_account_id varchar(255)`, `bank_name
    varchar(120)`, `bank_account_number_ciphertext bytea`,
    `bank_account_last4 varchar(4)`, `bank_routing_number char(9)`,
    `bank_details_updated_at timestamptz`, with a check that the five bank
    columns are all null or all not null. New `invoice_reviews (id uuid
    PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL
    REFERENCES organizations(id), work_order_id uuid NOT NULL, invoice_id uuid
    NOT NULL, technician_id uuid, rating
    smallint NOT NULL CHECK (rating BETWEEN 1 AND 5), comment varchar(500),
    created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY
    (organization_id, work_order_id) REFERENCES work_orders(organization_id,
    id), FOREIGN KEY (organization_id, invoice_id) REFERENCES
    invoices(organization_id, id), FOREIGN KEY (organization_id,
    technician_id) REFERENCES technician_profiles(organization_id, id),
    UNIQUE (work_order_id))`.
- Cross-row invariants: Σ net allocations ≤ invoice total; `amount_paid` =
  Σ net amounts of the invoice's payments; enforced transactionally by
  BR-11, BR-15 and BR-31.

## Tenant isolation and authorization

- Public endpoints: organization and invoice come only from the token hash
  (BR-01); `attemptId`, `paymentId` and `photoId` are authorized against that
  invoice (BR-02); any mismatch is the identical `404`.
- Webhook: authorized only by the Stripe signature; organization and invoice
  come from the stored attempt, never from event metadata alone (metadata
  must match the attempt, BR-09).
- Internal bank details endpoints resolve the organization from the session
  and require the Owner role; the full account number is never returned
  internally.
- The external payment guard (BR-31 f) keeps the existing
  `invoices-payments-management` authorization and branch scope.
- Responses use `Cache-Control: no-store`; logs and audit follow BR-26.

## API contracts

Contract status: Final. Public base path `/public/invoice-links` (JSON POST,
token in body); webhook `/webhooks/stripe`; internal paths as listed.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/public/invoice-links/view` | `{ token }` | `200 PublicInvoice` (BR-03) | `404 invoice_link_unavailable` · `413` · `429` | Token |
| POST | `/public/invoice-links/pdf`, `/logo` | `{ token }` | Unchanged | Unchanged | Token |
| POST | `/public/invoice-links/payments/card-intent` | `{ token, idempotencyKey }` | `201 { attemptId, clientSecret, amount, currency, status: "pending" }` · `200 { attemptId, clientSecret \| null, amount, currency, status }` (BR-06 replay) | `400` · `404` · `409 invoice_not_payable`, `card_unavailable`, `payment_in_progress`, `idempotency_conflict` · `413` · `429` · `502 payment_provider_unavailable` | Token |
| POST | `/public/invoice-links/payments/status` | `{ token, attemptId }` | `200` BR-16 | `400` · `404` · `413` · `429` | Token |
| POST | `/public/invoice-links/payments/bank-transfer-notice` | `{ token, idempotencyKey }` | `201`/`200 { changed, reportedOn }` | `400` · `404` · `409 invoice_not_payable`, `bank_transfer_unavailable` · `413` · `429` | Token |
| POST | `/public/invoice-links/receipt` | `{ token, paymentId }` | `200 application/pdf` | `400` · `404` · `413` · `429` | Token |
| POST | `/public/invoice-links/completion-report` | `{ token }` | `200 application/pdf` | `404` · `413` · `429` | Token |
| POST | `/public/invoice-links/photos` | `{ token }` | `200 [{ photoId, type, caption, takenOn }]` | `404` · `413` · `429` | Token |
| POST | `/public/invoice-links/photos/content` | `{ token, photoId }` | `200 image/jpeg\|image/png` | `400` · `404` · `413` · `429` | Token |
| POST | `/public/invoice-links/review` | `{ token, rating, comment }` | `201 { submitted: true }` | `400` · `404` · `409 review_not_available`, `review_exists` · `413` · `429` | Token |
| POST | `/webhooks/stripe` | Raw Stripe event, `Stripe-Signature` | `200` | `400` invalid signature · `413` · `500` processing failure (retried) | Signature |
| GET | `/organization-settings/bank-details` | — | `200 { configured, bankName, accountNumberMasked, routingNumber, updatedAt }` | `403` · `503 bank_details_unavailable` | Owner |
| PUT | `/organization-settings/bank-details` | `{ bankName, accountNumber, routingNumber, updatedAt }` | `200` GET body | `400` · `403` · `409 bank_details_changed` · `503` | Owner |
| POST | `/invoices/{invoiceId}/payments` | Unchanged | Unchanged; `payment` row adds `status`, `refundedAmount`; receipt number assigned | Adds `409 payment_in_progress` | Unchanged |
| GET | `/invoices/overview`, `/invoices/payments`, `/invoices/payments/export`, `/invoices`, `/invoices/export` | Unchanged; `method` accepts `card_online` | Net amounts; `PaymentRow` adds `status`, `refundedAmount`, `receivedByName \| null` | Unchanged | Unchanged |

Errors use ProblemDetails with `code`; `400` includes field `errors`. Money
values are JSON numbers with 2 decimals; instants ISO 8601 UTC; dates ISO
`yyyy-mm-dd` in the organization timezone. Internal contracts: a payment
gateway interface (create intent, retrieve client secret, cancel intent,
verify and parse webhook) with a Stripe implementation and a deterministic
fake for tests; the existing `IEmailSender`, MigraDoc PDF infrastructure and
public rate-limit policies. Stripe keys and the bank details key come from
configuration (user-secrets/environment), never from source.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Public invoice (payable) | Skeleton for header, timeline, card and panel | No photos/report → buttons hidden | Neutral (BR-34), `429`, load error + Retry | Token only | BR-27, BR-28 |
| Card payment | Overlay + "Processing <balance>" | N/A | Failure alert, still confirming, `502`, `409` (BR-29) | Card unavailable → disabled | Success view |
| Bank transfer / cash | Progress on **I sent the transfer** | Not configured → disabled | "We couldn't send your notice. Try again." | — | Reported text |
| Paid / success view | Skeleton | No receipt → no **Download receipt** | PDF failure "We couldn't download the file. Please try again." | Token only | BR-30 |
| Review card | Progress on submit | Hidden when unavailable | BR-30 messages | — | Thanks text |
| Photo gallery | Spinner per image | N/A (hidden) | "We couldn't load this photo." | — | Image with label |
| Bank details (settings) | Section skeleton | Not configured → empty fields | BR-32 messages | Owner only | "Bank details saved." |

- Mockups (Approved; reviewed together as one flow; deviations in BR-33,
  BR-34): `design/assets/13-design.png` (card, payable),
  `design/assets/Factura INV-1048_ transferencia bancaria (ACH).png`,
  `design/assets/Procesando el pago de la factura.png`,
  `design/assets/Factura con pago rechazado.png`,
  `design/assets/14-design.png` (success/receipt/review),
  `design/assets/Pago y recibo móvil FieldOps.png` (mobile),
  `design/assets/Factura no disponible.png` (neutral). Cash panel and the
  Company settings section have no mockup and follow BR-28 and BR-32 with
  existing patterns.
- Responsive and accessibility constraints: BR-33.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid, expired, revoked, draft or void token; foreign attempt/payment/photo id | Identical `404`; neutral state | None |
| Invoice paid or zero balance on pay/notice | `409 invoice_not_payable`; page reloads | None |
| Pending card attempt exists | `409 payment_in_progress`; still-confirming message / drawer message | None |
| Provider unavailable at intent creation | `502`; "We couldn't start the payment. Try again." | Attempt `failed` (`provider_unavailable`) |
| Card declined / failed | Webhook → `failed` after confirmed cancel; failure alert | Attempt only; invoice unchanged |
| Pending past 30 min, cancel not confirmed | Stays `pending`; `409 payment_in_progress` | None |
| Invalid webhook signature | `400` | None |
| Duplicate webhook event | `200` | None |
| Success not applicable (void, amount mismatch, balance below amount) | `200`; outcome `needs_attention` | Event + audit only |
| Failure while applying an event | `500`; Stripe retries | Full rollback incl. event row and counters |
| Receipt email failure | Logged by payment id | Payment kept |
| Review not paid / duplicate | `409 review_not_available` / `review_exists` | None |
| Rate limit / oversized body | `429` with `Retry-After` / `413` | None |
| Bank details key missing | `503 bank_details_unavailable`; bank transfer unavailable | None |
| Stale bank details | `409 bank_details_changed` | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Tokens valid, revoked, expired, malformed, of a draft and of a void invoice; a valid token combined with an `attemptId`, `paymentId` or `photoId` of another invoice or organization | Each public endpoint is called (table-driven, one representative per id family) | Only valid combinations succeed; all others return the identical `404 invoice_link_unavailable` with no data and no write; paid invoices remain accessible |
| AC-02 | Invoices `sent`, `partially_paid` (external payment), overdue and `paid`, with and without Stripe keys and bank details, photos, completed visit and technician | The view is loaded | `PublicInvoice` fields, timeline, service, payment options (bank details only while payable, full number decrypted), `activeAttempt`, payments with method labels and review flags follow BR-03 to BR-05; no excluded ids or internal data |
| AC-03 | A payable invoice with the fake gateway | `card-intent` is called, repeated with the same key, called with a new key while pending, on a paid invoice, without card keys, and with the gateway failing | Attempt pending for the full balance with 30-minute expiry and intent created with attempt-id idempotency; same key → `200` same attempt; new key → `409 payment_in_progress`; `409 invoice_not_payable`; `409 card_unavailable`; gateway failure → `502` and attempt `failed (provider_unavailable)` |
| AC-04 | Webhook requests with valid, missing and invalid signatures, oversized bodies, unhandled types and unknown intents | They are posted | Invalid/missing → `400`, nothing stored; > 64 KB → `413`; unhandled → `200`, nothing stored; unknown intent → `200`, event `ignored` |
| AC-05 | A pending card attempt on a `sent` invoice and one on a `partially_paid` invoice | `payment_intent.succeeded` is delivered and the status endpoint is polled | One `card_online` payment numbered `PAY-n` with receipt `RCT-<n>-01` (then `-02` for a second receipt on the same invoice), brand/last4, null recorder/receiver, one allocation, invoice `paid` with balance 0, attempt `succeeded`, audit per BR-26; receipt emailed after commit and `receipt_sent_at` set; status returns `succeeded` with the payment |
| AC-06 | A succeeded event | It is delivered twice sequentially, twice concurrently, and as two distinct event ids for the same intent; concurrent successes run on two invoices of one organization | Exactly one payment, number, receipt, allocation, balance change, audit pair and email per attempt; duplicates return `200` without effect; payment and receipt numbers are unique and gap-free per organization/invoice |
| AC-07 | Pending attempts | `payment_failed` arrives with the gateway confirming cancel, refusing cancel (intent succeeded), and unavailable; `canceled` arrives; a `succeeded` event later arrives for a failed attempt | Confirmed → `failed` with mapped category, invoice unchanged, lock released and status returns `failed`; refused/unavailable → stays `pending`; `canceled` → `failed`; the late success applies BR-11 once |
| AC-08 | A pending card attempt past `expires_at` | `card-intent`, an external payment, the view and the status endpoint touch it with the gateway confirming and refusing cancel; an attempt without intent id past expiry | Confirmed → `failed (expired)` and a new attempt or external payment succeeds; refused → `409 payment_in_progress` and attempt stays `pending`; no intent id → `failed (provider_unavailable)` |
| AC-09 | A `paid` card payment of 357.28 | `charge.refunded` arrives with cumulative 100.00, again with 100.00, then 357.28, and a refund event duplicate | First: refunded 100.00, payment and attempt `partially_refunded`, invoice `partially_paid` with balance 100.00; replay no effect; then `refunded`, invoice `sent`, `amount_paid` 0, balance 357.28; audit `payment.refunded` and `invoice.refund_applied` each applied step; no email |
| AC-10 | Invoices void, with amount mismatch, or with balance below the attempt amount | A succeeded event is delivered | No payment is applied; event outcome `needs_attention`; audit `invoice_payment_attempt.needs_attention`; attempt unchanged |
| AC-11 | A succeeded event and a failure injected after the allocation insert | The webhook is called, then retried without the failure | First returns `500` with payment, allocation, invoice, attempt, event row, payment counter and receipt sequence rolled back; the retry applies once with the unconsumed numbers |
| AC-12 | A payable invoice with and without bank details and organization email | The bank notice is posted, repeated, posted on a paid invoice and posted without details | Pending `bank_transfer` attempt, audit and organization email after commit (skipped without email), repeat `200 changed = false`, `409 invoice_not_payable`, `409 bank_transfer_unavailable`; balance and status unchanged; card payments remain possible |
| AC-13 | Payments with and without receipt number, with refunds, a completed visit with checklist, sign-off with signature, and evidence of every type | Receipt, completion report, photos and photo content are requested | PDFs with BR-19/BR-20 content, filenames and headers, no signature image or excluded data; photos only `before`/`after` in order; `404` for payments without receipt and when no completed visit |
| AC-14 | A `sent`, a `paid` invoice with and without completed visit/technician, and a reviewed work order | Reviews are submitted with valid and invalid rating/comment (table-driven) and concurrently | `409 review_not_available` before payment; one review with null or primary technician; `400` field errors; concurrent pair yields one `201` and one `409 review_exists` |
| AC-15 | Public endpoints and the webhook | Requests exceed each BR-23 limit and 16 KB / 64 KB | `429` with `Retry-After` per group and per invoice; `413` for oversized bodies; webhook not rate limited; BR-23 headers present |
| AC-16 | Every operation of this feature | Audit rows and captured logs are inspected | Actions and fields follow BR-26; none contains tokens, client secrets, provider ids, card brand/last4, bank data, review comments, emails or names; FieldOps endpoints accept no card fields (unknown fields rejected) |
| AC-17 | Owner and every other role; the encryption key present and missing | Bank details GET and PUT are called with valid, invalid (table-driven), stale and empty account values | Owner only (`403` otherwise); ciphertext differs from the plaintext and decrypts to it; GET returns only the masked number; empty account keeps the stored one; `400`, `409 bank_details_changed`, `503` per BR-24/BR-25; audit without values |
| AC-18 | Payments external, online, partially refunded and refunded; a pending card attempt | The hub overview, lists, exports and record payment are used | Net amounts, status/refunded fields and CSV "Status", "Online card" label and filter, null received-by, receipt numbers for new external payments, and `409 payment_in_progress` follow BR-31; nothing else changes |
| AC-19 | The EF model and the authoritative schema | The migration is generated | `fieldops-schema.sql` contains SA-14 to SA-18 exactly; the model matches; one migration is generated, not applied, and is valid with the new enum value (no literal use in the same transaction); `database-reviewer` approves |
| AC-20 | `/invoices/view#token=…` with a valid token, without a token, unavailable and rate limited | The page loads | Token removed from the URL before any call and sent only in POST bodies; organization header, status chip, timeline, amount due, lines, service details, report and photo buttons follow BR-27; neutral, `429` and error states follow BR-34; no portal elements exist |
| AC-21 | View responses with card/bank available and unavailable, a reported transfer, and organization contacts present and absent | Methods are selected and bank actions used | Default selection, disabled reasons, card form, bank instructions with copy feedback, reported text, cash contacts and the question link follow BR-28 |
| AC-22 | The card form with invalid and valid input; responses `201`, `409` codes, `502`, network failure; statuses pending → succeeded, pending → failed (each category), pending > 60 s; an `activeAttempt` on load; a replay with `clientSecret = null`; return from authentication with Stripe query parameters | **Pay**, **Try payment again** and **Choose another payment method** are used | Client validation, one intent request per submission and same-key network retry, overlay and disabled inputs, polling, success view, failure alert texts, still-confirming text and focus behavior follow BR-29; on return the Stripe parameters are removed from the URL before any call and never sent, and polling resumes with the stored attempt |
| AC-23 | A paid invoice with card and external payments, with and without receipt and recipient, with review available, submitted and failing | The success view renders and downloads/review are used | Success card, rows, downloads, payment summary with refunds, review states, **Maybe later**, thanks text and the info band follow BR-30; a refunded invoice shows the payable layout |
| AC-24 | Owner and another role on `/admin/company`, configured and not configured, with `400`, `409` and `503` responses | The bank section is used | Visibility, masked number and **Replace**, validation, messages and discard dialog follow BR-32 |
| AC-25 | The approved designs at 1440, 1024 and 375 px, keyboard only | The payable, transfer, processing, failure, success, mobile and neutral states are reviewed (user visual QA) | Layout, mobile accordion and sticky pay button, deviations, radio-group semantics, live regions, focus and non-color indicators follow BR-33 and BR-34 |

AC count 25. The flow is a public payment surface with financial integrity,
idempotency, refunds and encrypted data, so each criterion covers a distinct
risk group.

## Testing requirements

This feature changes financial integrity (balance, status, numbering,
refunds), a public payment boundary, webhook authentication and idempotency,
multi-record concurrency and encrypted sensitive data.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Refund delta and invoice status recomputation; failure category mapping; receipt number format; bank details encryption round trip and masking | AC-07, AC-09, AC-05, AC-17 |
| Backend integration | Webhook: signature, unhandled/unknown, success effects and receipt email, duplicate/concurrent events, rollback | AC-04, AC-05, AC-06, AC-11 |
| Backend integration | Card intent lock and idempotency; failure with confirmed cancel; expiry; external payment blocked | AC-03, AC-07, AC-08, AC-18 |
| Backend integration | Partial and full refund reconciliation and replays; needs-attention cases | AC-09, AC-10 |
| Backend integration | Public view, bank notice, PDFs/photos, review (incl. concurrency) | AC-02, AC-12, AC-13, AC-14 |
| Authorization/tenant isolation | Token states and foreign ids (one per id family); Owner-only bank details; audit/log content; rate limits | AC-01, AC-15, AC-16, AC-17 |
| Persistence | Schema doc and model match SA-14–SA-18; `database-reviewer`; migration generated, not applied | AC-19 |
| Frontend component/service | Public page states and token handling; method panels; card flow (fake Stripe adapter): duplicate prevention, polling, failure and success; success view and review; settings section; hub adjustments | AC-18, AC-20, AC-21, AC-22, AC-23, AC-24 |
| User visual QA | Fidelity, responsive/mobile layouts, keyboard and focus | AC-25 |

Backend integration may exceed the 8-method default: webhook idempotency,
concurrency, rollback and refund reconciliation are separate financial
integrity risks. Frontend may reach 6 methods with the card flow tested
through a fake Stripe adapter; no real Stripe calls, Playwright or browser
automation.

## Dependencies

- `invoice-draft-delivery` (AUDITED): public token, page, PDF, logo, snapshot,
  rate limits; its BR-28 note "Online payment will be available soon." is
  removed by this feature.
- `invoices-payments-management` (AUDITED): payments, numbering, receipt email
  composer, hub (BR-31 changes).
- `company-settings-and-branches`: `/admin/company` page and Owner Manage
  permission (BR-32 section).
- `mobile-job-completion` / `mobile-job-progress`: completion summary,
  checklist, sign-off and evidence data.
- Stripe test account keys provided by the user through configuration; new
  dependencies `Stripe.net` (backend) and `@stripe/stripe-js` (frontend),
  approved with OD-01.
- Schema amendments SA-14–SA-18 (approved 2026-10-09).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Invoice currency equals the organization currency and is supported by Stripe (`invoices-payments-management` AS-01). |
| AS-02 | A work order has at most one non-void invoice (`completed-jobs-review`), so one review per work order equals one per invoice. |
| AS-03 | `stripe_account_id` is stored for future Connect use and is not read in this feature. |
| AS-04 | Stripe's default webhook timestamp tolerance (5 minutes) is acceptable. |
| AS-05 | Existing `payments` rows are external payments; they receive `succeeded`, 0 refunded and no receipt number. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Provider and account model | Stripe platform account / Connect | No | 2026-10-09: Stripe PaymentIntents + Payment Element, signed webhooks, gateway abstraction with fake; platform test account; nullable `stripe_account_id`; card disabled without keys |
| OD-02 | Amount | Full balance / partial | No | 2026-10-09: full outstanding balance only |
| OD-03 | Persistence of states | — | No | 2026-10-09: attempts table; payments and attempts support `succeeded`, `partially_refunded`, `refunded` and `refunded_amount`; `card_online`; nullable received-by |
| OD-04 | Refunds | Full only / partial and full | No | 2026-10-09: provider-initiated partial and full refunds reconciled by delta; paid invoices may reopen |
| OD-05 | Overpayment race | Block / auto-refund | No | 2026-10-09: block external payments with `409 payment_in_progress` while a card attempt is pending |
| OD-06 | Duplicates and expiry | — | No | 2026-10-09: one pending per invoice; polling 2 s up to 60 s; 30-minute expiry released only after confirmed cancellation |
| OD-07 | Bank transfer | — | No | 2026-10-09: encrypted details in Company settings (Owner), full number only on the authorized public link, masked internally; notice creates a non-blocking pending attempt and emails the organization |
| OD-08 | Cash | — | No | 2026-10-09: instructions only |
| OD-09 | Receipt | — | No | 2026-10-09: `RCT-<invoice>-<nn>` assigned under invoice lock, unique; PDF on demand; email after commit; reference shows `PAY-n` |
| OD-10 | Review | — | No | 2026-10-09: one per work order, paid invoices only, immutable; nullable technician from latest completed visit |
| OD-11 | Token and neutral state | — | No | 2026-10-09: existing 365-day token and revocation; fully generic neutral page without organization contact |
| OD-12 | Report and photos | — | No | 2026-10-09: completion report PDF on demand; before/after photos only |
| OD-13 | Rate limits | — | No | 2026-10-09: as BR-23; webhook signature-verified and deduplicated |
| OD-14 | Mockup deviations | — | No | 2026-10-09: as BR-33/BR-34 |
| OD-15 | Migration | — | No | 2026-10-09: schema doc first; SA-14–SA-18 in one migration with `--generate-migration`, never applied |
| OD-16 | Metrics with refunds | — | No | 2026-10-09: net amounts (`amount − refunded_amount`); audit never includes bank or provider data |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02 |
| FR-03 | AC-03 |
| FR-04 | AC-04, AC-06, AC-10, AC-11 |
| FR-05 | AC-05, AC-06, AC-11 |
| FR-06 | AC-07, AC-08 |
| FR-07 | AC-09 |
| FR-08 | AC-05, AC-07 |
| FR-09 | AC-12, AC-21 |
| FR-10 | AC-13 |
| FR-11 | AC-14, AC-23 |
| FR-12 | AC-15 |
| FR-13 | AC-17, AC-24 |
| FR-14 | AC-16 |
| FR-15 | AC-20, AC-21, AC-22, AC-23, AC-25 |
| FR-16 | AC-08, AC-18 |
| FR-17 | AC-19 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-09 | — → DRAFT     | Created from the invoice payment designs with decisions OD-01–OD-16 resolved by the user |
| 2026-10-09 | DRAFT → DRAFT | Revised after validation: Stripe return parameters stripped before any call (BR-29, AC-22); `card-intent` replay shape with nullable `clientSecret` (BR-06, contract); `takenOn` defined (BR-21); composite technician FK in `invoice_reviews` (SA-18) |
| 2026-10-09 | DRAFT → APPROVED | Approved by user via /spec approve |
