# Invitation acceptance

| Field    | Value                   |
| -------- | ----------------------- |
| Feature  | `invitation-acceptance` |
| Type     | Full-stack              |
| Status   | APPROVED                |
| Created  | 2026-09-29              |
| Updated  | 2026-09-29              |
| Approved | 2026-09-29              |

## Context and objective

Design 18 lets an Owner invite, resend and revoke invitations, but the email
goes to a no-op adapter and nobody can use the link. This spec completes the
flow: the invitation is delivered by a real SMTP adapter (Mailpit captures it
locally), the emailed link opens `/auth/invitation`, and a new or existing
FieldOps user accepts it. Acceptance activates exactly one membership with the
invited role and branch access, consumes the single-use token, audits the
change and signs the user in to the invited organization. Success means a
token works exactly once, reveals nothing when unusable, never duplicates or
reactivates identities, and all acceptance writes happen together or not at
all.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Owner (`owner`) | Invite, resend and revoke from Users & permissions (unchanged design-18 rules); resending is the only way to replace a lost or expired link | Accept on behalf of the invitee |
| Invitee without a FieldOps account (anonymous) | Open the emailed link; see its own invitation details; confirm names, create a password and accept | See another invitation; choose or change role, branches or organization; search for or request invitations |
| Invitee with a FieldOps account | Sign in with their current password on the invitation page and accept | Accept an invitation addressed to another email; reactivate a suspended/disabled user or an existing membership of the organization |
| Anyone holding an unusable link | See the generic unavailable screen | Learn the email, organization, role, branches or why the link is unusable |
| System | Hash and look up tokens; lock and consume the invitation; create/reuse the user; create the membership, branch access and optional profile link; audit; issue the session; throttle | Trust a client organization, membership, role, branch or user identifier; store, log, audit or return a raw token |

## Scope

- Real invitation delivery: SMTP adapter behind the existing
  `IInvitationDelivery` port, validated settings, Mailpit in
  `docker-compose.yml` for local capture; the no-op adapter is removed.
- Accept link format `{frontend origin}/auth/invitation#token={token}`.
- Anonymous `POST /invitations/validate` and `POST /invitations/accept`;
  authenticated `POST /invitations/accept-existing`.
- Atomic acceptance: user creation (new) or reuse (existing), membership,
  branch access, optional link of an existing technician profile, token
  consumption, `last_login_at`, audit row and session for the invited
  organization.
- Rate limiting and safe `400`, `401`, `409`, `410`, `429`, `500` responses.
- Public page `/auth/invitation` with loading, unavailable, load-error,
  new-account, existing-account, field-error, submitting, generic-error and
  success/redirect states, built from designs 26-accept-invitation and
  26-invitation-expired.
- Sign In "Other ways to get started": remove "Accept an invitation" and shorten
  the guest note.

## Non-goals

- Password recovery; customer "Request a service" (the row stays as is).
- OAuth/social login, MFA.
- Role selection; editing role, branches or organization during acceptance.
- Organization switching; changing Sign In organization resolution (sign-in
  BR-08).
- A public invitation search, resend-by-email or "lost link" screen.
- Creating technician profiles (Team owns profile creation).
- Email verification flows beyond setting `email_verified_at` at acceptance.
- Changing invite/resend/revoke rules of design 18, except the link format and
  the delivery adapter.
- Schema changes or migrations.
- Playwright or agent-driven browser testing.

## User flow

1. An Owner invites or resends from Users & permissions (design 18). The SMTP
   adapter sends the email; resend replaces the token so the old link stops
   working.
2. The invitee opens `…/auth/invitation#token=…`. The page stores the token in
   tab-scoped `sessionStorage` and immediately replaces the URL with
   `/auth/invitation`.
3. The page shows a loading card and calls `POST /invitations/validate`.
4. Unusable token → the unavailable screen. Load failure → error with Retry.
   Valid → "Join {organizationName}" with email, role and branch access, and the
   new-account form prefilled with the invited names.
5. New user: confirms names, enters password and confirmation, selects
   **Accept invitation** → `POST /invitations/accept`.
6. Existing user: selects **Sign in to accept**; the card shows the invited email
   read-only and a current-password field; **Sign in and accept** calls the
   common `POST /sessions`, then `POST /invitations/accept-existing`.
7. The backend accepts atomically and issues the session for the invited
   organization. The frontend stores the returned session, clears the token and
   navigates to `/overview` replacing history, exactly like a Sign In success.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | Invitation delivery must use an SMTP adapter implementing the existing `IInvitationDelivery` port per BR-15, registered as the only production delivery adapter with settings validated on start; the no-op adapter is removed, and `docker-compose.yml` must provide a Mailpit service for local capture. |
