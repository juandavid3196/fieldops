# Completed jobs review

| Field    | Value                  |
| -------- | ---------------------- |
| Feature  | `completed-jobs-review` |
| Type     | Full-stack             |
| Status   | DRAFT                  |
| Created  | 2026-10-08             |
| Updated  | 2026-10-08             |
| Approved | —                      |

## Context and objective

`mobile-job-completion` leaves work orders in `completed` with no office
review before billing. This feature delivers Design 11 at `/invoices/review`:
a paginated queue of completed work orders without an active invoice, where
owners and accounting compare the approved quote with the time and materials
actually recorded, check completion evidence, keep an internal note, mark
follow-up and create a **draft** invoice. The draft always keeps the approved
quote amounts, discount, taxes and currency; real-world variances are review
information only. Generation is one idempotent transaction that also moves
the work order to `approved_for_billing`. Nothing is sent, charged or
messaged.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View the queue, details, evidence, audit trail and export in all branches; save the note; mark/clear follow-up; return to queue; generate the draft invoice | Send invoices, record payments, edit invoice lines or adjustments |
| Accounting (`accounting`) | The same as Owner, in branch scope (BR-02) | Anything outside branch scope (`404`); the Owner's Cannot list |
| Operations Manager (`operations_manager`), Viewer (`viewer`) | View the queue, details, evidence, audit trail and export in all branches | Save the note, change follow-up, return to queue or generate (`403`) |
| Dispatcher (`dispatcher`) | View the queue, details, evidence, audit trail and export in branch scope | Anything outside branch scope (`404`); every action (`403`) |
| Technician (`technician`), unknown roles | — | Every endpoint (`403`); the existing forbidden state in the app shell |

## Scope

- Route `/invoices/review` inside the authenticated app shell, and the
  Invoices navigation group with its sub-items (BR-22).
- Queue with metrics, filters (branch, completion date, technician, variance
  status), search, tabs and pagination.
- Review detail: header, billing comparison, totals, completion verification,
  evidence, work evidence, audit trail and invoice preparation data.
- Labor and material variance detection.
- Internal accounting note, follow-up mark and **Return to quote** (returns the job to the review queue).
- **Generate invoice**: one transactional, idempotent draft invoice per work
  order.
- CSV export of the filtered queue.
- Schema amendments SA-04, SA-05 and SA-06 (approved 2026-10-08), with one
  migration generated and never applied. SA-07 records that no tax schema
  change is made.

## Non-goals

- Sending invoices, email, SMS, notifications, links to customers or
  completion reports ("View completion report" is hidden).
- Payments, payment options, card/ACH processing or any `payments` /
  `payment_allocations` write.
- Billing actual quantities, adjustments, extra charges, discounts other than
  the approved quote's, or editing invoice lines.
- Tax jurisdictions or tax rates other than the frozen quote rates.
- Drafts, Sent, Payments and Overdue pages (coming-soon links only).
- Returning work to technicians (`needs_correction`), reopening visits or
  editing completion data, checklist, materials, evidence or acknowledgment.
- Voiding invoices.
- Changes to the global search or the app shell header.
- Browser automation or Playwright by agents.

## User flow

1. Owner or Accounting opens **Invoices → Needs review**.
2. The system shows the metrics, filters and the queue, with the first item
   selected.
3. The user filters, searches, switches tabs or pages, and selects a job.
4. The system shows the job's billing comparison, totals, verification,
   evidence and invoice preparation data.
5. The user optionally writes an accounting note, then either **Return to
   queue**, **Mark for follow-up** or **Generate invoice**.
6. On **Generate invoice**, the system asks for confirmation when variances
   exist, blocks and marks follow-up when a mandatory closing requirement is
   missing, and otherwise creates the draft invoice, announces it and removes
   the job from the queue.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every endpoint must resolve the organization and membership from the session and apply role permissions and branch scope per BR-01 and BR-02. |
