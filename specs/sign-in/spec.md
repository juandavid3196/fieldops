# Sign In

| Field    | Value        |
| -------- | ------------ |
| Feature  | `sign-in`    |
| Type     | Full-stack   |
| Status   | APPROVED     |
| Created  | 2026-09-24   |
| Updated  | 2026-09-24   |
| Approved | 2026-09-24   |

## Context and objective

FieldOps can create a tenant (`organization-onboarding`) but nobody can sign
in. This feature adds a public Sign In page and the backend session that every
later authenticated feature relies on. An existing active user signs in with
email and password, gets an HttpOnly cookie session tied to one active
organization resolved server-side, and lands on a minimal authenticated
Overview page from which they can sign out. Success means that only an active
user with an active membership in an active organization obtains a session,
that every failure looks the same from outside, and that brute-force attempts
are throttled.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Anonymous visitor | Open `/auth/sign-in`; submit email and password; open `/auth/register-company` from the page | Learn whether an email exists or which account state blocked a sign-in; choose or supply an organization, membership, user or role identifier |
| Signed-in user (any role: `owner`, `dispatcher`, `technician`, `accounting`, `operations_manager`, `viewer`) | Read their own session (`GET /sessions/current`); open `/overview`; sign out | Read another user's session; switch organization; use a session after their user, membership or organization stops being active |
| System | Resolve the active organization; issue, validate and delete the session cookie; update `last_login_at`; write the audit row; throttle attempts | Accept an organization identifier from the client |

No permission from `permissions`/`role_permissions` is checked in this spec.
The role is returned for display only.

## Scope

- Public page `/auth/sign-in` based on design 26, with the registration
  success message for `?registered=true`.
- Backend: sign in (`POST /sessions`), current session
  (`GET /sessions/current`) and sign out (`DELETE /sessions/current`).
- PBKDF2-HMAC-SHA256 password verifier for the onboarding BR-20 format.
- HttpOnly cookie session with Remember me lifetimes.
- Server-side active organization resolution and a per-request session
  re-check against user, membership and organization state.
- Rate limiting: per IP, per email (failed attempts) and global.
- `last_login_at` update and `auth.signed_in` audit row.
- CORS credentials and `Retry-After` exposure for the frontend origin.
- Frontend session service, `withCredentials` interceptor, `authGuard` and
  `guestGuard`, `''` and `**` redirects.
- Minimal authenticated Overview page (`/overview`) with sign out.
- `ApiError.retryAfterSeconds`.
- Loading, validation, submitting, error and success states; accessibility and
  responsive behavior.

## Non-goals

- Organization registration (`organization-onboarding`).
- Password recovery; the "Forgot password?" link is not shown.
- Email verification (unverified users can sign in).
- Invitations; "Accept an invitation" is not shown.
- Guest service requests; "Request a service" (header and list) and the guest
  note are not shown.
- User, role or permission administration; permission checks.
- Organization switching or an organization picker.
- Social authentication and multi-factor authentication.
- JWT, refresh tokens, server-side session store or per-session revocation.
- Persistent account lockout.
- `returnUrl` and the session-expired message.
- Privacy, Terms and Help links; the language selector.
- App shell (sidebar, top bar) and role-based landing pages.
- Data Protection key persistence and forwarded headers (deployment work).
- Password change and invalidating sessions after it.

## User flow

1. Visitor opens `/auth/sign-in` (directly, from `''`, from an unknown path, or
   from onboarding with `?registered=true`).
2. `guestGuard` calls `GET /sessions/current`. With a valid session the browser
   goes to `/overview`; otherwise the page renders. With `registered=true` the
   success message is shown and the URL is replaced with `/auth/sign-in`.
3. Visitor enters email and password, optionally checks Remember me, and
   selects **Sign in**.
4. Frontend validates. Invalid: field messages appear and nothing is sent.
5. Frontend sends one `POST /sessions` and shows the submitting state.
6. Backend throttles, validates, verifies the credentials, resolves the active
   organization, updates `last_login_at`, writes the audit row, sets the
   session cookie and returns the session.
7. Frontend stores the session and navigates to `/overview`, which shows the
   user's name, organization and role and a **Sign out** button.
8. On error, the page shows the message from Error behavior; the email is kept
   and the password is cleared.
9. **Sign out** sends `DELETE /sessions/current`; on `204` the frontend clears
   the session and navigates to `/auth/sign-in`.

## Functional requirements

