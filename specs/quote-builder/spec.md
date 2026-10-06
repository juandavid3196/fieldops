# Quote builder

| Field    | Value           |
| -------- | --------------- |
| Feature  | `quote-builder` |
| Type     | Full-stack      |
| Status   | AUDITED         |
| Created  | 2026-10-05      |
| Updated  | 2026-10-05      |
| Approved | 2026-10-05      |

## Context and objective

`requests-pipeline` brings a request to `ready_for_quote`, either directly or
by completing an assessment. Today **Create quote** leads to a Coming soon
page, and **Complete assessment** stores no diagnosis or evidence. This
feature does two things. First, it records the assessment findings: a
diagnosis, a recommended scope and photos. Second, it turns a
`ready_for_quote` request into an editable, explicitly saved draft quote. The
backend calculates every amount. The draft is sent by email as an immutable
`quote_versions` snapshot with a secure link prepared for Customer Quote
Approval. Later edits create a new version and never modify a sent one.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Complete assessments with findings. Create, edit, save, preview, send, revise and discard quotes, and resend the quote email, for requests in any branch | — |
| Dispatcher (`dispatcher`) | Same as Owner for requests in branch scope (`requests-pipeline` BR-02) | Requests or quotes outside scope (`404`) |
| Operations Manager, Accounting, Viewer | Read quotes and assessment findings of visible requests (`requests-pipeline` BR-01/BR-02), including internal notes and the estimated margin. Open `/quotes/:id` read-only and download assessment photos | Any mutation or the calculate endpoint (`403`). The editor shows the forbidden state |
| Technician (`technician`) and unknown roles | — | Everything in this feature (`403`; forbidden state) |
| Customer (anonymous) | Receive the quote email and open the link, which shows a placeholder in this feature (BR-27) | Read any quote data in this feature |

## Scope

- Assessment findings. **Complete assessment** in the request panel opens a
  dialog with diagnosis, recommended scope and up to 6 photos (BR-01 to
  BR-03). The request detail shows the completed assessment findings, and
  photos can be downloaded.
- Quote creation from a `ready_for_quote` request, with or without a completed
  assessment. The quote number comes from the organization sequence (BR-05,
  BR-06).
- Editor route `/quotes/:quoteId/edit` (Design 3) with customer, property,
  request and assessment summary cards (BR-08), line items (BR-09 to BR-12),
  summary with simple discount and internal estimated margin (BR-13 to
  BR-16), customer message, internal note, terms and expiration (BR-17 to
  BR-19), customer approval information (BR-20) and delivery (BR-21).
- Explicit **Save draft** with optimistic concurrency (BR-22).
- Customer preview (BR-23).
- Send by email. Sending freezes an immutable version with its lines, totals,
  currency, terms and expiration, moves the request to `quoted`, writes the
  audit row, creates the secure link token and sends the email after commit
  (BR-24 to BR-26).
- **Resend email** for the current sent version (BR-26).
- Revision. A new draft version is created from the last sent one, and the
  previous link stays valid until the revision is sent (BR-28).
- **Discard draft** (BR-29).
- Read-only quote view `/quotes/:quoteId` with sent versions (BR-30).
- Public placeholder route for the link (BR-27).
- Changes to existing behavior, which supersede the cited rules:
  - `requests-pipeline` BR-03 and BR-15: the **Complete assessment** dialog,
    **Create quote** / **Continue quote**, **View quote** on `quoted`
    requests, **Move back to review** blocked by a draft, and **Cancel
    request** cancelling a draft quote (BR-04, BR-31).
  - `customer-property-detail` BR-14: Recent work quote links target
    `/quotes/<id>` (BR-30).
- Schema amendments (BR-32).

## Non-goals

- Customer Quote Approval: the public quote page, approving or rejecting,
  selecting optional lines, clarification requests, `quote_responses` rows,
  and the `expired` transition. This spec only creates the token and the
  placeholder route (BR-27).
- Deposits. The design's **Require deposit** toggle is not rendered.
- Discount types other than one fixed amount per version, and per-line
  discounts.
- Configurable or organization-level terms, tax rates other than the
  organization default, and tax-inclusive pricing (AS-03).
- SMS delivery, an SMS provider, and `notifications` rows. **Mark as sent**
  without email, and a dropdown on **Send quote**.
- Work orders, invoices, payments and visits.
- A Quotes list page, the sidebar Quotes module and the customer Quotes tab.
  They stay Coming soon.
- Autosave.
- Editing a sent version or a completed assessment.
- Shell changes. The sidebar and top bar stay as implemented, even where the
  mockup shows older navigation.
- Automated browser, E2E or visual tests by agents.

## User flow

1. A manager presses **Complete assessment** on an `assessment_scheduled`
   request whose assessment has started. The manager enters a diagnosis, an
   optional recommended scope and photos, and confirms. The request moves to
   `ready_for_quote`. Alternatively, the request reaches `ready_for_quote`
   through **Mark ready for quote**, with no assessment.
2. In `ready_for_quote`, the manager presses **Create quote**. The system
   creates the draft (version 1) with the next quote number and opens
   `/quotes/<id>/edit`.
3. The manager adds service, material, custom and optional lines, reorders
   them, and sets the discount, customer message, internal note, terms and
   expiration. Each change is recalculated by the backend, and the line
   amounts, totals and margin update.
4. The manager presses **Save draft** and can leave and come back later with
   **Continue quote**.
5. The manager presses **Preview** to see the customer's view.
6. The manager edits the email message and presses **Send quote**, then
   confirms. The system freezes the version, moves the request to `quoted`,
   creates the link and emails the customer after commit. It then opens
   `/quotes/<id>` with a toast.
7. Later, the manager presses **Revise quote** on `/quotes/<id>`. Version n+1
   opens in the editor, prefilled from version n. The customer's link to
   version n stays valid until version n+1 is sent.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | **Complete assessment** must capture and persist the diagnosis, the recommended scope and 0–6 photos in the existing completion transaction. The request detail must return the latest completed assessment findings, and photos must be downloadable by readers (BR-01 to BR-03). |
