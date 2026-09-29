# Design 18 — Users & permissions

| Field    | Value                                  |
| -------- | -------------------------------------- |
| Feature  | `design-18-users-and-permissions`      |
| Type     | Full-stack                             |
| Status   | APPROVED                               |
| Created  | 2026-09-28                             |
| Updated  | 2026-09-28                             |
| Approved | 2026-09-28                             |

## Context and objective

`/coming-soon/users-and-permissions` is a placeholder. This slice replaces it
with the complete Design 18 screen at `/admin/users`, inside the Design 17
shell: metrics, filterable user table (members and pending invitations),
read-only permission matrix, invite drawer, edit-access, resend/revoke and
suspend/reactivate. Every mutation is authorized server-side, tenant-scoped and
audited, and suspension ends continued authenticated use through the existing
per-request session revalidation. Invitation acceptance is out of scope.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | View page; invite; resend/revoke; edit access; suspend/reactivate | Suspend or downgrade the last active Owner; suspend own access; supply an organization identifier; reach another organization |
| Viewer (`viewer`) | Open the page read-only (list, metrics, matrix) | Any mutation (`403`); sees no Invite button or row actions |
| Other roles (`operations_manager`, `dispatcher`, `technician`, `accounting`, unknown) | Use the shell | See the Administration item; read or change anything here (`403`; forbidden state by URL) |
| Unauthenticated visitor | — | Any endpoint (`401`) |
| System | Resolve organization/user/membership from the session; hash invitation tokens; write audit rows | Trust client organization identifiers |

## Scope

- `/admin/users` in the authenticated shell with the Administration column
  (Users & permissions current), breadcrumb, header, Invite user button,
  read-only banner, four metric cards, Users section (info note, filters,
  table, paginator) and Permission matrix section with legend.
- Roles: Owner, Operations Manager, Dispatcher, Technician, Accounting, Viewer.
- Search; Role, Branch access and Account status filters; Clear filters;
  sortable User column; server-side paging.
- Members and pending invitations in one table; expired invitations keep the
  "Pending invitation" tag plus an "Expired" note.
- Invite drawer; Edit access in the same drawer; Resend and Revoke invitation;
  Suspend and Reactivate.
- Role summaries; matrix served by the API; branch-scoped and "All branches"
  access; optional "link operational team profile" flag stored on the
  invitation; invitation expiry of 3, 7 or 14 days.
- Last-Owner and self-suspension protection; audit rows.
- Invitation delivery port with a no-op development adapter.
- Schema amendment (BR-16) and migration.
- Loading, empty (filtered), error, partial-metrics, validation, forbidden,
  read-only, submitting and success states; desktop, tablet and mobile.

## Non-goals

- Invitation acceptance (`/auth/invitation`), password creation, creation of the
  `users` row, membership activation or creating/linking the
  `technician_profiles` row at acceptance (follow-up spec). No `users` row is
  created for an invitee.
- Real email transport (only the port and a no-op adapter).
- Custom roles or editing the matrix; "View if granted" grants (display only).
- Customer accounts; Team pages (links go to Coming soon); per-request activity
  tracking; photo avatars (initials only); changing a user's name or email;
  deleting users; multi-organization switching.
- Playwright/browser automation by agents; copying handoff HTML/JS.
- Applying the migration to any database.

## User flow

1. An Owner opens Administration → Users & permissions. Metrics, table and
   matrix load with skeletons meanwhile.
2. The Owner searches/filters/sorts/pages the table.
3. The Owner selects **Invite user**, completes the drawer and selects **Send
   invitation**; the drawer closes, a toast shows, the table and metrics refresh.
4. From a row the Owner selects **Edit access** (drawer in edit mode) or the
   overflow menu: Suspend access / Reactivate (members), Resend / Revoke
   (invitations), each with the confirmations of BR-12.
5. A suspended user's next request returns `401`; after Reactivate they can
   sign in again.