| FR-02 | The queue must list exactly the work orders of BR-03, filtered, searched, tabbed, sorted and paginated per BR-04 and BR-05. |
| FR-03 | The queue response must return metrics and tab counts per BR-06. |
| FR-04 | The detail must compare each approved quote line with the recorded time and materials and classify labor and material variances per BR-07 to BR-09. |
| FR-05 | The detail must return approved and invoice totals per BR-10. |
| FR-06 | The detail must return the completion verification with mandatory and informational items per BR-11. |
| FR-07 | The detail must return evidence, work evidence and audit trail per BR-12, and evidence images must be served per BR-13. |
| FR-08 | The detail must return invoice preparation defaults per BR-14. |
| FR-09 | Owner and Accounting must be able to save the note, mark or clear follow-up and return a job to the queue per BR-15 and BR-16. |
| FR-10 | **Generate invoice** must validate, guard and create the draft invoice and its side effects in one transaction per BR-17 to BR-19. |
| FR-11 | Generation must be idempotent and serialized per BR-20. |
| FR-12 | The filtered queue must be exportable to CSV per BR-21. |
| FR-13 | The persistence model must apply schema amendments SA-04 to SA-06 per Data and persistence impact. |
| FR-14 | The route, navigation and queue panel must follow BR-22 and BR-23. |
| FR-15 | The detail and invoice panels must render Design 11 per BR-24 to BR-26. |
| FR-16 | Note, follow-up, return, generation and export interactions must follow BR-27. |
| FR-17 | The page must meet the responsive, accessibility and fidelity rules of BR-28 and BR-29. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Roles. Read policy (queue, detail, evidence image, export, options): `owner`, `operations_manager`, `dispatcher`, `accounting`, `viewer`. Action policy (`PATCH` review, `POST` invoice): `owner`, `accounting`. Other roles → `403` before any read or write. No organization id is accepted from the client. | Backend |
| BR-02 | Branch scope (`requests-pipeline` BR-02): `owner`, `operations_manager`, `viewer` and members with `organization_users.is_all_branches` see all branches; `dispatcher` and `accounting` otherwise see only `organization_user_branches`. A work order is visible when its `branch_id` is in scope. A work order, branch, technician or evidence of another organization, outside scope or unknown → identical `404` with no foreign data and no write. Lists, counts, metrics and exports include only visible work orders. | Backend |
| BR-03 | Queue membership: work orders of the organization with `status = completed` and no `invoices` row for that work order with `status <> 'void'`. A completed work order whose only invoices are `void` is included. Completion date = the latest `visits.actual_completed_at` of its `completed` visits. Completed by = the active (`unassigned_at` null) primary assignee of the visit with that latest completion. GET detail, `PATCH` review and evidence images accept only queue members; any other visible work order → identical `404`. | Backend |
| BR-04 | Filters, all optional, combined with AND: `branchId` (a branch in scope); `completed` = `7d`, `30d` (default), `90d` or `all`, where `Nd` keeps work orders completed at or after 00:00 of the local date N−1 days before today in `organizations.timezone`; `technicianId` (a technician profile of the organization) keeps work orders where that profile is the active primary assignee of any non-cancelled visit; `variance` = `all` (default), `none`, `any`, `labor`, `material` per BR-08/BR-09; `search` trimmed 0–100 characters, case-insensitive substring of customer display name or work order title, or a work order number written as digits or `<prefix>-<digits>`. An invalid value → `400` with field errors; a foreign or out-of-scope `branchId`/`technicianId` → `404`. | Backend |
| BR-05 | Tabs, sort and pages: `tab` = `all` (default), `variances` (labor or material variance) or `ready` (no variance and every mandatory BR-11 item met). Sort: completion date descending, then work order number descending. `page` ≥ 1 (default 1), page size 20; a page beyond the last returns an empty `items` with the totals. | Backend |
| BR-06 | Metrics and counts apply branch, completion date, technician and search; they ignore `variance` and `tab`: **Needs review** = queue count; **With variances** = count with any variance; **Ready to invoice** = count of `ready` items; **Completed value** = Σ approved total (BR-10) in the organization currency. Tab counts `all`, `variances`, `ready` apply every filter except `tab`. | Backend |
| BR-07 | Comparison rows. Billed lines = the approved quote version's (`work_orders.quote_version_id`) non-optional `quote_lines` plus optional lines selected in its approved `quote_responses` (`quote_response_optional_lines`), ordered by `sort_order`. Each row: index, name, approved quantity and unit, actual, billable quantity (= approved quantity), tax ("Taxable" when `tax_rate > 0`, else "Non-taxable"), amount (`line_subtotal`) and status. Hourly lines = `service` lines whose trimmed, case-insensitive unit is `h`, `hr`, `hrs`, `hour` or `hours`. Non-hourly `service` lines: actual = approved quantity, status "Matches quote". `product` lines: when `work_order_planned_materials` rows reference the line, actual = Σ `visit_materials.quantity` linked to those rows across non-cancelled visits, compared with Σ their planned quantity: equal "Matches quote", higher "Over quote", lower "Under quote"; with no referencing rows, actual "—" and status "Not tracked". After the quote lines, one row per `visit_materials` row without `planned_material_id` and per planned material without `quote_line_id` with used quantity > 0: approved "—", actual used quantity, billable 0, tax and amount "—", status "Not in quote". | Backend |
| BR-08 | Labor variance. Approved seconds = Σ quantity × 3600 of the billed hourly lines (BR-07; unselected optional lines never count), rounded to whole seconds. Actual seconds = Σ (`ended_at − started_at`) of closed `work` entries of non-cancelled visits. Tolerance = max(900, 10 % of approved seconds). When approved > 0: actual − approved > tolerance → **Over quote**; approved − actual > tolerance → **Under quote**; otherwise none. When approved = 0: actual > 0 → **Over quote**; actual = 0 → none. With exactly one hourly line, its row shows the actual labor time and the labor status. Otherwise, when there are two or more hourly lines or actual > 0, a "Labor total" row follows the quote lines (approved Σ hours or "0h", actual labor time, billable and amount "—", labor status) and every hourly line shows actual "—" and status "See labor total". Durations format as "<h>h", "<h>h <m>m" or "<m>m" with minutes rounded down. | Backend |
| BR-09 | Material variance: true when any planned material of the work order has Σ used quantity ≠ planned quantity, or any non-cancelled visit has a `visit_materials` row without `planned_material_id`. Item badge: "No variance", "Labor variance", "Material variance" or "Labor and material variance"; a second tag "Follow-up" when `billing_follow_up_at` is set. Variances never change billed amounts. | Both |
| BR-10 | Totals from the approved `quote_responses` row of the version: approved subtotal = `subtotal`, discount = `discount_total`, tax = `tax_total`, approved total = `total`, currency = `quote_versions.currency`. Invoice subtotal, discount, tax and total equal those values; variance amount = invoice total − approved total (always 0.00). Tax label follows `quote-builder` BR-15 ("Tax (<rate>%)" or "Tax") over the billed lines. Amounts are backend-calculated; the frontend never recalculates them. | Backend |
| BR-11 | Completion verification, aggregated over non-cancelled visits. **Mandatory** (block generation, BR-18): (a) every `visit_checklist_items` row with `is_required = true` is completed; (b) every completed visit has at least one `before` and one `after` `visit_evidence`; (c) every completed visit has a `customer_signoffs` row and, when `organizations.require_customer_signature` is true, its `acknowledgement_method = 'signed'`. **Informational** (never block): materials recorded (any `visit_materials` row), labor time recorded (actual seconds > 0), completion summary recorded (any non-empty `visits.completion_summary`). Labels: "<c>/<t> tasks complete" (all checklist items); "Before and after photos" / "Before and after photos missing"; "Customer signature: <signer name>" when signed, "Customer acknowledgment: <method label>" otherwise (`mobile-job-completion` BR-05 labels), "Customer signature missing" or "Customer acknowledgment missing" when unmet; "Materials recorded" / "No materials recorded"; "Labor time recorded: <duration>" / "No labor time recorded"; "Completion summary recorded" / "No completion summary". Each item returns `{ key, met, mandatory, label }`. | Both |
| BR-12 | Detail content: header (title, `<prefix>-<number>`, quote `<quote_prefix>-<quote_number>`, customer display name, property one-line address, completed by, completion date, organization timezone); evidence summary = count of all evidence and up to 3 thumbnails (`before` first, then `after`); work evidence per visit = visit number, checklist items (label, required, completed), materials (description, quantity, unit, Planned/Added), evidence (id, type, caption) grouped Before, During, After, Incident, Other, completion summary, and acknowledgment (method label, signer name, relationship label, comments or reason, "Signature captured" when signed); audit trail = up to 50 `audit_logs` rows of the work order and its visits, newest first, each with a readable action label (known `work_order.*` and `visit.*` actions mapped, others "Activity recorded"), actor full name or "System", and time. Never returned: signature content, audit `before_data`/`after_data`/`metadata`, costs or IP addresses. | Backend |
| BR-13 | Evidence image: `GET …/evidence/{evidenceId}` only for evidence of a visit of a visible queue member; otherwise identical `404`. Response as `mobile-job-progress` BR-12: stored content type, `Content-Disposition: inline`, `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`. | Backend |
| BR-14 | Invoice defaults: number preview `<invoice_prefix>-<next_invoice_number>` (informational; the number is assigned at generation, BR-19); issue date = today in the organization timezone; payment terms from `quote_versions.terms`: the frozen `net_15` text → `net_15`, the frozen `net_30` text → `net_30`, anything else → `due_upon_receipt`; due date = issue date + 0, 15 or 30 days; tax label (BR-10) and currency, both read-only; follow-up `{ at, byName }` or null; note; `canAct` (BR-01 action policy). Terms labels: `due_upon_receipt` "Due upon receipt", `net_15` "Net 15", `net_30` "Net 30". | Both |
| BR-15 | Review update `PATCH` `{ note?, followUp? }`, at least one field (`400`). `note` trimmed, 0–500 characters, empty → null, stored in `work_orders.billing_review_note`. `followUp: true` sets `billing_follow_up_at = now` and `billing_follow_up_by_user_id` = actor when not already set (unchanged otherwise); `false` clears both. `updated_at = now` when anything changed. Audit (organization, work order branch, actor, `entity_type` `work_order`): `work_order.billing_note_updated` (metadata `{ length }`) and `work_order.billing_follow_up_marked` / `work_order.billing_follow_up_cleared` (metadata `{ reason: "manual" \| "returned_to_queue" \| "requirements_unmet" }`; the client may send only the first two). The note text never appears in audit or logs. Sending the current values is a no-op without audit. The update runs in one transaction that locks the work order row and re-checks queue membership (BR-03) after locking; a work order that left the queue → `404` with no write. Concurrent updates apply in lock order (last write wins). | Backend |
| BR-16 | **Return to quote** (the Design 11 "Return to queue" action, renamed per OD-13) = `PATCH` with the current note and `followUp: false`, reason `returned_to_queue`; the work order status and invoices are unchanged and the job stays in the queue. | Both |
| BR-17 | Generate request `{ issueDate, paymentTerms, note, acknowledgeVariances }`: `issueDate` an ISO date between today − 30 days and today in the organization timezone ("Choose an issue date within the last 30 days."); `paymentTerms` one of BR-14 ("Select payment terms."); `note` per BR-15; `acknowledgeVariances` boolean. Invalid → `400` with field errors and no write. | Both |
| BR-18 | Generate guards, after BR-01/BR-02 and BR-17, evaluated after locking (BR-20) in this order: (1) an invoice with `status <> 'void'` exists for the work order → `200 { changed: false, invoice }`, no write; (2) work order status ≠ `completed` → `409 work_order_status_invalid` "This job is no longer ready for invoicing.", no write; (3) any mandatory BR-11 item unmet → the note is saved and follow-up is marked (BR-15, reason `requirements_unmet`) and committed, no invoice, `409 completion_requirements_unmet` "Complete the required closing items before generating an invoice. This job was marked for follow-up."; (4) a labor or material variance exists and `acknowledgeVariances` is not true → `409 variance_confirmation_required` "Confirm the variances to generate this invoice.", no write; (5) Σ billed `line_subtotal`, `line_tax`, `line_total` ≠ the BR-10 subtotal, tax and total → `409 invoice_totals_mismatch` "The approved quote amounts don't match its lines. Review the quote before invoicing.", no write. Guard (3) cannot be bypassed by any confirmation. | Backend |
| BR-19 | Generate effect, one transaction: (a) invoice number = `organizations.next_invoice_number`, which is incremented by 1; (b) one `invoices` row: `status = draft`, organization, `branch_id` and `customer_id` of the work order, `work_order_id`, `invoice_number`, `issue_date`, `due_date` (BR-14), `payment_terms`, `currency`, `subtotal`, `discount_total`, `tax_total`, `total` per BR-10, `amount_paid = 0`, `balance_due = total`, `notes` null, `sent_at` null, `created_by_user_id` = actor; (c) one `invoice_lines` row per billed line copying name as `description`, `quantity`, `unit`, `unit_price`, `tax_rate`, `line_subtotal`, `line_tax`, `line_total`, `sort_order`, with `source_quote_line_id`; never lines for "Not in quote" or labor rows; (d) `work_orders.status = approved_for_billing`, note saved (BR-15), follow-up cleared, `updated_at = now`; (e) every `completed` visit of the work order → `approved` with `reviewed_by_user_id` = actor, `reviewed_at = now`, `updated_at = now` and one `visit_status_history` row `completed → approved`; (f) audit `invoice.created` (`entity_type` `invoice`, invoice id, branch, after `{ status, invoiceNumber, total, currency }`, metadata `{ workOrderId, laborVariance, materialVariance }`) and `work_order.status_changed` (before/after `{ status }`, metadata `{ invoiceId }`). Any failure rolls back everything, including the counter. No notification, email, SMS, payment or PDF is created. Response `201 { changed: true, invoice: { id, number, status, total, currency } }`. | Backend |
| BR-20 | Concurrency and idempotency: the transaction locks the work order row, then the organization row, and re-reads state after locking. Concurrent or repeated generations produce exactly one non-void invoice, one number and one set of side effects; the others return `200 changed = false`. SA-04 is the backstop: its unique violation is treated as `changed = false` returning the existing invoice. `PATCH` review shares the work order lock (BR-15), so a `PATCH` serialized after a generation returns `404` and never writes note or follow-up on an invoiced work order. | Backend |
| BR-21 | CSV export `GET …/export` with the BR-04/BR-05 filters and `tab` (no `page`), sorted per BR-05, `text/csv; charset=utf-8`, file `completed-jobs-review-<yyyy-mm-dd>.csv` (organization date). Columns: Work order, Customer, Job, Branch, Technician, Completed at (organization local `yyyy-mm-dd HH:mm`), Approved total, Currency, Labor variance (None/Over/Under), Material variance (Yes/No), Ready (Yes/No), Follow-up (Yes/No). Cells starting with `=`, `+`, `-`, `@`, tab or carriage return are prefixed with `'`. More than 5,000 rows → `409 export_too_large` "Narrow the filters to export 5,000 jobs or fewer.". Notes, addresses and audit data are never exported. | Backend |
| BR-22 | Route and navigation: `/invoices/review` is a lazy route in the app shell, reachable for the BR-01 read roles; `technician` gets the existing forbidden state. The Finance item **Invoices** becomes an expandable group with **Needs review** (`/invoices/review`, current on this page), **Drafts**, **Sent**, **Payments** and **Overdue** (coming-soon paths). `/invoices` redirects to `/invoices/review`. | Frontend |
| BR-23 | Header and queue panel: title "Completed jobs review", subtitle "Verify completed work before invoicing."; metric cards "Needs review", "With variances", "Ready to invoice", "Completed value" (currency); **Export** menu with "Export CSV". Filters "Branch" ("All branches" + branches in scope), "Completion date" ("Last 7 days", "Last 30 days" default, "Last 90 days", "All time"), "Technician" ("All technicians" + technicians), "Variance status" ("All", "No variance", "Any variance", "Labor variance", "Material variance") and search "Search by customer, WO #, or job description..." (debounced). Panel title "Needs review (<n>)" and tabs "All (<n>)", "Variances (<n>)", "Ready (<n>)". Items show customer, approved total, job title, `#<prefix>-<n>`, "Completed <MMM d, yyyy>, <h:mm a>" and the BR-09 badges; the selected item is highlighted. Paging controls below the list. Changing a filter, search or tab resets to page 1 and selects the first item. States: loading skeletons; "No completed jobs need review." when the unfiltered queue is empty; "No jobs match these filters." with **Clear filters** otherwise; load error "We couldn't load completed jobs. Try again." with **Retry**. | Frontend |
| BR-24 | Detail panel: header per BR-12 with chips "Completed" and "Not invoiced"; tabs "Billing review", "Work evidence", "Audit trail". **Billing review**: "Billing comparison" table (#, Line item, Approved, Actual, Billable, Tax, Amount, Status) per BR-07/BR-08 with status icon and text; **Add adjustment** shown disabled with the description "Adjustments will be available in invoice drafts."; the warning "Additional charges require customer approval." only when a row is "Over quote" or "Not in quote"; "Totals comparison" with Approved subtotal, Actual billable subtotal, Discount (only when > 0), Tax label, Invoice total and Variance; "Completion verification" with the BR-11 items (met/unmet icon and text; unmet mandatory items styled as errors with the text "Required before invoicing"); "Evidence (<n>)" with thumbnails that open the Work evidence tab; "Accounting note (internal only)" textarea, placeholder "Add an internal note about billing, variances, or customer communication...", counter "<n>/500". **Work evidence** and **Audit trail** render BR-12 content, with "No activity recorded." when empty. Detail states: loading skeleton; `404` → "This job is no longer in the review queue." and the queue reloads; error "We couldn't load this job. Try again." with **Retry**. | Frontend |
| BR-25 | Invoice panel: "Invoice details" with "Invoice number (preview)" (read-only), "Issue date" (date picker limited per BR-17), "Payment terms" (select), "Due date" (read-only, recalculated on change), "Tax jurisdiction" (read-only "Organization default • <rate>%" or "Quote tax rates"), "Currency" (read-only). "Delivery after generation" and "Payment options (on invoice)" are rendered with every checkbox and field disabled and unchecked/empty, followed by "Sending and online payments will be available later.". Summary: Subtotal, Discount (only when > 0), tax label, Total; info "Generating creates a draft invoice. Review it before sending.". Actions: **Return to quote**, **Mark for follow-up** (reads **Remove follow-up** when marked, with "Follow-up marked by <name> on <date>" shown) and **Generate invoice** (primary). | Frontend |
| BR-26 | Read-only roles (`canAct` false): note, issue date and terms are read-only and the three actions are hidden, replaced by "Only owners and accounting can review and generate invoices.". | Frontend |
| BR-27 | Interactions: actions send the current note; while a request is pending all three actions are disabled and the clicked one shows progress. **Return to quote** → "Returned to quote.", queue reloads and the next item (or the first) is selected. **Mark/Remove follow-up** → "Marked for follow-up." / "Follow-up removed." and the badge updates. **Generate invoice**: client validates BR-17 fields and focuses the first invalid one; with any variance it first opens the dialog "Generate invoice with variances?" — "This job has labor or material variances. The draft invoice uses the approved quote amounts; variances stay for review only." — **Generate draft** / **Cancel**, and sends `acknowledgeVariances: true` only after confirming; when a mandatory item is unmet no confirmation is offered: the request is sent and the `409 completion_requirements_unmet` message is shown with the follow-up state updated. `201` → "Draft invoice <number> created."; `200 changed = false` → "Draft invoice <number> already exists."; both reload the queue and metrics and select the next item. `409 work_order_status_invalid` → its message and queue reload; `409 invoice_totals_mismatch` and `variance_confirmation_required` → their messages; `400` → field messages; other failures → "We couldn't complete this action. Try again." keeping every entered value. Selecting another item with an unsaved note change asks "Discard unsaved note?" (**Discard** / **Keep editing**). Export: downloads the file; `409 export_too_large` shows its message; other failures "We couldn't export the queue. Try again.". Messages are announced politely. | Frontend |
| BR-28 | Responsive: ≥ 1280 px three columns (queue, detail, invoice panel) as Design 11; 768–1279 px queue and detail side by side with the invoice panel below the detail; < 768 px the queue fills the width and selecting an item opens the detail with **Back to quote** and the invoice panel below it. No horizontal page scroll at 375 px; the comparison table scrolls inside its card. | Frontend |
| BR-29 | Accessibility and fidelity: tabs use tab semantics; queue items are keyboard-selectable with the selection exposed; the comparison table has header cells; statuses, variances and verification never rely on color alone; dialogs trap and restore focus; field errors are linked to fields; targets ≥ 44 × 44 px on touch layouts. Design 11 deviations: Delivery and Payment panels disabled (BR-25); Add adjustment disabled; tax jurisdiction and currency read-only; "Completion summary sent" reads "Completion summary recorded"; "Return to queue" reads "Return to quote" and the mobile back action reads "Back to quote" (OD-13); "View completion report" hidden; Export menu has one option; mockup data are examples (by BR-08, the sample "2h" approved vs "1h 42m" actual is "Under quote"). | Frontend |

### Review field rules

| Field | Rule | Message when invalid |
| ----- | ---- | -------------------- |
| `note` | Trimmed, 0–500 characters, empty → null | "Use 500 characters or fewer." |
| `issueDate` | ISO date, today − 30 days … today (organization timezone) | "Choose an issue date within the last 30 days." |
| `paymentTerms` | `due_upon_receipt`, `net_15`, `net_30` | "Select payment terms." |
| `acknowledgeVariances` | Boolean, required on generate | "Confirm the variances to generate this invoice." |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Work order `completed` (no active invoice) | `approved_for_billing` + draft invoice | Generate invoice | Owner, Accounting | BR-17, BR-18 |
| Work order `approved_for_billing` with active invoice | unchanged | Generate invoice | Owner, Accounting | `200 changed = false` |
| Work order `completed` | unchanged, follow-up marked | Generate invoice | Owner, Accounting | Mandatory BR-11 item unmet |
| Work order any other status | unchanged | Generate invoice | Owner, Accounting | `409 work_order_status_invalid` |
| Visit `completed` | `approved` | Generate invoice | Owner, Accounting | Same transaction as the invoice |
| Follow-up clear | marked | Mark for follow-up / blocked generation | Owner, Accounting | Queue member |
| Follow-up marked | clear | Remove follow-up / Return to quote / Generate invoice | Owner, Accounting | Queue member |
| — | Invoice `draft` | Generate invoice | Owner, Accounting | BR-19; no transition beyond `draft` |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): `organizations`
  (`timezone`, `currency`, `invoice_prefix`, `next_invoice_number`,
  `work_order_prefix`, `quote_prefix`, `require_customer_signature`),
  `organization_users`, `organization_user_branches`, `branches`, `customers`,
  `properties`, `quotes`, `quote_versions`, `quote_lines`, `quote_responses`,
  `quote_response_optional_lines`, `work_orders`,
  `work_order_planned_materials`, `visits`, `visit_assignments`,
  `technician_profiles`, `users`, `visit_time_entries`,
  `visit_checklist_items`, `visit_materials`, `visit_evidence`,
  `customer_signoffs` (never `signature_content`), `invoices`, `audit_logs`.
