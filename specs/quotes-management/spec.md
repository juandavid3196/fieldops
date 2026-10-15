# Quotes management

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `quotes-management` |
| Type     | Full-stack          |
| Status   | AUDITED             |
| Created  | 2026-10-14          |
| Updated  | 2026-10-14          |
| Approved | 2026-10-14          |

## Context and objective

The **Quotes** sidebar item still leads to the Coming soon page. Quotes are
only reachable from a request, the customer page or a direct link. This
feature adds the `/quotes` workspace of Design 31. Staff can track every quote
from draft to customer response through server-computed metrics, tabs, search,
filters, sorting, real pagination and state-aware actions. These actions reuse
the approved Quote Builder, Customer Quote Approval and Create Work Order
rules. The feature adds no schema change and only one mutation, which already
exists: **Revise quote**. A narrow change on `/requests` supports the
`status=ready_for_quote` deep link.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Dispatcher (`dispatcher`) | Read the workspace within branch scope (BR-02); use every action valid for the quote's state, including **Continue**, **Revise quote** and the work order actions allowed by the work order Manage policy | Anything outside scope (`404` for a branch filter) |
| Operations Manager (`operations_manager`) | Read the workspace for all branches; **View quote**, **Open request**; **Create work order**, **Continue work order** and **View job** under the existing work order policies | **Continue**, **Revise quote** (quote Manage only) |
| Accounting (`accounting`), Viewer (`viewer`) | Read the workspace within scope; **View quote**, **Open request**; **View job** when the work order Read policy allows it (Viewer) | Every quote or work order mutation action |
| Technician (`technician`), unknown roles | — | Every endpoint of this feature (`403`); the forbidden state on `/quotes` |

Policies are unchanged. Quote Read = `quote-builder` BR-07 (`owner`,
`operations_manager`, `dispatcher`, `accounting`, `viewer`). Quote Manage =
`owner`, `dispatcher`. Work order Manage and Read = `create-work-order` BR-01.

## Scope

- Replace the `/quotes` Coming soon destination with Design 31. The sidebar
  **Quotes** item opens `/quotes`. The page has a breadcrumb, a header with
  **View ready requests**, four metric cards, the tabs All quotes / Drafts /
  Sent / Approved / Closed with counts, search, branch, status and date
  filters, sort, **Clear filters**, a ready-requests notice, a quotes table
  with an inline action and a row menu, and server-side pagination.
- Add `GET /quotes`, `GET /quotes/summary` and `GET /quotes/options`.
- Compute a display status per quote on the server.
- A narrow cross-cutting change on `/requests`: the `status=ready_for_quote`
  query parameter (BR-17), authorized by the user on 2026-10-14 without
  reopening the rest of `requests-pipeline`.

## Non-goals

- The quote editor, the read-only quote view, sending, resending emails,
  discarding drafts, public approval and work order creation. They keep their
  approved behavior and are only linked from here.
- Creating quotes from `/quotes`. Quotes are still created from a
  `ready_for_quote` request (`quote-builder` BR-04/BR-05).
- Any other change to `/requests` (filters, metrics, board, panel).
- The customer detail Quotes tab, which keeps its Coming soon link. The
  `/coming-soon/quotes` destination stays valid.
- Schema changes, exports, bulk actions and email sending.

## User flow

1. A Read user opens **Quotes** in the sidebar.
2. The system resolves the branch selection, loads the options, the summary
   and the first page of the **All quotes** tab, and reflects the state in the
   URL.
3. The user switches tabs, searches, filters, sorts or pages. The table, tab
   counts and pagination update from the server. Metrics and the ready-request
   count update only when the branch changes.
4. The user selects **Continue** on a draft and opens the editor, or
   **View** on any other quote and opens the read-only view.
5. The user selects **View ready requests** or **Review requests** and opens
   `/requests?status=ready_for_quote`, which shows only the Ready for quote
   column.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every endpoint must resolve the organization and membership from the session, apply the quote Read policy and branch scope, and treat out-of-scope or foreign branch ids as unknown (BR-01, BR-02). |
