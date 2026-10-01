# Design 22 — Team technician management

| Field    | Value                           |
| -------- | ------------------------------- |
| Feature  | `team-technician-management`    |
| Type     | Full-stack                      |
| Status   | APPROVED                        |
| Created  | 2026-10-01                      |
| Updated  | 2026-10-01                      |
| Approved | 2026-10-01                      |

## Context and objective

Organizations need to manage the operational profiles of the technicians they
schedule, independently of the sign-in accounts that control access. This
feature delivers the Design 22 Team page at `/team`: workforce metrics, a
searchable, filterable, paginated technician table with today/this-week
workload, capacity and unlinked-account alerts, skill coverage, and a detail
drawer. Owners and Operations Managers create, edit, activate and deactivate
`TechnicianProfile` records and optionally link or unlink an existing
organization member account without changing that member's role or
permissions. Technicians see only their own profile, read-only. Every read and
write is confined to the caller's organization and branch scope.

## Actors and permissions

Derived from the permission catalog module `team` (Owner Full, Operations
Manager Edit, Dispatcher View, Technician Own profile, Accounting None, Viewer
None) and OD-01/OD-16.

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View page, metrics, alerts, skill coverage and drawer; create, edit, activate, deactivate, link and unlink; all branches | — |
| Operations Manager (`operations_manager`) | Same as Owner; all branches | — |
| Dispatcher (`dispatcher`) | View page, metrics, alerts, skill coverage and read-only drawer within assigned branches (all when `is_all_branches`) | Any mutation or linkable-account lookup (`403`); profiles outside branch scope (`404`) |
| Technician (`technician`) | View only their own linked profile, read-only, via `GET /team/me` | List, metrics, alerts, skill coverage, other profiles and every mutation (`403`) |
| Accounting (`accounting`), Viewer (`viewer`) | — | Every endpoint in this spec (`403`) |

## Scope

- Team page inside the authenticated shell at `/team`, replacing the `team`
  Coming soon destination of the sidebar **Team** item.
- Header with back link, title, description, **Manage skills** and **Add
  technician profile** actions and the note "Sign-in access and permissions
  are managed in Settings."
- Five metrics: Active profiles, Available now, On jobs, At capacity,
  Unlinked accounts.
- Tabs **Team members (N)**, **Availability**, **Skills** (the latter two link
  to Coming soon); **Today / This week** period toggle.
- Search, Branch / Skill / Current status / Account link filters, name sort
  and pagination.
- Derived today's status, jobs, workload, at-capacity and next-available
  values from availability, breaks, exceptions and visit assignments.
- Capacity and unlinked-account alerts; skill coverage summary.
- Detail drawer faithful to the design; create/edit drawer; activate and
  deactivate with the upcoming-visits guard.
- Link an eligible existing member account to a profile and unlink it; no
  role, permission, membership or branch-access change.
- Technician own-profile read-only view.
- Branch scoping, tenant isolation, audit rows.
- Loading, empty, filtered-empty, error, forbidden, read-only, submitting and
  responsive states.
- Schema amendments (BR-22) and migration generation, never application.

## Non-goals

- Editing skills, proficiency, primary skill, weekly availability, breaks or
  exceptions and the skill catalog (Design 23). Their buttons and the
  Availability/Skills tabs navigate to Coming soon.
- Auto-assignment, assignment or reassignment of visits, ZIP/service-area
  coverage, payroll, time tracking and GPS tracking.
- Creating or inviting users, changing roles, permissions, membership status or
  branch access (Design 18).
