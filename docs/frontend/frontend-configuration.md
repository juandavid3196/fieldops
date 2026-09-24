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

1. `authInterceptor`: placeholder. Authentication is not implemented, so it
   sends no token. When implemented, credentials must only be attached to API
   URLs.
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

## Technical debt

### Error-contract review for the first form spec

Field-level validation messages from `ValidationProblemDetails.errors` are
currently passed through to the UI unchanged. ASP.NET Core model-binding
errors can contain framework details (for example, "The JSON value could not
be converted to System.Guid"). The first spec that includes a form must define
the validation error contract, including which messages are user-facing, and
update `ApiErrorService` accordingly.