| FR-02 | Invite and resend must build the accept link `{first configured frontend origin}/auth/invitation#token={token}` (BR-01); the token never appears in a query string or path. |
| FR-03 | `POST /invitations/validate` must return only the BR-04 details for a usable token and the identical BR-05 response for any unusable one. |
| FR-04 | `POST /invitations/accept` must accept a usable invitation for an email without a `users` row, creating the user per BR-07 and applying BR-09. |
| FR-05 | `POST /invitations/accept-existing` must accept a usable invitation for the authenticated session user whose email equals the invitation email, applying BR-09 without changing the user's password, names or other memberships. |
| FR-06 | Acceptance must perform the BR-09 writes in one transaction and return the session body with a session cookie for the invited organization; any failure rolls back everything and sets no cookie. |
| FR-07 | Acceptance must enforce the BR-08 eligibility rules: no duplicate users, no placeholder hashes, no reactivation of a suspended/disabled user or of any existing membership. |
| FR-08 | Acceptance must link an existing technician profile only per BR-11 and never create one. |
| FR-09 | A token must be usable once; concurrent or repeated acceptance must produce exactly one membership and one audit row (BR-10). |
| FR-10 | The three endpoints must apply BR-12 rate limits, the BR-18 check order and the BR-13/BR-14 error and logging rules. |
| FR-11 | The frontend must serve public `/auth/invitation` (no guard), capture and clear the token per BR-16 and send it only in POST bodies. |
| FR-12 | The page must show the loading, unavailable and load-error states of UI behavior before any invitation detail is shown. |
| FR-13 | The new-account mode must follow design 26-accept-invitation with the BR-06 validation, checklist, show-password control and submitting state. |
| FR-14 | The existing-account mode must sign in through `POST /sessions` with the invited email and then call `POST /invitations/accept-existing`. |
| FR-15 | The page must map every response to the BR-17 copy, never showing backend text, and on success store the session and navigate to `/overview` replacing history. |
| FR-16 | Sign In must no longer show "Accept an invitation", and the guest note must read "Guest requests do not create an account." |
| FR-17 | Both invitation screens must follow the responsive, visual and accessibility rules in UI behavior. |

## Business and validation rules

