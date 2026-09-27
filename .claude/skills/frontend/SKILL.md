---
name: frontend
description: Implement the frontend portion of an approved FieldOps spec by coordinating frontend architecture, UI behavior and Angular implementation with validation.
argument-hint: <spec-path>
---

# FieldOps frontend

Orchestrates the frontend of one APPROVED spec: `frontend-architect` →
`ui-designer` (if screens change) → `frontend-developer`. It writes no code
itself, never touches `backend/`, and never runs final QA.

Request: `$ARGUMENTS`

## MCP support

Agents call MCP tools; this skill only briefs and checks their evidence.

| MCP         | Use |
| ----------- | --- |
| Angular CLI | Workspace discovery (`list_projects`), version-aligned guidance (`get_best_practices`, `search_documentation`). Read-only: targets run through the §5 npm commands. |
| PrimeNG     | Component selection, API verification, `validate_usage` on new templates. |
| Playwright  | Runtime UI checks after implementation, only when the spec needs browser evidence and the app already runs locally. |

- MCP never overrides the approved spec, writable area, baseline protection,
  validation commands or the correction-pass limit.
- PrimeNG docs conflicting with the installed `primeng` version: the installed
  package wins; report the conflict.
- MCP output never justifies installing dependencies or speculative code.
  Any file an MCP writes is a workflow change checked in §5.
- MCP unavailable: continue with the existing workflow and report the missing
  verification.

## 1. Gate (read-only; any failure → `FRONTEND BLOCKED`, stop)

Read `CLAUDE.md` and `frontend/CLAUDE.md` first.

