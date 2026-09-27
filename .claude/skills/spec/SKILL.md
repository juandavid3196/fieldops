---
name: spec
description: Create, revise, validate or approve FieldOps functional specifications in specs/<feature-slug>/spec.md. Writes specs only; never implements code.
argument-hint: create <feature> | revise <spec-path> | validate <spec-path> | approve <spec-path> | amend <spec-path> <correction>
disable-model-invocation: true
---

# FieldOps spec

The spec defines what must be built. It builds nothing. It never approves itself.

Request: `$ARGUMENTS`

Mode = first word. Anything else: show the five modes and stop.

| Mode                   | Result                                                |
| ---------------------- | ----------------------------------------------------- |
| `create <feature>`     | New `specs/<feature-slug>/spec.md`, status DRAFT       |
| `revise <spec-path>`   | Updated spec; APPROVED or later returns to DRAFT       |
| `validate <spec-path>` | Read-only report: `READY FOR APPROVAL` or `NOT READY`  |
| `approve <spec-path>`  | Status APPROVED, only if validation passes             |
| `amend <spec-path> <correction>` | Non-behavioral correction; status stays APPROVED |

## Target path

- The only valid target is a relative path matching
  `^specs/[a-z0-9]+(-[a-z0-9]+)*/spec\.md$`, with a slug other than
  `templates`. `create` derives the slug and checks the same rule.
