---
name: spec-impl
description: Implement and audit one approved FieldOps functional specification by orchestrating backend, frontend and final-audit workflows, then update its lifecycle only after successful independent verification.
argument-hint: <spec-path> [--generate-migration] [--resume]
disable-model-invocation: true
---

# FieldOps spec implementation

Orchestrates one APPROVED spec through the `backend`, `frontend` and
`final-audit` skills, then records `IMPLEMENTED → AUDITED`. It writes no
application code, invokes no agent directly, never applies migrations,
commits, pushes or merges, and never changes approved behavior. Only the user
starts it. Its only direct tool use beyond reading files is the bounded MCP
preflight in §1a; every other MCP or agent call happens inside a child skill,
whose report carries the evidence.

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

## 1a. MCP preflight (read-only, bounded, no retries)

Relevant servers by spec Type:

| Type                    | Relevant servers |
| ----------------------- | ----------------- |
| Frontend, UI-only       | `angular-cli`, `primeng`, `playwright` |
| Backend                 | `microsoft-learn`, only if the spec's data-impact or API contract needs a version-specific .NET 10/EF Core/ASP.NET Core answer; otherwise skip the check and mark it `not checked (not needed)` |
| Full-stack              | Union of both rows |

For each relevant server, make exactly one minimal, bounded, read-only
discovery call (e.g. workspace/component/tool listing) with a short timeout.
Never retry a call that fails or times out. Record `AVAILABLE`, or
`UNAVAILABLE (<exact failure or timeout text>)`.

Build once and keep unchanged for the rest of the run:

```text
SPEC-IMPL MCP PREFLIGHT
- <server>: AVAILABLE | UNAVAILABLE (<reason>) | not checked (not relevant/needed)
```

Forward this block to every child skill invocation. A server marked
`UNAVAILABLE` here is never retried by this run, by any child skill, or by any
agent it invokes — they use the recorded result instead. Documentation MCP
(`microsoft-learn`) unavailability never blocks implementation. Playwright
unavailability blocks only an AC that explicitly requires browser evidence
and has no permitted static/mocked fallback (`final-audit` §8 decides this,
using the recorded result, not a fresh check).

## 1b. Context packet

Build one packet, once, from the spec and the §1 baseline already read:

```text
SPEC-IMPL CONTEXT PACKET
Spec: <path> · Type: <type> · Status: APPROVED
In-scope FR/AC: <ids>
API contract: Final · <the exact contract rows the child needs>
Tenant/authorization: <organization resolution, cross-org behavior, permission per action, one line each>
Persistence impact: <tables/columns touched> · Migration authorized: yes (--generate-migration) | no
Design: <approved design paths>
Precedents: <relevant existing files/patterns already identified in this run>
Baseline: HEAD <hash> · authorized paths: <resume list, or "none — first run">
MCP: <§1a block>
Test budget: <CLAUDE.md Testing policy budgets>
Prior stage reports: <valid reports being reused, only when --resume>
```

Send each child only its relevant rows: `backend` gets FR/AC, API contract,
tenant/authorization, persistence, MCP (backend-relevant rows only), test
budget and baseline; `frontend` gets FR/AC, API contract (as consumed),
design, MCP (frontend-relevant rows only), test budget and baseline;
`final-audit` gets the full packet plus the consolidated child reports and
the exact change set. Keep each child's brief concise (target under ~1,500
words); never paste full agent output. Children still read their nested
`CLAUDE.md` and the exact files the packet names — they do not rescan
unrelated areas the packet already resolved.

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
- An earlier child COMPLETE report is valid only when its spec hash equals the
  §1 spec hash (or Git history shows only `/spec amend` non-behavioral
  amendments since that hash; unverifiable → invalid) and its area fingerprint (`CLAUDE.md` Validation
  profiles) equals the current one. Any changed file in that area, security
  behavior or approved requirement invalidates the report and its ledger rows.
