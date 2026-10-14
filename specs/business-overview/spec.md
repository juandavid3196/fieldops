# Business overview

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `business-overview` |
| Type     | Full-stack          |
| Status   | AUDITED             |
| Created  | 2026-10-14          |
| Updated  | 2026-10-14          |
| Approved | 2026-10-14          |

## Context and objective

Owners have no single view of how the business performs. Revenue, collections,
receivables, sales conversion, branch results, service profitability and team
capacity are spread across Billing, Quotes, Jobs and Team. This feature adds a
read-only Business Overview (Design 16) as the Owner's view of `/dashboard`.
It is built only from existing invoices, payments, quotes, requests, work
orders, visits, reviews and technician availability, for a date range and
branch, with comparison to the previous period and a CSV export.
Profitability figures are labelled "Estimated" and are computed on the server
from configured item costs. They are not formal accounting. Operations
Managers and Dispatchers keep the Operations Dashboard (Design 15). The Owner
switches between both views on the same page, and the sidebar keeps a single
Dashboard item.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Read the Business Overview and export it for all branches of the organization or one active branch; switch to the Operations Dashboard on `/dashboard` | Change data from the overview |
| Operations Manager, Dispatcher | Keep the Operations Dashboard on `/dashboard`, unchanged | See the Business view or call `/business-overview*` (`403`) |
| Accounting, Viewer, Technician, unknown roles | Keep the existing `/overview` welcome page | Open `/dashboard` (existing forbidden state) or call `/business-overview*` (`403`) |

## Scope

- Business view of `/dashboard` per Design 16, with the following parts:
  - Header with the date range, branch selector, comparison toggle, export and
    refresh.
  - KPI row.
  - Revenue & estimated profit chart.
  - Cash flow snapshot with receivables aging.
  - Sales pipeline.
  - Branch performance.
  - Service profitability estimates.
  - Team & capacity.
  - Owner attention.
  - Estimates disclaimer.
- A **Business | Operations** view switch for the Owner on `/dashboard`. The
  Operations view is the existing Operations Dashboard, unchanged.
- Three read endpoints: options, overview data and the CSV export.

## Non-goals

- Formal accounting: net income, overhead, payroll, payroll taxes,
  depreciation, inventory valuation, tax accounting or accrual adjustments.
- Labor costing from technician or organization hourly rates. Labor is
  represented only by the configured cost of service items.
- A configurable margin target. The target is the fixed BR-09 constant.
- A Reports module or a "Job profitability report". The Design 16 button is
  removed.
- Open roles or hiring data.
- PDF export, scheduled or emailed reports, automatic refresh.
- Changes to the Operations Dashboard, to the sidebar items, to the shell
  search or bell, or to `/overview` for other roles.
- New schema, migrations, audit rows or notifications.
- Automated browser, E2E or visual tests by agents.

## User flow

1. The Owner signs in or opens **Dashboard** in the sidebar. `/overview` still
   redirects to `/dashboard`.
2. The page shows the Business view with **This month** and **All branches**,
   with comparison on.
3. The Owner changes the range, branch or comparison. The URL updates and the
   cards reload.
4. The Owner opens receivables, an alert target or the Operations view, or
   selects **Export report** to download a CSV of the current selection.
5. An Operations Manager or Dispatcher opening `/dashboard` sees the
   Operations Dashboard exactly as before, without the view switch.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | `/dashboard` must show the Business view to the Owner by default, with a Business / Operations switch kept in the URL. Operations Managers and Dispatchers keep the Operations Dashboard without the switch. The sidebar keeps one Dashboard item (BR-01). |
