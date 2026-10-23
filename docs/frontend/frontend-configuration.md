**English** | [Español](frontend-configuration.es.md)

# Frontend configuration

Foundational runtime configuration for the FieldOps Angular application
(`frontend/`).

## Environments

Environment files are compiled into public JavaScript. Never store secrets,
tokens or connection strings in them.

| File                                         | Used by                                   | `apiBaseUrl`            |
| -------------------------------------------- | ----------------------------------------- | ----------------------- |
| `src/environments/environment.ts`            | `ng serve`, `ng test`, development builds | `http://localhost:5034` |
| `src/environments/environment.production.ts` | `ng build --configuration production`     | `/api`                  |

The production file replaces `environment.ts` through `fileReplacements` in
`angular.json`.

## API base URL

Services build endpoint URLs with `buildApiUrl(inject(API_CONFIG), path)`
(`src/app/core/config/api.config.ts`). Interceptors only act on requests whose
URL starts with the configured base URL.

### Production reverse proxy

In production the frontend calls the API on the same origin under `/api`.
The API itself has no `/api` route prefix (for example, health is served at
`/health`), so the reverse proxy must:

- Route `/api/*` to the backend.
- Strip the `/api` prefix before forwarding (`/api/health` → `/health`).
- Serve every other path from the Angular build output, falling back to
  `index.html` for client-side routes.

Because requests are same-origin, production does not depend on the API CORS
configuration.

## Local development

Run the backend with the **`http`** launch profile:

```bash
dotnet run --project backend/src/FieldOps.Api --launch-profile http
```

The `https` profile enables HTTPS redirection, which redirects the frontend's
cross-origin calls to `http://localhost:5034` and breaks them under CORS.
`appsettings.Development.json` already allows the Angular dev server origin
`http://localhost:4200`.

Then start the frontend from `frontend/` with `npm start`.

## HTTP pipeline

Registered in `src/app/app.config.ts`, in order:

1. `authInterceptor`: sets `withCredentials: true` on API URLs only
   (`isApiUrl`), so the browser sends the HttpOnly session cookie. It never
   adds an `Authorization` header or token, and other URLs are left untouched.
2. `portalUnauthorizedInterceptor`: sends a customer back to the portal sign-in when a portal
   request returns `401` while a portal session was in memory (sign-in, invitation and
   password-reset endpoints are excluded).
3. `errorInterceptor`: converts failed API responses into `ApiError`
   (`src/app/core/models/api-error.model.ts`) using `ApiErrorService`.

## Language

English and Spanish, runtime switching and persistence: see [i18n-architecture.md](i18n-architecture.md). `app.config.ts` registers `provideI18n()` (spec-mandated; the frontend CLAUDE.md list predates it).

## Error handling

The API returns RFC 9457 ProblemDetails. `ApiErrorService` maps each failure to
a category and a fixed, user-friendly message chosen by HTTP status (localized through the translation resources, with known `403`/`404`/`409` codes mapped to their own text):

- Backend `title` and `detail` are never shown, and `ApiErrorService` never
  depends on backend exception text.
- A `500` never contains the exception message, type or stack trace, in any
  environment including Development: the client receives a generic
  ProblemDetails. Full exception details exist only in backend logs.
- `traceId` is kept as a support reference and correlates with the backend
  logs.
- API endpoint errors, including handled `500`s, carry the CORS headers for
  allowed origins, so the app served from `http://localhost:4200` receives the
  real status instead of a network error (see
  `docs/backend/api-configuration.md`).
- For `400`/`422` responses with an `errors` object, field-level messages are
  kept in `ApiError.fieldErrors`.
- For `429` responses, `ApiError.retryAfterSeconds` holds the `Retry-After`
  header parsed as a non-negative integer of seconds (trimmed, digits only,
  safe integer). HTTP-date, negative, decimal or empty values and every other
  status give `undefined`. The API CORS policy exposes `Retry-After` so the
  browser can read it cross-origin in development.
