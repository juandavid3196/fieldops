# API configuration

Foundational runtime configuration for `FieldOps.Api`.

## Required settings

The API refuses to start when any required setting is missing or invalid.

| Key | Environment variable | Notes |
| --- | --- | --- |
| `ConnectionStrings:FieldOpsDatabase` | `ConnectionStrings__FieldOpsDatabase` | PostgreSQL connection string. Never committed. |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... | Absolute `http`/`https` origins without path, trailing slash or wildcard. |

`appsettings.Development.json` allows `http://localhost:4200` (Angular dev
server). `appsettings.json` ships with an empty origin list, so every other
environment must provide its origins explicitly.

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

In memory, per process, only on `POST /sessions`. Every request counts,
whatever its outcome.

| Limit | Key | Rule |
| --- | --- | --- |
| Per client | `RemoteIpAddress` | 10 requests per 5-minute fixed window |
| Global | One partition | 300 requests per 1-minute fixed window |
| Per email | SHA-256 of the normalized email | After 5 failed (`401`) attempts in a sliding 15 minutes, `429` before password verification until the oldest failure leaves the window. Unknown emails count. Success clears it; `400`, `413`, `415` and `429` do not count. |

- Rejections are `429` ProblemDetails with `Retry-After` in whole seconds and
  CORS headers.
- Counters reset on restart and are not shared across instances. Concurrent
  attempts for one email may briefly exceed the per-email limit (accepted).

## Pipeline and CORS

Middleware order: `RequestLoggingMiddleware`, `UseExceptionHandler`,
`UseStatusCodePages`, (Development: OpenAPI, Swagger UI),
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
