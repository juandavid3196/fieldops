# Technician messages

| Field    | Value                 |
| -------- | --------------------- |
| Feature  | `technician-messages` |
| Type     | Full-stack            |
| Status   | AUDITED               |
| Created  | 2026-10-17            |
| Updated  | 2026-10-20            |
| Approved | 2026-10-17            |

## Context and objective

Technicians and the office have no internal channel tied to jobs. The only
message table, `request_messages`, belongs to service requests. This
feature adds internal text conversations of two types: **Job** (one per work
order) and **Team** (one per branch). Participation is computed from
assignments, roles and branch scope; it is not administered. Technicians use
the approved mobile **Messages** design. Owner, Operations Manager and
Dispatcher get a minimal `/messages` inbox in the staff shell. Unread counts
appear in the list, the bottom navigation and the technician bell. Updates
arrive by polling every 30 seconds while the tab is visible.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Technician (`technician`) with an `active` linked profile | Read and send text in conversations where they are a participant (BR-02); search and filter them; open/create the Job conversation of a visit assigned to them | Access other conversations (`404`); edit, delete, attach, react; administer participants |
| Technician with `inactive`/`suspended` profile, or without linked profile | — | Every endpoint (`403 technician_inactive` / `404 technician_profile_not_linked`) |
| Owner (`owner`), Operations Manager (`operations_manager`) | Read and send in every Job and Team conversation of the organization's branches | Edit, delete, attach; administer participants |
| Dispatcher (`dispatcher`) | The same within branch scope (`organization_user_branches`, or all when `is_all_branches`) | Conversations of branches outside scope (`404`) |
| Viewer, Accounting, customers, portal contacts, unknown roles | — | Every endpoint (`403`/`401`); no navigation item |

New policy **Messaging** = `owner`, `operations_manager`, `dispatcher`,
`technician`. Existing policies are unchanged.

## Scope

- Schema amendment SA-01 (approved; three tables).
- Endpoints under `/messages` shared by technicians and staff.
- Technician routes `/today/messages` and `/today/messages/:conversationId`
  replacing the Messages Coming soon destination; **Message** action on the
  technician visit detail; technician bell opens Messages and shows the
  unread badge.
- Staff routes `/messages` and `/messages/:conversationId` with a sidebar item
  and unread badge.
- Polling, unread counts, delivery states and retry.

## Non-goals

- Customer or portal participants, customer-facing messages, changes to
  `request_messages` or the portal.
- Attachments, voice notes, reactions, editing, deleting, read receipts,
  typing or presence indicators, WebSockets or push notifications.
- Group creation, renaming or participant administration; direct messages.
- Email or `notifications` rows for messages; changes to the staff bell.
- Message retention policies or export.

## User flow

1. A technician opens **Messages** (bottom navigation or bell); the list shows
   their Team conversation and Job conversations with messages, with unread
   counts.
2. They search or filter (All, Unread, Jobs, Team) and open a conversation.
3. The conversation shows messages with sender and time; opening marks it
   read; they type and send; the message shows Sending, then Sent, or Failed
   with Retry.
4. From a visit detail, **Message** opens (creating if needed) the work
   order's Job conversation.
5. A dispatcher opens **Messages** in the staff shell, reads and replies in
   the same conversations.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | Every endpoint must resolve organization, membership, role, scope and (for technicians) profile from the session and authorize conversations by computed participation (BR-01, BR-02). |
