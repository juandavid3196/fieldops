**English** | [Español](authentication.es.md)

# Authentication in FieldOps

Summary of how sign-in works. Technical detail:
[`docs/backend/api-configuration.md`](backend/api-configuration.md) and
[`docs/frontend/frontend-configuration.md`](frontend/frontend-configuration.md).
Full requirements: [`specs/sign-in/spec.md`](../specs/sign-in/spec.md).

## The idea in 30 seconds

- The user signs in with **email and password** at `/auth/sign-in`.
- If they are correct, the backend answers with a **session cookie**
  (`fieldops_session`). The browser stores it and sends it automatically with
  every request; JavaScript cannot read it (`HttpOnly`).
- The cookie is encrypted and says **who the user is and which organization
  they work in**. The server decides the organization, never the client.
- On **every request** the backend checks again that the user, their
  membership and the organization are still active. If not, the session is
  invalidated.
- There is no JWT, no refresh tokens and no sessions table.

## Endpoints

| Method | Route               | Purpose                           | Normal response |
| ------ | ------------------- | --------------------------------- | --------------- |
| POST   | `/sessions`         | Sign in                           | `200` + cookie  |
| GET    | `/sessions/current` | Who am I? (current session)       | `200` or `401`  |
| DELETE | `/sessions/current` | Sign out (deletes the cookie)     | `204`           |

Session response: `{ user: {id, firstName, lastName, email}, organization: {id, name}, role: {code, name} }`.

## Full flow

```text
Browser (Angular)                           API (.NET)
─────────────────                           ──────────
1. Opens /auth/sign-in
   guestGuard ── GET /sessions/current ──►  Valid cookie?
               ◄── 401 (no session) ──────  → shows the form
               ◄── 200 ───────────────────  → redirects to the landing page
                                              (/overview, or /today for technicians)

2. Submits the form (validated on the client first)
   ── POST /sessions {email, password, rememberMe} ──►
                                            a. Attempt limits (IP, global, email)
                                            b. Validates fields
                                            c. Verifies password (PBKDF2)
                                            d. Active user? Active membership
                                               in an active organization?
                                            e. Picks the organization (the oldest)
                                            f. Saves last_login_at + audit record
   ◄── 200 + Set-Cookie fieldops_session ──
   Stores the session and goes to the landing page (or the saved return URL)

3. /overview
   authGuard ── GET /sessions/current ──►   Revalidates user/membership/organization
             ◄── 200 ─────────────────      → shows "Welcome, {name}"

4. Sign out
   ── DELETE /sessions/current ──►          Deletes the cookie
   ◄── 204 ──  Clears the session and returns to /auth/sign-in
```

## Accepting an invitation

Detail: [`specs/invitation-acceptance/spec.md`](../specs/invitation-acceptance/spec.md).

- The email link is `{frontend origin}/auth/invitation#token={token}`
  (the token lives in the fragment, never in the query or path). Only its
  SHA-256 is stored; resending replaces the token.
- `POST /invitations/validate` (anonymous) returns the public details of the
  invitation, or an identical `410` for any unusable token.
- `POST /invitations/accept` creates the new user and `POST
  /invitations/accept-existing` joins a signed-in user (same email). Both
  do everything in **one transaction**, locking the invitation (`FOR UPDATE`),
  and return the session of the invited organization with a new cookie
  (`rememberMe = false`), issued only after the commit.
- Order of checks: attempt limit, session, body format, usable token (`410`),
  eligibility (`409`). An unusable token never reveals whether an account,
  membership or identity exists.
- Limit: 20 requests per IP every 5 min across the three endpoints.

## Password recovery

Detail: [`specs/password-recovery/spec.md`](../specs/password-recovery/spec.md).

- `POST /password-resets` (anonymous, `{ email }`) always answers an empty,
  neutral `202` (`no-store`): it does not reveal whether the account exists, is
  active, exceeded the per-email limit or whether sending failed. Only an
  `active` user within the limits receives a new token (32 random bytes, only
  its SHA-256 is stored, 30 min, single use). Creating one locks the user row
  (`FOR UPDATE`) and deletes their unused tokens; the email is sent **after**
  the commit through an in-memory queue and a background service (a failure or
  full queue is only logged by category).
- The link is `{frontend origin}/auth/reset-password#token={token}` (in the
  fragment, never in the query or path).
