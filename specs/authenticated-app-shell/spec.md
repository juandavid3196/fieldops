# Authenticated app shell and navigation

| Field    | Value                    |
| -------- | ------------------------ |
| Feature  | `authenticated-app-shell` |
| Type     | Frontend                 |
| Status   | AUDITED                  |
| Created  | 2026-09-28               |
| Updated  | 2026-09-28               |
| Approved | 2026-09-28               |

## Context and objective

Authenticated pages (`/overview`, `/admin/company`) currently render on their
own, with Sign out and the Company settings link only on Overview. This feature
adds one shared authenticated layout, based on designs 17 and 18: a responsive
sidebar, a top bar, a mobile navigation drawer, the organization, user and role
from the current session, and Sign out. Success means that every authenticated
route renders inside the shell, that a user only sees the routes that exist and
that their role can use, and that direct-route authorization stays in the
backend unchanged.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Signed-in `owner` or `viewer` | See **Overview** and **Administration → Company settings** in the navigation; open both routes; sign out | Gain any access from the navigation; the backend authorizes every request |
| Signed-in `dispatcher`, `technician`, `accounting`, `operations_manager` (or an unknown role code) | See **Overview** only; sign out; open `/admin/company` directly, which shows the existing in-page forbidden state inside the shell | See the Administration group; read company settings (API `403`, unchanged) |
| Unauthenticated visitor | — | See the shell; opening `/overview` or `/admin/company` redirects to `/auth/sign-in` |
| System | Show the shell only after `GET /sessions/current` returns `200`; take organization, user and role only from that session | Accept an organization, user or role from the URL or client storage |

## Scope

- One authenticated layout wrapping `/overview` and `/admin/company` only.
- Sidebar navigation with permission-aware visibility (BR-01) and active-route
  indication.
- Top bar: FieldOps wordmark, organization name, user menu (initials, full
  name, role name) containing **Sign out**.
- Mobile navigation drawer below `lg`.
- Sign out moved from Overview to the shell, including the `401` and failure
  outcomes.
- Overview reduced to its heading and Organization/Role details (supersedes
  the duplicated controls, see Dependencies).
- Sign out bypasses the Company setup unsaved-changes prompt once the session
  is cleared.
- Responsive layout and accessibility of the shell.

## Non-goals

- Placeholder pages, links, groups or disabled items for features that do not
  exist (Requests, Quotes, Work orders, Schedule, Customers, Team, Products and
  services, Invoices, Reports, Users & permissions, Business hours,
  Notifications).
- Global search, organization selector or switching, notifications, Help,
  dark-mode toggle, avatar photos.
- Sidebar collapse to icons, collapse toggle, Administration sub-nav as shared
  navigation (Company setup keeps its in-page section links).
- `returnUrl`, session-expired message on Sign In, role-based landing pages.
- Frontend route guards based on role; changes to backend authorization,
  session contract, API, persistence or migrations.
- Navy sidebar, Inter, handoff hex values, PrimeIcons or any new package,
  icon set, token or breakpoint.
- Changing the content, states or behavior of Company setup beyond FR-09.

## User flow

1. A signed-in user opens `/overview` or `/admin/company`.
2. The system revalidates the session (`GET /sessions/current`); on `200` it
   renders the shell with the route content; otherwise it navigates to
   `/auth/sign-in` without rendering the shell.
3. The shell shows the organization name and the user menu, and the navigation
   items allowed by BR-01, with the current route marked active.
4. Below `lg`, the user opens the navigation drawer with the menu button and
   selects an item; the drawer closes and the route changes.
5. The user opens the user menu and selects **Sign out**. The system sends
   `DELETE /sessions/current`; on `204` or `401` it clears the session and
   navigates to `/auth/sign-in`; otherwise it stays and shows the error.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must render `/overview` and `/admin/company` inside one authenticated shell layout, and no other route (Sign In, company registration, redirects) inside it. Route paths and page titles stay unchanged. Every navigation into a shell route must revalidate the session with `GET /sessions/current` through `authGuard` before activation; a non-`200` result navigates to `/auth/sign-in` without rendering the shell. |