6. A Viewer sees the same page read-only.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The frontend must serve `/admin/users` (replacing the `users-and-permissions` Coming soon slug) inside the shell with the Administration column (current item Users & permissions), breadcrumb "Administration / Users & permissions", h1, description and the BR-13 layout and copy. |
| FR-02 | `GET /users` must return a page of the session organization's members (`organization_users` with status `active` or `suspended`) and open invitations (not accepted, not revoked), filtered, sorted and paged per BR-03 and BR-04. |
| FR-03 | `GET /users/summary` must return `activeUsers`, `pendingInvitations`, `suspendedUsers`, `owners` per BR-05, independent of list filters. |
| FR-04 | The Users section must provide search, Role, Branch access and Account status filters, Clear filters, the seven columns, paginator "Showing {from}–{to} of {total} users", and the loading, filtered-empty, error and partial-metrics states of BR-14. |
| FR-05 | `GET /permission-matrix` must return the six roles (code, name, summary, flags) and the nine modules with each role's level per BR-07; the page renders it with the legend and role summaries and contains no hardcoded matrix. |
| FR-06 | `POST /users/invitations` must create an invitation per BR-08 to BR-10, deliver it through the delivery port and audit it. |
| FR-07 | The Invite user drawer must follow Design 18: fields, branch checkbox rules, role summary, team-link switch, expiry select, Safety notes, Cancel / Send invitation, validation and submitting states per BR-13 and BR-14. |
| FR-08 | Resend and revoke endpoints must behave per BR-11. Resend replaces the token and expiry; revoke stops the link. |
| FR-09 | Each row must offer **Edit access** and an overflow menu (`aria-label` "More actions for {name}") whose items depend on status per BR-12, with the BR-12 confirmations and toasts and disabled items with tooltips. |
| FR-10 | `PUT /users/{id}/access` and `PUT /users/invitations/{id}/access` must change role and branch access per BR-09; the drawer reopens in edit mode ("Edit access", email and names read-only, no team-link or expiry controls, footer "Save access"). |
| FR-11 | `POST /users/{id}/suspend` and `/reactivate` must set `organization_users.status` to `suspended` / `active` without deleting data; a suspended member's next authenticated request must return `401` under the existing session contract. |
| FR-12 | The server must enforce BR-15: last active Owner protection and no self-suspension; the UI shows the corresponding items disabled with tooltips. |
| FR-13 | Every successful mutation must write exactly one audit row per BR-17; no-ops and failures write none. |
| FR-14 | Every endpoint must enforce BR-01 authorization and organization isolation per Tenant isolation and authorization. |
| FR-15 | Viewers see the page read-only; other roles see the forbidden state (BR-14). |
| FR-16 | The page and drawer must follow the responsive rules in UI behavior (1440 docked drawer, 1100–1439 overlay, full screen below 768, cards and 2×2 metrics on mobile, matrix horizontal scroll with sticky Module column). |
| FR-17 | The desktop rendering must match `18-design.png` and the handoff structure, tokens and copy (BR-13) using PrimeNG and existing shell foundations, with dynamic data only and the accessibility rules in UI behavior. |
| FR-18 | The schema amendment BR-16 must be applied to `docs/database/fieldops-schema.sql`, the EF model and one generated migration (never applied by agents). |

## Business and validation rules

