---
name: spec-impl
description: Implement and independently audit one approved FieldOps specification by running only necessary backend/frontend stages, reusing fingerprinted evidence, then updating lifecycle after a passing audit.
argument-hint: <spec-path> [--generate-migration] [--resume]
disable-model-invocation: true
---

# FieldOps spec implementation

Orchestrate one APPROVED spec. Write no application code, invoke no agent directly, apply no migration and never commit/push/merge. Only the user starts this skill.

Request: `$ARGUMENTS`

## 1. Single gate and baseline

Accept one safe relative `specs/<slug>/spec.md` plus optional unique `--generate-migration` and/or `--resume`.

Read the spec and only the root/nested rules needed to validate:

- Git-tracked, clean APPROVED spec; record hash.
- Supported type with active bidirectionally traced FR/AC.
- No unresolved material/blocking decision.
- Exact existing design paths.
- Complete final contract rows where applicable.
- Authorization/tenant or approved public/pre-tenant rules.
- Data/persistence impact matching authoritative schema.
- Required docs owned by frontend/backend writable areas.
- Non-protected branch.
- HEAD and working-tree baseline with hashes for changed/untracked regular files.

Run all gate checks once and report failures together. Child stages in delegated mode do not repeat them.

Migration:

- Required EF/database change + schema structure + explicit flag → authorize and forward only to backend.
- Required change without flag, or missing authoritative structure → block.
- Unnecessary flag → block.
- Never apply/remove migrations or delete volumes.

## 2. Prior work and resume

Prior implementation is changed frontend/backend or required area-doc paths traceable to this spec. Specs, designs, handoffs, schema/reference/planning docs are not implementation.

- Prior work + no `--resume` → block and list paths.
- No prior work + `--resume` → block.
- Ambiguous ownership → ask once.
- Resume authorizes only exact traceable area paths with baseline status/hash.

Reuse an earlier stage report only when spec hash and area fingerprint still match and no relevant security/requirement changed. Start at the last invalid/failed stage; skip valid earlier stages. Corrections route only to their owning area. Final audit always reruns after a correction; Minor findings do not trigger correction.

Pass authorized paths in an area-specific `SPEC-IMPL RESUME AUTHORIZATION`; never send unrelated baseline paths to a child.

## 3. Mode and compact context packet

Classify each affected area:

- `ROUTINE`: approved behavior follows an existing pattern; final contracts/design states; no unresolved boundary, cross-cutting mechanism, dependency or shared abstraction.
- `TARGETED`: exactly one material architecture/design question.
- `FULL`: new cross-cutting auth/tenancy/state/integration, complex transaction/concurrency or multiple unresolved architecture/design conflicts.

Mode controls consultation, not security or validation depth.

Build one compact packet, target under 1,000 words, containing:

- Header `SPEC-IMPL DELEGATED RUN`.
- Spec path/hash/type/status.
- Area mode and named material question, if any.
- Compact FR/AC behavior-group ranges.
- Contract IDs plus concise request/success/errors/permission.
- Authorization/tenant resolution, cross-organization behavior and permissions.
- Persistence tables/columns and migration authorization.
- Exact design paths and required UI state groups.
- Only the area baseline/resume paths.
- Test budget and applicable mandatory security groups.
- Prior valid ledger rows/fingerprints.
- Recorded targeted MCP answers, or none.

Send each child only its area rows. Do not paste complete spec, agent output, global baseline or unrelated paths.

The orchestrator does not search framework docs or implementation precedents. Children use targeted repository reads. MCP has no eager preflight: every server starts `NOT CHECKED (no material question)`; a child may perform the single lazy check its own rules allow and reports reusable evidence.

## 4. Routing

| Type | Stages |
| --- | --- |
| Frontend/UI-only | frontend → final-audit |
| Backend | backend → final-audit |
| Full-stack | backend + frontend → final-audit |

For Full-stack, run backend and frontend concurrently when final consumed contracts are complete, writable areas do not overlap, no blocker remains and the harness supports concurrent Skill calls. Otherwise run sequentially and report why.

Invoke children with `SPEC-IMPL DELEGATED RUN`. Their own routing decides whether architects/designers are needed from the supplied mode.

Stop on any child BLOCKED/FAILED. Full-stack also fails when a consumed endpoint is missing or mismatches method/path/request/response/errors/permission.

Retain only:

- Exact child result line.
- Changed files.
- Compact evidence ledger.
- Validation rows/fingerprint.
- Security, persistence/migration, dependency and finding data.

Never retain or forward complete narrative reports.

## 5. Final audit packet

After required implementation stages complete, invoke `final-audit` once with `SPEC-IMPL FINAL AUDIT PACKET` containing:

- Spec path/hash/type.
- Implementation change manifest by area.
- Unrelated baseline exclusions with status/hash.
- Grouped FR/AC evidence ledgers.
- Current backend/frontend fingerprints and validation rows.
- Persistence changed paths/fingerprint/reviewer result.
- Migration generated/reviewed/not-applied evidence.
- Browser-dependent ACs only.
- Recorded targeted MCP evidence.

The audit reuses exact-fingerprint evidence and runs only missing/stale checks.

- `AUDIT PASS` / `AUDIT PASS WITH MINOR FINDINGS` → lifecycle.
- `AUDIT FAIL` → `SPEC IMPLEMENTATION FAILED`.
- `AUDIT BLOCKED` → `SPEC IMPLEMENTATION BLOCKED`.

Never invoke a developer after audit. Corrections require user `--resume`.

## 6. Lifecycle

Keep status APPROVED until every required stage completed, audit passed, migration was not applied, no blocker appeared and the spec remains clean with its initial hash.

Then edit only the spec:

- Status → AUDITED.
- Updated → today; preserve Created/Approved.
- Append:
  - `APPROVED → IMPLEMENTED · Required implementation workflows completed.`
  - `IMPLEMENTED → AUDITED · final-audit returned <exact verdict>.`

Diff must contain only those lifecycle fields/rows. Do not rerun audit.

On failure/block: never clean/revert child changes or edit lifecycle; list workflow-changed files for resume.

## 7. Compact report

Maximum 1,200 words:

- Result first.
- Spec/type/hash, flags and ROUTINE/TARGETED/FULL modes.
- Baseline/resume summary.
- Stages and agents invoked/skipped, parallel/sequential reason.
- Changed files.
- Consolidated grouped evidence ledger without duplicating child matrices.
- Validation fingerprints and run/reused summary.
- Security, persistence and migration status.
- Audit verdict and findings with owner.
- Lifecycle change or none.
- Remaining risks and suggested Conventional Commit; never commit.

Results:

- `SPEC IMPLEMENTED AND AUDITED`
- `SPEC IMPLEMENTATION BLOCKED`
- `SPEC IMPLEMENTATION FAILED`