- Creating `TechnicianProfile` records from users or users from profiles;
  mixing profile and user data (profile email/phone stay independent of the
  user's).
- Bulk selection and bulk actions (the mockup's row checkboxes are omitted).
- Sorting by columns other than Technician.
- Profile color (`color_hex`) editing; physical deletion of profiles.
- The top-bar "All branches" selector and any other shell change; only the
  page's Branch filter applies.
- Changing Design 18 links that point to the `team` Coming soon page.
- Applying migrations.

## User flow

1. A permitted user opens **Team**; the system shows metrics, the Team members
   tab in **Today** mode and the first page sorted by name A–Z, alerts and
   skill coverage.
2. The user searches, filters, pages, toggles Today / This week or sorts by
   name; rows, footer and period-dependent values update.
3. The user selects a technician name or **View**; the detail drawer opens.
4. An Owner or Operations Manager selects **Add technician profile** or
   **Edit profile**, completes the form and saves; the list, metrics and
   drawer refresh.
5. They select **Link account**, search eligible members, pick one and
   confirm; the profile shows Linked. **Unlink account** with confirmation
   removes the link.
6. They select **Deactivate** / **Activate** and confirm; deactivation is
   refused while upcoming visits are assigned.
7. A Technician opens **Team** and sees their own profile read-only.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/team` inside the shell; the sidebar **Team** item links to it and is active on it. |
| FR-02 | `GET /team/metrics` must return the BR-08 metrics and BR-14 alert data for the caller's scope, optional branch and period, and the page must render five metric cards and the alerts. |
| FR-03 | `GET /team/technicians` must return one page of profiles for search, filters, sort and period (BR-09–BR-11) plus the Team members count, and the page must render the BR-12 table, footer and paginator. |
| FR-04 | The system must derive each profile's today's status, jobs, workload, at-capacity flag and next available per BR-03–BR-07. |
| FR-05 | `GET /team/technicians/{id}` must return the BR-13 detail, and the drawer must render it per BR-13. |
| FR-06 | `POST /team/technicians` and `PUT /team/technicians/{id}` must create and update profiles validated per BR-15. |
| FR-07 | `POST /team/technicians/{id}/activate` and `/deactivate` must change `technician_profiles.status` per BR-17, refusing deactivation with upcoming assigned visits. |
| FR-08 | `GET /team/linkable-accounts`, `PUT` and `DELETE /team/technicians/{id}/account-link` must link and unlink an eligible member per BR-18 without changing roles, permissions, memberships or branch access. |
| FR-09 | `GET /team/skill-coverage` must return per-skill active-technician counts and the page must render them with BR-19 health tags. |
| FR-10 | `GET /team/me` must return the caller's own linked profile detail, and a Technician must see only that read-only view per BR-20. |
| FR-11 | Every endpoint must resolve the organization from the session and apply role and branch scope per BR-01 and BR-02. |
| FR-12 | The page must render loading, empty, filtered-empty, error, forbidden, read-only, submitting and responsive states per BR-21 and the UI table. |
| FR-13 | Profile mutations and link changes must write audit rows per BR-23. |
| FR-14 | The schema must be amended per BR-22 and a migration generated, never applied. |
| FR-15 | Design 23 entry points (Manage skills, Availability and Skills tabs, Edit skills, Edit availability) and View schedule must navigate to Coming soon per BR-24. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | Role policy: Read endpoints (`GET /team/metrics`, `/team/technicians`, `/team/technicians/{id}`, `/team/skill-coverage`, `/team/options`) allow `owner`, `operations_manager`, `dispatcher`. Mutations (`POST`/`PUT /team/technicians`, activate, deactivate, `PUT`/`DELETE` account-link) and `GET /team/linkable-accounts` allow `owner`, `operations_manager`. `GET /team/me` allows `technician` only. Any other role/endpoint combination → `403`. Frontend visibility is UX only. | Backend |
| BR-02 | Branch scope: `owner`, `operations_manager` and any member with `organization_users.is_all_branches` see all branches; `dispatcher` otherwise sees only branches in `organization_user_branches`. A profile is visible when `technician_profiles.branch_id` is in scope; outside scope or another organization → `404`. Metrics, alerts, counts, coverage and lists include only in-scope profiles. A `branchId` query/body value not in the organization or out of scope → `400` `errors.branchId` "Choose a branch you have access to." | Backend |
| BR-03 | Time basis: each profile's "now", "today" (local midnight to midnight) and "this week" (Monday 00:00 to next Monday 00:00) use its home branch `timezone`, falling back to `organizations.timezone`. `day_of_week` uses 0 = Sunday … 6 = Saturday. The API returns instants as UTC ISO-8601 plus the applicable `timezone`; the page formats them in it ("h:mm a"; dates "EEE, MMM d"). | Backend / Frontend |
| BR-04 | Availability for a day = that day's `technician_weekly_availability` windows minus their `technician_breaks`, minus portions covered by exceptions with `is_available = false`, plus portions of exceptions with `is_available = true` not already in a window. Available minutes weight window minutes by `capacity_percent / 100`; exception-added minutes count at 100 %. | Backend |
| BR-05 | Today's status, first match wins: **Inactive** / **Suspended** (profile `status`); **Time off** (an `is_available = false` exception contains now); **On job** (an active assignment — `visit_assignments.unassigned_at IS NULL` — on a visit with status `on_the_way`, `in_progress` or `paused`); **Break** (now inside one of today's breaks); **Available** (now inside today's availability per BR-04); **Off**. Tones: On job blue, Available green, Break amber, Time off amber, Off/Inactive/Suspended grey. | Backend / Frontend |
| BR-06 | Period jobs and workload (period = Today or This week): counted visits have an active assignment, status not `unscheduled`/`cancelled`, and `scheduled_start` within the period. Jobs = count. Scheduled minutes = sum of their `scheduled_start`–`scheduled_end` durations clipped to the period. Workload % = round(scheduled ÷ available minutes × 100). Available = 0 with scheduled > 0 → "No availability"; both 0 → "—". **At capacity** when workload ≥ 100 % or "No availability"; the bar is red above 100 %, otherwise blue. Inactive/Suspended profiles show "—" for jobs, workload and next available and are never at capacity. | Backend / Frontend |
| BR-07 | Next available: "Now" when status is Available; otherwise the first minute ≥ now today inside availability (BR-04) not covered by an active-assignment visit (status not `unscheduled`/`cancelled`/`completed`/`approved`) nor a break; otherwise the start of the next availability window within the next 7 days shown with its date; otherwise "—". | Backend / Frontend |
| BR-08 | Metrics (in-scope, optional `branchId`, independent of search/other filters): Active profiles = `status = active`; Available now / On jobs = active profiles with that BR-05 status; At capacity = active profiles at capacity for the selected period; Unlinked accounts = active profiles with `organization_user_id IS NULL`. The Unlinked accounts card shows an attention dot when > 0. | Backend / Frontend |
| BR-09 | Search (trimmed, ≤100 chars, case-insensitive, contains) matches first/last/full name, email and profile ID; a term with ≥3 digits also matches phone digits. Filters: Branch (All branches or one in-scope branch; page-local; also scopes metrics, alerts and coverage); Skill (All skills or one active skill; profiles having it); Current status (All active — default, active profiles only — Available, On job, Break, Time off, Off, Inactive, Suspended); Account link (All, Linked, Not linked). Changing search, filter, period or sort returns to page 1; search debounced 300 ms client-side. | Both |
| BR-10 | Sort: Technician name A–Z (default) or Z–A by "first last", case-insensitive, `id` tie-breaker; the column header toggles it. Page size 10, 1-based; a page beyond the last returns empty `items` with correct `totalCount`. | Backend / Frontend |
| BR-11 | Tab label "Team members (N)": N = active profiles in scope and branch filter, independent of search and other filters. Footer "Showing x – y of n technicians" uses the filtered total. | Backend / Frontend |
| BR-12 | Table columns: Technician (link-styled, opens drawer), Home branch, Skills (active skill names, primary first then A–Z, comma-joined, truncated with ellipsis; "—" when none), Account link (Linked green / Not linked red), Today's status (BR-05 tag; shown for both periods), Jobs today / Jobs this week, Workload (percent plus bar), Next available, Actions menu. Menu for Owner/Operations Manager: **View**, **Edit**, **Link account** or **Unlink account**, **Deactivate** or **Activate**; Dispatcher: **View**. | Frontend |
| BR-13 | Detail drawer: full name, "Technician profile", profile status tag; **Profile information** (Email, Mobile phone, Profile ID, Home branch; "—" when empty); **User account link** — linked: "Linked to user account (read-only)", User email, Role (role name), note "Account permissions are managed in Settings. Linking a user does not change their permissions.", **View access in Settings** (`/admin/users`, Owner only) and **Unlink account** (mutators); unlinked: "No linked user account" and **Link account** (mutators); **Operational skills** (name, BR-25 level label, "Primary" badge) and **Edit skills**; **Standard availability** (consecutive days with identical windows grouped, e.g. "Monday – Friday 8:00 AM – 5:00 PM"; Saturday and Sunday both off → "Weekends Off"; other days without windows "Off"; "No availability set" when none) and **Edit availability**; **Today** (Status, Current job = the On job visit's "{work_order_prefix}-{work_order_number} – {first line of scope_snapshot}" or "None", Jobs today, Workload, Next available); footer **View schedule** and **Edit profile** (mutators). Notes are shown only in the edit drawer. | Backend / Frontend |
| BR-14 | Alerts (in-scope, branch filter, period): capacity alert for the active profile with the highest workload at capacity — "{Name} is at {N}% capacity today." / "this week." ("has no availability" wording when BR-06 "No availability"), "+N more" when more are at capacity, **View schedule**; unlinked alert for the first unlinked active profile by name — "{Name} has no linked user account.", "+N more", **Link account** (mutators; Dispatcher sees no action). An alert is hidden when its count is 0. | Backend / Frontend |
| BR-15 | Create/edit fields (trimmed; empty optional strings stored null) — see Field validation table. Server errors are `400` `errors.<field>` with the same messages; the drawer shows them under the fields. Create sets `status = active`, no link. Edit cannot change status, link, skills or availability. | Both |
| BR-16 | Uniqueness: Profile ID stored uppercase, unique per organization ("This profile ID is already in use."); email stored lowercase, unique per organization case-insensitively ("Another technician profile already uses this email."). Checked before save and enforced by constraints (BR-22); a constraint race returns the same `400`. | Backend |
| BR-17 | Status changes: **Deactivate** (from `active` or `suspended` → `inactive`) is refused with `409` "Reassign this technician's upcoming visits before deactivating the profile." when an active assignment exists on a visit with status `scheduled`, `assigned`, `on_the_way`, `in_progress` or `paused` and `scheduled_end` null or > now; checked in the same transaction as the update. **Activate** (from `inactive` or `suspended` → `active`). Same-status requests → `204` no-op without audit. Links, skills, availability and history are preserved. Both actions ask for confirmation ("Deactivate {Name}?" / "Activate {Name}?"). | Both |
| BR-18 | Linking: eligible accounts are the organization's `organization_users` with `status = active`, role `technician`, `dispatcher` or `operations_manager`, not linked to any profile; `GET /team/linkable-accounts` returns name, email and role, searchable, max 20. Linking a profile already linked, or an ineligible/foreign/already-linked membership → `409` "This user account can't be linked to this profile." (foreign membership is indistinguishable from ineligible). Unique index race → same `409`. Unlink (confirm "Unlink {Name}'s user account?") sets `organization_user_id` null; unlinking an unlinked profile → `204` no-op. Neither changes `organization_users`, roles, permissions, branch access or `users`. | Backend |
| BR-19 | Skill coverage (in-scope, branch filter): every active skill of the organization with the count of active profiles having it, ordered by count desc then name. Health: **Healthy** ≥ 5, **Watch** = 4, **Low coverage** ≤ 3. Empty catalog → "No skills defined yet." with **Manage skills**. | Backend / Frontend |
| BR-20 | Technician own profile: `GET /team/me` returns the BR-13 detail of the profile linked to the caller's membership regardless of branch scope; none linked → `404` and the page shows "Your team profile isn't linked yet. Contact your manager." The view shows only the drawer sections as a page, without metrics, list, alerts, coverage, actions, Settings link or edit buttons; **View schedule** remains. | Both |
| BR-21 | Role UX: Owner/Operations Manager see header actions and mutation menus; Dispatcher sees no Add/edit/link/status actions (drawer read-only; **Manage skills** still navigates to Coming soon); Technician sees BR-20; Accounting, Viewer and unknown roles see "You don't have access to Team." with no further data requests. Filter Branch and form Home branch list only `GET /team/options` branches. | Frontend |
| BR-22 | Schema amendment (approved by the user 2026-10-01): `CREATE UNIQUE INDEX ux_technician_profiles_org_user ON technician_profiles(organization_user_id) WHERE organization_user_id IS NOT NULL`; `CREATE UNIQUE INDEX ux_technician_profiles_org_email ON technician_profiles(organization_id, lower(email)) WHERE email IS NOT NULL`; `ALTER TABLE technician_skills ADD COLUMN is_primary boolean NOT NULL DEFAULT false`; `CREATE UNIQUE INDEX ux_technician_skills_primary ON technician_skills(technician_id) WHERE is_primary`. `docs/database/fieldops-schema.sql` is updated accordingly. Migration generated with `--generate-migration`, never applied; it fails rather than deduplicating if conflicting rows exist. | Backend |
| BR-23 | Audit (`entity_type` `technician_profile`, `actor_user_id` caller, `branch_id` profile branch): `technician_profile.created` (after `{ branchId }`); `.updated` (metadata `{ changedFields }` names only; no-op → no row); `.activated` / `.deactivated` (before/after `{ status }`); `.account_linked` / `.account_unlinked` (metadata `{ organizationUserId }`). Email, phone and notes are never written to audit rows or logs. | Backend |
| BR-24 | Navigation: **Manage skills**, **Skills** tab and **Edit skills** → `/coming-soon/team-skills` ("Team skills"); **Availability** tab and **Edit availability** → `/coming-soon/team-availability` ("Team availability"); **View schedule** → `/coming-soon/schedule`. | Frontend |
| BR-25 | Proficiency labels: 1 Beginner, 2 Basic, 3 Intermediate, 4 Advanced, 5 Expert; null → no level. "Primary" badge when `technician_skills.is_primary`. Only active skills are shown. | Frontend |
| BR-26 | Unsaved changes: closing a dirty form drawer asks "Discard unsaved changes?" (shared discard dialog). While saving, the button shows loading, fields are disabled and double submission is prevented. | Frontend |

### Field validation

| Field | Required | Rules | Message |
| ----- | -------- | ----- | ------- |
| First name | Yes | ≤100 | "Enter a first name." / "Use 100 characters or fewer." |
| Last name | Yes | ≤100 | "Enter a last name." / "Use 100 characters or fewer." |
| Email | No | ≤254, valid email, BR-16 unique | "Enter a valid email address." |
| Mobile phone | No | ≤40; digits, spaces, `+ ( ) - .`; 7–15 digits | "Enter a valid phone number." |
| Profile ID | No | ≤50; letters, digits, `-`, `_`; BR-16 unique | "Use letters, numbers, hyphens or underscores." |
| Home branch | Yes | Active branch in scope (BR-02); an unchanged inactive branch is kept on edit | "Choose a branch you have access to." |
| Notes | No | ≤2000 | "Use 2000 characters or fewer." |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| — | active | Create | Owner, Operations Manager | BR-15, BR-16 |
| active, suspended | inactive | Deactivate | Owner, Operations Manager | No upcoming assigned visits (BR-17) |
| inactive, suspended | active | Activate | Owner, Operations Manager | — |
| unlinked | linked | Link account | Owner, Operations Manager | Eligible membership (BR-18) |
| linked | unlinked | Unlink account | Owner, Operations Manager | — |

## Data and persistence impact

- Tables used: `technician_profiles` (all columns except `color_hex` writes),
  `technician_skills`, `skills`, `technician_weekly_availability`,
  `technician_breaks`, `technician_exceptions`, `visit_assignments`, `visits`,
  `work_orders` (`work_order_number`, `scope_snapshot`), `organizations`
  (`timezone`, `work_order_prefix`), `branches`, `organization_users`,
  `organization_user_branches`, `roles`, `users` (name, email — read only),
  `audit_logs`.
- Writes: `technician_profiles` and `audit_logs` only.
- Schema amendments required: BR-22 (approved 2026-10-01; migration generation
  authorized, application forbidden).

## Tenant isolation and authorization

- Organization context: resolved server-side from the authenticated session's
  active membership.
- Client-provided organization identifiers: never accepted. `branchId`,
  `skillId`, profile ids and `organizationUserId` are verified against the
  caller's organization and branch scope.
- Profile of another organization or out of branch scope: `404`, no change.
  Branch of another organization/out of scope: `400` `errors.branchId`. Skill
  of another organization: `400` `errors.skillId`. Membership of another
  organization: `409` per BR-18.
- `GET /team/me` resolves only the caller's own membership.
- Permissions per action: BR-01; branch scope: BR-02.

## API contracts

Contract status: Final. All endpoints require an authenticated session, the
existing CSRF protection on unsafe methods, and return `401` when
unauthenticated. Validation errors use the existing `400` problem shape with
`errors.<field>`. `period` is `today` (default) or `week`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/team/options` | — | `200 { branches: [{ id, name }], skills: [{ id, name }] }` — active in-scope branches and active skills, by name | `403` | Read |
| GET | `/team/metrics` | Query `branchId?`, `period?` | `200 { activeProfiles, availableNow, onJobs, atCapacity, unlinkedAccounts, capacityAlert: { technicianId, name, workloadPercent?, noAvailability, moreCount } \| null, unlinkedAlert: { technicianId, name, moreCount } \| null }` | `400`, `403` | Read |
| GET | `/team/technicians` | Query `search?`, `branchId?`, `skillId?`, `status?` (`all_active`\|`available`\|`on_job`\|`break`\|`time_off`\|`off`\|`inactive`\|`suspended`), `accountLink?` (`all`\|`linked`\|`not_linked`), `sort?` (`name_asc`\|`name_desc`), `period?`, `page?` | `200 { items: [{ id, fullName, branchName, skills: [string], isLinked, profileStatus, todayStatus, jobs, workloadPercent?, workloadState: "percent"\|"no_availability"\|"none", atCapacity, nextAvailable: { kind: "now"\|"time"\|"none", at? }, timezone }], totalCount, teamMembersCount, page, pageSize: 10 }` | `400` invalid query, `403` | Read |
| GET | `/team/technicians/{id}` | Query `period?` | `200 { id, firstName, lastName, email?, phone?, employeeCode?, notes?, branch: { id, name }, profileStatus, account: { email, roleName } \| null, skills: [{ name, proficiency?, isPrimary }], availability: [{ dayOfWeek, windows: [{ start, end }] }], today: { todayStatus, currentJob: { label } \| null, jobs, workloadPercent?, workloadState, nextAvailable }, timezone }` (`notes` only for mutators) | `403`, `404` | Read |
| POST | `/team/technicians` | `{ firstName, lastName, email?, phone?, employeeCode?, branchId, notes? }` | `201 { id }` + `Location` | `400`, `403` | Mutate |
| PUT | `/team/technicians/{id}` | Same as POST | `200` (detail shape) | `400`, `403`, `404` | Mutate |
| POST | `/team/technicians/{id}/activate` | — | `204` | `403`, `404` | Mutate |
| POST | `/team/technicians/{id}/deactivate` | — | `204` | `403`, `404`, `409 { upcomingVisitCount }` | Mutate |
| GET | `/team/linkable-accounts` | Query `search?` | `200 [{ organizationUserId, fullName, email, roleName }]` (max 20, by name) | `400`, `403` | Mutate |
| PUT | `/team/technicians/{id}/account-link` | `{ organizationUserId }` | `204` | `403`, `404`, `409` | Mutate |
| DELETE | `/team/technicians/{id}/account-link` | — | `204` | `403`, `404` | Mutate |
| GET | `/team/skill-coverage` | Query `branchId?` | `200 [{ skillId, name, technicianCount, health: "healthy"\|"watch"\|"low" }]` | `400`, `403` | Read |
| GET | `/team/me` | — | `200` (detail shape without `notes`) | `403`, `404` | Technician |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Team page | Skeleton metrics, table rows and coverage | No profiles: "No technician profiles yet." + **Add technician profile** (mutators); filtered: "No technicians match your filters." + **Clear filters** | Section error with **Retry** per failed request | Forbidden state (BR-21); Dispatcher read-only | Metrics, table, alerts, coverage rendered |
| Detail drawer | Skeleton sections | Section fallbacks ("No skills yet", "No availability set") | Inline error + **Retry** | Read-only for Dispatcher; `404` → "This technician profile isn't available." | BR-13 content |
| Form drawer ("Add technician profile" / "Edit technician profile") | Branch options loading | — | Field errors; generic toast for other failures, form kept | Not rendered for read roles | Toast "Technician profile created." / "Technician profile updated."; list and metrics refresh |
| Link dialog | Searching indicator | "No eligible user accounts." | Inline `409` message | Mutators only | Toast "User account linked." / "User account unlinked." |
| Status confirm | Button loading | — | `409` message with upcoming visit count | Mutators only | Toast "Profile deactivated." / "Profile activated." |
| Technician own profile | Skeleton | BR-20 not-linked message | Error + **Retry** | — | Read-only sections |

- Mockups: `design/assets/22-design.png` (Approved). The form drawer follows
  the Customers drawer pattern (approved by the user, OD-14).
- Responsive: metrics wrap; below tablet width the table scrolls
  horizontally inside its card and the detail drawer is full width; filters
  stack. Workload bars carry a text percentage; status and link tags carry
  text, not color alone.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Duplicate email or Profile ID | `400` field message (BR-16) | None |
| Deactivate with upcoming assigned visits | `409` "Reassign this technician's upcoming visits before deactivating the profile." | None |
| Link ineligible, foreign or already-linked account | `409` "This user account can't be linked to this profile." | None |
| Profile out of scope / other organization | `404` "This technician profile isn't available." | None |
| Role not permitted | `403`; forbidden state or toast | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | An Owner signed in | They open **Team** in the sidebar | `/team` renders in the shell with the Design 22 header, note, five metrics, tabs, period toggle, filters, table, paginator, alerts and coverage; sidebar item active; no Coming soon page |
| AC-02 | Profiles with availability windows, breaks, exceptions and assigned visits in various states at a fixed clock (parameterized) | Metrics and list load for Today | Each row's today's status follows BR-05 precedence and the metrics follow BR-08 |
| AC-03 | Profiles with visits, windows, `capacity_percent` < 100, breaks and exceptions (parameterized, including no availability) | List loads for Today and This week | Jobs, workload %, "No availability"/"—" and at-capacity follow BR-04/BR-06 in the home-branch timezone with Monday–Sunday weeks; At capacity metric changes with the period |
| AC-04 | Available, on-job, fully booked and no-availability profiles | List loads | Next available is "Now", the first free time today, the next window with date, or "—" per BR-07 |
| AC-05 | Profiles across branches, skills, statuses and link states | Search, Branch, Skill, Current status, Account link and sort are combined and paged (parameterized) | Results, `totalCount`, `teamMembersCount` and page 10 follow BR-09–BR-11; Inactive/Suspended appear only under their status option; invalid query values → `400` |
| AC-06 | A Dispatcher assigned to branch A, and an Operations Manager | Each calls list, metrics, coverage, options and detail | The Dispatcher sees only branch A data and gets `404` for a branch B profile and `400` for `branchId` B; the Operations Manager sees all branches |
| AC-07 | A profile of organization B | A user of organization A requests its detail, edit, status changes or link | `404`, no data change; a membership of organization B in a link request → `409` |
| AC-08 | Each role | It calls every endpoint (parameterized) | Access follows BR-01: Accounting/Viewer `403` everywhere; Dispatcher `403` on mutations and linkable accounts; Technician `403` except `/team/me` |
| AC-09 | An Owner | They create a profile with valid fields | `201`; profile `active`, unlinked, email lowercased, Profile ID uppercased; toast; list and metrics refresh; `technician_profile.created` audit without email/phone |
| AC-10 | Invalid or duplicate field values, including an email differing only in case and a duplicate Profile ID (table-driven) | Create or edit is submitted | `400` with the Field validation messages; nothing saved; the drawer shows them under the fields |
| AC-11 | An existing profile | The Operations Manager edits fields and saves; then saves without changes | Values update; `.updated` audit lists changed field names only; the no-op writes no audit; status, link, skills and availability are unchanged |
| AC-12 | An active profile with an active assignment on a future `assigned` visit, and one without | Deactivate is confirmed | The first → `409` with the message and count, no change; the second becomes `inactive` with `.deactivated` audit; activate returns it to `active`; same-status calls are `204` no-ops |
| AC-13 | Active members with roles technician, dispatcher, operations_manager, accounting, a suspended technician and one already linked | Linkable accounts are listed and a link is attempted for each | Only active, unlinked technician/dispatcher/operations_manager members are listed and linkable; others → `409`; linking an already-linked profile → `409` |
| AC-14 | Two concurrent link requests for the same membership to different profiles | Both are processed | Exactly one succeeds; the other returns `409`; the BR-22 unique index holds |
| AC-15 | A linked profile | It is linked and later unlinked | The member's role, membership status, branch access and user record are unchanged before and after; audit rows `.account_linked` / `.account_unlinked` are written |
| AC-16 | A profile selected in the table | The drawer opens | Profile information, User account link (linked or unlinked variant), Operational skills with level labels and Primary badge, grouped Standard availability, Today section and footer render per BR-13/BR-25 |
| AC-17 | Profiles at and above capacity and unlinked active profiles | The page loads | Capacity and unlinked alerts show the BR-14 profile, wording for the period and "+N more"; each alert hides when its count is 0; **Link account** opens the link dialog for that profile |
| AC-18 | Active skills with 0, 3, 4 and 5+ active technicians and an inactive skill | Skill coverage loads | Counts include only active in-scope profiles, order by count then name, health tags follow BR-19; the inactive skill is absent; an empty catalog shows the empty state |
| AC-19 | A Technician with a linked profile, and one without | Each opens **Team** | The first sees only their own profile read-only (no metrics, list, alerts, coverage or actions); the second sees the not-linked message |
| AC-20 | Accounting and Viewer users | They open `/team` | The forbidden state renders with no further data requests |
| AC-21 | A Dispatcher | They open the page and a drawer | No Add/Edit/Link/Unlink/status actions; drawer read-only; Settings link hidden |
| AC-22 | The page as Owner | Requests are pending, fail, or return no rows | Skeletons, per-section error with **Retry**, and the empty or filtered-empty states render per the UI table |
| AC-23 | A dirty form drawer, and a submission in progress | The user closes it, and the user clicks save twice | The discard dialog appears; only one request is sent and controls are disabled while saving |
| AC-24 | Owner on the page and drawer | They select Manage skills, Availability/Skills tabs, Edit skills, Edit availability, View schedule | Each navigates to its BR-24 Coming soon destination |
| AC-25 | The generated migration | It is reviewed against BR-22 | It adds exactly the two `technician_profiles` unique indexes, `technician_skills.is_primary` and its partial unique index; the schema doc matches; the migration is not applied |

## Testing requirements

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Derivation calculator (status precedence, availability minutes with breaks/exceptions/capacity, workload, next available, week boundaries in a non-UTC timezone), table-driven | AC-02, AC-03, AC-04 |
| Backend integration | List/metrics/coverage with filters and paging; create/edit validation and uniqueness; deactivate guard; linking eligibility and race; link leaves membership untouched | AC-05, AC-09–AC-15, AC-17, AC-18 |
| Authorization/tenant isolation | Role matrix (parameterized), Dispatcher branch scope, one cross-organization denial for profile and membership, `/team/me` own-only | AC-06, AC-07, AC-08, AC-19 |
| Frontend component/service | Role-based UX and forbidden/technician views; drawer rendering of linked/unlinked and availability grouping; alerts; form submit/discard | AC-16, AC-17, AC-19–AC-23 |
| Persistence review | `database-reviewer` on mappings and migration | AC-25 |
| Manual visual QA (user) | Fidelity to Design 22, navigation, responsive | AC-01, AC-24 |

The backend unit count may reach the upper default because the derivation
calculator is the feature's main non-trivial logic.

## Dependencies

- Design 18 users and permissions (memberships, roles, `/admin/users`) —
  implemented.
- Invitation acceptance BR-11 auto-link by email — implemented; compatible
  with the lowercase email uniqueness.
- Shared discard dialog — implemented.
- Design 23 (skills/availability editing) — future; entry points go to
  Coming soon.
- Visits/work orders creation features — future; derivations read whatever
  exists (normally zero visits).

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | `day_of_week` follows 0 = Sunday, matching .NET `DayOfWeek`; Design 23 will write with the same convention. |
| AS-02 | Changing a profile's home branch is allowed regardless of assigned visits; assignment validation belongs to scheduling. |
| AS-03 | The back link "Team" above the title returns to `/overview`. |
| AS-04 | The existing `team` Coming soon slug remains for links that still use it. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Technician access | 403 / own profile read-only | No | Own profile read-only only (2026-10-01) |
| OD-02 | One profile per membership | Schema index / application only | No | Partial unique index + migration (2026-10-01) |
| OD-03 | Linkable accounts | HasTeamProfile roles / any active member | No | Active technician, dispatcher, operations_manager (2026-10-01) |
| OD-04 | Profile ID | Manual / generated | No | Manual, optional, unique per organization (2026-10-01) |
| OD-05 | Primary skill | Omit / derive / schema | No | Add `technician_skills.is_primary`, max one per technician; levels 1–5 labels (2026-10-01) |
| OD-06 | Today's status rules | — | No | Approved as BR-05 (2026-10-01) |
| OD-07 | At capacity threshold | ≥100 % / ≥90 % | No | ≥100 %; red bar above 100 % (2026-10-01) |
| OD-08 | Next available | — | No | Approved as BR-07 (2026-10-01) |
| OD-09 | Today / This week | Implement / omit | No | Implement; Monday–Sunday in branch timezone (2026-10-01) |
| OD-10 | Design 23 entry points | Coming soon / hide | No | Coming soon (2026-10-01) |
| OD-11 | Coverage thresholds | — | No | Healthy ≥5, Watch =4, Low ≤3 (2026-10-01) |
| OD-12 | Deactivate with upcoming visits | Warn / block | No | Block with 409 (2026-10-01) |
| OD-13 | Inactive profiles | Status filter / separate filter | No | Current status includes Inactive and Suspended (2026-10-01) |
| OD-14 | Form layout | Customers-style drawer / ui-designer | No | Customers-style drawer, no consultation (2026-10-01) |
| OD-15 | Alerts | — | No | Approved as BR-14 (2026-10-01) |
| OD-16 | Roles per action | — | No | Approved as BR-01/BR-02 (2026-10-01) |
| OD-17 | Profile email uniqueness | None / unique | No | Unique per organization, case-insensitive, validated and DB-enforced (2026-10-01) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02, AC-03, AC-17 |
| FR-03 | AC-05 |
| FR-04 | AC-02, AC-03, AC-04 |
| FR-05 | AC-16 |
| FR-06 | AC-09, AC-10, AC-11 |
| FR-07 | AC-12 |
| FR-08 | AC-13, AC-14, AC-15 |
| FR-09 | AC-18 |
| FR-10 | AC-19 |
| FR-11 | AC-06, AC-07, AC-08 |
| FR-12 | AC-20, AC-21, AC-22, AC-23 |
| FR-13 | AC-09, AC-11, AC-12, AC-15 |
| FR-14 | AC-14, AC-25 |
| FR-15 | AC-24 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-10-01 | — → DRAFT     | Created with user decisions OD-01–OD-17 |
| 2026-10-01 | DRAFT → APPROVED | Approved by user via /spec approve |