Text is trimmed; whitespace-only counts as empty. Emails are trimmed and lowercased.

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Authorization policies: `GET /users`, `/users/summary`, `/permission-matrix` → `owner` or `viewer`; every other endpoint here → `owner`. Role read from the membership on every request. | Backend |
| BR-02 | Row status mapping: member `active` → "Active"; member `suspended` → "Suspended"; open invitation → "Pending invitation". Members with status `pending` or `disabled` are not listed or counted. An invitation is *expired* when `expires_at <= now`; it stays "Pending invitation" (`isExpired = true`), shows the note "Expired" beside the tag, and keeps Resend and Revoke. | Backend (flag) / Frontend (copy) |
| BR-03 | `GET /users` query: `search` (≤100; case-insensitive contains over first name, last name, "first last" and email), `roleCode` (one of the six codes), `branchId` (uuid; keeps rows with `isAllBranches` or that branch assigned), `status` (`active`, `suspended`, `pending_invitation`), `sort` (`name` default or `-name`; by "first last" then email), `page` ≥1 (default 1), `pageSize` fixed at 10 (any other value → `400`). Invalid `roleCode`/`status`/`sort`/`branchId` → `400` with that key. Filters combine with AND. | Backend |
| BR-04 | Row fields: `id` (membership id or invitation id), `kind` (`member` \| `invitation`), `firstName`, `lastName`, `email`, `roleCode`, `roleName`, `isAllBranches`, `branches [{id,name}]` (ordered by name; empty when all), `teamProfile { applicable, name }`, `status`, `isExpired`, `lastActiveAt`, `isCurrentUser`, `isLastOwner`. `teamProfile.applicable` is true for `technician`, `dispatcher`, `operations_manager`; `name` is the first/last name of the `technician_profiles` row whose `organization_user_id` is the membership (null otherwise, always null for invitations). UI text: not applicable → "Not applicable"; applicable without profile → "Not linked". `lastActiveAt` is `users.last_login_at` for members, null for invitations ("Never"). UI format: under 1 minute "Now", "{n} min ago", "{n} hr ago", "{n} days ago" up to 30 days, then a date. | Both |
| BR-05 | Summary: `activeUsers` = members `active`; `pendingInvitations` = open invitations including expired; `suspendedUsers` = members `suspended`; `owners` = members `active` with role `owner`. Always organization-wide. | Backend |
| BR-06 | Roles are the six canonical `roles` rows; no custom roles. Sort order everywhere: Owner, Operations Manager, Dispatcher, Technician, Accounting, Viewer. | Both |
| BR-07 | Matrix (columns in BR-06 order; levels: `full` Full, `edit` Edit, `view` View, `assigned_only` Assigned only, `completed_only` Completed only, `financial_only` Financial only, `own_profile` Own profile, `view_if_granted` View if granted, `none` None). Rows: Overview: Full, View, View, None, View, View if granted · Customers: Full, View, Edit, Assigned only, View, View if granted · Requests & quotes: Full, View, Edit, None, View, View if granted · Work orders & schedule: Full, Edit, Edit, Assigned only, Completed only, View if granted · Invoices & payments: Full, View, View, None, Edit, View if granted · Reports: Full, View, View, None, Financial only, View if granted · Team: Full, Edit, View, Own profile, None, None · Company settings: Full, None, None, None, None, None · Audit log: Full, View, None, None, Financial only, None. The catalog is defined in backend code, is read-only, display-only (enforcement stays on role-code policies) and returned by the API. Tone: Full/Edit teal, View info, None neutral, all others warning; the legend gives text so color is never the only signal. | Backend / Frontend |
| BR-08 | Invite body: `email` (required, RFC-lite, ≤254 — "Enter a valid email address."), `firstName`, `lastName` (required, ≤100 — "Enter a first name." / "Enter a last name."), `roleCode` (required, six codes — "Select a role."), `isAllBranches` (boolean), `branchIds` (uuids of **active** branches of the session organization; at least one unless all branches — "Select at least one branch."; unknown/other-organization/inactive id → `400` `branchIds` "Select a valid branch."), `linkTeamProfile` (boolean, default false; honored only for roles with `teamProfile.applicable`, otherwise stored false), `expiresInDays` (3, 7 or 14, default 7 — "Select a valid expiry."). Roles `owner` and `operations_manager` force `isAllBranches = true` and ignore `branchIds`. | Both |
| BR-09 | Edit access body: `roleCode`, `isAllBranches`, `branchIds` with the BR-08 rules (forced all-branches included). Applies to a member or an open invitation. An accepted or revoked invitation → `409` "This invitation is no longer pending.". No-op change → `200` without audit. Email, names, team-link flag and expiry are never changed here. | Backend |
| BR-10 | Duplicates: an email of an existing `active` or `suspended` member of the organization → `409` `errors.email` "This person already has access."; an open invitation (including expired) for the email in the organization → `409` `errors.email` "This email already has a pending invitation." (revoke or resend it). An email that belongs to a user of another organization is allowed. | Backend |
| BR-11 | Invitation token: 32 random bytes, URL-safe; only its SHA-256 hash is stored in `token_hash`; the raw token exists only in memory and in the delivery-port call and is never persisted, returned, logged or audited. Create/resend call `IInvitationDelivery` (recipient, first name, organization name, inviter name, role name, expiry, accept link) before commit; a delivery failure rolls back and returns `502` "We couldn't send the invitation. Try again.". The development adapter is a no-op. Resend (open invitation, expired or not): new token hash, `expires_at = now + 7 days`, old link invalid, same role/branches. Revoke: sets `revoked_at`; the row leaves the list and the pending count. Resend/revoke of an accepted/revoked invitation → `409` "This invitation is no longer pending.". Revoking twice is `409`. | Backend |
| BR-12 | Row menu by status: Active member → Suspend access; Suspended member → Reactivate; Pending invitation → Resend invitation, Revoke invitation. Suspend/Revoke ask confirmation: "Suspend {name}? They won't be able to sign in. Assigned work and audit history are preserved." → **Suspend** (danger) / Cancel; "Revoke the invitation for {email}? The link will stop working." → **Revoke** (danger) / Cancel. Resend and Reactivate need none. Edit access asks confirmation when access is reduced — role moves down the BR-06 order (Owner highest, Viewer lowest) or branches are removed or All branches is turned off: "Change access for {name}? Role: {old} → {new}. Branches: {old} → {new}." → **Change access** / Cancel. Toasts: "Invitation sent to {email}", "Invitation resent to {email}", "Invitation revoked", "Access suspended for {name}", "Access reactivated for {name}", "Access updated for {name}". Suspend on the last Owner, on the current user, or Edit access downgrade of the last Owner is disabled with tooltips "The last Owner can't be suspended or downgraded." / "You can't suspend your own access." | Frontend |
| BR-13 | Copy and layout adopted from the handoff (the handoff HTML and §3, §6 prevail over the screenshot: stacked sections, no tabs, no email toggle): description "Manage sign-in access, predefined roles, and branch visibility."; banner "You have view-only access. Inviting users and editing access requires the Owner role."; metric labels Active users, Pending invitations, Suspended, Owners; note "User access controls sign-in and permissions. Skills, workload, and availability are managed in Team." (Team → Coming soon `team`); filter labels Role / Branch access / Account status with "All roles" / "All branches" / "All statuses", placeholder "Search by name or email…", **Clear filters**; columns User, Role, Branch access, Linked team profile, Status, Last active, Actions; matrix heading "Permission matrix" with "Predefined roles. Access is also limited to each user's assigned branches."; legend "Full / Edit: create and change records", "View: read only", "Limited: scoped subset", "None: no access"; drawer title "Invite user" (400px), fields Email address, First name, Last name, Role, Branch access (branch checkboxes + "All branches"), "{Role} role summary", "Link operational team profile after acceptance", "Invitation expires in" (3 days, 7 days, 14 days), Safety notes ("The last Owner cannot be suspended or downgraded." / "Suspending access preserves work orders and audit history." / "Viewers only see modules explicitly granted. Company settings and audit log stay unavailable."), **Cancel** / **Send invitation**. Role summaries (Dispatcher verbatim from the handoff, others authored on the BR-07 matrix): Owner "Full access to every module, company settings, users and the audit log."; Operations Manager "Can view most modules and edit work orders, scheduling and team in all branches. Cannot change company settings."; Dispatcher "Can manage customers, requests, quotes, work orders, scheduling, and technician assignment in assigned branches. Cannot change company settings or view the audit log."; Technician "Can see assigned work orders and visits and their own team profile. No access to invoices, reports or company settings."; Accounting "Can manage invoices and payments and view completed work and financial reports. Cannot schedule work or change company settings."; Viewer "Read-only access to modules explicitly granted. Company settings and audit log stay unavailable." | Frontend (summaries also served by API, BR-07) |
| BR-14 | States: loading — six skeleton rows and metric skeletons; filtered-empty — "No users match these filters." + **Clear filters**; error — `p-message` "We couldn't load users. Check your connection and try again." + **Retry** replacing the table; metrics failure with table loaded → each metric "—" with tooltip "Couldn't load"; matrix failure → inline error with Retry; forbidden — "You don't have access to users and permissions." inside the shell; validation on blur after first interaction and on submit; submitting — spinner, drawer locked; a `409`/`400` server message maps to its field (`email`, `branchIds`) or, without a field, to a toast/inline message keeping input. | Frontend |
| BR-15 | Safety: the last **active** Owner of the organization cannot be suspended or moved to another role (`409` "The last Owner can't be suspended or downgraded."); the current member cannot suspend themselves (`409` "You can't suspend your own access."). Checked in the same transaction as the change with the organization's Owner memberships locked, so concurrent requests cannot leave zero Owners. Multiple Owners are allowed. Suspend of an already suspended member and reactivate of an active member → `204` no-op without audit. Data (memberships, branch rows, technician profiles, audit history) is never deleted by suspend. | Backend |
| BR-16 | Schema amendment (approved by the user 2026-09-28): `user_invitations` + `first_name varchar(100) NOT NULL`, `last_name varchar(100) NOT NULL`, `is_all_branches boolean NOT NULL DEFAULT false`, `link_team_profile boolean NOT NULL DEFAULT false`, `CHECK (expires_at > created_at)`, and `CREATE UNIQUE INDEX ux_user_invitations_open_email ON user_invitations(organization_id, email) WHERE accepted_at IS NULL AND revoked_at IS NULL`. Emails are stored lowercased. Existing rows (none expected) are backfilled with empty names by the migration. No other table changes. | Backend |
| BR-17 | Audit (`entity_type`, `entity_id`): `user.invited` (`user_invitation`, before null, after `{ roleCode, isAllBranches, branchIds, linkTeamProfile, expiresAt }`); `user.invitation_resent` (`user_invitation`, after `{ expiresAt }`); `user.invitation_revoked` (`user_invitation`); `user.access_updated` (`organization_user` or `user_invitation`, before/after `{ roleCode, isAllBranches, branchIds }`); `user.suspended` / `user.reactivated` (`organization_user`, before/after `{ status }`). `actor_user_id` is the caller; `branch_id` null. Tokens and hashes are never audited; emails are never logged. | Backend |
| BR-18 | Session invalidation relies on the existing per-request revalidation of the membership status (`docs/authentication.md`); no session table or new mechanism is added. Suspending affects only the membership of the session organization. | Backend |
| BR-19 | Client-side limits mirror BR-08. Role change in the invite drawer updates the role summary, forces All branches (checked, checkboxes disabled) for Owner and Operations Manager, and shows the team-link switch only for `teamProfile.applicable` roles. "All branches" checks every branch; unchecking any branch unchecks it; checking all branches individually checks it. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Member `active` | `suspended` | Suspend | Owner | Not last Owner, not self (BR-15) |
| Member `suspended` | `active` | Reactivate | Owner | — |
| (none) | Invitation open | Invite | Owner | BR-08, BR-10 |
| Invitation open | open (new token/expiry) | Resend | Owner | Not accepted/revoked |
| Invitation open | revoked | Revoke | Owner | Not accepted/revoked |
| Invitation open, not expired | expired | Time passes (`expires_at`) | System | Display state only; no write |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  `organization_users` (`id`, `organization_id`, `user_id`, `role_id`, `status`, `is_all_branches`, `updated_at`), `organization_user_branches`, `users` (`first_name`, `last_name`, `email`, `last_login_at`), `roles`, `branches` (`id`, `organization_id`, `name`, `is_active`), `user_invitations`, `invitation_branches`, `technician_profiles` (`organization_user_id`, `first_name`, `last_name`), `organizations` (`name`, for the delivery port), `audit_logs` (insert).
- Schema amendments required: BR-16 — **approved by the user 2026-09-28** (OD-03). Implementation updates `docs/database/fieldops-schema.sql`, the EF model and generates one migration under `--generate-migration`; agents never apply it. Persistence changes require `database-reviewer`.
- `permissions` and `role_permissions` are not used; the matrix is a code catalog (OD-04).
- Domain changes: `UserInvitation` gains names, `IsAllBranches`, `LinkTeamProfile`, resend, revoke and access update; `OrganizationUser` gains suspend, reactivate and access update with the BR-15 guards.

