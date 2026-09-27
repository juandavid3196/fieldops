# FieldOps

Multi-tenant field service platform: each organization schedules and assigns
its own workforce. Not a marketplace.

| Path                 | Contents                                                    | Rules                |
| -------------------- | ----------------------------------------------------------- | -------------------- |
| `frontend/`          | Angular 22, PrimeNG 22, SCSS, Vitest                        | `frontend/CLAUDE.md` |
| `backend/`           | .NET 10 Web API, EF Core 10, PostgreSQL 17, `FieldOps.slnx` | `backend/CLAUDE.md`  |
| `docs/`              | Backend, frontend and database reference docs               |                      |
| `.claude/`           | Settings, DB-safety hook, agents, skills                    |                      |
| `.mcp.json`          | Project MCP servers (see MCP below)                         |                      |
| `.githooks/`         | `pre-commit`, `pre-push`                                    |                      |
| `.github/workflows/` | Frontend, backend and integration CI                        |                      |
| `docker-compose.yml` | Local PostgreSQL; reads `.env` (template `.env.example`)    |                      |

## Source of truth

1. Approved functional spec.
2. `docs/database/fieldops-schema.sql`.
3. Existing architecture and conventions (`docs/`, CLAUDE.md files, code).
4. Tests.
5. Mockups and design references.

On any conflict: stop and report both sources and the options.

## Working rules

- Inspect before changing. Present a short plan with assumptions, conflicts
  and missing decisions first.
- Stay in scope: no unrelated refactors or speculative features.
- Check `git status` first; never discard, overwrite or reformat others' work.
- Do not commit, push, merge or open PRs unless asked.
- Never bypass validation (`--no-verify`, skipped hooks, disabled tests or rules).
- Run the commands for every area changed. Never claim completion after a
  failed or skipped check.

## Git

- Default branch `main` (`master` is stale); never work on it directly.
- One concern per branch and PR.
- Branches: `feature|fix|chore|docs/<kebab-description>`, plus the spec ID
  when one exists (`feature/FEAT-012-customer-list`).
- Commits: Conventional Commits, e.g. `chore(frontend): configure core architecture`.

## Commands

```bash
# frontend/
npm ci
npm run format:check
npm run lint
npm run test -- --watch=false
npm run build -- --configuration production

# backend/
dotnet tool restore
dotnet restore FieldOps.slnx
dotnet format FieldOps.slnx --verify-no-changes --no-restore
dotnet build FieldOps.slnx --configuration Release --no-restore
dotnet test tests/FieldOps.UnitTests/FieldOps.UnitTests.csproj --configuration Release --no-build

# backend/ integration (requires Docker Desktop running: Testcontainers)
dotnet test tests/FieldOps.IntegrationTests/FieldOps.IntegrationTests.csproj --configuration Release --no-build
```

- Use `FieldOps.slnx`; there is no `FieldOps.sln`.
- Focused test commands: `frontend/CLAUDE.md`, `backend/CLAUDE.md`.
- Hooks (`git config core.hooksPath .githooks`): pre-commit runs format and lint
  checks; pre-push runs frontend test/build and backend build/unit tests.
  Integration tests and browser audits run only in CI, `final-audit` or manually.

## Testing policy

Tests prove behavior and risk, not files, classes, FRs or ACs. One focused
test may be evidence for several FRs/ACs.

| Level               | Default budget per feature (guidance, not a limit)                        |
| ------------------- | ------------------------------------------------------------------------- |
| Frontend            | ~3–6 focused tests                                                        |
| Backend integration | ~3–6 focused tests per endpoint/use-case group                            |
| Backend unit        | Only non-trivial domain rules, calculations, validators, security utilities |
| Browser (audit)     | One happy path; one critical failure flow only when valuable              |

- Exceeding a budget needs a one-line justification in the workflow report.
- Never required: one test file per production file; one test per FR/AC; the
  same rule at unit, integration and browser level (unless security-critical);
  tests of trivial DTOs, interfaces, getters, constants, wrappers or
  configuration already exercised by an integration test.
- Always required when the feature touches them (never reduced by budgets):
  authentication and sessions; authorization and permissions; cross-organization
  access; password hashing and sensitive data; cookie security and CSRF;
  destructive or unsafe migrations; rollback of important multi-record writes;
  payment or financial integrity; upload validation.
- A normal tenant-owned endpoint needs one representative cross-tenant denial
  test; add more only for distinct code paths with distinct risk.

## Validation profiles

