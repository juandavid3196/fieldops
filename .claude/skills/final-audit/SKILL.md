---
name: final-audit
description: Independently audit a completed FieldOps implementation against its approved spec by coordinating persistence review and final QA, without fixing code.
argument-hint: <spec-path>
---

# FieldOps final audit

Audits one implemented APPROVED spec: `database-reviewer` (if persistence
changed) → `qa-auditor`. Read-only: it fixes nothing, invokes no architect or
developer, never changes spec status, commits, pushes, merges or applies
migrations. It returns a verdict to `spec-impl`, which owns lifecycle
transitions.

Request: `$ARGUMENTS`

## 1. Gate (read-only; any failure → `AUDIT BLOCKED`, stop)

Read `CLAUDE.md`, `frontend/CLAUDE.md` and `backend/CLAUDE.md` first.

| Check       | Rule |
| ----------- | ---- |
| Argument    | Exactly one relative path matching `^specs/[a-z0-9]+(-[a-z0-9]+)*/spec\.md$`, slug not `templates`. |
| Path safety | Reject absolute paths (`/`, `\`, `~`, drive letters), backslashes, any `..`, anything outside `specs/`. Never normalize or guess. |
| File        | Exists. |
| Status      | `APPROVED`. DRAFT, IMPLEMENTED or AUDITED → stop. |
| Type        | `Frontend`, `Backend`, `Full-stack` or `UI-only`. `Infrastructure` → stop (not audited by this workflow). |
| Decisions   | No Open decision with `Blocking: Yes` and no resolution. |
| FR/AC       | ≥1 active FR and ≥1 active AC. |
| Branch      | Not `main` or `master`. |
| Baseline    | Record `git status --porcelain --untracked-files=all` and `git hash-object` for every existing changed or untracked regular file; for deleted paths keep the status entry. |
| Change set  | Resolvable (§2). |

Run all checks and report every failure at once.

## 2. Change set

Candidates:

1. Committed branch changes: `git diff --name-status <base>...HEAD`, where
   `<base>` is `main` (`CLAUDE.md` default branch; prefer `origin/main` when
   present) and `git log <base>..HEAD` lists the audited commits.
2. Staged and unstaged changes: `git diff --name-status HEAD`.
3. Untracked files from the baseline.

Pre-existing work: a `SPEC-IMPL AUDIT BASELINE` block from `spec-impl` for
this spec lists unrelated baseline paths and resume-authorized paths, each
with status and hash. The audited implementation contains only:

| Included | Rule |
| -------- | ---- |
| Committed | Candidate 1 paths traceable to this spec. |
| New work | Paths new or altered since the `spec-impl` baseline. |
| Resumed | Resume-authorized paths from the block. |

An unrelated baseline path whose current status and hash match the block is
excluded from §3 classification, out-of-scope findings and the verdict; it is
checked only in §7. One that no longer matches was altered by the workflow:
include it as an out-of-scope change.

Without the block (direct `/final-audit`), uncommitted paths not traceable
to this spec are not assumed to be implementation.

Stop with `AUDIT BLOCKED`, asking the user who owns the paths, when any
committed or uncommitted path's ownership is ambiguous. Also stop when the
base cannot be resolved, the merge base is missing, or the change set is
empty. `git diff` alone is never the implementation.

## 3. Scope

From the spec, list: active FR/AC IDs, frontend, backend and persistence
scope, API contract rows, authorization and tenant-isolation rules (or public
/pre-tenant protections), required tests, approved design paths, required
documentation and non-goals.

Classify every path in the §2 implementation:

| Class       | Examples |
| ----------- | -------- |
| Frontend    | `frontend/src/**` except tests |
| Backend     | `backend/src/**` outside persistence |
| Persistence | Persisted entities, `Persistence/Configurations/`, `FieldOpsDbContext`, `Migrations/` (incl. snapshot, `*.Designer.cs`), `database-migration-script.sql` |
| Tests       | `*.spec.ts`, `backend/tests/**` |
| Docs        | `docs/**`, `*.md` required by the spec |
| Out of scope | Anything not traceable to an active FR/AC or required doc; `.claude/`, CI, hooks, dependencies unless the spec requires them |

## 4. Persistence review

Invoke `database-reviewer` when the change set contains any mapping-relevant
change: persisted entity shape, EF configuration, `FieldOpsDbContext` or its
model, migration or snapshot, or a constraint, index, enum or provider-specific
mapping. Always run a fresh review, even if the `backend` skill already
approved. Otherwise record why it was skipped (e.g. frontend-only, or
domain-behavior-only changes).

Brief: `FINAL AUDIT (final-audit skill) — make no changes`, the spec path,
the §2 base, commit range and path list (committed + uncommitted + untracked),
`docs/database/fieldops-schema.sql`, and the relevant entities,
configurations and migrations.

`REJECTED` → final `AUDIT FAIL`; never route it to a developer.

Migration status, when the change set adds a migration:

| Evidence | Source |
| -------- | ------ |
| Reviewed | This audit's `database-reviewer` result covers the migration, snapshot and `*.Designer.cs`. |
| Not applied | `backend` workflow report or command log states it was not applied, with no `dotnet ef database update\|drop` run. Never connect to the development database to check. |

Either missing → migration status `NOT VERIFIED` → `AUDIT FAIL`.

## 5. QA audit

Invoke `qa-auditor` after §4. Brief: `FINAL AUDIT (final-audit skill) — make
no changes`, the spec path, the §2 change set and classification, frontend
/backend workflow reports if provided, the §4 result or skip reason, the
validations required by §6, the §8 rules with the ACs needing browser
evidence, and the approved design paths.

Require evidence for every active FR and AC (`MET`, `NOT MET`, `NOT
VERIFIED`) and for: scope and non-goals, API contract implementation, domain
and validation rules, authorization, tenant isolation and cross-organization
behavior, public/pre-tenant protections where applicable, loading, empty,
error, permission and submission states, static accessibility, sensitive data
in responses/logs/source, test quality and missing cases, spec-required docs,
regressions and unrelated changes, and persistence approval when applicable.

## 6. Validation

Run, or confirm `qa-auditor` ran, the `CLAUDE.md` commands for every changed
area:

| Area     | Working directory | Commands |
| -------- | ----------------- | -------- |
| Frontend | `frontend/`       | Format check, lint, tests, production build |
| Backend  | `backend/`        | Tool restore, restore, format verification, Release build, unit tests |

- Integration tests only when backend changed and `docker info` succeeds
  without starting anything (Testcontainers). Otherwise `NOT VERIFIED` with
  the reason. Never target the local FieldOps database.
- Restore only from existing lockfiles and manifests when needed to run
  validation: `npm ci` (from `frontend/`), `dotnet tool restore` and
  `dotnet restore FieldOps.slnx` (from `backend/`). `frontend/package.json`,
  `frontend/package-lock.json`, `*.csproj` and `backend/dotnet-tools.json`
  must stay unchanged (§7).
- Never: format that writes files, add, update or remove packages
  (`npm install`, `dotnet add package`), generate, remove or apply
  migrations, touch snapshots, delete Docker volumes, print secrets or
  connection strings.

## 7. Read-only integrity

After all agents and commands, compare
`git status --porcelain --untracked-files=all` and hashes with this audit's
§1 baseline, and every unrelated path with the `SPEC-IMPL AUDIT BASELINE`
block. Any new, removed or altered path, other than build/test output
ignored by the repository's `.gitignore`, or any altered unrelated baseline
path → `AUDIT FAIL`. Report it; never clean, delete or revert.

## 8. Browser evidence

`qa-auditor` uses Playwright MCP for ACs that need browser evidence, only
against the already-running local app (`http://localhost:4200`, API
`http://localhost:5034`). Never start Docker Desktop or the app, apply
migrations, or use real credentials, personal profiles or production data.
Data-changing submissions only against disposable test data or with user
approval; otherwise verify up to the request and mark persistence
`NOT VERIFIED`.

- Check what the spec requires: navigation, loading, empty, error,
  permission and submission states, keyboard/focus, accessibility
  snapshots, supported breakpoints, dark mode.
- Screenshots only as required evidence, in the ignored `.playwright-mcp/`
  output; any other written path fails §7.
- Browser checks never replace frontend tests, build or static review.
- Playwright unavailable or app not running: only ACs requiring browser
  evidence become `NOT VERIFIED`; list breakpoints, dark mode, pixel
  comparison, runtime focus, browser accessibility and end-to-end navigation
  as not performed.

## 9. Verdict

| Verdict                          | When |
| -------------------------------- | ---- |
| `AUDIT FAIL`                     | Any Critical or Important finding; `database-reviewer` `REJECTED`; a required validation failed, was skipped or is `NOT VERIFIED`; any required FR/AC `NOT MET` or `NOT VERIFIED`; migration status `NOT VERIFIED`; integrity check failed. |
| `AUDIT PASS WITH MINOR FINDINGS` | Only Minor findings; everything else complete. |
| `AUDIT PASS`                     | No findings and complete evidence. |
| `AUDIT BLOCKED`                  | Gate failed; nothing audited. |

## 10. Report

1. Spec path, status and Type.
2. Base, commit range and uncommitted/untracked scope audited.
3. Changed-file classification.
4. Agents invoked or skipped, with reason.
5. FR/AC matrix: ID · implementation · test evidence · status.
6. `database-reviewer` result and findings.
7. Migration status: none, or reviewed + not applied with evidence, or
   `NOT VERIFIED`.
8. QA findings: Critical, Important, Minor.
9. Validation: each command, pass/fail/not run, error excerpt.
10. Browser evidence per AC (Playwright) and checks not performed.
11. Items not verified.
12. Out-of-scope changes; unrelated baseline paths excluded and whether
    they stayed unaltered.
13. Read-only integrity result.
14. Verdict.
15. Follow-up owner per finding: `frontend-developer`, `backend-developer`,
    user decision or infrastructure task.