- Reject absolute paths (`/`, `\`, `~`, drive letters such as `C:`), any
  `..` segment, backslashes and anything else. Report and stop; never
  normalize or guess a corrected path.

## Hard limits

- Write only the validated target. Never edit application code,
  `docs/`, schema, migrations, agents, hooks, CI or `specs/templates/`.
- Never generate migrations or SQL, run builds or start implementation.
- Never set APPROVED outside `approve`. Only the user typing `/spec approve`
  authorizes approval; "looks good" or similar is not approval. Never set
  IMPLEMENTED or AUDITED. Only `amend` edits an APPROVED spec without
  returning it to DRAFT, and only within its allowlist.
- Never invoke `backend-developer`, `frontend-developer`, `database-reviewer`
  or `qa-auditor`.
- Never call MCP tools directly; only the consulted read-only agents may use
  their documentation MCP tools.
- Never invent material decisions. Ask the user when a choice changes
  behavior, security, permissions, data, schema or scope. Record only minor,
  non-behavioral choices as assumptions (`AS-xx`).

## create / revise

1. Classify: Frontend, Backend, Full-stack, UI-only or Infrastructure.
   Infrastructure needs an explicit request, not a spec (`CLAUDE.md`): say so
   and stop unless the user still wants one.
2. Read the relevant nested `CLAUDE.md`, `docs/`, existing specs under
   `specs/`, relevant code, `docs/database/fieldops-schema.sql` for any data
   impact, and mockups the user supplied. `revise`: read the target spec first.
3. Identify goal, actors, scope, non-goals, business rules, dependencies,
   conflicts and missing decisions. Conflicts follow the source-of-truth order
   in `CLAUDE.md`: report both sources and options.
4. Consult read-only agents only for the areas in play:

   | Type       | Agents                                                      |
   | ---------- | ----------------------------------------------------------- |
   | Frontend   | `frontend-architect`; `ui-designer` if screens change       |
   | Backend    | `backend-architect`                                         |
   | Full-stack | `backend-architect`, `frontend-architect`; `ui-designer` if screens change |
   | UI-only    | `ui-designer`                                               |
   | Infrastructure | The architect of each affected area                     |

   Start every brief with `DRAFT REVIEW (spec skill)` and ask only for
   conflicts, feasibility and missing decisions, not an implementation plan.
   If an agent declines, continue without it and say so in the report.
5. Ask the user every blocking question in one batch (`AskUserQuestion` when
   options are clear). Wait for answers before writing affected sections.
6. Write the spec from `specs/templates/feature-spec.md`:
   - Consolidate agent findings in your own words; never paste agent output.
   - Delete sections that do not apply; keep `Tenant isolation and
     authorization` for any business feature.
   - Stable IDs; never renumber or reuse an ID. Mark a removed item
     `Removed YYYY-MM-DD: <reason>` and keep its row so the ID stays reserved.
   - Every active FR maps to ≥1 active AC and vice versa in `Traceability`.
   - Each AC is one independently verifiable behavior; closely related
     assertions of that behavior stay in one AC. Aim for ~15–30 active ACs;
     past 30, consolidate related outcomes or split the feature, unless a
     security-sensitive feature justifies more (say why in the report).
   - `Testing requirements` lists behaviors and risks per level, not one test
     per AC; several FRs/ACs may share evidence (`CLAUDE.md` Testing policy).
   - No volatile implementation details (component selectors, deprecated
     APIs, CSS classes, test file names) unless they are architectural
     constraints.
   - Dates are ISO `YYYY-MM-DD`; set `Updated` on every write.
7. `create`: slug is kebab-case from the feature name; status DRAFT; stop if
   the folder already exists and ask whether to `revise` instead.
8. `revise` of an APPROVED, IMPLEMENTED or AUDITED spec: confirm with the user
   first, then set status DRAFT, clear `Approved`, and add a Change log row
   with the reason. Never silently edit an approved spec; non-behavioral
   corrections use `amend`.
9. Report: file path, status, open decisions (blocking first), assumptions,
   agents consulted, next step (`/spec validate <path>`).

## validate

Read-only. Check and list each failure with its section and ID:

- Metadata complete; status is a valid value.
- No placeholders (`<...>`, `TBD`, `TODO`, `YYYY-MM-DD`) or empty tables.
- Every FR testable; every AC describes one independently verifiable behavior
  (related assertions of it may share the AC); no vague terms.
- Traceability complete both ways for active IDs; removed IDs are excluded
  from traceability but must stay listed; no duplicate or reused IDs.
- Business features: actors, permissions and tenant isolation specified,
  including how the organization context is resolved, server-side
  authorization of any client-provided organization identifier and
  cross-organization access behavior.
- Data impact matches `docs/database/fieldops-schema.sql`.
- API errors and UI loading/empty/error/permission states covered when those
  sections apply.
- Testing requirements describe behavioral coverage by level and cover every
  applicable mandatory security area (`CLAUDE.md` Testing policy), including
  authorization and tenant isolation. They need not map one test per AC.
- Scope and non-goals do not overlap.

Warnings (reported, never blocking on their own):

- More than 30 active ACs: suggest consolidating related outcomes or
  splitting the feature, unless the spec justifies it for security.
- Volatile implementation details that are not architectural constraints.

Approval blockers, each forcing `NOT READY`:

- Any open decision without a resolution that is blocking or affects
  behavior, security, permissions, data, schema, API or scope.
- Any pending schema amendment (not yet in `docs/database/fieldops-schema.sql`).
- Any API contract marked pending or incomplete when the section applies.
- Any behavior that depends on a mockup not supplied or not approved.

Verdict: `READY FOR APPROVAL` or `NOT READY`, then findings, then warnings.

## approve

1. Run `validate`. `NOT READY` → report and stop; status unchanged.
2. Status must be DRAFT; otherwise report and stop.
3. Set status APPROVED, `Approved` and `Updated` to today, add a Change log
   row `DRAFT → APPROVED` with "Approved by user via /spec approve".
4. Change nothing else in the spec.

## amend

Controlled exception for an APPROVED spec. It never broadens: any doubt about
whether an edit changes behavior → refuse.

1. Gate (any failure → report and stop, nothing written): target path valid
   (§ Target path); status exactly APPROVED (DRAFT → `revise`; IMPLEMENTED or
   AUDITED → `revise`); file Git-tracked and clean
   (`git status --porcelain -- <spec-path>` empty); a stated correction
   (none → ask for it); `validate` returns `READY FOR APPROVAL`.
2. Classify every intended edit. Allowed only:

   | Allowed | Examples |
   | ------- | -------- |
   | Grammar, spelling, singular/plural in descriptive prose | "an request" → "a request" |
   | Clarifying text with identical observable behavior | Rewording a context sentence |
   | Documentation paths | Moved `docs/frontend/...` reference |
   | Testing-tool notes | Naming the tool that runs an existing check |
   | Already-approved implementation notes that affect no scope, API, data, permission, security, FR, BR or AC outcome | Fixing a file name in an architectural note |

   Refuse and direct to `/spec revise <spec-path>` when an edit touches: any
   FR, BR or AC meaning or outcome; user-visible copy, messages or quoted
   strings; the API contracts, data impact, tenant isolation, actors or
   permissions, states, error behavior, scope or non-goals; Traceability;
   IDs; metadata other than `Updated`; or security behavior.
3. Apply only the allowed edits. Set `Updated` to today; keep Status,
   `Created` and `Approved`. Append a Change log row:
   `YYYY-MM-DD · APPROVED → APPROVED · Non-behavioral amendment (/spec amend): <what changed>`.
4. Run `validate`. `NOT READY` → restore the file with
   `git restore -- <spec-path>` (safe: it was clean and only this mode
   edited it) and report.
5. Check `git diff -- <spec-path>`: every changed line is an allowed edit,
   `Updated` or the new Change log row; otherwise restore as in step 4.
6. Report: path, each edit with its allowed category, validation verdict,
   status (APPROVED).
