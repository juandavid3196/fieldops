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
| `GET /health` | All | `self` and `postgresql` checks. `200` when healthy, `503` otherwise. |
| `GET /openapi/v1.json` | Development | OpenAPI document. |
| `GET /swagger` | Development | Swagger UI over the OpenAPI document. |

The health response contains only check names, statuses and durations; no
connection details or exception messages.

## Error responses

Unhandled exceptions return `500` as `application/problem+json`
(RFC 9457) with a `traceId`. The exception message is included as `detail`
only in Development. Empty error responses such as `404` also use
ProblemDetails.

## Logging

Logging uses the built-in `ILogger` console provider:

- Development: single-line `simple` formatter, EF Core SQL commands visible.
- Other environments: `json` formatter with UTC timestamps, EF Core at
  `Warning`.

Rules:

- Never log passwords, tokens, connection strings or sensitive customer data.
- The exception handler logs the request method and path only, not the query
  string.
- EF Core sensitive data logging stays disabled.