Text is trimmed (passwords are not); whitespace-only counts as empty. Emails
compare trimmed and lowercased.

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Token: 32 random bytes, URL-safe base64 without padding (43 characters); only the SHA-256 hash is stored (`user_invitations.token_hash`, existing design-18 token generation). Lifetime = invitation expiry (3/7/14 days on invite, 7 days on resend, design-18 BR-08/BR-11). Resend replaces the hash, so the previous token is unknown. | Backend |
| BR-02 | Usable invitation: `token_hash` matches, `accepted_at` and `revoked_at` null, `expires_at > now`, organization `is_active`. | Backend |
| BR-03 | Request `token`: required string of exactly 43 characters from `A–Z a–z 0–9 - _`; otherwise `400` ValidationProblemDetails whose only key is `token` with the message "Enter a valid value." (the token value is never echoed). Malformed JSON, an empty body or a JSON `null` body → `400` without keys (sign-in FR-03). | Backend |
| BR-04 | Validate `200` body: `{ organizationName, inviterName, email, firstName, lastName, role: { code, name }, isAllBranches, branches: [{ name }] (ordered by name, active branches only, empty when all), expiresAt }`. No ids, no other data. `inviterName` is the inviter's first and last name. A non-all-branches invitation whose invited branches are all inactive returns `isAllBranches = false` and `branches = []`; the page shows Branch access "No active branches" and acceptance returns `409 access_unavailable` (BR-09). | Backend |
| BR-05 | Unknown, replaced, expired, revoked, consumed or inactive-organization token → `410` ProblemDetails with an identical body (same `type`, `title`, `status`, no `detail`, no `errors`, only `traceId` differs) on all three endpoints. | Backend |
| BR-06 | Fields. `firstName`, `lastName`: required, ≤100 — "Enter a first name." / "Enter a last name." / "Use 100 characters or fewer.". `password`: required; 12–128 UTF-16 code units; not equal to the invitation email ignoring case — "This field is required." / "Use 12 to 128 characters." / "Choose a password that is different from your email." (onboarding BR-17 messages). `confirmPassword` (client only): required, equal to password — "This field is required." / "Passwords don't match.". Existing-account `password` (sign-in BR-02/BR-03): required, ≤128 — "Enter your password." / "Use 128 characters or fewer.". Server `400` uses ValidationProblemDetails keys `firstName`, `lastName`, `password`, `token`. | Both |
| BR-07 | New user: `users` row with invitation email, confirmed names, PBKDF2 hash of the submitted password (onboarding BR-20 format), `status = active`, `email_verified_at = now`, `last_login_at = now`. Invitation names are not changed. | Backend |
| BR-08 | Eligibility (checked inside the transaction): `accept` when a `users` row exists for the email, in any status → `409` `account_exists`. `accept-existing`: no valid session → `401`; session user email ≠ invitation email → `409` `identity_mismatch`. Both: an `organization_users` row already exists for (invitation organization, user) in any status → `409` `membership_exists` (it is never reactivated or changed). A user whose `status` is not `active` can never accept (new path: `account_exists`; existing path: cannot obtain a session). | Backend |
| BR-09 | Acceptance writes: membership `organization_users` (`status = active`, `role_id` and `is_all_branches` from the invitation, `invited_by_user_id` = inviter, `joined_at = now`); `organization_user_branches` for each `invitation_branches` branch still active in the organization (none when all branches); `user_invitations.accepted_at = now`; BR-11 profile link; `users.last_login_at = now`; one BR-14 audit row. When the invitation is not all-branches and no invited branch is still active → `409` `access_unavailable`, no change. | Backend |
| BR-10 | Concurrency: the invitation row is locked (`SELECT … FOR UPDATE`) and BR-02 is re-checked under the lock. The loser of a race, or any later attempt, gets `410`. Unique constraints `users(email)` and `organization_users(organization_id,user_id)` map to `409` `account_exists` / `membership_exists`, never `500`. | Backend |
| BR-11 | Technician profile: only when `link_team_profile = true`. Exactly one `technician_profiles` row of the invitation organization with `organization_user_id` null and trimmed, lowercased `email` equal to the invitation email → set its `organization_user_id` to the new membership. Zero or several matches → no profile change; acceptance still succeeds. Never create a profile. | Backend |
| BR-12 | Rate limit (one policy shared by the three endpoints, in memory like sign-in BR-11): per client IP 20 requests per 5-minute fixed window counted across all three endpoints together, global 300 per minute across the three; every request counts, whatever its outcome. The limiter runs before any other check. `429` ProblemDetails with `Retry-After`. `POST /sessions` keeps its own sign-in limits and per-email throttle. | Backend |
| BR-13 | Errors never contain the token, hash, email, names or exception text. `409` ProblemDetails carries only an extension `code` (`account_exists`, `identity_mismatch`, `membership_exists`, `access_unavailable`). `500` is the generic ProblemDetails. All responses carry `Cache-Control: no-store`. | Backend |
| BR-14 | Audit row: `action = 'user.invitation_accepted'`, `entity_type = 'user_invitation'`, `entity_id` = invitation id, `organization_id` = invitation organization, `actor_user_id` = accepting user, `branch_id` null, `before_data` null, `after_data` `{ membershipId, roleCode, isAllBranches, branchIds, accountCreated, teamProfileLinked }`, `metadata` `{}`, `ip_address` = client IP. Never log or audit the token, hash, email, password or request bodies. | Backend |
| BR-15 | SMTP adapter: .NET built-in SMTP client (no new package); settings `Email:Smtp` (`Host`, `Port`, `EnableSsl`, `SenderAddress`, `SenderName`, optional `UserName`/`Password` from user-secrets/environment) validated on start. Development: `localhost:1025`, no SSL, Mailpit (SMTP 1025, UI 8025, ports bound to `127.0.0.1`). Message: subject "{inviterName} invited you to join {organizationName} on FieldOps"; plain-text and HTML bodies with the invitee first name, organization, role, expiry date, an **Accept invitation** link/button with the accept link and "This invitation can only be used once."; no other data. A send failure throws, so design-18 `502` rollback applies. The adapter logs failure category only. | Backend |
| BR-16 | Frontend token handling: on load, a `token` in the URL fragment is written to `sessionStorage` and the URL is replaced with `/auth/invitation` (no fragment, no query) before any request. A `token` query parameter is ignored and removed. The token is read only from `sessionStorage`, sent only in POST bodies, and removed on success, on `400`/`410`, and on **Back to sign in**; kept on `409`, `429`, `500` and network errors so Retry works. No token → unavailable screen. | Frontend |
| BR-17 | Page copy. Unavailable screen (all BR-05 cases, `400`, missing token): title "This invitation is no longer available", body "This invitation can no longer be used. Ask an Owner at your organization to send you a new one.", info note "For your security, expired and replaced invitation links cannot be reactivated.", button **Back to sign in**, plain text "Need help? Contact your organization.". Load error (validate `500`, network, other): "We couldn't load this invitation. Check your connection and try again." + **Retry**. Validate `429`: the load-error state with "Too many attempts. Try again in {n} minutes." (`n` as below) and **Retry** disabled until `retryAfterSeconds` (60 if absent) have passed. Response `400` mapping on any endpoint: only the `token` key → unavailable screen; `firstName`, `lastName` or `password` keys → field messages; no keys → the generic message below. `409 account_exists`: "An account already exists for this email. Sign in to accept." with a **Sign in to accept** action. Other `409`: "This invitation can't be accepted with this account. Contact an Owner at {organizationName}." `401` from `POST /sessions`: "The email or password is incorrect. Check your details and try again." (password cleared and focused). `429`: "Too many attempts. Try again in {n} minutes." (`n` per sign-in BR-15, "1 minute" singular). `500`, network, other: "We couldn't accept the invitation right now. Try again in a moment.". A `400` field message outside BR-06 → "Enter a valid value.". | Frontend |
| BR-18 | Check order, first failing check wins: 1. rate limit (`429`); 2. `accept-existing` only: valid session (`401`); 3. content type, size and body/field validation (`415`, `413`, `400`); 4. usable token (`410`, BR-02); 5. eligibility (`409`, BR-08/BR-09) inside the transaction under the BR-10 lock. Consequently `409` (including `account_exists`) is returned only for a usable token, and an unusable token never reveals account, membership or identity state. | Backend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Invitation open, token T1 | Open, token T2 (T1 unknown) | Resend | Owner | Design-18 BR-11 |
| Invitation open, not expired | Accepted (`accepted_at` set) | Accept / accept-existing | Invitee | BR-02, BR-08, BR-09 under the BR-10 lock |
| Invitation open | Expired (unusable) | `expires_at` passes | System | Display only; no write |
| (no user) | User `active` | Accept as new user | Invitee | BR-07, BR-08 |
| (no membership in organization) | Membership `active` | Accept | Invitee | BR-08 |
| Any session / none | Session for invited organization | Accept success | System | Transaction committed |

