# Technician today's jobs

| Field    | Value                    |
| -------- | ------------------------ |
| Feature  | `technician-todays-jobs` |
| Type     | Full-stack               |
| Status   | AUDITED                  |
| Created  | 2026-10-07               |
| Updated  | 2026-10-07               |
| Approved | 2026-10-07               |

## Context and objective

`dispatch-calendar` assigns visits to technicians, but technicians have no
place to see their work. This feature delivers Design 7, **Today's jobs**: a
mobile-first page at `/today` inside a dedicated technician shell with a bottom
navigation. The signed-in technician sees only the visits actively assigned to
their own `TechnicianProfile` for the current local day: greeting, metrics,
progress, the next job and the ordered route. **Start travel** and **View job
details** open a minimal read-only job page that Design 8 will extend. No visit
state changes in this feature.

## Actors and permissions

| Actor                                                                    | Can                                                                                                                                                                     | Cannot                                                                                     |
| ------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Technician (`technician`) with an `active` linked profile                | Read today's visits actively assigned to their own profile; read the job page of any visit actively assigned to them (BR-11); open external directions and `tel:` links | See other technicians' visits, choose a technician, change any visit, time entry or status |
| Technician with an `inactive` or `suspended` linked profile              | —                                                                                                                                                                       | Every endpoint of this feature (`403 technician_inactive`); no visit is returned           |
| Technician without a linked profile                                      | —                                                                                                                                                                       | Every endpoint (`404 technician_profile_not_linked`); the page shows the explanatory state |
| Owner, Operations Manager, Dispatcher, Viewer, Accounting, unknown roles | —                                                                                                                                                                       | Every endpoint (`403`); `/today` and its child pages show the existing forbidden state     |

## Scope

