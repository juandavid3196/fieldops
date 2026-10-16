# Reports workspace

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `reports-workspace` |
| Type     | Full-stack          |
| Status   | AUDITED             |
| Created  | 2026-10-16          |
| Updated  | 2026-10-16          |
| Approved | 2026-10-16          |

## Context and objective

**Reports** in the sidebar still opens a Coming soon page. Managers, dispatchers
and accounting staff cannot review historical performance in a filterable,
reconcilable and exportable form. This feature replaces that page with a
predefined Reports workspace at `/reports` (Design 25), integrated in the
current shell. It ships five server-computed reports: Job profitability,
Invoice aging, Quote conversion, Completed jobs & on-time performance, and
Technician utilization. Each report has explicit definitions, a period, a
branch, report filters, a summary with an optional previous-period comparison,
a chart, and a sortable, paginated table whose totals reconcile with the source
modules. Rows deep-link to authorized source entities. Each report exports to
CSV and PDF; every successful export is audited as `export.generated`, and
**Export history** is derived from those audit rows. The figures reuse the
approved rules of Business Overview, Operations Dashboard, Jobs, Quotes and
Billing. Profitability is always labelled as estimated.

## Actors and permissions

The `design-18-users-and-permissions` BR-07 "Reports" row is applied per report
as BR-02 defines.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner, Operations Manager | Open, filter, export and see the export history of all five reports, for all branches | Change data from reports |
| Dispatcher | Same for Quote conversion, Completed jobs & on-time, and Technician utilization, within their branch scope | Job profitability or Invoice aging (`403`) |
| Accounting | Same for Job profitability and Invoice aging, within their branch scope | The three operational reports (`403`) |
| Viewer, Technician, unknown roles | — | Any `/reports*` endpoint (`403`); they see the existing forbidden state and no sidebar item |
| System | Resolve the organization, role and branch scope from the session. Compute every figure, sort, page and export. Write `export.generated` | Trust a client organization id; write any other data |

## Scope

- Route `/reports` in the authenticated shell; the sidebar item **Reports**
  opens it, and `/coming-soon/reports` redirects to it.
- Workspace (Design 25) with these parts:
  - Report catalog panel: search, Favorites, Recently viewed and groups.
  - Global period, branch and comparison controls.
  - Report header with favorite, **Export CSV** and **Download PDF**.
  - Report filters with **Apply filters** and **Reset**.
  - Summary cards, chart, Report definitions panel, table, totals and
    pagination.
  - **Export history** drawer.
- Five reports: `job_profitability`, `invoice_aging`, `quote_conversion`,
  `jobs_on_time`, `technician_utilization`.
- Server-side calculation, comparison, grouping, sorting, paging, CSV and PDF.
- `export.generated` for report exports. This extends `audit-trail` BR-16
  `reportType` values and the BR-02 financial flag.
- Favorites and Recently viewed stored in the browser only.

## Non-goals

- Report builder, custom fields or columns, saved reports, scheduled or
  emailed reports, free-form dashboards, embedded BI.
- Formal accounting: net income, overhead, payroll, labor costing from rates,
  tax accounting, accruals.
- Configurable targets or goals. The only target is the fixed 40 % margin of
  `business-overview` BR-09.
- Historical balances. Invoice aging is always as of today.
- Automatic refresh.
- Re-downloading a past export file.
- Row selection or bulk actions. The mockup's checkboxes are removed.
- Reproducing the mockup's sidebar, top bar, breadcrumb or "Updated just now"
  pill.
- Schema changes or migrations.
- Changes to Business Overview, Operations Dashboard, Jobs, Quotes, Invoices or
  the Audit trail UI.
- Automated browser, E2E or visual tests by agents.

## User flow

1. A member with at least one visible report selects **Reports** in the
   sidebar.
2. The system shows the workspace with the report from the URL. With no report
   in the URL, it shows the first visible report in catalog order, with
   **This month**, the default branch and comparison on.
3. The member changes the period, branch or report filters and selects
   **Apply filters**. The URL updates and the report reloads from the server.
4. The member sorts by a column or pages through the table, and the server
   returns the new page. The member opens a row link to a source entity.
5. The member stars the report, or reopens one from Favorites or Recently
   viewed.
6. The member selects **Export CSV** or **Download PDF**. The system downloads
   the file built from the applied state and records `export.generated`.
7. The member opens **Export history**, reviews their recent report exports,
   and selects **Open report** to restore one export's report and filters.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | `/reports` must show only the reports the caller's role may see, grouped in the catalog, and the backend must reject every report and endpoint outside the caller's permissions (BR-01, BR-02). |