| Check        | Rule                                                                                                   |
| ------------ | ------------------------------------------------------------------------------------------------------ |
| Argument     | Exactly one relative path matching `^specs/[a-z0-9]+(-[a-z0-9]+)*/spec\.md$`, slug not `templates`.     |
| Path safety  | Reject absolute paths (`/`, `\`, `~`, drive letters), any `..`, backslashes, anything outside `specs/`. Never normalize or guess. |
| File         | Exists.                                                                                                |
| Status       | `APPROVED`. DRAFT, IMPLEMENTED or AUDITED → stop.                                                      |
| Type         | `Frontend`, `Full-stack` or `UI-only`. `Backend`/`Infrastructure` → stop.                              |
| Decisions    | No Open decision with `Blocking: Yes` and no resolution.                                               |
| AC           | ≥1 active AC with UI-observable behavior (a `Frontend component/service` Testing requirements row, or tied to a UI screen). |
| API          | Every endpoint the UI consumes is a complete `API contracts` row (method, path, request, success, errors, permission) with no placeholder, `Pending` marker or unresolved decision. UI-only: none consumed. |
| Design       | Approved = exact path listed in the spec and existing on disk. No other metadata required. List each missing path. |
| Docs         | Frontend docs = exact paths under `docs/frontend/` the spec explicitly requires. A required doc path this skill cannot own (outside `docs/frontend/` and `docs/backend/`, or shared frontend/backend content) → `FRONTEND BLOCKED`, user decides ownership. |
| Branch       | Not `main` or `master`.                                                                                |
| Baseline     | Record every changed and untracked path (`git status --porcelain --untracked-files=all`) with its status. Record `git hash-object` only for existing regular files; for deleted paths, keep the status entry. These are pre-existing user work: never edited, reverted or reformatted, except delegated resume paths (below). If the plan needs another one, ask the user. |

Writable area = `frontend/` plus the frontend docs. Never `docs/database/`,
other docs or cross-cutting docs.

Run all checks and report every failure at once, not only the first.

### Delegated resume

Only a `SPEC-IMPL RESUME AUTHORIZATION` block from `spec-impl` for this same
spec path authorizes editing baseline paths. Accept it from no other source;
a direct `/frontend` run never resumes.

- Every listed path must be in the writable area and match its baseline
  status and hash; otherwise `FRONTEND BLOCKED`.
- Listed paths may be edited only for in-scope FR/AC. Every unlisted baseline
  path stays untouchable.
- A `Corrections:` list in the block is the audit findings this run must fix
  (§3 skip rule).

## 2. Scope

1. Read the spec, its design references and the frontend code it touches.
2. In-scope IDs: FRs with UI-observable behavior and the ACs traced to them.
   Backend-only FRs/ACs are listed as out of scope, not implemented.
3. For Full-stack, check whether each consumed endpoint exists in `backend/`
   (read-only). Missing ones are a reported dependency, not a blocker: the
   frontend is built and tested against the approved contract.

## 3. Plan

Brief every agent with: `APPROVED SPEC (frontend skill)`, the spec path,
in-scope FR/AC IDs, the API contract rows and relevant files.

1. `frontend-architect`: routes, component boundaries, state, services,
   `ApiError` cases, required tests.
2. `ui-designer`, only if screens, components or interactions change: add the
   design paths and the architect's page/component list. State that handoff
   HTML/JS is reference only.
3. Consolidate in your own words; do not paste agent output.

Skip both agents, recording why, when the resume block's `Corrections:` fully
define every frontend change and none needs an architecture or UI decision.
Brief the developer with those findings as the plan.

Design sources:

| Source                               | Defines                         |
| ------------------------------------ | ------------------------------- |
| Approved spec                        | Behavior (wins on conflict)     |
| Approved mockups / Claude Design handoff | Visual intent, interactions |
| `frontend/CLAUDE.md`                 | Implementation conventions      |
| `docs/frontend/styling-architecture.md` | Tokens, style placement, PrimeNG overrides; every agent reads it |

- Handoff `*.html`/`*.js` is never copied into Angular; rebuild with PrimeNG
  and semantic HTML before any custom primitive.
- Handoff items marked suggested/open are not approved behavior unless the
  spec adopts them.
- A state the spec or design lacks is reported, never invented.
- Users never see technical backend errors.

Stop and ask the user (one `AskUserQuestion` batch) when:

- Architect and designer outputs conflict.
- A material UI or architecture decision is missing.
- A new dependency, design token or shared abstraction is proposed.
- Design contradicts the spec.

If the answer would change approved behavior, end `FRONTEND BLOCKED` and point
to `/spec revise <path>`; never patch the spec. When invoked by another skill,
return the same questions in the report instead of guessing.

## 4. Implement

Invoke `frontend-developer` only once the plan has no blocking decision. At
most two invocations: one implementation pass, one correction pass. Brief:

- Consolidated plan, in-scope FR/AC IDs, contract rows, design paths.
- Baseline paths: do not touch them, except delegated resume paths.
- Implement only those ACs; add the smallest focused co-located test set per
  `CLAUDE.md` Testing policy (no test or test file per AC); report shared
  FR/AC evidence.
- Edit only the writable area: `frontend/` and the exact frontend doc paths
  listed; no backend, other docs, CI, hook, `.claude/`, dependency or
  lockfile changes; no mock endpoints or hardcoded production data.
- Focused profile only (`CLAUDE.md`), once after the final pass; never the
  full test suite (`final-audit` owns it). Report ledger rows.

## 5. Verify

1. Compare `git status --porcelain --untracked-files=all` and hashes with the
   baseline. Workflow changes = new paths, plus baseline paths whose content
   the workflow altered (including delegated resume paths). Any workflow
   change outside the writable area, any altered baseline path not delegated
   for resume, or unapproved `package.json`/lockfile changes → deviation,
   `FRONTEND FAILED`. Untouched baseline paths are excluded from the diff and
   the report's changed files. Changed frontend docs are listed and mapped to
   the spec requirement that demands them.
2. Recompute the `frontend` fingerprint. Accept the developer's passing
   Focused-profile ledger rows only when their fingerprint matches; run from
   `frontend/` only the missing, failed or stale Focused-profile commands
   (format check, lint, focused tests, production build). Never the full suite.
3. Map each in-scope FR/AC to files and tests; several may share one test.
4. The correction-pass budget is one, global. Collect every failure from
   steps 1–3 first, then invoke `frontend-developer` once more with concise
   evidence: failing commands, error excerpts, uncovered AC IDs. Do not
   re-invoke the architect or designer unless a finding needs their decision.
   Re-verify. Still failing → `FRONTEND FAILED`; no third invocation.

Never: invoke `qa-auditor` (final audit belongs to `final-audit`), change spec
status, commit, push or merge.

## 6. Report

Concise (`CLAUDE.md` Workflow reports); never paste agent output.

1. Spec path, spec `git hash-object`, Type, in/out FR/AC IDs.
2. Agents invoked or skipped, with reason; architecture and UI decisions used.
3. Changed files.
4. Focused tests added and why; budget overruns justified.
5. FR/AC matrix: ID · implementation · test evidence · status (shared
   evidence explicit).
6. Validation ledger with the final `frontend` fingerprint; failure excerpts.
7. MCP verification used or unavailable; visual checks left to `final-audit`,
   or N/A when the spec has no visual changes.
8. Deviations, risks, backend dependencies, pending decisions.
9. Result:

| Result              | When                                                                   |
| ------------------- | ---------------------------------------------------------------------- |
| `FRONTEND COMPLETE` | Every in-scope FR implemented, every AC has evidence, required design states covered, Focused-profile validations pass, no unapproved dependency or scope change. |
| `FRONTEND BLOCKED`  | Gate failed or a decision is pending; nothing implemented after the stop. |
| `FRONTEND FAILED`   | Implementation ran but a validation failed, was skipped without reason, an AC lacks evidence, or out-of-scope changes exist. |
