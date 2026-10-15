# Notifications center

| Field    | Value                  |
| -------- | ---------------------- |
| Feature  | `notifications-center` |
| Type     | Full-stack             |
| Status   | AUDITED                |
| Created  | 2026-10-15             |
| Updated  | 2026-10-15             |
| Approved | 2026-10-15             |

## Context and objective

The shell bell and the Administration **Notifications** link still lead to the
Coming soon page. The `notifications` table exists, but nothing writes to it.
An audit on 2026-10-15 found no caller of `Notification.Create`. This feature
adds the `/notifications` inbox of Design 32 for internal staff. It writes
real `in_app` notifications for a closed set of existing business events
(BR-05), and connects the bell to the caller's real unread count. Every list,
count and link is re-authorized on read against the caller's current role and
branch scope. Every destination keeps its own authorization.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Internal shell roles: Owner (`owner`), Operations Manager (`operations_manager`), Dispatcher (`dispatcher`), Accounting (`accounting`), Viewer (`viewer`) | Read their own visible notifications (BR-03); use the counters, search, filters and pagination; mark their own notifications read or unread; **Mark all as read**; follow a link when they may read the target entity (BR-12) | Read or change another user's notifications (`404`); see notifications that BR-03 hides |
| Technician (`technician`), unknown roles | — | Every endpoint of this feature (`403`); they receive no notification rows (BR-04) and see no sidebar item or bell count (BR-17) |
| Customers, portal contacts, anonymous users | Trigger events (BR-05) | Every endpoint of this feature (`401`/`403`) |

New policy **Notification Inbox** = `owner`, `operations_manager`,
`dispatcher`, `accounting`, `viewer`. Existing policies are unchanged.

## Scope

- Replace the `/coming-soon/notifications` destination with the `/notifications` page of Design 32. The page has a breadcrumb, a header with **Mark all as read**, Unread and Today cards, the All notifications and Unread tabs with counts, search, type, branch and date filters, **Clear filters**, a list grouped into Today and Earlier with a row menu, and server-side pagination.
- Add the `GET /notifications`, `GET /notifications/summary`, `GET /notifications/options`, `POST /notifications/{id}/read`, `POST /notifications/{id}/unread` and `POST /notifications/read-all` endpoints.
- Write notification rows for the events in BR-05, in the same transaction as each event.
- Bell in the shell top bar: it opens the notifications popover of Design 33 without changing route (BR-26) and shows the unread count, refreshed by a 30-second poll of the summary while the tab is visible (BR-16). On `/notifications`, a notice offers to reload the list when new activity arrives (BR-25).
- Sidebar item **Notifications** for internal roles. The Administration column link **Notifications** points to `/notifications` (BR-17).
- One schema amendment: an index (Data and persistence impact).

## Non-goals

- Notification preferences, email, SMS, push, WebSockets, server-sent events, polling of the list itself (only `GET /notifications/summary` is polled, BR-16), deletion, archiving and template configuration.
- Time-based reminders, such as visits needing attention or quotes expiring soon. No scheduled process is added. The design's "Visit requires attention" and "Quote expiring soon" examples are not implemented.
- Technician notifications and any change to the technician shell. Its bell stays at `/today/soon/notifications`.
- Customer or portal notifications (`recipient_contact_id` stays unused).
- Notifications for events not listed in BR-05, including `LogResponse` request messages, internal notes, request status changes and work order edits.
- Backfilling notifications for past events, or creating them from seed or sample data.
- New columns on `notifications` or changes to any other table.

## User flow

