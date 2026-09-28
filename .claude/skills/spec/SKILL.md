---
name: spec
description: Create, revise, validate, approve or non-behaviorally amend one FieldOps functional specification under specs/<feature-slug>/spec.md. Writes only the target spec and consults architects/designers only for material unresolved questions.
argument-hint: create <feature> | revise <spec-path> | validate <spec-path> | approve <spec-path> | amend <spec-path> <correction>
disable-model-invocation: true
---

# FieldOps spec

Define approved behavior without implementing it. Never approve implicitly or let an agent approve its own work.

Request: `$ARGUMENTS`

Modes:

- `create <feature>`
- `revise <spec-path>`
- `validate <spec-path>`
- `approve <spec-path>`
- `amend <spec-path> <correction>`

Target paths are safe relative `specs/<kebab-slug>/spec.md`, never `templates`, absolute, backslash-containing or with `..`.

## Limits

- Write only the validated target spec.
- Never edit application code, docs, schema, migrations, agents, hooks or CI.
- Never implement, build, generate SQL/migrations or call developer/reviewer/QA agents.
- Only explicit `/spec approve` sets APPROVED.
- Only `amend` may edit APPROVED without returning it to DRAFT, and only for its strict non-behavioral allowlist.
- Ask once for material decisions affecting behavior, security, permissions, contracts, data, schema or scope. Do not invent them.

## create / revise

### Targeted discovery

Read:

- The target spec first for `revise`.
- Relevant root/nested rules.
- The single closest existing spec/code precedent.
- Only exact docs, schema tables and approved design paths needed by this feature.

Never scan all specs, docs, schema or broad code directories.

Identify goal, actors, scope/non-goals, business rules, contracts, authorization/tenant behavior, persistence impact, dependencies and missing material decisions.

### Conditional consultation

Do not consult agents merely because a spec has backend or screens.

Invoke only when a material question remains:

- `backend-architect`: new contract/boundary, tenancy/transaction/concurrency, schema conflict or backend feasibility uncertainty.
- `frontend-architect`: new route/state/shared abstraction, contract-consumption conflict or frontend feasibility uncertainty.
- `ui-designer`: missing required UI state/responsive/accessibility behavior or spec/mockup conflict.

A routine feature following an established precedent with final contracts and complete approved designs consults no agent. Briefs start `DRAFT REVIEW (spec skill)` and contain only the named questions; request conflict/feasibility/options, never a full implementation plan. Maximum one consultation per relevant agent and no MCP preflight.

Ask all remaining blocking questions in one batch before writing affected sections.

### Write

Use `specs/templates/feature-spec.md`.

- Status DRAFT; ISO dates; update `Updated` on every write.
- Stable IDs; never renumber/reuse removed IDs.
- Every active FR maps to active AC evidence and vice versa.
- Group related assertions into one observable behavior.
- Use tables for repeated field validation instead of one AC per field.
- Target about 12–25 active ACs. Above 25, consolidate or split; exceed 30 only with documented high-risk justification.
- Testing requirements describe behavior/risk groups, not tests per AC/file.
- Avoid volatile selectors, file names, CSS classes or library details unless architectural constraints.
- Preserve tenant isolation/authorization for every business feature.

`create` stops if the slug already exists. `revise` of APPROVED/IMPLEMENTED/AUDITED requires user confirmation, returns to DRAFT, clears Approved and records why. Non-behavioral corrections use `amend`.

Report only path/status, blocking decisions, assumptions, consultations and next command.

## validate

Read-only. Check:

- Complete valid metadata and no placeholders/empty required tables.
- Testable FRs and independently observable, non-duplicative ACs.
- Bidirectional active traceability and reserved removed IDs.
- Actors, permissions, organization resolution, server authorization of client identifiers and cross-organization behavior.
- Data impact against exact authoritative schema sections.
- Complete final API rows where applicable.
- UI loading/empty/error/permission/submission states where applicable.
- Behavior/risk-based testing requirements and applicable security areas.
- Non-overlapping scope/non-goals and existing required design paths.

Warnings:

- More than 25 active ACs; more than 30 requires justification.
- Volatile implementation detail.
- Repeated equivalent cases that should be table-driven.

Block approval for unresolved material decisions, pending schema amendments, incomplete contracts or missing required designs.

Return `READY FOR APPROVAL` or `NOT READY`, then only findings/warnings.

## approve

Run `validate`. Require DRAFT and `READY FOR APPROVAL`. Set Status APPROVED, Approved/Updated to today and append:

`DRAFT → APPROVED · Approved by user via /spec approve`

Change nothing else.

## amend

Require safe path, clean Git-tracked APPROVED spec, stated correction and current `READY FOR APPROVAL`.

Allowed only:

- Grammar/spelling.
- Clarifying prose with identical observable behavior.
- Moved documentation paths.
- Testing-tool notes for an existing check.
- Correcting a non-behavioral implementation reference.

Anything affecting FR/BR/AC meaning, user copy, contracts, data, tenancy, permissions, states, errors, scope, traceability, IDs or security requires `revise`.

Apply only allowed edits, update `Updated`, preserve other metadata and add one APPROVED → APPROVED change-log row. Revalidate and verify the diff contains only allowed changes; otherwise restore the initially clean target and report failure.