- Route `/today` (Today's jobs) and `/today/visits/:visitId` (minimal job page),
  both inside a new technician shell.
- Technician shell: top bar and bottom navigation (BR-14, BR-15), Coming soon
  destinations for Schedule, Time, Messages and notifications reusing the
  existing parameterized Coming soon page.
- Technician landing on `/today` after sign-in and invitation acceptance, and a
  **Today's jobs** entry in the existing desktop shell for technicians (BR-16).
- Read endpoints `GET /technician/today` and `GET /technician/visits/{visitId}`;
  the second is the base Design 8 extends.
- Installability: web app manifest and mobile meta tags only (BR-17).

## Non-goals

- GPS, location tracking, embedded maps, **View full map**, distances, travel or
  drive times, route optimization.
- Messaging (**Message** button), customer notifications, notification badges.
- Inventory and material readiness ("Ready").
- Time clock / **Clocked in**, time entries.
- Visit state changes (on the way, start, pause, complete), checklist,
  evidence, sign-off — Design 8 and later.
- Viewing other days, technician schedule, offline mode, service worker, data
  caching, push notifications.
- Schema changes and any writes.

## User flow

1. A technician signs in; the system opens `/today`.
2. The page loads `GET /technician/today` and shows the greeting, metrics,
   progress, the next job card and the ordered route.
3. The technician taps **Call**, **Directions** (external Google Maps), **Start
   travel**, **View job details** or a route row; the last three open
   `/today/visits/:visitId`.
4. The job page shows the read-only visit summary and "Job execution is coming
   soon."; **Back to Today's jobs** returns.
5. The technician refreshes manually; "Updated <relative time>" reflects the last
   successful load.

## Functional requirements

| ID    | Requirement                                                                                                                                                                                                            |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| FR-01 | Every endpoint must resolve organization, membership and `TechnicianProfile` from the session and return only visits actively assigned to that profile, never accepting a technician identifier (BR-01, BR-02, BR-03). |
| FR-02 | `GET /technician/today` must return the caller's visits for the current local day per BR-04, ordered per BR-05.                                                                                                        |
| FR-03 | The response and page must show the greeting, date, metrics and progress per BR-06–BR-08.                                                                                                                              |
| FR-04 | The page must show the next job card per BR-09 and BR-10, with Call and Directions per BR-12.                                                                                                                          |
| FR-05 | The page must show today's route per BR-05 and BR-13.                                                                                                                                                                  |
| FR-06 | `GET /technician/visits/{visitId}` and `/today/visits/:visitId` must show the minimal read-only job page per BR-11, opened from Start travel, View job details and route rows without any state change.                |
| FR-07 | The technician shell must provide the top bar, bottom navigation and Coming soon destinations per BR-14 and BR-15.                                                                                                     |
| FR-08 | Technicians must land on `/today` and reach it from the desktop shell per BR-16.                                                                                                                                       |
| FR-09 | The frontend must ship a web app manifest without service worker per BR-17.                                                                                                                                            |
| FR-10 | The page must reproduce Design 7 with the deviations, states and responsive behavior of BR-18 and BR-19.                                                                                                               |

## Business and validation rules

| ID    | Rule                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         | Enforced by |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------- |
| BR-01 | Policy **TechnicianSelf** = role `technician` only. Any other role → `403`. Frontend role checks are UX only.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | Both        |
| BR-02 | Profile resolution: organization and membership come from the validated session; the profile is the one whose `organization_user_id` is the caller's membership in that organization (`ux_technician_profiles_org_user`). None → `404` with `code` `technician_profile_not_linked`. Profile `status` `inactive` or `suspended` → `403` with `code` `technician_inactive`, checked before any visit is read, and no visit data is returned. No path, query or body parameter carries a technician or organization id; any such field is ignored.                                                                                                                              | Backend     |
| BR-03 | Visibility: a visit is the caller's when it belongs to the session organization and has a `visit_assignments` row for the caller's profile with `unassigned_at IS NULL`. Branch scope (`organization_user_branches`) is not applied; the assignment is the authority. Any other visit — another technician's, unassigned from the caller, another organization's or nonexistent — → `404`, identical in every case, with no foreign data.                                                                                                                                                                                                                                    | Backend     |
| BR-04 | Today and time zone: the time zone is the `timezone` of the profile's `branch_id` branch, falling back to `organizations.timezone`. Today = `[local 00:00, next local 00:00)` of the current date in that zone, computed with DST-aware conversion. A visit is today's when `scheduled_start` is inside that interval and its status is neither `unscheduled` nor `cancelled`. The response returns `date` (`YYYY-MM-DD`), `timezone` and instants as ISO with offset; the page formats every date and time in that zone, never the device zone.                                                                                                                             | Backend     |
| BR-05 | Route order: `scheduled_start` ascending, then work order number ascending, then visit number ascending. The response list and the page route use this order; position numbers 1…n follow it.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                | Backend     |
| BR-06 | Greeting: "Good morning" from 05:00 to 11:59, "Good afternoon" from 12:00 to 17:59, "Good evening" otherwise, by local time in the BR-04 zone at page load, followed by ", <profile first_name>". Below it the date "EEEE, MMM d, yyyy".                                                                                                                                                                                                                                                                                                                                                                                                                                     | Frontend    |
| BR-07 | Metrics over today's visits: **jobs** = count; **scheduled hours** = sum of `scheduled_end − scheduled_start` in hours, one decimal, a trailing ".0" dropped ("7.5", "8"); **remaining** = jobs − completed. Completed = status `completed` or `approved`; `needs_correction` counts as not completed. Labels: "job"/"jobs", "scheduled hour"/"scheduled hours", "remaining". The three cards keep Design 7 layout with their icons; with 0 jobs they show 0.                                                                                                                                                                                                                | Both        |
| BR-08 | Progress: "<completed> of <jobs> completed", a bar and the percentage `round(completed / jobs × 100)` with "%". The bar exposes its value and the same text to assistive technology. Hidden when jobs = 0.                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | Frontend    |
| BR-09 | Next job: the first visit in BR-05 order whose status is `on_the_way`, `in_progress` or `paused`; otherwise the first whose status is `scheduled` or `assigned`; otherwise none. Card label "IN PROGRESS" for the first group and "NEXT" for the second, followed by " • h:mm a – h:mm a" (scheduled start–end). No next job, without card or actions: jobs = 0 → "No jobs scheduled for today."; jobs > 0 and remaining = 0 → "All jobs completed for today."; remaining > 0 (for example only `needs_correction` visits left) → "No more jobs to start today.".                                                                                                            | Both        |
| BR-10 | Next job card content: priority chip with icon and text only for priority 1 "Urgent" and 2 "High priority"; title; "#<prefix>-<n>"; customer display name; property address one line ("line1, city, state postal"); service category chip; schedule "h:mm a – h:mm a" with the arrival window line "Arrival window" (`arrival_window_start`–`arrival_window_end`; "Arrival at h:mm a" when both equal; hidden when null); "<n> planned materials" ("1 planned material"; hidden when 0) without readiness. Actions: **Call** and **Directions** (BR-12), **Start travel** (primary) and **View job details** — both navigate to `/today/visits/:visitId` and change nothing. | Both        |
| BR-11 | Job page: `GET /technician/visits/{visitId}` returns a visit visible per BR-03 whose status is not `unscheduled` or `cancelled`, on any date; otherwise `404`. The page shows title, "#WO-n", the status label of the visit's actual status (BR-13 texts, never "Next"), local date "EEE, MMM d", schedule and arrival window, customer, address with **Call** and **Directions**, service category, planned materials count, the dispatch note under "Dispatch note" (hidden when null), the note "Job execution is coming soon." and **Back to Today's jobs**. `404` → "This job isn't available." with **Back to Today's jobs**. Read-only; no write occurs.              | Both        |
| BR-12 | Call and Directions: **Call** is a `tel:` link to the phone of the customer's primary active contact (`customer_contacts.is_primary AND is_active`), hidden when null. **Directions** opens in a new browsing context (`noopener`) `https://www.google.com/maps/dir/?api=1&destination=<value>` with `<value>` = "latitude,longitude" when both are set, else the URL-encoded one-line address. No map API, key or provider integration.                                                                                                                                                                                                                                     | Frontend    |
| BR-13 | Route section "Today's route": one row per today's visit in BR-05 order with the position marker (a check icon when completed), "h:mm a", title, property `address_line1` and a status chip: `completed`/`approved` "Completed", the BR-09 next visit "Next", `scheduled`/`assigned` "Scheduled", `on_the_way` "On the way", `in_progress` "In progress", `paused` "Paused", `needs_correction` "Needs correction". The next row is highlighted. Status is conveyed by text, never color alone. Each row is a link to `/today/visits/:visitId`. No map, **View full map** or drive times. Hidden when jobs = 0.                                                              | Frontend    |
| BR-14 | Technician shell top bar: FieldOps logo, page title ("Today's jobs" on `/today`, "Job details" on the job page, the module name on Coming soon pages), notifications bell linking to the `notifications` Coming soon destination without badge, and the avatar (profile initials, `color_hex` when set) with a menu showing the user's name and **Sign out** (existing sign-out flow). Under the greeting: "Updated <relative time>" since the last successful load ("just now" under 1 minute, then "<n> min ago") with a refresh button that reloads once at a time; no automatic refresh.                                                                                 | Frontend    |
| BR-15 | Bottom navigation, fixed: **Today** (`/today`, active on `/today` and its job pages), **Schedule**, **Time**, **Messages** — Coming soon destinations inside the technician shell with the shared parameterized Coming soon page, titled "Schedule", "Time", "Messages", text "<Module> isn't available yet." and **Back to Today's jobs** — and **More**, opening a panel with **My profile** (existing own-profile view at `/team`) and **Sign out**. The current item has `aria-current="page"`. No unread dots. Unknown technician Coming soon modules → `/today`.                                                                                                       | Frontend    |
| BR-16 | Landing: after successful sign-in or invitation acceptance with no valid `returnUrl`, role `technician` navigates to `/today`; other roles keep `/overview`. A valid `returnUrl` keeps its existing behavior. In the existing desktop shell, technicians see a **Today's jobs** navigation item linking to `/today`; other roles do not.                                                                                                                                                                                                                                                                                                                                     | Frontend    |
| BR-17 | Installability: a web app manifest (name and short name "FieldOps", `start_url` "/", `display` `standalone`, teal `theme_color`, background color, 192 px and 512 px icons from the FieldOps logo) linked from the document with mobile `theme-color` and viewport meta. No service worker, offline mode or caching of authenticated data; no new dependency.                                                                                                                                                                                                                                                                                                                | Frontend    |
| BR-18 | Design 7 deviations: no "Clocked in" chip, no travel card (replaced by **remaining**), no "min away · mi", no "Ready", no **Message**, no "Customer will be notified…" text, no map, **View full map** or drive times, no unread dots. Data in the mockup are examples.                                                                                                                                                                                                                                                                                                                                                                                                      | Frontend    |
| BR-19 | Responsive and accessibility: mobile-first; at 375 px no page-level horizontal scroll; from 768 px the technician shell renders as a centered column of at most 480 px with the bottom navigation inside it. The bottom bar respects the device safe-area inset and content is never hidden behind it. Interactive targets are at least 44 × 44 px; every control has a visible focus indicator; the route is an ordered list; external links state that they open a new tab in their accessible name.                                                                                                                                                                       | Frontend    |

## States and transitions

None. This feature reads visits and never changes their status.

## Data and persistence impact

- Tables and columns read (from `docs/database/fieldops-schema.sql`):
  `organization_users`, `technician_profiles` (`organization_user_id`,
  `branch_id`, `status`, `first_name`, `last_name`, `color_hex`), `branches`
  (`timezone`), `organizations` (`timezone`, `work_order_prefix`), `visits`
  (`id`, `work_order_id`, `visit_number`, `status`, `scheduled_start`,
  `scheduled_end`, `arrival_window_start`, `arrival_window_end`,
  `dispatch_note`), `visit_assignments` (`visit_id`, `technician_id`,
  `unassigned_at`), `work_orders` (`work_order_number`, `title`, `priority`,
  `service_category_id`, `customer_id`, `property_id`), `service_categories`
  (`name`), `customers` (`display_name`), `customer_contacts` (`phone`,
  `is_primary`, `is_active`), `properties` (address columns, `latitude`,
  `longitude`), `work_order_planned_materials` (count).
- Writes: none. No audit rows.
- Schema amendments required: None.

## Tenant isolation and authorization

- Organization context: resolved from the validated session membership. The
  profile is resolved from that membership (BR-02).
- Client-provided identifiers: only `visitId` (path), authorized against the
  session organization and an active assignment of the caller's profile;
  otherwise `404` (BR-03). No technician or organization id is accepted.
- Another technician's visit, a visit unassigned from the caller, or a visit of
  another organization → `404` with no foreign data; lists never include them.
- Policy per BR-01; inactive/suspended profile `403`, unlinked `404` (BR-02).
- Responses use `Cache-Control: no-store`. Logs never contain addresses,
  phones, customer names or dispatch notes.

## API contracts

Contract status: Final. Errors use ProblemDetails; `403`/`404` of BR-02 carry
`code`.

`TodayVisit` = `{ visitId, visitNumber, workOrderId, displayNumber, title,
status, start, end, arrivalWindowStart | null, arrivalWindowEnd | null,
priority, serviceCategory (string, category name), customerName, phone | null, address: { line1,
line2 | null, city, stateRegion | null, postalCode | null, countryCode },
latitude | null, longitude | null, plannedMaterialsCount }`.

| Method | Path                           | Request | Success                                                                                                                                                                               | Errors                                                                                      | Permission     |
| ------ | ------------------------------ | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- | -------------- |
| GET    | `/technician/today`            | —       | `200 { date, timezone, technician: { firstName, initials, colorHex \| null }, metrics: { jobs, scheduledMinutes, completed, remaining }, nextVisitId \| null, visits: TodayVisit[] }` | `403` (role; `technician_inactive`) · `404 technician_profile_not_linked`                   | TechnicianSelf |
| GET    | `/technician/visits/{visitId}` | —       | `200 TechnicianVisitDetail = TodayVisit & { date, timezone, dispatchNote \| null }`                                                                                                   | `403` (role; `technician_inactive`) · `404` (BR-03, BR-11; `technician_profile_not_linked`) | TechnicianSelf |

`TechnicianVisitDetail` is the contract Design 8 extends; fields are additive.

## UI behavior and states

| Screen                   | Loading                                                                                  | Empty                                                                                                                                                                 | Error                                                                                                                                    | Permission                                                                                                                                                                                                                                                         | Success                   |
| ------------------------ | ---------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------- |
| `/today`                 | Skeleton greeting, metric cards, next card and route rows; refresh button shows progress | jobs = 0: "No jobs scheduled for today."; remaining = 0: "All jobs completed for today."; no startable job with remaining > 0: "No more jobs to start today." (BR-09) | "We couldn't load today's jobs. Try again." with **Retry**; a failed manual refresh keeps the last data and shows the error inline       | Non-technician: existing forbidden state, no request. `404 technician_profile_not_linked`: "Your team profile isn't linked yet. Contact your manager.". `403 technician_inactive`: "Your technician profile is inactive. Contact your manager." with no visit data | BR-06–BR-10, BR-13, BR-14 |
| `/today/visits/:visitId` | Skeleton summary                                                                         | —                                                                                                                                                                     | `404`: "This job isn't available." with **Back to Today's jobs**; other failures: "We couldn't load this job. Try again." with **Retry** | As `/today`                                                                                                                                                                                                                                                        | BR-11                     |
| Technician Coming soon   | —                                                                                        | —                                                                                                                                                                     | —                                                                                                                                        | As `/today`                                                                                                                                                                                                                                                        | BR-15                     |

- Mockups: `design/assets/7-design.png` (Approved; primary reference) with the
  BR-18 deviations.
- Responsive and accessibility: BR-19.

## Error behavior

| Case                                                                                                   | Response / message shown                               | Data change |
| ------------------------------------------------------------------------------------------------------ | ------------------------------------------------------ | ----------- |
| Role other than `technician`                                                                           | `403`; forbidden state                                 | None        |
| No linked profile                                                                                      | `404 technician_profile_not_linked`; explanatory state | None        |
| Profile `inactive`/`suspended`                                                                         | `403 technician_inactive`; inactive state, no visits   | None        |
| Visit of another technician, unassigned, other organization, nonexistent, `unscheduled` or `cancelled` | `404`; "This job isn't available."                     | None        |
| Load failure                                                                                           | Error state with **Retry**                             | None        |

## Acceptance criteria

| ID    | Given                                                                                                                                                                                                           | When                                                                                                   | Then                                                                                                                                                                                                                                                                                           |
| ----- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| AC-01 | A technician with an active linked profile and visits today assigned to them, to another technician, unassigned from them, `unscheduled`, `cancelled`, and a visit of another organization                      | `GET /technician/today` is called, including with a `technicianId` query parameter for another profile | Only the caller's actively assigned, non-`unscheduled`/`cancelled` visits of today are returned; the extra parameter is ignored; no foreign data appears                                                                                                                                       |
| AC-02 | The caller's visit, another technician's visit, a visit unassigned from the caller, a visit of another organization and a random id                                                                             | `GET /technician/visits/{visitId}` is called for each                                                  | The caller's visit returns `200 TechnicianVisitDetail`; every other case returns the identical `404` with no foreign data                                                                                                                                                                      |
| AC-03 | Owner, Operations Manager, Dispatcher, Viewer and Accounting users (parameterized); a technician without linked profile; a technician whose profile is `inactive` and one `suspended`                           | Each calls both endpoints and opens `/today`                                                           | Other roles get `403` and the forbidden state with no request; unlinked gets `404 technician_profile_not_linked` and the linked-profile message; inactive and suspended get `403 technician_inactive`, the inactive message and no visit data                                                  |
| AC-04 | A profile whose branch is in a time zone different from the organization and from UTC, visits at local 00:00, 23:59, previous-day 23:30 and next-day 00:15, and a branch without `timezone`                     | Today is requested                                                                                     | Exactly the visits whose `scheduled_start` falls in the branch local day are returned with `date` and `timezone`; the branch without time zone uses the organization zone; the page formats times in that zone regardless of the device zone                                                   |
| AC-05 | A DST transition day in the profile's zone                                                                                                                                                                      | Today is requested                                                                                     | The day interval follows the local midnights of that date and includes visits across the transition correctly                                                                                                                                                                                  |
| AC-06 | Visits today with equal and distinct start times across work orders and visit numbers                                                                                                                           | Today is loaded                                                                                        | `visits` and the route follow BR-05 order and positions 1…n                                                                                                                                                                                                                                    |
| AC-07 | Today's visits with statuses `completed`, `approved`, `needs_correction`, `assigned` and `scheduled` and known durations (table-driven, including 0 jobs)                                                       | Today is loaded                                                                                        | `jobs`, `scheduledMinutes`, `completed` and `remaining` match BR-07; cards show "7.5"/"8" formatting and singular/plural labels; progress shows "<x> of <n> completed" and the rounded percentage, hidden with 0 jobs                                                                          |
| AC-08 | Combinations of statuses (table-driven): one `in_progress` after an `assigned`, only `scheduled`/`assigned`, all completed, completed plus one `needs_correction`, none                                         | Today is loaded                                                                                        | `nextVisitId` and the card label follow BR-09 ("IN PROGRESS" / "NEXT"); without a next job the page shows "All jobs completed for today." only when remaining = 0, "No more jobs to start today." when remaining > 0, and "No jobs scheduled for today." with no jobs, never with card actions |
| AC-09 | A next visit with priority High, arrival window, two planned materials, a primary active contact phone and coordinates; a variant with priority Normal, equal window, no materials, no phone and no coordinates | The card renders                                                                                       | Content follows BR-10: priority chip only for Urgent/High, arrival line or "Arrival at", materials line or hidden; **Call** is a `tel:` link or hidden; **Directions** opens the BR-12 Google Maps URL with coordinates or the encoded address in a new tab                                    |
| AC-10 | The next job card and route rows                                                                                                                                                                                | **Start travel**, **View job details** and a route row are activated                                   | Each opens `/today/visits/:visitId`; no request other than GETs is sent and the visit status is unchanged                                                                                                                                                                                      |
| AC-11 | The job page of an assigned visit with a dispatch note, and an inaccessible id                                                                                                                                  | The page is opened                                                                                     | The visit shows BR-11 content, "Job execution is coming soon." and **Back to Today's jobs**; the inaccessible id shows "This job isn't available."                                                                                                                                             |
| AC-12 | Today's route with each status                                                                                                                                                                                  | The route renders                                                                                      | Rows show position (check when completed), time, title, street and the BR-13 status text; the next row is highlighted and marked "Next"; no map, map link or drive time exists                                                                                                                 |
| AC-13 | A technician at 08:00, 13:00 and 19:00 local                                                                                                                                                                    | `/today` loads                                                                                         | The greeting is "Good morning", "Good afternoon" or "Good evening, <first name>" with the full local date                                                                                                                                                                                      |
| AC-14 | The loaded page                                                                                                                                                                                                 | Time passes and refresh is used, once successfully and once failing                                    | "Updated just now" becomes "Updated <n> min ago"; refresh reloads once at a time, updates the text on success and keeps the data with an inline error on failure                                                                                                                               |
| AC-15 | `/today` failing to load                                                                                                                                                                                        | The page is opened and **Retry** is used                                                               | The error state shows and Retry reloads                                                                                                                                                                                                                                                        |
| AC-16 | The technician shell                                                                                                                                                                                            | The bottom navigation, bell, avatar menu and More panel are used                                       | Today is current on `/today` and job pages; Schedule, Time, Messages and the bell open the shared Coming soon page in the technician shell with their names and **Back to Today's jobs**; More shows **My profile** and **Sign out**; Sign out uses the existing flow; no unread dots          |
| AC-17 | A technician and a dispatcher                                                                                                                                                                                   | Each signs in or accepts an invitation without `returnUrl`, and with a valid `returnUrl`               | The technician lands on `/today`, the dispatcher on `/overview`; a valid `returnUrl` is honored; in the desktop shell only the technician sees **Today's jobs**                                                                                                                                |
| AC-18 | The built frontend                                                                                                                                                                                              | The document is served                                                                                 | It links a manifest with the BR-17 fields and icons and the `theme-color` meta; no service worker is registered and no dependency is added                                                                                                                                                     |
| AC-19 | Viewports 375 px and 1280 px                                                                                                                                                                                    | The page is used by keyboard and touch                                                                 | Design 7 layout with BR-18 deviations; at 375 px no page-level horizontal scroll and nothing hidden behind the bottom bar; at 1280 px a centered column ≤ 480 px; targets ≥ 44 px; visible focus; ordered route list; external links announce the new tab                                      |

## Testing requirements

| Level                          | Behavior or risk to prove                                                                                                                                                                                                                                                                                                                                                                                                                                             | Evidence for        |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------- |
| Backend unit                   | Local day interval and DST boundaries for a time zone (only if implemented as a separate utility; otherwise covered by integration)                                                                                                                                                                                                                                                                                                                                   | AC-04, AC-05        |
| Backend integration            | Today selection: active assignment only, status exclusions, ignored client technician id                                                                                                                                                                                                                                                                                                                                                                              | AC-01               |
| Backend integration            | Time zone: branch zone, organization fallback, day boundaries                                                                                                                                                                                                                                                                                                                                                                                                         | AC-04, AC-05        |
| Backend integration            | Order, metrics and next selection, table-driven                                                                                                                                                                                                                                                                                                                                                                                                                       | AC-06, AC-07, AC-08 |
| Authorization/tenant isolation | Detail `404` for another technician's, unassigned and other-organization visits; `403` for other roles (parameterized) and inactive/suspended; `404` unlinked                                                                                                                                                                                                                                                                                                         | AC-02, AC-03        |
| Frontend component/service     | Today page: metrics, progress, next card variants (Call/Directions URLs, priority, arrival window), route statuses, empty/error/inactive/unlinked states, refresh text; job page and its `404`; technician landing and bottom navigation/Coming soon; Start travel/View job details navigation without mutation; the existing parameterized `authenticated-app-shell` role-visibility evidence is updated for the technician-only **Today's jobs** item (3–6 methods) | AC-03, AC-07–AC-17  |
| User visual QA (manual)        | Design 7 fidelity, responsive layout, safe area, focus, manifest presence                                                                                                                                                                                                                                                                                                                                                                                             | AC-18, AC-19        |

Backend integration stays within 3–8 methods. Agents run no browser automation
or Playwright.

## Dependencies

- `dispatch-calendar` (APPROVED): visit schedules, arrival windows, dispatch
  note and active assignments.
- `team-technician-management` (AUDITED): profile ↔ membership link, statuses,
  own-profile view at `/team` (BR-20).
- `authenticated-app-shell` (AUDITED): desktop shell, forbidden state, sign-out
  flow, Coming soon page.
- `sign-in` and `invitation-acceptance` (AUDITED): post-authentication
  navigation and `returnUrl` handling.

## Assumptions

| ID    | Assumption (minor, non-behavioral)                                                                                                 |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------- |
| AS-01 | Greeting thresholds 05:00 / 12:00 / 18:00 (confirmed 2026-10-07).                                                                  |
| AS-02 | Visits do not cross local midnight (`dispatch-calendar` AS-01); membership in "today" uses `scheduled_start`.                      |
| AS-03 | The job page accepts any date of an actively assigned visit so Design 8 can reuse it; only `/today` is limited to the current day. |
| AS-04 | Manifest icons are derived from the existing FieldOps logo asset.                                                                  |

## Open decisions

| ID    | Question                                                       | Options                                                | Blocking | Resolution                                                                                                        |
| ----- | -------------------------------------------------------------- | ------------------------------------------------------ | -------- | ----------------------------------------------------------------------------------------------------------------- |
| OD-01 | Route and landing                                              | `/today` technician-only + landing · Overview entry    | No       | `/today`, technician landing (BR-16) — user 2026-10-07                                                            |
| OD-02 | Shell                                                          | Dedicated technician shell · responsive existing shell | No       | Dedicated shell, centered column on wide screens (BR-14, BR-19) — user 2026-10-07                                 |
| OD-03 | Bottom navigation                                              | Five items with Coming soon · Today + More             | No       | Five items; one parameterized Coming soon page; More with My profile and Sign out (BR-15) — user 2026-10-07       |
| OD-04 | Clocked in                                                     | Omit · disabled chip                                   | No       | Omitted (BR-18) — user 2026-10-07                                                                                 |
| OD-05 | Updated indicator                                              | Manual refresh · auto refresh                          | No       | Relative time + manual refresh (BR-14) — user 2026-10-07                                                          |
| OD-06 | Travel metric                                                  | Omit · "<n> remaining"                                 | No       | "<n> remaining", three cards kept (BR-07) — user 2026-10-07                                                       |
| OD-07 | Today and time zone                                            | Profile branch zone · work order branch zone           | No       | Profile branch zone, organization fallback (BR-04) — user 2026-10-07                                              |
| OD-08 | Completed                                                      | `completed`+`approved` · plus `needs_correction`       | No       | `completed`+`approved` (BR-07) — user 2026-10-07                                                                  |
| OD-09 | Next job                                                       | In-progress first then scheduled · first not completed | No       | BR-09 — user 2026-10-07                                                                                           |
| OD-10 | Next card content                                              | As proposed · with disabled Message                    | No       | BR-10, BR-12; Directions via external Google Maps URL, no provider — user 2026-10-07                              |
| OD-11 | Design 8 entry                                                 | Minimal job page + endpoint · Coming soon              | No       | Minimal page and reusable `GET /technician/visits/{visitId}` (BR-11) — user 2026-10-07                            |
| OD-12 | Route section                                                  | Omit map and drive times · disabled map                | No       | Omitted (BR-13) — user 2026-10-07                                                                                 |
| OD-13 | Unlinked / inactive profile                                    | Unlinked state; inactive read-only · inactive `403`    | No       | Unlinked explanatory state; inactive/suspended `403` with no visits (BR-02) — user 2026-10-07                     |
| OD-14 | PWA                                                            | Manifest only · service worker · none                  | No       | Manifest only, no service worker or data cache (BR-17) — user 2026-10-07                                          |
| OD-15 | No startable job while visits remain (e.g. `needs_correction`) | Distinct text · `needs_correction` as next             | No       | "No more jobs to start today."; "All jobs completed for today." only when remaining = 0 (BR-09) — user 2026-10-07 |

## Traceability

| FR    | AC                         |
| ----- | -------------------------- |
| FR-01 | AC-01, AC-02, AC-03        |
| FR-02 | AC-01, AC-04, AC-05, AC-06 |
| FR-03 | AC-07, AC-13               |
| FR-04 | AC-08, AC-09               |
| FR-05 | AC-06, AC-12               |
| FR-06 | AC-10, AC-11               |
| FR-07 | AC-14, AC-16               |
| FR-08 | AC-17                      |
| FR-09 | AC-18                      |
| FR-10 | AC-15, AC-19               |

## Change log

| Date       | Status change          | Reason                                                                                                                                                                                              |
| ---------- | ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2026-10-07 | — → DRAFT              | Created                                                                                                                                                                                             |
| 2026-10-07 | DRAFT → DRAFT          | Revised after validation: no-startable-job text (BR-09, UI states, AC-08, OD-15); `serviceCategory` type; job page status label (BR-11); AC-15 traced to FR-10; shell role-visibility evidence note |
| 2026-10-07 | DRAFT → APPROVED       | Approved by user via /spec approve                                                                                                                                                                  |
| 2026-10-07 | APPROVED → IMPLEMENTED | Required implementation workflows completed.                                                                                                                                                        |
| 2026-10-07 | IMPLEMENTED → AUDITED  | Final audit waived by user (audit not completed); user visual QA PASS stated by user. CI is authoritative.                                                                                          |