- Start at the last incomplete or failed stage, run only it and the later
  required stages, and skip any other stage whose earlier report is valid:

  | Prior result | Resume route |
  | ------------ | ------------ |
  | Backend BLOCKED/FAILED | `backend` → `frontend` if Full-stack and no valid report → `final-audit` |
  | Frontend BLOCKED/FAILED | Full-stack: `backend` only if its report is invalid → `frontend` → `final-audit` |
  | `AUDIT FAIL`, findings owned by `backend-developer` | `backend` → `frontend` only if it owns findings or its report is invalid → `final-audit` |
  | `AUDIT FAIL`, findings owned only by `frontend-developer` | `frontend` → `final-audit` |
  | `AUDIT BLOCKED` with no code finding | `final-audit` |
  | Finding owned by user decision or infrastructure | Blocked: resolve it first (`/spec revise` if behavior changes) |
  | Evidence not available or ambiguous | Full §4 route |

- A skipped stage counts as complete only with its valid earlier COMPLETE
  report; pass that report and its ledger rows to later stages and
  `final-audit`, marked as from the earlier run. No valid report → run the
  stage.
- The APPROVED spec's architecture, contract, tenancy, persistence and design
  decisions are already authoritative; resume never re-derives them through
  an architect or `ui-designer`. A skipped architect/designer stays skipped
  during resume unless a new material conflict appears that the recorded
  decisions do not cover.
- Backend and frontend evidence stay area-scoped: a frontend-only correction
  never invalidates a valid backend report or ledger row, and vice versa.
