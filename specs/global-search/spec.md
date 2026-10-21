# Global search

| Field    | Value           |
| -------- | --------------- |
| Feature  | `global-search` |
| Type     | Full-stack      |
| Status   | AUDITED         |
| Created  | 2026-10-20      |
| Updated  | 2026-10-21      |
| Approved | 2026-10-20      |

## Context and objective

The search field in the staff shell top bar does not search. Enter opens
`/coming-soon/search`. This feature turns it into a global search combobox
that finds customers, service requests, quotes, jobs (work orders) and
invoices of the caller's organization while the user types. Results appear in
a dropdown anchored below the bar (Design "Global search FieldOps"). Each type
is returned only when the caller's role may read it and the entity is in the
caller's branch scope. Selecting a result opens its existing detail page, and
that page re-authorizes the entity.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`), Operations Manager (`operations_manager`), Dispatcher (`dispatcher`), Viewer (`viewer`) | Search customers, service requests, quotes, jobs and invoices in their branch scope (BR-03) | See entities of another organization or outside their branch scope |
| Accounting (`accounting`) | Search customers, service requests, quotes and invoices in their branch scope | Find jobs (no Work Order Read), or anything else outside BR-03 |
| Technician (`technician`), unknown roles | — | `GET /search/global` (`403`). The technician shell has no global search |
| Customers, portal contacts, anonymous users | — | `GET /search/global` (`401`/`403`). The customer portal has no global search |

New policy **Global Search** = `owner`, `operations_manager`, `dispatcher`,
`accounting`, `viewer`. Per-type eligibility reuses the existing read
policies (BR-03). Existing policies are unchanged.

## Scope

- `GET /search/global`: a bounded, ranked, tenant- and branch-scoped search over five entity types (BR-01 – BR-09).
- The staff shell search field becomes an accessible combobox with a results dropdown, its states, keyboard support and Ctrl/Cmd + K (BR-10 – BR-17).
- Selecting a result opens the entity's existing detail route (BR-15).
- One schema amendment, SA-23: the `unaccent` extension and an immutable wrapper function, with no new indexes (Data and persistence impact).

## Non-goals

- Advanced search, date or status filters, a `/search` route or results page, saved, recent or history searches, quick commands, search inside files or attachments, external indexing (Elasticsearch or similar) and semantic search.
- Assets: no asset entity, permission or detail route exists.
- Searching messages, internal notes, customer notes, request descriptions, attachments, audit logs, contacts, users, passwords, tokens or any other content not listed in BR-04.
- Global search in the customer portal, the technician mobile shell, or the staff shell below 768 px (where the shell shows no search field).
- New indexes, phone-number normalization and changes to destination pages.

## User flow

1. A staff user focuses the search field (click, Tab or Ctrl/Cmd + K). The dropdown opens with "Search by name, number, email, or phone.".
2. The user types. With one character the dropdown shows "Enter at least 2 characters." and no request is made.
3. With 2 or more characters, 300 ms after the last keystroke the frontend requests `GET /search/global`, cancelling any request in flight. Four skeleton rows are shown until the response arrives.
4. The system returns up to 20 ranked results the caller may read, grouped by type. The first result is active.
5. The user moves with the arrow keys and presses Enter, or clicks a result. The dropdown closes, the field is cleared and the detail page opens.
6. Escape or a click outside closes the dropdown without navigating.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The endpoint must resolve organization, role and branch scope from the session, apply the Global Search policy, and return only types and entities the caller may read (BR-01 – BR-03). |
| FR-02 | The endpoint must validate and normalize `q` and `limit` (BR-05) and accept no other filter, type or column input. |
| FR-03 | The endpoint must match the fixed fields of each type, ignoring case and accents, with LIKE wildcards treated as literals (BR-04, BR-06). |
| FR-04 | The endpoint must rank, cap and flag results (BR-07) and return the contract fields of BR-08 for each item. |
| FR-05 | The search must not write audit rows, store the query or log its text, and must run a bounded number of projected queries (BR-09). |
| FR-06 | The shell search field must normalize input, debounce, skip short queries and cancel superseded requests (BR-10, BR-11). |
| FR-07 | The dropdown must render the grouped results, row content and every state of BR-12 – BR-14 without blocking the current page. |
| FR-08 | Selecting a result must close the dropdown, clear the field and open the entity's detail route (BR-15). |
| FR-09 | The combobox must support the keyboard, focus, close and ARIA behavior of BR-16, and Ctrl/Cmd + K (BR-17). |
| FR-10 | The search must exist only in the staff shell top bar and reproduce the approved design (BR-18, BR-19). |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Authorization. `GET /search/global` requires the Global Search policy. Other roles get `403` before any query. The endpoint accepts no organization, branch, user or entity-type parameter. Unknown query parameters are ignored. Frontend visibility is UX only. | Backend |
| BR-02 | Organization. Every per-type query filters by the session `organization_id`. Joined rows (customer, request, catalog item, category, branch) are joined within the same organization. | Backend |
| BR-03 | Per-type eligibility (re-evaluated on every request against the caller's current role and scope). `customer`: Customer View. `service_request`: Service Request View. `quote`: Service Request View (the policy existing quote reads use). `work_order`: Work Order Read. `invoice`: Billing Review Read. A type the caller's role cannot read is not queried. Branch scope: a caller with `is_all_branches` sees every branch. Otherwise an entity is visible when its effective branch is null or in the caller's active `organization_user_branches`. Effective branch: `customers.branch_id`; `service_requests.branch_id`; `quotes.branch_id`, else its request's `branch_id`; `work_orders.branch_id`; `invoices.branch_id`. An entity that fails a check is absent from items, from ranking and from `hasMore`, so nothing reveals that it exists. | Backend |
| BR-04 | Searchable fields (fixed server-side; never chosen by the client). | Backend |
| BR-05 | Input. `q` is trimmed. After trim, fewer than 2 or more than 100 characters → `400 errors.q`. Internal spaces are kept. `limit` is optional, default 20; a value that is not an integer from 1 to 20 → `400 errors.limit`. The response `query` echoes the trimmed `q`. | Backend |
| BR-06 | Matching. Text comparison is case- and accent-insensitive: both sides go through the SA-23 unaccent function and lower-casing. `%`, `_` and the escape character in `q` are escaped and match literally. Number matching: when `q`, ignoring case, is `<digits>`, `#<digits>`, `<prefix><digits>` or `<prefix>-<digits>` with the organization's prefix for that type (`request_prefix`, `quote_prefix`, `work_order_prefix`, `invoice_prefix`), the digits are compared with the document number as text. Phones and emails are compared as stored text, with no normalization. | Backend |
| BR-07 | Ranking and limits. Each matched entity gets the best rank among its fields: 1 exact (the number equals the digits, or a text field equals `q`), 2 prefix (the number text starts with the digits, or a text field starts with `q`), 3 partial (contains). Within each type, the top 5 by rank, then `updated_at` descending, then `id` are kept. The kept items of all types are ordered by the same keys, and the first `limit` are returned. `hasMore` is true when any eligible type has more than 5 matches or the combined kept items exceed `limit`. Items keep this global order in the response. | Backend |
| BR-08 | Item content. `entityType` is one of `customer`, `service_request`, `quote`, `work_order`, `invoice`. `displayNumber` uses each document's existing display format (prefix and number), and is null for customers. `status` is the entity's status code: `active` or `inactive` for customers (`is_active`), otherwise the `request_status`, `quote_status`, `work_order_status` or `invoice_status` value. `branchId` is the effective branch (BR-03). `branchName` is that branch's current name, and is null when the branch is null or the organization has fewer than two active branches. `updatedAt` is the entity's `updated_at` in ISO 8601 UTC. Title and subtitle per type are in the table below. | Backend |
| BR-09 | Privacy and efficiency. A search writes no `audit_logs` row and no other row, and stores no query, history or recent search. The query text never appears in application logs, telemetry, exception messages or error responses. Request logging keeps logging the path only, and EF sensitive-data logging stays off. Only duration, item count, `hasMore` and the error type may be logged. At most one projected query runs per eligible type plus one for branch count and prefixes, independent of data size. Queries project only the contract columns and never load full entities, lines, notes or attachments. | Backend |
| BR-10 | Input normalization (frontend). The field accepts at most 100 characters. The frontend trims the value before every decision. Empty after trim → the hint state. One character → the short-query state. Neither sends a request. | Frontend |
| BR-11 | Requests. A request is sent 300 ms after the last change of the trimmed value when it has 2 or more characters, with `limit=20`. Each new trimmed value cancels any request in flight, and its response is discarded (switchMap). A response whose `query` differs from the current trimmed value is ignored. An unchanged trimmed value sends no new request. Query, state, results and active item are held in signals. | Frontend |
| BR-12 | Dropdown. It is anchored below the search field, about 640 px wide (never wider than the viewport minus 32 px), with a maximum height of 520 px. The header and footer stay fixed and only the results scroll. It opens when the field gains focus or its value changes, and it overlays the page without moving it. | Frontend |
| BR-13 | Results. Header "Search results" with "<n> results" ("1 result") for the returned item count. Groups follow the response order: a group is placed at the position of its first item, and the items keep their order inside it. Group labels: "Customers", "Requests", "Quotes", "Jobs", "Invoices". Each row shows the type icon (one icon per type), then "<displayNumber> · <title>" (title only when `displayNumber` is null), then the subtitle and, when `branchName` is not null, " · <branchName>" (a missing part is omitted), and a status chip. The chip uses the existing status label and severity of that entity's list. Active customers show "Customer" and inactive customers "Inactive". Long text is truncated to one line without widening the dropdown. When `hasMore` is true, "More results exist. Refine your search." is shown after the last group. Footer: keyboard hints "↑↓ Navigate", "Enter Open", "Esc Close". The design's sample rows are never copied or seeded. | Frontend |
| BR-14 | States. Empty field: "Search by name, number, email, or phone.". One character: "Enter at least 2 characters.". Loading (from the change to a searchable value until its response): four skeleton rows. No items: "No results for “<query>”." with the trimmed query. Any failure other than `401`: "Couldn't search right now." with **Retry**, which resends the current query immediately. `401` follows the existing session handling. No state shows a toast, blocks the page or disables other shell controls. | Frontend |
| BR-15 | Selection and routes. Clicking a result or pressing Enter on the active one closes the dropdown, clears the field and navigates. Routes by `entityType`: `customer` → `/customers/<id>`, `service_request` → `/requests?request=<id>`, `quote` → `/quotes/<id>`, `work_order` → `/jobs/<id>`, `invoice` → `/invoices/<id>`. The frontend never builds a route from other fields. The destination's existing authorization and `403`/`404` states apply. A route change also closes the dropdown. | Frontend |
| BR-16 | Keyboard, focus and accessibility. The field is a combobox (`role="combobox"`, `aria-expanded`, `aria-controls` pointing to a `listbox`, `aria-autocomplete="list"`, `aria-activedescendant` naming the active option). Results are `option`s grouped under group labels. When results arrive, the first is active. Arrow Down and Arrow Up move the active option, wrapping at the ends, and scroll it into view. Enter opens the active option, and does nothing when there is none. Enter never navigates to `/coming-soon/search`. Escape closes the dropdown and keeps focus in the field. A click outside the field and the dropdown closes it. Focus moving from the field into the dropdown (for example the Retry button) keeps it open. Mouse hover sets the active option. The option's accessible name contains the type, number, title, subtitle and status. Changes in result count are announced politely. Retry and options have a visible focus indicator. | Frontend |
| BR-17 | Ctrl + K (Windows/Linux) and Cmd + K (macOS) focus the field and select its text from anywhere in the staff shell, and prevent the browser default. The shortcut does nothing where the field is not rendered. | Frontend |
| BR-18 | Placement. The combobox replaces the current search field of the staff app shell top bar for every internal role, where the shell already shows it (768 px and wider). It stays available on every staff shell route. The shell below 768 px, the customer portal and the technician mobile shell are unchanged. | Frontend |
| BR-19 | Design. The dropdown reproduces `design/assets/Global search FieldOps.png` with BR-12 – BR-14 content. From 768 to 1279 px the dropdown keeps its width limit and stays within the viewport. | Frontend |