| FR-02 | The server must compute each quote's display status, shown version and revision state, and the frontend must display them without re-deriving them (BR-03, BR-04). |
| FR-03 | The list must support the tabs, search, branch, status and date filters, sorting and pagination, and return the tab counts (BR-05 – BR-10). |
| FR-04 | The summary must return the four metrics and the ready-request count for the selected branch (BR-11, BR-12). |
| FR-05 | The table must show the quote and revision, customer and property, request, total, sent/valid-until text, status and last update for each row (BR-13, BR-14). |
| FR-06 | The inline action and row menu must offer only the actions valid for the quote's state and the caller's permissions, as decided by the server. **Revise quote** must reuse the existing revise endpoint (BR-15, BR-16). |
| FR-07 | **View ready requests** and the notice must show the ready-request count and open `/requests` filtered to `ready_for_quote` (BR-12, BR-18). |
| FR-08 | `/requests` must honor `status=ready_for_quote` (BR-17). |
| FR-09 | The page must keep its state in the URL, normalize invalid values and render the loading, empty, error and forbidden states (BR-19, BR-20). |
| FR-10 | The page must reproduce Design 31 with the specified responsive and accessibility behavior (BR-21, BR-22). |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization. The three endpoints use the existing quote Read policy, and other roles get `403` before any read. No endpoint accepts an organization id. `canManage` comes from the quote Manage policy, and `canManageWorkOrders` and `canReadWorkOrders` come from the work order policies. The per-row actions come from these policies (BR-15). Frontend visibility is UX only. **Revise quote** keeps its own Manage authorization and checks (`quote-builder` BR-28). | Backend |
| BR-02 | Scope. A quote is visible when its request is visible (`quote-builder` BR-07, `requests-pipeline` BR-02): the request's `branch_id` is null or in the caller's scope. The quote's branch = `quotes.branch_id`, falling back to the request's `branch_id`. With a selected branch, only quotes whose branch equals it are included. With All, every visible quote is included, including those without a branch. A `branchId` of another organization, inactive, or outside scope → `404`, identical to an unknown id. No response contains data of another organization or of an out-of-scope branch. | Backend |
| BR-03 | Display status (code → label, chip tone). Expiry = the shown version's `valid_until` end of day in the organization time zone has passed (`customer-quote-approval` BR-23). `draft` (never sent: `current_version_no = 0`) → "Draft" (neutral). `sent`, not expired → `awaiting_response` "Awaiting response" (info). `clarification_requested`, not expired → `clarification_requested` "Clarification requested" (warning). `sent` or `clarification_requested` and expired, or `expired` → `expired` "Expired" (neutral). `approved` → "Approved" (success). `rejected` → "Rejected" (danger). `cancelled` → "Cancelled" (neutral). The API returns the code. The frontend maps it to its fixed label and tone and never derives a status from raw fields. | Backend (derivation), Frontend (copy) |
| BR-04 | Shown version and revision. The shown version is the version with `version_no = current_version_no` (the last sent version). When `current_version_no = 0`, it is the mutable draft version. `revisionNo` = max(`current_version_no`, 1), displayed "Revision <n>". The total, currency, `sent_at` and `valid_until` come from the shown version. `revisionInProgress` = a mutable version exists and `current_version_no ≥ 1`. It is then displayed below the revision as "Revision <n+1> in progress". The display status is unchanged. | Backend |
| BR-05 | Tabs, by display status. **Drafts** = `draft`. **Sent** = `awaiting_response`, `clarification_requested`. **Approved** = `approved`. **Closed** = `rejected`, `expired`, `cancelled`. **All quotes** = the union of the four, so its count equals their sum. Tab counts apply branch, search and date, but not the status filter. The mockup numbers are illustrative only. | Backend |
| BR-06 | Status filter. Allowed codes per tab: Drafts = `draft`. Sent = `awaiting_response`, `clarification_requested`. Approved = `approved`. Closed = `rejected`, `expired`, `cancelled`. All = every BR-03 code. A code outside the tab's set → `400 errors.status`. The dropdown offers "All statuses" plus the tab's codes by label. Changing tab resets the status to All statuses. | Both |
| BR-07 | Date filter on `quotes.created_at` in the organization time zone (`ref` = now): `any` "Any date" (default), `today` "Today", `7d` "Last 7 days", `30d` "Last 30 days", `90d` "Last 90 days". A window of N days covers today and the previous N − 1 local days. An unknown code → `400 errors.date`. | Both |
| BR-08 | Search `q`. The term is trimmed; empty means no search; more than 100 characters → `400 errors.q`. Matching is case-insensitive. A quote matches when the term, without a leading `#`, is `<quote prefix>-<digits>` or plain digits equal to `quote_number`, or is `REQ-<digits>` equal to the request's `request_number`. It also matches when the term is contained in the customer display name, the request `guest_name`, the property name, `address_line1` or `city`, or the request title sources (catalog item name, category name). The frontend sends the term 300 ms after the last keystroke. | Both |
| BR-09 | Sort `sort`. `updated` (default) "Recently updated" = `quotes.updated_at` descending. `newest` "Newest" = `created_at` descending. `oldest` "Oldest" = `created_at` ascending. `total` "Highest total" = shown version `total` descending. `expiring` "Expiring soonest" = shown version `valid_until` ascending; quotes without one (drafts without a date) go last. Ties fall back to `quote_number` descending. An unknown value → `400 errors.sort`. | Both |
| BR-10 | Pagination. `page` ≥ 1 and `pageSize` 1 – 100 (`400` otherwise). The API default is 25. The UI always sends `pageSize` and offers 10 (default), 25 and 50 rows per page. A page past the last returns empty `items` with the real `total`, and the frontend then requests the last page (`replaceUrl`). The footer shows "<from>–<to> of <total>", **Rows per page**, previous and next buttons, and page numbers. | Both |
| BR-11 | Summary metrics, for the effective branch set only (no tab, search, status or date). Time zone: the organization's. Amounts are the sum of the shown version `total` over the counted quotes whose version currency equals the organization currency. Quotes in another currency are counted but not summed. Values use the organization currency. **Drafts**: `draft` quotes, caption "<amount> total". **Awaiting response**: `awaiting_response` + `clarification_requested` quotes, caption "<amount> sent". **Approved**: `approved` quotes whose approval `quote_responses.responded_at` falls in the current calendar month, value = that count, caption "<amount> this month", using the approval response `total` (it includes selected optional items). **Needs attention**: distinct `awaiting_response` or `clarification_requested` quotes that are `clarification_requested` or whose `valid_until` is between today and today + 7 days inclusive, caption "Questions or expiring soon". Cards are not interactive. | Backend |
| BR-12 | Ready requests. `readyRequests` = the count of visible `ready_for_quote` service requests in the effective branch set. A null-branch request counts only with All. Requests that already have a draft quote are included, so the count matches the destination. | Backend |
| BR-13 | Row content. **Quote**: "<Q-n>" as a link to `/quotes/:id`, with "Revision <n>" below and, when BR-04 applies, "Revision <n+1> in progress". **Customer & property**: the customer display name, else the request `guest_name`. Below it: the property name, followed by " · <postal code> <city>" when the property has an address (postal code omitted when null). "No property" when there is no property. **Request**: "REQ-<n>" as a link to `/requests?request=<requestId>`, with the request title (`requests-pipeline` BR-04) below. **Total**: the shown version total in its currency. **Sent / valid until** (dates "MMM d", plus ", yyyy" in another year, in the organization time zone): `draft` "Draft · Not sent"; `awaiting_response` and `clarification_requested` "Sent <sent_at> · Valid until <valid_until>"; `approved` "Approved <approval responded_at>"; `rejected` "Rejected <rejection responded_at>"; `expired` "Expired <valid_until>"; `cancelled` "Cancelled <updated_at>". **Status**: BR-03 chip with a dot and the label. | Both |
| BR-14 | Updated. Updated = `quotes.updated_at`. Every staff and customer transition already sets it. It is displayed relative to now in the organization time zone: < 1 min "Just now"; < 60 min "<n> min ago"; same day "Today"; previous day "Yesterday"; otherwise "MMM d" (", yyyy" in another year). The full date and time is the accessible label and the tooltip. | Both |
| BR-15 | Row actions (server-computed `actions`, in this order). `view` **View quote** → `/quotes/:id` (always). `continue_draft` **Continue draft** → `/quotes/:id/edit` (quote Manage, `draft`). `continue_revision` **Continue revision** → `/quotes/:id/edit` (quote Manage, `revisionInProgress`). `revise` **Revise quote** (quote Manage; status `sent`, `clarification_requested`, `rejected` or `expired`, including display-expired; no mutable version). `create_work_order` **Create work order** → `/quotes/:id/work-order` (work order Manage, `approved`, no work order). `continue_work_order` **Continue work order** → `/quotes/:id/work-order` (work order Manage, `approved`, work order `draft`). `view_job` **View job** → `/jobs/:workOrderId` (work order Read, work order status ≠ `draft`). `open_request` **Open request** → `/requests?request=<requestId>` (always). Inline button: **Continue** (= `continue_draft`) when present, else **View** (= `view`). The menu holds the remaining actions. Its button opens a menu with exactly those items. Every destination keeps its own authorization. | Backend (eligibility), Frontend (targets) |
| BR-16 | **Revise quote** from the list calls the existing `POST /quotes/{id}/revise { updatedAt }` with the row's `updatedAt`, disables the item while pending and, on success, navigates to `/quotes/:id/edit` (`quote-builder` BR-28). On `409 quote_changed` it shows the existing `quote_changed` message as a toast and reloads the list and summary. On `403` or `404` it shows "This quote isn't available." and reloads the list. No confirm dialog is added. | Frontend |
| BR-17 | Requests deep link (cross-cutting). When `/requests` has `status=ready_for_quote`, the board shows only the Ready for quote column (the mobile list shows only that group) and a removable chip "Ready for quote". Removing the chip drops the parameter, and all columns return. Any other `status` value is removed with `replaceUrl`. The parameter combines with `request` (the panel still opens), and the existing page-state filters keep applying to the column. Metrics, the backend and the other `requests-pipeline` behavior are unchanged. | Frontend |
| BR-18 | Header and notice. **View ready requests** shows the `readyRequests` badge (hidden at 0) and opens `/requests?status=ready_for_quote`. It is shown to every Read role. The notice "<n> service request(s) are ready for a quote." ("1 service request is ready for a quote." when n = 1) with the link **Review requests** (same destination) appears when `readyRequests` > 0. Its close button hides it for the rest of the browser tab session. The dismissal applies only to this viewer and is not persisted on the server. | Frontend |
| BR-19 | URL state. `tab` (`all` default, `drafts`, `sent`, `approved`, `closed`), `q`, `branch` (absent = default), `status`, `date` (absent = `any`), `sort` (absent = `updated`), `page` (absent = 1), `pageSize` (absent = 10). Reload and back restore the view. Unknown or invalid values are replaced by their defaults with `replaceUrl` before any request. A `404` branch resets to the default branch (`replaceUrl`). Changing the search, a filter, the sort, the tab or the page size resets to page 1. **Clear filters** (sliders button) resets search, status and date (not tab, branch or sort). Its badge counts the active ones, and it is disabled when none are active. | Frontend |
| BR-20 | Branch selector. "All branches" appears only with ≥ 2 in-scope branches, followed by each active in-scope branch by name. Default: All with ≥ 2, else the only branch. With no branch in scope, it shows "No branches available." and makes no summary or list request. | Both |
| BR-21 | Layout (Design 31). Breadcrumb "Operations" (plain text) / "Quotes". Title "Quotes", subtitle "Create, send and track customer estimates.". From 1280 px: the header action is right-aligned, the four metrics sit in one row, the filters sit in one row (search, branch, status, date, sort, Clear filters), and the full table shows. From 768 to 1279 px: metrics 2 × 2, filters wrap, and the table scrolls horizontally inside its card. Below 768 px: metrics one per row, header action full width, filters one per row, the table scrolls inside its card, and the pagination wraps. No page-level horizontal scroll at 375 px. The design's sample data is never copied. | Frontend |
| BR-22 | Accessibility. Tabs are a tablist whose names include the count. Status is conveyed by text, never color alone. The inline button's name is "Continue <Q-n>" or "View <Q-n>", and the menu button's name is "More actions for <Q-n>". Filters and sort have accessible labels. Metric icons are decorative. The notice is a status region, and its close button is named "Dismiss". Result changes are announced politely ("<total> quotes"). Every control has a visible focus indicator. Touch targets are ≥ 44 px below 768 px. | Frontend |
| BR-23 | Query efficiency. The list and the summary each run a bounded number of database queries, independent of page size and the number of rows (no per-row queries). Counts, filters, sorting and paging run in the database. | Backend |