## Tenant isolation and authorization

- Organization context: session `OrganizationId`, `UserId`, `MembershipId`; role read from the membership on every request.
- Client-provided organization identifiers: none accepted anywhere. Membership, invitation and branch ids are always resolved against the session `OrganizationId`.
- A membership or invitation of another organization on any `{id}` route: `404`, no change. `branchIds` naming another organization's branch: `400` `branchIds` "Select a valid branch.". Lists, summary and matrix never include other organizations' rows.
- Permissions: BR-01 (Viewer reads only; all mutations Owner only). Frontend visibility (hidden Administration item, hidden buttons, disabled menu items) is UX only.
- Sensitive data: the invitation token/hash never appears in responses, logs, audit rows or the UI; emails are not logged.
- CSRF: existing `SameSite=Strict` session cookie, JSON only, CORS allow-list.

## API contracts

Contract status: Final. All authenticated responses carry `Cache-Control: no-store`. Row = BR-04.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| GET | `/users` | query BR-03 | `200 { items: Row[], page, pageSize, totalCount }` | `400` BR-03 keys · `401` · `403` | Owner, Viewer |
| GET | `/users/summary` | — | `200 { activeUsers, pendingInvitations, suspendedUsers, owners }` | `401`, `403` | Owner, Viewer |
| GET | `/permission-matrix` | — | `200 { roles: [{ code, name, summary, forcesAllBranches, hasTeamProfile }], modules: [{ key, name, levels: { <roleCode>: { level, label } } }] }` | `401`, `403` | Owner, Viewer |
| POST | `/users/invitations` | BR-08 body | `201 Row` (`kind = invitation`) | `400` field keys · `409` `errors.email` · `502` delivery · `401` · `403` | Owner |
| POST | `/users/invitations/{id}/resend` | none | `200 Row` | `404` · `409` not pending · `502` · `401` · `403` | Owner |
| POST | `/users/invitations/{id}/revoke` | none | `204` | `404` · `409` not pending · `401` · `403` | Owner |
| PUT | `/users/invitations/{id}/access` | BR-09 body | `200 Row` | `400` · `404` · `409` not pending · `401` · `403` | Owner |
| PUT | `/users/{id}/access` | BR-09 body | `200 Row` | `400` · `404` · `409` last Owner · `401` · `403` | Owner |
| POST | `/users/{id}/suspend` | none | `204` | `404` · `409` last Owner / self · `401` · `403` | Owner |
| POST | `/users/{id}/reactivate` | none | `204` | `404` · `401` · `403` | Owner |