Existing memberships, `users.status` of existing users and other organizations
are never changed.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `user_invitations`: read all; write `accepted_at`.
  - `invitation_branches`: read.
  - `branches`: read `id`, `organization_id`, `name`, `is_active`.
  - `organizations`: read `id`, `name`, `is_active`.
  - `roles`: read `id`, `code`, `name`.
  - `users`: read `id`, `email`, `first_name`, `last_name`, `status`; insert
    (BR-07); write `last_login_at`, `updated_at`.
  - `organization_users`: read; insert (BR-09).
  - `organization_user_branches`: insert.
  - `technician_profiles`: read `organization_id`, `organization_user_id`,
    `email`; write `organization_user_id`, `updated_at`.
  - `audit_logs`: insert (BR-14).
- Constraints relied on: `user_invitations.token_hash` unique, `users(email)`
  unique, `organization_users(organization_id,user_id)` unique, technician
  profile composite FK `(organization_id, organization_user_id)`.
- Schema amendments required: None. Inspection confirmed the implemented
  invitation schema (design-18 BR-16) is sufficient. No migration.
- Domain changes (no schema effect): `UserInvitation` accept; membership
  creation from an invitation; technician profile link.

## Tenant isolation and authorization

- Organization context: the invitation row found by token hash is the only
  source of the target organization. No endpoint accepts an organization,
  membership, role, branch or user identifier; unknown body properties are
  ignored.
- `validate` and `accept` are anonymous; possession of a usable token is the
  authorization, and it reveals only BR-04 data of that invitation.
- `accept-existing` requires a valid session (sign-in FR-07). The user is taken
  from the session and must match the invitation email (BR-08); the session
  organization is irrelevant and never used as the target.
- Other organizations: no read or write outside the invitation organization;
  other memberships of an existing user are untouched.
- The issued session follows the sign-in cookie contract (BR-09 there,
  `rememberMe = false` lifetime) with the invited organization and new
  membership. Later sign-ins still use sign-in BR-08.
- CSRF: existing `SameSite=Strict` cookie, JSON only, CORS allow-list.
- Role policies are unchanged; the membership receives exactly the invited role.

## API contracts

