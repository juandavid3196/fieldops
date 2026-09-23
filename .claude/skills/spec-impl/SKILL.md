---
name: spec-impl
description: Implement and audit one approved FieldOps functional specification by orchestrating backend, frontend and final-audit workflows, then update its lifecycle only after successful independent verification.
argument-hint: <spec-path> [--generate-migration] [--resume]
disable-model-invocation: true
---

# FieldOps spec implementation

Orchestrates one APPROVED spec through the `backend`, `frontend` and
`final-audit` skills, then records `IMPLEMENTED → AUDITED`. It writes no
application code, invokes no agent or MCP tool directly (child reports carry
MCP evidence), never applies migrations,
commits, pushes or merges, and never changes approved behavior. Only the user
starts it.

Request: `$ARGUMENTS`

## 1. Gate (read-only; any failure → `SPEC IMPLEMENTATION BLOCKED`, stop)

Read `CLAUDE.md`, `frontend/CLAUDE.md`, `backend/CLAUDE.md` and the spec.

| Check       | Rule |
| ----------- | ---- |
| Arguments   | `<spec-path>` then optionally `--generate-migration` and/or `--resume`, each at most once. Anything else → reject. |
| Path        | Relative, matching `^specs/[a-z0-9]+(-[a-z0-9]+)*/spec\.md$`, slug not `templates`. |
| Path safety | Reject absolute paths (`/`, `\`, `~`, drive letters), backslashes, any `..`, anything outside `specs/`. Never normalize or guess. |
| File        | Exists, Git-tracked (`git ls-files --error-unmatch`) and clean (`git status --porcelain -- <spec-path>` empty). Record its `git hash-object` as the spec hash. |
| Status      | `APPROVED`. DRAFT, IMPLEMENTED or AUDITED → stop. |
| Type        | `Frontend`, `Backend`, `Full-stack` or `UI-only`. `Infrastructure` → stop. |
| FR/AC       | ≥1 active FR and ≥1 active AC; `Traceability` complete both ways for active IDs. |
| Decisions   | No Open decision with `Blocking: Yes` and no resolution. |
| Design      | Every design path the spec lists is exact and exists on disk. An APPROVED spec is authoritative; no other design metadata is required. |
| API         | Where the section applies, every `API contracts` row is complete (method, path, request, success, errors, permission) with no placeholder or `Pending` marker. An APPROVED spec's complete rows are authoritative. |
| Tenancy/auth | Organization resolution, cross-organization behavior and per-action permission defined, or an explicitly approved public/pre-tenant workflow. |
| Data impact | `Data and persistence impact` present; amendments `None` or already in `docs/database/fieldops-schema.sql`. |
| Docs        | Every doc path the spec explicitly requires is under `docs/frontend/` with `frontend` in the route, or `docs/backend/` with `backend` in the route. Anything else (other docs, `docs/database/`, shared content) → ask the user who owns it. |
| Migration flag | §2. |
| Branch      | Not `main` or `master`. |
| Baseline    | Record `HEAD`, `git status --porcelain --untracked-files=all`, and `git hash-object` for every existing changed or untracked regular file (deleted paths: status only). Pre-existing work is never edited, reverted or reformatted unless §3 authorizes it. |
| Prior work  | §3. |

Run all checks and report every failure at once.

## 2. Migration flag

Forward `--generate-migration` only to `backend`, only when the user typed it
in this invocation. Never add it.

| Spec requires EF/database change? | Flag | Outcome |
| --------------------------------- | ---- | ------- |
| Yes, Type `Backend`/`Full-stack`, structure present in `fieldops-schema.sql` | Yes | Forward |
| Yes | No | Blocked: rerun with `--generate-migration` |
| Yes, structure missing from schema SQL | Any | Blocked: schema amendment needed |
| No, or Type `Frontend`/`UI-only` | Yes | Reject the unnecessary flag |
| No | No | No migration |

"Requires" = the data-impact section explicitly states that the EF model or
database changes. Never run `dotnet ef database update|drop`, remove Docker
volumes or apply a generated migration.

## 3. Prior implementation and `--resume`

Prior work = paths changed in `main...HEAD` or baseline paths under
`frontend/` or `backend/`, or spec-required doc paths under `docs/frontend/`
or `docs/backend/`, that are traceable to this spec (its slug, endpoints,
entities, tables, FR/AC IDs in tests, or an earlier workflow report). Never
prior work: `specs/**`, the spec's design paths and handoff files,
`docs/database/**`, and planning, reference or other docs.

| Prior work | `--resume` | Outcome |
| ---------- | ---------- | ------- |
| None | No | Proceed |
| None | Yes | Reject the flag: nothing to resume |
| Found | No | Blocked: list the paths, overwrite nothing, suggest `--resume` |
| Found | Yes | Proceed with the authorized path list |
| Ownership ambiguous | Any | Stop and ask the user (`AskUserQuestion`) |

With `--resume`:

- Authorized paths = baseline paths identified as prior work, each with its
  baseline status and hash. Every other baseline path stays untouchable.
- Report the stage being resumed and its evidence (earlier report or findings
  in this conversation, or supplied by the user; otherwise "not available").
- Start at the first incomplete or responsible stage, then continue the §4
  route from there:

  | Prior result | Resume route |
  | ------------ | ------------ |
  | Backend BLOCKED/FAILED | `backend` → `frontend` if Full-stack → `final-audit` |
  | Frontend BLOCKED/FAILED | `frontend` → `final-audit` |
  | `AUDIT FAIL`, any finding owned by `backend-developer` | `backend` → `frontend` if Full-stack → `final-audit` |
  | `AUDIT FAIL`, findings owned only by `frontend-developer` | `frontend` → `final-audit` |
  | `AUDIT BLOCKED` with no code finding | `final-audit` |
  | Finding owned by user decision or infrastructure | Blocked: resolve it first (`/spec revise` if behavior changes) |
  | Evidence not available or ambiguous | Full §4 route |

- A skipped stage counts as complete only with its earlier COMPLETE report;
  pass that report to later stages and `final-audit`, marked as from the
  earlier run. No report → run the stage.
- Scope stays the approved spec; resume never widens it.
- Route authorized paths by area: `frontend/**` and `docs/frontend/**` to
  `frontend`; `backend/**` and `docs/backend/**` to `backend`. Never
  authorize other docs. Pass each child only its own paths, in its brief, as:

  ```text
  SPEC-IMPL RESUME AUTHORIZATION
  Spec: <spec-path>
  User invoked: /spec-impl <spec-path> --resume [...]
  Resumed stage: <stage and prior result>
  Authorized paths:
  - <status> <path> <hash>
  ```

  Without `--resume`, never send this block.

## 4. Routing

| Type       | Order |
| ---------- | ----- |
| Frontend   | `frontend` → `final-audit` |
| UI-only    | `frontend` → `final-audit` |
| Backend    | `backend` → `final-audit` |
| Full-stack | `backend` → `frontend` → `final-audit` |

Invoke each stage with the Skill tool and wait for its result line. Never
start a stage after an earlier one ended other than COMPLETE/PASS.

### Backend

`/backend <spec-path>` plus `--generate-migration` when §2 forwards it.

- `BACKEND COMPLETE` → continue.
- `BACKEND BLOCKED` / `BACKEND FAILED` → stop; no frontend, no audit.

Retain: scope, changed files, FR/AC matrix, validation, persistence review,
migration status (generated, reviewed, not applied), risks, pending decisions.

### Frontend

`/frontend <spec-path>`. For Full-stack, run only after `BACKEND COMPLETE`
and brief it with the approved API contract rows and the backend report.

- `FRONTEND COMPLETE` → continue.
- `FRONTEND BLOCKED` / `FRONTEND FAILED` → stop; no audit.
- Full-stack: any consumed endpoint still reported as a missing backend
  dependency → `SPEC IMPLEMENTATION FAILED`; no audit.

Retain: scope, changed files, FR/AC matrix, validation, design limitations,
backend dependencies, risks, pending decisions.

### Final audit

`/final-audit <spec-path>`. Provide: child reports, migration status and
non-application evidence, the complete workflow change set, the approved
design paths, and:

```text
SPEC-IMPL AUDIT BASELINE
Spec: <spec-path>
Baseline HEAD: <hash>
Unrelated baseline paths:
- <status> <path> <hash>
Resume-authorized paths:
- <status> <path> <hash>
```

Unrelated = every §1 baseline path not authorized by §3.

| Verdict | Next |
| ------- | ---- |
| `AUDIT PASS`, `AUDIT PASS WITH MINOR FINDINGS` | §5 |
| `AUDIT FAIL` | `SPEC IMPLEMENTATION FAILED` |
| `AUDIT BLOCKED` | `SPEC IMPLEMENTATION BLOCKED` |

On FAIL/BLOCKED: status stays APPROVED; invoke no developer. A correction
requires the user to rerun with `--resume`. Report every finding, keeping the
audit's severity, as: `ID · severity · area · file:line or FR/AC · issue ·
required owner`. Never omit, merge away or downgrade a finding.

## 5. Lifecycle transition

Status stays APPROVED until here. Before editing, re-confirm: every required
child COMPLETE (this run, or an earlier report for a stage skipped by §3),
audit passed in this run, migration not applied, no new blocking decision,
and the spec is still clean with the §1 spec hash. Any doubt → stop and report.

Edit only the target spec:

1. `Status` → `AUDITED`; `Updated` → today; keep `Created` and `Approved`.
2. Append two Change log rows dated today:
   - `APPROVED → IMPLEMENTED` · Required implementation workflows completed.
   - `IMPLEMENTED → AUDITED` · final-audit returned `<exact verdict>`.

Never touch FRs, ACs, contracts, scope or behavior. If the update needs any
other spec change, stop and report. Afterwards `git diff -- <spec-path>` must
show only the Status, Updated and two Change log lines. Do not rerun
`final-audit`. Minor findings go in the report, not the spec.

## 6. Failure integrity

On any BLOCKED or FAILED stage: never revert, delete or clean child changes;
never touch spec lifecycle; list exactly which files changed against the
baseline so `--resume` can pick them up.

## 7. Report

1. Spec path, Type, initial status.
2. Invocation flags.
3. Initial baseline (HEAD, pre-existing paths).
4. Resume status, resumed stage, authorized paths.
5. Workflow order run and skipped (resume: starting stage and why).
6. Child results (exact lines).
7. Backend FR/AC matrix.
8. Frontend FR/AC matrix.
9. Migration status.
10. Final-audit verdict and structured findings (§4 format).
11. Validation summary per command.
12. Lifecycle changes (or "none").
13. Remaining minor findings and risks.
14. Result.
15. Suggested Conventional Commit message; never commit.

| Result | When |
| ------ | ---- |
| `SPEC IMPLEMENTED AND AUDITED` | Audit PASS or PASS WITH MINOR FINDINGS and §5 succeeded. |
| `SPEC IMPLEMENTATION BLOCKED` | Gate failed, decision unresolved, a child BLOCKED, `AUDIT BLOCKED`, or §5 stopped. |
| `SPEC IMPLEMENTATION FAILED` | A child FAILED, an unresolved Full-stack endpoint dependency, or `AUDIT FAIL`. |