- Written: `organizations.next_invoice_number`, `invoices`, `invoice_lines`,
  `work_orders` (`status`, `billing_review_note`, `billing_follow_up_at`,
  `billing_follow_up_by_user_id`, `updated_at`), `visits` (`status`,
  `reviewed_by_user_id`, `reviewed_at`, `updated_at`), `visit_status_history`,
  `audit_logs`.
- Not written: `payments`, `payment_allocations`, `notifications`,
  `visits.review_notes`, quote tables, completion data.
- Schema amendments: **approved by the user 2026-10-08** (OD-07).
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model
  and generates one migration only under `--generate-migration`. Agents never
  apply it. Review by `database-reviewer`.
  - SA-04 `invoices`: `CREATE UNIQUE INDEX ux_invoices_work_order_active ON
    invoices(organization_id, work_order_id) WHERE status <> 'void'`. If
    duplicates exist, the migration fails instead of altering data.
  - SA-05 `invoices`: add `discount_total numeric(14,2) NOT NULL DEFAULT 0
    CHECK (discount_total >= 0)` and `payment_terms varchar(20) NULL CHECK
    (payment_terms IN ('due_upon_receipt','net_15','net_30'))`.
  - SA-06 `work_orders`: add `billing_review_note varchar(500) NULL`,
    `billing_follow_up_at timestamptz NULL`, `billing_follow_up_by_user_id
    uuid NULL REFERENCES users(id)` and `CHECK ((billing_follow_up_at IS
    NULL) = (billing_follow_up_by_user_id IS NULL))`.
  - SA-07: no schema change; tax rates and currency come from the approved
    quote (OD-08).