- Branch options for the filter and drawer reuse the existing `GET /branches` (active branches for the drawer).
- Validation errors use the existing ProblemDetails `errors` shape with the keys of BR-08.

## UI behavior and states

Design references (approved; the handoff is the definitive target and its §6 corrections prevail over the screenshot):

- `design/identity-access/handoff/Users and Permissions.dc.html` (with `support.js`, `image-slot.js`; rebuilt in Angular/PrimeNG, never copied)
- `design/identity-access/handoff/Identity-and-Organization-Handoff.md` §3, §4, §5, §6, §8
- `design/identity-access/screens/18-design.png` (secondary visual target; screenshot-only elements — tabs, "Send invitation email", Dashboard/Jobs/Billing nav labels, sample data — are not implemented)

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Users page | Skeleton rows and metrics | Filtered-empty + Clear filters (initial empty impossible) | Table error + Retry; metrics "—"; matrix inline error | Viewer read-only banner and "View only"; others forbidden state | BR-12 toasts, table and metrics refreshed |
| Invite / Edit drawer | Submit spinner, drawer locked | N/A | Field messages; unmapped errors as inline message, input kept | Not reachable for Viewer | Drawer closes, toast |
| Confirm dialogs | Confirm button spinner | N/A | Message toast, dialog closes, state unchanged | Owner only | Toast |
| Permission matrix | Skeleton | N/A | Inline error + Retry | Owner, Viewer | Rendered from API |

