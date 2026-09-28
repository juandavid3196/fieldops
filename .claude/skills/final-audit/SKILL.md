---
name: final-audit
description: Independently audit a completed FieldOps implementation using a supplied change manifest, reusable persistence evidence, risk-based QA, one full validation pass and browser checks only when an acceptance criterion requires them.
argument-hint: <spec-path>
---

# FieldOps final audit

Audit one implemented APPROVED spec without fixing code, changing lifecycle, committing, applying migrations or invoking developers.

Request: `$ARGUMENTS`

## 1. Invocation mode and gate

### Delegated by spec-impl

Require `SPEC-IMPL FINAL AUDIT PACKET` containing spec path/hash/type, grouped FR/AC evidence, exact workflow change manifest, baseline exclusions, child validation ledgers/fingerprints, persistence result/fingerprint, migration evidence, required browser-dependent ACs and MCP evidence.

Verify only:

- Spec still matches the supplied clean hash and remains APPROVED.
- Branch is not protected.
- Change manifest and current status are resolvable.
- Child fingerprints correspond to current changed areas.

Do not rebuild the parent gate, reclassify unchanged baseline paths or reread complete child/architect reports.

### Direct invocation

Validate one safe relative spec path, APPROVED auditable type, active FR/AC, no blocking decision and non-protected branch. Resolve the implementation from committed branch changes, staged/unstaged changes and untracked files. Exclude clearly unrelated work; ambiguous ownership → `AUDIT BLOCKED`.

Record an integrity baseline before any agent or command.

## 2. Change and scope

Use the delegated manifest or direct-mode resolved change set. Classify paths as frontend, backend, persistence, tests, required docs or out of scope.

Create a compact audit packet:

- Grouped FR/AC → implementation/test evidence.
- Final contract, authorization, tenancy, security and non-goal references.
- Required UI states and browser-dependent ACs.
- Changed files and direct dependencies.
- Validation and persistence evidence with fingerprints.

Inspect changed files, targeted diffs, relevant tests and direct dependencies. Do not scan unrelated code, docs, snapshots or `node_modules`.

## 3. Persistence evidence

Mapping-relevant persistence changes require an independent `database-reviewer` approval.

Reuse the backend-stage result instead of rerunning it when all match:

- Approved spec hash.
- Exact persistence changed-path set and persistence fingerprint.
- Migration fingerprint/status.
- Result is `APPROVED` or `APPROVED WITH NOTES`.
- No persistence path changed after the review.

Invoke a fresh reviewer only when evidence is missing, stale, rejected or the persistence fingerprint changed. Never review the same fingerprint twice in one workflow.

A migration must be reviewed and have explicit non-application evidence. Missing evidence → `NOT VERIFIED`.

## 4. QA audit

Invoke `qa-auditor` once with this exact header, followed by the compact packet, change manifest, reused persistence evidence or review result, validation command list, current fingerprints and browser-dependent AC list:

```text
FINAL AUDIT CONTEXT PACKET
Mode: READ ONLY
```

Require:

- Evidence for every active FR/AC, grouped by behavior and compact ID ranges.
- Exhaustive review of authentication, sessions, authorization, tenant isolation, cross-organization access, sensitive data, public contracts, transactions/rollback/concurrency, destructive operations and migration safety when applicable.
- Representative review for repetitive fields, layout variants and equivalent validation cases.
- Scope/non-goals, meaningful tests, required states, static accessibility, required docs and regressions.

Do not require one test or prose row per AC. Do not pass architect/UI output unless an approved decision cannot be located in the spec.

## 5. Validation

Final audit is the only local owner of the Full profile.

For each changed area:

1. Recompute fingerprint.
2. Reuse fresh passing full rows.
3. Reuse fresh focused rows for area-wide format/lint/build commands.
4. Run only stale, missing or failed commands.
5. Focused test rows never replace required full suites.

Run the Full profile at most once per changed area. Use concise command output and report only summaries/failure excerpts. Integration tests use disposable isolated infrastructure and never start Docker Desktop or target the local database.

Never format, install/change packages, generate/remove/apply migrations, alter snapshots or print secrets.

## 6. Browser evidence

Do not check Playwright merely because frontend files changed.

Use it only when an active AC needs runtime evidence that static review or automated tests cannot provide: responsive rendering, browser navigation, runtime focus/keyboard, contrast/axe or explicit visual comparison.

- Reuse evidence on the same frontend fingerprint.
- Use an already-running local app only.
- Never use real credentials or mutate the local FieldOps database.
- Data-changing behavior uses mocked/intercepted responses or disposable infrastructure.
- Maximum one happy and one critical failure flow.
- Mobile/desktop only for responsive ACs.
- Axe only for accessibility ACs, primary state plus at most one error state.
- Pixel comparison only when explicitly required.

Check Playwright availability lazily at first required use, once, with no retry. If unavailable, only browser-dependent ACs become `NOT VERIFIED`.

## 7. Integrity and verdict

After all reads/commands, compare status and hashes with the audit baseline. Any workflow-created/altered tracked path, altered unrelated baseline path or non-ignored output → `AUDIT FAIL`; never clean or revert.

Verdicts:

- `AUDIT PASS`: complete evidence, no findings.
- `AUDIT PASS WITH MINOR FINDINGS`: only Minor findings; no correction cycle.
- `AUDIT FAIL`: Critical/Important finding, rejected/stale persistence, failed/missing required validation, required FR/AC not met/verified, unsafe migration evidence or integrity failure.
- `AUDIT BLOCKED`: gate/change ownership cannot be resolved.

`N/A` with a reason is not `NOT VERIFIED`.

## 8. Compact report

Maximum 1,500 words unless findings require more:

- Verdict first.
- Spec hash, base and audited change groups.
- Agents invoked/skipped; persistence evidence reused or rerun.
- Grouped FR/AC evidence matrix.
- Security evidence.
- Findings by severity with file/line or FR/AC, expected/actual and owner.
- Validation ledger with fingerprints.
- Browser checks required/performed/not verified.
- Out-of-scope paths and read-only integrity.

Never reproduce the spec or paste child reports.