- Pages may show page-specific copy chosen by `ApiError.kind` (for example the
  sign-in BR-15 messages), never backend text.

## Authentication and route access

- `SessionService` (`core/services/session.service.ts`) holds the current
  session as a read-only signal. `loadCurrent()` calls `GET /sessions/current`
  and clears the signal on failure; `signIn()` posts `POST /sessions` with the
  email trimmed and lowercased; `signOut()` calls `DELETE /sessions/current`
  and clears the signal only on success. Loading or creating a session also
  applies the user's saved language and theme. `PortalSessionService` does the same for
  `/portal/sessions`.
- `authGuard` (`core/guards/auth.guard.ts`) allows a route only when
  `GET /sessions/current` returns `200`; anything else redirects to
  `/auth/sign-in`. `guestGuard` redirects a visitor who already has a session to
  `landingPath(role)` (`/today` for technicians, `/overview` for everyone else) and lets
  the page open otherwise. `portalGuard` does the same for the customer portal
  (`GET /portal/sessions/current`, redirect to `/portal/sign-in`). All of them call the
  endpoint on every activation and redirect with `RedirectCommand` and `replaceUrl: true`.
- `app.routes.ts` redirects `''` and every unmatched path (`**`) to
  `/auth/sign-in`. Every feature area is lazy loaded (`loadChildren`/`loadComponent`) and
  registers its translation scope with `withI18n`. Public areas (`auth`,
  `request/:slug`, `quotes/view`, `quote-approval`, public invoices, organization
  registration) sit outside the guarded shells.
- `/overview` sends the roles that manage operations (`owner`, `operations_manager`,
  `dispatcher`) to `/dashboard`; other roles see the welcome page.
- Guards are UX only: the backend authorizes every request.

## Application shells

Three shells wrap the guarded routes. The staff and technician shells share the
`authGuard` rules (`runGuardsAndResolvers: 'always'`, so the session is revalidated on every
navigation); the portal shell uses `portalGuard`.

- `layout/app-shell` (staff). Layout mode comes from `matchMedia` (mirrors of
  `_breakpoints.scss` in `core/config/breakpoints.ts`): drawer below 768px, 72px rail from 768px,
  256px sidebar from 1100px. The user's sidebar choice (rail or full) is stored in
  `localStorage` (`fieldops.sidebar.collapsed`); without it the layout mode decides. The
  navigation lives in `layout/shell-sidebar` and is data in `layout/app-shell/app-shell.nav.ts`
  (`NAV_GROUPS`); items and groups are filtered by role code, which is UX only.
- Top bar: global search (`layout/app-shell/global-search`), organization menu (session
  organization only, no request), Help link, notifications bell (popover with unread badge for
  internal roles) and the user menu with account, language and appearance options.
- `layout/technician-shell`: mobile-first shell for technicians (Today's jobs, Schedule, Time,
  Messages, More). Technicians land on `/today`.
- `layout/portal-shell`: customer portal shell with its own navigation and account menu.
- `/coming-soon/:module` (`features/coming-soon`) is a shared placeholder page for
  destinations without a dedicated screen; slugs and names are in
  `core/config/coming-soon-modules.ts`. An unknown slug redirects to the shell's home; the
  page makes no request. `/coming-soon/help` and `/coming-soon/reports` redirect to the real
  Help and Reports pages.
- Self-hosted Inter under `public/fonts/inter/` (OFL license file included) and `primeicons`
  provide typography and icons.

## Field-level errors

`ApiErrorService` keeps field messages from `ValidationProblemDetails.errors` in
`ApiError.fieldErrors` and translates them through `FieldErrorCatalog` (keyed by field and the
exact approved English message). A message that is not in the catalog becomes a generic localized
text, so framework details (for example "The JSON value could not be converted to System.Guid")
never reach the user. The sign-in page additionally keeps an allow-list of the `email` and
`password` messages it shows.