## States and transitions

This feature defines no new transition. **Revise quote** reuses
`quote-builder` BR-28 unchanged. Quote statuses keep their transitions in
`quote-builder` and `customer-quote-approval`. The `expired` display status is
derived and is never written by this feature.

## Data and persistence impact

- Tables and columns read (from `docs/database/fieldops-schema.sql`):
  - `quotes`: `id`, `organization_id`, `branch_id`, `request_id`,
    `customer_id`, `property_id`, `quote_number`, `status`,
    `current_version_no`, `approved_version_id`, `created_at`, `updated_at`.
  - `quote_versions`: `id`, `quote_id`, `version_no`, `total`, `currency`,
    `valid_until`, `sent_at`, `is_immutable`.
  - `quote_responses`: `quote_version_id`, `response`, `responded_at`, `total`.
  - `service_requests`: `id`, `branch_id`, `request_number`, `status`,
    `customer_id`, `guest_name`, `category_id`, `catalog_item_id`,
    `description`.
  - `catalog_items.name`, `service_categories.name` (request title).
  - `customers.display_name`.
  - `properties`: `name`, `address_line1`, `city`, `postal_code`.
  - `work_orders`: `id`, `quote_version_id`, `status`.
  - `branches`: `id`, `name`, `is_active`.
  - `organizations`: `timezone`, `currency`, `quote_prefix`.
  - `organization_users.is_all_branches` and `organization_user_branches`, for scope.