Contract status: Final. Paths follow the existing no-prefix routing. All
requests are `application/json`, body ≤4 KB (`413`/`415` as sign-in FR-03).
Session body = sign-in API contracts. Errors are evaluated in the BR-18 order.
`400` shapes follow BR-03 (`token` key) and BR-06 (field keys).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/invitations/validate` | `{ token }` | `200` BR-04 body | `400` (BR-03) · `410` (BR-05) · `413` · `415` · `429` + `Retry-After` · `500` | Anonymous |
| POST | `/invitations/accept` | `{ token, firstName, lastName, password }` | `200` session body + `Set-Cookie: fieldops_session` (invited organization) | `400` ValidationProblemDetails (`token`, BR-06 keys) · `409` `account_exists` / `membership_exists` / `access_unavailable` · `410` · `413` · `415` · `429` · `500` (rolled back, no cookie) | Anonymous |
| POST | `/invitations/accept-existing` | `{ token }` + session cookie | `200` session body + `Set-Cookie` replacing the session with the invited organization | `401` (checked before the body) · `400` (`token`) · `413` · `415` · `409` `identity_mismatch` / `membership_exists` / `access_unavailable` · `410` · `429` · `500` (rolled back, previous cookie unchanged) | Valid session |

- Changed internal contract: `IInvitationLinkBuilder` produces the BR-01
  fragment link. `IInvitationDelivery` is unchanged.
- CORS: existing credentials policy; `Retry-After` already exposed.

## UI behavior and states

Design references (approved by the user):

- `c:\Users\usuario\Desktop\portafolio\Fieldops-docs\diseños\acceptance invitation\26-accept-invitation.png`
- `c:\Users\usuario\Desktop\portafolio\Fieldops-docs\diseños\acceptance invitation\26-invitation-expired.png` (layout for the generic unavailable screen; its details box and expired-specific copy are replaced by BR-17 per OD-02)
- `c:\Users\usuario\Desktop\portafolio\Fieldops-docs\diseños\acceptance invitation\auth-technician-background.png`

Layout: the Sign In split-screen layout, breakpoints and wordmark. The brand
panel uses the technician background (optimized ≤300 KB, dark surface
fallback, `alt=""`, scrim for AA contrast), `h2` "Your team, connected from day
one." and "Secure access for every person, in every field location, so you can
get to work faster together." with the teal accent rule. The form side shows a
centered card on the light surface.

New-account card (26-accept-invitation): **Back to sign in** link; decorative
teal envelope icon; `h1` "Join {organizationName}"; subtitle "{inviterName}
invited you to join their FieldOps workspace."; details box (description list)
Email, Role, Branch access ("All branches" or names joined with ", "); First
name and Last name (prefilled, editable, side by side); Branch access shows
"No active branches" when BR-04 returns an empty list without all branches; Password and Confirm
password (`autocomplete="new-password"`); one **Show password** checkbox
controlling both fields (no per-field eye icons); "Your password must:"
checklist "12–128 characters" and "Must not match your email", each with a met
/ not-met icon plus text state; **Accept invitation** (full width); separator;
"Already have a FieldOps account? **Sign in to accept**"; footer lock note
"This invitation can only be used once and expires in {n} days." (`n` = days
remaining rounded up, "1 day" singular).

Existing-account mode (same card): heading, subtitle and details box
unchanged; Email shown read-only (the invitation email); Password
(`autocomplete="current-password"`) with **Show password**; **Sign in and
accept**; "New to FieldOps? **Create your account**" returns to the new-account
mode; same footer note.

Unavailable screen (26-invitation-expired layout): **Back to sign in** link,
decorative amber envelope-clock icon, BR-17 title and body, no details box,
info note, **Back to sign in** button, help text.

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/auth/invitation` | Card skeleton while validating; no detail shown | No token → unavailable screen | Unavailable screen (`410`, `400` with only `token`); load error + Retry (validate `429` disables Retry for the `Retry-After` period); field messages; BR-17 page messages in a `p-message` slot above the submit button | Public; any existing session is ignored until acceptance replaces it | Submitting: button spinner, "Accepting…" / "Signing in…", inputs locked, repeat submits ignored; then session stored, token cleared, navigate to `/overview` replacing history, no toast |
| `/auth/sign-in` | Unchanged | N/A | Unchanged | Unchanged | "Other ways" without "Accept an invitation"; note per FR-16 |

- Validation: on blur after first interaction and on submit; invalid forms are
  never sent; after a `409 account_exists`, **Sign in to accept** switches mode
  keeping the email; after `401` the password is cleared and focused.
- If `POST /sessions` succeeds but `accept-existing` fails, the page shows the
  mapped message (or the unavailable screen for `410`); the user remains signed
  in to their existing organization.
- Responsive (sign-in conventions, `md` 48rem, `lg` 64rem): from `lg` split
  screen with the brand panel; below `lg` brand panel hidden and the text
  wordmark above the card; below `md` one column, card full width, name fields
  stacked, checklist items stacked, inputs/buttons full width, touch targets
  ≥44px, no horizontal scroll at 320px.
- Accessibility: one `h1` per state; brand heading `h2`; decorative icons
  `aria-hidden`; details box as a description list; checklist state conveyed by
  text, not color only; **Show password** is a labelled checkbox; invalid fields
  `aria-invalid` + `aria-describedby`; error messages `role="alert"`, loading
  status announced politely; focus moves to the `h1` when the state changes
  (loaded, unavailable, mode switch) and to the first invalid field on submit;
  visible preset focus ring; Enter submits.
- Styling: existing `--fo-*`/Aura teal tokens, PrimeNG inputs, password,
  checkbox, button, message and skeleton; no new package.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Missing/malformed token | Unavailable screen (`400` with only `token`, or no request) | None |