- `final-audit` always reruns after any correction. Minor findings alone
  never require a correction cycle.
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
  Corrections:
  - <finding ID> · <severity> · <file:line or FR/AC> · <issue> · fully specified: yes|no
  ```

  `Corrections:` lists only this area's audit findings (or `none`), so a
  child skips its architect when every correction is fully specified and
  needs no architectural decision.

  Without `--resume`, never send this block.

## 4. Routing

| Type       | Order |
| ---------- | ----- |
| Frontend   | `frontend` → `final-audit` |
| UI-only    | `frontend` → `final-audit` |
| Backend    | `backend` → `final-audit` |
| Full-stack | `backend` → `frontend` → `final-audit` (§4a: run `backend`/`frontend` concurrently when eligible) |

Invoke each stage with the Skill tool, briefed with its §1b packet rows and
the §1a MCP block, and wait for its result line. Never start a stage after an
earlier one ended other than COMPLETE/PASS.

## 4a. Parallel Full-stack execution

Run `backend` and `frontend` concurrently — one message, two Skill tool
calls — only when every condition holds:

- Type is Full-stack.
- API contract status is Final and every row the frontend consumes is
  complete (already required by §1).
- No unresolved blocking decision remains from §1/§3.
- Writable areas do not overlap: `backend/` + backend docs vs. `frontend/` +
  frontend docs never do.
- This harness supports parallel Skill invocations in one message.

Brief both with the same §1b packet and §1a MCP block. `frontend` implements
against the approved contract and does not wait for backend code or its
report.

After both return:

- Both `BACKEND COMPLETE` and `FRONTEND COMPLETE` → confirm every backend
  endpoint the frontend consumes matches the approved contract (method,
  path, request, response, status codes, permission) using both reports. A
  mismatch, or a consumed endpoint backend did not implement, →
  `SPEC IMPLEMENTATION FAILED`.
- Either stage BLOCKED/FAILED → stop per the Backend/Frontend rules below;
  do not invoke `final-audit`.
- Run `final-audit` only once both stages have returned.

Any condition failing, or the harness not safely supporting parallel Skill
calls this run, falls back to the sequential §4 route; report which
condition failed or that parallel execution was unavailable. Never simulate
concurrency with background shell processes.

On `--resume`, run backend and frontend concurrently only when §3's resume
route requires running both stages (not one skipped as valid) and every §4a
condition still holds; each still receives only its own
`SPEC-IMPL RESUME AUTHORIZATION` block. A resume route needing only one
stage runs it alone, per §3.

### Backend

`/backend <spec-path>` plus `--generate-migration` when §2 forwards it.

- `BACKEND COMPLETE` → continue.
- `BACKEND BLOCKED` / `BACKEND FAILED` → stop; no frontend, no audit.

Retain: spec hash, scope, changed files, FR/AC matrix, validation ledger
with fingerprint, persistence review, migration status (generated, reviewed,
not applied), risks, pending decisions.

### Frontend

`/frontend <spec-path>`, briefed with the §1b packet's API contract rows.
Sequential Full-stack (§4a conditions not met): run only after
`BACKEND COMPLETE` and also brief it with the backend report. Parallel
Full-stack (§4a): run alongside `backend`, briefed only with the approved
contract — it does not wait for the backend report.

- `FRONTEND COMPLETE` → continue.
- `FRONTEND BLOCKED` / `FRONTEND FAILED` → stop; no audit.
- Full-stack: any consumed endpoint still reported as a missing backend
  dependency → `SPEC IMPLEMENTATION FAILED`; no audit.

Retain: spec hash, scope, changed files, FR/AC matrix, validation ledger
with fingerprint, design limitations, backend dependencies, risks, pending
decisions.

### Final audit

`/final-audit <spec-path>`. Provide: the full §1b packet, the §1a MCP block,
child reports (including any dependency-research results they recorded),
migration status and non-application evidence, the complete workflow change
set, the approved design paths, and:

```text
SPEC-IMPL AUDIT BASELINE
Spec: <spec-path>
Baseline HEAD: <hash>
Unrelated baseline paths:
- <status> <path> <hash>
Resume-authorized paths:
- <status> <path> <hash>
Validation ledger:
- <command> · <area> · focused|full · <result> · <stage/run> · <fingerprint>
```

Unrelated = every §1 baseline path not authorized by §3. The ledger holds the
rows of every child report used (this run or valid earlier ones); never
invent or edit rows. `final-audit` reuses only rows whose fingerprint is
still current.

| Verdict | Next |
| ------- | ---- |
| `AUDIT PASS`, `AUDIT PASS WITH MINOR FINDINGS` | §5 |
| `AUDIT FAIL` | `SPEC IMPLEMENTATION FAILED` |
| `AUDIT BLOCKED` | `SPEC IMPLEMENTATION BLOCKED` |

On FAIL/BLOCKED: status stays APPROVED; invoke no developer. A correction
requires the user to rerun with `--resume`. Minor findings never start a
correction cycle. Report every finding, keeping the
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

Concise (`CLAUDE.md` Workflow reports); summarize child reports, never paste
them or the spec.

1. Spec path, Type, initial status, flags.
2. MCP preflight results (§1a); whether backend/frontend ran in parallel
   (§4a) or sequentially, and why.
3. Baseline (HEAD, pre-existing paths); resume status, resumed stage,
   authorized paths, reused reports and why they are still valid.
4. Stages run and skipped, with reason (including skipped architects/
   `ui-designer` and the satisfied gate); child result lines.
5. Changed files.
6. Focused tests added and why; any test-budget approval obtained and from
   whom.
7. FR/AC evidence matrix (backend and frontend), shared evidence explicit.
8. Validation ledger (all rows, focused and full, run and reused).
9. Security checks and migration status.
10. Final-audit verdict and structured findings (§4 format).
11. Skipped checks with reason.
12. Lifecycle changes (or "none"); remaining minor findings and risks.
13. Result, and a suggested Conventional Commit message; never commit.

| Result | When |
| ------ | ---- |
| `SPEC IMPLEMENTED AND AUDITED` | Audit PASS or PASS WITH MINOR FINDINGS and §5 succeeded. |
| `SPEC IMPLEMENTATION BLOCKED` | Gate failed, decision unresolved, a child BLOCKED, `AUDIT BLOCKED`, or §5 stopped. |
| `SPEC IMPLEMENTATION FAILED` | A child FAILED, an unresolved Full-stack endpoint dependency, or `AUDIT FAIL`. |
