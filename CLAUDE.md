# FieldOps

Multi-tenant field service platform: each organization schedules and assigns its own workforce. Not a marketplace.

| Path | Contents | Rules |
| --- | --- | --- |
| `frontend/` | Angular 22, PrimeNG 22, SCSS, Vitest | `frontend/CLAUDE.md` |
| `backend/` | .NET 10, EF Core 10, PostgreSQL 17, `FieldOps.slnx` | `backend/CLAUDE.md` |
| `docs/` | Backend, frontend and database references | |
| `.claude/` | Settings, safety hook, agents and skills | |
| `.mcp.json` | Optional project MCP servers | |
| `.githooks/` | Pre-commit and pre-push checks | |
| `.github/workflows/` | CI | |

## Source of truth

1. Approved functional spec.
2. `docs/database/fieldops-schema.sql`.
3. Current architecture/conventions: relevant docs, CLAUDE files and code.
4. Tests.
5. Approved design references.

On conflict, stop and report the exact sources and material options.

## Working rules

- Inspect only affected files and direct dependencies before changing.
- For direct/unstructured work, give a short plan and material assumptions. A delegated spec workflow already supplies a context packet: do not create another plan or reread complete cited documents.
- Stay in approved scope; no unrelated refactors or speculative abstractions.
- Check `git status`; never overwrite, revert or reformat unrelated work.
- Do not commit, push, merge or open PRs unless explicitly asked.
- Never bypass hooks, validation, tests or rules.
- Run the validation profile owned by the current workflow. Reuse passing evidence only on the exact same area fingerprint.

## Git

- Default branch is `main`; `master` is stale. Treat both as protected.
- One concern per branch/PR.
- Branch: `feature|fix|chore|docs/<kebab-description>`, adding the spec ID when available.
- Conventional Commits.

## Commands

Setup commands are conditional, not part of every validation run:

- Frontend: `npm ci` only when dependencies are missing or package manifests changed.
- Backend: `dotnet tool restore` when tools are unavailable/manifest changed; `dotnet restore FieldOps.slnx` when assets are missing or project/manifests changed.

Frontend validation from `frontend/`:

- `npm run format:check`
- `npm run lint`
- `npm run test -- --watch=false`
- `npm run build -- --configuration production`

Backend validation from `backend/`:

- `dotnet format FieldOps.slnx --verify-no-changes --no-restore`
- `dotnet build FieldOps.slnx --configuration Release --no-restore`
- `dotnet test tests/FieldOps.UnitTests/FieldOps.UnitTests.csproj --configuration Release --no-build`
- Integration when required and Docker is already available:
  `dotnet test tests/FieldOps.IntegrationTests/FieldOps.IntegrationTests.csproj --configuration Release --no-build`

Use `FieldOps.slnx`; there is no `FieldOps.sln`. Focused commands live in nested CLAUDE files.

Hooks: pre-commit checks format/lint; pre-push checks frontend test/build and backend build/unit. Integration runs in CI/final audit when relevant; runtime visual QA is manual and user-owned.

## Testing policy

Tests prove distinct behavior and risk, not files, classes, FRs or ACs. One focused test may evidence several requirements.

Default new-test range per feature:

| Level | Default |
| --- | --- |
| Frontend | 3–6 test methods total |
| Backend integration | 3–8 test methods total |
| Backend unit | 0–4; only non-trivial domain, validation, calculation or security utilities |
| Automated browser/E2E by agents | 0; runtime visual QA is supplied by the user |

These are consolidation targets, not reasons to stop approved implementation. Use parameterized/table-driven cases for equivalent inputs. Exceed a default only for distinct high-risk behavior and explain it in the report; no separate user approval is needed when the approved spec already requires that evidence.

Never require:

- One test/file per production file.
- One test per FR/AC or form field.
- The same rule at unit, integration and manual visual-QA levels.
- Tests for trivial DTOs, interfaces, getters, constants, wrappers or configuration already exercised meaningfully.

New evidence is mandatory only when the feature creates or changes the applicable boundary:

- Authentication/session behavior.
- Authorization/permissions or tenant/branch isolation.
- Hashing, secrets, sensitive responses/logging, cookies or CSRF.
- Destructive/unsafe migrations.
- Important multi-record rollback/concurrency.
- Financial integrity.
- Upload authorization/type/size validation.