- Responsive (Design 17 breakpoints): ≥1440px docked 400px drawer, no mask or focus trap; 1100–1439px overlay below the top bar with mask (click closes, dirty guard); 768–1099px rail sidebar, Administration link row, overlay drawer; below 768px sidebar drawer, metrics 2×2, users as cards (avatar initials, name, email, role tag, status, Edit access and overflow menu), matrix scrolls horizontally with sticky Module column, full-screen drawer with sticky footer; no horizontal page scroll at 320px; touch targets ≥44px on mobile.
- Accessibility: metric cards icon + label + number; status tags with icon and text; overflow `aria-label` "More actions for {name}"; matrix `th scope="col"` roles and `th scope="row"` modules; branch checkboxes with `label for`; switches with accessible names; overlay/full-screen drawer is a modal dialog trapping focus and returning it to the originating row; docked drawer is a labelled non-modal region, Esc closes it (dirty guard); errors linked with `aria-describedby`; confirmation dialogs focus the safe action.
- Styling: existing `--fo-*` tokens, PrimeIcons, Inter and shell; PrimeNG `p-table` (lazy), `p-select`, `p-checkbox`, `p-toggleswitch`, `p-tag`, `p-avatar`, `p-drawer`, `p-menu`, `p-confirmdialog`, `p-toast`, `p-skeleton`, `p-message`; metric and section cards are plain semantic markup. No new package or visual abstraction unless required.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Invite/edit validation failure | `400` field keys and BR-08 messages | None |
| Email already a member / open invitation | `409` `errors.email` BR-10 message under Email | None |
| Invitation not pending | `409` "This invitation is no longer pending."; toast, list refreshes | None |
| Last Owner / self suspension | `409` messages of BR-15; toast; items normally disabled | None |
| Delivery failure | `502` "We couldn't send the invitation. Try again."; drawer stays open | None |
| Membership/invitation of another organization or unknown | `404`; list refreshes with a toast "This user is no longer available." | None |
| `401` | Existing session-cleared redirect | None |
| `403` on mutation | `ApiError.message`; input kept | None |
| List load failure | BR-14 error state with Retry | None |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | A signed-in Owner at 1440px | `/admin/users` renders | The shell, Administration column with Users & permissions current, breadcrumb, h1, description, Invite user, four metrics, Users section (note, filters, seven-column table, paginator) and Permission matrix section appear per BR-13 with real data; the Coming soon slug no longer applies |
| AC-02 | Members (active, suspended, pending/disabled) and open, expired, revoked and accepted invitations across two organizations | `GET /users` with search, `roleCode`, `branchId`, `status`, sort and paging (parameterized) | Only the session organization's active/suspended members and open invitations are returned with BR-04 fields (team profile, last active, expired flag, last-Owner/current-user flags); filters combine; the branch filter keeps all-branches rows; sort, `pageSize = 10` and `totalCount` are correct; invalid query values → `400` with their key |
| AC-03 | The same data | `GET /users/summary` | Counts follow BR-05 (expired invitations count as pending, revoked/accepted excluded, only active Owners), are unaffected by list filters and exclude other organizations |
| AC-04 | An Owner on the page | They search, pick filters, clear them, hit no matches, then simulate list failure and metrics failure (parameterized) | Requests carry the filters and paging; Clear filters resets; "No users match these filters." with Clear filters; loading skeletons; error + Retry replaces the table; failed metrics show "—" with "Couldn't load" while the table stays |
| AC-05 | An Owner and a Viewer | The matrix loads | The API returns the BR-07 catalog (six roles with summaries, nine modules) and the UI renders exactly those cells with tones and the legend, `th` scopes and no hardcoded values; a failure shows the inline error with Retry |
| AC-06 | An Owner and a valid invite body | `POST /users/invitations` | `201` row `kind = invitation`, status "Pending invitation"; names, role, branches, flags and `expires_at` (default 7 days; 3, 14 accepted) persisted; email lowercased; only the token hash stored; the delivery port called once with the raw token; one `user.invited` audit row; no token/hash/email in logs, responses or audit; a delivery failure returns `502` and stores nothing |
| AC-07 | Bodies failing one rule each: bad email, empty names, unknown role, no branch, foreign/inactive branch, bad expiry, existing member, existing open invitation (parameterized); plus `owner`/`operations_manager` with branch ids; `linkTeamProfile` for an Accounting role | `POST /users/invitations` | Failures return `400`/`409` with their key and BR-08/BR-10 message and no change; forced roles store `is_all_branches = true` and no branch rows; `linkTeamProfile` is stored false for non-applicable roles |
| AC-08 | An Owner opens Invite user | They change role, toggle branches, reveal/hide the team-link switch, submit invalid then valid data, and the server rejects the email | Drawer fields, copy, role summary and branch rules match BR-13/BR-19; validation runs on blur and submit with BR-08 messages; submitting locks the drawer with a spinner; success closes it, shows "Invitation sent to {email}" and refreshes table and Pending invitations; a `409` shows the message under Email with input kept |
| AC-09 | Open, expired, accepted and revoked invitations | Resend and revoke each (parameterized) | Resend on open/expired: `200`, new token hash, `expires_at ≈ now + 7 days`, old token hash no longer stored, one audit row, delivery called once; revoke: `204`, `revoked_at` set, row and pending count drop, one audit row; accepted/revoked invitations → `409` "This invitation is no longer pending." with no change |
| AC-10 | Rows of each status, including an expired invitation | The Owner opens the overflow menus and confirms or cancels | Menus follow BR-12 (Active: Suspend; Suspended: Reactivate; Pending: Resend, Revoke); expired rows show the "Expired" note and keep both items; Suspend and Revoke confirm with BR-12 copy and Cancel sends nothing; toasts match; the table and metrics refresh |
| AC-11 | A member and an open invitation | `PUT` access with new role/branches, an all-branches switch, forced roles and invalid bodies (parameterized) | `200` row with normalized access persisted for both kinds; forced roles become all-branches; no-op returns `200` without audit; otherwise one `user.access_updated` row with before/after; invalid → `400`; accepted/revoked invitation → `409`; email, names and flags unchanged |
| AC-12 | An Owner selects Edit access on a member and on a pending invitation | They change role/branches, reduce access, cancel and save | The drawer titled "Edit access" shows email and names read-only, no team-link or expiry controls and footer "Save access"; a reduction opens the BR-12 confirmation with old and new values; Cancel keeps the drawer; save shows "Access updated for {name}" and refreshes; closing a dirty drawer asks to discard |
| AC-13 | An active member with a live session and a second member | An Owner suspends them, the suspended user sends any authenticated request, then an Owner reactivates and the user signs in again | Suspend: `204`, status `suspended`, one `user.suspended` row, no rows deleted; the user's next request returns `401` and clears the cookie; reactivate: `204`, one `user.reactivated` row, sign-in works again; repeating either returns `204` without audit; metrics change accordingly |
| AC-14 | The only Owner, a second Owner, and the current user | Suspend or downgrade the last Owner, suspend self, then downgrade one of two Owners, and two concurrent downgrades of two Owners (parameterized) | Last Owner suspend/downgrade → `409` BR-15 message; self-suspend → `409`; with two Owners one change succeeds and the concurrent pair never leaves zero active Owners; the UI shows the items disabled with the BR-12 tooltips |
| AC-15 | Sessions for `owner`, `viewer`, `dispatcher`, `technician`, `accounting`, `operations_manager` and no session (parameterized) | Each endpoint of the API contracts is called | Owner: allowed; Viewer: the three GETs allowed, every mutation `403` with no change; other roles `403` everywhere; no session `401` |
| AC-16 | Owners of organizations A and B | A reads lists, summary and matrix; A calls every `{id}` route with B's membership/invitation id; A invites with B's branch id; B repeats reads | A never sees B's rows or counts; every `{id}` call returns `404` with B unchanged; the foreign branch id returns `400` `branchIds`; B sees only its own data |
| AC-17 | A Viewer and a dispatcher at `/admin/users` | The page renders | Viewer: banner, no Invite button, Actions show "View only", no drawer or menus, matrix visible; dispatcher: the forbidden state inside the shell with no data requests beyond the rejected one; Administration item hidden per the existing shell rule |
| AC-18 | An Owner opens the drawer at 1440, 1280 and 390px (parameterized) | The drawer renders | 1440: docked 400px, no mask or trap, content reflows; 1280: overlay below the top bar with mask, focus trapped, mask click closes via the dirty guard; 390: full screen with sticky footer; focus returns to the originating control |
| AC-19 | Viewports 390, 800 and 1280px (parameterized), and 320px | The page renders | 390: sidebar drawer, metrics 2×2, user cards with initials, name, email, role tag, status and menu, matrix horizontal scroll with sticky Module column; 800: rail sidebar and Administration link row; 1280: full sidebar and column; no horizontal page scroll at 320px; touch targets ≥44px on mobile |
| AC-20 | The implementation and `18-design.png`/handoff at 1440×900 with the drawer open | They are compared (USER VISUAL QA REPORT) | Structure, dimensions (sidebar 256, top bar 64, Administration 224, drawer 400), tokens, table columns, status/role/matrix tags, cards, filters and drawer sections match within ±2px; all fixed copy matches BR-13; only data differs and is real; tabs and email toggle are absent by decision |
| AC-21 | The page, drawer and dialogs | A keyboard pass and accessibility review run | Controls are reachable with visible focus; labels, `aria-label`s, `th` scopes, dialog focus/return and status icons follow UI behavior; color is never the only signal |
| AC-22 | An empty test database and the generated migration | The migration applies in the test container and invitations are created | The BR-16 columns, check and partial unique index exist per the amended schema doc; a second open invitation for the same organization and email violates the index; the EF model matches the amended schema (database-reviewer) |