| ID    | Requirement |
| ----- | ----------- |
| FR-01 | The frontend must serve `/auth/sign-in` from `features/authentication`, lazy loaded, protected by `guestGuard`. `''` and every unmatched path (`**`) must redirect to `/auth/sign-in`. |
| FR-02 | The frontend must validate email and password per BR-01 and BR-02 on submit, and on blur after the first submit attempt. Each invalid field shows exactly one message (BR-03). Invalid forms are never sent. |
| FR-03 | The backend must expose anonymous `POST /sessions`, accepting only `application/json` with a body of at most 4 KB. It normalizes and validates per BR-01 and BR-02 and returns `400` ValidationProblemDetails (keys `email`, `password`, BR-03 messages) when invalid, `415` for other or missing content types, `413` for larger bodies and `400` without field keys for malformed JSON, an empty body or a JSON `null` body. |
| FR-04 | The backend must verify credentials per BR-04 to BR-06 and return the same `401` response (BR-07) for an unknown email, a wrong password, an unreadable hash, a user whose status is not `active`, and a user with no eligible membership (BR-08). |
| FR-05 | The backend must resolve the active organization server-side per BR-08. The request body has no organization field, and any identifier properties sent by the client are ignored. |
| FR-06 | On valid credentials, the backend must, in one transaction, set `users.last_login_at` to now and insert the BR-13 audit row; then set the session cookie (BR-09, BR-10) and return `200` with the session body (API contracts). If the transaction fails, it returns `500` and sets no cookie. |
| FR-07 | On every request carrying the session cookie, the backend must re-check that the user is `active`, the membership in the session is `active` and belongs to that user and organization, the organization is active, and the BR-10 absolute limit has not passed. If any check fails, the request is treated as unauthenticated and the response deletes the cookie. The role is loaded from the membership on each request, not from the cookie. |
| FR-08 | `GET /sessions/current` must return `200` with the session body for a valid session and `401` otherwise. Both responses carry `Cache-Control: no-store`. |
| FR-09 | `DELETE /sessions/current` must delete the session cookie and return `204`, with or without a valid session. |
| FR-10 | The backend must enforce BR-11 and BR-12 on `POST /sessions`: `429` ProblemDetails with `Retry-After` (whole seconds) and no data change. |
| FR-11 | Every server-side component that needs the caller's organization must obtain it only from the validated session (FR-07). Organization identifiers in bodies, queries or headers are never used to resolve it. |
| FR-12 | The API pipeline must run authentication after `UseCors` and before `UseAuthorization`, and the rate limiter after `UseCors`. CORS must allow credentials for the configured origins only and expose `Retry-After`. Unauthenticated API requests get `401` ProblemDetails, never a redirect. |
| FR-13 | The backend must follow BR-14 for logging. |
| FR-14 | The frontend must provide a core session service holding the current session as a signal, loaded from `GET /sessions/current`. `authInterceptor` must set `withCredentials` on API URLs only. `authGuard` protects `/overview` and redirects to `/auth/sign-in` when the session request returns anything other than `200`. `guestGuard` redirects to `/overview` when it returns `200`. |
| FR-15 | The Sign In page must implement the states in UI behavior: registered message, submitting, field errors and the response errors in Error behavior. While a request is in flight, further submits are ignored so exactly one request is sent per attempt. After `429`, submit stays disabled for `retryAfterSeconds` (60 if absent). |
| FR-16 | On `200` from `POST /sessions`, the frontend must store the returned session and navigate to `/overview`, replacing the Sign In entry in history. No toast. |
| FR-17 | The frontend must serve `/overview` from `features/overview`, lazy loaded, protected by `authGuard`. It shows a `h1` "Welcome, {firstName}", the organization name labelled "Organization", the role name labelled "Role" (a description list) and a **Sign out** button that calls `DELETE /sessions/current`; on `204` it clears the session and navigates to `/auth/sign-in`; on failure it stays and shows "We couldn't sign you out. Try again." |
| FR-18 | `ApiError` must carry `retryAfterSeconds`: the `Retry-After` header parsed as a non-negative integer of seconds on `429` responses, otherwise `undefined`. |
| FR-19 | The Sign In page must render the controls and layout in UI behavior and omit every control listed as omitted there. |
| FR-20 | The Sign In and Overview pages must meet the accessibility and responsive rules in UI behavior. |

## Business and validation rules