### BR-04 searchable fields and BR-08 title and subtitle

| `entityType` | Searched fields | `title` | `subtitle` |
| ------------ | --------------- | ------- | ---------- |
| `customer` | `display_name`, `legal_name`, `primary_email`, `primary_phone` | `display_name` | "Residential customer" (`person`) or "Commercial customer" (`company`) |
| `service_request` | `request_number` (BR-06), catalog item name, service category name, customer `display_name`, `guest_name` | Catalog item name, else category name, else "Service request" | Customer `display_name`, else `guest_name`, else null |
| `quote` | `quote_number` (BR-06), customer `display_name`, else the request's `guest_name` | Customer `display_name`, else the request's `guest_name`, else "Quote" | The request's display number |
| `work_order` | `work_order_number` (BR-06), `title`, customer `display_name` | `title` | Customer `display_name` |
| `invoice` | `invoice_number` (BR-06), customer `display_name` | `total` formatted in the invoice `currency` (e.g. "$485.00") | Customer `display_name`, plus " · Due <MMM d>" when `due_date` is set |

## Data and persistence impact

- Tables and columns read (from `docs/database/fieldops-schema.sql`):
  - `customers` (`id`, `organization_id`, `branch_id`, `type`, `display_name`, `legal_name`, `primary_email`, `primary_phone`, `is_active`, `updated_at`).
  - `service_requests` (`id`, `organization_id`, `branch_id`, `request_number`, `customer_id`, `catalog_item_id`, `category_id`, `guest_name`, `status`, `updated_at`), `catalog_items` name, `service_categories` name.
  - `quotes` (`id`, `organization_id`, `branch_id`, `request_id`, `customer_id`, `quote_number`, `status`, `updated_at`).
  - `work_orders` (`id`, `organization_id`, `branch_id`, `work_order_number`, `customer_id`, `title`, `status`, `updated_at`).
  - `invoices` (`id`, `organization_id`, `branch_id`, `invoice_number`, `customer_id`, `status`, `total`, `currency`, `due_date`, `updated_at`).
  - `branches` (`id`, `organization_id`, `name`, `is_active`), `organizations` (the four document prefixes), `organization_users`, `organization_user_branches`.
