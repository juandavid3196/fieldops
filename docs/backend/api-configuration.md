# API configuration

Foundational runtime configuration for `FieldOps.Api`.

## Required settings

The API refuses to start when any required setting is missing or invalid.

| Key | Environment variable | Notes |
| --- | --- | --- |
| `ConnectionStrings:FieldOpsDatabase` | `ConnectionStrings__FieldOpsDatabase` | PostgreSQL connection string. Never committed. |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... | Absolute `http`/`https` origins without path, trailing slash or wildcard. |
| `Email:Provider` | `Email__Provider` | `Smtp` or `Resend`. Any provider other than `Resend` fails start in Production. Validated on start; errors name the key, never the value. |
| `Email:SenderAddress` (valid email), `Email:SenderName` | `Email__SenderAddress`, `Email__SenderName` | Sender for every email (invitations and password recovery). |
| `Email:Smtp:Host`, `Port` (1-65535) | `Email__Smtp__Host`, ... | Required only when the provider is `Smtp`. |
| `Email:Resend:ApiKey` | `Email__Resend__ApiKey` | Required when the provider is `Resend`. Environment variables or user-secrets only, never committed. |
| `Email:Smtp:EnableSsl` | `Email__Smtp__EnableSsl` | `false` when absent (`Smtp` provider only). |
| `Email:Smtp:UserName`, `Password` | `Email__Smtp__UserName`, `Email__Smtp__Password` | Optional; set together; user-secrets or environment only, never committed. |

`appsettings.Development.json` allows `http://localhost:4200` (Angular dev
server) and selects the `Smtp` provider at `localhost:1025` without SSL (Mailpit, see below).
`appsettings.json` ships with an empty origin list and only `Email:SenderName`, so
every other environment must provide them explicitly.

### Local email capture (Mailpit)

`docker-compose.yml` runs `mailpit` (pinned image): SMTP on `127.0.0.1:1025`,
web UI on `http://127.0.0.1:8025`. Start it with
`docker compose up -d mailpit`; invitations and password reset emails sent by the API appear in the UI.
Integration tests never use it (they replace the delivery port).

## Local secrets

The connection string is not stored in any `appsettings*.json` file. For local
development use user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:FieldOpsDatabase" \
  "Host=localhost;Port=5432;Database=fieldops;Username=<user>;Password=<password>" \
  --project src/FieldOps.Api
