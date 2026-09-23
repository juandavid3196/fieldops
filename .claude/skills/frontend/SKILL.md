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
| AC           | ≥1 active AC covered by `Frontend component/service` in Testing requirements, or tied to a UI screen.  |
| API          | Every endpoint the UI consumes is a complete `API contracts` row (method, path, request, success, errors, permission) with no placeholder, `Pending` marker or unresolved decision. UI-only: none consumed. |
| Design       | Approved = exact path listed in the spec and existing on disk. No other metadata required. List each missing path. |
| Branch       | Not `main` or `master`.                                                                                |
| Baseline     | Record every changed and untracked path (`git status --porcelain --untracked-files=all`) with its status. Record `git hash-object` only for existing regular files; for deleted paths, keep the status entry. These are pre-existing user work: never edited, reverted or reformatted. If the plan needs one, ask the user. |

Run all checks and report every failure at once, not only the first.

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

Design sources:

| Source                               | Defines                         |
| ------------------------------------ | ------------------------------- |
| Approved spec                        | Behavior (wins on conflict)     |
| Approved mockups / Claude Design handoff | Visual intent, interactions |
| `frontend/CLAUDE.md`                 | Implementation conventions      |

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
- Baseline paths: do not touch them.
- Implement only those ACs; add focused co-located tests covering the
  relevant ACs and states (not necessarily one test per AC).
- Edit only `frontend/`; no backend, CI, hook, `.claude/`, dependency or
  lockfile changes; no mock endpoints or hardcoded production data.
- Run the four frontend validations and report each result.

## 5. Verify

1. Compare `git status --porcelain` with the baseline. Workflow changes =
   new paths, plus baseline paths whose content the workflow altered. Any
   workflow change outside `frontend/`, any altered baseline path, or
   unapproved `package.json`/lockfile changes → deviation, `FRONTEND FAILED`.
   Untouched baseline paths are excluded from the diff and the report's
   changed files.
2. Re-run from `frontend/`: `npm run format:check`, `npm run lint`,
   `npm run test -- --watch=false`, `npm run build -- --configuration production`.
3. Map each in-scope FR/AC to files and tests.
4. On failure after the first pass, invoke `frontend-developer` once more as a
   correction pass with concise evidence: failing command, error excerpt,
   uncovered AC IDs. Re-verify. Still failing → `FRONTEND FAILED`; no third
   invocation.

Never: invoke `qa-auditor` (final audit belongs to `final-audit`), change spec
status, commit, push or merge.

## 6. Report

1. Spec path, Type and frontend scope (in/out FR/AC IDs).
2. Agents invoked (and skipped, with reason).
3. Architecture and UI decisions used.
4. Changed files.
5. FR/AC matrix: ID · implementation · test evidence · status.
6. Validation: each command, pass/fail, error excerpt.
7. Visual checks not performed (breakpoints, dark mode, mockup comparison),
   or N/A when the spec has no visual changes.
8. Deviations, risks, backend dependencies, pending decisions.
9. Result:

| Result              | When                                                                   |
| ------------------- | ---------------------------------------------------------------------- |
| `FRONTEND COMPLETE` | Every in-scope FR implemented, every AC has evidence, required design states covered, all four validations pass, no unapproved dependency or scope change. |
| `FRONTEND BLOCKED`  | Gate failed or a decision is pending; nothing implemented after the stop. |
| `FRONTEND FAILED`   | Implementation ran but a validation failed, was skipped without reason, an AC lacks evidence, or out-of-scope changes exist. |
