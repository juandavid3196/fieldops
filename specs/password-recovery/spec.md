# Password recovery

| Field    | Value               |
| -------- | ------------------- |
| Feature  | `password-recovery` |
| Type     | Full-stack          |
| Status   | APPROVED            |
| Created  | 2026-09-30          |
| Updated  | 2026-09-30          |
| Approved | 2026-09-30          |

## Context and objective

Users who forget their password have no way back into FieldOps: the Sign In
"Forgot password?" link is inert (sign-in OD-11). This spec adds a secure,
self-service recovery flow: a user requests a one-time link by email, opens
`/auth/reset-password`, chooses a new password and is sent back to Sign In.
Success means the request never reveals whether an account exists, a link
works exactly once within 30 minutes, the password change is atomic, and every
existing session of that user in every organization stops working
immediately. The same work introduces the shared email port with a Resend
production adapter, so all FieldOps email uses one validated provider.

## Actors and permissions

| Actor | Can | Cannot |
| ----- | --- | ------ |
| Anyone (anonymous) | Request a reset link for any email and always receive the same neutral confirmation | Learn whether an account exists, is eligible, was rate limited per email or whether delivery succeeded |
| Holder of a usable reset link | See the account email; set a new password once | Change any other user's password; change status, email, names, memberships or roles; activate a suspended/disabled user or membership |
| Holder of an unusable link | See the generic unavailable-link state | Learn why the link is unusable or which account it belonged to |
| Signed-in user (any organization) | Nothing new; any existing session is ignored on these pages | Use a session to skip the email link |
| System | Normalize emails; generate, hash, replace and consume tokens; dispatch email after the request; update the password and revoke sessions; throttle | Store, log or return a raw token; log emails, passwords, email bodies or API credentials; trust a client user or organization identifier |

No role or organization permission applies: recovery is identity-level and
spans every organization of the user.

## Scope

- Sign In "Forgot password?" becomes a link to `/auth/forgot-password`.
- Anonymous `POST /password-resets`, `POST /password-resets/validate` and
  `POST /password-resets/confirm`.
- Reset tokens (random, hashed, 30-minute, single-use, replaced by a newer
  request) in a new `password_reset_tokens` table.
- Global session revocation through the new `users.password_changed_at`
  column checked by the existing per-request session revalidation.
- Shared `IEmailSender` port with an SMTP adapter (Mailpit in development) and
  a Resend HTTP adapter (production), selected by validated `Email` settings.
  Invitation delivery is moved onto this port with unchanged message content.
- In-process background dispatch of reset emails after the response.
- Rate limits per IP, per normalized email and global.
- Public pages `/auth/forgot-password` and `/auth/reset-password` with
  loading, submitting, neutral confirmation, unavailable-link, field-error,
  generic-error and success states, reusing the authentication layout and
  technician brand panel.

## Non-goals

- Changing a password while signed in, password history, strength meters,
  composition rules, breached-password checks.
- Security questions, social login, magic-link sign-in, MFA, administrator-set
  or administrator-triggered resets.
- Automatic sign-in after reset.
- Email change or email verification flows; `users.email_verified_at` is not
  changed.
- A persistent/outbox email queue, delivery retries or bounce handling.
- Purging old token rows (only unused rows of the requesting user are
  replaced).
- Audit rows for recovery (OD-09).
- Changing invitation, sign-in or onboarding business rules other than the
  items stated here.

## User flow

1. User selects **Forgot password?** on Sign In → `/auth/forgot-password`.
2. User enters an email and selects **Send reset link**.
3. System validates the format and returns the neutral `202`; the page shows
   the neutral confirmation. When the normalized email belongs to an eligible
   user and no limit applies, the system replaces any unused token, stores a
   new token hash and, after responding, emails
   `{frontend origin}/auth/reset-password#token={token}`.
4. User opens the link. The page moves the token from the fragment to
   `sessionStorage`, clears the URL and validates the token.
5. Usable → "Create a new password" form showing the account email. Unusable →
   unavailable-link state with **Request a new link**.
6. User enters and confirms a new password and selects **Reset password**.
7. System, in one transaction, consumes the token, stores the new hash and
   sets `password_changed_at`; all earlier sessions of the user become invalid.
8. Page shows "Password updated"; **Continue to sign in** opens
   `/auth/sign-in`.

## Functional requirements

