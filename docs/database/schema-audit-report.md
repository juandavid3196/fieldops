# FieldOps Database Schema Audit Report

**Date:** 2026-09-22
**Source of truth:** `docs/database/FieldOps_PostgreSQL_Schema.sql` (`docs/database/fieldops-schema.sql`)
**Scope:** All Domain entities, EF Core configurations, `FieldOpsDbContext` registrations, and generated migrations.

## Scope

Audited all 50 Domain entities, their EF Core configurations, `FieldOpsDbContext`
registrations, and the full history of 10 migrations against
`docs/database/fieldops-schema.sql`, using the authoritative
`FieldOpsDbContextModelSnapshot.cs` (EF's complete resolved model) as the
primary cross-reference artifact, supplemented by direct schema comparison
and git history analysis.

## 1. Expected tables (from schema) — 50 total

`organizations, branches, users, roles, permissions, role_permissions,
organization_users, organization_user_branches, user_invitations,
invitation_branches, customers, customer_contacts, properties,
customer_notes, service_categories, catalog_items, skills,
technician_profiles, technician_skills, technician_weekly_availability,
technician_breaks, technician_exceptions, service_requests,
request_attachments, request_messages, request_status_history, assessments,
assessment_attachments, quotes, quote_versions, quote_lines,
quote_responses, work_orders, work_order_required_skills,
work_order_checklist_templates, visits, visit_assignments,
visit_status_history, visit_time_entries, visit_checklist_items,
visit_materials, visit_evidence, visit_incidents, customer_signoffs,
invoices, invoice_lines, payments, payment_allocations, notifications,
audit_logs`

## 2. Implemented tables — 50/50 ✅

All 50 tables found in the EF Core model (`.ToTable(...)` count = 50, exact
match), each with a corresponding `DbSet<T>` in `FieldOpsDbContext`
(count = 50).

## 3. Missing elements

**None found.**

## 4. Incorrect elements

**None found.** Detailed checks performed:

| Category | Result |
|---|---|
| Shadow properties / duplicate FKs | None detected — scanned all `Guid`-typed properties across the snapshot; no `*Id1`, `*Id2`, or type-suffixed shadow names |
| Delete behaviors | All 132 relationships reviewed line-by-line. 22 `Cascade` relationships exactly match the schema's `ON DELETE CASCADE` clauses; all others `NoAction` — including the deliberate exceptions (`customer_signoffs.visit_id`, `audit_logs.actor_user_id`/`branch_id` correctly **not** cascaded) |
| Composite foreign keys | All `(organization_id, X)` → `(organization_id, id)` composite FKs correctly paired with `HasPrincipalKey`; no malformed composites |
| Check constraints | 43/43 present, text matches the schema's CHECK expressions exactly (constraint *names* use a custom `ck_table_column` convention since the raw SQL doesn't name its checks — cosmetic only, not a deviation) |
| Indexes | All 16 schema-named indexes present; both partial indexes (`ix_assignments_technician`, and the corrective `ix_visit_assignments_active_technician_unique`) retain their `WHERE unassigned_at IS NULL` filter; all 5 `DESC`-ordered indexes (`ix_requests_pipeline`, `ix_quotes_status`, `ix_payments_customer_date`, `ix_audit_entity`, `ix_audit_actor`) correctly configured via `IsDescending` |
| Enum mappings | 12/12 native PostgreSQL enums correctly declared with exact label order; 4 varchar+CHECK "pseudo-enums" (`TechnicianStatus`, `VisitTimeEntryType`, `VisitEvidenceType`, `NotificationChannel`) correctly mapped as `character varying`, **not** native enums |
| jsonb mappings | 7/7 columns (`business_hours`, `billing_address`, `service_address`, `payload`, `before_data`, `after_data`, `metadata`) correctly typed |
| inet mappings | 2/2 columns (`audit_logs.ip_address`, `quote_responses.ip_address`) correctly mapped to `System.Net.IPAddress` |
| Numeric precision | All `numeric(14,2)`, `numeric(7,4)`, `numeric(12,3)`, `numeric(9,6)`, `numeric(4,1)` columns verified against `HasPrecision` calls |
| Tenant ownership (`organization_id`) | Present everywhere the schema declares it, including the double-FK pattern (direct `→organizations` **and** composite `→parent`) on every table that needs it; correctly **absent** on tables with no `organization_id` column in the schema (`quote_lines`, `assessment_attachments`, `visit_*` children, `payment_allocations`, etc.) |
| `bigserial` PK | `audit_logs.id` correctly uses `UseIdentityByDefaultColumn()` (bigint identity), matching the `smallserial` pattern already used for `roles`/`permissions` |

## 5. Relationships configured differently between navigations and DB mappings

**None found.** Every navigation's `HasForeignKey`/`HasPrincipalKey`/`OnDelete`
pairing was cross-checked directly in the resolved snapshot output (not just
the source config files), so navigation-vs-mapping drift would have
surfaced here — it didn't.

## 6. Migration integrity

- `dotnet ef migrations list` → 10 migrations, matching every migration
  generated across this project's implementation history.
- Every migration file (`.cs` and `.Designer.cs`, 20 files) traced via
  `git log -- <path>` to **exactly one commit each** — no post-creation
  edits.
- `git status` clean at the start and end of the audit.

## 7–9. Corrective migration

Generated `AlignDatabaseSchema` to conclusively test for pending model
changes. **Result: completely empty** (`Up`/`Down` both contain zero
operations) — definitive proof the EF Core model exactly matches the last
applied migration. Per the requirement that the corrective migration "must
contain only the required differences," an empty migration represents zero
differences, so **it was removed** rather than left in history. **No
corrective migration was needed or created.**

## 10. Idempotent SQL script

Generated to `backend/database-migration-script.sql` (2,333 lines).
Inspected: begins with `__EFMigrationsHistory` bootstrap, wraps every
operation in `DO $EF$ ... IF NOT EXISTS(...) ... END $EF$;` guards keyed by
migration ID, covers all 10 migrations in correct order (verified via
distinct `migration_id` extraction), ends with a clean `COMMIT`. **Not
applied** to any database.

## 11. Validation results

| Command | Result |
|---|---|
| `dotnet format --verify-no-changes` | ✅ Pass |
| `dotnet build` | ✅ Pass — 0 warnings, 0 errors |
| `dotnet test` | ✅ Pass — 232/232 (231 unit + 1 integration) |
| `dotnet ef migrations list` | ✅ 10 migrations, all applied per `__EFMigrationsHistory` |
| `dotnet ef migrations script --idempotent` | ✅ Generated and inspected, not applied |
| `dotnet ef database update` | Not run, per instruction |

## 12. Final approval

### ✅ APPROVED

The FieldOps EF Core persistence model is fully aligned with
`docs/database/fieldops-schema.sql`. All 50 tables, 43 check constraints,
16+ indexes, 12 enums, 4 pseudo-enums, jsonb/inet mappings, composite keys,
and delete behaviors are correct and complete. No missing elements, no
incorrect elements, no shadow properties, no relationship drift, and no
migration history tampering were found. No corrective migration was
required.

**Note on git status:** only `backend/database-migration-script.sql` was
newly created as a deliverable of this audit — no source code or migrations
were modified.