| FR-02 | Every figure, row, link and export must be limited to the session organization and the caller's branch scope, with the selected branch authorized server-side (BR-03). |
| FR-03 | The period, previous period, granularity, time zone and URL state must follow BR-04 and BR-05. |
| FR-04 | Job profitability must list jobs completed in the period with their estimated revenue, cost, profit and margin, its summary, chart and totals (BR-08). |
| FR-05 | Invoice aging must list open invoices as of today by aging bucket, reconciling with the Invoices hub (BR-09). |
| FR-06 | Quote conversion must report sent and accepted quotes by event date in the period, with rows that reconcile with both counts (BR-10). |
| FR-07 | Completed jobs & on-time must report scheduled visits in the period with completion, on-time result and duration variance, plus the separate Jobs completed metric (BR-11). |
| FR-08 | Technician utilization must report the capacity, load, overtime and delivery of each active technician over the period (BR-12). |
| FR-09 | The summary must support an optional previous-period comparison (BR-06). |
| FR-10 | Sorting and pagination must run on the server with per-report allowlists, and the totals must cover the whole filtered set (BR-07). |
| FR-11 | Rows must deep-link only to source entities the caller may open (BR-13). |
| FR-12 | CSV and PDF exports must use the applied state and the same permissions, enforce volume limits, escape CSV injection and record `export.generated` only after success (BR-14 … BR-16). |
| FR-13 | **Export history** must list the caller's own recent report exports from the audit trail and reopen them (BR-17). |
| FR-14 | Favorites and Recently viewed must be kept per user and organization in the browser only (BR-18). |
| FR-15 | The page must render the Design 25 layout with the documented deviations, Report definitions, states, navigation and accessibility (BR-19 … BR-23). |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Catalog. Keys, groups, names and descriptions, in catalog order: <ul><li>**Billing & payments**: `job_profitability` "Job profitability", "Compare revenue, estimated costs, and estimated gross margin for completed jobs."; `invoice_aging` "Invoice aging", "Review open balances by days past due as of today."</li><li>**Sales & quotes**: `quote_conversion` "Quote conversion", "Track quotes sent and accepted in the period."</li><li>**Operations**: `jobs_on_time` "Completed jobs & on-time performance", "Review scheduled visits, completion, on-time arrival, and duration."; `technician_utilization` "Technician utilization", "Compare available and scheduled hours, overtime, and delivery by technician."</li></ul> A group is shown only when it has at least one visible report, with the visible count beside it. | Both |
| BR-02 | Access matrix (the role comes from the session membership): <ul><li>`owner`, `operations_manager`: all five reports.</li><li>`dispatcher`: `quote_conversion`, `jobs_on_time`, `technician_utilization`.</li><li>`accounting`: `job_profitability`, `invoice_aging`.</li><li>`viewer`, `technician` and unknown roles: none.</li></ul> A role with no report gets `403` on every `/reports*` endpoint before any read. A known report outside the caller's set → `403` on its data and export endpoints. An unknown `reportKey` → `404`. Frontend visibility is UX only. | Backend |
| BR-03 | Scope. No request carries an organization id; any such parameter is ignored. <ul><li>`owner`, `operations_manager` and members with `is_all_branches` have every branch of the organization, active or inactive, in scope. `dispatcher` and `accounting` members without `is_all_branches` have only their `organization_user_branches`.</li><li>**All branches** = every in-scope branch. Inactive branches are included and labelled "(Inactive)".</li><li>A `branchId` that is in scope (active or inactive) selects that branch. Unknown, foreign or out-of-scope → `404 branch_not_found`, identical in every case.</li><li>Branch attribution reuses the source modules: `work_orders.branch_id` for jobs and visits, `invoices.branch_id` for invoices, `technician_profiles.branch_id` for technicians. A quote follows `quotes-management` BR-02: visible when its request is visible, branch = `quotes.branch_id`, falling back to the request `branch_id`. A quote with no branch counts only with All.</li><li>`categoryId` must be a service category of the organization (active or inactive), else `400 errors.categoryId`. `technicianId` must be a technician profile whose branch is in scope; unknown, foreign or out-of-scope → `404 technician_not_found`.</li></ul> No response, link or export contains data of another organization or of an out-of-scope branch. | Backend |
| BR-04 | Period and time zone. Every date is a local date in `organizations.timezone`, and a day is `[00:00, 24:00)` local. Presets, custom range, API validation (`from` ≤ `to` ≤ today, at most 366 days, `400 errors.from` / `errors.to`), previous period (same length L, ending the day before `from`) and granularity (`day` for L ≤ 31, `week` Monday-start clipped for L ≤ 92, otherwise `month` clipped) follow `business-overview` BR-03 exactly. Default `this_month`. The comparison label reads "Compare to previous period" with the previous range below it ("MMM d, yyyy – MMM d, yyyy"). Table dates and times use the organization time zone ("MMM d, yyyy"; "MMM d, yyyy h:mm a" for times). `invoice_aging` does not use the period (BR-09). | Both |
| BR-05 | URL state. <ul><li>`report` (a BR-01 key; absent or not visible to the caller → the first visible report, `replaceUrl`).</li><li>`range`, `from`, `to` (BR-04).</li><li>`branch` (absent = default: All with ≥ 2 in-scope branches, else the only one).</li><li>`compare` (`false` only when off).</li><li>Report filters (BR-08 … BR-12): `status`, `category`, `technician`, `bucket`, `event`, `onTime`. Every report filter defaults to `all` (absent from the URL); `category` and `technician` mean all categories and all technicians when absent.</li><li>`sort`, `direction`, `page` (absent = 1), `pageSize` (`10` default, `25`, `50`).</li></ul> Reload and back restore the view. An unknown, invalid or not-applicable value is replaced by its default with `replaceUrl` before any request. A `404` branch or technician resets that value to its default. Changing the report clears its filters, sort and page and keeps the period, branch and compare. | Frontend |
| BR-06 | Comparison. The data response always carries the previous-period value of every summary metric (`null` where not applicable). The **Compare to previous period** toggle (on by default) only changes the display and the export, and never refetches. With comparison on: <ul><li>Count, money and hour metrics show the `business-overview` BR-11 delta: "▲ +12.4%" / "▼ −3.1%" / "No change", with an icon plus text, hidden when previous = 0.</li><li>Percent metrics show "vs <p>% previous", or "vs — previous" when the previous value is null.</li><li>Minute metrics (Avg duration variance) show the previous value as text, with no relative delta: "vs +<n> min previous", "vs −<n> min previous", "vs On estimate previous" at 0, or "vs — previous" when null.</li></ul> Tables and charts show the current period only. For `invoice_aging` the toggle is disabled with the note "Aging is calculated as of today." and no delta is shown. | Both |
| BR-07 | Sorting, paging and totals. `sort` must be in the report's allowlist (BR-08 … BR-12; unknown → `400 errors.sort`). `direction` = `asc` or `desc` (else `400 errors.direction`). The default sort and direction come from the report's rule. Ties fall back to the row's display number or name ascending, then id, so paging is stable. `page` ≥ 1 (else `400 errors.page`). `pageSize` ∈ {10, 25, 50} (else `400 errors.pageSize`). When absent from the API query: `page` = 1, `pageSize` = 10, `sort` and `direction` = the report's default, and each report filter = `all` (no category, no technician). A page past the last returns empty `items` with the real `total`, and the frontend requests the last page (`replaceUrl`). `totals` and the summary always cover the whole filtered set, never only the page. Filtering, sorting and paging run in the database except where BR-12 computes per-technician load in memory. The data endpoint runs a bounded number of queries, independent of page size (no per-row queries). | Backend |
| BR-08 | **Job profitability.** <ul><li>**Rows**: work orders in scope with status `completed` or `approved_for_billing` whose completion date (`business-overview` BR-08: latest `actual_completed_at` among non-cancelled visits) falls in the period.</li><li>**Filters**: `status` = `all` "All completed" (default), `completed` "Completed", `approved_for_billing` "Approved for billing"; `category` (`work_orders.service_category_id`); `technician` (the active primary assignee of the visit with the latest completion, as `completed-jobs-review` BR-03 "Completed by").</li><li>**Revenue** = Σ (`subtotal` − `discount_total`) of the work order's recognized invoices (status not `draft` or `void`), whatever their issue date, tax excluded. **Estimated cost** = the `business-overview` BR-05 line cost of those invoices (`quantity` × source quote line or visit material `unit_cost`, 0 without a source). **Estimated gross profit** = revenue − estimated cost; it can be negative. **Estimated margin** = profit ÷ revenue × 100, one decimal, "—" at revenue 0. Below 40.0 % follows `business-overview` BR-09 (icon plus "Below 40% target").</li><li>A job with no recognized invoice is **Not invoiced**: a "Not invoiced" tag, revenue, cost, profit and margin "—", contributing 0 to every sum.</li><li>**Summary**: Completed jobs (row count), Not invoiced (count), Revenue, Estimated cost, Estimated gross profit, Estimated gross margin (Σ profit ÷ Σ revenue).</li><li>**Chart** "Revenue vs estimated cost by service category": horizontal grouped bars per category with revenue > 0 in the filtered set, by revenue descending then name. Up to 10 bars, the rest summed as "Other categories". Legend: Revenue, Estimated cost.</li><li>**Columns**: Job (WO-n), Completed, Customer, Service, Technician ("Unassigned" when none), Revenue, Est. cost, Est. gross profit, Est. margin, Branch, Invoicing ("Invoiced" / "Not invoiced").</li><li>**Sort allowlist**: `completedAt` (default, `desc`), `job`, `customer`, `category`, `technician`, `revenue`, `estimatedCost`, `grossProfit`, `margin`, `branch`. Not-invoiced rows sort after invoiced rows for the money and margin keys in either direction.</li><li>**Totals row**: Σ revenue, Σ estimated cost, Σ profit, total margin.</li><li>**Reconciliation**: with `status = all` and no category or technician filter, Completed jobs equals `business-overview` "Jobs completed" for the same period and branch, and the rows equal the Jobs **Completed** tab entries completed in the period. Revenue here is by completion, so it differs from Business Overview revenue (by issue date); the definitions say so.</li></ul> | Backend |
| BR-09 | **Invoice aging.** <ul><li>Always as of today (organization time zone). The period and the comparison do not apply to the figures. The data and export endpoints still validate `from`, `to` (BR-04) and `compare` (BR-15) exactly as for the other reports, with the same `400` errors, but valid values do not change any figure, row or file content. The response has `asOf` = today, `previousFrom`/`previousTo`/`granularity` null and every summary `previous` null, and the UI disables the date range and the toggle with the note "Aging is calculated as of today."</li><li>**Rows**: invoices in scope with stored status `sent` or `partially_paid`.</li><li>**Bucket** by days past `due_date` per `invoices-payments-management` BR-05: `current` "Current" (not past due), `1_30` "1–30 days", `31_60` "31–60 days", `60_plus` "60+ days".</li><li>**Display status**, labels and `daysOverdue` follow `invoices-payments-management` BR-03.</li><li>**Filters**: `bucket` = `all` (default) or a bucket key; `status` = `all` (default), `sent`, `partially_paid`, `overdue` (display status).</li><li>**Summary**: Outstanding (Σ `balance_due`), Overdue (Σ `balance_due` of overdue rows), Overdue invoices (count), and the four bucket amounts.</li><li>**Chart** "Balance by aging bucket": four labelled bars with their amounts.</li><li>**Columns**: Invoice (INV-n), Customer, Job (WO-n), Issue date, Due date, Days overdue ("—" when not overdue), Bucket, Status, Total, Balance due, Branch.</li><li>**Sort allowlist**: `daysOverdue` (default, `desc`), `dueDate`, `issueDate`, `invoice`, `customer`, `total`, `balanceDue`, `branch`.</li><li>**Totals**: Σ total, Σ balance due.</li><li>**Reconciliation**: with no filter, Outstanding, Overdue and the buckets equal `invoices-payments-management` BR-04/BR-05 for the same branch.</li></ul> | Backend |
| BR-10 | **Quote conversion.** <ul><li>Event-based, per `business-overview` BR-06. A quote is **sent in period** when its earliest non-null `quote_versions.sent_at` local date is in the period. It is **accepted in period** when it has a `quote_responses` row with `response = 'approved'` whose `responded_at` local date is in the period.</li><li>**Rows**: visible quotes (BR-03) that are sent in period, accepted in period, or both, each with both flags.</li><li>**Filters**: `event` = `all` (default), `sent`, `accepted`; `status` = `all` or a `quotes-management` BR-03 display status other than `draft`; `category` (the request `category_id`; quotes without one are "Uncategorized" and match only `all`).</li><li>**Summary**: Quotes sent, Quotes accepted, Conversion = accepted ÷ sent × 100 as an integer ("—" when sent = 0), Sent value, Accepted value. Sent value = Σ `total` of each sent-in-period quote's earliest sent version. Accepted value = Σ approval-response `total` (`quotes-management` BR-11). Only amounts in `organizations.currency` are summed; other-currency quotes are counted but not summed, and the UI notes "<n> quotes in other currencies are counted but not summed." when n > 0.</li><li>**Chart** "Quotes sent vs accepted": grouped bars per BR-04 bucket (by the sent date and by the accepted date).</li><li>**Columns**: Quote (Q-n), Customer (display name, else the request `guest_name`), Request (REQ-n), Service, Branch ("No branch" when null), First sent ("—" when not sent in period), Accepted ("—" when not accepted in period), Status (BR-03 label), Total (shown version, with currency), Accepted value ("—" when not accepted in period).</li><li>**Sort allowlist**: `firstSentAt` (default, `desc`; rows without one last), `acceptedAt`, `quote`, `customer`, `total`, `status`, `branch`.</li><li>**Totals**: sent count, accepted count, Σ accepted value.</li><li>The conversion can exceed 100 % when acceptances of quotes sent earlier outnumber new sends; it is shown as computed.</li></ul> | Backend |
| BR-11 | **Completed jobs & on-time performance.** <ul><li>**Rows**: visits in scope whose work order status is not `draft` or `cancelled`, with `scheduled_start` in the period and status not `unscheduled` or `cancelled` (the `operations-dashboard` BR-14 denominator).</li><li>**Status groups** per `operations-dashboard` BR-06: Scheduled, Active, Completed.</li><li>**Reference instant**: `ref` = min(now, end of `to`).</li><li>**Arrival limit** and eligibility follow `operations-dashboard` BR-09. **On-time result**: `on_time` "On time" (`actual_started_at` ≤ limit), `late` "Late" (started after the limit), `not_started` "Not started" (not started, status `scheduled`, `assigned` or `on_the_way`, limit < `ref`), `not_due` "Not due yet" (any other not-started visit; not eligible).</li><li>**Duration**: estimated = the work order `estimated_duration_minutes`; actual = `actual_completed_at` − `actual_started_at` − `pause_seconds`, in minutes, for completed-group visits with both timestamps; variance = actual − estimated ("+n min", "−n min", "On estimate"); "—" when either is missing.</li><li>**Filters**: `status` = `all`, `scheduled`, `active` or `completed` (group); `onTime` = `all` or a result key; `category` (work order category); `technician` (visits with an active assignment of that technician, `operations-dashboard` BR-05).</li><li>**Summary**: Scheduled visits (rows), Completed visits (completed group), Completion rate (completed ÷ scheduled, integer %), On-time arrival (on time ÷ eligible, integer %; "—" when none are eligible), Avg duration variance (mean variance in whole minutes over visits with both durations; "—" when none), and **Jobs completed**.</li><li>**Jobs completed** is a separate work-order metric: `business-overview` BR-08 for the same period and branch, with the category filter applied, and the status, on-time and technician filters not applied. Its card shows the caption "Work orders completed in the period".</li><li>**Chart** "Arrival performance": stacked bars per BR-04 bucket by `scheduled_start`: On time, Late, Not started.</li><li>**Columns**: Job (WO-n · Visit n), Scheduled, Customer, Service, Technician (primary active assignment; "Unassigned"), Status (group label), Arrival limit, Actual start ("—"), On-time result, Est. duration, Actual duration, Variance, Branch.</li><li>**Sort allowlist**: `scheduledStart` (default, `desc`), `job`, `customer`, `technician`, `status`, `onTime`, `variance`, `branch`.</li><li>**Totals**: row count, completed count, on-time count of eligible.</li><li>**Reconciliation**: with no report filter, On-time arrival equals the `business-overview` BR-15 overall on-time value for the same period and branch.</li></ul> | Backend |
| BR-12 | **Technician utilization.** <ul><li>**Rows**: technician profiles with `status = 'active'` whose branch is in the selection, counted now (`business-overview` BR-17).</li><li>**Filter**: `technician`.</li><li>Per technician, over every day of the period: available and scheduled minutes per `operations-dashboard` BR-08. Utilization = scheduled ÷ available, integer % ("—" when available = 0). Scheduled overtime = Σ per day of max(0, scheduled − available) minutes. Completed visits = completed-group visits with `actual_completed_at` in the period on which the technician has an active assignment. On-time arrival = the BR-11 result over the visits in the period with an active assignment of the technician.</li><li>Hours display per `business-overview` BR-10.</li><li>**Summary**: Technicians, Available hours, Scheduled hours, Utilization (Σ scheduled ÷ Σ available), Scheduled overtime hours.</li><li>**Chart** "Utilization by technician": horizontal bars of the top 15 technicians by scheduled hours, then name, with the integer % as text. No target line.</li><li>**Columns**: Technician, Branch, Available h, Scheduled h, Utilization, Overtime h, Completed visits, On-time arrival.</li><li>**Sort allowlist**: `utilization` (default, `desc`; "—" last), `technician`, `branch`, `availableMinutes`, `scheduledMinutes`, `overtimeMinutes`, `completedVisits`, `onTime`.</li><li>**Totals**: Σ available, Σ scheduled, total utilization, Σ overtime, Σ completed visits.</li><li>**Reconciliation**: with no filter, Technicians, Utilization and Scheduled overtime equal `business-overview` BR-17 for the same period and branch.</li></ul> | Backend |
| BR-13 | Deep links. Each row returns `links` with the id of each linked entity, or null when the entity no longer resolves, is outside the caller's branch scope, or the caller's role cannot read that module under its existing policy. Destinations: <ul><li>job → `/jobs/:id`</li><li>visit → its job</li><li>customer → `/customers/:id`</li><li>invoice → `/invoices/:id`</li><li>quote → `/quotes/:id`</li><li>request → `/requests?request=<id>`</li><li>technician → `/team/:technicianId/skills-availability`</li></ul> A null link renders as plain text. Every destination keeps its own authorization. | Both |
| BR-14 | CSV export. `GET /reports/{reportKey}/export?format=csv` uses the data endpoint query without `page`/`pageSize`, plus `compare`. <ul><li>Response: `text/csv; charset=utf-8`, `Content-Disposition: attachment`. File `<report-key with hyphens>-<from>-to-<to>.csv`, or `invoice-aging-<asOf>.csv`.</li><li>Layout: two blocks in one file, each starting with its own header row, separated by one empty line. Every row of both blocks starts with a `Section` column.</li><li>Rows block: header `Section`, then the report's table columns (BR-08 … BR-12, exact labels), then `Currency` when the report has money columns. Each data row has `Section` = `Rows`, one per filtered row in the applied sort, followed by one row with `Section` = `Totals` holding the totals row (empty cells where the table shows none).</li><li>Summary block: header `Section`, `Metric`, `Current`, `Previous`, `Unit`. One row per summary metric with `Section` = `Summary`, the metric label, its values and its unit (`currency code`, `count`, `percent`, `hours`, `minutes`). `Previous` is empty when `compare=false` or the value is null.</li><li>Formats: money has 2 decimals and no symbol; percents have the table precision; durations are in minutes; dates are `yyyy-MM-dd` and times `yyyy-MM-dd HH:mm` in the organization time zone; "—" values are empty cells; tags are their text labels.</li><li>Escaping follows `completed-jobs-review` BR-21: cells starting with `=`, `+`, `-`, `@`, tab or carriage return are prefixed with `'`, and fields with comma, quote or newline are quoted.</li><li>Never exported: customer emails, phones, addresses, notes, review comments or audit data.</li><li>More than 5,000 rows → `409 export_too_large` "Narrow the filters to export 5,000 rows or fewer.", checked before the file is built.</li></ul> | Backend |
| BR-15 | PDF export. `GET /reports/{reportKey}/export?format=pdf`, same query, rendered on the server with the existing PDF library and embedded fonts. <ul><li>Response: `application/pdf`, `Content-Disposition: attachment`, file name as BR-14 with `.pdf`.</li><li>Content: organization name, report name, period (or "As of <date>"), branch, applied filters by label, "Generated <MMM d, yyyy h:mm a> <time zone>", the summary (with previous values when `compare=true`), the chart values as a table, the Report definitions (BR-20), every row with the totals row, and for `job_profitability` the BR-20 disclaimer.</li><li>Landscape pages, repeated table header and page numbers.</li><li>The same exclusions as BR-14 apply.</li><li>More than 1,000 rows → `409 export_too_large` "Narrow the filters to export 1,000 rows or fewer.", checked before rendering.</li></ul> `format` other than `csv` or `pdf` → `400 errors.format`. | Backend |
| BR-16 | Export audit. After the file is fully built for a `200` response and before it is returned, insert one `export.generated` row per `audit-trail` BR-16, with these values: <ul><li>`reportType` = `report_<reportKey>`.</li><li>`metadata` = `{ reportType, format, filters, rowCount }`. `filters` holds the normalized `from`, `to` (or `asOf`), `branchId`, report filter codes and ids, `sort`, `direction` and `compare`; no names or free text.</li><li>`branch_id` = the selected branch or null.</li></ul> `report_job_profitability` and `report_invoice_aging` are financial for `audit-trail` BR-02 and BR-10; the other three are not. `400`, `403`, `404` and `409` write nothing. If the insert fails, the export returns `500` with no file. | Backend |
| BR-17 | Export history. `GET /reports/exports` returns the caller's own `export.generated` rows of the session organization whose `reportType` starts with `report_`, with `occurred_at` in the last 90 days, newest first, at most 20. Rows for a report the caller can no longer see are omitted. Each item: `id`, `occurredAt`, `reportKey`, `reportName`, `format`, `rowCount`, `filters`. Drawer "Export history" (400 px desktop, full width below 640 px): Date ("MMM d, yyyy h:mm a"), Report, Format (CSV/PDF), Rows, a filters summary (period or "As of <date>", and the branch name or "All branches"; a branch no longer in scope shows "Unavailable branch"), and **Open report**. **Open report** closes the drawer and navigates to `/reports` with that export's report, period (as `custom`), branch, filters, sort and compare, page 1. Values that are no longer valid fall back per BR-05. Empty: "No report exports in the last 90 days." Error: "We couldn't load export history." with **Try again**. The drawer note reads "Files aren't stored. Open a report to export it again." | Both |
| BR-18 | Favorites and Recently viewed are browser-only preferences in `localStorage`, under a key scoped to the organization id and user id. <ul><li>Favorites: an ordered set of report keys, toggled by the star in the report header (accessible name "Add to favorites" / "Remove from favorites") and listed in the panel with their count.</li><li>Recently viewed: the last 5 distinct reports successfully loaded, newest first.</li><li>Unknown keys and reports not visible to the caller are ignored. A storage read or write failure leaves both lists empty and the page fully usable.</li><li>Nothing is sent to the server.</li></ul> | Frontend |
| BR-19 | Filter interaction. The date range, branch and report filters are edited as a draft and take effect only on **Apply filters**, which writes the URL, resets to page 1 and reloads. **Reset** restores the period, branch and report-filter defaults, the default sort and page 1, and reloads. Sort headers, page and page size reload immediately. The compare toggle applies immediately without a request (BR-06). A refresh button reloads the applied state on demand; there is no automatic refresh. "Updated just now" (< 1 minute) or "Updated <n> min ago" sits beside it and updates each minute. Changing the report or applied state cancels in-flight requests. Responses are `Cache-Control: no-store`. | Frontend |
| BR-20 | Report definitions panel (Design 25 right panel), with an info icon, per report: <ul><li>`job_profitability`: Revenue "Invoiced amount of recognized invoices for jobs completed in the period, net of discounts, excluding tax. Revenue is grouped by job completion, so it can differ from revenue by invoice date in Business Overview."; Estimated cost "Quantity × configured unit cost of the product or service on each invoice line."; Estimated gross profit "Revenue minus estimated cost."; Estimated gross margin "Estimated gross profit ÷ revenue × 100."; Not invoiced "Completed jobs with no issued invoice yet. They count as jobs but add no revenue or cost."; plus the disclaimer, verbatim from `business-overview` BR-19: "Profitability figures are operational estimates based on configured product and service costs. They exclude overhead, payroll, payroll taxes, depreciation, and other accounting expenses. They are not net income and not a substitute for accounting software."</li><li>`invoice_aging`: Outstanding, Overdue and each bucket, defined per BR-09.</li><li>`quote_conversion`: Quotes sent, Quotes accepted and Conversion, plus "Conversion can exceed 100% when quotes sent in earlier periods are accepted in this one."</li><li>`jobs_on_time`: Scheduled visits, Completion rate, On-time arrival ("Started by the arrival window end, or within 15 minutes of the scheduled start."), Duration variance and Jobs completed.</li><li>`technician_utilization`: Available hours, Scheduled hours, Utilization, Scheduled overtime.</li></ul> Every profitability label contains "Estimated" or "Est.". The panel shows no costs, targets or figures that the schema does not hold. | Frontend |
| BR-21 | Page copy and layout. Title "Reports", subtitle "Analyze operations, sales, billing, and team performance.", **Export history** in the header. The catalog panel has search ("Search reports…", client-side match on name and description), Favorites, Recently viewed and the BR-01 groups. The report header has the star, name, description, **Export CSV** and **Download PDF**. Then the filter row (report filters), the summary cards, the chart beside Report definitions, and the table with sortable headers (`aria-sort`), totals row and footer "Showing <a>–<b> of <total> <noun>" with rows per page 10/25/50. From 1280 px: catalog panel, report content and definitions as in Design 25. 768–1279 px: the catalog panel becomes a **Reports** button opening it as a drawer, and the definitions move below the chart. Below 768 px: one column, summary cards 2 per row, filters one per row, the table scrolls horizontally inside its card, no page horizontal scroll at 375 px, touch targets ≥ 44 px. | Frontend |
| BR-22 | States. <ul><li>Loading: controls disabled until options load; summary, chart and table skeletons.</li><li>Empty (zero rows): summary cards with zeros or "—", the chart text "No data for this report and filters.", the table text "No rows match these filters." with **Reset**.</li><li>Errors: a load failure shows "We couldn't load this report." with **Try again**; a refresh failure keeps the data and shows "Couldn't refresh." with **Try again**.</li><li>No branch in scope: "No branches available." and no data request.</li><li>A role with no report: the existing in-page forbidden state, with no data request. `401` uses the existing session handling.</li><li>Export: the button is busy and disabled while running; `409` shows the server limit message of BR-14 / BR-15; any other failure shows "We couldn't export the report. Try again."; success downloads with the server file name.</li></ul> | Frontend |
| BR-23 | Navigation and accessibility. <ul><li>The sidebar item **Reports** (same icon and position) links to `/reports` and is shown only to `owner`, `operations_manager`, `dispatcher` and `accounting` (UX only). `/coming-soon/reports` redirects to `/reports` (`replaceUrl`).</li><li>Charts have a visually hidden table of the plotted values.</li><li>Deltas, below-target marks, on-time results and tags use text plus icon, never color alone.</li><li>The star, the toggle and the drawer are keyboard-operable and labelled. The drawer traps focus and returns it to **Export history**.</li><li>Result changes are announced politely ("<total> <noun>").</li></ul> | Frontend |

## States and transitions

None. The feature is read-only, apart from the `export.generated` audit insert.

## Data and persistence impact

- Tables and columns read (from `docs/database/fieldops-schema.sql`):
  - Organization and access: `organizations` (`name`, `timezone`, `currency`), `branches` (`name`, `is_active`), `organization_users` (`role_id`, `is_all_branches`, `status`), `organization_user_branches`, `roles`.
  - Invoices: `invoices` (`branch_id`, `customer_id`, `work_order_id`, `invoice_number`, `status`, `issue_date`, `due_date`, `subtotal`, `discount_total`, `total`, `balance_due`, `currency`), `invoice_lines` (`quantity`, `source_quote_line_id`, `source_visit_material_id`), `quote_lines` (`unit_cost`), `visit_materials` (`unit_cost`).
  - Quotes and requests: `quotes` (`branch_id`, `quote_number`, `current_version_no`, `status`), `quote_versions` (`version_no`, `sent_at`, `valid_until`, `total`, `currency`), `quote_responses` (`response`, `responded_at`, `total`), `service_requests` (`branch_id`, `request_number`, `category_id`, `guest_name`, `customer_id`).
  - Jobs and customers: `work_orders` (`branch_id`, `work_order_number`, `status`, `service_category_id`, `customer_id`, `estimated_duration_minutes`), `service_categories` (`name`, `is_active`), `customers` (`display_name`), `visits` (`visit_number`, `status`, `scheduled_start`, `scheduled_end`, `arrival_window_end`, `actual_started_at`, `actual_completed_at`, `pause_seconds`), `visit_assignments` (`technician_id`, `is_primary`, `unassigned_at`).
  - Team: `technician_profiles` (`branch_id`, `status`, names), `technician_weekly_availability`, `technician_breaks`, `technician_exceptions`, `assessments` (load per `operations-dashboard` BR-08).
  - Audit: `audit_logs` (read for BR-17 by `organization_id`, `actor_user_id`, `action`, `occurred_at`, `metadata`; insert for BR-16).
  - Join keys: `invoices.work_order_id`, `invoice_lines.invoice_id`, `quotes.request_id`, `quotes.customer_id`, `quote_versions.quote_id`, `quote_responses.quote_version_id` (to its quote through `quote_versions.quote_id`), `service_requests.customer_id`, `work_orders.customer_id`, `visits.work_order_id`, `visit_assignments.visit_id`, `technician_profiles.organization_user_id`.
  - No payment tables are read.
- Writes: only the BR-16 `audit_logs` insert.
- Schema amendments required: None. The existing `ix_audit_org_occurred` and `ix_audit_actor` indexes serve BR-17.
- Queries are bounded by the period (≤ 366 days), the organization, the branch scope and the page.

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. Every
  query is restricted to that `organization_id`, and no endpoint accepts an
  organization id.
- Client-provided identifiers are authorized server-side per BR-03:
  - `branchId` (in scope, else `404 branch_not_found`).
  - `technicianId` (in scope, else `404 technician_not_found`).
  - `categoryId` (organization category, else `400`).
  - `reportKey` (catalog key, else `404`; not permitted for the role → `403`).
- Report permissions: the BR-02 matrix, enforced on data, export and history. History is limited to the caller's own rows and visible reports.
- Branch-limited members never receive rows, totals, links, chart values or export content from other branches. With All, only their branches are included.
- Row links: computed per BR-13 for the caller; the destinations re-check
  their own policies.
- Responses and exports contain no customer contact data, notes, review
  comments or audit before/after data.

## API contracts

Contract status: Final. Errors use ProblemDetails, and `400` carries `errors`.
Responses are `no-store`. Dates are `YYYY-MM-DD` in the organization time zone,
and instants are ISO 8601 UTC. Money is decimal in `currency`, percentages are
numbers (`null` when the BR shows "—"), and durations are minutes.

`Metric` = `{ key, label, unit (currency/count/percent/hours/minutes), value | null, previous | null }`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/reports/options` | — | `200 { timezone, currency, today, marginTarget, reports: [{ key, group, name, description }] (BR-01 order, visible only), branches: [{ id, name, isActive }] (in scope, by name), categories: [{ id, name, isActive }], technicians: [{ id, name, isActive, branchId }] (in scope, by name) }` | `401`, `403` | BR-02 any report |
| GET | `/reports/{reportKey}` | Query: `from`, `to` (validated for every report; unused in `invoice_aging` figures, BR-09), `branchId?`, report filters (BR-08 … BR-12: `status`, `categoryId`, `technicianId`, `bucket`, `event`, `onTime`), `sort`, `direction`, `page`, `pageSize` | `200 { reportKey, from \| null, to \| null, previousFrom \| null, previousTo \| null, asOf, timezone, currency, granularity \| null, marginTarget, summary: [Metric], notes: { otherCurrencyCount \| null }, chart: { kind (category_bars/bucket_bars/grouped_series/stacked_series/technician_bars), series: [{ key, label }], points: [{ label, start \| null, values: { <seriesKey>: number } }] }, items: [row per BR-08 … BR-12 columns + id + links: { job, visit, customer, invoice, quote, request, technician } (ids or null)], totals: { <column>: value \| null }, total, page, pageSize }` | `400` (`errors.<param>`), `401`, `403`, `404` (`reportKey`, `branch_not_found`, `technician_not_found`) | BR-02 for the report |
| GET | `/reports/{reportKey}/export` | Data query without `page`/`pageSize`, plus `format` (`csv`/`pdf`, required) and `compare` (`true` default/`false`) | `200 text/csv` (BR-14) or `200 application/pdf` (BR-15), attachment, then the BR-16 row | `400` (incl. `errors.format`, `errors.compare`), `401`, `403`, `404`, `409 export_too_large`, `500` | BR-02 for the report |
| GET | `/reports/exports` | — | `200 { items: [{ id, occurredAt, reportKey, reportName, format, rowCount, filters }] }` (BR-17) | `401`, `403` | BR-02 any report |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/reports` workspace | Controls disabled until options load; summary, chart and table skeletons (BR-22) | "No data for this report and filters." / "No rows match these filters." with **Reset**; "No branches available." | "We couldn't load this report." with **Try again**; "Couldn't refresh."; `404` branch or technician resets (BR-05) | No visible report: existing forbidden state, no data request; a report not permitted in the URL is replaced by the first visible report | BR-08 … BR-12 content, definitions, links |
| Export (CSV / PDF) | Button busy and disabled | — | `409` limit message; "We couldn't export the report. Try again." | Buttons only on visible reports | File downloaded with the server name |
| Export history drawer | Row skeletons | "No report exports in the last 90 days." | "We couldn't load export history." with **Try again** | Own rows only | BR-17 list and **Open report** |

- Mockups: `design/assets/25-design.png` (Approved). The data in it are
  examples. This spec fixes the following deviations:
  - The current shell replaces the mockup's sidebar, top bar, search, branch
    pill and breadcrumb.
  - The period and branch appear once (global bar), not again in the report
    filter row.
  - The catalog panel lists only the five reports in three groups; the
    mockup's extra report names and the "Customers", "Team" and "Audit" groups
    are not shown.
  - Row checkboxes are removed.
  - The cost definition omits "recorded labor estimates", and the disclaimer
    follows BR-20.
  - The 28.6 % highlight is the BR-08 below-target mark with icon and text.
  - Refresh and "Updated …" are added (BR-19).
- Responsive and accessibility: BR-21, BR-23.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Role with no report | `403`; forbidden state | None |
| Report not permitted for the role | `403`; the URL falls back to the first visible report | None |
| Unknown `reportKey` | `404`; the URL falls back to the first visible report | None |
| Branch or technician unknown, foreign or out of scope | `404 branch_not_found` / `technician_not_found`; the filter resets to its default | None |
| Invalid period, filter, sort, direction, page, page size, format or compare | `400 errors.<param>`; invalid URL values never reach the API (BR-05) | None |
| Export over the limit | `409 export_too_large` with the BR-14 / BR-15 message | None (no audit row) |
| Export audit insert fails | `500`; "We couldn't export the report. Try again." | None (no file, no row) |
| Load / refresh failure | BR-22 messages | None |
| Browser storage unavailable | Favorites and Recently viewed empty; page usable | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | Members of each role (parameterized) | They call `/reports/options`, open `/reports` and render the shell | Owner and Operations Manager see all five reports. Dispatcher sees Quote conversion, Completed jobs & on-time and Technician utilization. Accounting sees Job profitability and Invoice aging, in the BR-01 groups and counts. Viewer and Technician get `403`, see the forbidden state with no data request, and have no Reports sidebar item |
| AC-02 | A Dispatcher, an Accounting member and an Owner | The Dispatcher calls `job_profitability` data/export, Accounting calls `quote_conversion` data/export, and the Owner calls an unknown key | `403`, `403` and `404` respectively, with no data and no audit row. A non-permitted `report` in the URL is replaced by the first visible report |
| AC-03 | Organizations A and B; A has branches A1, A2 and an inactive A3 with data; a Dispatcher and an Accounting member limited to A1 | Each report and export is requested with All, A1, A3, a B branch and a B technician | All includes A3 rows labelled "(Inactive)" for all-branches members, and A3 is selectable. Branch-limited members get only A1 data with All. A B branch or technician, or A2 for the limited member, returns an identical `404`. No B data appears in rows, totals, chart, links or exports |
| AC-04 | Rows whose job, customer, invoice, quote, request and technician the caller can and cannot open (other branch, role without that module, deleted entity) | The report loads | `links` hold ids only for openable entities. Those render as links to the BR-13 destinations, and the others render as plain text |
| AC-05 | Each preset, a custom range, a range over 366 days, a future `to`, and dates near midnight in an organization in `America/Bogota` | The report is requested and the URL is reloaded | `from`/`to`, the previous period, the granularity and the row dates follow BR-04 in the organization time zone. Invalid ranges get `400`, and the frontend resets them. Report, period, branch, compare, filters, sort, page and page size are restored on reload and back, and invalid URL values fall back with `replaceUrl` |
| AC-06 | Completed and approved-for-billing jobs completed inside and outside the period: one with two recognized invoices, one with a draft-only invoice, one with void and recognized invoices, lines with quote, material and no cost source, and one negative-margin job | Job profitability loads with each status, category and technician filter | Rows, revenue, estimated cost, profit and margin follow BR-08. The draft-only job shows "Not invoiced" with "—" and adds 0. A margin under 40 % shows the icon and "Below 40% target" |
| AC-07 | The AC-06 data | Summary, chart and totals render with no filter | Completed jobs equals Business Overview "Jobs completed" for the same period and branch. Not invoiced, Revenue, Estimated cost, Estimated gross profit and Estimated gross margin equal the sums of all filtered rows (not the page) and the totals row. The chart shows up to 10 categories plus "Other categories" |
| AC-08 | Sent, partially paid, paid, draft and void invoices across buckets and two branches | Invoice aging loads with no filter, then with each bucket and status filter, and with different `from`/`to` | Rows are only open invoices with BR-03 status and days overdue. Outstanding, Overdue and the buckets equal the Invoices hub BR-04/BR-05 for the same branch. Changing the period changes nothing, the response has `asOf` = today, and the UI disables the range and comparison with "Aging is calculated as of today." |
| AC-09 | Quotes first sent in and before the period, some approved in the period, one sent and approved in the period, one in another currency, one without a branch | Quote conversion loads with each event, status and category filter | Rows are the union with both flags. Sent, accepted and conversion equal Business Overview BR-06 for the same period and branch, including a value over 100 %. Sent and accepted values exclude the other-currency quote and show the note. The no-branch quote appears only with All |
| AC-10 | Visits scheduled in the period: on time, late, not started past their limit, not due yet, cancelled, unscheduled, completed with and without durations; work orders completed in the period | Completed jobs & on-time loads with each filter | Rows exclude cancelled and unscheduled visits. On-time results, completion rate, on-time arrival and duration variance follow BR-11, and on-time equals Business Overview BR-15. Jobs completed equals BR-08 and ignores the status, on-time and technician filters |
| AC-11 | Technicians with availability, breaks, time off, assessments and over-capacity days, one inactive, in two branches | Technician utilization loads for All, one branch and one technician | Rows include only active technicians in the selection. Available, scheduled, utilization, overtime, completed visits and on-time follow BR-12. Summary Technicians, Utilization and Overtime equal Business Overview BR-17. "—" is shown when nothing is available |
| AC-12 | A previous period with and without data | A report renders with comparison on, then off | With comparison on, count, money and hour metrics show BR-06 deltas with icon and text, hidden when previous = 0, and percent metrics show "vs <p>% previous". Turning comparison off hides them without a request and sets `compare=false` in the URL. Invoice aging shows no comparison |
| AC-13 | 60 rows in a report | Each allowlisted sort is requested in both directions, with page sizes 10/25/50 and a page past the last | Order follows the rule with stable ties, and not-invoiced and "—" values are placed as BR-08 and BR-12 state. Pages slice correctly with the real `total`. Past the last page returns empty `items`, and the UI moves to the last page. `totals` are unchanged across pages |
| AC-14 | Each report (parameterized) | It receives an unknown `sort`, `direction`, `status`, `bucket`, `event`, `onTime`, a foreign `categoryId`, `page` 0, `pageSize` 30 or a malformed date | `400` with the matching `errors.<param>` key and no data |
| AC-15 | Each report, with rows containing commas, quotes, newlines and formula-leading text, and a selection over 5,000 rows | `format=csv` is exported with comparison on and off | The file has the BR-14 name, headers, the applied sort and filters, values equal to the data endpoint, and a summary block with Previous empty when comparison is off. Cells are escaped, with no contact data or notes. Over 5,000 rows → `409 export_too_large` with no file and no audit row |
| AC-16 | Each report, and a selection over 1,000 rows | `format=pdf` is exported | An `application/pdf` attachment with the BR-15 name contains the header data, filters, summary, chart values, definitions, rows and totals, plus the profitability disclaimer for Job profitability. Over 1,000 rows → `409` with the 1,000 message and no audit row. `format=xls` → `400 errors.format` |
| AC-17 | Successful CSV and PDF exports of a financial and a non-financial report, then a `400`, `403`, `409` export and a failing audit insert | The audit log is inspected and Accounting opens the Audit trail | Each success wrote exactly one `export.generated` with `reportType` `report_<key>`, `format`, normalized `filters`, `rowCount` and the branch. The failing exports wrote none. The insert failure returned `500` with no file. Accounting sees only the Job profitability and Invoice aging export events |
| AC-18 | User U with 25 report exports in 90 days and one older, user V with exports in U's organization, an audit-log CSV export by U, and U's Dispatcher role losing a report | U opens **Export history** | Only U's 20 newest `report_*` exports of the last 90 days appear, without V's, the older one, the audit-log export or the no-longer-visible report. **Open report** navigates to that report with its period as custom, branch, filters, sort and compare, page 1 |
| AC-19 | Edited but unapplied period, branch and report filters | The member waits, then selects **Apply filters**, **Reset**, a sort header, a page, a page size, the compare toggle and refresh | Nothing reloads until Apply. Apply writes the URL, resets to page 1 and requests once. Reset restores defaults. Sort, paging and refresh request immediately, and compare does not request. "Updated …" resets after a load |
| AC-20 | A member in organizations O1 and O2, then storage that throws | They star reports, open several reports, switch organization, and lose access to a favorite | Favorites and the last 5 Recently viewed persist per user and organization, ignore unknown or invisible keys, and send nothing to the server. With storage failing, both lists are empty and the page works |
| AC-21 | A report | It loads, returns zero rows, fails, fails on refresh, and the member has no branch in scope | Skeletons appear, then the content. The empty texts appear with **Reset**. "We couldn't load this report." appears with **Try again**. A failed refresh keeps the data and shows "Couldn't refresh.". With no branch, "No branches available." appears and no data request is sent |
| AC-22 | Applied state and unapplied edits | **Export CSV** and **Download PDF** succeed, return `409`, and fail | The request uses the applied state and the current compare value. The button is busy and disabled while running. The file downloads with the server name. The BR-14 / BR-15 `409` message or "We couldn't export the report. Try again." is shown |
| AC-23 | Members of each role | The sidebar renders and `/coming-soon/reports` is opened | **Reports** links to `/reports` only for Owner, Operations Manager, Dispatcher and Accounting. `/coming-soon/reports` redirects to `/reports` with `replaceUrl` |
| AC-24 | Each report | Report definitions render | They show the BR-20 text for that report, including the Job profitability disclaimer and the quote-conversion note on values over 100 %. Every profitability label contains "Estimated" or "Est.", and no labor cost, target other than 40 %, or accounting figure appears |
| AC-25 | The implementation against `25-design.png` at 1440 px, plus 1024, 768, 375 and 320 px, a keyboard pass and the drawer open (USER VISUAL QA REPORT) | They are compared | Layout, catalog panel, controls, cards, chart, definitions, table, totals and pagination match the mockup within the current shell, with only the documented deviations. There is no page horizontal scroll. `aria-sort`, the star, the toggle, the drawer focus trap and return, the chart text alternative, and text-plus-icon states work |

## Testing requirements

25 active ACs. Backend integration exceeds the default range, by up to 10
methods. Each of the five reports has distinct reconciliation rules against a
different source module. The feature also adds a per-report authorization
matrix with branch isolation, and audited exports with volume limits and CSV
injection protection. Grouping keeps one method per report and one per risk.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | (1) On-time result classification and duration variance (table-driven: limits, `ref`, missing timestamps). (2) CSV cell escaping and number formatting for report rows, unless the existing `completed-jobs-review` escaping helper is reused unchanged. ≤ 2 methods | AC-10, AC-15 |
| Backend integration | (1) Role × report matrix on options, data, export and history, plus unknown key (parameterized). (2) Cross-tenant and branch-limited isolation, with an inactive branch and out-of-scope technician. (3)–(7) One seeded reconciliation method per report against the cited source rules, including its filters. (8) Sort allowlists, paging, totals and validation (parameterized). (9) CSV and PDF content, limits, escaping and the `export.generated` audit, including insert failure. (10) Export history scoping. ≤ 10 methods | AC-01 … AC-18 |
| Authorization/tenant isolation | Included above: `403` per role and report, one cross-tenant `404`, one out-of-scope branch, history own-only | AC-01, AC-02, AC-03, AC-18 |
| Frontend component/service | (1) URL state, Apply/Reset, compare without a request, fallbacks for invalid or not-permitted values. (2) Catalog visibility, search, Favorites and Recently viewed storage (including failure). (3) Table sort, paging and states (loading, empty, error, refresh, no branch, forbidden). (4) Export busy, `409` and error, and the history drawer with **Open report**. (5) Report rendering of deltas, below-target and not-invoiced marks, aging disabled controls and definitions (table-driven). (6) Sidebar visibility by role and the coming-soon redirect. ≤ 6 methods | AC-01, AC-02, AC-04, AC-05, AC-12, AC-19 … AC-24 |
| Manual (USER VISUAL QA REPORT) | Design 25 fidelity with deviations, responsive widths, keyboard and drawer focus, chart text alternative | AC-25 |

## Dependencies

Statuses verified 2026-10-16 from each spec's metadata.

| Spec | Status | Used for |
| ---- | ------ | -------- |
| `business-overview` | AUDITED | BR-03 period, BR-05 revenue/cost, BR-06 conversion, BR-08 jobs completed, BR-09 target, BR-10 formats, BR-11 deltas, BR-15 on-time, BR-17 team |
| `operations-dashboard` | AUDITED | BR-05 technician filter, BR-06 status groups, BR-08 utilization, BR-09 on-time, BR-13/BR-14 durations and completion |
| `jobs-management` | AUDITED | Completed tab, `/jobs/:id` |
| `quotes-management` | AUDITED | BR-02 scope, BR-03 display status, BR-11 amounts and currency, `/quotes/:id` |
| `invoices-payments-management` | AUDITED | BR-03 display status, BR-04 metrics, BR-05 aging, `/invoices/:id` |
| `completed-jobs-review` | AUDITED | BR-02 branch scope, BR-03 completed by, BR-21 escaping and limits |
| `audit-trail` | AUDITED | BR-02 financial flag, BR-16 `export.generated` (extended here), BR-25 link precedent |
| `design-18-users-and-permissions` | APPROVED | BR-07 Reports matrix row |
| `authenticated-app-shell` | AUDITED | Sidebar item and Coming soon route |
| `team-technician-management`, `technician-skills-availability` | AUDITED | Availability rules and the technician page |

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Invoice money is in `organizations.currency`, as `business-overview` assumes. |
| AS-02 | Per-technician daily load over up to 366 days is computed in memory within acceptable time (`business-overview` AS-03). |
| AS-03 | The existing PDF library and embedded fonts (PDFsharp-MigraDoc, Inter) render report PDFs; no dependency is added. |
| AS-04 | The existing read policies of Jobs, Customers, Invoices, Quotes, Requests and Team are the source for BR-13 link eligibility. |
| AS-05 | The organization and user ids needed for the BR-18 storage key are available from the current session on the frontend. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Catalog per role | Matrix as BR-02; profitability Owner-only | No | BR-02; Viewer and Technician `403` (user, 2026-10-16) |
| OD-02 | Job profitability basis | Jobs completed in period with Not invoiced; invoices issued in period | No | Jobs completed in period, Not invoiced shown (user, 2026-10-16) |
| OD-03 | Invoice aging and dates | As of today ignoring the period; issue-date filter | No | As of today, range and comparison disabled (user, 2026-10-16) |
| OD-04 | Quote conversion | Event-based (BO BR-06); cohort | No | Event-based, can exceed 100 %, explained in definitions (user, 2026-10-16) |
| OD-05 | On-time report grain | Visits scheduled in period with separate Jobs completed; work orders | No | Visits, Jobs completed separate (user, 2026-10-16) |
| OD-06 | Technician utilization columns | Capacity plus delivery; capacity only | No | Capacity plus completed visits and on-time (user, 2026-10-16) |
| OD-07 | Inactive branches | Included in All and selectable with "(Inactive)"; excluded | No | Included and selectable (user, 2026-10-16) |
| OD-08 | Export limits and audit | CSV 5,000 / PDF 1,000; same limit | No | CSV 5,000, PDF 1,000, `409`; audit only after success, `format` in metadata, profitability and aging financial (user, 2026-10-16) |
| OD-09 | Export history scope | Own exports; organization-wide for Owner/Ops | No | Own, 90 days, 20, Open report (user, 2026-10-16) |
| OD-10 | Comparison scope | Summary only; also chart series | No | Summary only (user, 2026-10-16) |
| OD-11 | Minor UX defaults | Route, redirect, sidebar roles, time zone, page sizes, no checkboxes, definitions without labor, 40 % target, local storage | No | Accepted (user, 2026-10-16) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-02, AC-23 |
| FR-02 | AC-03 |
| FR-03 | AC-05 |
| FR-04 | AC-06, AC-07 |
| FR-05 | AC-08 |
| FR-06 | AC-09 |
| FR-07 | AC-10 |
| FR-08 | AC-11 |
| FR-09 | AC-12 |
| FR-10 | AC-13, AC-14 |
| FR-11 | AC-04 |
| FR-12 | AC-15, AC-16, AC-17, AC-22 |
| FR-13 | AC-18 |
| FR-14 | AC-20 |
| FR-15 | AC-19, AC-21, AC-24, AC-25 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-16 | — → DRAFT | Created |
| 2026-10-16 | DRAFT → DRAFT | Revised after validation: disclaimer verbatim from `business-overview` BR-19 (BR-20); minute-metric comparison as previous-value text (BR-06); invoice aging keeps common `from`/`to`/`compare` validation with no effect on figures (BR-09, API); explicit URL and API defaults (BR-05, BR-07); exact two-block CSV layout with `Section` column (BR-14); join keys in Data impact |
| 2026-10-16 | DRAFT → DRAFT | Revised after validation: Data impact join keys corrected to the schema (`quotes.request_id`, `quote_responses.quote_version_id` via `quote_versions.quote_id`) and `quotes.customer_id` added; no behavior change |
| 2026-10-16 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-16 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-16 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