| FR-02 | The sidebar must list exactly the items allowed by BR-01, in this order: **Overview**, then an **Administration** group heading followed by **Company settings**. A group with no visible items is not rendered. No other navigation item exists. |
| FR-03 | The navigation item whose route matches the current URL must be marked active, visually and with `aria-current="page"`; exactly one item is active on a shell route. |
| FR-04 | The top bar must show the FieldOps wordmark, the session organization name as plain text, and a user menu button showing the BR-02 initials, the user's full name and the role name. These values come only from the current session and update whenever the session state changes, without a page reload. |
| FR-05 | The user menu must contain one action, **Sign out**, which sends `DELETE /sessions/current`. While it is in flight, the action shows progress and further activations send no request. On `204` or `401` the frontend clears the session and navigates to `/auth/sign-in`, replacing the history entry. On any other failure the session is kept, the shell shows BR-03 sign-out error as an alert, and focus returns to the user menu button. |
| FR-06 | Below `lg` (64rem) the sidebar must be hidden and a menu button in the top bar must open a modal navigation drawer from the left with the same BR-01 items and active state. Selecting an item navigates and closes the drawer; Esc, the close button and the backdrop close it; focus returns to the menu button. From `lg` the sidebar is always visible and the menu button is not rendered. |
| FR-07 | `/overview` must show only the `h1` "Welcome, {firstName}" and the Organization/Role description list. Its Sign out button, sign-out error message and Company settings link are removed. |
| FR-08 | Hidden navigation must not replace authorization: `/admin/company` stays reachable by URL for every signed-in user, and a role outside BR-01 sees the existing Company setup forbidden state inside the shell. Backend authorization is unchanged. |
| FR-09 | Leaving Company setup must not show the "Discard unsaved changes?" prompt once the session has been cleared, so sign-out with unsaved edits navigates to `/auth/sign-in` directly and discards them. The prompt behaves as before for every other navigation while a session exists. |
| FR-10 | The shell must meet the responsive and accessibility rules in UI behavior. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | -------- | ----------- |
| BR-01 | Navigation visibility by session `role.code`: **Overview** — every signed-in user. **Administration → Company settings** — `owner`, `viewer` (mirrors backend policy `CompanySettingsView`). Any other or unknown code sees Overview only. `role.code` controls visibility only; it never grants access, and the backend stays authoritative. | Frontend (UX only) |
| BR-02 | Full name: trimmed `firstName` and trimmed `lastName` joined by one space; if one is empty, only the other. Initials: first character of the trimmed `firstName` and of the trimmed `lastName`, uppercased; if one is empty, only the other. The initials are decorative; the button's accessible name contains the full name. | Frontend |
| BR-03 | Copy: sidebar landmark "Main navigation"; menu button "Open navigation"; drawer close "Close navigation"; user menu button accessible name "Account menu, {full name}, {role name}"; action **Sign out**; sign-out error "We couldn't sign you out. Try again." | Frontend |

## States and transitions

Not applicable: the shell creates no persistent state. Session transitions
(signed in → signed out) follow `specs/sign-in/spec.md`.

## Data and persistence impact

- Tables and columns used: none directly. The shell reads the existing
  `GET /sessions/current` body.
- Schema amendments required: None. No backend or EF migration.

## Tenant isolation and authorization

- Organization context: resolved server-side from the validated session
  cookie (sign-in FR-07); the shell only displays `organization.name`.
- Client-provided organization identifiers: none are sent; the shell never
  reads organization, user or role from the URL or browser storage.
- Another organization's data: not reachable through the shell; unchanged
  backend behavior.
- Permissions: BR-01 is UX only. `/admin/company` authorization stays in
  `CompanySettingsView`/`CompanySettingsManage` (company-settings BR-10);
  direct URLs return the existing `403` forbidden state for other roles.

## API contracts