- Writes: none, except the existing revise operation when invoked.
- Schema amendments required: None.

## Tenant isolation and authorization

- Organization context: resolved from the session membership; every query
  filters by it.
- Client-provided organization identifiers: none accepted. `branchId` is
  authorized against the caller's scope (BR-02); a foreign or out-of-scope
  value → `404`. Revise authorizes its quote id as in `quote-builder` BR-07.
- Quotes, requests, customers, properties and work orders of another
  organization or of an out-of-scope request never appear in items, counts,
  metrics, `readyRequests` or options.
- Quote Read policy for all three endpoints. Quote Manage and the work order
  policies only affect flags and `actions`.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/quotes/options` | — | `200 { branches: [{ id, name }], organizationTimezone, currency, canManage, canManageWorkOrders, canReadWorkOrders }` (active, in scope, by name) | `403` | Quote Read |
| GET | `/quotes/summary` | Query: `branchId?` | `200 { currency, drafts: { count, amount }, awaitingResponse: { count, amount }, approvedThisMonth: { count, amount }, needsAttention: { count }, readyRequests }` | `400` (malformed id) · `403` · `404` (branch) | Quote Read |
| GET | `/quotes` | Query: `tab?` (`all` default, `drafts`, `sent`, `approved`, `closed`), `q?`, `branchId?`, `status?`, `date?` (`any` default, `today`, `7d`, `30d`, `90d`), `sort?` (`updated` default, `newest`, `oldest`, `total`, `expiring`), `page?` (1), `pageSize?` (25) | `200 { items: QuoteRow[], total, counts: { all, drafts, sent, approved, closed } }` | `400` (`tab`, `q`, `status`, `date`, `sort`, `page`, `pageSize`, malformed id; ProblemDetails `errors.<field>`) · `403` · `404` (branch) | Quote Read |
| POST | `/quotes/{id}/revise` | Unchanged (`quote-builder`) | Unchanged | Unchanged | Quote Manage |

`QuoteRow` = `{ id, displayNumber, revisionNo, revisionInProgress, customerName,
property: { name, postalCode | null, city | null } | null, request: { id,
displayNumber, title }, branch: { id, name } | null, total, currency, status
(BR-03 code), sentAt | null, validUntil | null (date), respondedAt | null,
updatedAt, createdAt, workOrderId | null, actions: ("view" | "continue_draft" |
"continue_revision" | "revise" | "create_work_order" | "continue_work_order" |
"view_job" | "open_request")[] }`. `respondedAt` is the approval or rejection
`responded_at` of the shown version. Instants are ISO 8601 with offset. Every
response is `no-store`. `total` is the count of the filtered set, and `items`
holds the requested page. `GET /quotes/summary` and `/options` do not collide
with `GET /quotes/{id}` (guid constraint).

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/quotes` options | Metric and table skeletons; filters disabled | — | Full-page "We couldn't load quotes. Try again." with **Retry**; neither summary nor list is requested until options load | `403`: forbidden state, no further calls | Branch selector, currency and flags ready (BR-20) |
| `/quotes` metrics and header | Four card skeletons; header badge and notice hidden | — | "We couldn't load quote metrics. Try again." with **Retry** in the metrics row; badge and notice hidden; the table keeps working | `403`: page forbidden state | BR-11 values, BR-18 badge and notice |
| `/quotes` table | Row skeletons; filters stay usable; the previous rows are replaced | All tab, no search or filters, total 0: "No quotes yet. Quotes are created from requests that are ready for a quote.". Otherwise: "No quotes match the current view." with **Clear filters** when any filter is active | "We couldn't load quotes. Try again." with **Retry** | `403`: forbidden state, no further calls | BR-13 – BR-15 rows and BR-10 footer |
| Revise from the menu | Item disabled while pending | — | BR-16 toasts | — | Navigates to the editor |
| No branches | — | "No branches available." | — | — | — |
| `/requests?status=ready_for_quote` | Existing board loading | Existing empty column copy | Existing error | Existing | Only the Ready for quote column with the "Ready for quote" chip (BR-17) |