```

Values must match the PostgreSQL container settings in the repository `.env`
file (see `.env.example`). CI and deployed environments use environment
variables.

## Endpoints

| Endpoint | Environments | Purpose |
| --- | --- | --- |
| `GET /health` | All | Same as `/health/ready`. Kept for frontend compatibility (`ApiHealthService`). |
| `GET /health/live` | All | Liveness: `self` check only; never touches dependencies. Always `200` while the process runs. |
| `GET /health/ready` | All | Readiness: every registered check, including `postgresql`. `200` when healthy, `503` otherwise. |
| `GET /openapi/v1.json` | Development | OpenAPI document. |
| `GET /swagger` | Development | Swagger UI over the OpenAPI document. |
| `POST /sessions` | All | Sign in (anonymous). `application/json` only, body ≤ 4 KB, rate limited. `200` session body + `Set-Cookie: fieldops_session`, `Cache-Control: no-store`. |
| `GET /sessions/current` | All | Current session (`[Authorize]`). `200` session body or `401`; both `Cache-Control: no-store`. |
| `DELETE /sessions/current` | All | Sign out (anonymous). Always `204` with a `Set-Cookie` deleting `fieldops_session`. |
| `POST /invitations/validate` | All | Invitation details for a usable token (anonymous). Body `{ token }`, ≤ 4 KB. `200` `{ organizationName, inviterName, email, firstName, lastName, role: { code, name }, isAllBranches, branches: [{ name }], expiresAt }`. |
| `POST /invitations/accept` | All | Accept as a new user (anonymous). Body `{ token, firstName, lastName, password }`. `200` session body + `Set-Cookie` for the invited organization. |
| `POST /invitations/accept-existing` | All | Accept as the signed-in user (`[Authorize]`, email must match). Body `{ token }`. `200` session body + `Set-Cookie` replacing the session. |
| `POST /password-resets` | All | Request a reset link (anonymous). Body `{ email }`, ≤ 4 KB. Always `202` with an empty body and `no-store` once the email is well formed; `400` `email` otherwise. |
| `POST /password-resets/validate` | All | Body `{ token }`. `200 { email }` for a usable token, identical `410` for any unusable one. |
| `POST /password-resets/confirm` | All | Body `{ token, password }`. `204`; `400` `token`/`password`; identical `410`; `500` rolled back. |
| `GET /public/organizations/{slug}/service-request-form` | All | Public request form configuration (anonymous, `no-store`). `200 { organizationName, phone, website, requestPrefix, timezone, categories: [{ id, name, services: [{ id, name }] }] }`. One identical `404` for an unknown slug or an organization that does not accept public requests; `429` + `Retry-After`. |
| `POST /public/organizations/{slug}/service-requests` | All | Public service request (anonymous, `no-store`). `multipart/form-data`: part `request` (JSON) + 0–5 `attachments` files; body ≤ 27 262 976 bytes (`413`). `201 { requestNumber }`; `400` field errors (generic for the honeypot or a malformed body); identical `404`; `415`; `429`; `500` rolled back. Attachment type is detected from content; files are stored inline. The slug is redacted in request logs. |
| `POST /public/quote-links/view` | All | Public quote (anonymous). Body `{ token }`, ≤ 16 KB. `200 PublicQuote`; `404` `quote_link_unavailable`; `410` `quote_superseded`; `429`. |
| `POST /public/quote-links/calculate` | All | Body `{ token, selectedOptionalLineIds }`. `200 Totals` from the frozen version values; persists nothing; `400` `selectedOptionalLineIds`. |
| `POST /public/quote-links/approve` | All | Body `{ token, selectedOptionalLineIds, acceptTerms }`. `200 PublicQuote`; `400` `acceptTerms`/`selectedOptionalLineIds`; `409` `quote_already_answered`. |
| `POST /public/quote-links/decline` | All | Body `{ token, reason }` (1-1000 characters). `200 PublicQuote`; `400` `reason`; `409`. |
| `POST /public/quote-links/clarification` | All | Body `{ token, message }` (1-1000 characters). `200 PublicQuote` (a repeat returns the existing clarification); `400` `message`; `409`. |
| `POST /public/quote-links/photos/{photoId}` | All | Body `{ token }`. `200` image of the quote's latest completed assessment (up to 6); otherwise the generic `404`. |
| `POST /public/quote-links/logo` | All | Body `{ token }`. `200` organization logo, or the generic `404` when there is none. |
| `POST /public/quote-links/pdf` | All | Body `{ token }`. `200 application/pdf` attachment `<Q-n>-v<versionNo>.pdf`. |

Public quote-link endpoints (customer-quote-approval): anonymous, the raw token in
the JSON body is the only credential and the organization, quote and version come
from its hashed row. One identical `404` ProblemDetails (`code`
`quote_link_unavailable`, title "This link isn't available.") covers malformed,
unknown, revoked, cancelled and expired tokens; `410` `quote_superseded` covers
a token of an older version. A token that expired on a `sent` or
`clarification_requested` quote first moves the quote to `expired` (one
`quote.expired` audit row, idempotent under the quote row lock). Every response
carries `Cache-Control: no-store`, `Referrer-Policy: no-referrer` and
`X-Content-Type-Options: nosniff` (`QuoteLinkPublicHeadersMiddleware`, set when
the response starts so errors keep them); image and PDF responses use
`Cache-Control: private, no-store`. Bodies are limited to 16 KB (`413`).
Responses lock the quote row (`SELECT ... FOR UPDATE`), re-read the state under
the lock and are idempotent per version; the partial unique indexes
`ux_quote_responses_final` and `ux_quote_responses_clarification` are the backstop
and a violation repeats the decision once. Handlers log the quote id and an
outcome category only: the token, hash, reasons, questions and responder data
never reach logs, audit rows or problem details, and request logs mask the photo
id. The PDF is generated by PDFsharp-MigraDoc (MIT) in the Inter font embedded in
`FieldOps.Infrastructure` (SIL OFL 1.1, `Pdf/Fonts/OFL.txt`); no installed font is
used. The quote link is `{first CORS origin}/quotes/view#token={token}`.