1. An event in BR-05 happens. In the same transaction, the system writes one unread `in_app` notification for each eligible recipient (BR-04).
2. A staff user sees the unread count on the shell bell, which refreshes every 30 s while the tab is visible. Selecting the bell opens a popover with the five latest notifications (BR-26), and **View all notifications** or the sidebar opens `/notifications`.
3. The system resolves the caller's visibility (BR-03). It loads the options, the summary and the first page of **All notifications**, and reflects the state in the URL.
4. The user switches tabs, searches, filters or pages. The list and the tab counts update from the server.
5. The user selects a notification. The system marks it read and navigates to the authorized target. A notification without a link is only marked read.
6. The user marks a single notification read or unread from its menu, or selects **Mark all as read**. The counters and the bell update immediately.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every endpoint must resolve the organization and membership from the session and apply the Notification Inbox policy. It must accept no organization or user id, and return only notifications addressed to the caller that are visible under BR-03. |
| FR-02 | Each BR-05 event must write exactly one unread `in_app` notification per eligible recipient (BR-04). The write must happen in the event's transaction, with the snapshot content of BR-06 – BR-08, and never for replays, failed operations or sample data (BR-09). |
| FR-03 | The list must support the tabs, search, type, branch and date filters, newest-first order and pagination, and return the tab counts (BR-10, BR-11, BR-13). |
| FR-04 | The summary must return the Unread and Today counts used by the cards and the bell (BR-14). |
| FR-05 | Each row must show the read state, type icon, title, description, related entity, branch and relative time, grouped into Today and Earlier (BR-15). |
| FR-06 | Selecting a row must mark it read and open its server-authorized link. The row menu must toggle read/unread, and **Mark all as read** must mark every visible unread notification (BR-12, BR-18, BR-19). |
| FR-07 | The shell bell must open the notifications popover (BR-26) and show the caller's unread count, refreshed by a 30-second poll that pauses while the tab is hidden. The sidebar and the Administration links must open `/notifications` for internal roles only (BR-16, BR-17, BR-26). |
| FR-08 | The page must keep its state in the URL, normalize invalid values, render the loading, empty, error and forbidden states, and offer a reload notice when new activity arrives (BR-20, BR-21, BR-25). |
| FR-09 | The page must reproduce Design 32 with the specified responsive and accessibility behavior (BR-22, BR-23). |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization. Every endpoint requires the Notification Inbox policy. Other roles get `403` before any read or write. No endpoint accepts an organization id or a recipient id. A notification id that is unknown, belongs to another organization, belongs to another recipient, is not `in_app`, or is hidden by BR-03 → `404 notification_not_found`, with identical responses. Frontend visibility is UX only. | Backend |
| BR-02 | Type catalog (code → label, icon meaning). `request` "Requests" (document). `message` "Messages" (chat bubble). `quote` "Quotes" (check for approved, x for declined, question for clarification). `visit` "Visits" (calendar). `job` "Jobs" (wrench). `invoice` "Invoices" (receipt). `payment` "Payments" (dollar). Each `template_code` in BR-05 maps to exactly one type. The API returns the type code and the event code. The frontend maps them to fixed labels and icons. | Both |
| BR-03 | Visibility on read (re-authorization). A notification is visible to the caller only when all of these hold: its `organization_id` is the session organization, its `recipient_user_id` is the caller, its `channel = 'in_app'`, the caller's **current** role is in BR-04's role set for its event, and its branch (payload `branchId`) is null or in the caller's **current** branch scope (`is_all_branches`, or an `organization_user_branches` row for an active branch). A notification that fails any condition is excluded from every list, tab count, summary count, bell count, search result, page and total, and from **Mark all as read**, and its id resolves to `404`. If access returns later, the notification reappears in its stored read state. | Backend |
| BR-04 | Recipients. At event time, the recipients are every active (`status = 'active'`) membership of the event's organization that meets three conditions: its role is in the event's role set below, the event's branch is null or in its branch scope, and its user is not the acting user. Role sets: `request.created` and `customer.message_sent` → `owner`, `dispatcher`. `quote.approved`, `quote.rejected` and `quote.clarification_requested` → `owner`, `dispatcher`. `visit.assigned`, `visit.on_the_way`, `visit.started` and `visit.paused` → `owner`, `operations_manager`, `dispatcher`. `job.completed` → `owner`, `operations_manager`, `dispatcher`, `accounting`. `invoice.sent`, `invoice.bank_transfer_reported` and `payment.received` → `owner`, `accounting`. Technicians and customer contacts never receive rows. When there are zero recipients, no row is written. | Backend |
| BR-05 | Events (`template_code`, source, entity, branch). `request.created`: a service request is created through the public form, the customer portal or staff creation. The entity is the request and the branch is the request's `branch_id` (nullable). `customer.message_sent`: a portal contact successfully submits **Send a message**. The entity is the customer and the branch is `customers.branch_id`. `quote.approved`, `quote.rejected`, `quote.clarification_requested`: the customer response is recorded through the approval link. The entity is the quote and the branch is `quotes.branch_id`, else the request's `branch_id`. `visit.assigned`: a visit is dispatched or reassigned to a technician. `visit.on_the_way`, `visit.started`, `visit.paused`: the technician starts travel, starts the job or pauses. For the four visit events, the entity is the visit and the branch is its work order's `branch_id`. `job.completed`: a visit completion moves its work order to `completed`. The entity is the work order and the branch is the work order's `branch_id`. `invoice.sent`: an invoice is sent (not on resend). `invoice.bank_transfer_reported`: a customer reports a bank transfer. `payment.received`: a payment is recorded manually or an online payment succeeds. For the three invoice and payment events, the entity is the invoice and the branch is `invoices.branch_id`. No other event writes notifications. | Backend |
| BR-06 | Stored content (snapshot, never re-rendered). `template_code` = the BR-05 event code. `channel = 'in_app'`. `status = 'sent'`. `scheduled_at`, `sent_at` and `created_at` = the event time. `recipient_contact_id` = null. `subject` = the title (≤ 240 characters). `payload` = `{ "type", "description", "entityKind", "entityId", "entityLabel", "secondaryLabel", "branchId" }`. `entityKind` is one of `request`, `customer`, `quote`, `visit`, `work_order`, `invoice`. `secondaryLabel` and `branchId` may be null. The payload never contains message text, contact email or phone, tokens, internal notes, costs or margins. | Backend |
| BR-07 | Copy (title → description). Names are the customer display name, else the request guest name. Amounts are in the document currency. `request.created`: "New service request" → "<name> submitted REQ-<n>.". `customer.message_sent`: "Customer sent a message" → "<contact full name> from <customer> sent a message from the customer portal.". `quote.approved`: "Quote <Q-n> was approved" → "<customer> approved the quote for <approved total>.". `quote.rejected`: "Quote <Q-n> was declined" → "<customer> declined the quote.". `quote.clarification_requested`: "Question about quote <Q-n>" → "<customer> requested a clarification.". `visit.assigned`: "Visit assigned" → "<WO-n> was assigned to <technician> for <MMM d, h:mm a>.". `visit.on_the_way`: "Technician on the way" → "<technician> is on the way to <WO-n>.". `visit.started`: "Visit started" → "<technician> started <WO-n>.". `visit.paused`: "Visit paused" → "<technician> paused <WO-n>.". `job.completed`: "Job completed" → "<technician> completed <WO-n>.". `invoice.sent`: "Invoice <INV-n> was sent" → "<INV-n> was sent to <customer> for <total>.". `invoice.bank_transfer_reported`: "Bank transfer reported" → "<customer> reported a bank transfer of <amount> for <INV-n>.". `payment.received`: "Payment received" → "Payment of <amount> was recorded for invoice <INV-n>.". Document numbers use each document's existing display format. Dates and times use the organization `timezone`. | Backend |
| BR-08 | Entity labels. Request: `entityLabel` "REQ-<n>". Customer: the display name. Quote: "<Q-n>", with `secondaryLabel` "REQ-<n>" when the quote has a request. Visit: "<WO-n>", with `secondaryLabel` "Visit <visit_number>". Work order: "<WO-n>". Invoice: "<INV-n>". Other `secondaryLabel` values are null. | Backend |
| BR-09 | Integrity. Notification rows are added in the same database transaction as the event's state change and audit row. If the event fails or rolls back, no notification exists. A notification failure fails the event, as any persistence failure does. Idempotent replays, such as payment webhooks already processed, repeated link submissions, or retries that change no state, write no notification. Seeders, sample data and fixtures outside tests never write notifications. | Backend |
| BR-10 | Tabs. `all` = every visible notification. `unread` = visible with `status ≠ 'read'`. Both tab counts apply search, type, branch and date, and are returned with every list response. | Backend |
| BR-11 | Filters. `q`: trimmed. Empty means no search. More than 100 characters → `400 errors.q`. Matching is case-insensitive substring on `subject`, payload `description`, `entityLabel` and `secondaryLabel`. The frontend sends the term 300 ms after the last keystroke. `type`: a BR-02 code, or absent for all; unknown → `400 errors.type`. `branchId`: an active in-scope branch of the organization. The option keeps notifications of that branch only. Absent = all visible, including those with a null branch. A foreign, inactive, unknown or out-of-scope id → `404 branch_not_found`. `date` (on `created_at` in the organization time zone): `any` (default) "Any date", `today` "Today", `7d` "Last 7 days", `30d` "Last 30 days". A window of N days covers today and the previous N − 1 local days. Unknown → `400 errors.date`. | Both |
| BR-12 | Link. The server returns `target = { kind, id }` only when the caller's current role passes the target's read policy and the entity still exists in the organization and in the caller's branch scope. Otherwise `target = null`, and the row is still listed. Read policy and route per kind: `request` → Service Request View, `/requests?request=<id>`. `customer` → Customer View, `/customers/<id>`. `quote` → quote Read, `/quotes/<id>`. `visit` → Dispatch Read, `/schedule?visit=<id>`. `work_order` → Work Order Read, `/jobs/<id>`. `invoice` → Billing Review Read, `/invoices/<id>`. The frontend maps the kind to the route and never builds a link from labels. Every destination still applies its own authorization, and its existing `403`/`404` states apply. | Backend (eligibility), Frontend (route) |
| BR-13 | Order and pagination. Order: `created_at` descending, then `id` descending. `page` ≥ 1 and `pageSize` 1 – 100 (`400` otherwise). The API default is 25. The UI always sends `pageSize` and offers 10 (default), 25 and 50 rows per page. A page past the last returns empty `items` with the real `total`, and the frontend then requests the last page (`replaceUrl`). The footer shows "<from>–<to> of <total>", **Rows per page**, previous and next buttons, and page numbers. | Both |
| BR-14 | Summary (no tab, search, type, branch or date). `unread` = visible notifications with `status ≠ 'read'`. `today` = visible notifications created during the current local day in the organization time zone. The cards are labelled "Unread" and "Today", and they are not interactive. | Backend |
| BR-15 | Row content. Unread rows show a dot and a stronger title. Read rows show neither. Then come the type icon (BR-02), title (`subject`) and description. The related entity shows `entityLabel` as a link when `target` is not null, else as plain text, with "<secondaryLabel> · <branch name>" below. A part is omitted when absent, and nothing is shown when both are absent. The branch name is the current name of the branch. Relative time from `created_at`, in the organization time zone: < 1 min "Just now". < 60 min "<n> min ago". Same local day "<n> hr ago". Previous local day "Yesterday". Otherwise "MMM d", plus ", yyyy" in another year. The full date and time is the tooltip and the accessible label. Groups: rows created during the current local day go under **Today**, the rest under **Earlier**. Groups apply within the current page, and an empty group is not shown. | Both |
| BR-16 | Bell. For internal roles, the bell is a button that opens the notifications popover (BR-26) and does not navigate. It shows a badge with the summary `unread` (hidden at 0, "99+" above 99) and is named "Notifications, <n> unread" ("Notifications" at 0). The count loads when the shell renders. While the tab is visible, the shell requests `GET /notifications/summary` every 30 s after the last successful load, whether that load came from a poll, a navigation or a marking response. A navigation less than 30 s after the last successful load reuses the current value without a request. While the tab is hidden (`document.hidden`) no request is made. When the tab becomes visible again, one request is made if the last successful load is 30 s old or more, and polling resumes. At most one summary request is in flight. Marking actions on the page update the bell immediately from the server response, with no extra request. A failed load keeps the previous value, or no badge when there is none, with no error UI, and the next tick retries. A `403` stops the polling. Sign-out stops it and clears the count. The list endpoint is never polled. | Frontend |
| BR-17 | Navigation. The sidebar shows **Notifications** (bell icon) to every internal role, in the bottom block above **Administration**. Administration stays visible to `owner` and `viewer` only. The item is current on `/notifications`. The Administration column link **Notifications** opens `/notifications`. For `technician`, the sidebar item and the bell badge are not shown, the app-shell bell keeps its current Coming soon link, and the technician shell is unchanged. `/coming-soon/notifications` stays a valid Coming soon page. | Frontend |
| BR-18 | Selecting a row (click or Enter on the row's primary control). An unread row calls `POST /notifications/{id}/read` and is shown as read immediately. The link then opens. Navigation does not wait for, and is not blocked by, a failure of the read call. A row with `target = null` is only marked read, and the page shows the toast "This item is no longer available.". A read row with a target navigates without any call. | Frontend |
| BR-19 | Read state. `POST /{id}/read` sets `status = 'read'`. `POST /{id}/unread` sets `status = 'sent'`. Both are idempotent and return the new summary. The row menu (⋮) offers **Mark as read** or **Mark as unread** for the row's state. **Mark all as read** sets `status = 'read'` on every visible unread notification of the caller created up to the request time, regardless of the tab or filters. It returns `{ updated, summary }`, and the page reloads the list. The button is disabled at `unread = 0` and while pending. On a `404` for a single row, the page shows "This notification isn't available." and reloads the list and summary. On other errors, it shows "Couldn't update the notification. Try again." and restores the previous state. No other columns change. | Both |
| BR-20 | URL state. `tab` (`all` default, `unread`), `q`, `type`, `branch` (absent = All branches), `date` (absent = `any`), `page` (absent = 1), `pageSize` (absent = 10). Reload and back restore the view. Unknown or invalid values are replaced by their defaults with `replaceUrl` before any request. A `404` branch resets to All branches (`replaceUrl`). Changing the search, a filter, the tab or the page size resets to page 1. **Clear filters** (sliders button) resets search, type, branch and date. Its badge counts the active ones, and it is disabled when none are active. | Frontend |
| BR-21 | Options. `branches` = the caller's active in-scope branches by name. The branch dropdown shows "All branches" followed by them. It is hidden when the caller has fewer than two. The type dropdown shows "All types" plus the BR-02 labels. | Both |
| BR-22 | Layout (Design 32). Breadcrumb "Administration" (plain text) / "Notifications". Title "Notifications", subtitle "Stay updated on activity that needs your attention.". **Mark all as read** with a check icon is right-aligned. The Unread and Today cards sit to the right of the tabs. Tabs "All notifications <count>" and "Unread <count>". From 1280 px: the filters sit in one row (search, Type, Branch, Date, Clear filters), and the rows show the columns dot/icon, title + description, entity + branch, time, menu. From 768 to 1279 px: the cards wrap below the header, the filters wrap, and the entity and time move under the description. Below 768 px: header action full width, one filter per row, cards side by side, rows stacked, and the pagination wraps. No page-level horizontal scroll at 375 px. The design's sample rows are never copied or seeded. | Frontend |
| BR-23 | Accessibility. Tabs are a tablist whose names include the count. The unread state is conveyed by text ("Unread" in the accessible name), never by the dot alone. The row's primary control is named "<title>, <unread/read>, <relative time>". The menu button is "More actions for <title>". Filters have accessible labels. Icons are decorative. Result changes are announced politely ("<total> notifications"). Every control has a visible focus indicator. Touch targets are ≥ 44 px below 768 px. | Frontend |
| BR-24 | Query efficiency. The list, summary and read-all each run a bounded number of database queries, independent of page size and row count. Filtering, counting, ordering and paging run in the database, using `ix_notifications_recipient`. Recipient resolution at event time uses one query per event, not one per member. | Backend |
| BR-25 | New-activity notice. On `/notifications`, when a summary refresh from the poll or from tab focus returns `unread` greater than the unread count the page last received from the list or from a marking response, the page shows a polite notice "New notifications available" with the action **Refresh** above the list. The list, counts, URL and focus do not change until the user selects **Refresh**. **Refresh** reloads the list and summary with the current URL state and dismisses the notice. The notice is also dismissed by any list reload (filter, tab, page, **Mark all as read**). It is not shown on the first load, on a `403` or when `unread` does not increase. It is announced politely and is keyboard-reachable. | Frontend |
| BR-26 | Notification popover (Design 33). For internal roles, selecting the bell opens a popover anchored below the bell, without changing the route. The technician shell is unchanged. Width 400 – 420 px and height up to 500 px; below 480 px it fits the viewport width. Each time it opens, it requests `GET /notifications?tab=all&page=1&pageSize=5` and shows those rows, which are the five most recent notifications visible to the caller (BR-03). Header: title "Notifications" and **Mark all as read** (disabled at `unread = 0` and while pending, as BR-19). Footer: **View all notifications** with an arrow, which closes the popover and opens `/notifications`. The header and the footer stay fixed, and only the list scrolls. Each row shows the type icon (BR-02), title, description, relative time (BR-15), and for unread rows a soft teal background, a dot and "Unread" in the accessible name. Long title and description are truncated to one line without widening the popover, and the full text is the accessible name. Selecting a row follows BR-18 and then closes the popover. The popover adds no row menu or other per-row action. **Mark all as read** and read marking update the popover rows and the bell from the server response with no extra summary request, and **Mark all as read** reloads the popover list. States: loading skeleton rows, empty "You're all caught up.", error "Couldn't load notifications." with **Retry**, and `403` closing the popover. The popover closes with Escape, a click outside, a route change or selecting a row or the footer link. On open, focus moves to the first row, or to the header action when there are no rows. On close, focus returns to the bell. The bell is a button with `aria-haspopup="dialog"` and `aria-expanded`, and the popover is a dialog named "Notifications". Controls are reachable with Tab and have a visible focus indicator. The popover adds no polling and no extra summary request, and it reuses the BR-16 count and 30 s rule. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | `sent` | BR-05 event | System, in the event transaction | BR-04 recipient; not a replay (BR-09) |
| `sent` | `read` | Select row, **Mark as read**, **Mark all as read** | Recipient | Visible (BR-03) |
| `read` | `sent` | **Mark as unread** | Recipient | Visible (BR-03) |

`pending` and `failed` are never written or shown by this feature. `in_app`
rows in those states are not created.

## Data and persistence impact

- Tables and columns (from `docs/database/fieldops-schema.sql`):
  - `notifications`: all columns written on creation (BR-06). Only `status` is updated afterwards.
  - Read for recipients and visibility: `organization_users` (`organization_id`, `user_id`, `role_id`, `status`, `is_all_branches`), `organization_user_branches`, `roles`, `branches` (`id`, `organization_id`, `name`, `is_active`), `organizations.timezone`.
  - Read for content and targets: `service_requests` (`branch_id`, `request_number`, `customer_id`, `guest_name`), `customers` (`branch_id`, `display_name`), `customer_contacts` names, `quotes` (`branch_id`, `request_id`, `quote_number`), approved response total, `work_orders` (`branch_id`, `work_order_number`), `visits` (`work_order_id`, `visit_number`, scheduled start, technician), technician names, `invoices` (`branch_id`, `invoice_number`, `total`, currency), payment amounts.
- Domain change (no column change): `Notification` gains creation with `subject` and `payload`, and read/unread methods limited to the BR-19 transitions.
- Schema amendment required (**confirmed by the user 2026-10-15**, OD-08): `CREATE INDEX ix_notifications_recipient ON notifications(organization_id, recipient_user_id, created_at DESC);`. No new columns. Implementation updates `docs/database/fieldops-schema.sql` first, maps the index in the EF model, and generates one migration under `--generate-migration`. The migration is never applied. The mapping is reviewed by `database-reviewer`.

## Tenant isolation and authorization

- Organization context: resolved from the authenticated session membership on every request. Recipients are resolved server-side from memberships of the event's organization only.
- Client-provided identifiers: the notification `id` and the `branchId` filter only. Both are authorized against the caller (BR-01, BR-03, BR-11), and an unauthorized value is indistinguishable from an unknown one (`404`).
- A notification of another organization or another user, or one hidden by BR-03 → `404 notification_not_found`. A foreign branch filter → `404 branch_not_found`.
- Permission: Notification Inbox on all six endpoints. Links are re-authorized per kind (BR-12), and every destination re-authorizes on its own.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/notifications` | Query `tab`, `q`, `type`, `branchId`, `date`, `page`, `pageSize` | `200 { items: [{ id, type, eventCode, title, description, entityKind, entityLabel, secondaryLabel, branch: { id, name } \| null, createdAt, isRead, target: { kind, id } \| null }], total, page, pageSize, counts: { all, unread }, timeZone }` | `400` (`errors.tab`, `errors.q`, `errors.type`, `errors.date`, `errors.page`, `errors.pageSize`), `401`, `403`, `404 branch_not_found` | Notification Inbox |
| GET | `/notifications/summary` | — | `200 { unread, today }` | `401`, `403` | Notification Inbox |
| GET | `/notifications/options` | — | `200 { branches: [{ id, name }] }` | `401`, `403` | Notification Inbox |
| POST | `/notifications/{id}/read` | — | `200 { summary: { unread, today } }` | `401`, `403`, `404 notification_not_found` | Notification Inbox |
| POST | `/notifications/{id}/unread` | — | `200 { summary: { unread, today } }` | `401`, `403`, `404 notification_not_found` | Notification Inbox |
| POST | `/notifications/read-all` | — | `200 { updated, summary: { unread, today } }` | `401`, `403` | Notification Inbox |

State-changing requests use the existing cookie session and CSRF protection.
`createdAt` is ISO 8601 UTC. `timeZone` is the IANA `organizations.timezone`.
The frontend uses it for relative time and the Today/Earlier groups (BR-15).

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/notifications` | Skeleton cards, tab counts and rows. Filters disabled until options load | No notifications at all: "You're all caught up" / "New activity for you will appear here.". Filters or tab with no match: "No notifications match your filters" + **Clear filters** | "Couldn't load notifications." + **Retry**. The summary failing alone shows "—" in the cards | `403` (technician, unknown role): the existing forbidden state, with no requests after the first `403` | List, counts and pagination per BR-10 – BR-15, plus the BR-25 notice when new activity arrives |
| Shell bell | No badge until loaded | No badge at 0 | Previous value kept, no error UI, next tick retries (BR-16) | Technician: unchanged Coming soon link, no badge, no polling | Badge with the unread count, refreshed every 30 s while visible |

- Mockups: `design/assets/32-design.png` (Approved) for the page and `design/assets/33-design.png` (Approved) for the popover. The rows "Visit requires attention" and "Quote expiring soon" are excluded (Non-goals).
- Responsive and accessibility constraints: BR-22, BR-23.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invalid query value | `400` with the field key. The UI normalizes the URL first, so it never sends one | None |
| Foreign, inactive or out-of-scope `branchId` | `404 branch_not_found`. The UI resets to All branches | None |
| Read/unread on a hidden, foreign or unknown id | `404`. "This notification isn't available." The list and summary reload | None |
| Read/unread or read-all network/server error | "Couldn't update the notification. Try again." The previous state is restored | None |
| Selecting a row with `target = null` | Marked read; "This item is no longer available." | `status = 'read'` |
| Destination denies or no longer finds the entity | The destination's existing `403`/`404` state | None beyond the read mark |
| Event transaction fails | The event's existing error | No event change and no notification |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A technician or an unknown role, and separately an unauthenticated caller | They call any of the six endpoints | `403`, or `401` when unauthenticated, with no data read or changed |
| AC-02 | Notifications of user A in org 1, and user B in org 2 | B lists, summarizes, reads or unreads A's notification id | B never sees it in lists or counts; read/unread → `404 notification_not_found`, identical to an unknown id; A's row is unchanged |
| AC-03 | A notification for a branch the caller leaves, and another for an event type whose role set no longer includes the caller's new role | The caller lists, searches, pages, reads the summary, uses read-all or acts on those ids | Both are absent from items, totals, tab counts, summary, bell and read-all; their ids → `404`; when access returns, they reappear in their stored state |
| AC-04 | Org members with every role, branch scopes A and B, an inactive member and the acting user | Each BR-05 event fires on a branch A entity, and one `request.created` fires with a null branch | Rows exist only for active members with the event's role set and branch A in scope (all members in scope for the null branch), excluding the actor and technicians; each has `in_app`, `sent`, the event `template_code` and the BR-06 payload |
| AC-05 | One event per BR-05 source path (public, portal and staff request; portal message; approve, reject and clarification; dispatch, travel, start, pause; completion; invoice send; bank transfer report; manual and online payment) | Each completes successfully | Exactly one notification set per event, with the BR-07 title and description and the BR-08 labels; the message notification contains no message text or contact data |
| AC-06 | A failing event transaction, a repeated payment webhook, a repeated approval-link submission, an invoice resend, a `LogResponse` and an internal note | They run | No notification rows are written |
| AC-07 | 30 visible notifications, mixed read state, types, branches and dates | The caller lists with `tab=unread`, `type`, `branchId`, `date=7d` and `q` matching a title, a description and an entity label | Items match every filter in `created_at` desc, `id` desc order; `total` is correct; both tab counts apply the filters except the tab; the date window uses the organization time zone |
| AC-08 | Invalid `tab`, `q` > 100 characters, unknown `type`, unknown `date`, `page` 0, `pageSize` 101, and a foreign `branchId` | The list is called | `400` with the matching field key for each invalid value, and `404 branch_not_found` for the branch |
| AC-09 | Unread notifications created today and yesterday in the organization time zone, close to local midnight | The summary is requested | `unread` and `today` follow BR-14 using the organization time zone, not UTC |
| AC-10 | Notifications whose targets are of each kind, one target deleted or moved out of scope, and an accounting caller with a `job.completed` notification | The list is returned | `target` is set only when the caller passes the kind's read policy and the entity is in scope; otherwise it is null and the row is still listed (accounting gets no job target) |
| AC-11 | Unread and read visible notifications | The caller marks one read, marks one unread twice, and calls read-all on a filtered view | Single calls toggle only `status` and return the new summary, idempotently; read-all marks every visible unread notification regardless of filters, returns `updated` and the summary, and leaves hidden notifications unchanged |
| AC-12 | A page with notifications from today and earlier | The page renders | Rows show the dot and "Unread" name for unread rows only, the type icon, title, description, entity link or text, secondary label · branch, and relative time per BR-15, under Today and Earlier headers, without empty groups |
| AC-13 | An unread row with a target, a read row with a target, and a row without a target | The user selects each | The first marks read without waiting and navigates to the BR-12 route; the second navigates without a call; the third marks read and shows "This item is no longer available." |
| AC-14 | A visible row | The user uses its menu | **Mark as read** or **Mark as unread** matches its state; success updates the row, cards, tab counts and bell; `404` shows "This notification isn't available." and reloads; other errors show the retry message and restore the state |
| AC-15 | Unread notifications exist, and then none | The user selects **Mark all as read** | The button is disabled while pending; on success the list reloads, all counts become 0 and the bell badge disappears; the button is disabled at 0 unread |
| AC-16 | A URL with invalid `tab`, `type`, `date`, `page`, `pageSize`, and a branch that returns `404` | The page loads | Invalid values are replaced with defaults (`replaceUrl`) before any request; the branch resets to All branches; reload and back restore valid state; filter, tab and size changes reset to page 1 |
| AC-17 | A page past the last one, and a search typed quickly | The page loads, and the user types | An empty past-last page triggers a request for the last page; the search request is sent 300 ms after the last keystroke; **Clear filters** resets search, type, branch and date, counts its badge and is disabled with none active |
| AC-18 | The loading, empty-inbox, no-match, error, summary-error and `403` conditions | The page renders each | Each shows the UI states table content; **Retry** reloads; `403` stops further requests |
| AC-19 | An internal role with 0, 7 and 120 unread | The shell renders and the user selects the bell | The bell is a button that shows no badge, "7" and "99+", has the BR-16 accessible name and opens the BR-26 popover without changing the route |
| AC-20 | The bell loaded at t = 0, with the tab visible | Time passes and the user navigates, marks a notification read, hides and shows the tab, and a load fails | A navigation at t = 10 s makes no request; one summary request per 30 s tick; a navigation at t = 40 s after a tick at t = 30 s makes none; no request while the tab is hidden; on return one request if 30 s or more have passed and the poll resumes; at most one request in flight; the mark response updates the badge with no extra request and restarts the interval; a failed load keeps the previous value without error UI and the next tick retries; a `403` and sign-out stop the polling |
| AC-21 | An internal role, `owner`, and a technician | The shell renders | Internal roles see **Notifications** above Administration, current on `/notifications`; Administration still shows only for `owner`/`viewer`; the Administration column link opens `/notifications`; the technician sees no item, badge or popover, the app-shell bell keeps the Coming soon link, and the technician shell is unchanged |
| AC-22 | Viewports of 1280, 1024, 768 and 375 px | The user opens `/notifications` and the popover | The layout matches Design 32, Design 33 and BR-22, BR-26, without the excluded examples or sample data and with no page-level horizontal scroll at 375 px (user visual QA) |
| AC-23 | Keyboard and screen-reader use | The user navigates the page and the popover | Tabs, filters, rows, menus, pagination and the popover controls are reachable with visible focus; accessible names and announcements follow BR-23; touch targets are ≥ 44 px below 768 px; the popover focus, Escape and ARIA behavior follow BR-26 (user visual QA) |
| AC-24 | 10 versus 100 visible notifications on a page | The list, summary and read-all run | The number of database queries is the same; filtering, counting and paging run in SQL; the schema doc and migration add only `ix_notifications_recipient`, unapplied |
| AC-25 | `/notifications` is open with a loaded list, and the summary unread rises through a poll, then does not rise, then the user selects **Refresh** | Each poll result arrives | The notice appears only when unread rises above the last known unread; the rows, counts, URL and focus do not move; **Refresh** reloads with the current URL state and dismisses the notice; filter, tab, page and **Mark all as read** reloads also dismiss it; the notice is announced politely and reachable by keyboard; it never appears on the first load or on a `403` |
| AC-26 | An internal role with unread and read notifications, one without target, no notifications, a failing list and a technician | The user opens the bell, selects rows, uses **Mark all as read**, **View all notifications**, Escape, an outside click and a route change | The popover opens without changing route and loads five rows with the BR-26 content, unread styling and truncation; a row marks read, closes the popover and navigates to the authorized target, or marks read with the toast "This item is no longer available." when it has no target; **Mark all as read** and read marking update the rows and the bell without an extra summary request; the footer opens `/notifications`; empty, error with **Retry** and loading states render; Escape, outside click and route change close it and focus returns to the bell; the technician has no popover |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Relative-day windows in the organization time zone (date filter and Today) only if implemented as a pure utility; `Notification` read/unread transitions | AC-07, AC-09, AC-11 |
| Backend integration | Recipient fan-out matrix (table-driven across events, roles, branches, actor exclusion); one representative producer per event family (request, message, quote, visit, job, invoice/payment) with content snapshot; no-write on rollback and replay; list filters, counts, order and paging; summary; read/unread/read-all; validation errors; bounded query count | AC-04 – AC-11, AC-24 |
| Authorization/tenant isolation | Technician `403`; cross-organization and cross-recipient `404`; re-authorization when the branch scope or role changes (one test covering list, counts and read-all); target eligibility per read policy | AC-01 – AC-03, AC-10 |
| Frontend component/service | URL normalization and reset rules; row selection (mark-then-navigate, no-target toast); menu toggle and read-all with error paths; UI states; bell badge, 30 s polling with hidden-tab pause and resume (fake timers), reuse window and update from mark responses; new-activity notice; sidebar visibility by role; popover content, actions, states, close rules and focus | AC-12 – AC-21, AC-25, AC-26 |
| Browser (user visual QA) | Design 32 and Design 33 fidelity, responsive breakpoints, focus and keyboard | AC-22, AC-23 |

Event families are proven by representative producer tests. Each source path
does not need its own test when it shares the same producer code. Expected
size: about 6–8 backend integration methods (several table-driven) and 4–6
frontend methods. The fan-out matrix and the re-authorization are distinct
security risks and justify the upper end.

## Dependencies

- `authenticated-app-shell` (shell, bell, sidebar, forbidden state): AUDITED.
- Producers' approved specs, whose behavior is unchanged apart from adding notification rows in their transaction: `public-service-request`, `requests-pipeline`, `customer-portal-dashboard` (Send a message, portal requests), `customer-quote-approval`, `dispatch-calendar`, `mobile-job-progress`, `mobile-job-completion`, `invoice-draft-delivery`, `invoices-payments-management`, `customer-invoice-payments`.
- Destinations: `/requests?request=`, `/customers/:id`, `/quotes/:id`, `/schedule?visit=`, `/jobs/:id`, `/invoices/:id` (existing, each with its own authorization).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The visit `scheduled start` and technician used in the `visit.assigned` copy are the values committed by the dispatch transaction. |
| AS-02 | Branch names in rows are read at query time, so a renamed branch shows its current name. Titles and descriptions stay as snapshots. |
| AS-03 | Portal-originated requests reuse the existing `service_request.created` creation path, so the same producer covers them. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Recipients | Role/scope fan-out · targeted owners | No | Role/scope fan-out with the BR-04 matrix, excluding the actor (user 2026-10-15) |
| OD-02 | Technicians | Include · exclude | No | Fully excluded; no rows, technician shell unchanged (user 2026-10-15) |
| OD-03 | Customer message source | `LogResponse` · portal Send a message | No | Portal Send a message only, to `owner`/`dispatcher`, entity = customer, `/customers/:id`; `LogResponse` writes none (user 2026-10-15) |
| OD-04 | Time-based reminders | Scheduler · exclude | No | Excluded; the design's examples are removed (user 2026-10-15) |
| OD-05 | Content storage | Snapshot in `subject`/`payload` · render on read · new columns | No | Snapshot, no new columns (user 2026-10-15) |
| OD-06 | Read state | `status` toggle · new `read_at` | No | `read` ↔ `sent`, no read time (user 2026-10-15) |
| OD-07 | Lost access | Keep showing · hide | No | Hidden from lists, counts, bell, search, paging and read-all (user 2026-10-15) |
| OD-08 | Index | Add index · no schema change | No | Only `ix_notifications_recipient (organization_id, recipient_user_id, created_at DESC)`; migration generation authorized, never applied (user 2026-10-15) |
| OD-09 | Bell refresh | Navigation with 30 s cache · polling · WebSockets | No | Revised 2026-10-15 (user): poll `GET /notifications/summary` every 30 s while the tab is visible, pause when hidden and resume on focus, keep the 30 s reuse on navigation and the immediate update from mark responses. No WebSockets or SSE; the list is never polled; a notice offers a manual reload (BR-16, BR-25) |
| OD-10 | Navigation | Sidebar for all internal roles · admin only | No | Sidebar for internal app-shell roles above Administration; technician excluded (user 2026-10-15) |
| OD-11 | Bell click | Navigate to `/notifications` · popover | No | Popover with the five latest notifications and a footer link to `/notifications`, for internal roles only; no extra polling, no per-row actions (user 2026-10-15, Design 33) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02, AC-03 |
| FR-02 | AC-04, AC-05, AC-06 |
| FR-03 | AC-07, AC-08, AC-16, AC-17, AC-24 |
| FR-04 | AC-09 |
| FR-05 | AC-10, AC-12 |
| FR-06 | AC-10, AC-11, AC-13, AC-14, AC-15 |
| FR-07 | AC-19, AC-20, AC-21, AC-26 |
| FR-08 | AC-16, AC-17, AC-18, AC-25 |
| FR-09 | AC-22, AC-23 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-15 | — → DRAFT     | Created |
| 2026-10-15 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-15 | APPROVED → DRAFT | Revised via /spec revise: summary polling every 30 s, tab-visibility pause and new-activity notice (BR-16, BR-25, AC-20, AC-25, OD-09). The contracts are unchanged |
| 2026-10-15 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-15 | APPROVED → DRAFT | Revised via /spec revise: notification popover on the bell (BR-26, AC-26, OD-11, Design 33); BR-16, FR-07, AC-19, AC-21, AC-22, AC-23 updated. Polling (BR-16) and the page notice (BR-25) unchanged; contracts unchanged |
| 2026-10-15 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-15 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-15 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