## Testing requirements

22 active ACs. TARGETED Full-stack. Reuse `FieldOpsApiFactory`, existing auth/session and frontend test helpers. Agents run no browser automation; AC-19 to AC-21 rely on the USER VISUAL QA REPORT.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | Last-Owner/self guards and role-reduction rule; invitation token hashing/expiry rules. ≤2 methods | FR-06, FR-12, AC-14 |
| Backend integration | (1) List/summary with filters, sort, paging and expired handling; (2) invite create + validation table + duplicates + delivery failure rollback; (3) resend/revoke/state conflicts; (4) edit access for member and invitation incl. forced roles; (5) suspend/reactivate incl. audit, no-op and 401 on the suspended session; (6) last-Owner rules incl. concurrency; (7) matrix catalog; (8) migration/index. ≤8 methods | AC-02, AC-03, AC-05 to AC-07, AC-09, AC-11, AC-13, AC-14, AC-22 |
| Authorization/tenant isolation | Parameterized role/no-session matrix over all endpoints incl. Viewer `403`; representative cross-organization `{id}` `404`, foreign branch `400` and list isolation | AC-15, AC-16 |
| Frontend component/service | (1) Page load, metrics, table columns, filters, paging, states incl. partial failure; (2) matrix render and role summaries; (3) invite drawer rules and validation, server error mapping; (4) row menus, confirmations, disabled last-Owner/self items, expired note; (5) edit-access mode and reduction confirmation; (6) Viewer read-only and forbidden. ≤6 methods | AC-01, AC-04, AC-05, AC-08, AC-10, AC-12, AC-14 (UI), AC-17 |
| Manual (USER VISUAL QA REPORT) | Desktop side-by-side at 1440×900, 1280 overlay, 800 and 390 layouts, 320 overflow, keyboard pass | AC-18 to AC-21 |