- Mockups: `design/assets/31-design.png` (Approved). The sidebar and top bar
  are the existing app shell. The sample rows and numbers are illustrative.
- Responsive and accessibility: BR-21, BR-22.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid URL value (tab, status for tab, date, sort, page, pageSize, q > 100) | Replaced by the default with `replaceUrl` before the request; a server `400` resets the offending parameter the same way | None |
| `branch` foreign, inactive or out of scope | `404`; the page resets to the default branch (`replaceUrl`) | None |
| Page past the last | Empty items; the page requests the last page (`replaceUrl`) | None |
| Role without Read | `403`; forbidden state | None |
| Options request fails (network or `5xx`) | Full-page error with **Retry**; no summary or list request | None |
| Network or `5xx` on summary or list | Metrics or table error state with **Retry** | None |
| Revise `409 quote_changed` | Existing `quote_changed` toast; list and summary reload | None |
| Revise `403` / `404` | "This quote isn't available."; list reloads | None |
| `/requests?status=` with another value | Parameter removed with `replaceUrl` | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | Users with each role | They call the three endpoints and open `/quotes` | Owner, Dispatcher, Operations Manager, Accounting and Viewer get `200`; Technician and unknown roles get `403` and the forbidden state with no further calls; `canManage`, `canManageWorkOrders` and `canReadWorkOrders` follow the existing policies |
| AC-02 | Quotes in two organizations, in two branches and on a null-branch request, and a Dispatcher scoped to one branch | The Dispatcher lists, counts and summarizes, and passes the other branch's or organization's id | Only visible quotes appear in items, counts, metrics and `readyRequests`; the null-branch quote appears only under All; the foreign and out-of-scope `branchId` return `404`, identical to an unknown id; options list only in-scope active branches |
| AC-03 | Quotes in every stored status, sent quotes past and before `valid_until` end in the organization time zone, and a sent quote with a revision draft | The list is returned | Each row's `status`, `revisionNo`, `revisionInProgress` and shown-version total, currency and dates follow BR-03 and BR-04 |
| AC-04 | Quotes of every display status | Each tab is listed | Membership and counts follow BR-05; All equals the sum of the four tabs; expired-by-date quotes are in Closed |
| AC-05 | Quotes of every display status | The status filter is applied per tab, including a code outside the tab | Only matching rows are returned; tab counts ignore the status filter; an out-of-tab code returns `400 errors.status`; the dropdown offers only the tab's statuses and resets on tab change |
| AC-06 | Quotes created around the boundaries of today and of 7, 30 and 90 days in the organization time zone | Each date filter is applied, including an unknown code | Rows and counts follow BR-07; an unknown code returns `400 errors.date` |
| AC-07 | Quotes with distinct numbers, request numbers, customers, guest names, properties, cities and request services | Searching "Q-7", "#q-7", "7", "REQ-12", part of a guest name, part of a city and part of a service name, and a 101-character term | Matches follow BR-08 case-insensitively; the 101-character term returns `400 errors.q`; the frontend debounces by 300 ms |
| AC-08 | Quotes with distinct updated, created, total and expiry values, including drafts without a date | Each sort is requested | Order follows BR-09, including nulls last for `expiring` and the tie-breaker; an unknown sort returns `400` |
| AC-09 | 23 visible quotes | Pages are requested with sizes 10, 25 and 50, a page past the last, and invalid page values | Items, `total` and the footer "<from>–<to> of <total>" follow BR-10; past the last page the UI moves to the last page; invalid values return `400` |
| AC-10 | Drafts, awaiting and clarification quotes, approvals this month and last month (with selected optional items), quotes expiring today, in 7 and in 8 days, a quote in another currency, and a second branch | The summary is requested for one branch and for All | Counts and amounts follow BR-11 in the organization time zone and currency; the approval amount uses the response total; the other-currency quote is counted but not summed; the tab, search, status and date do not change them |
| AC-11 | `ready_for_quote` requests in and out of scope, one with a draft quote, one with a null branch, and requests in other statuses | The summary is requested for one branch and for All | `readyRequests` follows BR-12 |
| AC-12 | Rows of every display status, with and without customer, guest name, property and postal code, and dates in the current and another year | The table renders | Quote, revision, customer and property, request link and title, total in the version currency, Sent / valid until text and status chip follow BR-13 |
| AC-13 | Quotes updated just now, minutes ago, earlier today, yesterday and in another year | The table renders | The Updated text and its accessible full date follow BR-14 |
| AC-14 | Quotes in each state (never-sent draft, sent with and without a revision draft, clarification, display-expired, rejected, approved with no work order, with a draft work order and with a created work order, cancelled), viewed by Owner, Dispatcher, Operations Manager, Accounting and Viewer | The rows are returned and the menus opened | `actions` match BR-15 exactly per state and role; the inline button is **Continue** only with `continue_draft`, otherwise **View**; Accounting never sees work order or mutation actions |
| AC-15 | Inline buttons, menu items and the quote and request links | Each navigation is activated | They open `/quotes/:id`, `/quotes/:id/edit`, `/quotes/:id/work-order`, `/jobs/:workOrderId` and `/requests?request=<id>` per BR-13 and BR-15 |
| AC-16 | A sent quote, then the same quote changed by another user, then a quote that became inaccessible | **Revise quote** is chosen from the menu | The first call reuses the revise endpoint with the row's `updatedAt` and opens the editor on a new mutable version; the stale one shows the `quote_changed` toast and reloads list and summary with no data change; the inaccessible one shows "This quote isn't available." and reloads |
| AC-17 | `readyRequests` of 0, 1 and 2 | The page renders and the notice is dismissed, the page reloaded and a new browser session started | The badge is hidden at 0; the notice copy follows BR-18 (singular at 1) and is hidden at 0; after dismissal it stays hidden in the session and returns in a new session; **View ready requests** and **Review requests** open `/requests?status=ready_for_quote` |
| AC-18 | `/requests` opened with `status=ready_for_quote`, with it plus `request=<id>`, and with `status=new` | The page loads, the chip is removed and back is used | Only the Ready for quote column (or mobile group) shows with the chip, and page filters still apply; the panel opens with `request`; removing the chip restores all columns; `status=new` is removed with `replaceUrl`; metrics are unchanged |
| AC-19 | A URL with every parameter set, another with invalid values, and a `404` branch | The page is loaded, reloaded and navigated back | Valid state is restored; invalid values and the `404` branch are replaced by defaults with `replaceUrl` before or after the request per BR-19; changes reset to page 1; **Clear filters** resets search, status and date, shows their count and is disabled with none |
| AC-20 | Organizations with one, two and zero in-scope branches | The page loads | The branch selector and defaults follow BR-20; with zero, "No branches available." shows and neither summary nor list is requested |
| AC-21 | Loading, empty (no quotes), filtered empty, options failure, list failure and summary failure | The page renders each case | The UI table states and copy appear; an options failure shows the full-page error and requests neither summary nor list; a summary failure shows its error, hides the badge and notice, and the table still works; **Retry** reloads only the failed part |
| AC-22 | The sidebar **Quotes** item, and Design 31 at 1280, 1024 and 375 px | The item is used and the page is reviewed | The item opens `/quotes`; the layout follows BR-21 with no page-level horizontal scroll at 375 px; accessibility follows BR-22 |
| AC-23 | A page of 50 rows with several versions, responses and work orders per quote | The list and summary are requested | Each runs the same bounded number of queries as for one row (BR-23) |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Table-driven display status, shown version and revision state, and action eligibility by status, revision, work order state and policy flags (BR-03, BR-04, BR-15), only if implemented as pure functions. | AC-03, AC-14 |
| Backend integration | Grouped list flow: tabs and counts, status per tab and out-of-tab `400`, date windows, search, sorts, pagination and validation errors. Summary metrics, currency exclusion and `readyRequests`. Row content and `actions` per state. Bounded query count. Expected 4 – 6 methods. | AC-03 – AC-14, AC-23 |
| Authorization/tenant isolation | One test covering role `403`s and flags, a Dispatcher's scope over items, counts, metrics and `readyRequests` (including a null-branch request), and one representative cross-organization and out-of-scope `branchId` `404`. | AC-01, AC-02, AC-11 |
| Frontend component/service | URL state ↔ request mapping, normalization, page reset and **Clear filters**. Inline button and menu built from `actions` with BR-15 targets, and the status label map. Revise success and `409`. Notice and badge, including session dismissal. Table and metrics states. `/requests` `status=ready_for_quote` column filter and normalization. Expected 4 – 6 methods. | AC-05, AC-09, AC-14 – AC-21 |
| Browser (final audit) | User-supplied visual QA of Design 31 at 1280, 1024 and 375 px, the sidebar link and the `/requests` deep link. No agent-driven browser automation. | AC-12, AC-13, AC-18, AC-22 |