Invitation endpoints: every response is `Cache-Control: no-store`
(`InvitationNoStoreMiddleware`). `400` is `ValidationProblemDetails` with keys
`token`, `firstName`, `lastName` or `password` (malformed JSON or empty body:
no keys); `410` is one identical ProblemDetails for every unusable token
(unknown, replaced, expired, revoked, consumed, inactive organization);
`409` carries only the extension `code` (`account_exists`, `identity_mismatch`,
`membership_exists`, `access_unavailable`). Check order: rate limit, session
(`accept-existing`), content type/body/field shape, usable token, eligibility
under a `SELECT ... FOR UPDATE` lock on the invitation row. Accept is one
transaction (user, membership, branches, `accepted_at`, technician profile
link, `user.invitation_accepted` audit row); the cookie is issued only after
commit. The accept link is `{first CORS origin}/auth/invitation#token={token}`.
There is no `[Consumes]` attribute on these actions: it rejects during routing,
before the rate limiter and the session check, so `415` comes from body binding.

Password reset endpoints: every response is `Cache-Control: no-store`
(`PasswordResetNoStoreMiddleware`); no `[Consumes]` attribute (same reason as
invitations). The reset link is `{first CORS origin}/auth/reset-password#token={token}`.
The token is the invitation token generator (32 bytes, URL-safe base64, SHA-256
stored), lasts 30 minutes and is single use. Request: per-email limit, eligible
(`active`) user lookup, then one transaction that locks the `users` row
(`FOR UPDATE`), deletes the user's unused tokens and inserts the new one; the
email is queued only after the commit and sent by `PasswordResetEmailDispatcher`
(bounded in-memory `Channel`, drop on full, failures logged by category only).
Confirm: PBKDF2 hash first, then one transaction that locks the token row,
re-checks token, user status and password versus email, writes `used_at`,
`users.password_hash`, `password_changed_at` and `updated_at`. Session
revalidation rejects any session whose `signed_in_at` precedes the user's
`password_changed_at` (`401`, cookie cleared); `password_changed_at` is stored
truncated to milliseconds to match the cookie claim.

Session body: `{ user: { id, firstName, lastName, email }, organization: { id, name }, role: { code, name } }`.

Health responses are `application/json` with `Cache-Control: no-store, no-cache`
and contain only check names, statuses and durations; no descriptions,
connection details or exception messages.

## Error responses

| Case | Response |
| --- | --- |
| Unhandled exception | `500` `application/problem+json` with `type`, `title`, `status` and `traceId`, in every environment. Never exception messages, types or stack traces. |
| Unhandled exception, client does not accept JSON | `500` with an empty body. |
| Invalid model binding (`[ApiController]`) | Automatic `400` `ValidationProblemDetails` with `errors` and `traceId`. Not customized. |
| Empty `4xx`, such as an unknown route | `UseStatusCodePages` writes ProblemDetails. |
| Unauthenticated request to an `[Authorize]` endpoint | `401` ProblemDetails with `Cache-Control: no-store`, never a redirect. Forbidden: `403`. |
| Body over the endpoint's `[RequestSizeLimit]` | `BadHttpRequestExceptionHandler` (before `GlobalExceptionHandler`) writes ProblemDetails with the exception's status (`413`). Enforced by Kestrel, not by the in-memory test server. |
| Missing or non-JSON `Content-Type` on a `[Consumes("application/json")]` endpoint | `415` ProblemDetails. `POST /sessions` binds with `EmptyBodyBehavior.Disallow`, so a missing `Content-Type` is `415` with or without a body. |
| Rate limit rejection | `429` ProblemDetails with `Retry-After` (whole seconds). |

- `SessionsController` is not an `[ApiController]`: it checks `ModelState`
  itself. Malformed JSON, a JSON type mismatch, an empty body or a JSON
  `null` body returns an empty `400`, rendered by `UseStatusCodePages` as
  ProblemDetails with `traceId` and no `errors`; field rules return
  `ValidationProblemDetails` with only the `email` and `password` keys. The
  global `ApiBehaviorOptions` are unchanged.
- Every credential failure returns the same `401` body (written by
  `UseStatusCodePages`); only `traceId` differs.

- The full exception is written only to server logs, correlated by `traceId`.
- Errors raised at or after `UseCors` (endpoints, authorization), including
  handled and empty-body `500`s, carry CORS headers for configured origins
  only: CORS applies them when the response starts, after the exception
  handler writes it. Failures in middleware before `UseCors` get no CORS
  headers. Covered by `CorsTests`.
