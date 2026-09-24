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

- The full exception is written only to server logs, correlated by `traceId`.
- Errors raised at or after `UseCors` (endpoints, authorization), including
  handled and empty-body `500`s, carry CORS headers for configured origins
  only: CORS applies them when the response starts, after the exception
  handler writes it. Failures in middleware before `UseCors` get no CORS
  headers. Covered by `CorsTests`.
- Future `404`/`409`/validation mappings belong to the use-case spec that
  introduces them: a dedicated `IExceptionHandler` registered before
  `GlobalExceptionHandler`, or explicit results from the endpoint.
- Result pattern, validation library usage and authentication are deferred to
  their own specs.

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

## Test isolation

- `FieldOpsApiFactory` passes settings through `UseSetting`; they are applied
  with command-line argument precedence, above user-secrets and environment
  variables.
- Tests without a database use an unreachable connection string (`127.0.0.1`,
  port `1`) that fails fast.
- Database-dependent tests use Testcontainers (`postgres:17-alpine`), never the
  local FieldOps database.
- Every new required setting must be set in `FieldOpsApiFactory`.

## Deferred to deployment

HSTS, forwarded headers, `AllowedHosts` and how probes interact with HTTPS
redirection are decided with the deployment work.