| ID    | Rule | Enforced by |
| ----- | ---- | ----------- |
| BR-01 | `email`: trimmed and lowercased before any check (same normalization as onboarding BR-19). Required; ≤254 characters; one `@` with a non-empty local part and a domain containing a dot; no whitespace (onboarding BR-06). | Both |
| BR-02 | `password`: not trimmed; required; ≤128 UTF-16 code units. No minimum length or composition check at sign-in. `rememberMe`: optional boolean, default `false`. | Both |
| BR-03 | Field messages, first failing rule wins. `email`: 1. empty → "Enter your email address." 2. longer than 254 or format fails → "Enter a valid email address, for example name@company.com." `password`: 1. empty → "Enter your password." 2. longer than 128 → "Use 128 characters or fewer." These four are the only field messages the page displays; any other server field message is replaced with "Enter a valid value." | Both |
| BR-04 | Password verifier: the stored value must be `pbkdf2-sha256${iterations}${base64-salt}${base64-hash}` with an integer iteration count ≥ 600,000, a 16-byte salt and a 32-byte hash (onboarding BR-20). It derives the key with `Rfc2898DeriveBytes.Pbkdf2` (SHA-256, the stored iteration count) and compares with `CryptographicOperations.FixedTimeEquals`. Any other format is a verification failure. No new package. | Backend |
| BR-05 | When no user has the normalized email, the backend verifies the password against a fixed dummy hash in the BR-04 format with 600,000 iterations, so the response time does not depend on whether the email exists. | Backend |
| BR-06 | The password is verified before any status or membership check. Status and membership are evaluated only after a successful verification. | Backend |
| BR-07 | Every credential failure returns the same `401` ProblemDetails (same `type`, `title`, `status`, no `detail`, no `errors`), with no cookie set. | Backend |
| BR-08 | Eligible memberships: `organization_users.status = 'active'` whose `organizations.is_active = true`. The active organization is the eligible membership with the earliest `joined_at` (nulls last), then earliest `created_at`, then lowest `id`. No eligible membership → BR-07. `users.status` must be `active`; `pending`, `suspended` and `disabled` → BR-07. `email_verified_at` is not checked. | Backend |
| BR-09 | Session cookie `fieldops_session`: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, no `Domain`. The value is an encrypted, authenticated ticket (ASP.NET Core cookie authentication, Data Protection) holding the user id, organization id, membership id, sign-in time and the Remember me flag. It holds no role, email or password data. | Backend |
| BR-10 | Lifetime. `rememberMe = false`: a browser-session cookie (no `Expires`/`Max-Age`) whose ticket expires 8 hours after sign-in and is never renewed. `rememberMe = true`: a persistent cookie with a 14-day sliding expiration, renewed on authenticated requests, and never valid more than 30 days after sign-in. | Backend |
| BR-11 | `POST /sessions` rate limits (ASP.NET Core rate limiter, in memory). Per client IP (`RemoteIpAddress`): 10 requests per 5-minute fixed window. Global: 300 requests per 1-minute fixed window. Every request counts, whatever its outcome. | Backend |
| BR-12 | Per-email throttle (in memory, keyed by a SHA-256 of the normalized email): after 5 failed attempts (`401`) within a sliding 15-minute window, further `POST /sessions` for that email return `429` before password verification, with `Retry-After` = seconds until the oldest counted failure leaves the window. Unknown emails are counted the same way. A successful sign-in clears the counter. `400`, `413`, `415` and `429` do not count as failures. Counters reset on restart and are not shared across instances. | Backend |
| BR-13 | Audit row on success: `action = 'auth.signed_in'`, `entity_type = 'user'`, `entity_id` = user id, `organization_id` = resolved organization id, `actor_user_id` = user id, `ip_address` = client IP, `branch_id`, `before_data`, `after_data` null, `metadata` `{}`. Failed attempts are not audited. | Backend |
| BR-14 | Never log request bodies, emails, passwords, password hashes, cookie values or `Set-Cookie` headers. Failed sign-ins may be logged only with the failure category and, when known, the user id; never the email. | Backend |
| BR-15 | Page messages: registered → "Your organization was created. Sign in to continue."; `401` → "The email or password is incorrect. Check your details and try again."; `429` → "Too many sign-in attempts. Try again in {n} minutes.", where `n` = `retryAfterSeconds` / 60 rounded up (minimum 1, 60 seconds when absent); when `n` = 1 the text is "Too many sign-in attempts. Try again in 1 minute."; network, `5xx`, `413`, `415`, `400` without mappable keys and any other status → "We couldn't sign you in right now. Try again in a moment." These page messages are an explicit exception to showing `ApiError.message`; backend text is still never shown. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| Signed out | Signed in (cookie issued) | `POST /sessions` `200` | Visitor | BR-04 to BR-08 pass; BR-11/BR-12 not exceeded |
| Signed in | Signed out (cookie deleted) | `DELETE /sessions/current` | Signed-in user | None |
| Signed in | Signed out (cookie deleted) | Any request after user, membership or organization becomes inactive, or after the BR-10 limit | System | FR-07 check fails |
| Signed in | Signed out (cookie gone) | Browser closed with `rememberMe = false`, or 8 h / 14 days idle / 30 days reached | Browser / System | BR-10 |
| Email not throttled | Email throttled | 5th failed attempt within 15 min | System | BR-12 |
| Email throttled | Email not throttled | Oldest failure leaves the window | System | BR-12 |

`users.status`, `organization_users.status` and `organizations.is_active` are
read only; this feature never changes them.

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `users`: read `id, email, password_hash, first_name, last_name, status`;
    write `last_login_at`, `updated_at`.
  - `organization_users`: read `id, organization_id, user_id, role_id, status, joined_at, created_at`.
  - `organizations`: read `id, name, is_active`.
  - `roles`: read `id, code, name`.
  - `audit_logs`: insert per BR-13.
  - `permissions`, `role_permissions`: not used.
- Lookups use existing constraints: `users (email)` unique,
  `organization_users (organization_id, user_id)` unique and primary keys.
- Schema amendments required: None. No EF migration.
- Domain code changes (no schema effect): `User` must support recording the
  last sign-in time.
- Accepted limitations of a store-less cookie (decided 2026-09-24):
  - Sign out deletes the cookie only on that browser; a copied cookie stays
    valid until BR-10 expiry unless FR-07 invalidates it.
  - No per-session revocation and no password-change invalidation.
  - Throttle counters are per process and lost on restart.
  - Data Protection keys use the default store; losing them signs every user
    out. Persistence is deployment work.
  - Behind the production proxy, `RemoteIpAddress` is the proxy until forwarded
    headers are configured, so the BR-11 per-IP limit applies to all clients
    together. Accepted until deployment.

## Tenant isolation and authorization

- Organization context: resolved by the backend at sign-in (BR-08), stored
  inside the encrypted cookie ticket and re-validated on every request
  (FR-07). It is the only source of `OrganizationId` for later features
  (FR-11).
- Client-provided organization identifiers: never accepted. `POST /sessions`
  has no identifier fields; unknown properties such as `organizationId`,
  `membershipId`, `userId` or `roleId` are ignored and the result is identical
  to a request without them. Headers such as `X-Organization-Id` are ignored.
- Another organization: a session never exposes data from an organization
  other than the resolved one. `GET /sessions/current` returns only the
  caller's user, organization and role.