- Future `404`/`409`/validation mappings belong to the use-case spec that
  introduces them: a dedicated `IExceptionHandler` registered before
  `GlobalExceptionHandler`, or explicit results from the endpoint.
- The result pattern is deferred to its own spec. Sign-in validation uses
  FluentValidation in `FieldOps.Application`.

## Authentication

Cookie authentication (ASP.NET Core, Data Protection ticket), no server-side
session store.

| Attribute | Value |
| --- | --- |
| Name | `fieldops_session` |
| Flags | `HttpOnly`, `Secure` (always), `SameSite=Strict`, `Path=/`, no `Domain` |
| Ticket | User id, organization id, membership id, sign-in time, Remember me flag. No role, email or password. |
| Remember me off | Browser-session cookie (no `Expires`/`Max-Age`); ticket expires 8 h after sign-in; never renewed. |
| Remember me on | Persistent cookie, 14-day sliding expiry renewed on every authenticated request; rejected 30 days after sign-in. |

- Sign-in resolves the organization server-side: among active memberships in
  active organizations, earliest `joined_at` (nulls last), then `created_at`,
  then `id`. Client identifiers (body fields, `X-Organization-Id`) are never
  read. Later features take `OrganizationId` only from the validated session.
- Every request carrying the cookie is re-checked (`SessionCookieEvents`):
  user `active`, membership `active` and owned by that user and organization,
  organization active, absolute lifetime not passed. The role is read from
  the membership on each request. On failure the request is unauthenticated.
- `InvalidSessionCookieMiddleware` (after `UseAuthentication`) deletes the
  cookie when a request carried it but did not authenticate (tampered,
  another key ring, expired, invalidated), on every endpoint including
  anonymous ones. It adds nothing when the response already sets the cookie
  (new sign-in, sign-out). Requests rejected before authentication (`429`,
  CORS preflight) are not cleaned up.
- Passwords: PBKDF2-HMAC-SHA256 in
  `pbkdf2-sha256${iterations}${base64-salt}${base64-hash}`, at least 600,000
  iterations, 16-byte salt, 32-byte hash (`Pbkdf2PasswordHasher`). Unknown
  emails verify a fixed dummy hash. The password is verified before status
  and membership checks.
- A successful sign-in updates `users.last_login_at`/`updated_at` and inserts
  the `auth.signed_in` audit row in one `SaveChangesAsync` (one transaction).
  If it fails the response is `500` and no cookie is set.
- Data Protection keys use the default store; losing them signs every user
  out. No global fallback authorization policy: endpoints opt in with
  `[Authorize]`.

## Rate limiting

In memory, per process, on `POST /sessions`, `POST /organization-registrations`,
the three `/invitations/*` endpoints, the three `/password-resets*` endpoints and the public quote-link endpoints.
Every request counts, whatever its outcome.

| Limit | Key | Rule |
| --- | --- | --- |
| Per client | `RemoteIpAddress` | 10 requests per 5-minute fixed window |
| Global | One partition | 300 requests per 1-minute fixed window |
| Invitations, per client | `RemoteIpAddress`, one partition shared by the three invitation endpoints | 20 requests per 5-minute fixed window; global 300 per minute across the three |
| Password reset, per client | `RemoteIpAddress` | `POST /password-resets`: 5 per 15-minute fixed window (`password-reset-request`). `validate` + `confirm` share one partition: 20 per 5 minutes (`password-reset-token`). Global 300 per minute, one partition shared by the three endpoints. |
| Public request form, per client | `RemoteIpAddress` | `GET .../service-request-form`: 60 per 5-minute fixed window (`public-request-form`); global 600 per minute. |
| Public request submission, per client | `RemoteIpAddress` | `POST .../service-requests`: 5 per 15-minute fixed window (`public-request-submit`); global 100 per hour. |
| Public quote link, read (`quote-link-read`) | `RemoteIpAddress` | `view`, `calculate`, `photos`, `logo`: 60 per 5-minute fixed window; global 600 per minute. |
| Public quote link, actions (`quote-link-action`) | `RemoteIpAddress` | `approve`, `decline`, `clarification`: 10 per 15-minute fixed window; global 100 per minute. |
| Public quote link, PDF (`quote-link-pdf`) | `RemoteIpAddress` | `pdf`: 10 per 5-minute fixed window; global 100 per minute. |
| Password reset, per email | SHA-256 of the normalized email | 3 requests per 60 minutes (`IPasswordResetEmailThrottle`, bounded in memory), counted for every well-formed email. Not a `429`: the request returns the neutral `202` with no token or email. |
| Per email | SHA-256 of the normalized email | After 5 failed (`401`) attempts in a sliding 15 minutes, `429` before password verification until the oldest failure leaves the window. Unknown emails count. Success clears it; `400`, `413`, `415` and `429` do not count. |