## Dependencies

- `quote-builder` (Read/Manage policies, BR-07 visibility, BR-28 revise, BR-30 view, editor route): implemented.
- `customer-quote-approval` (BR-18/BR-23 expiry, `quote_responses`): implemented.
- `create-work-order` (BR-01 policies, BR-04 quote actions, `/quotes/:id/work-order`): implemented.
- `requests-pipeline` (BR-02 scope, BR-04 title, `/requests` page and `request` parameter): implemented.
- `jobs-management`: precedent for the workspace structure, contracts and URL state.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Chip tones reuse the existing status chip tokens; no new colors are introduced. |
| AS-02 | Amounts use the existing currency formatting of the quote pages. |
| AS-03 | The notice dismissal uses session-scoped browser storage (per tab session) and degrades to "not dismissed" when storage is unavailable. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Display status and tabs | BR-03 / BR-05; cancelled in Closed · excluded | No | BR-03 / BR-05 with cancelled in Closed (user 2026-10-14) |
| OD-02 | Version shown and revision in progress | BR-04 | No | Accepted (user 2026-10-14) |
| OD-03 | Metrics | BR-11, Approved = this month | No | Accepted (user 2026-10-14) |
| OD-04 | Date filter and sort | BR-07, BR-09 | No | Accepted (user 2026-10-14) |
| OD-05 | Row actions | Navigation + reused Revise · navigation only | No | Navigation + reused Revise; Resend and Discard stay on view/editor (user 2026-10-14) |
| OD-06 | Ready requests destination | `/requests?status=ready_for_quote` · plain `/requests` | No | Deep link with a narrow `/requests` change, without reopening `requests-pipeline` (user 2026-10-14) |
| OD-07 | Permissions | Existing quote and work order policies | No | Accepted (user 2026-10-14) |
| OD-08 | Customer detail Quotes tab | Keep Coming soon · link `/quotes` | No | Keep Coming soon (user 2026-10-14) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02 |
| FR-02 | AC-03 |
| FR-03 | AC-04, AC-05, AC-06, AC-07, AC-08, AC-09, AC-23 |
| FR-04 | AC-10, AC-11, AC-23 |
| FR-05 | AC-12, AC-13 |
| FR-06 | AC-14, AC-15, AC-16 |
| FR-07 | AC-11, AC-17 |
| FR-08 | AC-18 |
| FR-09 | AC-19, AC-20, AC-21 |
| FR-10 | AC-22 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-14 | — → DRAFT     | Created |
| 2026-10-14 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-14 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-14 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS. |