| Unusable token together with an existing account, existing membership, identity mismatch or invalid fields | BR-18 order: invalid fields → `400`; otherwise `410`; never `409` | None |
| Validate rate-limited | `429`; load-error state with the BR-17 `429` copy; Retry disabled for the period | None |
| `400` without keys (malformed JSON) | BR-17 generic message | None |
| Unknown, replaced, expired, revoked, consumed, inactive organization | `410` identical body; unavailable screen; token cleared | None |
| Field validation | `400` BR-06 keys; field messages; input kept | None |
| Email already has an account (new path) | `409 account_exists`; BR-17 message with **Sign in to accept** | None |
| Wrong current password | `POST /sessions` `401`; BR-17 message | Sign-in throttle counter |
| Session user ≠ invitation email; existing membership (any status); no invited branch still active | `409` with code; BR-17 generic 409 message | None |
| No session on accept-existing | `401`; generic page message | None |
| Double or concurrent acceptance | First `200`; others `410` | One acceptance only |
| Rate limit | `429` + `Retry-After`; BR-17 message; submit disabled for the period | None |
| Transaction failure (e.g. audit insert) | `500` generic; BR-17 generic message; no cookie issued/replaced | Fully rolled back |
| SMTP send failure on invite/resend | Design-18 `502` behavior | Rolled back |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An Owner invites and then resends an invitation | The delivery port is called | Each call carries an accept link `{origin}/auth/invitation#token={43-char token}` with no query string; the stored hash is the SHA-256 of that token; after resend the first token returns `410` on validate and the second is usable |
| AC-02 | Development settings and the Mailpit service | An Owner sends an invitation (USER check) | Mailpit shows one email with the BR-15 subject and content and a working **Accept invitation** link that opens the invitation page |
| AC-03 | A usable token for a Dispatcher invitation with two branches (one inactive) | `POST /invitations/validate` | `200` with exactly the BR-04 fields (active branch only, ordered by name), no ids, no token or hash, `Cache-Control: no-store` |
| AC-04 | Tokens that are unknown, replaced, expired, revoked, consumed, of an inactive organization, and a malformed token (parameterized; accept with valid fields, accept-existing with a valid session of the invited email) | Each is posted to validate, accept and accept-existing | Unusable ones return `410` bodies identical apart from `traceId`; malformed returns `400` whose only key is `token`; no response contains the token, email or organization data; nothing changes |
| AC-05 | A usable token whose email has no user | `POST /invitations/accept` with valid names and password | `200` session body for the invited organization and role with a session cookie; one active user (BR-07 fields, PBKDF2 hash verifying the password), one active membership with the invited role, all-branches flag and active invited branches, `accepted_at` set, one BR-14 audit row (`accountCreated = true`); `GET /sessions/current` returns the invited organization |
| AC-06 | Bodies failing one BR-06 rule each (empty/long names, empty/short/long password, password equal to email in different case) (parameterized) | `POST /invitations/accept` | `400` with that key and message; no user, membership or audit row; invitation still usable |
| AC-07 | A usable token whose email already has a user (active, suspended, disabled; parameterized) | `POST /invitations/accept` | `409 account_exists`; the existing user row (hash, names, status) is unchanged; no membership; invitation still open |
| AC-08 | A user with an active membership in organization A signed in, invited to organization B | `POST /invitations/accept-existing` | `200` session body for organization B; the cookie now resolves to B; one new B membership with the invited access; no new user; password, names and the A membership are unchanged; audit `accountCreated = false` |
| AC-09 | No session; a session of a different user; a user with an existing B membership that is suspended (and one that is active) (parameterized) | `POST /invitations/accept-existing` | `401`; `409 identity_mismatch`; `409 membership_exists` with the suspended membership still suspended; nothing changes and the invitation stays open |
| AC-10 | An invitation limited to branches that are all inactive now | Accept is called | `409 access_unavailable`, no change; with one branch still active only that branch is assigned; an all-branches invitation assigns no branch rows |
| AC-11 | `link_team_profile = true` with exactly one unlinked matching profile, with none, and with two; and `false` with one (parameterized) | Acceptance succeeds | Only the single-match case links that profile to the new membership (`teamProfileLinked = true`); no profile is ever created; other cases leave profiles unchanged |
| AC-12 | One usable token | Two acceptances run concurrently, then a third follows | Exactly one returns `200`; the others return `410`; one user/membership/audit row exists |
| AC-13 | The audit insert is forced to fail | A new-user acceptance runs | `500` generic ProblemDetails, no cookie; no user, membership, branch rows or profile link; `accepted_at` null; the token still validates |
| AC-14 | One IP that sent 20 requests to the invitation endpoints in 5 minutes | It sends another | `429` with `Retry-After` and CORS headers; no data change |
| AC-15 | Successful, failed and rate-limited invitation requests plus invite/resend | Logs, audit rows and error bodies are inspected | None contain the raw token, token hash, email, password or request body |
| AC-16 | The page opened with `#token=abc…`, with `?token=abc…`, and with no token (parameterized) | It initializes | Fragment: token in `sessionStorage`, URL `/auth/invitation` before the validate request, token only in the POST body; query: ignored, removed, unavailable screen; none: unavailable screen with no request |
| AC-17 | Validate pending, `410`, `400` with only `token`, `429` with `Retry-After: 90`, `500`/network, and a `200` with no branches and `isAllBranches = false` (parameterized) | The page renders | Pending: skeleton, no details; `410`/`400`: BR-17 unavailable screen without any invitation data and token removed; `429`: load-error state reading "Try again in 2 minutes." with Retry disabled for 90 s and token kept; `500`: load error + Retry that re-validates with the stored token; empty branches: Branch access "No active branches" |
| AC-18 | A validated invitation in new-account mode | The user edits fields, toggles Show password, submits invalid then valid data and receives `200` | Heading, subtitle, details and prefilled names follow the validate response; checklist and field messages follow BR-06; Show password reveals both fields; submitting locks the form with "Accepting…" and ignores repeats; success stores the session, removes the token and navigates to `/overview` replacing history |
| AC-19 | Accept responses `400` with field keys, with an unknown field key, with only `token` and without keys, `409` (each code), `429` with `Retry-After: 90`, `500`, network (parameterized) | The page maps them | Field keys → BR-06 field messages ("Enter a valid value." for unknown); only `token` → unavailable screen with token removed; no keys → generic message; BR-17 messages only (no backend text); `account_exists` offers **Sign in to accept**; `429` shows "2 minutes" and disables submit for 90 s; except for the unavailable case, input and token are kept |
| AC-20 | Existing-account mode | The user submits a password and `POST /sessions` returns `401`, then `200` followed by accept-existing `200` | `POST /sessions` receives the invitation email; `401` shows the BR-17 message with the password cleared and focused and no accept call; success calls accept-existing with the token and navigates to `/overview` |
| AC-21 | The Sign In page | It renders | "Other ways to get started" has no "Accept an invitation" row; "Create a company account" and "Request a service" are unchanged; the note reads "Guest requests do not create an account." |
| AC-22 | Viewports 1280, 800, 390 and 320px (USER check) | Both invitation screens render | 1280: split screen with the technician panel; 800: panel hidden, wordmark above the card; 390: one column, stacked names and checklist, full-width controls, ≥44px targets; 320: no horizontal scroll |
| AC-23 | The implementation beside both approved PNGs at desktop width (USER VISUAL QA REPORT) | They are compared and a keyboard pass runs | Card structure, spacing, tokens, icons and copy match the designs except the documented deviations (no unavailable details box, single Show password control, text wordmark); focus order, visible focus, labels, alerts and focus moves follow UI behavior |
| AC-24 | Parameterized combinations: unusable token + email with an existing account (accept); unusable token + existing membership or identity mismatch (accept-existing); unusable token + invalid fields (accept); no session + malformed token (accept-existing) | Each is posted | Responses follow BR-18: `410`, `410`, `400` with field keys, `401` respectively; no `409` is ever returned for an unusable token; nothing changes |
| AC-25 | `Email:Smtp` settings missing a host, with an invalid port or an invalid sender address (parameterized), and valid settings | Options validation runs and the service provider is built outside tests | Invalid settings fail validation on start; valid settings pass and `IInvitationDelivery` resolves to the SMTP adapter; no no-op adapter is registered |

