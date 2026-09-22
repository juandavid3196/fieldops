# Frontend

Angular 22 standalone app. Environments, API URLs, proxy and error mapping:
`docs/frontend/frontend-configuration.md`.

## Structure (`src/app`)

| Folder      | Contains                                                                              |
| ----------- | ------------------------------------------------------------------------------------- |
| `core/`     | Singletons: `config/`, `guards/`, `interceptors/`, `models/`, `services/`. No UI.     |
| `shared/`   | Feature-agnostic `components/`, `directives/`, `pipes/`, `utils/`. No business logic. |
| `layout/`   | App shells in `layout/components/`.                                                   |
| `features/` | One folder per feature: `<feature>.routes.ts`, pages, components, models, services.   |

- `app.config.ts` registers only routing, HttpClient with interceptors, and PrimeNG.
- Lazy load every feature from `app.routes.ts`.
- Move code to `shared/` only when a second feature reuses it.
- No empty files, placeholder services or base classes.
- Signals for local synchronous state; RxJS for async streams; `inject()` for DI.
- `strict` and `strictTemplates` stay on; no `any` unless unavoidable.

## HTTP

- Components never inject `HttpClient`; services do.
- Build URLs with `buildApiUrl(inject(API_CONFIG), path)`; never hard-code the origin.
- Interceptors (`core/interceptors/`) act only on `isApiUrl` URLs.
  `authInterceptor` is a placeholder and sends no token.
- Handle `ApiError`, not `HttpErrorResponse`. Show `ApiError.message`, never
  ProblemDetails `title` or `detail`.
- Environment files are public: no secrets.

## UI

- Prefer PrimeNG components over custom complex ones.
- Theme lives in `core/config/primeng.config.ts` (Aura, dark mode via `.app-dark`).
  No new tokens or global overrides before a design-system spec.
- `primeicons` is not installed; `pi pi-*` classes require adding it.
- Component styles: SCSS with `--p-*` variables, within the 4 kB/8 kB budget.
  Keep `src/styles.scss` minimal.
- Every page implements and tests loading, empty, error and permission states.
- Responsive from mobile width; no business logic in templates.

## Conventions

- Files: `*.service.ts`, `*.interceptor.ts`, `*.model.ts`, `*.config.ts`,
  `*.routes.ts`; components as `name.ts`/`name.html`/`name.scss`.
- Imports: packages, blank line, relative. No path aliases.
- Tests: co-located `*.spec.ts` with `TestBed`; HTTP via
  `provideHttpClientTesting()` and `HttpTestingController.verify()`, with a
  fake `API_CONFIG`.
