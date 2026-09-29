---
name: frontend
description: Implement the frontend portion of an approved FieldOps spec with conditional architecture/UI review, focused Angular tests and compact evidence. Use directly for frontend work or as a delegated stage of spec-impl.
argument-hint: <spec-path>
---

# FieldOps frontend

Implement one approved frontend scope. The skill orchestrates agents but writes no code, never touches `backend/` and never performs final QA.

Request: `$ARGUMENTS`

## 1. Invocation mode and gate

### Delegated by spec-impl

A valid brief starts with `SPEC-IMPL DELEGATED RUN` and includes spec path/hash, mode, frontend FR/AC groups, consumed-contract references, design paths/state summary, area baseline/resume paths and MCP evidence.

Verify only:

- Spec still exists, is clean, has the supplied hash/status/type and no new blocking decision.
- Frontend packet rows and exact design paths remain valid.
- Branch is not `main` or `master`.
- Authorized paths are inside `frontend/` or exact required `docs/frontend/` paths.

Do not repeat the parent gate, rebuild the global baseline or reread complete documents already summarized in the packet.

### Direct invocation

Require one safe relative `specs/<slug>/spec.md` path. Validate existing APPROVED Frontend/Full-stack/UI-only spec, active UI FR/AC, complete consumed API rows, existing approved design paths, no blocking decision, non-protected branch and full working-tree baseline.

Pre-existing paths are read-only unless a matching `SPEC-IMPL RESUME AUTHORIZATION` delegates them. Run all applicable gate checks and report failures together.

## 2. Context and MCP

Build or consume a compact frontend packet:

- FR/AC behavior groups and spec section references.
- Consumed contract IDs with concise request/response/error/permission summary.
- Routes, permission states and required UI state inventory.
- Exact approved mockup/handoff paths.
- Exact relevant code paths and one closest precedent when known.
- Test budget, baseline paths and prior valid evidence.

Use targeted reads. Root/nested `CLAUDE.md`, full spec/handoff or styling/configuration docs are read only when the packet lacks a required rule or its fingerprint changed.

MCP is lazy:

- Angular/PrimeNG start `NOT CHECKED (no question)`.
- One targeted query is allowed only when an unresolved installed API behavior changes implementation.
- PrimeNG `validate_usage` may run once on changed templates.
- Maximum three frontend MCP calls total, no retries, and reuse recorded answers.
- No agent uses Playwright or browser automation. Runtime visual evidence is supplied later by the user's `USER VISUAL QA REPORT`.

Repository precedent and installed typings win.

## 3. Architecture and UI routing

Classify from the packet:

- `ROUTINE`: existing route/feature/form/drawer/table/sidebar pattern, final contracts, complete design states and no new shared abstraction/dependency.
- `TARGETED`: one material architecture or UI gap.
- `FULL`: new routing/state/shared pattern, unresolved design conflict or materially new responsive/interaction behavior.

Routing:

- `ROUTINE`: invoke neither architect nor designer.
- `TARGETED`: invoke only the agent that owns the named gap.
- `FULL`: invoke both only when both architecture and design are genuinely affected.

A fully specified audit correction skips both. If an agent returns `NOT NEEDED`, continue without another planning pass.

The spec defines behavior; approved handoff defines visual intent; repository conventions define implementation. Handoff HTML/JS is reference only and is never copied into Angular.

Stop only for a missing behavior-changing decision, contract conflict, new dependency/token/shared abstraction or design contradiction. Delegated runs return questions; direct runs ask once. Never patch the spec.

## 4. Implement

Invoke `frontend-developer` once with the compact packet, targeted deltas if any, writable area and resume paths.

- Implement only approved behavior with existing Angular, PrimeNG, semantic HTML and FieldOps tokens.
- No backend, unrelated docs, CI, hooks, `.claude/`, dependency or lockfile changes.
- Add the smallest behavior-group tests within the policy; one test may evidence multiple FR/AC IDs.
- Preserve required loading, empty, error, permission, submission, focus and destructive-action states that apply.
- Run the frontend Focused profile once after the final implementation pass; never the full suite.
- No Playwright in implementation.

For Full-stack parallel execution, implement against final approved contracts. Missing backend code is a dependency reported to `spec-impl`; never invent mocks or production data.

## 5. Verify and correction

1. Compare workflow changes with the area baseline; fail on unauthorized, out-of-area, out-of-scope or unapproved manifest edits.
2. Recompute frontend fingerprint. Reuse fresh passing focused ledger rows; run only missing, failed or stale commands.
3. Verify grouped FR/AC evidence and required UI states.
4. Report any backend endpoint dependency or contract mismatch.

Collect all failures before one global `frontend-developer` correction pass. Reinvoke an architect/designer only for a genuinely new material decision. Reverify once; remaining failure → `FRONTEND FAILED`.

## 6. Compact report

Maximum 900 words; never paste agent output or reproduce the spec:

- Result line.
- Spec hash, mode and scope groups.
- Agents invoked/skipped and why.
- Changed files.
- Evidence ledger: FR/AC range · behavior group · implementation · tests · status.
- Validation rows with frontend fingerprint.
- MCP evidence used, backend dependencies and runtime visual AC groups requiring the user's QA report.
- Only deviations, risks, blockers or pending decisions.

Results:

- `FRONTEND COMPLETE`
- `FRONTEND BLOCKED`
- `FRONTEND FAILED`