## Testing requirements

25 active ACs. TARGETED Full-stack. Reuse the existing API test factory, the
recording delivery fake and the users/sign-in/session test helpers; time via
the injected `TimeProvider`. Agents run no browser automation.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | SMTP settings validation table and adapter registration (1 method); optionally the profile-match selection if non-trivial outside the handler. ≤2 methods | AC-25, AC-11 |
| Backend integration | (1) Link format, resend replacement and validate details vs unusable-token table; (2) new-user acceptance happy path incl. session, audit, branches, profile-link table; (3) new-user validation and `account_exists` table; (4) existing-user acceptance incl. untouched other memberships and conflict table (`401`, mismatch, suspended membership not reactivated, `access_unavailable`); (5) BR-18 check-order table; (6) concurrent/double acceptance; (7) audit-failure rollback; (8) rate limit and no-secret logging/audit. ≤8 methods | AC-01, AC-03 to AC-15, AC-24 |
| Authorization/tenant isolation | Identity mismatch and existing-membership denial (one representative each, within (4)); no state leak for unusable tokens (within (5)); target organization from token only; other organization memberships unchanged | AC-08, AC-09, AC-24 |
| Frontend component/service | (1) Token capture/URL replacement/query ignore/no token; (2) validate states incl. unavailable and retry; (3) new-account form rules, show password, submit and success navigation; (4) response mapping table; (5) existing-account sign-in-then-accept sequence; (6) Sign In other-ways change (extend existing spec). 5–6 methods | AC-16 to AC-21 |
| Manual (USER) | Mailpit end-to-end delivery and link; responsive; visual fidelity and keyboard pass (USER VISUAL QA REPORT) | AC-02, AC-22, AC-23 |