| ID | Requirement |
| -- | ----------- |
| FR-01 | The Sign In "Forgot password?" control must navigate to `/auth/forgot-password`. |
| FR-02 | `POST /password-resets` must validate the email (BR-05) and otherwise return the identical neutral `202` (BR-06) for every outcome; only for an eligible user within limits (BR-04, BR-12) it must replace unused tokens and create a new one (BR-01 to BR-03). |
| FR-03 | For each created token the system must send, after the response and through `IEmailSender`, one reset email (BR-10, BR-11); delivery outcome must never change the response. |
| FR-04 | `POST /password-resets/validate` must return the account email for a usable token (BR-07) and the identical `410` for any unusable token (BR-08). |
| FR-05 | `POST /password-resets/confirm` must atomically consume a usable token, store the new password hash and set `users.password_changed_at` (BR-09, BR-14), leaving statuses, memberships and all other data unchanged, and allow only one success per token under concurrency. |
| FR-06 | Session revalidation must reject every session whose sign-in instant precedes the user's `password_changed_at` (BR-15), in all organizations. |
| FR-07 | The three endpoints must apply BR-12 rate limits, the BR-13 check order and the BR-16 error, caching and logging rules. |
| FR-08 | The backend must provide the `IEmailSender` port with SMTP and Resend adapters selected and validated per BR-17, use it for invitation delivery with unchanged content and failure behavior, and keep Mailpit as the development transport. |
| FR-09 | `/auth/forgot-password` must show the request form, submitting, neutral confirmation, field-error and page-error states (BR-18, BR-19). |
| FR-10 | `/auth/reset-password` must capture and handle the token per BR-20 before any request. |
| FR-11 | `/auth/reset-password` must show loading, unavailable-link and load-error states before any form (BR-21). |
| FR-12 | The reset form must enforce BR-09 and the confirm-password rule, submit once, and on success show the "Password updated" state leading to Sign In (BR-22). |
| FR-13 | Reset responses must map to BR-21/BR-22 field, unavailable and page messages without showing backend text. |
| FR-14 | Both pages must follow the approved designs, the authentication layout and the responsive/accessibility rules of UI behavior. |
| FR-15 | The BR-14 schema amendment must be applied to `docs/database/fieldops-schema.sql`, the EF model and one generated migration that agents never apply. |

## Business and validation rules

