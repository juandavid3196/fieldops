---
name: backend
description: Implement the backend portion of an approved FieldOps spec with conditional architecture, focused tests, optional persistence review and compact evidence. Use directly for backend work or as a delegated stage of spec-impl.
argument-hint: <spec-path> [--generate-migration]
---

# FieldOps backend

Implement one approved backend scope. The skill orchestrates agents but writes no application code, never touches `frontend/`, never applies migrations and never runs final QA.

Request: `$ARGUMENTS`

## 1. Invocation mode and gate

### Delegated by spec-impl

A valid brief starts with `SPEC-IMPL DELEGATED RUN` and includes the spec path/hash, mode, backend FR/AC groups, contract references, authorization/tenancy summary, persistence impact, migration authorization, area baseline/resume paths and MCP evidence.

Verify only:

- Spec still exists, is clean, has the supplied hash/status/type and no new blocking decision.
- Backend packet rows are complete and match the referenced spec sections.
- Branch is not `main` or `master`.
- Authorized paths are inside `backend/` or exact required `docs/backend/` paths.

Do not repeat the parent gate, rebuild the global baseline or reread complete documents already summarized in the packet.

### Direct invocation

Require exactly `<spec-path>` and optional `--generate-migration`. Validate safe relative path, existing APPROVED Backend/Full-stack spec, active backend FR/AC, complete final API rows, authorization/tenant or approved public-flow rules, data impact, no blocking decision, non-protected branch and full working-tree baseline.

Migration rules:

| Data impact | Flag | Result |
| --- | --- | --- |
| EF/database change present in authoritative schema | Present | Authorized |
| EF/database change | Absent | `BACKEND BLOCKED` |
| Structure absent from schema | Any | `BACKEND BLOCKED` |
| No EF/database change | Present | `BACKEND BLOCKED` |
| No EF/database change | Absent | No migration |

Run all applicable gate checks before implementation and report failures together.

Pre-existing paths are read-only unless a matching `SPEC-IMPL RESUME AUTHORIZATION` explicitly delegates them.

## 2. Context and MCP

Build or consume a compact backend packet:

- FR/AC behavior groups and spec section references.
- Contract IDs plus concise request/response/error/permission summary.
- Authorization, tenant and cross-organization rules.
- Persistence tables/columns and migration state.
- Exact relevant code paths and one closest precedent when known.
- Test budget, baseline paths and prior valid evidence.

Use targeted reads. Root/nested `CLAUDE.md`, full spec, API docs or complete schema are read only when the packet lacks a required rule or its fingerprint changed.

MCP is lazy. Start `microsoft-learn: NOT CHECKED (no question)`. The architect or developer may make one targeted check only when an unresolved version-specific .NET/ASP.NET/EF question changes implementation. Maximum two MCP calls for the entire backend stage, no retries, and record the answer for later stages. Repository code, installed packages, spec and schema win.

## 3. Architecture routing

Classify from the packet:

- `ROUTINE`: final contracts; established command/query/controller/validator or mapping pattern; no new boundary, dependency, tenancy mechanism, transaction strategy or unresolved architecture conflict.
- `TARGETED`: one material architecture question.
- `FULL`: new aggregate/public contract pattern, cross-cutting authorization/tenancy, complex transaction/concurrency or architectural conflict.

`ROUTINE` skips `backend-architect`. `TARGETED` asks it only the named question. `FULL` requests its delta plan. A fully specified audit correction skips architecture unless implementation exposes a new material decision.

If the architect returns `BACKEND ARCHITECT NOT NEEDED`, continue without another planning pass.

Behavior-changing, schema, public-contract, authorization, tenancy, dependency or uncovered migration decisions block. When delegated, return the questions; when direct, ask once. Never silently revise the spec.

## 4. Implement

Invoke `backend-developer` for one implementation pass, briefed with the compact packet, architecture delta if any, writable area and resume paths.

- Implement only approved behavior.
- No frontend, unrelated docs, CI, hooks, `.claude/` or unapproved packages.
- No committed migration edits or manual snapshot/designer edits.
- Generate a migration only when authorized; never apply/remove with `--force`.
- Add the smallest behavior-group tests within the testing policy. One test may evidence multiple FR/AC IDs.
- Mandatory applicable security boundaries remain exhaustive: authorization, tenant isolation, sessions, hashing/sensitive data, rollback/concurrency and migration safety.
- Run the backend Focused profile once after the final implementation pass; never the full suites.
- Integration tests use only isolated disposable infrastructure; never start Docker Desktop or use the local FieldOps database.

## 5. Persistence review

Invoke `database-reviewer` once only when mapping-relevant files changed: persisted shape, EF configuration/model, DbContext, migration/snapshot, constraint, index, enum or provider mapping.

Compute and report a persistence fingerprint from those changed paths. Domain-method-only changes skip review.

- `APPROVED` / `APPROVED WITH NOTES`: continue.
- `REJECTED`: include findings in the single correction pass.

## 6. Verify and correction

1. Compare workflow changes with the area baseline; fail on unauthorized, out-of-area or out-of-scope edits.
2. Recompute backend fingerprint. Reuse fresh passing focused ledger rows; run only missing, failed or stale commands.
3. Verify grouped FR/AC evidence and every applicable mandatory security boundary.
4. Confirm migration is none or generated/reviewed/not applied.
5. Confirm no forbidden database/volume command ran.

Collect all implementation, validation and reviewer failures before one global `backend-developer` correction pass. Reinvoke an architect only for a genuinely new material decision. Reverify once; remaining failure → `BACKEND FAILED`.

## 7. Compact report

Maximum 900 words; never paste agent output or reproduce the spec:

- Result line.
- Spec hash, mode and scope groups.
- Agents invoked/skipped and why.
- Changed files.
- Evidence ledger: FR/AC range · behavior group · implementation · tests · status.
- Validation rows with backend fingerprint.
- Security, persistence fingerprint/review and migration status.
- Only deviations, risks, blockers or pending decisions.

Results:

- `BACKEND COMPLETE`
- `BACKEND BLOCKED`
- `BACKEND FAILED`
