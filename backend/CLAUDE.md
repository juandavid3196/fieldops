# Backend

Clean Architecture + DDD. API configuration: `docs/backend/api-configuration.md`.

## Projects

- `Domain`: aggregates/enums; no EF/ASP.NET.
- `Application`: use cases, validators, ports and DI.
- `Infrastructure`: DI and persistence.
- `Api`: thin controllers, configuration, extensions and middleware.
- Unit tests: non-trivial domain/application/model rules. Integration: API pipeline and Testcontainers.
- References: `Api → Application + Infrastructure`, `Infrastructure → Application + Domain`, `Application → Domain`.
- Template leftovers are outside feature scope.

## Entities

- `public sealed`, private EF constructor, private setters, no navigation properties.
- `Create(...)` validates invariants, trims strings and assigns `Guid.NewGuid()`.
- Use UTC `DateTimeOffset`, `DateOnly`/`TimeOnly` and `decimal`; never `DateTime`.
- Test only non-trivial/distinct rules; parameterize equivalent field cases.

## EF Core

- One internal sealed configuration/entity plus required `DbSet<T>`.
- Match exact affected schema definitions: columns, keys, constraints, indexes, lengths, precision, provider types/defaults. Inspect targeted schema/configuration/snapshot diffs, not the complete model by default.
- Snake case via `UseSnakeCaseNamingConvention()`; true defaults use `HasDefaultValue(true).HasSentinel(true)`.
- PostgreSQL enums require `HasPostgresEnum`, `MapEnum` and focused model-test registration.
- `DeleteBehavior.NoAction`; Cascade only when schema says so.
- Tenant relationships follow authoritative organization/composite FKs; never add `organization_id` where tenancy is inherited.
- Mapping changes require one `database-reviewer` approval per exact persistence fingerprint; final audit reuses it unchanged.

## Migrations

- Generate only with explicit workflow authorization; stop on unrelated pending changes.
- Never edit committed/applied migrations or hand-edit snapshot/designer files.
- Inspect generated operations against the expected schema delta.
- Remove only the workflow's uncommitted/unapplied migration with `dotnet ef migrations remove`, never `--force`.
- Never apply/drop databases. Regenerate the SQL script only when requested.

Commands from `backend/`:

- `dotnet ef migrations add <Name> --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj --startup-project src/FieldOps.Api/FieldOps.Api.csproj --output-dir Persistence/Migrations`
- `dotnet ef migrations script --idempotent --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj --startup-project src/FieldOps.Api/FieldOps.Api.csproj --output database-migration-script.sql`

User-secrets may supply design-time connection strings; never read/print them.

## API

- Settings: configuration class + `IValidateOptions<T>` + `ValidateOnStart()`; register through Extensions.
- Connection strings: user-secrets/environment only.
- Local profile: `http` at `http://localhost:5034`.
- Controllers delegate to Application; preserve approved safe ProblemDetails.
- Resolve and authorize organization context server-side.

## MCP

Microsoft Learn is lazy: one targeted unresolved .NET/ASP.NET/EF version question, no retry; record/reuse the answer. It never authorizes dependencies, contracts, schema or migrations.

## Tests

- Integration settings use `FieldOpsApiFactory`, never user-secrets.
- Prefer clear `Method_Condition_ExpectedResult` names; naming alone is not a finding when intent is clear.
- Do not duplicate identical unit/integration validation.
- One grouped integration test may evidence contract, authorization and tenancy for a flow.
- Add shared auth/session/tenant infrastructure tests only when that boundary or a distinct authorization path changes.
- One representative cross-tenant denial per distinct path, not per endpoint/field.

Focused after Release build:

- Unit: `dotnet test tests/FieldOps.UnitTests/FieldOps.UnitTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~FieldOps.UnitTests.<Folder>"`
- Integration: `dotnet test tests/FieldOps.IntegrationTests/FieldOps.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~FieldOps.IntegrationTests.<Folder>"`

Integration runs only when `docker info` already succeeds, using Testcontainers—not the local database. Full suites belong to final audit.