- No rows are written. No column changes.
- Completed, cancelled, converted, expired, void and other historical rows are included when readable. Inactive customers are included. The schema has no deleted-row state, so nothing else is excluded.
- Schema amendment **SA-23** (confirmed by the user 2026-10-20, OD-01): `CREATE EXTENSION IF NOT EXISTS unaccent;` and an `IMMUTABLE` SQL wrapper function over `unaccent` with an explicit dictionary, used by BR-06. No new indexes. The existing org-scoped indexes are reviewed during implementation and none is added. Implementation updates `docs/database/fieldops-schema.sql` first, maps the function for EF queries, and generates one migration under `--generate-migration`. The migration is never applied. The mapping is reviewed by `database-reviewer`.

## Tenant isolation and authorization

- Organization context: resolved from the authenticated session membership on every request. No client organization or branch identifier is accepted.
- Client-provided input: only `q` and `limit`, both validated (BR-05). The client cannot choose entity types, fields or columns.
- Entities of another organization, of a type the caller's role cannot read, or outside the caller's branch scope are never matched, ranked, counted in `hasMore` or returned (BR-03).
- Permission: Global Search on the endpoint, then each type's existing read policy. Every destination re-authorizes the entity when opened.

## API contracts

Contract status: Final.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/search/global` | Query `q` (required), `limit` (optional, 1–20, default 20) | `200 { query, items: [{ entityType, entityId, displayNumber \| null, title, subtitle \| null, status \| null, branchId \| null, branchName \| null, updatedAt }], hasMore }` | `400 errors.q`, `400 errors.limit`, `401`, `403` | Global Search, then BR-03 per type |

`branchName` is an addition to the proposed contract (OD-06). `entityId` and
`branchId` are UUIDs, `updatedAt` is ISO 8601 UTC. Error bodies never contain
the query text.

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Staff shell search dropdown | Four skeleton rows | Empty field: "Search by name, number, email, or phone."; one character: "Enter at least 2 characters."; no items: "No results for “<query>”." | "Couldn't search right now." + **Retry** | Types the role cannot read never appear; technician, portal and mobile shells have no search | Grouped results per BR-13, with "More results exist. Refine your search." when `hasMore` |

- Mockups: `design/assets/Global search FieldOps.png` (Approved). Its sample data, the "SR-" prefix and the "8 results" figure are illustrative; real prefixes and counts apply.
- Responsive and accessibility constraints: BR-12, BR-16, BR-18, BR-19.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| `q` under 2 or over 100 characters after trim | `400 errors.q`. The UI never sends one (BR-10) | None |
| `limit` invalid | `400 errors.limit`. The UI always sends 20 | None |
| Technician or unknown role | `403` | None |
| Network or server failure | "Couldn't search right now." + **Retry**; the page stays usable | None |
| Destination denies or no longer finds the entity | The destination's existing `403`/`404` state | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | The field is empty, holds only spaces, or holds one character after trim | The user types or waits | No request is sent; the hint or "Enter at least 2 characters." is shown |
| AC-02 | Orgs 1 and 2 with matching entities of every type, and a scoped caller in org 1 with branch A, entities in branch A, branch B and with a null branch | The caller searches | Only org 1 entities with an effective branch of A or null are returned; an all-branches caller also gets branch B |
| AC-03 | An entity whose number exactly matches `q` (with or without prefix or `#`), plus other entities of other types that match by prefix or partially and were updated later | The caller searches | The exact match is the first item and its group is shown first; prefix matches come before partial ones, ties by `updatedAt` descending |
| AC-04 | Results of each of the five types | The user clicks one, or presses Enter on the active one | The dropdown closes, the field is cleared and the BR-15 route for its `entityType` opens |
| AC-05 | An open dropdown | The user presses Escape, clicks outside, or moves focus from the field to **Retry** | Escape and the outside click close it, and Escape keeps focus in the field; focus moving into the dropdown keeps it open |
| AC-06 | Results are shown | The user uses only the keyboard (Ctrl/Cmd + K, typing, Arrow Up/Down with wrap, Enter, Escape) | Every result can be reached and opened; `aria-expanded`, `aria-controls`, `aria-activedescendant` and the listbox options follow BR-16 |
| AC-07 | A technician; an accounting user with matching jobs; and entities out of scope | They search | The technician gets `403`; accounting gets no `work_order` items and `hasMore` is not affected by the hidden jobs; out-of-scope entities are absent from items and `hasMore`, identical to non-existent ones |
| AC-08 | A request in flight | The user keeps typing, and then stops for 300 ms | The previous request is cancelled and its response is never rendered; only one request is sent after the user stops, with the latest trimmed value |
| AC-09 | A failing search and a search with no items | The responses arrive | The error state with **Retry** or "No results for “<query>”." is shown inside the dropdown; the page and other shell controls stay usable; **Retry** resends the query |
| AC-10 | Searches with distinctive text, including one that fails | They complete | No `audit_logs` row or other row is written, and the text appears in no log entry or error body |
| AC-11 | `q` with 1 or 101 characters after trim, `limit` 0, 21 or non-numeric, and extra parameters such as `type` or `field` | The endpoint is called | `400 errors.q` or `400 errors.limit` for invalid values; extra parameters have no effect on the result |
| AC-12 | Entities matching through each searched field of BR-04, a name with accents ("Martínez") and a `q` containing `%`, `_` and the escape character | The caller searches with different case and without accents | Each type matches only through its BR-04 fields, ignoring case and accents; wildcard characters match literally; fields outside BR-04 never match |
| AC-13 | 8 matching customers, 8 matching requests and 8 matching jobs | The caller searches with the default and a smaller `limit` | Each type contributes at most 5 items, the total never exceeds `limit` (20 by default), items are globally ordered by BR-07 and `hasMore` is true |
| AC-14 | Matching entities of each type, including an inactive customer, a cancelled request, a void invoice and an organization with one active branch and with two | The caller searches | Each item has the BR-08 `displayNumber`, title, subtitle, status, `branchId` and `updatedAt`; historical rows are included with their status; `branchName` is set only for the two-branch organization |
| AC-15 | 10 versus 1,000 matching rows | The search runs | The number of database queries is the same; queries project contract columns only, without loading entities or attachments |
| AC-16 | A response with several types and `hasMore = true` | The dropdown renders | The header count, groups in response order, row icon, "<number> · <title>", subtitle with branch, status chip ("Customer" / "Inactive" for customers), the more-results message and the footer hints follow BR-13 |
| AC-17 | The user is on any staff shell route | They press Ctrl + K or Cmd + K, or press Enter with no active result | The field is focused with its text selected and the browser default is prevented; Enter with no result does nothing and never opens `/coming-soon/search`; the field accepts at most 100 characters |
| AC-18 | The staff shell at 768 px and wider, the staff shell below 768 px, the customer portal and the technician shell, and a route change with the dropdown open | Each renders | Only the staff shell from 768 px shows the combobox, on every route; the others are unchanged; the route change closes the dropdown |
| AC-19 | Viewports of 1440, 1024 and 768 px | The user searches | The dropdown matches the design, BR-12 and BR-19: anchored below the field, about 640 px wide within the viewport, at most 520 px high with fixed header and footer, four skeleton rows while loading (user visual QA) |
| AC-20 | Keyboard and screen-reader use | The user searches and moves through results | Option names, group labels, polite result announcements and visible focus follow BR-16 (user visual QA) |
| AC-21 | The implementation | The schema doc, EF model and migration are reviewed | Only SA-23 (`unaccent` and its immutable wrapper) is added, with no new index or column; the migration is generated and not applied |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Query normalization: number parsing with org prefix, `#` and digits, and LIKE escaping (table-driven) | AC-03, AC-11, AC-12 |
| Backend integration | Field matching per type with case/accent insensitivity and literal wildcards (table-driven); ranking, per-type and total caps and `hasMore`; item content per type including historical rows and `branchName`; validation errors; bounded query count; no audit row and no query text in captured logs | AC-03, AC-10 – AC-15 |
| Authorization/tenant isolation | One test covering cross-organization exclusion, branch scope with null branches, all-branches callers, accounting without jobs and hidden entities not affecting `hasMore`; technician `403` | AC-02, AC-07 |
| Frontend component/service | No request under 2 characters; 300 ms debounce and cancellation (fake timers); states and Retry; grouped rendering and row content; selection clears, closes and routes per type; keyboard, Escape, outside click, focus-into-dropdown and Ctrl/Cmd + K; shell placement | AC-01, AC-04 – AC-06, AC-08, AC-09, AC-16 – AC-18 |
| Browser (user visual QA) | Design fidelity, widths and heights, skeleton, focus and screen-reader names | AC-19, AC-20 |
| Persistence review | SA-23 mapping and migration content, unapplied | AC-21 |