- `POST /password-resets/validate` (`{ token }`) returns `200 { email }` or an
  identical `410` for any unusable token (unknown, expired, used, replaced or
  user no longer `active`).
- `POST /password-resets/confirm` (`{ token, password }`) answers `204`. In
  **one transaction** it locks the token, rechecks it, stores the new PBKDF2
  hash, `password_changed_at` and `used_at`. Whoever loses a race gets `410`.
  It does not change statuses or memberships and does not sign in.
- **Global revocation**: any session with `signed_in_at` earlier than
  `password_changed_at` gets `401` and the cookie is deleted, across all
  organizations. There is no sessions table.
- Limits (in memory): 5 requests per IP every 15 min on `POST
  /password-resets`; 20 per IP every 5 min across validate and confirm; 300 per
  minute globally across the three; 3 per email every 60 min (silent: the
  neutral `202` continues with no token or email).

## Customer portal

The portal uses a **second, independent cookie** (`fieldops_portal_session`, same flags and duration) and the
same `users` table. `/portal/*` endpoints accept only that cookie; internal ones ignore it, and the internal cookie
never authenticates the portal (`401`). Both can coexist in one browser.

| Method | Route | Purpose |
| ------ | ----- | ------- |
| POST | `/portal/sessions` | Sign in (same limits and same generic `401`; picks the oldest active link) |
| GET / DELETE | `/portal/sessions/current` | Current session / sign out (only deletes the portal cookie) |
| POST | `/portal/sessions/current/account` | Switch account between the user's active links |
| POST | `/portal/invitations/validate`, `/accept`, `/accept-existing` | Activate access with the invitation token (identical `410` if unusable) |
| POST | `/portal/password-resets` | Recover the password with a `/portal/reset-password#token=…` link |

- A link is active if `customer_contacts.portal_user_id` is the user and the contact, their customer and their
  organization are active. It is revalidated on every request; if that fails (archived customer, inactive contact,
  access withdrawn by staff, disabled user, inactive organization or password changed after signing in) the response
  is `401` and the cookie is deleted. Changing the password also invalidates the internal session.
- Organization, customer and contact always come from the validated cookie. A foreign id (another customer or another
  organization) answers the same `404` `portal_resource_unavailable`.
- Per-user limits (in memory): requests 5, properties 10, messages 5 and reschedules 5 every 15 min.

## Important rules

| Rule | Detail |
| ---- | ------ |
| Generic error | Unknown email, wrong password, suspended user or no active organization: **always the same `401`**. Nobody can tell whether an email exists. |
| Duration | Without "Remember me": lasts while the browser stays open, at most 8 h. With "Remember me": 14 days without use, at most 30 days in total. |
| Brute-force protection | 10 attempts per IP every 5 min; 300 global per minute; 5 failures per email in 15 min. When exceeded: `429` + `Retry-After`. Counters live in memory (lost on restart). |
| Organization | Never accepted from the client (neither body nor headers). Taken from the validated cookie. |
| Revalidation | If the user, the membership or the organization is deactivated, or if the user changed their password after that session started (`users.password_changed_at` > `signed_in_at`), the next request gets `401` and the cookie is deleted. The role is read from the database on every request. |
| Logs | Emails, passwords, hashes and cookies are never logged. |
| CSRF | `SameSite=Strict` cookie + JSON only + CORS with credentials only for configured origins. |

## Backend files

**`FieldOps.Api`** (HTTP entry point)

| File | Purpose |
| ---- | ------- |
| `Controllers/SessionsController.cs` | The 3 endpoints. Translates results into HTTP responses and sets/deletes the cookie. |
| `Contracts/SignInRequest.cs` | Shape of the JSON received on sign-in. |
| `Authentication/SessionCookie.cs` | Cookie name and durations (8 h, 14 d, 30 d). |
| `Authentication/SessionClaims.cs` | What goes inside the cookie (user, organization, membership, sign-in time, "remember me") and how to read it. |
| `Authentication/SessionCookieEvents.cs` | Revalidates the session on every request and answers `401` without redirects. |
| `Extensions/ApiAuthenticationExtensions.cs` | Configures the cookie (HttpOnly, Secure, SameSite=Strict). |
| `Extensions/ApiRateLimitingExtensions.cs` | Per-IP and global limits. |
| `Middleware/InvalidSessionCookieMiddleware.cs` | Deletes invalid cookies on any endpoint. |
| `Middleware/BadHttpRequestExceptionHandler.cs` | Returns a clean `413` when the body exceeds 4 KB. |
| `Program.cs` | Order: CORS → rate limiter → authentication → cookie cleanup → authorization. |