| FR-02 | The system must persist conversations, messages and read states per SA-01 and BR-03. |
| FR-03 | The list endpoint and screens must provide the conversation list with search, filters, order, previews and unread counts per BR-04–BR-06. |
| FR-04 | The summary endpoint must return unread totals shown in the bottom navigation, technician bell and staff sidebar per BR-07 and BR-15. |
| FR-05 | The conversation endpoints and detail screens must show messages with sender, timestamp and work order context, with paging per BR-08 and BR-12. |
| FR-06 | Users must send text messages idempotently with Sending/Sent/Failed states and Retry per BR-09 and BR-13. |
| FR-07 | Opening a conversation must mark it read per BR-10. |
| FR-08 | Job and Team conversations must be resolved or created without duplicates per BR-11, including the visit detail **Message** action. |
| FR-09 | The screens must poll every 30 seconds while visible and provide accessible loading, empty, error and permission states per BR-14 and BR-16. |
| FR-10 | Navigation must follow BR-15. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization: policy Messaging. Technicians: profile resolution as `technician-todays-jobs` BR-02 (`technician_profile_not_linked`, `technician_inactive`) before any read. Staff scope: Owner, Operations Manager and `is_all_branches` members all active branches; Dispatcher `organization_user_branches`. Organization from the session; no organization, user or technician id accepted from the client. A conversation the caller does not participate in, of another organization, or nonexistent → identical `404` with no foreign data. | Backend |
| BR-02 | Computed participation, evaluated on every request (no participant table). **Job** conversation of work order W (status not `draft`) in branch B: a technician whose active profile has an active assignment on a non-`cancelled` visit of W, and staff with B in scope. **Team** conversation of branch B (active): technicians whose active profile `branch_id` = B, and staff with B in scope. A user who loses the assignment, branch or role loses access immediately; their past messages remain visible to current participants. | Backend |
| BR-03 | Persistence: a message has a non-null author (the caller's user), body trimmed 1–2000 characters, `created_at` = server time, and a client-generated `client_message_id` unique per conversation and author. Read state is one row per conversation and user with `last_read_at`. Messages are never updated or deleted by this feature. `conversations.last_message_at` is set to the new message's `created_at` in the same transaction as the insert. | Backend |
| BR-04 | List: authorized conversations only. Job conversations appear only with ≥ 1 message; the caller's Team conversations always appear (virtual entries with `id` null and `branchId` until first resolution). Order: `last_message_at` descending, nulls last, then title. Page size 30 with an opaque `cursor`. `ConversationSummary = { id \| null, type ("job" \| "team"), title, branchId, workOrder: { id, displayNumber } \| null, lastMessage: { preview, authorName, authorInitials, isMine, createdAt } \| null, unreadCount }`. Title: Job → work order title; Team → "<branch name> team". Preview: first 140 characters of the body, whitespace collapsed. | Backend |
| BR-05 | Filters and search: `filter` = `all` (default) \| `unread` (unreadCount > 0) \| `jobs` \| `team`. `search` trimmed 2–80 characters (`400 errors.search` "Enter 2 to 80 characters."), case-insensitive contains over work order title, "<prefix>-<n>", branch name and message bodies, applied only after BR-02 authorization; results never reveal titles, counts or snippets of non-authorized conversations. | Backend |
| BR-06 | Unread: messages of the conversation with `created_at` greater than the caller's `last_read_at` (all when no read state) and author other than the caller. | Backend |
| BR-07 | Summary: `{ unreadMessages, unreadConversations }` over the caller's authorized conversations. The badge shows `unreadMessages` ("99+" above 99), hidden at 0; the Unread filter chip shows the same number. | Both |
| BR-08 | Conversation detail: `{ id, type, title, branchName, workOrder: { id, displayNumber, title, visitId \| null } \| null }` where `visitId` (technicians only) is the caller's visit of W visible through `GET /technician/visits/{visitId}`, preferring an active visit (`technician-schedule-active-visits` BR-05), then the earliest upcoming, then the latest; null for staff. Messages: newest 50 by `created_at` descending then id, returned in ascending order, with `before` cursor for older pages. `Message = { id, clientMessageId \| null (author only), author: { name, initials, roleLabel }, isMine, body, createdAt }`. Author name and role come from the author's current profile/membership; a removed member shows "Former member". | Backend |
| BR-09 | Send: `POST` with `{ clientMessageId (uuid), body }`; the caller must be a participant (BR-02). Same author and `clientMessageId` already stored in that conversation → `200` with the stored message and no insert; new → `201`. Body validation `400 errors.body` "Enter a message of up to 2000 characters.". Sending also advances the author's read state to the new message. Rate: at most 30 messages per minute per user and organization (`429 message_rate_limited` "You're sending messages too quickly. Try again in a moment." with a `Retry-After` header). | Backend |
| BR-10 | Read: `POST …/read` sets the caller's `last_read_at` to the `created_at` of the latest message (never moving backwards); no messages → no-op. The detail screen calls it on open and after polling brings new messages while visible. | Both |
| BR-11 | Resolve: `POST /messages/conversations/resolve` with `{ type: "job", workOrderId }` or `{ type: "team", branchId }` returns the existing conversation id or creates it (Job branch = work order `branch_id`), guarded by unique keys so concurrent calls produce one row; the caller must be a participant of the conversation it would be (else identical `404`, no write). Creation writes no message and no audit row. The technician visit detail shows **Message** (with accessible name "Message the team about this job"), resolving the visit's work order and opening the conversation; a failure shows "We couldn't open the conversation. Try again." | Both |
| BR-12 | Conversation screen (approved visual language of the Messages design): top bar back button ("Back to messages") and conversation title; context strip with "#<prefix>-<n>" chip and **Open job** (technician → `/today/visits/:visitId` when `visitId`; staff → `/jobs/:workOrderId`) or "Team" chip for Team; day separators "Today", "Yesterday", "EEE, MMM d"; my messages right-aligned in the primary tone, others left-aligned with initials avatar, author name and role label; each message shows "h:mm a"; "Load earlier messages" at the top when more exist; scroll anchored to the newest message on open and when already at the bottom, otherwise a "New messages" control. Composer: multiline field labeled "Message", counter "<n>/2000" from 1800 characters, **Send** disabled when empty or over limit; Enter inserts a newline, Ctrl/Cmd+Enter sends. Times in the device time zone and locale; the server stores and returns UTC instants. | Frontend |
| BR-13 | Delivery states (author only): **Sending** while the request is pending (message shown immediately with a client id), **Sent** after `200/201`, **Failed** on error with **Retry** resending the same `clientMessageId` and **Discard**; `400` shows the field message and keeps the text in the composer; `429` shows its message as Failed. Failed messages live only in the open screen. No read receipts. State text is visible and announced, not color alone. | Frontend |
| BR-14 | Polling: while the document is visible, the summary every 30 s in both shells; the list every 30 s while on the list; new messages (after the newest loaded) every 30 s while in a conversation. Polling pauses when hidden and refreshes immediately when visible again; failures are silent and retried on the next tick; one request per kind in flight. New incoming messages are announced politely ("New message from <name>"). | Frontend |
| BR-15 | Navigation. Technician: **Messages** at `/today/messages` (current also on conversation pages) with the unread badge; `/today/soon/messages` redirects; the bell opens `/today/messages` and shows the same badge (replacing its Coming soon destination); top bar title "Messages". Bottom order per `technician-schedule-active-visits` BR-20. Staff: sidebar item **Messages** for Owner, Operations Manager and Dispatcher with the badge; not shown to other roles; the staff bell and `notifications-center` are unchanged. More, Account settings, Help & support and Sign out unchanged. | Frontend |
| BR-16 | List screen: title "Messages", subtitle "Job and team conversations"; search "Search messages" (debounced 300 ms, applied from 2 characters); filter chips All, Unread (with count), Jobs, Team as a single-select group; notice "Messages stay linked to the related job or team conversation."; section "Recent"; rows with unread dot, avatar (last author initials; building icon for Team), title, preview "<first name>: <text>" ("You: <text>" when mine; "No messages yet" when none), relative time ("2 min", "1h", "Yesterday", "MMM d"), unread count badge, "#<prefix>-<n>" or "Team" chip, chevron; the whole row is a link. States: skeleton rows; empty "No conversations yet." (All), "No unread messages." (Unread), "No job conversations yet." (Jobs), "No results for "<text>"." (search); error "We couldn't load your messages. Try again." with **Retry**. Staff `/messages`: same list and conversation components, two panes from 1024 px (list left, conversation right, "Select a conversation" when none), single pane below. | Frontend |
| BR-17 | Responsive and accessibility: `technician-todays-jobs` BR-19 applies in the technician shell; the composer stays above the bottom navigation and safe area at 375 px; message list is a log region; unread counts have text equivalents ("3 unread messages"); filter chips expose the selected state; targets ≥ 44 × 44 px; visible focus. | Frontend |
| BR-18 | Privacy: message bodies and search text never appear in logs, audit rows or error telemetry; responses use `Cache-Control: no-store`. | Backend |

## Data and persistence impact

- Read (from `docs/database/fieldops-schema.sql`): `organization_users`
  (`role_id`, `status`, `is_all_branches`), `organization_user_branches`,
  `roles`, `users` (names), `technician_profiles` (`branch_id`, `status`,
  names), `branches` (`name`, `is_active`), `work_orders` (`branch_id`,
  `status`, `work_order_number`, `title`), `visits`, `visit_assignments`,
  `organizations` (`work_order_prefix`).
- Written: `conversations`, `conversation_messages`, `conversation_read_states`.
- Schema amendment SA-01, **approved by the user 2026-10-17** (OD-01).
  Implementation updates `docs/database/fieldops-schema.sql` and the EF model
  and generates one migration only under `--generate-migration`; agents never
  apply it. Review by `database-reviewer`. Each table follows the direct
  `organizations` FK plus composite parent FK pattern.
  - `conversations (id uuid PK, organization_id uuid NOT NULL REFERENCES
    organizations(id), branch_id uuid NOT NULL, type varchar(10) NOT NULL
    CHECK (type IN ('job','team')), work_order_id uuid NULL, created_at
    timestamptz NOT NULL DEFAULT now(), last_message_at timestamptz NULL,
    FOREIGN KEY (organization_id, branch_id) REFERENCES branches(organization_id,
    id), FOREIGN KEY (organization_id, work_order_id) REFERENCES
    work_orders(organization_id, id), CHECK ((type = 'job') = (work_order_id IS
    NOT NULL)), UNIQUE (organization_id, id))`; unique index `(work_order_id)
    WHERE type = 'job'`; unique index `(branch_id) WHERE type = 'team'`; index
    `(organization_id, branch_id, last_message_at DESC)`.
  - `conversation_messages (id uuid PK, organization_id uuid NOT NULL
    REFERENCES organizations(id), conversation_id uuid NOT NULL, author_user_id
    uuid NOT NULL REFERENCES users(id), client_message_id uuid NOT NULL, body
    varchar(2000) NOT NULL CHECK (length(btrim(body)) > 0), created_at
    timestamptz NOT NULL DEFAULT now(), FOREIGN KEY (organization_id,
    conversation_id) REFERENCES conversations(organization_id, id), UNIQUE
    (conversation_id, author_user_id, client_message_id))`; index
    `(conversation_id, created_at DESC, id DESC)`.
  - `conversation_read_states (organization_id uuid NOT NULL REFERENCES
    organizations(id), conversation_id uuid NOT NULL, user_id uuid NOT NULL
    REFERENCES users(id), last_read_at timestamptz NOT NULL, PRIMARY KEY
    (conversation_id, user_id), FOREIGN KEY (organization_id, conversation_id)
    REFERENCES conversations(organization_id, id))`.
  - Rows are never deleted by this feature; no `ON DELETE CASCADE` from
    work orders or branches.

## Tenant isolation and authorization

- Organization, role, scope and profile come from the session (BR-01).
- Client-provided identifiers: `conversationId` (path), `workOrderId` and
  `branchId` (resolve body), message `before`/list `cursor` (opaque, bound to
  the conversation/filter); each authorized by BR-02 in the session
  organization; otherwise identical `404`.
- Search runs only over authorized conversations (BR-05).
- One representative cross-organization denial, one out-of-scope dispatcher
  denial and one non-assigned technician denial cover the participation paths.
- Mutations use the existing cookie session and CSRF protections; nothing new.

## API contracts

Contract status: Final. Errors use ProblemDetails with `code` where listed.
Common errors: `403` (role; `technician_inactive`) · `404`
(`technician_profile_not_linked`; non-participant). Permission Messaging.

| Method | Path | Request | Success | Additional errors |
| ------ | ---- | ------- | ------- | ----------------- |
| GET | `/messages/summary` | — | `200 { unreadMessages, unreadConversations }` | — |
| GET | `/messages/conversations` | Query `filter?`, `search?`, `cursor?` | `200 { items: ConversationSummary[], nextCursor \| null }` | `400` (`errors.filter`, `errors.search`, `errors.cursor`) |
| POST | `/messages/conversations/resolve` | `{ type: "job", workOrderId }` \| `{ type: "team", branchId }` | `200 { conversationId }` | `400 errors.type` |
| GET | `/messages/conversations/{conversationId}` | — | `200 ConversationDetail` (BR-08) | — |
| GET | `/messages/conversations/{conversationId}/messages` | Query `before?`, `after?` | `200 { items: Message[], hasMore }` | `400 errors.cursor` |
| POST | `/messages/conversations/{conversationId}/messages` | `{ clientMessageId, body }` | `201 Message` (new) · `200 Message` (repeat) | `400 errors.body`, `errors.clientMessageId` · `429 message_rate_limited` |
| POST | `/messages/conversations/{conversationId}/read` | — | `204` | — |

`after` returns messages newer than the given message id in ascending order
(polling); `before` returns the previous page.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/today/messages`, staff `/messages` list | Skeleton rows | BR-16 per filter/search | BR-16 error with **Retry** | Technician states as `/today`; other roles forbidden state | BR-16 |
| Conversation | Skeleton bubbles | "No messages yet. Start the conversation." | Load: "We couldn't load this conversation. Try again." with **Retry**; `404`: "This conversation isn't available." with **Back to messages**; send per BR-13 | As list | BR-12, BR-13 |
| Visit detail **Message** | Button progress | — | BR-11 message | Technician only | Opens conversation |

- Mockups: `design/assets/FieldOps_ Mensajes de trabajo y equipo.png`
  (Approved; list, bottom navigation, bell badge). Conversation screen and
  staff inbox are defined by BR-12 and BR-16 in the same visual language and
  approved with this spec (OD-09). Deviations: customer names in the mockup
  ("Sofia Martinez") are illustrative; no customer participants; the
  "Schedule update" chip is not implemented (rows show "#WO-n" or "Team").
- Responsive and accessibility: BR-17.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Non-participant, other organization, out of scope or unknown conversation/work order/branch | `404`; "This conversation isn't available." | None |
| Viewer, Accounting, unknown role | `403`; forbidden state | None |
| Invalid search, filter, cursor or type | `400` with field message | None |
| Empty or too long body | `400 errors.body`; text kept in composer | None |
| Repeated `clientMessageId` | `200` with stored message | None |
| Rate limit | `429 message_rate_limited`; message Failed with Retry | None |
| Network failure on send | Failed with **Retry** / **Discard** | None |
| Polling failure | Silent; next tick retries | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A technician assigned to a visit of W1, a technician assigned only to W2, a technician unassigned from W1, a technician of another branch, a dispatcher out of scope, an Owner, and a conversation of another organization (parameterized) | Each reads, sends to and resolves the W1 Job conversation and the branch Team conversation | Access matches BR-02 exactly; every denial is the identical `404` with no foreign data and no write |
| AC-02 | Viewer, Accounting, an unlinked and an inactive technician | They call every endpoint and open Messages | `403`, `404 technician_profile_not_linked`, `403 technician_inactive`; no sidebar item for Viewer/Accounting |
| AC-03 | A technician unassigned from W1 after sending messages there | Remaining participants read W1 and the former assignee lists conversations | Participants still see the messages with author name; the former assignee no longer sees or opens W1 |
| AC-04 | Conversations with and without messages, Team without messages, and conversations of other branches/organizations whose titles and bodies match the search (table-driven) | The list is requested with each filter and search, including 1 and 81 characters | Membership, order, virtual Team entry, previews and unread counts follow BR-04–BR-06; search returns only authorized matches; invalid search `400` |
| AC-05 | Messages from others before and after the caller's read state and the caller's own messages | Summary and list are requested, then the conversation is opened and marked read | Unread counts follow BR-06/BR-07; after read the conversation's count is 0 and the summary decreases; read state never moves backwards |
| AC-06 | A participant, including in a Job conversation of a completed work order | They send bodies of 0 (whitespace), 1, 2000 and 2001 characters, repeat a `clientMessageId`, and exceed 30 messages in a minute | Valid bodies `201` with `last_message_at` updated and the author's read state advanced, also for the completed work order; invalid `400 errors.body`; repeat `200` with the same message and one row; excess `429 message_rate_limited` with `Retry-After` |
| AC-07 | Concurrent resolve calls for the same work order and the same branch | They run | One conversation per work order and per branch; all calls return the same id; no message or audit row written |
| AC-08 | A conversation with 120 messages | The detail and pages are requested with `before` and `after` | The newest 50 in ascending order, `hasMore`, older pages via `before`, newer via `after`; detail `workOrder.visitId` follows BR-08 for technicians and is null for staff |
| AC-09 | Bodies and search text in requests | Logs and audit are inspected after sends, searches and failures | No body or search text appears; responses carry `Cache-Control: no-store` |
| AC-10 | The technician list | Search, each filter and a row are used; loading, empty per filter and error with Retry occur | BR-16 content, chips with Unread count, notice and states render; the row opens the conversation |
| AC-11 | A Job and a Team conversation with messages over several days | The technician opens each | Header, context strip with **Open job** (or Team chip), day separators, bubble alignment, author, role and time follow BR-12; "Load earlier messages" loads older ones keeping position |
| AC-12 | The composer | A message is sent successfully, then one fails and is retried, one is discarded, one is rejected with `400` | Sending → Sent; Failed with Retry reusing the same `clientMessageId` → Sent without duplicates; Discard removes it; `400` keeps the text with the message; states are announced |
| AC-13 | A technician visit detail | **Message** is used, with a resolve failure once | The Job conversation opens (created if needed); failure shows the BR-11 message |
| AC-14 | Both shells open with the tab visible and then hidden | New messages arrive | Badges, list and open conversation update within one 30 s tick while visible; polling stops while hidden and refreshes on return; the new message is announced; failures are silent |
| AC-15 | The technician shell and the staff shell | Bell, bottom navigation, `/today/soon/messages` and staff sidebar are used | Technician bell and **Messages** open `/today/messages` with the shared badge ("99+" cap); Messages current on conversation pages; redirect works; staff sidebar item only for Owner/Ops/Dispatcher with badge; staff bell unchanged |
| AC-16 | Staff `/messages` at 1280 px and 375 px | A dispatcher selects conversations and replies | Two panes from 1024 px with "Select a conversation"; single pane below; sending and states as technicians |
| AC-17 | Viewports 375 px and 1280 px | Messages is used by keyboard and touch | Approved list design; composer above bottom navigation and safe area; no page-level horizontal scroll; targets ≥ 44 px; visible focus; counts with text equivalents |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Authorization/tenant isolation | Participation matrix across Job/Team, roles, scope, assignment loss and other organization (parameterized), including search leakage | AC-01–AC-04 |
| Backend integration | List membership/order/filters/search and unread/read state | AC-04, AC-05 |
| Backend integration | Send validation, idempotency, rate limit, `last_message_at`; resolve concurrency; paging and `visitId` | AC-06–AC-08 |
| Backend integration | Sensitive data absent from logs/audit; `no-store` | AC-09 |
| Persistence review | SA-01 mapping, FKs, unique indexes and migration (`database-reviewer`); generated only with the flag, never applied | SA-01 |
| Frontend component/service | List (search, filters, states), conversation (rendering, paging, delivery states/retry), polling visibility, Message action, navigation and badges (3–6 methods) | AC-10–AC-15 |
| User visual QA (manual) | Design fidelity, staff two-pane layout, responsive, focus | AC-16, AC-17 |

Backend integration may reach 8 methods because participation and search
leakage are distinct high-risk authorization paths. Agents run no browser
automation.

## Dependencies

- `technician-schedule-active-visits` (DRAFT): bottom navigation order and
  active-visit definition used by `visitId`. Implemented first, then
  `technician-time-summary`.
- `technician-todays-jobs`, `mobile-job-details` (AUDITED): shell, profile
  resolution, visit detail.
- `authenticated-app-shell` (AUDITED): staff sidebar; `dispatch-calendar` /
  `requests-pipeline` BR-02: branch scope.
- `notifications-center` (AUDITED): unchanged staff bell; polling precedent.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Removed 2026-10-17: confirmed by the user and recorded as OD-10. |
| AS-02 | Role labels reuse existing role display names; technicians show "Technician". |
| AS-03 | The BR-09 rate limit reuses the existing ASP.NET Core rate-limiting infrastructure (precedent: invitations), partitioned per user and organization. |
| AS-04 | Inactive branches' Team conversations are hidden from lists but kept in storage. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Schema | Three new tables · reuse `request_messages` | No | SA-01 approved — user 2026-10-17 |
| OD-02 | Participants | Computed · stored | No | Computed, no participant table (BR-02) — user 2026-10-17 |
| OD-03 | Staff side | Minimal `/messages` · none | No | Minimal `/messages` for Owner, Operations Manager, Dispatcher — user 2026-10-17 |
| OD-04 | Customer participants | Internal only · include customers | No | Internal only; mockup names illustrative — user 2026-10-17 |
| OD-05 | Delivery state | Sending/Sent/Failed · plus Read | No | Sending, Sent, Failed with Retry; no receipts — user 2026-10-17 |
| OD-06 | Job conversation creation | Visit detail **Message** · list action | No | **Message** on visit detail; Job listed only with ≥ 1 message; Team always — user 2026-10-17 |
| OD-07 | Technician bell | Messages · Coming soon | No | Opens Messages, badge shared with the tab — user 2026-10-17 |
| OD-08 | Refresh | Polling 30 s visible · WebSockets | No | Polling 30 s while visible — user 2026-10-17 |
| OD-09 | Conversation screen without mockup | Defined in spec · ui-designer | No | Defined in spec (BR-12, BR-16), validated with the spec — user 2026-10-17 |
| OD-10 | Closed work orders, rate limit, time display | — | No | Sending allowed in Job conversations of completed/cancelled work orders while computed access holds, lost with participation (BR-02); 30 messages/minute per user + organization with `Retry-After` (BR-09); device zone and locale, UTC on server (BR-12) — user 2026-10-17 |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02, AC-03 |
| FR-02 | AC-06, AC-07, AC-09 |
| FR-03 | AC-04, AC-10 |
| FR-04 | AC-05, AC-15 |
| FR-05 | AC-08, AC-11 |
| FR-06 | AC-06, AC-12 |
| FR-07 | AC-05 |
| FR-08 | AC-07, AC-13 |
| FR-09 | AC-14, AC-16, AC-17 |
| FR-10 | AC-15 |

## Change log

| Date       | Status change | Reason  |
| ---------- | ------------- | ------- |
| 2026-10-17 | — → DRAFT     | Created |
| 2026-10-17 | DRAFT → DRAFT | Revised with user confirmations: rate limit per user + organization with `Retry-After` (BR-09, AC-06); UTC storage and device zone/locale display (BR-12); sending on closed work orders confirmed (OD-10) |
| 2026-10-17 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-20 | APPROVED → AUDITED | User override: final-audit returned AUDIT FAIL because the frontend full test run (`npm run test -- --watch=false`) never finished green (10, 113, 150, 17 and 78 failures across five runs, different files each time, mostly 5 s timeouts; the flagged files pass in isolation and none belong to this feature). The user confirmed the feature works in the browser and asked to mark the spec audited. Persistence APPROVED (fp `5cd91c66`), backend full green (1087 unit, 576 integration), qa-auditor found no Critical or Important findings (2 Minor), user visual QA PASS. Open: confirm the frontend suite is stable (rerun on a quiet machine or in CI). |