## Dependencies

- `specs/design-17-company-setup-completion/spec.md` (APPROVED) and `specs/authenticated-app-shell/spec.md`: shell, Administration column, tokens, breakpoints, Coming soon page; this spec supersedes only the `users-and-permissions` Coming soon slug (BR-02 there) and makes the Administration item's Users & permissions entry a real route.
- `specs/sign-in/spec.md` (AUDITED): session contract and per-request membership revalidation (BR-18).
- `specs/company-settings-and-branches/spec.md` (APPROVED): `GET /branches`.
- Tables `organization_users`, `organization_user_branches`, `users`, `roles`, `user_invitations`, `invitation_branches`, `branches`, `technician_profiles`, `audit_logs`.
- Implementation classification for `spec-impl`: Backend TARGETED (new invitation aggregate behavior, delivery port, schema amendment; migration requires `--generate-migration`); Frontend TARGETED (new lazy route and feature reusing shell, Administration column, drawer patterns).

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | The Administration column and drawer patterns from Design 17 are reused; only extracted if duplication would otherwise result. |
| AS-02 | The accept link is `{configured frontend origin}/auth/invitation?token={token}`; the route is delivered by the future acceptance spec. |
| AS-03 | Team profile names link to `/coming-soon/team`; the info-note link "Team" likewise. |
| AS-04 | Avatar initials are the first letters of first and last name. |
| AS-05 | Role summaries, matrix and role order are served from one backend catalog so frontend and API cannot diverge. |
| AS-06 | Search is debounced 300 ms; filters and paging are reflected in query parameters of the API only, not the URL. |
| AS-07 | Suspending a member does not touch `users.status`; the users row is shared across organizations. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Tabs and invitation-email toggle | Handoff stacked sections, always email / screenshot tabs and toggle | Yes | Handoff: stacked sections, no tabs, no toggle, invitation always sent (2026-09-28) |
| OD-02 | Delivery and acceptance | Port + no-op adapter / store only / include acceptance | Yes | Email port with no-op dev adapter, hash-only token; acceptance in a follow-up spec (2026-09-28) |
| OD-03 | Invitation persistence | Extend `user_invitations` / pending `users` rows | Yes | Extend `user_invitations` (BR-16), no `users` row before acceptance; expiry 3/7/14 days default 7; expired invitations stay "Pending invitation" with an "Expired" note and keep Resend and Revoke (2026-09-28) |
| OD-04 | Matrix source | Code catalog via API / seeded tables | Yes | Design values approved as a read-only code catalog served by `GET /permission-matrix`; enforcement unchanged; "View if granted" display-only (2026-09-28) |
| OD-05 | Last active | `last_login_at` / activity tracking | Yes | Last sign-in labelled "Last active"; "Never" for invitations (2026-09-28) |
| OD-06 | Owner and edit rules | Multiple Owners with last-Owner protection / single | Yes | Multiple Owners; last-Owner and self-suspension guards; edit access changes role and branches only; forced All branches for Owner and Operations Manager; confirmation on reductions (2026-09-28) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-01 |
| FR-02 | AC-02 |
| FR-03 | AC-03, AC-04 |
| FR-04 | AC-04 |
| FR-05 | AC-05 |
| FR-06 | AC-06, AC-07 |
| FR-07 | AC-08 |
| FR-08 | AC-09, AC-10 |
| FR-09 | AC-10, AC-12 |
| FR-10 | AC-11, AC-12 |
| FR-11 | AC-13 |
| FR-12 | AC-14 |
| FR-13 | AC-06, AC-09, AC-11, AC-13 |
| FR-14 | AC-15, AC-16 |
| FR-15 | AC-17 |
| FR-16 | AC-18, AC-19 |
| FR-17 | AC-20, AC-21 |
| FR-18 | AC-22 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-28 | — → DRAFT     | Created; decisions OD-01 to OD-06 answered by the user (1a, 2a, 3a with expired-invitation behavior, 4a, 5a, 6a) |
| 2026-09-28 | DRAFT → APPROVED | Approved by user via /spec approve |
