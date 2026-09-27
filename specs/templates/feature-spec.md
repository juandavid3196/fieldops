# <Feature name>

| Field    | Value                                    |
| -------- | ---------------------------------------- |
| Feature  | `<feature-slug>`                         |
| Type     | Frontend \| Backend \| Full-stack \| UI-only \| Infrastructure |
| Status   | DRAFT                                    |
| Created  | YYYY-MM-DD                               |
| Updated  | YYYY-MM-DD                               |
| Approved | —                                        |

<!--
Rules:
- Delete any section that truly does not apply; do not leave it empty.
- Precise, testable language. No "fast", "intuitive", "etc.", "should ideally".
- Implementation details only when they are architectural constraints; no
  volatile ones (component selectors, deprecated APIs, CSS classes, test file
  names).
- Status values: DRAFT | APPROVED | IMPLEMENTED | AUDITED.
- Only `/spec approve` sets APPROVED. Any change after approval returns the
  status to DRAFT with a Change log entry, except non-behavioral corrections
  through `/spec amend` (status stays APPROVED, labelled Change log row).
- Acceptance criteria: each one independently verifiable behavior; keep
  related assertions together. Aim for ~15–30 active ACs; more than 30 needs
  consolidation, a split, or a security justification.
- Testing requirements: behaviors and risks per level (`CLAUDE.md` Testing
  policy), not one test per AC. Several FRs/ACs may share evidence.
- IDs are never renumbered or reused. A removed item keeps its row as
  `Removed YYYY-MM-DD: <reason>` and is excluded from Traceability.
- Pending schema amendments, pending API contracts, behavior awaiting a
  mockup and unresolved material decisions block approval.
-->

## Context and objective

<Problem being solved and the measurable outcome. 2–4 sentences.>

## Actors and permissions

| Actor  | Can                  | Cannot               |
| ------ | -------------------- | -------------------- |
| <role> | <permitted actions>  | <denied actions>     |

## Scope

- <Included capability>

## Non-goals

- <Explicitly excluded capability>

## User flow

1. <Actor> <action>.
2. System <observable response>.

## Functional requirements

| ID    | Requirement                                   |
| ----- | --------------------------------------------- |
| FR-01 | The system must <observable behavior>.        |

## Business and validation rules

| ID    | Rule                                          | Enforced by           |
| ----- | --------------------------------------------- | --------------------- |
| BR-01 | <rule, with exact limits and formats>         | Backend \| Both       |

## States and transitions

| From | To  | Trigger | Actor | Guard |
| ---- | --- | ------- | ----- | ----- |
|      |     |         |       |       |

## Data and persistence impact

- Tables and columns used (from `docs/database/fieldops-schema.sql`): <list>.
- Schema amendments required: None | <amendment> (Pending blocks approval).

## Tenant isolation and authorization

- Organization context: <how it is resolved server-side>.
- Client-provided organization identifiers: <where accepted, if anywhere>;
  always authorized server-side against the caller, never trusted.
- <Resource> of another organization: <exact response>.
- <Permission required per action>.

## API contracts

Contract status: Final | Pending (Pending blocks approval).

| Method | Path | Request | Success | Errors | Permission |
| ------ | ---- | ------- | ------- | ------ | ---------- |
|        |      |         |         |        |            |

## UI behavior and states

| Screen | Loading | Empty | Error | Permission | Success |
| ------ | ------- | ----- | ----- | ---------- | ------- |
|        |         |       |       |            |         |

- Mockups: None | <reference> (Approved | Pending; Pending blocks approval).
- Responsive and accessibility constraints: <only feature-specific ones>.

## Error behavior

| Case | Response / message shown | Data change |
| ---- | ------------------------ | ----------- |
|      |                          | None        |

## Acceptance criteria

| ID    | Given        | When     | Then                    |
| ----- | ------------ | -------- | ----------------------- |
| AC-01 | <precondition> | <action> | <observable result of one behavior> |

## Testing requirements

| Level                          | Behavior or risk to prove | Evidence for |
| ------------------------------ | ------------------------- | ------------ |
| Backend unit                   | <non-trivial rule only>   | FR-xx, AC-xx |
| Backend integration            | <endpoint/use-case group> | AC-xx, AC-xx |
| Authorization/tenant isolation | <one representative cross-tenant denial; permission checks> | AC-xx |
| Frontend component/service     | <UI behavior and states with distinct logic> | AC-xx, AC-xx |
| Browser (final audit)          | <happy path; critical failure flow if valuable> | AC-xx |

## Dependencies

- <Spec, infrastructure or decision this feature requires, and its status>.

## Assumptions

| ID    | Assumption (minor, non-behavioral) |
| ----- | ---------------------------------- |
| AS-01 |                                    |

## Open decisions

| ID    | Question | Options | Blocking | Resolution |
| ----- | -------- | ------- | -------- | ---------- |
| OD-01 |          |         | Yes      | —          |

## Traceability

| FR    | AC           |
| ----- | ------------ |
| FR-01 | AC-01        |

## Change log

| Date       | Status change      | Reason |
| ---------- | ------------------ | ------ |
| YYYY-MM-DD | — → DRAFT          | Created |
