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
10. Generate one migration for the complete slice.
11. Inspect the generated migration.
12. Compare it against the relational model.
13. Run tests.
14. Produce an implementation report.

# Rules

- Do not modify entities outside the active spec.
- Do not invent fields or relationships.
- OrganizationId is required for tenant-owned entities.
- Use Guid primary keys.
- Use timestamptz for UTC timestamps.
- Use snake_case in PostgreSQL.
- Avoid cascade delete unless explicitly approved.
- Never apply a migration automatically.
- Never place business rules inside controllers.
- Do not generate repositories without a demonstrated requirement.
- Stop if the specification and relational model conflict.

# Validation commands

dotnet format --verify-no-changes
dotnet build
dotnet test