| FR-02 | Every `/business-overview*` endpoint must resolve the organization from the session and accept only the Owner. Every figure must be limited to the organization and to the selected branch (BR-02). |
| FR-03 | The page must keep the range preset, custom dates, branch and comparison in the URL. It must resolve the period, the previous period and the chart granularity in the organization time zone (BR-03, BR-04). |
| FR-04 | The KPI row must show Revenue, Estimated gross profit, Estimated gross margin, Quote conversion, Outstanding invoices and Jobs completed, with previous-period deltas where BR-11 defines them (BR-05 … BR-08, BR-11). |
| FR-05 | The Revenue & estimated profit chart must plot revenue, estimated profit and cash collected per bucket for the current period and, with comparison on, the previous period (BR-12). |
| FR-06 | The Cash flow snapshot must show invoiced, collected, outstanding and overdue amounts and the receivables aging (BR-13). |
| FR-07 | The Sales pipeline must show requests, quotes sent, accepted quotes and jobs created in the period (BR-14). |
| FR-08 | Branch performance must show revenue, estimated margin, jobs completed, on-time percentage and rating per branch, with totals (BR-15). |
| FR-09 | Service profitability estimates must show revenue, estimated margin and average job value per service category (BR-16). |
| FR-10 | Team & capacity must show active technicians, utilization, scheduled overtime and the next-7-days scheduled utilization (BR-17). |
| FR-11 | Owner attention must list overdue invoices, below-target branches and services, and rising overtime, each with its action. The disclaimer must state the limits of the estimates (BR-09, BR-18, BR-19). |
| FR-12 | **Export report** must download a CSV of the current selection, computed by the server (BR-20). |
| FR-13 | The page must reproduce Design 16 with the documented deviations, and must handle the loading, empty, error, permission, refresh and responsive states with the BR-10 number formats (BR-10, BR-21, BR-22). |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | View routing. `/dashboard` reads `view`. For `owner`, `view` absent or `business` shows the Business view, and `view=operations` shows the existing Operations Dashboard with its own URL parameters and behavior (`operations-dashboard` BR-01 … BR-18). The Owner sees a two-option switch **Business** / **Operations** in the page header of both views. Switching the view navigates to `/dashboard` with only the new `view` value, which drops the other view's parameters. `operations_manager` and `dispatcher` always see the Operations Dashboard without the switch; any `view` value is removed with `replaceUrl`. Other roles see the existing forbidden state with no data request. The sidebar item stays a single **Dashboard** (`/dashboard`). `/overview` keeps redirecting the Manage roles. An invalid `view` for the Owner is replaced by the Business view (`replaceUrl`). Frontend visibility is UX only; the backend decides (`403`). | Both |
| BR-02 | Authorization and scope. Every `/business-overview*` endpoint requires an active `owner` membership of the session organization; every other role gets `403` before any read. No request carries an organization id; any such parameter is ignored. With **All branches**, figures include every branch of the organization, active or inactive. Requests and quotes with a null `branch_id` count only with All. With a branch, only rows of that branch count. The branch is read from `invoices.branch_id`, `work_orders.branch_id` (visits, reviews, jobs), `quotes.branch_id`, `service_requests.branch_id` and `technician_profiles.branch_id`. A payment counts through the allocations to invoices of that branch. A `branchId` that is unknown, inactive or of another organization → `404`, identical in every case. No response or export contains data of another organization. | Backend |
| BR-03 | Period. All dates are local dates in `organizations.timezone`, and a day is `[00:00, 24:00)` local. URL `range` takes one of the presets below; it defaults to `this_month`. `today` is the local date at request time. Preset resolution: <ul><li>`this_month`: first day of the month to today.</li><li>`last_month`: the whole previous month.</li><li>`last_30_days`: today − 29 to today.</li><li>`this_quarter`: first day of the calendar quarter to today.</li><li>`this_year`: 1 January to today.</li><li>`custom`: URL `from` and `to` (`YYYY-MM-DD`), chosen in a date range picker.</li></ul> The API receives the resolved `from` and `to`, both inclusive. API validation: both are valid dates, `from` ≤ `to`, `to` ≤ today, and the period is at most 366 days long. Any violation → `400` (`errors.from` / `errors.to`). On the frontend, an invalid or incomplete custom range or an unknown preset resets to `this_month` (`replaceUrl`). The **previous period** has the same number of days L and ends the day before `from`. Granularity: `day` for L ≤ 31, `week` (Monday-start, buckets clipped to the period) for L ≤ 92, otherwise `month` (clipped). Previous-period bucket *i* is current bucket *i* shifted back L days. Header subtitle: "<MMM d – d, yyyy>" (or "<MMM d, yyyy – MMM d, yyyy>" across months or years) followed by " · <branch name>" or " · All branches". | Both |
| BR-04 | Header controls. **Date range** shows the resolved range and offers This month, Last month, Last 30 days, This quarter, This year and Custom. **Branch** selector: **All branches** (only with ≥ 2 active branches) and each active branch by name, default All, or the only branch when there is one. URL `branch` holds the branch id and is absent for All. A `404` branch resets to the default (`replaceUrl`). With no active branch: "No branches available." and no overview request. **Compare to previous period** is a toggle, on by default; URL `compare=false` only when off. Any other `compare` value means on and is removed (`replaceUrl`). It changes the display and the export only; it never refetches. **Export report** follows BR-20. A refresh button reloads on demand; there is no automatic refresh. Beside the title the page shows "Updated just now" (< 1 minute) or "Updated <n> min ago", updated each minute. | Both |
| BR-05 | Revenue and estimated cost. **Recognized invoices**: invoices whose stored status is not `draft` or `void` and whose `issue_date` is in the period. **Revenue** = Σ (`subtotal` − `discount_total`) of recognized invoices, tax excluded. **Estimated cost** of an invoice = Σ over its lines of `quantity` × `unit_cost` of the line's `source_quote_line_id` quote line, else of its `source_visit_material_id` visit material. A line with neither contributes 0. **Estimated gross profit** = revenue − estimated cost. It can be negative. **Estimated gross margin** = profit ÷ revenue × 100, one decimal; "—" when revenue = 0. All money is in `organizations.currency`. | Backend |
| BR-06 | Quote conversion. **Sent** = quotes whose earliest non-null `quote_versions.sent_at` falls in the period. **Accepted** = distinct quotes with a `quote_responses` row `response = 'approved'` whose `responded_at` falls in the period. KPI "<p>%" = accepted ÷ sent × 100, integer, "—" when sent = 0. Caption "<accepted> accepted of <sent> sent". Both counts are taken by event date in the period, so the value can exceed 100 % when acceptances of earlier quotes outnumber new sends; it is shown as computed. | Backend |
| BR-07 | Outstanding invoices KPI. Computed as of today, independent of the period: `invoices-payments-management` BR-04 **Outstanding** with the overdue amount from the same rule (BR-03 display status), for the selected branch or All. Caption "<overdue amount> overdue" with a warning icon when the overdue amount > 0; "Nothing overdue" otherwise. | Backend |
| BR-08 | Jobs completed. Work orders with status `completed` or `approved_for_billing` whose latest `actual_completed_at` among their non-cancelled visits falls in the period. | Backend |
| BR-09 | Margin target. A fixed server constant of 40.0 %, returned as `marginTarget`. A margin is **below target** when revenue > 0 and margin < target. Below-target margins in tables show a warning icon and the visible text "Below 40% target" (tooltip and accessible name). The value is never conveyed by color alone. | Both |
| BR-10 | Rounding and format. Card and table money is rounded to whole currency units with the currency symbol. Chart axes use compact units ($5K). Export money has 2 decimals with no symbol. Margins have one decimal. Conversion, utilization and on-time are integers. Hours are whole hours (`round(minutes / 60)`). | Both |
| BR-11 | Comparison deltas. Shown only with comparison on. They apply to Revenue, Estimated gross profit and Jobs completed: delta = (current − previous) ÷ \|previous\| × 100, one decimal, shown as "▲ +12.4%" or "▼ −3.1%" with an up or down icon plus text, or "No change" at 0.0. The delta is hidden when previous = 0. Margin, conversion and outstanding show no delta. Previous values follow the same rules over the previous period. | Both |
| BR-12 | Revenue & estimated profit chart. Subtitle with an info icon: "Estimated profit = revenue − configured product and service costs." Tabs: <ul><li>**Revenue** (default): revenue and estimated profit lines.</li><li>**Estimated profit**: profit line only.</li><li>**Cash collected**: collected line only.</li></ul> Each series is computed per BR-03 bucket. Revenue and profit use the BR-05 rules per bucket by `issue_date`. Collected uses BR-13 per bucket by `paid_at` local date. With comparison on, each current line has a dashed previous-period line, and the legend names "(current)" and "(previous)". X labels show the bucket start ("Sep 4"; months as "Sep 2026"). A visually hidden table lists every plotted value. Empty, when every current bucket is 0 for the tab: "No data for this period." | Both |
| BR-13 | Cash flow snapshot. **Invoiced** = Σ `total` of recognized invoices (BR-05), tax included. **Collected** = Σ over payments whose `paid_at` local date is in the period of each allocation `amount` × (payment `amount` − `refunded_amount`) ÷ payment `amount`, rounded to cents per allocation; with a branch, only allocations to that branch's invoices count. The schema stores no refund date, so a refund always reduces the period of the original `paid_at`, including a period already shown. **Outstanding** and **Overdue** follow BR-07 (as of today). **Accounts receivable aging** (as of today) follows `invoices-payments-management` BR-05: Current, 1–30 days, 31–60 days, 60+ days, each a labelled bar with its amount. **View receivables** → `/invoices`. | Backend |
| BR-14 | Sales pipeline, in the period: **Requests** = `service_requests` with `created_at` in the period (any status); **Quotes sent** = BR-06 sent; **Accepted** = BR-06 accepted; **Jobs created** = `work_orders` with `created_at` in the period and status not `draft` or `cancelled`. The four counts appear in a vertical sequence with icons and text labels; they are not a cohort funnel. | Backend |
| BR-15 | Branch performance. One row per active branch in the selection, plus each inactive branch with a non-zero figure in the period, labelled "<name> (inactive)". Rows are ordered by revenue descending, then name. Columns: <ul><li>**Revenue** and **Estimated margin** (BR-05 with BR-09 marking).</li><li>**Jobs** (BR-08).</li><li>**On-time %**: `operations-dashboard` BR-09 applied over visits with `scheduled_start` in the period, with `ref` = min(now, end of `to`); "—" when none are eligible.</li><li>**Rating**: average `invoice_reviews.rating` created in the period, one decimal; "—" when none.</li></ul> A **Total** row appears only with ≥ 2 rows: Σ revenue, total margin, Σ jobs, overall on-time and "—" for rating. | Backend |
| BR-16 | Service profitability estimates. One row per service category (`work_orders.service_category_id` of the invoice's work order) with revenue > 0 in the period, ordered by revenue descending, then name; up to 5 rows are shown and the export holds all. Columns: Service (category name), Revenue, Estimated margin (BR-05 with BR-09 marking), **Avg job value** = revenue ÷ distinct work orders of recognized invoices in that category. Empty: "No invoiced services in this period." | Backend |
| BR-17 | Team & capacity. **Technicians** = technicians with `status = 'active'` in the selection, counted now. **Utilization** = Σ scheduled ÷ Σ available minutes over those technicians and every day of the period, per `operations-dashboard` BR-08, integer percent; "—" when available = 0. **Scheduled overtime** = Σ over technician-days of max(0, scheduled − available) minutes, shown in hours. With comparison on and previous overtime > 0, it adds "▲ <p>%" / "▼ <p>%", the relative change vs the previous period, with icon and text. **Next 7 days (scheduled)** = utilization for today … today + 6, independent of the period; "—" when available = 0. There is no Open roles row. | Backend |
| BR-18 | Owner attention. Items in this order: <ol><li>Overdue invoices (BR-07) when the count > 0: "<n> overdue invoice(s)", "Totaling <amount>" → `/invoices?status=overdue`.</li><li>Each below-target active branch (BR-15 rows, by margin ascending): "<branch> estimated margin below target", "<m>% vs 40.0% target" → `/dashboard?branch=<id>` keeping range and compare. These items appear only with All branches. Inactive branch rows keep their BR-09 marking in the table but never produce an alert.</li><li>Each below-target service (all BR-16 categories, by margin ascending): "<category> estimated margin below target", "<m>% vs 40.0% target" → `/admin/products-services`.</li><li>Scheduled overtime when previous overtime > 0 and the change ≥ +10.0 %: "Scheduled overtime up <p>%", "<x>h vs <y>h in previous period" → `/team`.</li></ol> Overdue invoices have severity "critical"; the other items have severity "warning". Each item shows an icon and text, never color alone. The card shows 3 items; **View all (<n>)** expands and **Show less** collapses. Empty: "Nothing needs your attention." | Both |
| BR-19 | Disclaimer, always shown beside Owner attention: "Profitability figures are operational estimates based on configured product and service costs. They exclude overhead, payroll, payroll taxes, depreciation, and other accounting expenses. They are not net income and not a substitute for accounting software." The Design 16 **Open Job profitability report** button is not shown. Every profitability label carries the word "Estimated". | Frontend |
| BR-20 | Export. `GET /business-overview/export` with the same `from`, `to`, `branchId` plus `compare` returns `text/csv; charset=utf-8`, file `business-overview-<from>-to-<to>.csv`. Columns: Section, Metric, Dimension, Current, Previous, Unit. Unit is the currency code, `percent`, `count` or `hours`. Rows cover KPIs, chart buckets (Dimension = bucket start), cash flow, aging, pipeline, every branch row and total, every service category, and team & capacity. Previous is empty when `compare=false` or not applicable. Escaping follows `completed-jobs-review` BR-21; values are those of the overview endpoint for the same parameters. Never exported: customer names, emails, addresses, notes or individual invoices or payments. The button shows a busy state; a failure shows the toast "We couldn't export the report." | Both |
| BR-21 | Loading and errors. Header controls are disabled until options load. Each card shows its own skeleton in the Design 16 layout. An options or overview failure shows the page alert "We couldn't load the business overview." with **Try again**, and the controls stay usable. A refresh failure keeps the shown data and shows "Couldn't refresh." with **Try again**. Changing range, branch or view cancels in-flight requests. Responses are `Cache-Control: no-store`. With no recognized invoices: KPIs show $0, margins "—", and each card shows its empty text. | Frontend |
| BR-22 | Layout and accessibility. <ul><li>From 1280 px: KPI row of 6; chart, cash flow and pipeline in one row; branch, service and team cards in one row; Owner attention with the disclaimer in one row.</li><li>768–1279 px: KPIs 3 per row and cards in two columns.</li><li>Below 768 px: one column, KPIs 2 per row, tables scroll horizontally inside their card, no page horizontal scroll, touch targets ≥ 44 px.</li></ul> Deltas, warnings and severities use text and icon. Info icons expose their text to keyboard and screen readers. The view switch is a labelled, keyboard-operable two-option control. | Frontend |

## States and transitions

None. The feature is read-only.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`), read
  only:
  - `organizations` (`timezone`, `currency`), `branches` (`name`, `is_active`), `organization_users`.
  - `invoices` (`branch_id`, `work_order_id`, `status`, `issue_date`,
    `due_date`, `subtotal`, `discount_total`, `total`, `balance_due`),
    `invoice_lines` (`quantity`, `source_quote_line_id`,
    `source_visit_material_id`), `quote_lines` (`unit_cost`),
    `visit_materials` (`unit_cost`).
  - `payments` (`amount`, `refunded_amount`, `paid_at`),
    `payment_allocations` (`amount`, `invoice_id`).
  - `service_requests` (`branch_id`, `created_at`), `quotes` (`branch_id`),
    `quote_versions` (`sent_at`), `quote_responses` (`response`,
    `responded_at`).
  - `work_orders` (`branch_id`, `status`, `service_category_id`,
    `created_at`), `service_categories` (`name`), `visits` (`status`,
    `scheduled_start`, `arrival_window_end`, `actual_started_at`,
    `actual_completed_at`), `invoice_reviews` (`rating`, `created_at`).
  - `technician_profiles`, `technician_weekly_availability`,
    `technician_breaks`, `technician_exceptions`, `visit_assignments`,
    `assessments` (load per `operations-dashboard` BR-08).
- No writes and no audit rows.
- Schema amendments required: None.
- Queries are bounded by the period (≤ 366 days), its previous period, the
  organization and the selected branch.

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. No
  query carries an organization id; any such parameter is ignored.
- Client-provided identifiers: only `branchId` (query). It is authorized
  against the session organization; an unknown, inactive or foreign branch
  returns `404` with no data.
- Policy: a new Owner-only read policy on every `/business-overview*`
  endpoint; other roles `403`. The Operations Dashboard keeps
  `DispatchPolicies.Manage`, unchanged.
- Responses and the export contain only aggregates plus branch and category
  names and ids. They never contain customer names, contact data, notes,
  review comments or individual invoices and payments.

## API contracts

Contract status: Final. Errors use ProblemDetails, and `400` carries
`errors`. Responses are `no-store`. Dates are `YYYY-MM-DD` in the
organization time zone. Money is decimal in `currency`; percentages are
numbers (`null` when the BR shows "—"); durations are minutes.

`Money2` = `{ current, previous }` (the previous period, BR-03).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/business-overview/options` | — | `200 { timezone, currency, today, marginTarget, branches: [{ id, name }] }` (active branches by name) | `403` | Owner |
| GET | `/business-overview` | Query: `from`, `to`, `branchId?` | `200 { from, to, previousFrom, previousTo, timezone, currency, marginTarget, granularity (day/week/month), kpis: { revenue: Money2, grossProfit: Money2, grossMarginPercent: { current \| null, previous \| null }, quoteConversion: { accepted, sent, percent \| null }, outstanding: { amount, overdueAmount, overdueCount }, jobsCompleted: { current, previous } }, series: [{ start, end, revenue, grossProfit, collected, previousRevenue, previousGrossProfit, previousCollected }], cashFlow: { invoiced, collected, outstanding, overdue, aging: { current, days1To30, days31To60, days60Plus } }, pipeline: { requests, quotesSent, accepted, jobsCreated }, branches: [{ branchId, name, isActive, revenue, grossProfit, marginPercent \| null, jobsCompleted, onTimePercent \| null, rating \| null, reviewCount }], branchTotals: { revenue, marginPercent \| null, jobsCompleted, onTimePercent \| null } \| null, services: [{ categoryId, name, revenue, grossProfit, marginPercent \| null, jobCount, averageJobValue }], team: { technicians, utilizationPercent \| null, scheduledMinutes, availableMinutes, overtimeMinutes, previousOvertimeMinutes, next7DaysPercent \| null }, alerts: [{ type (overdue_invoices/branch_margin/service_margin/overtime), severity (critical/warning), count \| null, amount \| null, branchId \| null, categoryId \| null, name \| null, marginPercent \| null, overtimeMinutes \| null, previousOvertimeMinutes \| null, changePercent \| null }] }`. `services` holds every category with revenue > 0; the UI shows 5. | `400` (`from`, `to`, malformed `branchId`) · `403` · `404` (branch) | Owner |
| GET | `/business-overview/export` | Query: `from`, `to`, `branchId?`, `compare` (`true`/`false`, default `true`) | `200 text/csv` per BR-20 with `Content-Disposition: attachment` | `400` (`from`, `to`, `compare`, malformed `branchId`) · `403` · `404` (branch) | Owner |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/dashboard` Business view (Owner) | Controls disabled until options load; per-card skeletons (BR-21) | No active branch: "No branches available." Per card: BR-12, BR-16, BR-18; zero KPIs per BR-21 | Page alert "We couldn't load the business overview." with **Try again**; refresh failure "Couldn't refresh."; export toast "We couldn't export the report."; `404` branch and invalid range reset (BR-03, BR-04) | Operations Manager or Dispatcher: Operations Dashboard, no switch. Other roles: existing forbidden state, no request. `401`: existing session-ended handling | Cards per BR-05 … BR-19 |
| `/dashboard?view=operations` (Owner) | Existing | Existing | Existing | Owner only for the switch | Existing Operations Dashboard plus the view switch |

- Mockups: `design/assets/16-design.png` (Approved). The data in it are
  examples. This spec fixes the following deviations:
  - The branch selector and "Updated …" with a refresh button sit in the page
    header, not the top bar, as in Design 15.
  - A **Business | Operations** switch is added to the header.
  - The chart formula and the disclaimer omit "labor" (BR-12, BR-19).
  - Team & capacity has no Open roles row.
  - Branch-performance warnings read "Below 40% target".
  - **Open Job profitability report** is removed.
  - The bell and shell search are unchanged.
- Responsive and accessibility: BR-22.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Role other than Owner on `/business-overview*` | `403`; Operations Dashboard or forbidden state per BR-01 | None |
| Branch unknown, inactive or foreign | `404`; selector reset to default | None |
| `from`/`to` malformed, reversed, future or > 366 days; `compare` invalid | `400`; frontend resets to This month | None |
| Load failure | "We couldn't load the business overview." with **Try again** | None |
| Refresh failure | Data kept; "Couldn't refresh." with **Try again** | None |
| Export failure | Toast "We couldn't export the report." | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An Owner signed in | They select **Dashboard** or open `/overview` | `/dashboard` shows the Business view with the Business/Operations switch. **Operations** shows the existing Operations Dashboard at `view=operations` with the switch, and **Business** returns. The sidebar has exactly one Dashboard item |
| AC-02 | An Operations Manager or Dispatcher | They open `/dashboard` or `/dashboard?view=business` | The Operations Dashboard renders unchanged, with no switch, and `view` is removed from the URL. Their calls to `/business-overview*` return `403` |
| AC-03 | An Accounting, Viewer or Technician user | They open `/dashboard` or call any `/business-overview*` endpoint | The page shows the forbidden state with no data request, and the API returns `403` |
| AC-04 | Organizations A and B with invoices, payments, quotes and visits; A has branches A1 and A2, A3 inactive with invoices | The A Owner loads All, then `branchId` = A1, A3 or a branch of B | All includes A1, A2 and A3 data, with A3 as "(inactive)" in branch performance. A1 includes only A1 rows. A3 and B's branch return `404` with no foreign data, and nothing of B appears in any figure or export |
| AC-05 | Each preset, a custom range, an invalid range and a period over 366 days | The Owner selects it or reloads the URL | `from`/`to`, the previous period, the granularity and the subtitle follow BR-03 in the organization time zone. The API rejects an invalid or over-long range with `400`, and the frontend resets it to This month. Range, branch and compare are restored on reload and back |
| AC-06 | Recognized, draft and void invoices with discounts and tax; lines sourced from quote lines, from visit materials and with no source | The overview loads | Revenue, estimated cost, estimated gross profit (including a negative case) and margin follow BR-05, with "—" margin at zero revenue |
| AC-07 | Quotes first sent in and before the period, some approved in the period | The overview loads | Quote conversion shows the BR-06 percent and "<a> accepted of <s> sent", or "—" with no sends; the pipeline shows the BR-14 four counts |
| AC-08 | Comparison on and off; a previous period with and without data | The KPI row renders | With comparison on, Revenue, Estimated gross profit and Jobs completed show BR-11 deltas with icon and text, hidden when previous = 0. Turning comparison off hides the deltas and dashed lines without a new request and updates the URL |
| AC-09 | Periods of 20, 60 and 200 days | The chart renders on each tab | Buckets are daily, weekly and monthly. Revenue/profit/collected values per bucket match BR-05 and BR-13, with previous lines when comparison is on. A hidden table lists the values, and an all-zero tab shows "No data for this period." |
| AC-10 | Sent, partially paid, paid, past-due and partially refunded payments across two branches | The cash flow snapshot renders for All and for one branch | Invoiced, refund-adjusted Collected, Outstanding, Overdue and the four aging buckets follow BR-13 and BR-07. Outstanding and aging do not change with the period. **View receivables** opens `/invoices` |
| AC-11 | Work orders completed in and outside the period, one with a cancelled later visit; reviews and visits with on-time and late starts | Branch performance renders | Per-branch revenue, margin, jobs, on-time and rating follow BR-15 and BR-08, sorted by revenue. Total appears only with ≥ 2 rows, and a margin under 40 % shows the warning icon and "Below 40% target" |
| AC-12 | Invoices across 6 service categories, one below target | Service profitability renders | The top 5 categories by revenue show revenue, margin with BR-09 marking and average job value per BR-16. With none: "No invoiced services in this period." |
| AC-13 | Technicians with availability, time off, assignments over capacity in the current and previous period, and an inactive technician | Team & capacity renders | Technicians, utilization, overtime hours with its change, and next-7-days utilization follow BR-17. "—" appears when nothing is available, there is no Open roles row, and the inactive technician is not counted |
| AC-14 | Overdue invoices, a below-target branch, a below-target service and overtime up 18 % | Owner attention renders with All and with one branch | Items appear in BR-18 order and copy with their targets and severity icons. Branch items appear only with All. 3 are shown and **View all (n)** expands. With none: "Nothing needs your attention." |
| AC-15 | The Business view | It renders | The BR-19 disclaimer is shown, there is no profitability-report button, and every profitability label contains "Estimated" |
| AC-16 | A selection with comparison on and off | The Owner selects **Export report** | A `business-overview-<from>-to-<to>.csv` downloads. Its values equal the overview for the same parameters, Previous is empty when comparison is off, formula-leading text is escaped, and no customer or invoice-level data is present. A failure shows "We couldn't export the report." |
| AC-17 | The overview is loaded | The Owner selects refresh, or the overview or options request fails, or a refresh fails | The data reloads and "Updated …" resets. A first-load failure shows "We couldn't load the business overview." with **Try again** and usable controls. A failed refresh keeps the data and shows "Couldn't refresh." |
| AC-18 | An organization with one active branch, and one with none | The Owner opens the overview | With one branch, it is selected and All is not offered. With none, "No branches available." is shown and no overview request is sent |
| AC-19 | The overview at 1280 px, 1024 px and 375 px | The page renders | The layout follows BR-22 and Design 16 with the documented deviations. There is no page horizontal scroll. Deltas, warnings and alerts use text and icon, and the switch and info icons are keyboard operable (user visual QA) |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Period resolution: presets, the previous period and granularity buckets at month, quarter and year edges (parameterized). Line cost source selection and profit/margin/delta rules including zero and negative values. Refund-adjusted collected allocation | FR-03, FR-04; AC-05, AC-06, AC-08, AC-10 |
| Backend integration | One grouped overview happy path on seeded data covering KPIs, series, cash flow, aging, pipeline, branches with inactive, services, team and alerts. Range validation. Export content equal to the overview, with compare off and escaping | AC-06, AC-07, AC-09 … AC-14, AC-16 |
| Authorization/tenant isolation | Representative: `403` for Operations Manager and Accounting on overview and export; foreign and inactive branch `404` with no foreign rows in All totals | AC-02, AC-03, AC-04 |
| Frontend component/service | View resolution by role (switch, `view` removal, forbidden). URL state for presets, custom range, branch and compare, including resets. Compare toggle without refetch. Card rendering of deltas, BR-09 warnings, empties and alerts (table-driven). Export download and error. Load, refresh and no-branch states | AC-01, AC-02, AC-05, AC-08, AC-14, AC-15, AC-16, AC-17, AC-18 |
| Browser (user visual QA) | Design 16 fidelity with deviations, responsive breakpoints, text-plus-icon states, chart text alternative | AC-19 |

## Dependencies

Statuses verified 2026-10-14 from each spec's metadata.

| Spec | Status | Used for |
| ---- | ------ | -------- |
| `operations-dashboard` | AUDITED | Operations view, BR-08 utilization, BR-09 on-time rule |
| `invoices-payments-management` | AUDITED | BR-03 overdue, BR-04 outstanding, BR-05 aging, `/invoices?status=` |
| `customer-invoice-payments` | AUDITED | Payment refunds, `invoice_reviews` |
| `completed-jobs-review` | AUDITED | BR-21 CSV escaping |
| `invoice-draft-delivery` | AUDITED | Invoice issue, totals and line sources |
| `quote-builder`, `customer-quote-approval` | AUDITED | `sent_at`, approved responses, quote line costs |
| `requests-pipeline` | AUDITED | Service requests |
| `create-work-order` | AUDITED | Work order creation and category |
| `team-technician-management` | AUDITED | Availability and load |
| `products-services` | APPROVED | Catalog cost configuration (`/admin/products-services`) |
| `authenticated-app-shell` | AUDITED | Sidebar Dashboard item and `/overview` redirect |

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Invoice `subtotal` excludes tax and is before `discount_total`, as the invoice totals of `invoice-draft-delivery` define them. |
| AS-02 | The Owner always has all-branches scope, so no per-user branch restriction applies to this view. |
| AS-03 | Per-day technician load over a period of up to 366 days is computed in memory for an organization's technicians within acceptable response time. |
| AS-04 | `/invoices?status=overdue` is an existing valid hub URL (`invoices-payments-management` BR-20). |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Access and Owner's Operations view | Owner only with a Business/Operations switch; Owner loses Operations; Owner + Accounting | No | Owner only, with a switch in the page header (user, 2026-10-14) |
| OD-02 | Revenue recognition | By invoice issue date, net of discount, tax excluded; by collection | No | Issue date, net of discount, tax excluded (user, 2026-10-14) |
| OD-03 | Estimated cost and labor | Configured item costs only; technician hourly rate; organization rate | No | Configured item costs only, no migration (user, 2026-10-14) |
| OD-04 | Margin target | Fixed 40 %; negative only; configurable | No | Fixed 40 % (user, 2026-10-14) |
| OD-05 | Balances and aging | As of today; as of period end | No | As of today; invoiced and collected by period (user, 2026-10-14) |
| OD-06 | Team & capacity rows | Keep overtime, drop open roles; drop both | No | Keep overtime, drop open roles (user, 2026-10-14) |
| OD-07 | Export | Server CSV; client CSV; PDF | No | Server CSV (user, 2026-10-14) |
| OD-08 | Profitability report button | Remove; link to coming soon | No | Remove (user, 2026-10-14) |
| OD-09 | Range, controls and refresh | BR-03/BR-04 presets, granularity, header selector, manual refresh | No | As proposed (user, 2026-10-14) |
| OD-10 | Pipeline and operational definitions | Event-date counts per BR-06, BR-08, BR-14, BR-15 | No | As proposed (user, 2026-10-14) |
| OD-11 | Owner attention | BR-18 items, top 3 with expansion | No | As proposed (user, 2026-10-14) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01, AC-02, AC-03 |
| FR-02 | AC-02, AC-03, AC-04 |
| FR-03 | AC-05, AC-18 |
| FR-04 | AC-06, AC-07, AC-08, AC-10 |
| FR-05 | AC-09 |
| FR-06 | AC-10 |
| FR-07 | AC-07 |
| FR-08 | AC-11 |
| FR-09 | AC-12 |
| FR-10 | AC-13 |
| FR-11 | AC-14, AC-15 |
| FR-12 | AC-16 |
| FR-13 | AC-17, AC-18, AC-19 |

## Change log

| Date | Status change | Reason |
| ---- | ------------- | ------ |
| 2026-10-14 | — → DRAFT | Created |
| 2026-10-14 | DRAFT → DRAFT | Revised after validation: branch margin alerts only for active branches (inactive `branchId` is `404`); invalid `compare` URL value handling; refund attribution to the original payment period; FR-13 cites BR-10 |
| 2026-10-14 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-10-14 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-10-14 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
