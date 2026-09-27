---
name: implement-persistence-slice
description: Generate and validate a complete EF Core persistence slice from an approved FieldOps specification.
---

# Purpose

Implement a group of related FieldOps entities using .NET 10, EF Core 10 and PostgreSQL.

# Required input

- Approved specification
- Entities and properties
- Relationships and cardinalities
- Business constraints
- Expected tables and indexes

# Workflow

1. Read CLAUDE.md and the approved spec.
2. Inspect the existing Domain and Infrastructure patterns.
3. Present the entities, relationships and files to create.
4. Wait for approval before implementation.
5. Create Domain entities.
6. Create EF Core configurations.
7. Update FieldOpsDbContext only when necessary.
8. Add domain and persistence tests.
9. Build the complete solution.
10. Migration: generate one for the complete slice only when the invoking
    `backend`/`spec-impl` workflow explicitly authorized
    `--generate-migration`. Otherwise report that a migration is required
    and stop.
11. Inspect the generated migration.
12. Compare it against the relational model.
13. Run the focused tests (Validation commands).
14. Produce an implementation report.

# Rules

- Do not modify entities outside the active spec.
- Do not invent fields or relationships.
- OrganizationId is required for tenant-owned entities.
- Use Guid primary keys.
- Use timestamptz for UTC timestamps.
- Use snake_case in PostgreSQL.
- Avoid cascade delete unless explicitly approved.
- Never apply a migration (`dotnet ef database update|drop`).
- Never modify or remove a committed or applied migration.
- Never place business rules inside controllers.
- Do not generate repositories without a demonstrated requirement.
- Stop if the specification and relational model conflict.

# Validation commands

Focused profile from `CLAUDE.md` (run from `backend/`); the full suites belong
to `final-audit`:

dotnet format FieldOps.slnx --verify-no-changes --no-restore
dotnet build FieldOps.slnx --configuration Release --no-restore
Focused unit/persistence tests with the `--filter` command in `backend/CLAUDE.md`.