## Tenant isolation and authorization

- Organization and membership are resolved from the session; role and branch
  scope per BR-01/BR-02 apply before any read or write.
- Client-provided identifiers: `workOrderId` and `evidenceId` (path),
  `branchId` and `technicianId` (query). Each is authorized server-side
  against the session organization and branch scope; otherwise identical
  `404`. Customer, quote, visits, lines and invoice data are resolved
  server-side from the authorized work order. No organization, customer,
  amount, tax, currency or invoice number is accepted from the client.
- Actions require the action policy; frontend hiding is UX only.
- Responses use `Cache-Control: no-store`. Logs and audit never contain note
  text, signer data, addresses or customer names.

## API contracts

Contract status: Final. Base path `/billing-review`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/options` | — | `200 { timezone, currency, branches: [{ id, name }], technicians: [{ id, name }], canAct }` (in scope) | `403` | Read |
| GET | `/queue` | Query `branchId`, `completed`, `technicianId`, `variance`, `search`, `tab`, `page` (BR-04, BR-05) | `200 { items: [{ workOrderId, number, title, customerName, approvedTotal, currency, completedAt, laborVariance: "none"\|"over"\|"under", materialVariance, ready, followUp }], page, pageSize, total, tabs: { all, variances, ready }, metrics: { needsReview, withVariances, readyToInvoice, completedValue, currency } }` | `400` · `403` · `404` | Read |
| GET | `/export` | Same query without `page` | `200 text/csv` (BR-21) | `400` · `403` · `404` · `409 export_too_large` | Read |
| GET | `/work-orders/{workOrderId}` | — | `200 BillingReviewDetail` (shape below; BR-07–BR-14) | `403` · `404` | Read |
| GET | `/work-orders/{workOrderId}/evidence/{evidenceId}` | — | `200` image bytes (BR-13) | `403` · `404` | Read |
| PATCH | `/work-orders/{workOrderId}/review` | `{ note?, followUp?, reason?: "manual"\|"returned_to_queue" }` | `200 { note, followUp }` | `400` · `403` · `404` | Action |
| POST | `/work-orders/{workOrderId}/invoice` | `{ issueDate, paymentTerms, note, acknowledgeVariances }` | `201 { changed: true, invoice }` · `200 { changed: false, invoice }`; `invoice = { id, number, status, total, currency }` | `400` · `403` · `404` · `409 work_order_status_invalid`, `completion_requirements_unmet`, `variance_confirmation_required`, `invoice_totals_mismatch` | Action |

Errors use ProblemDetails with `code`; `400` includes field `errors`.
Money values are JSON numbers with 2 decimals; instants are ISO 8601 UTC;
dates are ISO `yyyy-mm-dd`. Display strings are produced by the backend only
where a rule says so (quantities and durations, BR-07/BR-08); the frontend
maps codes to the labels of the BR rules.

`BillingReviewDetail`:

| Field | Shape |
| ----- | ----- |
| `header` | `{ workOrderId, number, title, quoteNumber, customerName, propertyAddress, completedByName \| null, completedAt, timezone }` (`number` and `quoteNumber` as `<prefix>-<n>`) |
| `lines` | `[{ index \| null, kind: "quote" \| "labor_total" \| "not_in_quote", name, approved: string \| null, actual: string \| null, billable: string \| null, tax: "taxable" \| "non_taxable" \| null, amount: number \| null, status: "matches" \| "over" \| "under" \| "not_in_quote" \| "not_tracked" \| "see_labor_total" }]`; `approved`/`actual`/`billable` are formatted quantities (no trailing zeros, unit appended unless `unit`) or BR-08 durations; `null` renders "—"; `index` is null for non-quote rows |
| `laborVariance` | `"none" \| "over" \| "under"` |
| `materialVariance` | boolean |
| `totals` | `{ approvedSubtotal, invoiceSubtotal, discountTotal, taxLabel, taxTotal, invoiceTotal, variance, currency }` |
| `verification` | `[{ key: "checklist" \| "photos" \| "acknowledgment" \| "materials" \| "labor" \| "summary", met, mandatory, label }]` |
| `ready` | boolean (no variance and every mandatory item met) |
| `evidence` | `{ count, thumbnails: [{ id, type: "before" \| "after" }] }` (≤ 3) |
| `workEvidence` | `[{ visitId, visitNumber, checklist: [{ label, required, completed }], materials: [{ description, quantity: string, unit, origin: "planned" \| "added" }], evidence: [{ id, type: "before" \| "during" \| "after" \| "incident" \| "other", caption \| null }], completionSummary \| null, acknowledgment: { method, signerName \| null, relationship \| null, comment \| null, signatureCaptured } \| null }]`, ordered by `visitNumber` |
| `auditTrail` | `[{ label, actorName, occurredAt }]` (≤ 50, newest first) |
| `invoiceDefaults` | `{ numberPreview, issueDate, paymentTerms, dueDate, taxLabel, currency }` |
| `note` | string \| null |
| `followUp` | `{ at, byName } \| null` |
| `canAct` | boolean |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Queue panel and metrics | Skeleton cards and items | BR-23 empty texts | BR-23 error + Retry | Technician → forbidden state | List with first item selected |
| Detail panel | Skeleton | N/A (a job is always selected when the queue has items) | BR-24 error + Retry; `404` text | Read-only roles see content (BR-26) | BR-24 content |
| Invoice panel | Skeleton | N/A | Shares the detail error | BR-26 read-only text | BR-25 content |
| Actions | Progress on the clicked action | N/A | BR-27 messages | Hidden for read-only roles | BR-27 messages |

- Mockups: `design/assets/11- design.png` (Approved; deviations in BR-29).
- Responsive and accessibility constraints: BR-28 and BR-29.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Role without read permission | `403`; forbidden state | None |
| Read-only role calls an action | `403`; "Only owners and accounting can review and generate invoices." | None |
| Foreign, out-of-scope or unknown id | Identical `404`; "This job is no longer in the review queue." | None |
| Invalid filter or body | `400` with field errors (Review field rules) | None |
| Job already invoiced | `200 changed = false`; "Draft invoice <number> already exists." | None |
| Work order not `completed` | `409 work_order_status_invalid` | None |
| Mandatory closing item unmet | `409 completion_requirements_unmet` | Note saved, follow-up marked, audit |
| Variance without confirmation | `409 variance_confirmation_required` | None |
| Line sums ≠ approved totals | `409 invoice_totals_mismatch` | None |
| Export over 5,000 rows | `409 export_too_large` | None |
| Failure during generation | `500`; "We couldn't complete this action. Try again." | Full rollback, counter unchanged |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Members of every role, including dispatcher and accounting with and without `is_all_branches` | They call each endpoint (table-driven) | Read roles get data limited to branch scope; technician and unknown roles get `403`; operations manager, viewer and dispatcher get `403` on `PATCH` and `POST` with no write |
| AC-02 | Work orders, evidence, branches and technicians of another organization or outside branch scope, and random ids | They are used in paths and filters | Identical `404` with no foreign data and no write (one representative case per path) |
| AC-03 | Work orders in every status, completed ones with draft, sent and only void invoices | The queue is loaded | Only `completed` work orders without a non-void invoice appear (void-only included), with BR-03 completion date and completed-by; detail and `PATCH` for excluded ones return `404` |
| AC-04 | A queue across branches, dates, technicians and variances | Filters, search (name, title, `WO-3091`, `3091`), tabs and pages are applied | Results, sort, page size 20, empty page beyond the last and `400`/`404` for invalid or foreign filters follow BR-04 and BR-05 |
| AC-05 | The same queue | Metrics and tab counts are read with filters | Values follow BR-06: they apply branch, date, technician and search, ignore variance and tab, and Completed value sums approved totals |
| AC-06 | Hourly line quantities and recorded work time (table-driven: within tolerance, exactly at tolerance, 16 min over with 1h approved, 10 % rule with 4h approved, under, approved 0 with and without work, two hourly lines) | The detail is loaded | Labor status, actual time format and "Labor total" / "See labor total" rows follow BR-08 |
| AC-07 | Product lines with matching, higher and lower used quantities, without planned rows, non-hourly services, selected and unselected optional lines, additional materials and planned materials without quote line | The detail is loaded | Rows, statuses, billable quantities and the material variance flag follow BR-07 and BR-09; unselected optional lines are absent |
| AC-08 | Approved quotes with and without discount, mixed and single tax rates | The detail is loaded | Totals, discount, tax label, currency and variance 0.00 follow BR-10 regardless of variances |
| AC-09 | Visits with incomplete required tasks, missing before/after photos, missing acknowledgment, non-signed acknowledgment with signature required and not required, no materials, no labor and no summary, across multiple visits | The detail is loaded | Verification items, labels, `met`, `mandatory` and `ready` follow BR-11 |
| AC-10 | A job with evidence of every type, materials, acknowledgment with signature and audit rows | The detail is loaded | Header, evidence summary, work evidence and audit trail (≤ 50, newest first, mapped labels) follow BR-12; no signature content, audit payloads, costs or IP addresses are returned |
| AC-11 | Evidence of a queue member, of an excluded work order and of another organization | The evidence image is requested | Queue member returns bytes with BR-13 headers; others return identical `404` |
| AC-12 | Quotes with each frozen terms text and a custom text, in a non-UTC organization timezone | The detail is loaded | Number preview, issue date, terms, due date, tax label and currency follow BR-14 |
| AC-13 | A queue member | Note, follow-up on/off, repeated values and Return to quote are sent | Stored values, no-op behavior, `400` over 500 characters and audit rows without note text follow BR-15 and BR-16; status unchanged |
| AC-14 | A ready job with selected optional lines, discount and "Not in quote" materials | Generate invoice succeeds | One draft invoice and lines per BR-19 (amounts, discount, terms, dates, currency copied; no Not in quote or labor lines), counter incremented, work order `approved_for_billing`, visits `approved` with history, follow-up cleared, audit rows; no notification, payment or email row |
| AC-15 | A ready job | Generate invoice is sent twice sequentially and twice concurrently | Exactly one non-void invoice and one number are created; the other calls return `200 changed = false` with the same invoice |
| AC-16 | Invalid bodies, a non-completed work order, unmet mandatory items, variances without and with acknowledgment, line sums ≠ totals, and a failure injected after the invoice insert | Generate invoice is sent | Responses follow BR-17 and BR-18; unmet mandatory items save the note and mark follow-up without invoice even with `acknowledgeVariances: true`; the injected failure rolls back everything including the counter |
| AC-17 | A filtered queue, a cell starting with `=` and more than 5,000 matching rows | The export is requested | CSV columns, filename, filters, sort and escaping follow BR-21; over 5,000 → `409 export_too_large` |
| AC-18 | The EF model and the authoritative schema | The migration is generated | SA-04, SA-05 and SA-06 match exactly; the migration is generated but not applied; `database-reviewer` approves |
| AC-19 | Each role signed in to the app shell | `/invoices`, `/invoices/review` and the navigation are used | Redirect, route access, forbidden state for technician and the Invoices group with its five sub-items follow BR-22 |
| AC-20 | Queue responses with items, empty unfiltered, empty filtered and errors | The queue panel renders and filters change | Header, metrics, filters, search debounce, tabs, items, badges, paging, reset to page 1 and loading/empty/error states follow BR-23 |
| AC-21 | Details with every row status, discount, unmet mandatory items and a read-only role | The detail panel renders | Tabs, comparison table, warning, totals, verification, evidence, note counter, detail states and read-only behavior follow BR-24 and BR-26 |
| AC-22 | A detail with each payment terms value | The invoice panel renders and terms or issue date change | Fields, due-date recalculation, disabled Delivery and Payment panels with their text, summary and info follow BR-25 |
| AC-23 | Jobs with and without variances and with unmet mandatory items | Return to quote, follow-up, Generate invoice and item switching with an unsaved note are used | Confirmation only for variances, no confirmation for unmet mandatory items, messages, queue reload and selection, pending state and value retention follow BR-27 |
| AC-24 | Design 11 at 1440, 1024 and 375 px, keyboard only | The page is reviewed (user visual QA) | Layout, fidelity deviations, focus, tab semantics and non-color indicators follow BR-28 and BR-29 |
| AC-25 | Export succeeding, too large and failing | **Export CSV** is used | Download or BR-27 export messages |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Variance calculator, table-driven: labor tolerance max(15 min, 10 %), approved 0, over/under, multiple hourly lines; material variance and row statuses | AC-06, AC-07 |
| Backend unit | Terms mapping and due date; CSV cell escaping | AC-12, AC-17 |
| Backend integration | Queue membership, filters, search, tabs, pagination, metrics | AC-03, AC-04, AC-05 |
| Backend integration | Detail: comparison, totals, verification, evidence, audit trail, no sensitive fields; evidence image | AC-07, AC-08, AC-09, AC-10, AC-11, AC-12 |
| Backend integration | Review `PATCH`: note, follow-up, return to queue, audit without note text | AC-13 |
| Backend integration | Generate success effects and rollback | AC-14, AC-16 |
| Backend integration | Generate idempotency and concurrency (financial integrity) | AC-15 |
| Backend integration | Generate guards and export | AC-16, AC-17 |
| Authorization/tenant isolation | Role matrix for read and action policies; one representative cross-tenant and out-of-scope `404` per distinct path (work order, evidence, filter) | AC-01, AC-02 |
| Persistence | Model matches SA-04–SA-06; `database-reviewer` approval; migration generated, not applied | AC-18 |
| Frontend component/service | Queue page: filters, tabs, paging and states; detail rendering of statuses, verification and read-only role; invoice panel due date; action flow (variance dialog, blocked generation, success/idempotent messages, unsaved note); navigation group and redirect | AC-19, AC-20, AC-21, AC-22, AC-23, AC-25 |
| User visual QA | Fidelity, responsive layouts, keyboard and focus | AC-24 |

The backend integration count (up to 8 methods including authorization) is
at the default limit; generation idempotency/rollback is financial integrity
and requires its own evidence.

## Dependencies

- `mobile-job-completion` and `mobile-job-progress`: completion data, time
  entries, materials, evidence and acknowledgments.
- `quote-builder` and `customer-quote-approval`: frozen lines, terms, tax
  label and approved totals with selected optional lines.
- `create-work-order`: work orders and planned materials linked to quote
  lines.
- `requests-pipeline`: branch scope rule.
- `authenticated-app-shell`: shell, navigation and coming-soon routes.
- Schema amendments SA-04–SA-06 (approved 2026-10-08).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Every work order has an approved quote version with one approved `quote_responses` row (`create-work-order` invariant). |
| AS-02 | Approved response totals include selected optional lines with no discount share (`customer-quote-approval` BR-11, `quote-builder` BR-13). |
| AS-03 | Quote currencies equal the organization currency, so Completed value is a single-currency sum. |
| AS-04 | `invoice_lines.description` takes the quote line name; the line description stays on the quote. |
| AS-05 | Technician names for filters come from `technician_profiles` of the organization in branch scope. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Queue unit | Work order / visit | No | 2026-10-08: work order `completed` without active invoice; generation sets `approved_for_billing` and visits `approved` |
| OD-02 | Permissions | Owner + Accounting act / also Ops Manager | No | 2026-10-08: view per BR-01 with branch scope; act Owner and Accounting |
| OD-03 | Return to queue | Close review without action / `needs_correction` | No | 2026-10-08: saves note, clears follow-up, status unchanged |
| OD-04 | Billing basis | Fixed approved price / actual quantities | No | 2026-10-08: approved quote amounts; variances informational; adjustments disabled |
| OD-05 | Variance thresholds | — | No | 2026-10-08: labor \|diff\| > max(15 min, 10 % approved), approved 0 → any time; materials any difference or additional material |
| OD-06 | Generation with issues | Confirm / block | No | 2026-10-08: variances need confirmation; unmet mandatory items block and mark follow-up, never bypassable |
| OD-07 | Schema | Columns / table | No | 2026-10-08: SA-04–SA-07 approved; columns on `work_orders`; follow-up stores time and user, clearing empties both |
| OD-08 | Tax jurisdiction and currency | Read-only from quote / catalog | No | 2026-10-08: read-only from approved quote |
| OD-09 | Terms and dates | — | No | 2026-10-08: Due upon receipt, Net 15, Net 30; issue today−30…today; due computed |
| OD-10 | Delivery and payment panels | Disabled / hidden | No | 2026-10-08: shown disabled, unchecked |
| OD-11 | Export, sub-navigation, completion report, global search | — | No | 2026-10-08: CSV export; sub-items as BR-22; report hidden; search unchanged |
| OD-12 | Filters, metrics, page size | — | No | 2026-10-08: as BR-04–BR-06; draft keeps approved amounts, discount, taxes and currency |
| OD-13 | Copy of the return action, its message and the mobile back action | Design 11 "queue" wording / "quote" wording | No | 2026-10-08: user decision after final audit: "Return to quote", "Returned to quote.", "Back to quote"; behavior unchanged |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03, AC-04 |
| FR-03 | AC-05 |
| FR-04 | AC-06, AC-07 |
| FR-05 | AC-08 |
| FR-06 | AC-09 |
| FR-07 | AC-10, AC-11 |
| FR-08 | AC-12 |
| FR-09 | AC-13 |
| FR-10 | AC-14, AC-16 |
| FR-11 | AC-15 |
| FR-12 | AC-17 |
| FR-13 | AC-18 |
| FR-14 | AC-19, AC-20 |
| FR-15 | AC-21, AC-22 |
| FR-16 | AC-23, AC-25 |
| FR-17 | AC-24 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-08 | — → DRAFT     | Created from Design 11 with decisions OD-01–OD-12 resolved by the user |
| 2026-10-08 | DRAFT → DRAFT | Revised after validation: explicit `BillingReviewDetail` contract, `PATCH` lock and queue re-check (BR-15, BR-20), BR-08 counts only billed hourly lines |
| 2026-10-08 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-08 | APPROVED → DRAFT | Revised with user confirmation after final audit: return-action, message and mobile back copy changed to "Return to quote", "Returned to quote." and "Back to quote" (OD-13, BR-16, BR-25, BR-27, BR-28, BR-29); behavior unchanged |
