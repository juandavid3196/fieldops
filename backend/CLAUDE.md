# Backend

Clean Architecture + DDD. Configuration, endpoints, errors and logging:
`docs/backend/api-configuration.md`.

## Projects

| Project                           | Contains                                                                                      |
| --------------------------------- | --------------------------------------------------------------------------------------------- |
| `src/FieldOps.Domain`             | Entities and enums by aggregate folder. No EF Core or ASP.NET dependencies.                   |
| `src/FieldOps.Application`        | No code yet: `.gitkeep` folders, FluentValidation packages; no project references either way. |
| `src/FieldOps.Infrastructure`     | `DependencyInjection.cs`, `Persistence/` (DbContext, `Configurations/`, `Migrations/`).       |
| `src/FieldOps.Api`                | Controllers without business logic, `Configuration/`, `Extensions/`, `Middleware/`.           |
| `tests/FieldOps.UnitTests`        | Entity rules per aggregate; `Persistence/FieldOpsDbContextModelTests.cs` builds the model.    |
| `tests/FieldOps.IntegrationTests` | API pipeline via `FieldOpsApiFactory`; database tests via Testcontainers.                     |

- Current references: `Api → Infrastructure → Domain`.
- Target references, added with the first use case and not before:
  `Api → Application + Infrastructure`, `Infrastructure → Application + Domain`,
  `Application → Domain`.
- Template leftovers (`Class1.cs`, `UnitTest1.cs`, `WeatherForecast*.cs`): do
  not extend or remove; a separate chore will.

## Entities

- `public sealed class`, private parameterless constructor for EF,
  `{ get; private set; }`, no navigation properties.
- `static Create(...)` validates (`ArgumentException` + `nameof`), trims
  strings and assigns `Guid.NewGuid()`.
- `DateTimeOffset` (UTC), `DateOnly`/`TimeOnly`, `decimal` for money. Never `DateTime`.

## EF Core mapping

- One `internal sealed <Entity>Configuration` per entity in
  `Persistence/Configurations/`, plus a `DbSet<T>` in `FieldOpsDbContext`.
- Match `docs/database/fieldops-schema.sql` exactly: table, index and
  constraint names, lengths, `char(n)`, precision, `text`/`jsonb`/`inet`,
  `gen_random_uuid()` and `now()` defaults. Snake_case comes from
  `UseSnakeCaseNamingConvention()`.
- `true`-default booleans: `HasDefaultValue(true).HasSentinel(true)`.
- Register PostgreSQL enums in `FieldOpsDbContext` (`HasPostgresEnum`),
  `DependencyInjection` (`MapEnum`) and `FieldOpsDbContextModelTests`.
- `DeleteBehavior.NoAction`; `Cascade` only for schema `ON DELETE CASCADE`.
- Comment the source SQL above non-obvious keys, indexes and constraints.
- Tenancy: tenant-owned tables get a required `OrganizationId` FK to
  `organizations`. Children of tenant-owned parents also get the composite FK
  `(OrganizationId, ParentId) → parent (OrganizationId, Id)` against the
  parent's alternate key. Tables without `organization_id` in the schema
  inherit tenancy; do not add it.

## Migrations

- Generate only when asked, only for the active task's model changes; stop if
  unrelated pending changes appear.
- Never modify a committed or applied migration; add a corrective one.
- Never hand-edit `FieldOpsDbContextModelSnapshot.cs` or `*.Designer.cs`.
- Inspect every generated migration against the schema before reporting.
- Discard an uncommitted migration with `dotnet ef migrations remove`, never
  `--force` (it reverts the database).
- EF model and schema disagree: stop and report.
- `database-migration-script.sql` is committed; regenerate only when asked.

```bash
# Needs ConnectionStrings:FieldOpsDatabase in user-secrets.
dotnet ef migrations add <Name> --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj \
  --startup-project src/FieldOps.Api/FieldOps.Api.csproj --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj \
  --startup-project src/FieldOps.Api/FieldOps.Api.csproj --output database-migration-script.sql
```

## Microsoft Learn MCP

- Documentation only; ask version-specific questions (.NET 10, ASP.NET Core
  10, EF Core 10). It never authorizes a package, schema change or migration.

## API and tests

- Settings: class in `Configuration/` + `IValidateOptions<T>` + `ValidateOnStart()`;
  register services via `Extensions/`.
- Connection strings: user-secrets or environment variables, never `appsettings*.json`.
- Run locally with `--launch-profile http` (`http://localhost:5034`); the
  frontend depends on it.
- Test names: `Method_Condition_ExpectedResult`. Integration tests pass
  settings through `FieldOpsApiFactory`, never user-secrets.
