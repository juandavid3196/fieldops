**English** | [Español](README.es.md)

# FieldOps web app

Angular 22 single-page application for FieldOps: the staff app, the technician mobile shell and the customer portal.
The project overview, architecture and setup live in the [root README](../README.md).

## Quick start

```bash
npm ci
npm start        # http://localhost:4200, expects the API at http://localhost:5034
```

Run the API first with the `http` launch profile (see [Getting started](../README.md#20-getting-started)).

## Scripts

| Task                 | Command                                                       |
| -------------------- | ------------------------------------------------------------- |
| Start dev server     | `npm start`                                                   |
| Production build     | `npm run build -- --configuration production`                 |
| Unit tests (Vitest)  | `npm run test -- --watch=false`                               |
| Lint                 | `npm run lint`                                                |
| Format check / write | `npm run format:check` / `npm run format`                     |
| i18n check           | `npm run i18n:check` (self-test: `npm run i18n:check:test`)   |
| Theme check          | `npm run theme:check` (self-test: `npm run theme:check:test`) |

## Where things live

- `src/app/core`, `shared`, `layout`, `features`: structure described in [`CLAUDE.md`](CLAUDE.md) and [Frontend architecture](../README.md#8-frontend-architecture).
- `src/styles`: design tokens and global styles ([styling architecture](../docs/frontend/styling-architecture.md)).
- `scripts/i18n-check`, `scripts/theme-check`: custom consistency checks that also run in CI.
- Environment files and the production reverse proxy: [frontend configuration](../docs/frontend/frontend-configuration.md).
- Translations: [i18n architecture](../docs/frontend/i18n-architecture.md).