Contract status: Final. Existing endpoints only; no change.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/sessions/current` | — | `200` `{ user: { id, firstName, lastName, email }, organization: { id, name }, role: { code, name } }` | `401` | Signed-in |
| DELETE | `/sessions/current` | — | `204` | `401` (treated as signed out by FR-05) · `5xx`/network | Signed-in |

## UI behavior and states

Design references (approved), used for the shell only:

- `design/identity-access/screens/17-design.png`
- `design/identity-access/screens/18-design.png`
- `design/identity-access/handoff/Identity-and-Organization-Handoff.md` §4
  (`AppShellComponent`, `SidebarNav`, `TopBar`) and §6 correction 1 (nav
  labels follow the spec).

Adaptations to the designs: only Overview and Administration → Company
settings exist; no search, organization selector, notifications, Help,
collapse control, sub-nav or navy sidebar. The wordmark reuses the Sign In
"Field"/"Ops" wordmark pattern. Active item uses teal primary tokens on an
Aura surface.

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Shell | Not applicable: `authGuard` resolves the session before the shell renders; sign out shows progress on its action | Not applicable: Overview is always visible | Sign-out failure: BR-03 alert, session kept. Session expired (`401` from a page request): existing page behavior navigates to Sign In; the shell adds none | BR-01 filtered navigation; forbidden content for `/admin/company` stays the page's existing state | Sign out navigates to `/auth/sign-in` |
| Overview | Not applicable | Not applicable | Not applicable | Not applicable | Heading and Organization/Role list (FR-07) |

- Mockups: designs 17 and 18 (Approved).
- Responsive (`md` 48rem, `lg` 64rem from `_breakpoints.scss`; no new
  breakpoints):
  - Below `lg`: no sidebar; menu button in the top bar; modal left drawer.
    Below `md` the top bar shows only the menu button, wordmark and user menu
    button with initials; the organization name, full name and role name are
    not displayed and remain only in the user menu button's accessible name.
    From `md` they are displayed, each on one line, and a value longer than
    its space is cut off with an ellipsis (full value in the accessible name).
  - From `lg`: fixed sidebar beside the content; no menu button.
  - No horizontal page scroll at 320px; touch targets ≥44px below `lg`.
    Route content (including Company setup's sticky bottom bar and drawer)
    stays fully visible and operable inside the shell.
- Accessibility:
  - Landmarks: top bar in a `header`, sidebar and drawer in a `nav` labelled
    "Main navigation", route content in the single `main`.
  - The Administration group heading is exposed as a group label, not a link.
  - Drawer: labelled modal dialog that traps focus; Esc closes it and focus
    returns to the menu button.
  - User menu: opens with Enter/Space, arrow keys move within it, Esc closes
    it and returns focus to its button.
  - Visible focus on every control; the active item is not indicated by color
    alone.
- Styling and icons: Aura + teal tokens only; no PrimeIcons or new package.
  Use only icons already exposed by installed PrimeNG components; otherwise
  use semantic text controls.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Session check fails on navigation (`401`, network, `5xx`) | Navigate to `/auth/sign-in`; shell not rendered | None |
| `DELETE /sessions/current` → `401` | Treated as signed out: session cleared, navigate to `/auth/sign-in` | None |
| `DELETE /sessions/current` → other failure | "We couldn't sign you out. Try again." (alert); session and page kept; focus to user menu button | None |
| Other role opens `/admin/company` | Existing forbidden state inside the shell | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A signed-in `owner` or `viewer` (parameterized) | A shell route renders | The navigation shows exactly **Overview** and the **Administration** group with **Company settings**, in that order |
| AC-02 | A signed-in `dispatcher`, `technician`, `accounting`, `operations_manager` or unknown role code (parameterized) | A shell route renders | The navigation shows only **Overview**; no Administration group heading or Company settings item exists |
| AC-03 | A signed-in `dispatcher` | They open `/admin/company` by URL | The shell renders with only **Overview** in the navigation and the page shows the existing "You don't have access to company settings." state (API `403` unchanged) |
| AC-04 | A visitor without a valid session | They open `/overview` or `/admin/company` | The browser is on `/auth/sign-in` and no shell element was rendered |
| AC-05 | Any visitor | They open `/auth/sign-in` or `/auth/register-company` | The page renders without the sidebar, top bar or navigation drawer |
| AC-06 | A signed-in `owner` | They are on `/overview`, then select **Company settings** | On `/overview` only Overview has `aria-current="page"`; after navigation the browser is on `/admin/company` and only Company settings has it |
| AC-07 | An `owner` on `/admin/company` with unsaved edits | They select **Overview** in the navigation | The existing "Discard unsaved changes?" prompt appears; **Keep editing** keeps the page and edits; **Discard** navigates to `/overview` |
| AC-08 | A session for Alex Morgan, role Owner, organization "FieldOps Services", viewport 1280px | A shell route renders | The top bar shows "FieldOps Services", the initials "AM", "Alex Morgan" and "Owner"; the user menu button's accessible name is "Account menu, Alex Morgan, Owner" |
| AC-09 | An `owner` on `/admin/company` | They save a new display name and the page reloads the session | The top bar shows the new organization name without a page reload |
| AC-10 | A signed-in `owner` whose membership role changes to `dispatcher` | They navigate to another shell route | The session is re-requested before activation and Company settings is no longer in the navigation |
| AC-11 | A signed-in user | They select **Sign out** and receive `204` or `401` (parameterized) | The session service is empty, the browser is on `/auth/sign-in` with the current history entry replaced, and navigating Back to any shell route ends on `/auth/sign-in` without rendering the shell |
| AC-12 | A signed-in user | Sign out fails with `500` or a network error | "We couldn't sign you out. Try again." is shown as an alert, the route and session are kept, and focus is on the user menu button |
| AC-13 | A sign-out request in flight | The user activates **Sign out** again | No second request is sent and the action shows progress |
| AC-14 | An `owner` on `/admin/company` with unsaved edits | They sign out and receive `204` | No unsaved-changes prompt appears and the browser is on `/auth/sign-in` |
| AC-15 | A signed-in user on `/overview` | The page renders | It shows "Welcome, {firstName}" and the Organization and Role list, and no Sign out button or Company settings link |
| AC-16 | Viewport 375px or 800px (parameterized) | The user opens the menu button, then selects an item | The sidebar is hidden; the drawer opens with the BR-01 items and active state; selecting an item navigates and closes the drawer; focus returns to the menu button |
| AC-17 | The navigation drawer open | The user presses Esc, the close button or the backdrop (parameterized) | The drawer closes without navigating and focus returns to the menu button |
| AC-18 | Viewport 1280px | A shell route renders | The sidebar is visible beside the content and no menu button is rendered |
| AC-19 | Viewport 320px on `/overview` and `/admin/company` | The pages render | No horizontal page scroll; the top bar shows only the menu button, wordmark and initials button (organization name, full name and role name not displayed); Company setup's sticky save bar is visible and operable; touch targets ≥44px |
| AC-20 | Keyboard-only use | The user tabs through the top bar and navigation and opens the user menu | Every control is reachable with visible focus; the user menu opens with Enter/Space, Esc closes it and returns focus to its button; landmarks match UI behavior |
| AC-21 | An automated axe check on the shell at 1280px, with the drawer open, with the user menu open, and with the sign-out error | It runs | It reports no violations |

## Testing requirements

Each row is a behavior group with a method budget; parameterized cases count as
one method.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend | None: no backend change. Direct-route `403` and cross-tenant evidence is reused from the unchanged company-settings integration tests | AC-03 (API part) |
| Frontend routing | Shell wraps only `/overview` and `/admin/company`; guard revalidates per navigation and redirects without rendering the shell; public routes render without shell. ≤1 method | AC-04, AC-05, AC-10 |
| Frontend component/service | Parameterized BR-01 role visibility and active-route marking; top bar session context and reactive update; sign-out outcomes (`204`/`401`/failure/in flight) with focus; unsaved-changes bypass when the session is cleared plus unchanged prompt otherwise; mobile drawer open/select/close; trimmed Overview. ≤5 methods | AC-01, AC-02, AC-06 to AC-09, AC-11 to AC-17 |
| Authorization/tenant isolation | Frontend: forbidden state renders inside the shell for a non-BR-01 role. Backend unchanged and not re-tested | AC-03 |
| Browser (final audit) | Owner signs in, uses the navigation to Company settings and back, signs out; 320/375/800/1280 px layouts; keyboard pass; axe checks. Read-only interactions against the local database. 1 scenario | AC-06, AC-11, AC-18 to AC-21 |

## Dependencies

- `specs/sign-in/spec.md` (AUDITED): session contract, `SessionService`,
  `authGuard`, `guestGuard`, `/overview`. This spec supersedes only the
  duplicated presentation controls of its FR-17 (the Overview **Sign out**
  button and its error message move to the shell, same copy); AC-48 to AC-50
  are re-evidenced here by AC-11, AC-12 and AC-15.
- `specs/company-settings-and-branches/spec.md` (APPROVED): `/admin/company`,
  forbidden state, unsaved-changes guard, session reload after save. This spec
  supersedes only the Overview **Company settings** link of its FR-01; AC-19
  there is re-evidenced by AC-06 here. FR-09 extends its FR-17 guard with the
  cleared-session bypass.
- Implementation classification for `spec-impl`: Frontend TARGETED (new shell
  parent route in `layout/`; authenticated routes move under it). Backend N/A.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | The shell lives under `frontend/src/app/layout/`; `admin/company` moves from the mixed public/authenticated organizations route list to the shell's children without changing its lazy page or guards. |
| AS-02 | Drawer and user menu reuse installed PrimeNG overlay components; no new shared abstraction beyond the shell. |
| AS-03 | Sign-out logic moves from Overview into the shell with the same `SessionService.signOut()` call; the `401` case is handled in the shell. |
| AS-04 | The cleared-session bypass reads the existing `SessionService` session signal (null after sign-out). |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Navigation visibility source | Frontend `role.code` / session capabilities / show all | Yes | `role.code` mapping, visibility only, never grants access (2026-09-28) |
| OD-02 | Overview duplicated controls | Remove / keep | Yes | Remove Sign out and Company settings link; keep heading and details (2026-09-28) |
| OD-03 | Navigation structure and labels | Overview + Administration group / flat | Yes | Overview; Administration → Company settings; empty group hidden (2026-09-28) |
| OD-04 | Responsive sidebar | Drawer below `lg` / + icon collapse | Yes | Drawer below `lg`; fixed from `lg`; no collapse (2026-09-28) |
| OD-05 | Top bar contents | Wordmark, org name, user menu / other | Yes | Wordmark, org name, user menu with Sign out; no search/switcher/notifications/Help (2026-09-28) |
| OD-06 | Sign-out outcomes | `401` as signed out / as failure | Yes | `204` and `401` sign out; other failures alert and keep session (2026-09-28) |
| OD-07 | Sign out with unsaved edits | Bypass prompt / prompt first | Yes | Bypass once the session is cleared (2026-09-28) |
| OD-08 | Session revalidation, loading, forbidden, active marker, tokens, icons | Proposed defaults | Yes | Confirmed: revalidate per navigation; no shell loading state; existing forbidden state in shell; `aria-current`; Aura + teal; installed PrimeNG icons or text only (2026-09-28) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-04, AC-05, AC-10 |
| FR-02 | AC-01, AC-02 |
| FR-03 | AC-06 |
| FR-04 | AC-08, AC-09 |
| FR-05 | AC-11, AC-12, AC-13 |
| FR-06 | AC-16, AC-17, AC-18 |
| FR-07 | AC-15 |
| FR-08 | AC-03 |
| FR-09 | AC-07, AC-14 |
| FR-10 | AC-19, AC-20, AC-21 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-28 | — → DRAFT     | Created |
| 2026-09-28 | DRAFT → DRAFT | Fixed validation findings F1 (AC-11 history: entry replaced, Back to a shell route ends on Sign In), F2 (exact top bar display below/from `md`; AC-19 updated; AC-08 set to 1280px) and W1 (full name defined in BR-02) |
| 2026-09-28 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-09-28 | APPROVED → IMPLEMENTED | Required implementation workflows completed. |
| 2026-09-28 | IMPLEMENTED → AUDITED | final-audit returned AUDIT PASS WITH MINOR FINDINGS. |