**`FieldOps.Application`** (logic, no HTTP or database)

| File | Purpose |
| ---- | ------- |
| `Authentication/SignInHandler.cs` | The "sign in" use case: runs steps a–f of the flow. |
| `Authentication/SignInCommandValidator.cs` | Email and password rules with the exact messages. |
| `Authentication/EmailNormalizer.cs` | Trims spaces and lowercases the email. |
| `Authentication/ActiveOrganizationSelector.cs` | Picks the organization: oldest active membership in an active organization. |
| `Authentication/GetCurrentSessionHandler.cs` | Loads the current session (used on every request). |
| `Authentication/SignInResult.cs`, `SessionView.cs`, `SignInCommand.cs`, `MembershipCandidate.cs`, `SignInFailureCategory.cs` | Input/output data of the use case. |
| `Authentication/IPasswordHasher.cs`, `ISignInThrottle.cs`, `IAuthenticationStore.cs` | Interfaces implemented by Infrastructure. |

**`FieldOps.Infrastructure`** (technical details)

| File | Purpose |
| ---- | ------- |
| `Authentication/Pbkdf2PasswordHasher.cs` | Creates and verifies passwords (PBKDF2-SHA256, 600,000 iterations). Uses a fake hash when the email does not exist so response time does not give it away. |
| `Authentication/InMemorySignInThrottle.cs` | Counts failures per email (5 in 15 min). |
| `Persistence/AuthenticationStore.cs` | Database queries: user, memberships, session, and saving `last_login_at` + audit in one transaction. |

**`FieldOps.Domain`**: `User.RecordSignIn` (last sign-in date) and
`AuditLog.Create` (now accepts the IP).

## Frontend files (`frontend/src/app`)

| File | Purpose |
| ---- | ------- |
| `core/services/session.service.ts` | Stores the current session (signal) and calls the 3 endpoints. |
| `core/models/session.model.ts` | Session and sign-in types. |
| `core/interceptors/auth.interceptor.ts` | Adds `withCredentials` to API requests so the cookie travels. |
| `core/guards/auth.guard.ts` | Protects the staff and technician shells: no session → `/auth/sign-in`. Revalidated on every navigation. |
| `core/guards/guest.guard.ts` | If a session exists and you open `/auth/sign-in` → the landing page (`core/config/landing.ts`: `/today` for technicians, `/overview` otherwise). |
| `core/guards/portal.guard.ts`, `core/services/portal-session.service.ts` | Same idea for the customer portal (`/portal/sign-in`, `/portal/sessions/current`). |
| `core/services/api-error.service.ts` | Converts HTTP errors into `ApiError`; reads `Retry-After` on `429`. |
| `app.routes.ts` | `''` and unknown routes → `/auth/sign-in`. |
| `features/authentication/pages/sign-in/` | Sign-in page: form, validation, messages and states. |
| `.../sign-in.validators.ts` / `sign-in.messages.ts` | Form rules and the texts the user sees. |
| `features/authentication/components/sign-in-brand-panel/` | Decorative panel on the left (large screens only). |
| `features/overview/pages/overview/` | Welcome page for roles that do not manage operations: name, organization and role. Owner, Operations Manager and Dispatcher are redirected to `/dashboard`. |

Guards are only user experience; **the backend decides**.

## What the user sees on errors

| Situation | Message |
| --------- | ------- |
| Wrong credentials (`401`) | "The email or password is incorrect…" (clears and focuses the password) |
| Too many attempts (`429`) | "Too many sign-in attempts. Try again in {n} minutes." (button locked for that time) |
| Any other failure | "We couldn't sign you in right now. Try again in a moment." |

Technical backend text is never shown. All messages are localized (English and Spanish).

## Known limitations

- Signing out only deletes the cookie in that browser; there is no
  per-session revocation. Changing the password (recovery) does invalidate all
  of the user's earlier sessions across all organizations.
- Attempt counters and encryption keys (Data Protection) do not persist:
  restarting loses the counters; losing the keys signs everyone out. Solving
  this is deployment work.
- Behind a proxy, the per-IP limit affects everyone at once until forwarded
  headers are configured.
- Locally, the cookie is `Secure` over `http://localhost`: it works in
  Chrome and Firefox and may fail in Safari. Start the API with
  `--launch-profile http`.