- Membership inactive, organization inactive or user not active after sign-in:
  the next request is `401` and the cookie is deleted.
- A tampered, foreign-key-signed or expired cookie: `401`, cookie deleted.
- Permissions: `POST /sessions` and `DELETE /sessions/current` are anonymous;
  `GET /sessions/current` requires a valid session. Frontend guards are UX
  only; the backend decides.

## API contracts

Contract status: Final. Paths are relative to the API base URL (`API_CONFIG`),
following the existing no-prefix routing.

Session body (`200`):
`{ "user": { "id": "{uuid}", "firstName": "…", "lastName": "…", "email": "…" }, "organization": { "id": "{uuid}", "name": "…" }, "role": { "code": "owner", "name": "Owner" } }`

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/sessions` | `application/json` `{ "email": "…", "password": "…", "rememberMe": false }`, ≤4 KB | `200` session body + `Set-Cookie: fieldops_session` (BR-09, BR-10), `Cache-Control: no-store` | `400` ValidationProblemDetails (`email`, `password`; BR-03) or without keys for malformed JSON, an empty body or a JSON `null` body · `401` generic ProblemDetails (BR-07) · `413` · `415` (other or missing content type) · `429` + `Retry-After` · `500` generic ProblemDetails | Anonymous |
| GET | `/sessions/current` | Cookie | `200` session body, `Cache-Control: no-store` | `401` ProblemDetails (cookie deleted when present but invalid) | Valid session |
| DELETE | `/sessions/current` | Cookie (optional), no body | `204`, `Set-Cookie` deleting `fieldops_session` | `500` generic ProblemDetails | Anonymous |

- Every error body follows `docs/backend/api-configuration.md`: no exception
  text, a `traceId`, and CORS headers for configured origins.
- CORS: `AllowCredentials()` for the configured origins,
  `Access-Control-Expose-Headers: Retry-After`.

## UI behavior and states

Design reference (approved by the user): design 26, Sign In. Verified on
2026-09-24 that screen 26 is Sign In.

- `design/identity-access/screens/26-design.png`
- `design/identity-access/handoff/Sign In.dc.html` (reference only; rebuilt in Angular + PrimeNG + semantic HTML, no handoff code copied)
- `design/identity-access/handoff/Identity-and-Organization-Handoff.md` §1 (only `[C]` items and the `[S]` items adopted here)
- `design/identity-access/assets/signin-background.png`

Shown on Sign In:

- Form panel: `h1` "Welcome back", subtitle "Sign in to continue to
  FieldOps", the page message slot (BR-15), Email (`type="email"`,
  `autocomplete="email"`), Password (`autocomplete="current-password"`) with a
  show/hide toggle, Remember me (`p-checkbox`, binary, unchecked by default),
  **Sign in** (`p-button type="submit"`, full width, large), and an "Other ways
  to get started" section with one row: "Create a company account" / "For
  service business owners", linking to `/auth/register-company`.
- Brand panel (from `lg`): text wordmark "FieldOps", tagline "People · Work ·
  Customers · For a brighter tomorrow", `h2` "Run every service job from one
  place", subhead, three status cards ("Request approved", "Technician
  assigned", "Invoice paid") and three benefit rows ("Work runs smoother",
  "Happier customers", "A more profitable business") with the design copy,
  text only, and the background photo anchored bottom-center with
  `object-fit: cover` and `alt=""`.

Omitted: "Need service?" and "Request a service" (header and list row),
"Forgot password?", "Accept an invitation", the guest note, Privacy, Terms,
Help, the language selector, the mountain logo mark and every icon except the
password toggle's text.

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| Sign In (`/auth/sign-in`) | `guestGuard` resolves the session before render; no page skeleton. Submitting: **Sign in** shows a spinner and the label "Signing in…", inputs are locked and further submits are ignored | Not applicable (form) | Field messages (BR-03) under each field; page messages (BR-15) in the message slot; the email is kept, the password is cleared and focused after `401` | Signed-in visitor is redirected to `/overview` | Navigate to `/overview`; no toast. Registered message (BR-15) when opened with `registered=true` |
| Overview (`/overview`) | `authGuard` resolves the session before render. Sign out shows a spinner on the button and ignores repeat clicks | Not applicable | "We couldn't sign you out. Try again." (`p-message`, `role="alert"`) | Signed-out visitor is redirected to `/auth/sign-in` | After sign out, navigate to `/auth/sign-in` |

- Message slot: one `p-message` between the subtitle and the Email field.
  Only one message is shown at a time; a new submit or error replaces the
  registered message. Registered message: severity success, `role="status"`.
  Error messages: severity error, `role="alert"`.
- Mockups: design 26 (Approved) for Sign In. Overview has no mockup by
  design: its content is fully defined by FR-17 and uses the same field and
  message patterns as Sign In; no Overview behavior depends on a mockup.
- Accessibility:
  - One `h1` per page; the brand panel heading is an `h2`.
  - Tab order: Email → Password → Show/hide → Remember me → Sign in → Create a
    company account.
  - The show/hide toggle is a real `<button type="button">` with visible text
    "Show"/"Hide", `aria-label` "Show password"/"Hide password" and
    `aria-pressed`.
  - Invalid fields have `aria-invalid="true"` and `aria-describedby` pointing
    at their message. Color is never the only error indicator.
  - Enter submits the form. No autofocus.
  - Focus ring uses the preset focus tokens.
- Responsive (breakpoints `md` = 48rem, `lg` = 64rem):
  - From `lg`: split screen, brand panel about 55% and form panel about 45%;
    the form is centered with a maximum width of 27.5rem.
  - Below `lg`: brand panel hidden; the "FieldOps" text wordmark appears at
    the top-left of the form panel.
  - Below `md`: one column; inputs and buttons full width; touch targets
    ≥44px; no horizontal scroll at 320px.
  - `src/styles/_breakpoints.scss` is created with this first consumer.
- Styling: Aura + teal preset tokens only, no handoff hex values. Navy is
  replaced by a dark `surface` token, the lavender card by a teal tint, and
  Inter by the current font stack. White text over the photo must meet WCAG AA
  contrast (a scrim token is allowed). The photo is served as an optimized
  image of at most 300 KB with a dark surface background as fallback.
  `primeicons` and `@primeicons/angular` are not added.
- Message contrast: Aura's default success and error `p-message` text colors
  fail WCAG AA. They are overridden once, app-wide, in the PrimeNG preset
  (`core/config/primeng.config.ts`) to darker `green`/`red` shades in light
  mode so every message meets AA (AC-56).

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Client validation fails | BR-03 field messages; no request | None |
| `400` with `email`/`password` keys | Field messages (BR-03 filter) | None |
| `400` without mappable keys, `413`, `415` | "We couldn't sign you in right now. Try again in a moment." | None |
| `401` (any credential failure) | "The email or password is incorrect. Check your details and try again."; password cleared and focused | None; the per-email failure counter increases |
| `429` | "Too many sign-in attempts. Try again in {n} minutes." ("1 minute" when `n` = 1); submit disabled for `retryAfterSeconds` (60 if absent) | None |
| `500`, including a failed `last_login_at`/audit transaction | "We couldn't sign you in right now. Try again in a moment."; no cookie | None (transaction rolled back) |
| Network failure (status 0) | "We couldn't sign you in right now. Try again in a moment."; form unlocked | None, or a completed sign-in whose response was lost |
| Session invalid on a later request | `401`; cookie deleted; `authGuard` sends the user to `/auth/sign-in` | None |
| Sign out fails | "We couldn't sign you out. Try again." | None |

## Acceptance criteria

| ID    | Given | When | Then |
| ----- | ----- | ---- | ---- |
| AC-01 | A visitor without a session | They open `/auth/sign-in` | The Sign In form renders with the FR-19 controls and none of the omitted controls |
| AC-02 | A visitor without a session | They open `/` or `/unknown-path` | The browser is on `/auth/sign-in` |
| AC-03 | A visitor with a valid session | They open `/auth/sign-in` | The browser is on `/overview` |
| AC-04 | The page opened with `?registered=true` | It renders | The success message "Your organization was created. Sign in to continue." is shown with `role="status"` and the URL is `/auth/sign-in` |
| AC-05 | Empty email and password | The visitor submits | No request is sent, the email shows "Enter your email address." and the password shows "Enter your password." |
| AC-06 | For each BR-03 rule checked by the client, a value that fails only that rule | The form is validated | The field shows that rule's message |
| AC-07 | A form after a failed submit | The visitor corrects a field and blurs it | That field's error disappears |
| AC-08 | For each BR-03 rule, a request that fails only that rule | It is posted | The response is `400` with that key and message, and no cookie is set |
| AC-09 | A request with `Content-Type: text/plain` | It is posted to `POST /sessions` | The response is `415` ProblemDetails with no `Set-Cookie` |
| AC-10 | An active user in an active organization with an active membership | They post correct credentials with ` User@Example.COM ` | The response is `200` with the session body for that user, organization and role |
| AC-11 | A successful sign-in with `rememberMe = false` | The `Set-Cookie` header is inspected | It has no `Expires` or `Max-Age` attribute |
| AC-12 | A successful sign-in with `rememberMe = true` | The cookie is inspected | It is persistent with an expiry 14 days ahead |
| AC-13 | Unknown email; known email with wrong password; user `pending`; user `suspended`; user `disabled`; only an inactive membership; only an inactive organization; no membership; malformed `password_hash` | Each is posted with any password | Every response is `401` with an identical body apart from `traceId`, and no cookie is set |
| AC-14 | A suspended user and the correct password | It is posted | The response is `401` (not `403`) |
| AC-15 | A user with `email_verified_at = null` and otherwise eligible | They sign in | The response is `200` |
| AC-16 | A stored hash produced by the onboarding BR-20 hasher | The verifier checks the original password and a different one | The first verifies and the second fails |
| AC-17 | An unknown email | It is posted | The dummy hash is verified (asserted through the verifier being invoked once with the dummy hash) |
| AC-18 | A user with active memberships in organizations A (`joined_at` 2026-01-01) and B (`joined_at` 2025-01-01) | They sign in | The session organization is B |
| AC-19 | A user with an active membership in inactive organization B and in active organization A | They sign in | The session organization is A |
| AC-20 | A sign-in body that includes `organizationId` set to another organization the user is not a member of | It is posted | The session organization is the BR-08 organization, not the supplied one |
| AC-21 | A signed-in user whose membership is set to `suspended` | They call `GET /sessions/current` | The response is `401` and the cookie is deleted |
| AC-22 | A signed-in user whose organization is set inactive, or whose user status is set to `disabled` | They call `GET /sessions/current` | The response is `401` and the cookie is deleted |
| AC-23 | A signed-in user whose membership role is changed | They call `GET /sessions/current` | The response shows the new role |
| AC-24 | A tampered cookie value, or a cookie issued by another Data Protection key ring | It is sent to `GET /sessions/current` | The response is `401` |
| AC-25 | A signed-in user of organization A, with header `X-Organization-Id` set to organization B | They call `GET /sessions/current` | The response contains organization A only |
| AC-26 | A successful sign-in | `users` is read | `last_login_at` equals the sign-in time |
| AC-27 | The audit insert fails | Correct credentials are posted | The response is `500`, no cookie is set, `last_login_at` is unchanged and no audit row exists |
| AC-28 | A failed sign-in | `audit_logs` is read | No row was added |
| AC-29 | One client IP that sent 10 `POST /sessions` in the current 5-minute window | It sends an 11th | The response is `429` with `Retry-After` and CORS headers |
| AC-30 | 300 `POST /sessions` in the current minute | Any client sends another | The response is `429` with `Retry-After` |
| AC-31 | 5 failed attempts for one email within 15 minutes (from different IPs) | A 6th attempt with the correct password is posted | The response is `429` with `Retry-After`, no cookie is set and the password is not verified |
| AC-32 | 5 failed attempts for an unknown email | A 6th attempt is posted | The response is `429`, identical in shape to AC-31 |
| AC-33 | 4 failed attempts, then a successful sign-in | 5 more failed attempts follow | The 5th of them is `401`, not `429` |
| AC-34 | A signed-in user | They call `DELETE /sessions/current` | The response is `204` with a `Set-Cookie` that expires `fieldops_session` |
| AC-35 | No session | `DELETE /sessions/current` is called | The response is `204` |
| AC-36 | No session | `GET /sessions/current` is called | The response is `401` ProblemDetails with no `Location` header |
| AC-37 | A request from the configured frontend origin with credentials | Any session endpoint answers | The response has `Access-Control-Allow-Credentials: true` and `Access-Control-Expose-Headers` including `Retry-After` |
| AC-38 | Sign-in requests with correct, wrong and throttled credentials | Logs are inspected | No entry contains the email, password, hash or cookie value |
| AC-39 | A valid form | The visitor submits | The button shows "Signing in…", inputs are locked and a second click sends no second request |
| AC-40 | A `200` response | The frontend handles it | The session service holds the returned session and the browser is on `/overview` |
| AC-41 | A `401` response | The frontend handles it | The `401` BR-15 message is shown with `role="alert"`, the email is kept, the password is empty and focused |
| AC-42 | A `429` response with `Retry-After: 90` | The frontend handles it | The message reads "Try again in 2 minutes." and submit is disabled for 90 seconds, then enabled |
| AC-43 | A `500` or network error | The frontend handles it | The generic BR-15 message is shown and the form is unlocked |
| AC-44 | A `400` with a field message outside BR-03 | The page maps it | The field shows "Enter a valid value." |
| AC-45 | A `429` with `Retry-After: 30`; a `429` without it; a `500` | `ApiErrorService` maps them | `retryAfterSeconds` is `30`, `undefined` and `undefined` |
| AC-46 | An API request and a non-API request | `authInterceptor` handles them | Only the API request has `withCredentials: true` |
| AC-47 | A visitor without a session | They open `/overview` | The browser is on `/auth/sign-in` |
| AC-48 | A signed-in user | They open `/overview` | The page shows "Welcome, {firstName}", the organization name labelled "Organization", the role name labelled "Role" and **Sign out** |
| AC-49 | A signed-in user on `/overview` | They select **Sign out** and receive `204` | The session service is empty and the browser is on `/auth/sign-in` |
| AC-50 | A signed-in user on `/overview` | Sign out fails | "We couldn't sign you out. Try again." is shown and the page stays |
| AC-51 | The Sign In page | The "Create a company account" row is inspected | It links to `/auth/register-company` |
| AC-52 | A viewport 320px wide | Sign In renders | One column, the brand panel is hidden, and there is no horizontal scroll |
| AC-53 | A viewport 800px wide | Sign In renders | The brand panel is hidden and the text wordmark is shown above the form |
| AC-54 | A viewport 1280px wide | Sign In renders | The brand panel and the form panel are side by side |
| AC-55 | Keyboard-only use | The visitor tabs through Sign In | Focus follows the UI behavior tab order with a visible focus indicator, the toggle switches its label and `aria-pressed`, and Enter submits |
| AC-56 | An automated accessibility check (axe) on Sign In (default, validation error, `401` error, registered) and Overview | It runs | It reports no violations |
| AC-57 | A signed-in user | They call `GET /sessions/current` | The `200` response has `Cache-Control: no-store` |
| AC-58 | A sign-in with `rememberMe = true` and a request at least every day | A request is made 30 days after sign-in | The response is `401` |
| AC-59 | A request body larger than 4 KB | It is posted to `POST /sessions` | The response is `413` ProblemDetails with no `Set-Cookie` |
| AC-60 | A request body that is not valid JSON | It is posted to `POST /sessions` | The response is `400` ProblemDetails with a `traceId`, no field keys and no `Set-Cookie` |
| AC-61 | A successful sign-in with valid credentials | The `Set-Cookie` header is inspected | It sets `fieldops_session` with `HttpOnly`, `Secure`, `SameSite=Strict` and `Path=/` |
| AC-62 | A successful sign-in with `rememberMe = false` | A request with that cookie is made 8 hours after sign-in | The response is `401` |
| AC-63 | No session | `GET /sessions/current` is called | The `401` response has `Cache-Control: no-store` |
| AC-64 | A successful sign-in with `rememberMe = true` | A request with that cookie is made after 14 days without requests | The response is `401` |
| AC-65 | A request with credentials from an origin that is not configured | Any session endpoint answers | The response has no CORS headers |
| AC-66 | A successful sign-in | `audit_logs` is read | Exactly one new row exists and it matches every BR-13 value |
| AC-67 | A `429` response with `Retry-After: 45` | The frontend handles it | The message reads "Too many sign-in attempts. Try again in 1 minute." |
| AC-68 | A request without a `Content-Type` header | It is posted to `POST /sessions` | The response is `415` ProblemDetails with no `Set-Cookie` |
| AC-69 | A request with an empty body, or with the JSON body `null` | It is posted to `POST /sessions` | The response is `400` ProblemDetails with a `traceId`, no field keys and no `Set-Cookie` |

## Testing requirements

| Level | Covers |
| ----- | ------ |
| Backend unit | Password verifier and dummy hash: AC-16, AC-17. Email normalization and validators: AC-08. Organization resolution ordering: AC-18, AC-19. Per-email throttle window: AC-31, AC-32, AC-33. `User` last sign-in update |
| Backend integration (Testcontainers; users seeded directly) | AC-08, AC-09, AC-68, AC-69, AC-10, AC-11, AC-12, AC-58, AC-13, AC-14, AC-15, AC-18, AC-19, AC-20, AC-21, AC-22, AC-23, AC-24, AC-25, AC-26, AC-27, AC-28, AC-29, AC-30, AC-31, AC-32, AC-33, AC-34, AC-35, AC-36, AC-37, AC-38, AC-57, AC-59, AC-60, AC-61, AC-62, AC-63, AC-64, AC-65, AC-66. Time-based ACs use an injected `TimeProvider`. Tests run over HTTP, so they read `Set-Cookie` headers directly and forward the cookie explicitly instead of relying on a cookie container that drops `Secure` cookies |
| Authorization/tenant isolation | AC-13, AC-14 (no state leak); AC-18, AC-19, AC-20 (server-side resolution, client id ignored); AC-21, AC-22, AC-23 (per-request re-check); AC-24 (tampered cookie); AC-25 (header ignored); AC-36 (anonymous gets `401`) |
| Frontend component/service/guard | AC-01, AC-02, AC-03, AC-04, AC-05, AC-06, AC-07, AC-39, AC-40, AC-41, AC-42, AC-67, AC-43, AC-44, AC-45, AC-46, AC-47, AC-48, AC-49, AC-50, AC-51 |
| Frontend QA (Playwright, final audit) | AC-52, AC-53, AC-54, AC-55, AC-56 (axe via the `axe-core` devDependency). Only client-validation and failed-credential submissions; no successful sign-in against the local database (it writes `last_login_at` and `audit_logs`) |

## Dependencies

- Order: this spec is implemented before `organization-onboarding`. It adds
  the backend project references (`Api → Application + Infrastructure`,
  `Infrastructure → Application + Domain`, `Application → Domain`), a shared
  PBKDF2 hasher/verifier for BR-20/BR-04, the rate limiter registration, CORS
  credentials and `Retry-After` exposure, `ApiError.retryAfterSeconds` and
  `src/styles/_breakpoints.scss`. Onboarding reuses them and its AC-22 becomes
  satisfiable. Decided 2026-09-24.
- `/auth/register-company` (`organization-onboarding`, APPROVED, not
  implemented): until it lands, the "Create a company account" link falls
  through `**` back to `/auth/sign-in`. AC-51 checks only the link target.
- `axe-core` is added as a frontend devDependency for the AC-56 accessibility
  check. Approved 2026-09-24.
- Seeded roles in `roles`: present.
- Identity and tenancy tables migrated (`InitialIdentityAndTenancy`): present.
- Documentation updated with the implementation: `docs/backend/api-configuration.md`
  (authentication, cookie, CORS credentials, rate limiting) and
  `docs/frontend/frontend-configuration.md` (`authInterceptor`, guards,
  `retryAfterSeconds`), and `frontend/CLAUDE.md` § HTTP (`authInterceptor` is
  no longer a placeholder: it sets `withCredentials` on API URLs; pages may
  show page-specific copy by `ApiError.kind`, as BR-15 does, but never backend
  text).
- Deployment (not this spec): Data Protection key persistence and forwarded
  headers.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 | Server validation uses FluentValidation (already installed in `FieldOps.Application`). |
| AS-02 | Endpoint paths `/sessions` and `/sessions/current` follow the existing no-prefix routing (production reaches them under `/api`). |
| AS-03 | No global fallback authorization policy is added; `GET /sessions/current` uses `[Authorize]` and later specs mark their endpoints explicitly. |
| AS-04 | The password field uses the PrimeNG 22 non-deprecated password input with a custom toggle button, since `primeicons` is not installed. |
| AS-05 | The registered, `429` and Overview copy in BR-15 and FR-17 is proposed copy; the design supplies none. |
| AS-06 | Inputs are locked while submitting, as in onboarding, instead of the handoff's editable-while-submitting suggestion. |
| AS-07 | The background image is converted from the provided 1.5 MB PNG to an optimized format during implementation; the source asset is unchanged. |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 | Session mechanism | HttpOnly cookie / JWT + refresh tokens | Yes | HttpOnly cookie, Data Protection ticket, no store; JWT refresh store would need a schema amendment (2026-09-24) |
| OD-02 | Session duration and Remember me | 8 h / 14 days; 8 h / 30 days; omit | Yes | Browser-session 8 h absolute; Remember me 14 days sliding, 30 days absolute; unchecked by default (2026-09-24) |
| OD-03 | CSRF protection | SameSite=Strict + JSON / antiforgery | Yes | `SameSite=Strict`, JSON-only state change, CORS credentials for configured origins (2026-09-24) |
| OD-04 | Brute-force protection | IP + email + global / IP + global / temporary lockout | Yes | Per IP 10/5 min, per email 5 failures/15 min, global 300/min, in memory, no lockout; proxy risk accepted until deployment (2026-09-24) |
| OD-05 | Inactive user, membership or organization | Generic `401` / specific `403` | Yes | Generic `401`, password verified first, no email verification required (2026-09-24) |
| OD-06 | Several active memberships | Earliest joined / reject / picker | Yes | Earliest `joined_at`; no switching (2026-09-24) |
| OD-07 | Endpoints | Sign in + session + sign out / without sign out | Yes | All three (2026-09-24) |
| OD-08 | Writes on success | `last_login_at` + audit / `last_login_at` / none | Yes | `last_login_at` + `auth.signed_in` audit; failures not audited (2026-09-24) |
| OD-09 | Destination after sign-in | `/overview` as dependency / minimal page in this spec / in-page state | Yes | Minimal authenticated `/overview` page in this spec (2026-09-24) |
| OD-10 | `returnUrl` and root route | Out of scope / validated `returnUrl` | Yes | Out of scope; `''` redirects to Sign In; signed-in visitors go to `/overview` (2026-09-24) |
| OD-11 | "Forgot password?" | Omit / link to future route | Yes | Omit (2026-09-24) |
| OD-12 | Other design 26 controls | Only Create company account / none / all | Yes | Only "Create a company account"; the rest omitted (2026-09-24) |
| OD-13 | Brand panel styling | Aura substitutes / new tokens, Inter, icons / no panel | Yes | Aura substitutes, text wordmark, no icons (2026-09-24) |
| OD-14 | Order with onboarding | Sign-in first / onboarding first | Yes | Sign-in first and owns the shared pieces (2026-09-24) |

## Traceability

| FR    | AC |
| ----- | -- |
| FR-01 | AC-01, AC-02, AC-03 |
| FR-02 | AC-05, AC-06, AC-07 |
| FR-03 | AC-08, AC-09, AC-59, AC-60, AC-68, AC-69 |
| FR-04 | AC-13, AC-14, AC-15, AC-16, AC-17 |
| FR-05 | AC-18, AC-19, AC-20 |
| FR-06 | AC-10, AC-11, AC-12, AC-26, AC-27, AC-28, AC-58, AC-61, AC-62, AC-64, AC-66 |
| FR-07 | AC-21, AC-22, AC-23, AC-24 |
| FR-08 | AC-36, AC-57, AC-63 |
| FR-09 | AC-34, AC-35 |
| FR-10 | AC-29, AC-30, AC-31, AC-32, AC-33 |
| FR-11 | AC-20, AC-25 |
| FR-12 | AC-36, AC-37, AC-65 |
| FR-13 | AC-38 |
| FR-14 | AC-03, AC-46, AC-47 |
| FR-15 | AC-04, AC-39, AC-41, AC-42, AC-43, AC-44, AC-67 |
| FR-16 | AC-40 |
| FR-17 | AC-48, AC-49, AC-50 |
| FR-18 | AC-45 |
| FR-19 | AC-01, AC-51 |
| FR-20 | AC-52, AC-53, AC-54, AC-55, AC-56 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-24 | — → DRAFT     | Created |
| 2026-09-24 | DRAFT → DRAFT | Fixed validation findings F1–F7: split AC-09, AC-10, AC-11 and AC-34 into single-outcome ACs (added AC-59 to AC-63); FR-08 `no-store` on `200` and `401`; Overview mockup note; `frontend/CLAUDE.md` update listed in Dependencies; integration-test note for `Secure` cookies over HTTP |
| 2026-09-24 | DRAFT → DRAFT | Fixed validation findings F8–F9: split AC-12 (added AC-64) and AC-37 (added AC-65); also split AC-26 into `last_login_at` and audit row (added AC-66) |
| 2026-09-24 | DRAFT → APPROVED | Approved by user via /spec approve |
| 2026-09-24 | APPROVED → DRAFT | Revised by user request during /spec-impl: BR-15 singular "1 minute" (added AC-67); FR-17/AC-48 Overview labels "Organization" and "Role"; `axe-core` devDependency for AC-56; app-wide preset override of message colors for WCAG AA; FR-03 and API contract: missing `Content-Type` → `415`, empty or `null` body → keyless `400` (added AC-68, AC-69) |
| 2026-09-24 | DRAFT → APPROVED | Approved by user via /spec approve |