| ID | Rule | Enforced by |
| -- | ---- | ----------- |
| BR-01 | Token: 32 bytes from a cryptographically secure generator, URL-safe base64 without padding (43 characters) — the existing invitation token generator. Only its SHA-256 hash is stored in `password_reset_tokens.token_hash`. The raw token exists only in the email and the client. | Backend |
| BR-02 | Lifetime: `expires_at = created_at + 30 minutes` (injected clock). Usable token: hash matches, `used_at` null, `expires_at > now`, and the user's `status = active`. | Backend |
| BR-03 | Replacement: a new token for a user is created in one transaction that locks the `users` row (`FOR UPDATE`), deletes that user's rows with `used_at` null (expired or not) and inserts the new row. At most one unused token per user exists (BR-14 partial unique index). Consumed rows are kept. | Backend |
| BR-04 | Eligible account: a `users` row whose `email` equals the normalized request email and whose `status = active`, regardless of memberships. `pending`, `suspended`, `disabled` and unknown emails produce no token and no email. | Backend |
| BR-05 | Request `email`: trimmed and lowercased first (sign-in BR-01 normalization); required; ≤254; sign-in BR-01 format. Messages (sign-in BR-03): empty → "Enter your email address."; too long or invalid → "Enter a valid email address, for example name@company.com.". Server `400` ValidationProblemDetails key `email`; malformed JSON, empty or `null` body → `400` without keys. | Both |
| BR-06 | Neutral response: `202 Accepted`, empty body, `Cache-Control: no-store`, identical status, headers (apart from tracing) and body for eligible, ineligible, unknown, per-email-limited and delivery-failing requests. Token persistence happens before the response; email sending never does. | Backend |
| BR-07 | Validate `200` body: `{ email }` (the account's stored email). No ids, names, organizations, expiry or other data. | Backend |
| BR-08 | Unusable token (unknown, expired, consumed, replaced, or user no longer `active`) → `410` ProblemDetails with an identical body (same `type`, `title`, `status`, no `detail`, no `errors`; only `traceId` differs) on validate and confirm. Request `token`: required string of exactly 43 characters from `A–Z a–z 0–9 - _`, otherwise `400` whose only key is `token` with "Enter a valid value." (never echoed) — invitation BR-03. | Backend |
| BR-09 | New `password`: not trimmed; required; 12–128 UTF-16 code units; not equal to the account's normalized email ignoring case. Messages: "This field is required." / "Use 12 to 128 characters." / "Choose a password that is different from your email." (onboarding BR-17). `confirmPassword` is client-only: required, equal to `password` — "This field is required." / "Passwords don't match.". Server `400` key `password`. | Both |
| BR-10 | Reset link: `{first configured frontend origin}/auth/reset-password#token={token}` (invitation AS-05 origin rule); never in a query string or path. | Backend |
| BR-11 | Reset email: to the user's stored email; subject "Reset your FieldOps password"; plain-text and HTML bodies with the user's first name, a **Reset password** link/button with the BR-10 link, "This link expires in 30 minutes and can only be used once." and "If you didn't request a password reset, you can ignore this email. Your password won't change."; no other data. Dispatch: an in-memory bounded queue drained by a hosted background service after the creating transaction commits. A send failure or a full queue is logged by category only and drops the message; the token stays valid until replaced or expired. Pending messages are lost on restart (accepted; the user can request again). | Backend |
| BR-12 | Rate limits (in memory, like sign-in BR-11; every request counts whatever its outcome): `POST /password-resets` 5 per client IP per 15-minute fixed window → `429` ProblemDetails with `Retry-After`; per normalized email 3 per 60-minute window, counted for every well-formed email whether or not an account exists, and when exceeded the request returns the BR-06 neutral `202` without creating a token or sending email; validate + confirm together 20 per client IP per 5-minute window → `429`; global 300 per minute across the three endpoints → `429`. The per-email key is never logged. Sign-in and invitation limits are unchanged. | Backend |
| BR-13 | Check order, first failing check wins. Request: 1. IP/global rate limit (`429`); 2. content type, size, body/email validation (`415`, `413`, `400`); 3. per-email limit, eligibility, token creation (always `202`). Validate/confirm: 1. rate limit (`429`); 2. content type, size, `token` format and `password` required/length (`415`, `413`, `400`); 3. usable token (`410`, BR-02); 4. confirm only, inside the transaction under the BR-14 lock: token re-checked (`410`), password ≠ email (`400` `password`). An unusable token never yields the email rule. | Backend |
| BR-14 | Schema amendment (approved by the user 2026-09-30, OD-01): `users` + `password_changed_at timestamptz` (nullable, no default); new table `CREATE TABLE password_reset_tokens (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE, token_hash text NOT NULL UNIQUE, expires_at timestamptz NOT NULL, used_at timestamptz, created_at timestamptz NOT NULL DEFAULT now(), CONSTRAINT ck_password_reset_tokens_expires_after_created CHECK (expires_at > created_at))` and `CREATE UNIQUE INDEX ux_password_reset_tokens_open_user ON password_reset_tokens(user_id) WHERE used_at IS NULL`. No other table changes; existing users keep `password_changed_at` null. Confirm writes, in one transaction with the token row locked (`SELECT … FOR UPDATE`): `password_reset_tokens.used_at = now`; `users.password_hash` (onboarding BR-20 PBKDF2 format), `users.password_changed_at = now`, `users.updated_at = now`. `users.status`, `organization_users` and all other rows are unchanged. The loser of a race and any later attempt get `410`. | Backend |
| BR-15 | Session revocation: during the existing per-request revalidation (sign-in FR-07), a session whose `fieldops:signed_in_at` is earlier than the user's non-null `password_changed_at` is invalid → `401` and the cookie is cleared (existing invalid-session behavior). Sessions issued later (sign-in, invitation acceptance) are unaffected. Cookie renewal keeps the original `signed_in_at`. No session table is introduced. | Backend |
| BR-16 | Errors never contain the token, hash, email, password or exception text; `500` is the generic ProblemDetails; all responses carry `Cache-Control: no-store`. Logs never contain raw tokens, hashes, emails, passwords, request bodies, complete email bodies, the Resend API key or SMTP credentials; delivery failures log the provider and failure category only (for Resend the HTTP status code), optionally the user id. | Backend |
| BR-17 | Email settings (validated on start; errors name the key, never the value): `Email:Provider` required, `Smtp` or `Resend`; `Email:SenderAddress` a valid email address (its domain is the sender domain) and `Email:SenderName` required, both moved from `Email:Smtp`; `Email:Smtp` keeps `Host`, `Port`, `EnableSsl`, optional `UserName`/`Password` (invitation BR-15), required only when the provider is `Smtp`; `Email:Resend:ApiKey` required when the provider is `Resend`, supplied only through environment variables or user-secrets, never committed. In the Production environment any provider other than `Resend` fails start. Development settings use `Smtp` with Mailpit (`localhost:1025`). The Resend adapter posts to the Resend email API over HTTPS with `HttpClient` and a bearer key (no new package); a non-success response or transport error throws. `IInvitationDelivery` keeps its contract and composes the unchanged invitation BR-15 message through `IEmailSender`; an invitation send failure still yields design-18 `502` with rollback. | Backend |
| BR-18 | Forgot-password page copy (design-1): **Back to sign in** link; `h1` "Reset your password"; subtitle "Enter the email you use for FieldOps. If an account exists, we'll send a secure reset link."; "Email address" field (`autocomplete="email"`, placeholder as design); **Send reset link**; note "For your security, we won't confirm whether an account exists."; "Remember your password? **Sign in**". Neutral confirmation (OD-07a, same card, decorative mail icon): `h1` "Check your email"; "If an account exists for {email}, we've sent a link to reset your password. It expires in 30 minutes." (`{email}` = the normalized submitted value); **Back to sign in** button; "Use a different email" link returning to the form with the email kept and focused. | Frontend |
| BR-19 | Forgot-password responses: `202` → neutral confirmation; `400` `email` → BR-05 field message; `400` other or no keys, `413`, `415`, `5xx`, network → "We couldn't send the reset link right now. Try again in a moment."; `429` → "Too many attempts. Try again in {n} minutes." (`n` = `retryAfterSeconds`/60 rounded up, 60 s when absent, "1 minute" singular) with submit disabled for that period. Submitting: button spinner "Sending…", input locked, repeat submits ignored. | Frontend |
| BR-20 | Token handling (invitation BR-16 pattern): on load, a `token` in the URL fragment is written to `sessionStorage` and the URL is replaced with `/auth/reset-password` (no fragment, no query) before any request. A `token` query parameter is ignored and removed. The token is read only from `sessionStorage`, sent only in POST bodies, and removed on confirm success, on `410`, on `400` with only `token`, and when leaving through **Back to sign in** or **Request a new link**; kept on `429`, `5xx` and network errors. No token → unavailable-link state without a request. | Frontend |
| BR-21 | Reset page states. Loading: card skeleton while validating, no email shown. Unavailable-link (OD-07b; validate/confirm `410`, `400` with only `token`, missing token; design-26 invitation-expired layout, decorative amber icon): **Back to sign in** link; `h1` "This reset link is no longer available"; body "Request a new one."; **Request a new link** button → `/auth/forgot-password`. Load error (validate `5xx`, network, other): "We couldn't load this reset link. Check your connection and try again." + **Retry** re-validating with the stored token; validate `429`: the same state with "Too many attempts. Try again in {n} minutes." and Retry disabled for the period. | Frontend |
| BR-22 | Reset form (design-2): **Back to sign in** link; `h1` "Create a new password"; "Choose a new password for {email}."; Password and Confirm password (`autocomplete="new-password"`); one **Show password** checkbox controlling both fields (no per-field eye icons); "Your password must:" checklist "12–128 characters" and "Must not match your email" with met/not-met icon plus text state; **Reset password**; note "This reset link can only be used once and expires in 30 minutes.". Submitting: spinner "Resetting…", inputs locked, repeats ignored. Confirm `204` → token removed, "Password updated" state (design-3): `h1` "Password updated"; "Your password has been changed successfully. You can now sign in with your new password."; **Continue to sign in** → `/auth/sign-in` replacing history; note "For your protection, your other active sessions have been signed out.". Confirm errors: `400` `password` → field message ("Enter a valid value." for any other server field message); `400` no keys, `413`, `415`, `5xx`, network → "We couldn't reset your password right now. Try again in a moment."; `429` → BR-19 copy with submit disabled; `410`/`400` only `token` → unavailable-link state. Input is kept except on success and unavailable. | Frontend |

## States and transitions

| From | To | Trigger | Actor | Guard |
| ---- | -- | ------- | ----- | ----- |
| No open token / open token T1 | Open token T2 (T1 row deleted) | Request | Anonymous | BR-04 eligible, BR-12 within limits |
| Open token | Expired (unusable) | `expires_at` passes | System | Read-time check; no write |
| Open token | Consumed (`used_at` set) | Confirm | Link holder | BR-02 and BR-09 under the BR-14 lock |
| User sessions signed in before T | Invalid (`401`) | Confirm at T sets `password_changed_at` | System | BR-15 |
| User `active` | User `active` (password changed) | Confirm | Link holder | Status never changes |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`):
  - `users`: read `id`, `email`, `first_name`, `status`, `password_changed_at`;
    lock for update; write `password_hash`, `password_changed_at`,
    `updated_at`.
  - `password_reset_tokens` (new): insert, delete unused rows of the user,
    read by `token_hash`, lock, write `used_at`.
  - `organization_users`, `organizations`: unchanged; read only by the
    existing session revalidation.
- Constraints relied on: `users(email)` unique; `password_reset_tokens.token_hash`
  unique; `ux_password_reset_tokens_open_user`; expiry check constraint.
- Schema amendments required: BR-14 — **approved by the user 2026-09-30**
  (OD-01). Inspection confirmed neither the table nor the column exists.
  Implementation updates `docs/database/fieldops-schema.sql` (column inside
  `CREATE TABLE users`, the new table after `invitation_branches`, the index
  with the other indexes), the EF model and generates one migration under
  `--generate-migration`; agents never apply it. Persistence changes require
  `database-reviewer`.
- Domain changes: reset-token creation/consumption; `User` password change
  recording `password_changed_at`.

## Tenant isolation and authorization

- Organization context: none is resolved or accepted. Endpoints accept only
  `email`, `token` and `password`; unknown body properties are ignored. No
  user, organization or membership identifier is accepted from the client.
- Possession of a usable token is the only authorization for validate and
  confirm, and it reveals only the BR-07 email of that token's user.
- The request endpoint's response is independent of account existence,
  status, per-email limit and delivery (BR-06, BR-12, BR-13).
- Scope of effect: the password is identity-level; session revocation applies
  to all of the user's sessions in every organization. Other users, their
  sessions and all organization data are unchanged.
- Any session cookie sent to these endpoints is ignored; the pages are public
  (OD-08).
- CSRF: existing `SameSite=Strict` cookie, JSON only, CORS allow-list; the
  endpoints use no cookie.

## API contracts

Contract status: Final. Paths follow the existing no-prefix routing. All
requests are `application/json`, body ≤4 KB (`413`/`415` as sign-in FR-03).
Errors follow the BR-13 order. All responses `Cache-Control: no-store`.

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
| POST | `/password-resets` | `{ email }` | `202` empty body (BR-06) | `400` (`email` or no keys) · `413` · `415` · `429` + `Retry-After` · `500` | Anonymous |
| POST | `/password-resets/validate` | `{ token }` | `200 { email }` | `400` (`token` only) · `410` (BR-08) · `413` · `415` · `429` + `Retry-After` · `500` | Anonymous |
| POST | `/password-resets/confirm` | `{ token, password }` | `204` | `400` (`token` / `password` or no keys) · `410` · `413` · `415` · `429` + `Retry-After` · `500` (rolled back) | Anonymous |

- New internal contract: `IEmailSender` (Application) sending one message
  with recipient, subject, plain-text and HTML bodies; implemented by the SMTP
  and Resend adapters. `IInvitationDelivery` is unchanged for callers.
- CORS: existing credentials policy; `Retry-After` already exposed.

## UI behavior and states

Design references (approved by the user):

- `design/assets/design-1.png` — forgot-password form.
- `design/assets/design-2.png` — create a new password.
- `design/assets/design-3.png` — password updated.
- `c:\Users\usuario\Desktop\portafolio\Fieldops-docs\diseños\acceptance invitation\26-invitation-expired.png` — layout for the unavailable-link state (copy replaced by BR-21, OD-07b).
- Neutral confirmation has no mockup; it uses the design-1 card with BR-18 copy (OD-07a).

Layout: the existing authentication split screen, breakpoints and text
wordmark, with the existing technician brand panel ("Your team, connected
from day one.") used by `/auth/invitation`. Documented deviations: text
wordmark instead of the gear logo; one **Show password** checkbox instead of
per-field eye icons (design-2).

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
| `/auth/forgot-password` | N/A | Empty form | BR-05 field messages; BR-19 page messages in a message slot above the button; `429` disables submit | Public; session ignored | Submitting per BR-19; then neutral confirmation (BR-18) |
| `/auth/reset-password` | Card skeleton while validating | No token → unavailable-link state | Unavailable-link, load error + Retry, validate `429` (BR-21); field and page messages (BR-22) | Public; session ignored | Submitting per BR-22; "Password updated" → **Continue to sign in** |
| `/auth/sign-in` | Unchanged | Unchanged | Unchanged | Unchanged | "Forgot password?" navigates to `/auth/forgot-password` |

- Validation: on blur after first interaction and on submit; invalid forms are
  never sent; Enter submits.
- Responsive (sign-in conventions, `md` 48rem, `lg` 64rem): from `lg` split
  screen with the brand panel; below `lg` panel hidden and wordmark above the
  card; below `md` one column, full-width card and controls, checklist items
  stacked, touch targets ≥44px, no horizontal scroll at 320px.
- Accessibility: one `h1` per state; decorative icons `aria-hidden`; checklist
  state conveyed by text, not color only; **Show password** is a labelled
  checkbox; invalid fields `aria-invalid` + `aria-describedby`; page messages
  `role="alert"`; loading and neutral confirmation announced politely; focus
  moves to the `h1` on each state change and to the first invalid field on
  submit; visible preset focus ring.
- Styling: existing `--fo-*`/Aura teal tokens and PrimeNG input, password,
  checkbox, button, message and skeleton components; no new package.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
| Unknown, ineligible, per-email-limited email | `202`; neutral confirmation | None |
| Eligible email, delivery fails or queue full | `202`; neutral confirmation; failure logged by category | Token created (usable until replaced/expired) |
| Invalid email | `400` `email`; BR-05 field message | None |
| Request / reset rate limit | `429` + `Retry-After`; BR-19 copy; submit or Retry disabled for the period | None |
| Missing/malformed token | Unavailable-link state (`400` only `token`, or no request) | None |
| Unknown, expired, consumed, replaced token or user no longer active | `410` identical body; unavailable-link state; token removed | None |
| Password too short/long or equal to email | `400` `password`; field message; token still usable | None |
| Concurrent confirms | First `204`; others `410` | One consumption, one hash |
| Failure inside the confirm transaction | `500` generic; generic reset message | Fully rolled back; token still usable |
| Invalid email settings at start | Application fails to start; message names the key only | None |
| Invitation send failure | Design-18 `502` (unchanged) | Rolled back |

## Acceptance criteria

| ID | Given | When | Then |
| -- | ----- | ---- | ---- |
| AC-01 | An active user and a request with `"  Alex@Example.COM "` | `POST /password-resets` | `202` empty body with `no-store`; after dispatch the recording sender holds one BR-11 message to the stored email whose link is `{origin}/auth/reset-password#token={43-char token}` without query; one unused row stores the SHA-256 of that token with `expires_at = created_at + 30 min`; no raw token is persisted |
| AC-02 | An unknown email; users with status `pending`, `suspended`, `disabled`; an eligible email over its per-email limit; an eligible email whose sender throws (parameterized) | `POST /password-resets` | Each response is identical to AC-01's (status, body, headers apart from tracing); no token and no message for the unknown, ineligible and limited cases; user rows unchanged |
| AC-03 | Empty, too long and malformed emails, and malformed JSON (parameterized) | `POST /password-resets` | `400` with key `email` and the BR-05 message, or `400` without keys; nothing changes |
| AC-04 | An eligible user requests twice | Both tokens are posted to validate and confirm | The first returns `410` and the second is usable; exactly one unused row exists for the user |
| AC-05 | A usable token | `POST /password-resets/validate` | `200 { email }` only, `no-store`; the token remains usable |
| AC-06 | Tokens that are unknown, expired (clock +30 min), consumed, replaced, and of a user suspended after the request; and a malformed token (parameterized; confirm with a valid password) | Each is posted to validate and confirm | Unusable ones return `410` bodies identical apart from `traceId`; malformed returns `400` whose only key is `token`; no response contains the token or email; nothing changes |
| AC-07 | A usable token for an active user with one active and one suspended membership | `POST /password-resets/confirm` with a valid new password | `204`; the stored hash is BR-20 PBKDF2 and verifies the new password; `used_at`, `password_changed_at` and `updated_at` are set; user status and both membership statuses are unchanged; `POST /sessions` fails with the old and succeeds with the new password |
| AC-08 | The user holds session cookies for organizations A and B, and another user holds a session | The user confirms a reset | Both of the user's cookies get `401` on `GET /sessions/current` and are cleared; the other user's session still returns `200`; a sign-in after the reset yields a working session |
| AC-09 | A usable token with passwords of 11 and 129 characters, empty, and equal to the account email in different case (parameterized) | `POST /password-resets/confirm` | `400` with key `password` and the BR-09 message; the password, token and sessions are unchanged |
| AC-10 | One usable token | Two confirms with different passwords run concurrently, then a third follows | Exactly one returns `204` and the others `410`; the stored hash matches the winner's password; one row has `used_at` |
| AC-11 | A failure is forced inside the confirm transaction | Confirm runs | `500` generic ProblemDetails; password hash, `password_changed_at` and `used_at` unchanged; existing sessions still valid; the token still validates |
| AC-12 | One IP with 5 requests in 15 minutes; one IP with 20 validate/confirm requests in 5 minutes (parameterized) | It sends one more | `429` with `Retry-After` and CORS headers; no token created or consumed |
| AC-13 | Successful, neutral, failed-delivery, invalid and rate-limited requests across the three endpoints | Logs and error bodies are inspected | None contain a raw token, token hash, email, password, request body, email body or API key |
| AC-14 | `Email` settings missing the provider, with an unknown provider, an invalid sender address, `Resend` without `ApiKey`, `Smtp` without host, and `Smtp` in Production (parameterized), and valid `Smtp`/`Resend` settings | Options validation runs and the provider is built | Invalid settings fail on start naming only the key; valid settings resolve `IEmailSender` to the SMTP or Resend adapter; `IInvitationDelivery` resolves and sends through `IEmailSender` |
| AC-15 | The Resend adapter with a fake HTTP handler returning success, then `4xx`/`5xx` | A message is sent | Success posts one HTTPS request with the bearer key, configured sender, recipient, subject, text and HTML; failures throw and log only provider and status code |
| AC-16 | An Owner invites and resends with the recording sender, and with a sender that throws | The invitation endpoints run | Messages keep the invitation BR-15 subject, content and link; a throwing sender still yields `502` with no invitation change |
| AC-17 | Development settings with Mailpit (USER check) | A reset is requested for an eligible account and the link is opened | Mailpit shows one BR-11 email whose **Reset password** link opens `/auth/reset-password` and leads to a successful reset |
| AC-18 | The Sign In page and the forgot-password page | The user selects **Forgot password?**, submits invalid then valid email, and receives `202`, `429` with `Retry-After: 90` and `500` (parameterized) | Navigation reaches `/auth/forgot-password`; BR-05 field messages; `202` shows the BR-18 confirmation with the normalized email and "Use a different email" restores the form; `429` shows "Try again in 2 minutes." with submit disabled for 90 s; `500` shows the BR-19 generic message; submitting shows "Sending…" and ignores repeats |
| AC-19 | The reset page opened with `#token=abc…`, with `?token=abc…`, and with no token (parameterized) | It initializes | Fragment: token in `sessionStorage` and URL `/auth/reset-password` before the validate request, token only in POST bodies; query: ignored, removed, unavailable-link state; none: unavailable-link state with no request |
| AC-20 | Validate pending, `200`, `410`, `400` with only `token`, `429` with `Retry-After: 90`, `500`/network (parameterized) | The page renders | Pending: skeleton without email; `200`: form with "Choose a new password for {email}."; `410`/`400`: BR-21 unavailable-link state, token removed, **Request a new link** opens `/auth/forgot-password`; `429`: load error with Retry disabled 90 s, token kept; `500`: load error whose Retry re-validates |
| AC-21 | A validated reset form | The user types, toggles **Show password**, submits mismatched then valid passwords and receives `204` | Checklist and field messages follow BR-09; Show password reveals both fields; mismatch shows "Passwords don't match." and sends nothing; submitting shows "Resetting…" and ignores repeats; `204` removes the token and shows "Password updated" whose **Continue to sign in** navigates to `/auth/sign-in` replacing history |
| AC-22 | Confirm responses `400` with `password`, with an unknown key, with only `token`, without keys, `410`, `429` with `Retry-After: 90`, `500`, network (parameterized) | The page maps them | Per BR-22: field message, "Enter a valid value.", unavailable-link (token removed), generic message, unavailable-link, "2 minutes" with submit disabled 90 s, generic, generic; no backend text; input and token kept except on unavailable |
| AC-23 | Viewports 1280, 800, 390 and 320px (USER check) | Every state of both pages renders | 1280: split screen with the technician panel; 800: panel hidden, wordmark above the card; 390: one column, full-width controls, stacked checklist, ≥44px targets; 320: no horizontal scroll |
| AC-24 | The implementation beside the approved designs at desktop width (USER VISUAL QA REPORT) | They are compared and a keyboard pass runs | Structure, spacing, tokens, icons and copy match design-1/2/3 and the invitation-expired layout except the documented deviations; focus order, visible focus, labels, alerts and focus moves follow UI behavior |

## Testing requirements

24 active ACs. TARGETED Full-stack. Reuse the API test factory, the
users/sign-in/session helpers and the injected `TimeProvider`; tests replace
`IEmailSender` with a recording sender (and a throwing variant). Agents run no
browser automation.

| Level | Behavior or risk to prove | Evidence for |
| ----- | ------------------------- | ------------ |
| Backend unit | (1) Email settings validation table and adapter/invitation registration; (2) Resend adapter request shape and failure mapping with a fake handler. 2 methods | AC-14, AC-15 |
| Backend integration | (1) Request happy path and neutral-response table incl. delivery failure; (2) request validation, IP rate limits and no-secret logging; (3) replacement and unusable-token table incl. expiry and suspended user, malformed token; (4) confirm happy path incl. unchanged statuses and old/new sign-in; (5) global session revocation across organizations; (6) password rule table; (7) concurrent/double consumption and forced rollback; (8) invitation delivery through `IEmailSender` incl. `502`. ≤8 methods | AC-01 to AC-13, AC-16 |
| Authorization/tenant isolation | No account-existence leak (within (1)); token-only authorization and identical `410` (within (3)); revocation limited to the user, across organizations (within (5)) | AC-02, AC-06, AC-08 |
| Frontend component/service | (1) Sign In link and forgot page states/messages; (2) token capture/URL replacement/query ignore/no token; (3) validate states incl. unavailable and retry; (4) reset form rules, show password, submit and success; (5) confirm response mapping table. 5 methods (extend the existing Sign In spec for the link) | AC-18 to AC-22 |
| Manual (USER) | Mailpit end-to-end; responsive; visual fidelity and keyboard pass (USER VISUAL QA REPORT) | AC-17, AC-23, AC-24 |

## Dependencies

- `specs/sign-in/spec.md` (AUDITED): cookie contract, `fieldops:signed_in_at`,
  per-request revalidation, email normalization/messages, layout. This spec
  supersedes its OD-11 (Forgot password link) and the "no revocation on
  password change" limitation.
- `specs/invitation-acceptance/spec.md` (APPROVED): token format, fragment
  handling pattern, brand panel, invitation-expired layout, SMTP settings and
  Mailpit. This spec supersedes the transport parts of its FR-01, BR-15 and
  AC-25 (SMTP as the only production adapter, `Email:Smtp:Sender*` keys);
  invitation message content and failure behavior are unchanged.
- `specs/organization-onboarding/spec.md`: password rules (BR-17), hash format
  (BR-20).
- `design/assets/design-1.png` to `design-3.png` are currently staged but not
  committed; they must be committed with this spec.
- Documentation updated with the implementation: `docs/authentication.md`
  (recovery flow, revocation, removed limitation),
  `docs/backend/api-configuration.md` (endpoints, rate limits, `Email`
  settings, Resend key via environment), `docs/database/fieldops-schema.sql`
  (BR-14).
- Classification for `spec-impl`: Backend TARGETED (new anonymous endpoints,
  email port and Resend adapter, background dispatch, transaction/lock,
  session revalidation change, schema amendment; migration requires
  `--generate-migration`, `database-reviewer` required); Frontend TARGETED
  (two new public routes, token storage, multi-state pages).

## Assumptions

| ID | Assumption (minor, non-behavioral) |
| -- | ---------------------------------- |
| AS-01 | Page-error copy not shown in the designs (BR-19 generic, BR-21 load error, BR-22 generic) follows the sign-in/invitation wording pattern. |
| AS-02 | Residual timing difference from the token insert for eligible users is accepted; email sending, the dominant cost, is outside the request (OD-03). |
| AS-03 | The "other active sessions" wording of design-3 is kept although the browser's own session, if any, is also revoked. |
| AS-04 | The reset link uses the first configured CORS origin, as the invitation link builder does. |
| AS-05 | Old consumed and expired token rows remain until a later request by the same user replaces unused ones; retention cleanup is future work. |

## Open decisions

| ID | Question | Options | Blocking | Resolution |
| -- | -------- | ------- | -------- | ---------- |
| OD-01 | Global session revocation without a session table | `users.password_changed_at` vs `signed_in_at` / security stamp / session table | Yes | `users.password_changed_at` compared with `signed_in_at` (2026-09-30) |
| OD-02 | Email provider (no Resend adapter existed) | Shared `IEmailSender` + SMTP/Resend, invitations migrated / recovery only / separate infrastructure spec | Yes | Shared port, SMTP + Resend adapters, invitations moved onto it (2026-09-30) |
| OD-03 | Timing and delivery-failure leakage | Background dispatch after response / synchronous with swallowed failures | Yes | In-memory background dispatch (2026-09-30) |
| OD-04 | Eligible account | `status = active` only / any except `disabled` | Yes | `active` only, memberships irrelevant (2026-09-30) |
| OD-05 | Rate limits | BR-12 values with silent per-email limit / other values or `429` per email | Yes | BR-12 values; per-email limit returns neutral `202` (2026-09-30) |
| OD-06 | API contract | Three POST endpoints (`202` / `200 {email}` / `204`) / other | Yes | Three POST endpoints as specified (2026-09-30) |
| OD-07 | Copy without a mockup | a) neutral confirmation; b) unavailable-link state | Yes | Both texts approved as proposed (2026-09-30) |
| OD-08 | Existing session on recovery routes | Public, session ignored / `guestGuard` | Yes | Public, session ignored; the reset also revokes it (2026-09-30) |
| OD-09 | Audit (organization-scoped `audit_logs`) | No audit row, structured log only / one row per active membership | Yes | No audit row (2026-09-30) |

## Traceability

| FR | AC |
| -- | -- |
| FR-01 | AC-18 |
| FR-02 | AC-01, AC-02, AC-03, AC-04 |
| FR-03 | AC-01, AC-02, AC-17 |
| FR-04 | AC-05, AC-06 |
| FR-05 | AC-07, AC-09, AC-10, AC-11 |
| FR-06 | AC-08 |
| FR-07 | AC-06, AC-12, AC-13 |
| FR-08 | AC-14, AC-15, AC-16, AC-17 |
| FR-09 | AC-18 |
| FR-10 | AC-19 |
| FR-11 | AC-20 |
| FR-12 | AC-21 |
| FR-13 | AC-22 |
| FR-14 | AC-23, AC-24 |
| FR-15 | AC-04, AC-07 |

## Change log

| Date       | Status change | Reason |
| ---------- | ------------- | ------ |
| 2026-09-30 | — → DRAFT     | Created; decisions OD-01 to OD-09 answered by the user (1a–9a, 7a and 7b copy approved) |
| 2026-09-30 | DRAFT → APPROVED | Approved by user via /spec approve |