Do not recreate feature tests for unchanged shared infrastructure when fresh existing tests already prove it. A normal tenant endpoint needs one representative cross-tenant denial per distinct authorization path, not per endpoint/field.

## Validation profiles

| Profile | Contents | Owner |
| --- | --- | --- |
| Focused | Format verification, frontend lint, build and tests for changed behavior plus affected shared boundaries. Relevant focused integration only. | Developers and backend/frontend skills |
| Full | All non-setup validation commands for the changed area, once. Full backend integration when endpoints, persistence, auth/tenancy or other integration behavior changed and Docker is available; otherwise N/A with reason. | Final audit only; CI is authoritative after push |

Run Full outside final audit only for cross-cutting work or when focused selection is impossible.

Evidence row: `command · area · focused|full · pass|fail · fingerprint`. Evidence is fresh only on the same area fingerprint:

`{ git rev-parse HEAD; git diff HEAD --binary -- <area>; git ls-files -o --exclude-standard -- <area>; git ls-files -o --exclude-standard -z -- <area> | xargs -0 -r git hash-object --; } | git hash-object --stdin`

Reports contain changed files, grouped FR/AC evidence, validation rows, applicable security evidence, skipped checks with reason and result. Never paste specs or large agent output.

## Boundaries

Never:

- Apply/drop databases, run `dotnet ef database update|drop`, use `migrations remove --force` or remove Docker volumes.
- Read, print or commit `.env`, user-secrets, passwords, tokens or connection strings.

Only when explicitly requested and within approved scope:

- Generate migrations or SQL scripts.
- Change CI, hooks, `.claude/`, dependencies, architecture or public contracts.
- Delete files/branches, rewrite history, force-push or hard reset.

`.claude/hooks/block-database-mutations.mjs` is a safety net, not authorization. Mapping changes use `database-reviewer`; never invoke nested persistence implementation skills.

## MCP

MCP is supporting evidence, never a mandatory preflight or authority.

- Start each server `NOT CHECKED (no material question)`.
- Query only when repository precedent, installed typings/code and approved docs cannot answer a material version/API question.
- One targeted check by the first stage that needs it; record and reuse the answer. No retries after unavailable/timeout.
- Angular CLI/PrimeNG: frontend API questions. Microsoft Learn: .NET/ASP.NET/EF version questions.
- Agents never invoke Playwright, browser MCPs, runtime Axe, screenshot capture or pixel comparison. The user supplies a fingerprint-matched `USER VISUAL QA REPORT` for applicable visual ACs.
- No database MCP or data-changing UI interaction with the local FieldOps database.
- MCP cannot widen scope, install dependencies, alter contracts or bypass safety/edit boundaries.
- Report only concise conclusions, never documentation dumps.

## Spec workflow

- Business functionality requires an approved spec; explicit infrastructure tasks may proceed without one.
- Route: `spec` → `spec-impl` → delegated `backend`/`frontend` → `final-audit`.
- `spec-impl` classifies each area `ROUTINE`, `TARGETED` or `FULL`; mode changes consultation depth, never security or validation.
- Eligible Full-stack areas run concurrently against final contracts.
- Architects/designers are skipped for routine established patterns and fully specified corrections.
- Only `spec-impl` changes lifecycle after a passing independent audit.
- `--generate-migration` authorizes generation, never application. `--resume` continues the same spec from invalid/incomplete stages using exact fingerprints and authorized paths.
- Never change approved behavior silently.

Done means approved behavior is evidenced, applicable authorization/tenant boundaries are proven, required validation passes, persistence is reviewed, migration is not applied, applicable user visual QA passed on the current frontend fingerprint, docs are current, no unrelated work changed and final audit passed.

## Security

- Resolve organization context server-side; never trust a client organization identifier without authorization.
- Backend authorization is mandatory; frontend guards are UX only.
- Validate uploads by authorization, size and type.
- Never expose sensitive information in responses, logs, source or reports.

## Instruction style

- State rules once at the narrowest scope.
- Use targeted reads and compact reports.
- Link to docs instead of copying them.
- Report outcomes, evidence, risks and decisions—not tool narration.