- Rejections are `429` ProblemDetails with `Retry-After` in whole seconds and
  CORS headers.
- Counters reset on restart and are not shared across instances. Concurrent
  attempts for one email may briefly exceed the per-email limit (accepted).

## Pipeline and CORS

Middleware order: `RequestLoggingMiddleware`, `UseExceptionHandler`,
`UseStatusCodePages`, `InvitationNoStoreMiddleware`, `PasswordResetNoStoreMiddleware`, `QuoteLinkPublicHeadersMiddleware`, (Development: OpenAPI, Swagger UI),
`UseHttpsRedirection`, `UseCors`, `UseRateLimiter`, `UseAuthentication`,
`InvalidSessionCookieMiddleware`, `UseAuthorization`, endpoints.

The CORS policy allows the configured origins only, any header and method,
credentials (`Access-Control-Allow-Credentials: true`) and exposes
`Retry-After`. Other origins get no CORS headers.

## Logging

Logging uses the built-in `ILogger` console provider:

- Development: single-line `simple` formatter, EF Core SQL commands visible.
- Other environments: `json` formatter with UTC timestamps and scopes (which
  carry `TraceId`), EF Core at `Warning`.

`RequestLoggingMiddleware` (first in the pipeline) writes one line per request:

```text
HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMs:0.0} ms (traceId {TraceId})
```

| Status | Level |
| --- | --- |
| `2xx`/`3xx` | `Information` (`Debug` for successful `/health*` probes) |
| `4xx` | `Warning` |
| `5xx` | `Error` |

- If an exception escapes the pipeline, the status is `500`, or the status
  already sent when the response had started.
- The ProblemDetails and request-log `traceId` is the full W3C Activity id
  (`00-<trace-id>-<span-id>-<flags>`); the JSON console scope `TraceId` is only
  the 32-hex trace id, so correlate by the trace-id segment.

Rules:

- `GlobalExceptionHandler` logs each unhandled exception exactly once. .NET 10
  suppresses the exception middleware's own diagnostics for handled
  exceptions; the request line carries no exception.
- Never log query strings, bodies, headers, passwords, tokens, connection
  strings or sensitive customer data. Only method and path are logged.
- Keep `Microsoft.AspNetCore` at `Warning`: the built-in hosting request logs
  include the query string.
- EF Core sensitive data logging stays disabled.
- Sign-in: never log request bodies, emails, passwords, password hashes,
  cookie values or `Set-Cookie` headers. A failed sign-in logs only
  `Sign-in failed: {FailureCategory} {UserId}` (user id when known).

## Test isolation

- `FieldOpsApiFactory` passes settings through `UseSetting`; they are applied
  with command-line argument precedence, above user-secrets and environment
  variables.
- Tests without a database use an unreachable connection string (`127.0.0.1`,
  port `1`) that fails fast.
- Database-dependent tests use Testcontainers (`postgres:17-alpine`), never the
  local FieldOps database.
- Every new required setting must be set in `FieldOpsApiFactory`.
- `FieldOpsApiFactory` uses ephemeral Data Protection keys (one key ring per
  host, nothing written to the developer profile) and sets
  `RemoteIpAddress` from the test-only `X-Test-Client-IP` header.
- Session tests use clients without a cookie container and forward the
  cookie explicitly (a container drops `Secure` cookies over HTTP). Time is
  controlled by overriding the registered `TimeProvider`.
- Session tests share one Testcontainers PostgreSQL instance migrated with
  `MigrateAsync`; the request size limit test runs on Kestrel (`UseKestrel`).

## Deferred to deployment

HSTS, forwarded headers, `AllowedHosts` and how probes interact with HTTPS
redirection are decided with the deployment work. Also deferred:

- Data Protection key persistence.
- Forwarded headers: until configured, `RemoteIpAddress` behind the proxy is
  the proxy, so the per-IP sign-in limit applies to all clients together.