| Profile | Contents | Who runs it |
| ------- | -------- | ----------- |
| Focused | Per changed area: format verification, lint (frontend), build, and only the tests for the changed feature and affected shared behavior, including relevant integration tests. | Developers and the `frontend`/`backend` skills |
| Full    | Per changed area: every command in Commands above (backend integration when `docker info` succeeds). | Locally, only `final-audit`, once, at the end. CI is the authoritative post-push run. |

- Run the full suite outside `final-audit` only when the change is
  cross-cutting or no focused selection is possible; say why in the report.
- Validation ledger: every command in a workflow report is one row
  `command · area · focused|full · pass|fail · stage/time · fingerprint`.
  Evidence is fresh only while the area fingerprint is unchanged; stale or
  missing evidence is rerun, never trusted. Fingerprint (repo root, Git Bash;
  `<area>` = `frontend` or `backend`):

  ```bash
  { git rev-parse HEAD; git diff HEAD --binary -- <area>; git ls-files -o --exclude-standard -- <area>; \
    git ls-files -o --exclude-standard -z -- <area> | xargs -0 -r git hash-object --; } | git hash-object --stdin
  ```

- Workflow reports: changed files; focused tests added and why; shared FR/AC
  evidence; validation ledger; security checks; skipped checks with reason;
  result. Never paste the spec or large agent output.

## Agent boundaries

Never:

- Run `dotnet ef database update` or `dotnet ef database drop`, drop databases
  or remove Docker volumes. The user or approved CI applies migrations.
- Read, print or commit secrets (`.env`, user-secrets).

Only when explicitly requested:

- Generate migrations or SQL scripts.
- Change CI, hooks, `.claude/`, dependencies, architecture or public API contracts.
- Delete files or branches, `git reset --hard`, force push or rewrite history.

`.claude/hooks/block-database-mutations.mjs` is a safety net, not permission.
Project tooling: `implement-persistence-slice` skill, `database-reviewer` agent.

## MCP

| MCP             | Owner                   | Purpose                                                         |
| --------------- | ----------------------- | --------------------------------------------------------------- |
| Angular CLI     | Frontend workflows      | Workspace, official Angular guidance (`--read-only`: no targets) |
| PrimeNG         | Frontend workflows      | Component/API validation, pinned to the installed `primeng`     |
| Microsoft Learn | Backend workflows       | Current Microsoft documentation                                 |
| Playwright      | Frontend QA/final audit | Local browser evidence (`localhost:4200`/`5034` only)           |

- Specs and repository conventions stay authoritative; MCP output is
  supporting evidence, never permission to widen scope.
- MCP cannot bypass hooks, edit boundaries, migration rules or user approval.
- No database MCP. No MCP may access or mutate the local FieldOps PostgreSQL
  database, directly or through data-changing UI submissions.
- Grant agents only the exact `mcp__<server>__<tool>` names they need.
- Unavailable MCP: continue with the existing workflow and report the missing
  verification.
- Report concise MCP results; never paste large documentation responses.

## Spec workflow

- Business functionality requires an approved spec. Infrastructure and
  configuration work needs an explicit request instead.
- Lifecycle: draft → review → approval → implementation → tests → audit.
- Sections: objective, roles and permissions, main flow, business rules, states
  and transitions, entities and relationships, API contract, frontend
  requirements, error cases, acceptance criteria, required tests, out of scope.
- Implement only the active approved spec.
- Never change acceptance criteria silently. Report deviations; update the spec
  only after approval.
- Done: acceptance criteria pass, authorization and tenant isolation tested,
  validation passes, migrations reviewed, docs updated, no unrelated changes,
  audit report produced.
- Specs: `specs/<feature-slug>/spec.md` from `specs/templates/feature-spec.md`,
  managed with the `spec` skill (`/spec create|revise|validate|approve|amend`).
  `amend` only makes non-behavioral corrections and keeps APPROVED.
- Route: `spec` → `spec-impl` → `backend`/`frontend` → `final-audit`.
  `/spec-impl <spec-path> [--generate-migration] [--resume]` is user-invoked
  only; it sets IMPLEMENTED → AUDITED after the audit passes.
  `--generate-migration` authorizes a migration (never applied); `--resume`
  continues an earlier incomplete run of the same spec from its failed stage.

## Security

- Resolve `OrganizationId` from the authenticated context; never trust the client.
- Authorize in the backend; frontend guards are UX only.
- Validate uploads by size, type and authorization.
- Never log passwords, tokens, connection strings or sensitive customer data.

## Instruction style

- Concise, operational rules; state each rule once, in its narrowest scope.
  Root rules are not repeated in nested files.
- Tables, bullets and commands over prose. Document current repository
  behavior only, and only what changes agent decisions.
- Link to `docs/` instead of copying it. Update rules instead of appending.
- Report relevant results only (changed files, validation, risks, pending
  decisions), without narration.