| FR-02 | Managers must create a draft quote from a `ready_for_quote` request, with or without a completed assessment, idempotently. Creation consumes the organization quote number. The request panel must offer **Create quote**, **Continue quote** or **View quote** per BR-04 to BR-07. |
| FR-03 | The editor must show the header, the customer, property and request cards and the assessment summary per BR-08. |
| FR-04 | The editor must manage service, material, custom and optional lines from the catalog or manually, with editable snapshots and reordering per BR-09 to BR-12. |
| FR-05 | The backend must calculate line amounts, subtotal, discount allocation, tax, total and the internal estimated margin per BR-13 to BR-16. The frontend must display only backend-calculated values. |
| FR-06 | The editor must capture the customer message, internal note, terms and expiration per BR-17 to BR-19, and show the customer approval and delivery cards per BR-20 and BR-21. |
| FR-07 | **Save draft** must persist the whole draft with optimistic concurrency, and leaving with unsaved changes must ask first (BR-22). |
| FR-08 | **Preview** must show exactly the customer-visible content of the current form, calculated by the backend, and never internal data (BR-23). |
| FR-09 | **Send quote** must persist and freeze the version, transition the quote and the request, create the access token and write the audit rows in one transaction. It must then send the email after commit, and a failure must not undo the send (BR-24 to BR-26). |
| FR-10 | A sent version, its lines and totals must never change afterwards, whatever later changes happen to the catalog or the organization settings (BR-25). |
| FR-11 | **Resend email** must issue a new token for the current sent version, revoke its previous tokens and send the email (BR-26). |
| FR-12 | The email link must target the public route, which shows a placeholder and requests no data. Raw tokens must never be stored, logged or shown again (BR-27). |
| FR-13 | **Revise quote** must create the next draft version from the last sent one. Sending it must supersede the previous version and its tokens (BR-28). |
| FR-14 | **Discard draft** must cancel an unsent quote or delete a revision draft per BR-29, and request transitions must respect an existing draft quote (BR-31). |
| FR-15 | `/quotes/:quoteId` must show sent versions read-only, with the actions allowed to the caller's role (BR-30). |
| FR-16 | Every endpoint must resolve the organization from the session and apply the `requests-pipeline` role and branch scope. Client ids must be verified server-side. |
| FR-17 | Audit rows and logs must record quote and assessment operations without free texts, contact data or tokens (BR-33). |
| FR-18 | The schema amendments of BR-32 must be reflected in the schema document and the persistence model. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | **Complete assessment** dialog. Title "Complete assessment". Fields: **Diagnosis** \* (required, 1–2000 characters after trim → `errors.diagnosis` "Enter the diagnosis." / "Diagnosis must be 2000 characters or fewer."). **Recommended scope** (optional, ≤ 2000 → `errors.recommendedScope` "Recommended scope must be 2000 characters or fewer."; empty → null). **Photos** (optional, 0–6 → `errors.photos` "Add up to 6 photos."). Buttons **Complete assessment** / **Cancel**, plus the shared discard-changes dialog when the fields changed. The existing rules are unchanged: only in `assessment_scheduled`, and `409` "This assessment hasn't started yet." for a future start. In one transaction: diagnosis, `recommended_scope`, photos, `status = completed`, `completed_at`, and the request moves to `ready_for_quote`. Any failure persists nothing. Success shows the toast "Assessment completed." and refreshes the panel. | Both |
| BR-02 | Assessment photos. Multipart field `photos`. Each file is 1 byte–10 MB (10,485,760 bytes) and the total is ≤ 25 MB. The type is detected from the content signature (JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`) and must match the extension (`.jpg`/`.jpeg`, `.png`, case-insensitive), else `errors.photos` "Photos must be JPG or PNG files of 10 MB or less.". A body over 26 MB → `413`. Each photo is stored in `assessment_attachments` with `organization_id`, the detected `mime_type`, the sanitized file name (`public-service-request` rules), `size_bytes` and `content`. `storage_key` is null. | Both (detection Backend) |
| BR-03 | Completed assessment findings. Request detail adds `completedAssessment` = the request's latest `completed` assessment (by `completed_at`), or null: `{ id, start, completedAt, technician: { id, name } \| null, diagnosis \| null, recommendedScope \| null, photos: [{ id, fileName, mimeType, sizeBytes }] }`. Older completed assessments without findings return null fields and no photos. The request panel shows an "Assessment findings" section with the diagnosis, recommended scope and photo thumbnails, and each thumbnail opens the photo. Download headers follow `requests-pipeline` BR-16 (`inline`, `nosniff`, `private, no-store`). No public or unauthenticated URL exists. | Both |
| BR-04 | Request panel actions (Owner, Dispatcher). In `ready_for_quote`, the primary action is **Create quote** when the request has no non-cancelled quote, else **Continue quote**. Both navigate to `/quotes/<id>/edit`, after `POST /quotes` for **Create quote**. On `quoted` requests, every role allowed to read quotes sees **View quote**, which navigates to `/quotes/<id>`. The other `requests-pipeline` BR-03 actions are unchanged except as BR-31 states. | Frontend |
| BR-05 | Creation. `POST /quotes { requestId }`. The request must be visible to the caller and in `ready_for_quote`, else `409` `code` `request_changed` (`requests-pipeline` title). If the request already has a non-cancelled quote: when it is unsent (`status = draft`), `200` returns it unchanged and consumes no number; otherwise `409` `request_changed`. A new quote has `status = draft`, `current_version_no = 0`, `branch_id` = request branch, `customer_id` and `property_id` from the request, and `created_by_user_id` = caller. It gets a version 1 with `is_immutable = false`, `scope` = the request title (`requests-pipeline` BR-04) frozen at creation, `currency` = organization currency, terms preset `due_on_completion`, `valid_until` = organization today + 30 days, `discount_total = 0`, empty `customer_notes` and `internal_notes`, and no lines. Response `201`. Concurrent creates for the same request produce one quote: the loser receives the winner through the unique index (BR-32), as `200`. | Backend |
| BR-06 | Numbering. On creation, `quote_number` = `organizations.next_quote_number` read under a lock on the organization row, and `next_quote_number` is incremented in the same transaction (as request numbering). A number is never reused, even after a discard or cancellation. Display: `<quote_prefix>-<quote_number>` with the organization's current prefix, for example "Q-2036". The editor header shows it as "#Q-2036". | Backend |
| BR-07 | Visibility and scope. A quote is visible when its request is visible (`requests-pipeline` BR-01/BR-02). Quote reads (`GET /quotes/{id}`, versions, photos) allow the read roles. Every mutation and `calculate` allow `owner` and `dispatcher` only. A quote of another organization or outside scope → `404`, identical to a nonexistent id. | Backend |
| BR-08 | Editor header and cards. Title "Create quote" (version 1) or "Revise quote" (version > 1). The chip "Assessment completed" appears only when `completedAssessment` exists. Breadcrumb **Requests** › `<REQ-n>` (`/requests?request=<id>`) › **Quote**. Quote number. Save state "Draft saved" + relative time of `updatedAt` ("Just now" under 1 minute), or "Unsaved changes". **Preview**, and a more-actions menu with **Discard draft**. **Customer** card: the customer display name or `guest_name`, with initials; the phone and email of the linked active contact, else the guest phone and email; the property address, else the service address. **Edit** links to the customer detail when the request has a customer, and is hidden for guests. **Request** card: title (BR-05 scope), `<REQ-n>` and category, linking to `/requests?request=<id>`. **Assessment summary** card, from BR-03: technician name and initials, assessment start "MMM d, yyyy 'at' h:mm a", **Diagnosis**, **Recommended scope**, photo thumbnails (opening the photo) and **View full assessment** (`/requests?request=<id>`). Without a completed assessment: "No assessment. This quote is based on the request details." Empty findings show "No diagnosis recorded." | Both |
| BR-09 | Adding lines. **Add service** and **Add material** open a catalog picker over active catalog items of type `service` or `product`, using the existing `GET /catalog-items` search. It shows name, description and unit price, plus a **Custom line** option. **Add optional item** opens the same picker with a Service/Material type choice and adds the line with `isOptional = true`. A catalog line copies `catalogItemId`, type, name, description, unit, unit price, unit cost and taxable from the item. A custom line has a null `catalogItemId`, the button's type, an empty name and description, unit "unit", price 0, cost 0 and taxable on. Every snapshot field is editable on the quote and never changes the catalog. On save, a non-null `catalogItemId` must belong to the organization (active or not), else `400` `errors["lines[i].catalogItemId"]` "This catalog item isn't available.". Material lines map to `line_type = product`. | Both |
| BR-10 | Line fields (request keys and `400` messages under `errors["lines[i].<key>"]`). The table under this one lists the limits. Unknown keys and client-sent amounts are ignored, and `type` must be `service` or `product`. | Both |
| BR-11 | Line limits per version: 0–100 lines in total. Optional lines appear in their own "Optional items" section below the regular lines, labelled "Optional". Each section is ordered independently. `sort_order` is the 0-based position in the submitted array: regular lines first, then optional lines. Sending requires at least one non-optional line (BR-24). | Both |
| BR-12 | Reordering. A drag handle per line, and keyboard-accessible **Move up** / **Move down** actions in each line's menu. Moving a line never crosses between regular and optional sections. Reordering only marks the form as changed. Delete removes the line from the form with no confirm. | Frontend |
| BR-13 | Line calculation. Every rounding is to 2 decimals, half away from zero. `line_subtotal = round(quantity × unit_price)`. Tax rate: Taxable → `organizations.default_tax_rate`, read at each calculation for a draft and frozen at send. Non-taxable → 0. `line_tax = round((line_subtotal − discount_share) × tax_rate ÷ 100)`. `line_total = line_subtotal − discount_share + line_tax`. Optional lines have `discount_share = 0`. The displayed line **Amount** is `line_subtotal`. | Backend |
| BR-14 | Discount. One fixed amount per version, `discountTotal`: ≥ 0, at most 2 decimals and ≤ the subtotal of non-optional lines, else `400` `errors.discountTotal` "Discount can't exceed the subtotal." / "Enter a discount of 0 or more.". Allocation: for each non-optional line, `discount_share = floor_to_cent(D × line_subtotal ÷ S)`. The remaining cents go to the non-optional line with the largest `line_subtotal` (ties: the first in order). The sum of shares equals D exactly. With S = 0, D must be 0. The summary shows "Add discount", which reveals an amount input, and then "Discount −<amount>". | Both |
| BR-15 | Version totals. Optional lines are always excluded. `subtotal` = Σ `line_subtotal`. `discount_total` = D. `tax_total` = Σ `line_tax`. `total` = `subtotal − discount_total + tax_total`. Tax label: "Tax (<rate>%)" when every taxable non-optional line has the same rate, the rate formatted with up to 4 decimals and no trailing zeros; "Tax" otherwise. With no taxable lines it reads "Tax" with 0. A total above 999,999,999,999.99 → `400` `errors.lines` "This quote total is too large.". Money uses the version currency with 2 decimals. The selection of optional lines by the customer is added to totals only in Customer Quote Approval. | Backend |
| BR-16 | Estimated margin (internal). Revenue R = `subtotal − discount_total`. Cost C = Σ `round(quantity × unit_cost)` over non-optional lines. Margin = (R − C) ÷ R × 100, rounded half away from zero to 1 decimal, with gross profit R − C. With R = 0 → "—". It is shown only in the internal summary as "Estimated margin <n>% · <gross profit>", marked "Internal". It is derived on every read from frozen prices, costs and discount, and never stored as text. It never appears in the preview, the email or any customer-facing payload. | Both |
| BR-17 | Customer message (`customerMessage`): optional, ≤ 500 characters after trim, empty → null, stored in `quote_versions.customer_notes`, counter "<n>/500" → `errors.customerMessage` "Customer message must be 500 characters or fewer.". Internal note (`internalNote`): optional, ≤ 500, empty → null, stored in `internal_notes`, labelled "(Not visible to customer)" → `errors.internalNote` "Internal note must be 500 characters or fewer.". The internal note is visible to internal quote readers. It never appears in the preview, email, public link, audit or logs. | Both |
| BR-18 | Terms. `terms: { preset, customText? }` with these presets and frozen texts: `due_on_completion` "Payment due upon completion." (default); `net_15` "Payment due within 15 days of the invoice date."; `net_30` "Payment due within 30 days of the invoice date."; `custom` with `customText` 1–2000 characters after trim (`errors["terms.customText"]` "Enter the terms." / "Terms must be 2000 characters or fewer."). The select shows "Payment due upon completion", "Net 15", "Net 30" and "Custom". The resulting text is stored in `quote_versions.terms`. When the editor loads, a stored text equal to a preset text selects that preset; anything else selects Custom with that text. | Both |
| BR-19 | Expiration. `validUntil` (`YYYY-MM-DD`) from organization tomorrow to today + 365 days, checked on save and on send → `errors.validUntil` "Choose a date between tomorrow and one year from today.". Default: today + 30 (BR-05). A revision copies the previous date when it is still valid, else the default. Stored in `valid_until`. | Both |
| BR-20 | Customer approval card (information only, not persisted): "Customer approval", "The customer approves online through a secure link." and the note "A work order can be created after customer approval.". The mockup's toggle and checkboxes are not rendered. | Frontend |
| BR-21 | Delivery card. The **Email** checkbox is checked and disabled: email is the only channel. The **SMS** checkbox is unchecked and disabled with "SMS isn't available yet.". **Send to**: the recipient (`requests-pipeline` BR-13: linked active contact email, else `guest_email`), read-only. **Message** (`emailMessage`): 1–320 characters after trim, counter "<n>/320", default "Hi <contact first name or guest first name, or 'there'>, here is your quote for <request title>. Please review it and let us know if you have any questions." (truncated to 320). It is only sent, never stored → `errors.emailMessage` "Enter a message." / "Message must be 320 characters or fewer.". With no recipient, **Send quote** is disabled with "This customer has no email address.", and a send returns `400` `errors.recipient` with the same text. | Both |
| BR-22 | Saving. **Save draft** sends `PUT /quotes/{id}/draft` with the full draft (lines, `discountTotal`, `customerMessage`, `internalNote`, `terms`, `validUntil`) and `updatedAt`. The draft is replaced as a whole: lines are deleted and inserted. `quotes.updated_at` is the concurrency token; every mutation requires `updatedAt` and sets a new value. A mismatch, or a lost race at `SaveChanges`, → `409` `code` `quote_changed` "This quote changed. Refresh to see the latest." and nothing changes. Of a concurrent save and send, exactly one succeeds. Draft saving is allowed with 0 lines and with optional-only lines. Leaving the editor with unsaved changes (breadcrumb, links or navigation) shows the shared discard-changes dialog. **Save draft** is disabled while in flight or when nothing changed. Success shows the toast "Draft saved.". | Both |
| BR-23 | Calculation and preview. `POST /quotes/{id}/calculate` takes the draft body (no `updatedAt`), validates it like a save, persists nothing and returns per-line amounts, totals, tax label and margin. The editor calls it after each valid change, debounced. Amounts show a loading state, and a line with invalid input shows "—" without a call. **Preview** opens a dialog "Preview" with the customer view from the latest calculation of the current form: organization name, quote number, version, today's date, customer name and address, request title (scope), customer message, regular lines (name, description, quantity with unit, unit price, amount), "Optional items" with their amounts and the note "Not included in the total", subtotal, discount (when > 0), tax label and amount, total, terms and "Valid until <MMM d, yyyy>". It never shows the internal note, unit cost, margin, diagnosis or photos. The same content defines what Customer Quote Approval must render for a sent version. | Both |
| BR-24 | Send. **Send quote** opens the confirm dialog "Send quote?" with "<customer name> will receive version <n> of <Q-n> at <recipient>. A sent version can't be edited; later changes create a new version." and the buttons **Send quote** / **Cancel**. `POST /quotes/{id}/send` carries the draft body, `updatedAt` and `emailMessage`. It validates BR-10 to BR-21 and requires ≥ 1 non-optional line (`400` `errors.lines` "Add at least one line that isn't optional."). In one transaction it: saves the draft; recalculates and freezes rates and totals; sets the version `is_immutable = true`, `sent_at = now`, `currency` = organization currency; sets `current_version_no` = this `version_no` and `quotes.status = sent`; revokes every unrevoked token of earlier versions; creates the token (BR-27); on version 1 moves the request `ready_for_quote → quoted` with one status history row; and writes the audit rows (BR-33). | Both |
| BR-25 | Immutability. A version with `is_immutable = true` and its lines are never updated or deleted by any operation. `PUT /quotes/{id}/draft`, `calculate` and `send` act only on the mutable version. When none exists → `409` `code` `no_draft` "This quote has no draft. Revise it to make changes.". Later changes to catalog items or to the organization's tax rate, currency or other settings do not alter a sent version's texts, prices, costs, rates, currency, terms, expiration or totals. The display prefix follows BR-06. | Backend |
| BR-26 | Email after commit, through `IEmailSender`. Subject: "<organization name>: quote <Q-n> for <request title>". Text and HTML bodies: the message, "Quote <Q-n> · Total <total> · Valid until <MMM d, yyyy>", the link "Review your quote: <link>", the organization name and, when set, "Questions? Call us at <organization phone>.". The response carries `emailStatus` `sent` or `failed`. On failure, only the quote id and failure category are logged, the response is still `200`, and the send is never rolled back. The UI shows the toast "Quote sent, but we couldn't email it. Use Resend email to try again." (warning). **Resend email** (`POST /quotes/{id}/resend-email { updatedAt, emailMessage }`) is available on `/quotes/<id>` when the quote is `sent` and has a recipient. In one transaction, it revokes the current version's unrevoked tokens, creates a new token, writes `quote.email_resent` and updates `quotes.updated_at`. It then sends the same email. | Both |
| BR-27 | Token and public link. 32 random bytes from a cryptographic RNG, base64url without padding. Only the SHA-256 hex hash is stored, in `quote_access_tokens.token_hash`. `expires_at` = end of the `valid_until` day in the organization time zone. Link: `<first allowed frontend origin>/quote-approval#token=<raw>` (invitation link precedent: the token stays in the URL fragment). The raw token exists only in the email, never in responses, logs, audit or the UI. Route `/quote-approval` is public, outside the shell. It shows "This quote will be available soon." with the organization-neutral FieldOps layout and makes no API call. It is a temporary placeholder to be replaced by Customer Quote Approval. | Both |
| BR-28 | Revision. **Revise quote** (`POST /quotes/{id}/revise { updatedAt }`) is allowed when `status` ∈ `sent`, `clarification_requested`, `rejected`, `expired` and no mutable version exists, else `409` `quote_changed`. It creates version `current_version_no + 1` with `is_immutable = false`. Lines, discount, customer message, internal note, terms and scope are copied from the last sent version, `valid_until` follows BR-19, and the rates are recalculated from the organization. It navigates to the editor. The quote status and the request status are unchanged, and the previous version and its tokens stay valid until the revision is sent (BR-24). | Both |
| BR-29 | Discard draft. More-actions **Discard draft** opens the confirm dialog "Discard draft?". Version 1 (never sent): "The quote <Q-n> will be cancelled. Its number won't be reused." The quote becomes `cancelled` and the draft version and lines remain. The request stays `ready_for_quote`, and **Create quote** creates a new quote with a new number. Revision: "Your changes to version <n> will be deleted. Version <n−1> stays with the customer." The mutable version and its lines are deleted, and the quote is unchanged otherwise. Body `{ updatedAt }`. Success navigates to `/requests?request=<id>` (version 1) or `/quotes/<id>` (revision), with the toast "Draft discarded.". | Both |
| BR-30 | Read-only view `/quotes/:quoteId`. Header: quote number, quote status chip, request link and customer. A version selector lists sent versions as "Version <n> · Sent <MMM d, yyyy>", with the current one marked "Current" and older ones "Superseded". It renders the selected frozen version with the BR-23 customer layout plus internal data (internal note, estimated margin). Manager actions: **Revise quote** (BR-28), or **Continue editing** with the banner "Version <n> is being revised." when a mutable version exists; **Resend email** (BR-26). Read roles see no actions. A `draft` quote never sent redirects managers to the editor and shows read roles "This quote hasn't been sent yet." with the draft's internal summary. Opening `/quotes/<id>/edit` with no mutable version redirects to `/quotes/<id>`. Customer Recent work quote ID links and **View** target `/quotes/<id>`. | Both |
| BR-31 | Request transitions with quotes. **Move back to review** on a `ready_for_quote` request with a `draft` quote → `409` `code` `quote_draft_exists` "This request has a draft quote. Discard it first.". **Cancel request** on a request with a `draft` quote also cancels that quote in the same transaction and writes `quote.cancelled`. `quoted` requests accept no `requests-pipeline` mutations (existing rule). | Backend |
| BR-32 | Schema amendments, confirmed by the user 2026-10-05 (OD-18). (1) `quote_lines`: add `organization_id uuid NOT NULL REFERENCES organizations(id)`, `FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions(organization_id, id) ON DELETE CASCADE`, `name varchar(160) NOT NULL`, `is_optional boolean NOT NULL DEFAULT false`. (2) `quote_versions`: add `discount_total numeric(14,2) NOT NULL DEFAULT 0 CHECK (discount_total >= 0)` and `terms text`. (3) New `quote_access_tokens (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), organization_id uuid NOT NULL REFERENCES organizations(id), quote_version_id uuid NOT NULL, token_hash text NOT NULL UNIQUE, expires_at timestamptz NOT NULL, revoked_at timestamptz, created_by_user_id uuid NOT NULL REFERENCES users(id), created_at timestamptz NOT NULL DEFAULT now(), FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions(organization_id, id), CONSTRAINT ck_quote_access_tokens_expires_after_created CHECK (expires_at > created_at))` with index `ix_quote_access_tokens_version (quote_version_id) WHERE revoked_at IS NULL`. (4) `assessment_attachments`: add `organization_id uuid NOT NULL REFERENCES organizations(id)` (backfilled from `assessments.organization_id` before `NOT NULL`), `FOREIGN KEY (organization_id, assessment_id) REFERENCES assessments(organization_id, id) ON DELETE CASCADE`, `content bytea`; `storage_key` becomes nullable; `CHECK (content IS NOT NULL OR storage_key IS NOT NULL)`, `CHECK (mime_type IN ('image/jpeg','image/png'))`, `CHECK (size_bytes > 0 AND size_bytes <= 10485760)`. (5) `CREATE UNIQUE INDEX ux_quotes_request_open ON quotes(organization_id, request_id) WHERE status <> 'cancelled'`. (6) `CREATE UNIQUE INDEX ux_quote_versions_one_mutable ON quote_versions(quote_id) WHERE NOT is_immutable`. No other table changes. | Backend |
| BR-33 | Audit. `entity_type` `quote`, `entity_id` quote id, `actor_user_id` caller, `branch_id` quote branch. Actions: `quote.created` (after `{ quoteNumber, status, versionNo }`); `quote.draft_saved` (metadata `{ versionNo, lineCount }`); `quote.sent` (before/after `{ status }`, metadata `{ versionNo, total, currency, notified: "email" }`); `quote.revised` (metadata `{ versionNo }`); `quote.draft_discarded` (metadata `{ versionNo }`, revision); `quote.cancelled` (before/after `{ status }`, metadata `{ reason: "discarded" \| "request_cancelled" }`); `quote.email_resent` (metadata `{ versionNo }`). The request writes `service_request.quoted` (before/after `{ status }`, metadata `{ quoteId }`) on the first send. `service_request.assessment_completed` adds metadata `{ photoCount }`. Audit rows and logs never contain line names or descriptions, messages, notes, terms, diagnosis, scope, names, emails, phones, addresses, file names or tokens. | Backend |

### Line field limits (BR-10)

| Key | Rule | Message |
| --- | ---- | ------- |
| `type` | `service` \| `product` | "Choose a type." |
| `catalogItemId` | Null or an item of the organization (BR-09) | "This catalog item isn't available." |
| `name` | Required, 1–160 after trim and whitespace collapse | "Enter an item name." / "Item name must be 160 characters or fewer." |
| `description` | Optional, ≤ 1000; empty stored as `''` | "Description must be 1,000 characters or fewer." |
| `quantity` | > 0, ≤ 999,999.999, ≤ 3 decimals | "Enter a quantity greater than 0." |
| `unit` | 1–40 after trim; default "unit" | "Enter a unit." |
| `unitPrice` | ≥ 0, ≤ 999,999,999.99, ≤ 2 decimals | "Enter a price of 0 or more." |
| `unitCost` | ≥ 0, ≤ 999,999,999.99, ≤ 2 decimals; internal field, never customer-facing | "Enter a cost of 0 or more." |
| `taxable` | Boolean, required | "Choose a tax option." |
| `isOptional` | Boolean, required | — |

The quantity cell shows the unit when it isn't "unit" (for example "2 hr").
The Tax cell is a select "Taxable" / "Non-taxable". Unit cost is edited in an
internal per-line field that the preview never shows.

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Assessment `scheduled` | `completed` | Complete assessment | Owner, Dispatcher | Request `assessment_scheduled`, start ≤ now, BR-01/BR-02 valid; request → `ready_for_quote` |
| — | Quote `draft` (v1 mutable) | Create quote | Owner, Dispatcher | Request `ready_for_quote`, no non-cancelled quote |
| Quote `draft` | `sent` | Send | Owner, Dispatcher | BR-24; request `ready_for_quote → quoted` |
| Quote `draft` | `cancelled` | Discard draft (v1); Cancel request | Owner, Dispatcher | `updatedAt` matches (discard) |
| Quote `sent`, `clarification_requested`, `rejected`, `expired` | Same status + mutable version n+1 | Revise quote | Owner, Dispatcher | No mutable version |
| Quote with mutable version n+1 | `sent` (current version n+1) | Send | Owner, Dispatcher | BR-24; tokens of earlier versions revoked; request stays `quoted` |
| Quote with mutable version n+1 | Same status, version deleted | Discard draft | Owner, Dispatcher | `updatedAt` matches |
| Request `ready_for_quote` | `needs_review` | Move back to review | Owner, Dispatcher | No `draft` quote (BR-31) |

`approved` and the transitions into `clarification_requested`, `rejected` and
`expired` belong to Customer Quote Approval.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `assessments`: update `diagnosis`, `recommended_scope`, `status`,
    `completed_at`, `updated_at`.
  - `assessment_attachments`: insert and read (amended).
  - `quotes`: insert; update `status`, `current_version_no`, `updated_at`
    (concurrency token). `approved_version_id` is unused.
  - `quote_versions`: insert; update while mutable; delete a mutable revision.
    Columns: `version_no`, `scope`, `customer_notes`, `internal_notes`,
    `subtotal`, `discount_total` (new), `tax_total`, `total`, `currency`,
    `terms` (new), `valid_until`, `sent_at`, `is_immutable`,
    `created_by_user_id`.
  - `quote_lines`: insert and delete within the mutable version. Columns:
    `organization_id` (new), `catalog_item_id`, `line_type`, `name` (new),
    `description`, `quantity`, `unit`, `unit_cost`, `unit_price`, `tax_rate`,
    `line_subtotal`, `line_tax`, `line_total`, `sort_order`, `is_optional`
    (new).
  - `quote_access_tokens` (new): insert; set `revoked_at`.
  - `organizations`: read `name`, `phone`, `timezone`, `currency`,
    `default_tax_rate`, `quote_prefix`. Lock and update `next_quote_number`.
  - `service_requests`, `request_status_history`, `audit_logs`: as in
    `requests-pipeline`, plus the BR-31/BR-33 changes.
  - Read only: `catalog_items`, `customers`, `customer_contacts`,
    `properties`, `service_categories`, `technician_profiles`.
  - `quote_responses` and `notifications` are not used.
- Schema amendments required: BR-32, **confirmed by the user 2026-10-05**.
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model,
  and generates one migration under `--generate-migration`. Agents never apply
  it. The mapping change requires `database-reviewer`.
- `quote_lines.catalog_item_id` keeps its single-column FK. Organization
  ownership is validated in the application layer (BR-09).
- `organizations.prices_include_tax` is not read (AS-03).

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  body or query carries an organization id. Any such field is ignored.
- Client identifiers, verified against the session organization and the
  caller's branch scope:
  - `requestId` on create.
  - Quote id, version number and photo/attachment id in paths.
  - `catalogItemId` per line.
- A request, quote, version or photo of another organization or outside
  branch scope → `404`, identical to a nonexistent id, on every endpoint.
- A foreign `catalogItemId` → `400` on that line, with no data of the foreign
  item returned.
- Permissions: reads use the `requests-pipeline` read policy; `calculate` and
  mutations use the Manage policy (Owner, Dispatcher). Others → `403`.
  Responses use `Cache-Control: no-store`.
- Public link: holds no organization or quote id. The placeholder makes no
  API call. Tokens are stored hashed and scoped to one version
  (`quote_access_tokens.organization_id` + composite FK).
- Concurrency: the organization row lock for numbering (BR-06); the
  `quotes.updated_at` token (BR-22); the unique indexes of BR-32 for one open
  quote per request and one mutable version per quote; and the existing
  per-request serialization of `requests-pipeline` for request transitions.

## API contracts

Contract status: Final. Errors use ProblemDetails, and every `409` carries
`code`. Money is a decimal JSON number with 2 decimals; quantities have up to
3. Dates are `YYYY-MM-DD`, and instants are ISO with offset.

`DraftBody` = `{ lines: [{ catalogItemId?, type, name, description, quantity,
unit, unitPrice, unitCost, taxable, isOptional }], discountTotal,
customerMessage?, internalNote?, terms: { preset, customText? }, validUntil }`.

`Calculation` = `{ lines: [{ lineSubtotal, discountShare, taxRate, lineTax,
lineTotal }], subtotal, discountTotal, taxLabel, taxTotal, total, currency,
margin: { percent \| null, grossProfit } }`. The lines come back in request
order.

`QuoteDetail` = `{ id, number, displayNumber, status, updatedAt, canManage,
request: { id, displayNumber, title, category \| null, status }, customer: {
id \| null, name, phone \| null, email \| null, address \| null }, recipient: {
email \| null }, completedAssessment (BR-03) \| null, organization: { name,
currency, defaultTaxRate }, draft: { versionNo, ...DraftBody, calculation }
\| null, sentVersions: [{ versionNo, sentAt, total, isCurrent }] }`.

`Version` = `{ versionNo, sentAt, scope, customerMessage, internalNote, terms,
validUntil, currency, lines: [{ type, name, description, quantity, unit,
unitPrice, unitCost, taxRate, lineSubtotal, lineTax, lineTotal, isOptional }],
subtotal, discountTotal, taxLabel, taxTotal, total, margin }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/service-requests/{id}/assessment/complete` | `multipart/form-data`: `diagnosis`, `recommendedScope?`, `photos` (0–6) | `200 RequestDetail` (with `completedAssessment`) | `400` (`diagnosis`, `recommendedScope`, `photos`) · `403` · `404` · `409` (not started / `request_changed`) · `413` · `415` | Manage |
| GET | `/service-requests/{id}/assessments/{assessmentId}/photos/{photoId}` | — | `200` binary (BR-03 headers) | `403` · `404` | Read |
| GET | `/service-requests/{id}` | — | `200 RequestDetail` + `completedAssessment` + `quote: { id, status, hasDraft } \| null` | Unchanged | Read |
| POST | `/quotes` | `{ requestId }` | `201 QuoteDetail` · `200 QuoteDetail` (existing draft) | `400` (`requestId`) · `403` · `404` · `409` `request_changed` | Manage |
| GET | `/quotes/{id}` | — | `200 QuoteDetail` | `403` · `404` | Read |
| GET | `/quotes/{id}/versions/{versionNo}` | — | `200 Version` (sent versions only) | `403` · `404` | Read |
| POST | `/quotes/{id}/calculate` | `DraftBody` | `200 Calculation` | `400` (field keys) · `403` · `404` · `409` `no_draft` | Manage |
| PUT | `/quotes/{id}/draft` | `DraftBody` + `updatedAt` | `200 QuoteDetail` | `400` · `403` · `404` · `409` (`quote_changed`, `no_draft`) | Manage |
| POST | `/quotes/{id}/send` | `DraftBody` + `updatedAt` + `emailMessage` | `200 { quote: QuoteDetail, emailStatus }` | `400` (fields, `lines`, `emailMessage`, `recipient`) · `403` · `404` · `409` (`quote_changed`, `no_draft`) | Manage |
| POST | `/quotes/{id}/revise` | `{ updatedAt }` | `200 QuoteDetail` | `403` · `404` · `409` `quote_changed` | Manage |
| POST | `/quotes/{id}/discard-draft` | `{ updatedAt }` | `200 { quoteId, status, requestId }` | `403` · `404` · `409` (`quote_changed`, `no_draft`) | Manage |
| POST | `/quotes/{id}/resend-email` | `{ updatedAt, emailMessage }` | `200 { quote: QuoteDetail, emailStatus }` | `400` (`emailMessage`, `recipient`) · `403` · `404` · `409` `quote_changed` (not `sent`) | Manage |
| POST | `/service-requests/{id}/move-to-review` | Unchanged | Unchanged | Adds `409` `quote_draft_exists` | Manage |

- Internal contracts: the existing `IEmailSender`, unchanged. A quote email
  composer follows the `RequestInformationEmailComposer` precedent. A quote
  link builder follows `CorsOriginInvitationLinkBuilder`. The catalog picker
  reuses `GET /catalog-items` unchanged (`products-services` BR-03).
- `margin`, `unitCost` and `internalNote` appear only in these authenticated
  internal responses.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Complete assessment dialog | Submit shows progress and is disabled | No photos: "Add photos (optional)" drop zone | `400`: inline field errors. `409`: toast with its title, then the dialog closes and the panel refreshes. Other: "We couldn't complete this assessment. Please try again." with data kept | Managers only | BR-01 toast and panel findings |
| Editor `/quotes/:id/edit` | Skeletons for header, cards, lines and summary | No lines: "Add a service, material or optional item to build this quote." Summary shows zeros. No assessment: BR-08 text | Load failure: existing error pattern with **Retry**. `404`: "This quote isn't available." with **Back to requests** | Read roles and Technician: forbidden state "You don't have access to edit quotes." with no further calls | Cards, lines and calculated summary |
| Line amounts and summary | Values keep with a loading indicator during calculation | — | Calculate failure: "We couldn't update the totals." with **Retry**. Send and Save stay enabled; the backend recalculates | — | Backend values (BR-13 to BR-16) |
| Catalog picker | List skeleton | "No items found." plus **Custom line** | "We couldn't load the catalog." with **Retry** and **Custom line** | — | The line is added and focused |
| Save / Send / Discard / Revise / Resend | Button progress, disabled, one request | — | `400`: inline errors, focus on the first. `409` `quote_changed`: toast with its title and a **Refresh** action that reloads (with the discard prompt for unsaved changes). `409` `no_draft`: redirect to `/quotes/<id>`. Other: "We couldn't save this quote. Please try again." with data kept | — | BR-22, BR-24, BR-26, BR-28, BR-29 toasts and navigation; send success toast "Quote sent." |
| Preview dialog | Content skeleton until the calculation is ready | — | Calculation error: "We couldn't build the preview." with **Retry** | — | BR-23 content |
| Read-only `/quotes/:id` | Skeleton | BR-30 unsent text | As editor | Read roles: no actions | BR-30 |
| `/quote-approval` | — | — | — | Public | BR-27 text |

- Mockup: `design/assets/3-design.png` (Approved).
  - The main area must match it: the header with title, chip, breadcrumb,
    number, save state, **Preview** and the more-actions menu; the left column
    with the Customer, Request and Assessment summary cards; the center Line
    items card with columns Item, Description, Qty, Rate, Tax, Amount, a drag
    handle and delete per line, the three add buttons, Customer message,
    Internal note, Terms and Quote expiration; the right column with Quote
    summary, Customer approval and Delivery cards, and **Save draft** /
    **Send quote**.
  - Deviations by decision:
    - The shell stays as implemented.
    - **Require deposit** is not rendered (OD-06).
    - The Customer approval card is informational (OD-14).
    - SMS is disabled, and **Send quote** has no dropdown (OD-15).
    - The internal margin row and the Optional items section are added
      (OD-03, OD-05).
    - Tax amounts follow per-line rounding, so the mockup's sample "$22.28"
      is $22.29 with the same lines (AC-11).
    - Sample data is replaced by real data.
- Responsive:
  - Wide desktop: three columns, as in the mockup.
  - Tablet and small desktop: the left column cards move above the line items
    in a two-column grid, and the summary column stays at the right.
  - Mobile: single column in this order: header, customer, request,
    assessment, line items, texts, summary, customer approval, delivery and
    actions. Each line renders as a stacked card (item, description, qty,
    rate, tax, amount, actions). There is no horizontal page scroll.
- Accessibility:
  - Every line input has an accessible name including the line position.
  - Reordering is possible by keyboard (BR-12), and moves are announced.
  - The totals region is a polite live region.
  - Disabled SMS and Send controls expose their helper text.
  - Margin and the internal note are labelled "Internal".
  - Focus moves to the heading on load, and to the first invalid field after
    a `400`.
  - Status chips are never conveyed by color alone.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Quote, request, version or photo of another organization or out of scope | `404`; "This quote isn't available." / "This request isn't available." | None |
| Read role or Technician calls a mutation or `calculate` | `403`; forbidden state | None |
| Create on a request not in `ready_for_quote`, or with a sent quote | `409` `request_changed` | None |
| Invalid line, discount, texts, terms, expiration or email message | `400` on the field | None |
| Send without non-optional lines or without a recipient | `400` `errors.lines` / `errors.recipient` | None |
| Stale `updatedAt` or a lost concurrent mutation | `409` `quote_changed` | None |
| Edit, calculate or send with no mutable version | `409` `no_draft` | None |
| Move back to review with a draft quote | `409` `quote_draft_exists` | None |
| Invalid or oversized photos | `400` `errors.photos` / `413` | None |
| Email failure after send or resend | `200` with `emailStatus = failed`; warning toast; log has quote id and failure category only | Never rolled back |
| Concurrent creates for the same request | One quote returned to both | One quote, one number |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An `assessment_scheduled` request whose assessment has started | A Dispatcher completes it with a diagnosis, a recommended scope and 2 valid photos (one JPG, one PNG) | In one transaction the assessment is `completed` with both texts and 2 `assessment_attachments` rows (organization, detected type, size, content). The request is `ready_for_quote` with one `service_request.assessment_completed` audit row whose metadata has `photoCount = 2`. The panel shows the findings, and each photo downloads with the BR-03 headers |
| AC-02 | The same request | Completion is submitted with: no diagnosis; a 2001-character diagnosis or scope; 7 photos; a PDF; a `.png` with JPEG content; an 11 MB photo; 26 MB in total; and, separately, for an assessment starting in the future | Each returns `400` on its key (or `413` for the oversized body). The future one returns the existing `409`. Nothing is persisted |
| AC-03 | A `ready_for_quote` request with a completed assessment, and an organization with `quote_prefix` "Q" and `next_quote_number` 2036 | An Owner presses **Create quote**, then the panel is reopened and `POST /quotes` is repeated | One quote `Q-2036` (`draft`, `current_version_no = 0`) with a mutable version 1 is created per BR-05; `next_quote_number` becomes 2037. The editor opens showing "#Q-2036", the "Assessment completed" chip and the BR-08 cards with diagnosis, scope and photos. The panel now reads **Continue quote**. The repeated call returns the same quote with `200` and consumes no number |
| AC-04 | A request marked ready for quote directly (no assessment), plus requests in `new`, `assessment_scheduled`, `quoted` and `cancelled` | A quote is created for each | The first succeeds, with no chip and the BR-08 "No assessment" text. The others return `409` `request_changed` with nothing created |
| AC-05 | Two concurrent creates for the same request, and two concurrent creates for different requests | They run | The first pair yields exactly one quote and one consumed number, both responses carrying it. The second pair gets two distinct consecutive numbers. After a version-1 discard, the next quote gets a new number and the discarded one is never reused |
| AC-06 | The editor | The manager adds a catalog service, a catalog material, a custom line and an optional item, edits the catalog line's name, price and cost, and saves | The lines persist with the copied snapshots and edits, `line_type` `service`/`product`, `is_optional`, `name` and `organization_id`. The catalog item is unchanged. A foreign `catalogItemId` returns `400` on that line; an inactive item of the organization is accepted |
| AC-07 | A draft with 3 regular and 2 optional lines | Lines are reordered by drag and by the **Move up** / **Move down** actions, then saved and reloaded | The order persists via `sort_order`, each section is ordered independently, a line never crosses sections, and moves are announced |
| AC-08 | The editor | A save is submitted with each BR-10 invalid value, 101 lines, invalid terms, an out-of-range `validUntil`, a 501-character message or note, and amount fields sent by the client | Each invalid value returns `400` on its exact key and nothing changes. Client-sent amounts are ignored, and stored amounts are the backend's |
| AC-09 | Lines: 2 × 95.00 taxable, 1 × 38.00 taxable, 1 × 42.00 taxable, 1 × 65.00 non-taxable; organization rate 8.25 % | The draft is calculated and saved | Line amounts 190.00, 38.00, 42.00, 65.00; line taxes 15.68, 3.14, 3.47, 0.00; subtotal 335.00; tax label "Tax (8.25%)"; tax 22.29; total 357.29. Stored `tax_rate` values are 8.25 and 0 |
| AC-10 | Lines with subtotals 100.00, 50.00 and 33.33 (all taxable, 10 %) | Discounts of 10.00, 0, 183.33 and 183.34 are calculated | For 10.00 the floored shares are 5.45, 2.72 and 1.81, and the 2 remaining cents go to the 100.00 line (5.47, 2.72, 1.81, totalling exactly 10.00); tax is computed per line on the discounted base; and total = subtotal − discount + tax. 0 and 183.33 are accepted (183.33 gives tax 0). 183.34 returns `400` `errors.discountTotal` |
| AC-11 | The AC-09 lines plus an optional 120.00 taxable line | The draft is calculated, and a send is attempted on a draft whose only line is optional | The optional line shows amount 120.00 in "Optional items" with its own tax, and subtotal, tax and total are unchanged from AC-09. The optional-only send returns `400` `errors.lines` "Add at least one line that isn't optional." with nothing changed |
| AC-12 | The AC-10 lines with costs 40.00, 20.00 and 0, a 10.00 discount and an optional line with cost | Margin is calculated; then all prices are set to 0 | Margin = (173.33 − 60.00) ÷ 173.33 × 100 = 65.4 % with gross profit 113.33, excluding the optional line. With R = 0 it shows "—". Margin is labelled "Internal" and is absent from the preview, the email and every customer-facing field |
| AC-13 | A draft open in the editor | The form is edited without saving | The amounts and summary shown are exactly the `calculate` response values; `calculate` persists nothing (`updatedAt` and stored rows unchanged); an invalid line shows "—" and triggers no call |
| AC-14 | A saved draft | **Save draft** is pressed after edits, the page is reloaded, then a save with the old `updatedAt` is sent; separately, a save and a send run concurrently with the same `updatedAt` | The first save persists the full draft, writes `quote.draft_saved`, and shows "Draft saved" with "Just now"; the reload shows the same data. The stale save returns `409` `quote_changed` with nothing changed. Of the concurrent pair exactly one succeeds and the other returns `409` |
| AC-15 | Unsaved changes in the editor | The manager clicks the breadcrumb, then confirms and dismisses the discard dialog | The shared discard-changes dialog appears; confirm leaves with nothing saved; dismiss keeps the form. With no changes, leaving shows no dialog |
| AC-16 | A draft with a customer message, an internal note, regular and optional lines, a discount, Net 30 terms and an expiration | **Preview** is opened before saving | The dialog shows the BR-23 content from the current form's calculation, including the optional items note, discount, tax label, terms text and "Valid until" date. It shows no internal note, unit cost, margin, diagnosis or photos |
| AC-17 | A valid version-1 draft and a recipient email | **Send quote** is confirmed | In one transaction: version 1 is immutable with `sent_at`, frozen lines, rates, `discount_total`, totals, currency, terms text and `valid_until`; the quote is `sent` with `current_version_no = 1`; the request is `quoted` with one history row; one unrevoked `quote_access_tokens` row stores a SHA-256 hash with `expires_at` = end of `valid_until` (organization time zone); `quote.sent` and `service_request.quoted` audit rows are written. After commit, one email per BR-26 with the `/quote-approval#token=` link is sent, and the response has `emailStatus = sent`. The UI opens `/quotes/<id>` with "Quote sent." |
| AC-18 | The email sender throws | A send and then a **Resend email** are submitted | The send returns `200` with `emailStatus = failed` and the warning toast; version, statuses and token persist; the log has only the quote id and failure category. The resend revokes the earlier token, creates a new one, writes `quote.email_resent`, and sends the email |
| AC-19 | A request whose contact has no email and no guest email | The editor loads and a send is posted | **Send quote** is disabled with "This customer has no email address."; the post returns `400` `errors.recipient`. SMS is always unchecked and disabled with "SMS isn't available yet."; **Send quote** has no dropdown; no deposit control or customer approval toggle exists |
| AC-20 | A sent version 1 | Afterwards the catalog item price, the organization tax rate and currency change, and a draft save, a calculate and a send are posted with no mutable version | Version 1's texts, prices, costs, rates, currency, terms, expiration and totals are unchanged when read. The posts return `409` `no_draft` and nothing changes |
| AC-21 | A sent version 1 with its token | **Revise quote** is pressed, version 2 is edited and saved, then sent | Version 2 is created mutable and prefilled from version 1 with rates recalculated; the quote stays `sent`, the request stays `quoted`, and version 1's token stays unrevoked while version 2 is a draft. On send, version 2 becomes immutable and current, version 1's token is revoked, a new token exists for version 2, version 1 is unchanged, and the read-only view lists "Version 2 · Current" and "Version 1 · Superseded" |
| AC-22 | A version-1 draft, and separately a version-2 revision | **Discard draft** is confirmed on each | The version-1 quote becomes `cancelled` (`quote.cancelled`, reason `discarded`), the request stays `ready_for_quote`, and **Create quote** is offered again. The version-2 draft and its lines are deleted (`quote.draft_discarded`), version 1 and its token are intact, and the quote status is unchanged |
| AC-23 | A `ready_for_quote` request with a draft quote | **Move back to review** and then **Cancel request** are submitted | Move back returns `409` `quote_draft_exists` with nothing changed. Cancel request cancels the request and the draft quote in one transaction, with `quote.cancelled` reason `request_cancelled` |
| AC-24 | The emailed link | The link is opened, and logs, audit rows and API responses are inspected | `/quote-approval` renders "This quote will be available soon." outside the shell with no API call. The raw token appears only in the email; the database holds only its hash |
| AC-25 | A sent quote with a revision in progress | An Owner, an Operations Manager and a Viewer open `/quotes/<id>`; an Accounting user opens the editor URL | The Owner sees the selected frozen version with internal note and margin, the banner "Version 2 is being revised." with **Continue editing**, and **Resend email**. The Operations Manager and Viewer see the same data with no actions. Accounting sees the forbidden state. Recent work quote links and the panel's **View quote** on the `quoted` request open this page |
| AC-26 | Read roles, a Technician, a Dispatcher with another branch, and an Owner of another organization | They call `calculate`, the mutations, `GET /quotes/{id}`, a version and a photo download | Read roles get `403` on `calculate` and mutations and `200` on reads. The Technician gets `403` everywhere. The out-of-scope Dispatcher and the foreign Owner get `404` everywhere, including `POST /quotes` with that `requestId`, and no data is changed |
| AC-27 | Every quote and assessment mutation of this feature | Audit rows and logs are inspected | Each operation writes the BR-33 action with its fields only; none contains line texts, messages, notes, terms, diagnosis, scope, names, contact data, addresses, file names or tokens |
| AC-28 | Desktop, tablet and mobile widths | The editor and the read-only view are used | Layouts follow the responsive rules with no horizontal page scroll; lines become stacked cards on mobile; line inputs have positional accessible names; totals updates are announced; disabled controls expose their helper text |

AC count justification: 28 active ACs, above the 25 target. This feature
introduces financial integrity rules (calculation, discount allocation,
rounding, immutability, versioning), a public secret (token), file uploads and
optimistic concurrency. Each AC above covers a distinct high-risk behavior
that `CLAUDE.md` requires to be evidenced, and repeated inputs are already
consolidated into table-driven criteria.

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Quote calculator, table-driven: per-line rounding half away from zero, the design sample, discount allocation with remaining cents and ties, discount bounds, optional exclusion, mixed-rate tax label, margin including R = 0, total overflow | AC-09, AC-10, AC-11, AC-12 |
| Backend unit | Token generation and hashing: length, base64url, stored hash ≠ raw, expiry at the end of the local day | AC-17, AC-24 |
| Backend integration | Assessment completion with photos: persistence, transaction, validation table, `413` | AC-01, AC-02 |
| Backend integration | Create: numbering, idempotency, status guards, concurrent creates on one request | AC-03, AC-04, AC-05 |
| Backend integration | Draft save: snapshots, foreign/inactive catalog items, validation table, client amounts ignored, ordering, `calculate` persists nothing, stale and concurrent `409` | AC-06, AC-07, AC-08, AC-13, AC-14 |
| Backend integration | Send and immutability: frozen snapshot, request `quoted`, token row, audit, email after commit, email failure with resend, no recipient, `no_draft` after catalog/settings changes | AC-17, AC-18, AC-19, AC-20 |
| Backend integration | Revise, discard and request guards: token supersession, version deletion, cancellation, `quote_draft_exists`, cancel request cascade | AC-21, AC-22, AC-23 |
| Authorization/tenant isolation | One representative cross-organization `404` per path family (quote, version, photo, create by `requestId`), the out-of-scope Dispatcher, read-role `403` on mutations and `calculate`, Technician `403`; audit and log content | AC-26, AC-27, AC-24 |
| Frontend component/service | Editor: line management, section-constrained keyboard reorder, rendering only backend amounts, the "—" state without a call, `409` handling, discard dialog, disabled Send without recipient | AC-07, AC-13, AC-14, AC-15, AC-19 |
| Frontend component/service | Preview exclusion of internal data; read-only view actions by role and banner; completion dialog validation | AC-16, AC-25, AC-01 |
| Visual QA (user-supplied) | Design 3 match, responsive layouts, accessibility checks, placeholder page | AC-03, AC-19, AC-24, AC-28 |

## Dependencies

- `requests-pipeline` (IMPLEMENTED): request statuses, panel, role and branch
  scope, recipient rule, transitions.
- `schedule-assessment` (AUDITED): the assessments with technician and start.
- `products-services` (implemented): catalog items with `is_taxable`, cost
  and price, and `GET /catalog-items`.
- `company-settings-and-branches` (implemented): currency, default tax rate,
  quote prefix and sequence.
- `customer-property-detail` (implemented): Recent work links, changed by
  BR-30.
- Customer Quote Approval (next spec): must replace the BR-27 placeholder,
  render the BR-23 content from a valid token, and add totals for selected
  optional lines. Phase 4 is not done while the placeholder remains.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | The customer-facing quote does not include the assessment diagnosis or photos. The schema has no version-level copy of them, and the manager can summarize findings in the customer message. |
| AS-02 | **Edit** on the Customer card opens the existing customer detail page. **View full assessment** opens the request panel, which now shows the findings. |
| AS-03 | Prices are always tax-exclusive. `organizations.prices_include_tax` keeps its default and isn't editable today, so it is not read. |
| AS-04 | The catalog picker reuses `GET /catalog-items` with its fixed page size of 10 and search. Dispatchers already have catalog read access. |
| AS-05 | Drafts read the current organization tax rate and currency on each calculation and save. Values are frozen only at send. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Assessment findings capture | Include text + photos · text only · out of scope | No | Include diagnosis, recommended scope and photos (BR-01 to BR-03) — user 2026-10-05 |
| OD-02 | Manual and catalog lines | Catalog + Custom line · catalog only | No | Catalog + Custom line; every snapshot editable on the quote (BR-09) — user 2026-10-05 |
| OD-03 | Optional items | Include · exclude | No | Include, excluded from totals; selection affects totals only in Customer Quote Approval (BR-11, BR-15) — user 2026-10-05 |
| OD-04 | Discount | Fixed or percent · fixed only · none | No | Fixed only, 0 ≤ D ≤ subtotal, prorated pre-tax, remaining cents to the largest line (BR-14) — user 2026-10-05 |
| OD-05 | Tax and margin | Taxable/Non-taxable with org rate; with or without internal margin | No | Org rate per taxable line, per-line rounding; internal margin after discount, pre-tax, excluding optional lines, derived not stored (BR-13, BR-16) — user 2026-10-05 |
| OD-06 | Deposit | Out of scope · informational | No | Out of scope — user 2026-10-05 |
| OD-07 | Terms | Presets + Custom · free text | No | Presets + Custom (BR-18) — user 2026-10-05 |
| OD-08 | Expiration | 30 days · 14 days | No | 30-day default (BR-19) — user 2026-10-05 |
| OD-09 | Numbering | At draft creation · at first send | No | At draft creation, never reused (BR-06) — user 2026-10-05 |
| OD-10 | Saving | Explicit · autosave | No | Explicit save with optimistic concurrency (BR-22) — user 2026-10-05 |
| OD-11 | Versioning | Previous link valid until revision sent · revoked at revision start | No | Valid until the revision is sent (BR-28) — user 2026-10-05 |
| OD-12 | Request and navigation | Create from `ready_for_quote` with editor and read-only view · editor only | No | Editor plus read-only internal view (BR-04, BR-30) — user 2026-10-05 |
| OD-13 | Permissions | As proposed · hide internal notes from read roles | No | As proposed; internal notes visible to internal readers, never in the public link (BR-07, BR-17) — user 2026-10-05 |
| OD-14 | Customer approval card | Informational · persisted | No | Informational (BR-20) — user 2026-10-05 |
| OD-15 | SMS and send options | Email only, no Mark as sent · Mark as sent option | No | Email active, SMS disabled, no Mark as sent (BR-21) — user 2026-10-05 |
| OD-16 | Token and link | Create token and link · token only | No | Token and link with a temporary placeholder; Phase 4 is incomplete until Customer Quote Approval replaces it (BR-27) — user 2026-10-05 |
| OD-17 | Text mapping | `scope` = frozen title · `scope` = message | No | `scope` = frozen request title; `customer_notes` and `internal_notes` keep separate purposes (BR-05, BR-17) — user 2026-10-05 |
| OD-18 | Schema package | As proposed · adjusted | No | Confirmed with adjustments: use the existing `quote_lines` table; only `discount_total` for the discount; add `terms`; `quote_access_tokens`; `assessment_attachments` per OD-01; both partial unique indexes (BR-32) — user 2026-10-05 |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03, AC-04, AC-05, AC-25 |
| FR-03 | AC-03, AC-04, AC-28 |
| FR-04 | AC-06, AC-07, AC-08 |
| FR-05 | AC-09, AC-10, AC-11, AC-12, AC-13 |
| FR-06 | AC-08, AC-16, AC-19 |
| FR-07 | AC-14, AC-15 |
| FR-08 | AC-16 |
| FR-09 | AC-11, AC-17, AC-18, AC-19 |
| FR-10 | AC-20 |
| FR-11 | AC-18 |
| FR-12 | AC-17, AC-24 |
| FR-13 | AC-21 |
| FR-14 | AC-22, AC-23 |
| FR-15 | AC-25, AC-28 |
| FR-16 | AC-26 |
| FR-17 | AC-27 |
| FR-18 | AC-01, AC-06, AC-17 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-05 | — → DRAFT | Created |
| 2026-10-05 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-05 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-05 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
