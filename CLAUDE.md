# FieldOps

FieldOps is a multi-tenant field service management platform for companies
that manage their own customers, technicians, work orders and billing.

It is not a marketplace. Customers do not select technicians. The service
company schedules and assigns its internal workforce.

## Technology stack

### Frontend

- Angular 22
- TypeScript
- Standalone components
- Signals
- RxJS
- PrimeNG
- SCSS
- Vitest
- ESLint
- Prettier

### Backend

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core 10
- PostgreSQL
- Clean Architecture
- Domain-driven design
- Docker

## Repository structure

fieldops/
frontend/
backend/
src/
FieldOps.Api/
FieldOps.Application/
FieldOps.Domain/
FieldOps.Infrastructure/
tests/
FieldOps.UnitTests/
FieldOps.IntegrationTests/
specs/
templates/
frontend/
backend/
completed/
docs/
.claude/
agents/
skills/
hooks/

## Source of truth

Implementation decisions must follow this order:

1. Approved specification
2. Relational database model
3. Architecture documentation
4. Existing validated code
5. Design mockups

Stop and report the conflict when two sources disagree.

## Specification workflow

Business functionality requires an approved spec.

Initial database modeling and migrations may be implemented directly from
the approved relational database model without a functional spec.

Every spec must contain:

- Objective
- Roles and permissions
- Main flow
- Business rules
- States and transitions
- Entities and relationships
- API contract
- Frontend requirements
- Error cases
- Acceptance criteria
- Required tests
- Out-of-scope items

Implement only the active spec.

Do not add speculative features.

## Backend architecture

### Domain

Contains:

- Entities
- Value objects
- Domain rules
- Domain events
- Enums

Domain must not depend on Application, Infrastructure or API.

### Application

Contains:

- Use cases
- Commands and queries
- Interfaces
- Validation
- Application models

Application may depend only on Domain.

### Infrastructure

Contains:

- Entity Framework Core
- PostgreSQL configurations
- Identity implementations
- File storage
- Notifications
- External integrations

Infrastructure implements Application interfaces.

### API

Contains:

- Controllers
- Middleware
- Dependency injection configuration
- Authentication configuration
- HTTP concerns

Controllers must not contain business logic.

## Database rules

- PostgreSQL is the database.
- EF Core migrations control schema changes.
- The relational model is the database design reference.
- Do not execute the complete SQL schema manually.
- Use snake_case database naming.
- Use Guid primary keys.
- Use timestamptz for UTC timestamps.
- Tenant-owned entities require OrganizationId.
- Avoid cascade delete unless explicitly required.
- Never generate or apply a migration without reviewing it.
- Never modify an existing committed migration.
- Never apply migrations automatically to production.

## Database safety

Agents may generate, inspect and script EF Core migrations, but must never:

- Run `dotnet ef database update`.
- Run `dotnet ef database drop`.
- Drop PostgreSQL databases.
- Delete Docker database volumes.

Database migrations must be reviewed and applied manually by the user.

## Frontend architecture

Use feature-oriented organization.

src/app/
core/
shared/
layout/
features/

Rules:

- Use standalone components.
- Use Angular Signals for local synchronous state.
- Use RxJS for asynchronous streams.
- Use functional HTTP interceptors.
- Use lazy-loaded feature routes.
- Keep feature-specific models and services inside the feature.
- Shared must contain only reusable elements.
- Do not call HttpClient directly from UI components.
- Do not place business logic in templates.
- Do not use any type unless technically unavoidable.
- Use PrimeNG before creating custom complex components.
- All pages must include loading, empty, error and permission states.

## Security

- Never commit secrets.
- Never expose connection strings.
- Never trust OrganizationId received from the client.
- Resolve tenant identity from the authenticated context.
- Validate authorization in the backend.
- Frontend guards are not security boundaries.
- Validate uploaded files by size, type and authorization.
- Do not log passwords, tokens or sensitive customer data.

## Git workflow

Branches:

- feature/FEAT-XXX-description
- fix/FEAT-XXX-description
- chore/description

Rules:

- Do not work directly on main.
- Keep pull requests scoped to one spec.
- Do not mix unrelated refactoring with a feature.
- Use the spec ID in branch names and commits.

## Required validation

### Frontend

npm run format:check
npm run lint
npm run test
npm run build

### Backend

dotnet format --verify-no-changes
dotnet build
dotnet test

### Database

dotnet ef migrations script

## Definition of done

A spec is complete only when:

- Acceptance criteria pass.
- Authorization is implemented.
- Tenant isolation is verified.
- Tests pass.
- Frontend and backend builds pass.
- Database migrations are reviewed.
- No unrelated files were modified.
- Documentation is updated.
- An audit report has been produced.

## Agent behavior

Before modifying files:

1. Read this CLAUDE.md.
2. Read the active spec.
3. Inspect relevant existing code.
4. Present a short implementation plan.
5. Identify conflicts or missing decisions.

After implementation:

1. Run the required validation commands.
2. Review the changed files.
3. Report tests and build results.
4. List any deviation from the spec.
5. Do not claim completion when a validation failed.