## Dependencies

- `specs/design-18-users-and-permissions/spec.md` (APPROVED): invite, resend,
  revoke, token hashing, delivery port; this spec changes only the link format
  (its AS-02) and replaces the no-op adapter (its Non-goal "Real email
  transport").
- `specs/sign-in/spec.md` (AUDITED): session cookie, `POST /sessions`,
  per-request revalidation, throttle, page message patterns, auth layout and
  breakpoints. Organization resolution at sign-in is unchanged.
- `specs/organization-onboarding/spec.md`: password rules (BR-17) and hash
  format (BR-20).
- Infrastructure change authorized by the user (OD-04, 2026-09-29): Mailpit
  service in `docker-compose.yml`.
- Documentation updated with the implementation: `docs/backend/api-configuration.md`
  (endpoints, rate-limit policy, `Email:Smtp` settings, Mailpit),
  `docs/authentication.md` (session issued at acceptance).
- Classification for `spec-impl`: Backend TARGETED (new anonymous endpoints,
  transaction/lock, session issuance outside sign-in, SMTP adapter and
  settings); Frontend TARGETED (new public route, token storage, two modes).
  No migration; `database-reviewer` only if mapping changes.

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | `inviterName`, `firstName` and `lastName` in the validate response are covered by the approved design (subtitle, prefilled names) and already appear in the invitation email; they are the invitee's own invitation data, not other users' private data. |
| AS-02 | The session issued at acceptance uses `rememberMe = false` lifetime (8 h browser session). |
| AS-03 | An existing user whose only memberships are inactive cannot obtain a session with `POST /sessions` and therefore cannot use the existing-account path; an Owner must resolve their access. Accepted limitation. |
| AS-04 | The design's gear logo is replaced by the existing text wordmark, as on Sign In. |
| AS-05 | The accept link uses the first configured CORS origin, as the current link builder does. |
| AS-06 | Mailpit's image version is pinned in `docker-compose.yml`; its ports bind to localhost only. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Existing-user acceptance and landing | Common sign-in then accept, session stays in BR-08 org / same with session issued for invited org / no session at acceptance | Yes | Sign in via `POST /sessions`, then accept-existing; acceptance issues the session for the invited organization (new and existing users) (2026-09-29) |
| OD-02 | Unavailable screen content | Generic, no details / expired-specific with details | Yes | One generic screen, no details box, BR-17 copy (2026-09-29) |
| OD-03 | Brand panel | Technician photo and design copy / reuse Sign In panel | Yes | Sign In layout with technician background and design copy (2026-09-29) |
| OD-04 | Real delivery | SMTP + Mailpit in compose / SMTP only / `.eml` files | Yes | SMTP adapter with built-in client, Mailpit in `docker-compose.yml`, no-op removed (2026-09-29) |
| OD-05 | Technician profile | Link or create / link only / none | Yes | Link only an existing unlinked profile with the same normalized email; otherwise nothing; Team owns creation (2026-09-29) |
| OD-06 | Sign In other ways | Remove row and shorten note / remove row only | Yes | Remove row; note "Guest requests do not create an account." (2026-09-29) |
| OD-07 | Token transport in the link | Query / fragment | Yes | Fragment `#token=`; captured to `sessionStorage`, URL replaced, POST bodies only (2026-09-29) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-02, AC-25 |
| FR-02 | AC-01 |
| FR-03 | AC-03, AC-04 |
| FR-04 | AC-05, AC-06 |
| FR-05 | AC-08 |
| FR-06 | AC-05, AC-08, AC-13 |
| FR-07 | AC-07, AC-09, AC-10 |
| FR-08 | AC-11 |
| FR-09 | AC-01, AC-12 |
| FR-10 | AC-04, AC-14, AC-15, AC-24 |
| FR-11 | AC-16 |
| FR-12 | AC-17 |
| FR-13 | AC-18 |
| FR-14 | AC-20 |
| FR-15 | AC-18, AC-19 |
| FR-16 | AC-21 |
| FR-17 | AC-22, AC-23 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-29 | — → DRAFT     | Created; decisions OD-01 to OD-07 answered by the user (1b, 2a, 3a, 4a, 5b link-only, 6a, fragment token) |
| 2026-09-29 | DRAFT → DRAFT | Revised after validation: BR-18 check order (no `409` for unusable tokens; added AC-24); single `400` shape with `token` key and frontend `400` mapping (BR-03, BR-17, AC-04, AC-19); validate `429` load state (BR-17, AC-17); BR-12 limit shared across the three endpoints; "No active branches" display (BR-04, AC-17); SMTP settings/registration split from manual AC-02 into AC-25; test-helper class names replaced with generic wording |
| 2026-09-29 | DRAFT → APPROVED | Approved by user via /spec approve |
