# Frontend

Angular 22 standalone. Configuration: `docs/frontend/frontend-configuration.md`.

## Structure

- `core/`: singleton config, guards, interceptors, models/services; no UI.
- `shared/`: feature-agnostic UI/utilities; no business logic.
- `layout/`: shells. `features/`: lazy feature routes/pages/components/models/services.
- `app.config.ts` registers routing, HttpClient/interceptors and PrimeNG only.
- Move code to `shared/` after a second feature reuses it.
- Signals for local synchronous state; RxJS for async; `inject()` for DI.
- Keep `strict`/`strictTemplates`; avoid `any`, placeholders and speculative abstractions.

## HTTP

- Services own `HttpClient`; build URLs with `buildApiUrl(inject(API_CONFIG), path)`.
- Interceptors affect only `isApiUrl`. Auth uses `withCredentials`; never tokens.
- Handle `ApiError`. UI copy may follow `ApiError.kind`; never display backend ProblemDetails text.
- Environment files are public and secret-free.

## UI

- Reuse FieldOps/PrimeNG/semantic patterns before custom primitives.
- Routine CSS: inspect the nearest precedent/tokens. Read full styling architecture only for global styles, tokens, PrimeNG overrides, breakpoints or shared patterns.
- Aura + teal; dark mode via `.app-dark`. Global styles/tokens stay in `src/styles/`; component SCSS is co-located and uses `--p-*`/`--fo-*` within the 4/8 kB budget.
- No `::ng-deep`, `@import`, global `.p-*` overrides or undocumented `!important`.
- `primeicons` is not installed.
- Implement/test only approved applicable states (loading, empty, error, permission, submission, success); non-applicable states are N/A, never invented.
- Mobile-first; no business logic in templates.

## MCP/browser

- Angular/PrimeNG MCP is lazy: one targeted unresolved installed-API question, then reuse the answer. Installed code/typings win.
- No automatic project/best-practice/docs queries.
- Playwright belongs to final audit only for browser-dependent ACs; use ignored `.playwright-mcp/` output.

## Tests

- Co-locate `*.spec.ts` by behavior; no file/test per component, field or AC.
- Use `TestBed` only for Angular runtime/DI/template/router/HTTP behavior; test pure code directly.
- HTTP tests use `provideHttpClientTesting()`, fake `API_CONFIG` and `HttpTestingController.verify()`.
- Parameterize equivalent inputs. Do not repeat a rule across component/service/browser without distinct risk.
- Reuse unchanged shared auth/session/interceptor evidence.

Focused command from `frontend/`:

`npm run test -- --watch=false --include src/app/features/<feature> --include src/app/core/<changed-file>.spec.ts`

Use only relevant repeatable includes. Full suite belongs to final audit.

## Conventions

- Standard suffixes: `*.service.ts`, `*.interceptor.ts`, `*.model.ts`, `*.config.ts`, `*.routes.ts`; components use `name.ts/html/scss`.
- Imports: packages, blank line, relative; no aliases.
- Do not move files solely for stylistic consistency.