Expected size: about 1–2 backend unit methods, 5–7 backend integration
methods (table-driven) and 4–6 frontend methods. The authorization test is a
distinct security risk.

## Dependencies

- `authenticated-app-shell` (top bar and search field): AUDITED.
- Destinations, each with its own authorization: `/customers/:customerId`, `/requests?request=`, `/quotes/:quoteId`, `/jobs/:id`, `/invoices/:invoiceId`.
- Existing status label maps of the requests, quotes, jobs and invoices lists.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The invoice total and due date are formatted with the formatting the invoices list already uses. |
| AS-02 | The `unaccent` extension is available in the PostgreSQL 17 images used locally and in CI (it ships with contrib and is trusted). |
| AS-03 | Branch names are read at query time, so a renamed branch shows its current name. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Accent-insensitive search | `unaccent` extension (SA-23) · case-insensitive only | No | SA-23: `unaccent` with an immutable wrapper, no new indexes; migration generated, never applied (user 2026-10-20) |
| OD-02 | Request title | Catalog item / category name · description first line | No | Catalog item name, else category, else "Service request"; description is never searched (user 2026-10-20) |
| OD-03 | Customer email and phone | Customer primary fields · plus contacts | No | `display_name`, `legal_name`, `primary_email`, `primary_phone` only; literal phone match (user 2026-10-20) |
| OD-04 | Group order and 20 cap | Global rank · fixed type order | No | 5 per type, global rank, first 20; groups ordered by their first item (user 2026-10-20) |
| OD-05 | Below 768 px | Keep hidden · mobile search button | No | Unchanged; search only where the shell shows the field (user 2026-10-20) |
| OD-06 | Branch in the secondary line | Add `branchName` · `branchId` only | No | Add `branchName`, null unless the organization has two or more active branches (user 2026-10-20) |
| OD-07 | Enter and active result | First result active · none until arrows | No | First result active; Enter with no result does nothing; the Coming soon search navigation is removed (user 2026-10-20) |
| OD-08 | Defaults | — | No | `limit` 1–20 default 20; `q` validated after trim; numbers with prefix, without prefix or with `#`; status codes mapped in the frontend; quote and invoice title/subtitle per BR-08; one icon per type; query text never logged (user 2026-10-20) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-02, AC-07 |
| FR-02 | AC-11 |
| FR-03 | AC-12, AC-21 |
| FR-04 | AC-03, AC-13, AC-14 |
| FR-05 | AC-10, AC-15 |
| FR-06 | AC-01, AC-08 |
| FR-07 | AC-09, AC-16, AC-19 |
| FR-08 | AC-04 |
| FR-09 | AC-05, AC-06, AC-17, AC-20 |
| FR-10 | AC-18, AC-19 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-20 | — → DRAFT     | Created |
| 2026-10-20 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-21 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-21 | IMPLEMENTED → AUDITED | User override: final-audit returned AUDIT FAIL (open: database-reviewer approval for the unaccent migration, formal user visual QA report, and spec amendment for the post-spec UI changes: field kept after selection, mobile search button, dropdown width equal to the bar). |
