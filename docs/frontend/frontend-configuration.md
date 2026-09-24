# Frontend configuration

Foundational runtime configuration for the FieldOps Angular application
(`frontend/`).

## Environments

Environment files are compiled into public JavaScript. Never store secrets,
tokens or connection strings in them.

| File | Used by | `apiBaseUrl` |
| --- | --- | --- |
| `src/environments/environment.ts` | `ng serve`, `ng test`, development builds | `http://localhost:5034` |
| `src/environments/environment.production.ts` | `ng build --configuration production` | `/api` |

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
2. `errorInterceptor`: converts failed API responses into `ApiError`
   (`src/app/core/models/api-error.model.ts`) using `ApiErrorService`.

## Error handling

The API returns RFC 9457 ProblemDetails. `ApiErrorService` maps each failure to
a category and a fixed, user-friendly message chosen by HTTP status:

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
  and clears the signal only on success.
- `authGuard` (`core/guards/auth.guard.ts`) allows a route only when
  `GET /sessions/current` returns `200`; anything else redirects to
  `/auth/sign-in`. `guestGuard` redirects to `/overview` on `200` and lets
  the page open otherwise. Both call the endpoint on every activation and
  redirect with `RedirectCommand` and `replaceUrl: true`.
- `app.routes.ts` redirects `''` and every unmatched path (`**`) to
  `/auth/sign-in`. `/auth/**` and `/overview` are lazy loaded.
- Guards are UX only: the backend authorizes every request.

## Technical debt

### Error-contract review for the first form spec

`ApiErrorService` still passes field-level messages from
`ValidationProblemDetails.errors` through unchanged in `ApiError.fieldErrors`.
ASP.NET Core model-binding errors can contain framework details (for example,
"The JSON value could not be converted to System.Guid"). Sign-in filters them at
page level: it shows only the `email`/`password` messages that belong to its
BR-03 allow-list and replaces any other with "Enter a valid value.". Later
forms must apply an equivalent allow-list until `ApiErrorService` defines a
shared validation error contract.
