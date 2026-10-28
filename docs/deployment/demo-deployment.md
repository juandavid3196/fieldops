# Public demo deployment (Vercel + Railway + Resend)

First public demo of FieldOps: Angular on Vercel, the .NET 10 API and PostgreSQL 17 on Railway, email through the
existing Resend integration. One API instance, no idle sleep, no JWT, SignalR, Redis or external file storage.
Spanish version: [demo-deployment.es.md](demo-deployment.es.md).

## Topology

```text
Browser ──HTTPS──▶ Vercel (https://<site>.vercel.app)
                     ├─ /            Angular static files (+ index.html fallback)
                     └─ /api/*  ──▶  rewrite ──▶ https://<api>.up.railway.app/*   (prefix removed)
                                                     │  Railway edge (TLS, overwrites X-Forwarded-*)
                                                     ▼
                                              API container (port $PORT) ──private network──▶ PostgreSQL
                                                     └─ volume /data (Data Protection keys)
```

- The browser only talks to the Vercel origin. `apiBaseUrl` stays `/api`, so the session cookies
  (`HttpOnly; Secure; SameSite=Strict`, no `Domain`) are first-party cookies of the Vercel domain and `SameSite` is not relaxed.
- The API has no `/api` prefix; Vercel removes it: `/api/health/ready` → `/health/ready`.
- Links in emails (invitation, password reset, portal, quote, invoice) use the first `Cors:AllowedOrigins` entry, so it
  must be the public Vercel origin.

## Exact settings

### Railway – API service

| Setting | Value |
| --- | --- |
| Source | This GitHub repository |
| Root Directory | `backend` |
| Build context | `backend/` (the Root Directory) |
| Builder / Dockerfile path | Dockerfile / `Dockerfile` (i.e. `backend/Dockerfile`) |
| Config as code path | `/backend/railway.json` (set it explicitly; Railway documents config as code as deprecated in favour of Infrastructure as Code, and the same values can be entered in the dashboard) |
| Port | Railway injects `PORT`; the entrypoint listens on `http://0.0.0.0:$PORT` (8080 if unset). Do not define `PORT`. |
| Pre-deploy command | `./efbundle` (applies EF Core migrations; a failure aborts the deploy) |
| Healthcheck path | `/health/ready` (checks the database); timeout 120 s |
| Replicas | 1 |
| Sleep when inactive (Serverless) | Off |
| Volume | Mounted at `/data` (see Data Protection) |
| Public domain | Generate one in Networking; this is the `/api` rewrite destination |

`backend/railway.json` carries the build, pre-deploy, health check, restart policy, `numReplicas: 1` and
`sleepApplication: false` keys that exist in Railway's published schema.

### Railway – PostgreSQL service

PostgreSQL 17 (Railway template or the `postgres:17` image). Extensions: the global-search migration runs
`CREATE EXTENSION IF NOT EXISTS unaccent` (`gen_random_uuid()` is built in since PostgreSQL 13). `unaccent` ships with the
standard Postgres image and is "trusted", and the Railway database user is the owner, so no manual step is expected. If the pre-deploy fails with a
permission error on `CREATE EXTENSION`, run it once as a superuser on the demo database.

### Vercel

| Setting | Value |
| --- | --- |
| Root Directory | `frontend` |
| Framework preset | Other (`vercel.json` sets `framework: null`) |
| Install / Build command | `npm ci` / `node scripts/check-vercel-config.mjs && npm run build -- --configuration production` (from `vercel.json`) |
| Output directory | `dist/fieldops-web/browser` (verified against the production build) |
| Node.js version | 24.x (`engines` in `frontend/package.json`; CI uses Node 24) |
| Environment variables | None |
| Production branch | `main` |

**Pending value – the Railway domain.** `vercel.json` ships with `https://REPLACE-WITH-RAILWAY-API-DOMAIN/:path*`
(a host that cannot resolve). Replace `REPLACE-WITH-RAILWAY-API-DOMAIN` with the public Railway domain (no scheme, no
trailing slash, e.g. `fieldops-api-production.up.railway.app`) and commit. While the placeholder remains, the Vercel
build **fails on purpose** (`scripts/check-vercel-config.mjs`), so the demo cannot go live with a dead API proxy.

What `vercel.json` does:

- `/api/:path*` → Railway, prefix removed, method, query string, body (uploads), cookies and error responses pass through.
- Everything else that is not `/api/…` and has no file extension → `/index.html` (direct links and reloads work). Real files
  are served first, and a missing asset is not turned into HTML because paths with an extension are excluded.
- `Cache-Control: private, no-store` on `/api/*` (Vercel's CDN honours origin cache headers on external rewrites), plus
  `nosniff`, `Referrer-Policy` and `X-Frame-Options: SAMEORIGIN` headers.

### Resend

1. Create an API key (sending access) and verify a sending domain (or use Resend's test sender, which only delivers to
   the account owner's address).
2. `Email__SenderAddress` must belong to the verified domain.
3. Put the key only in `Email__Resend__ApiKey` on Railway.

## Variables

Template with every key: [`backend/railway.env.example`](../../backend/railway.env.example). Vercel needs none.

| Name | Service | Required | Secret | Example / note |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Railway API | Yes (image default `Production`) | No | `Production` |
| `PORT` | Railway API | Injected by Railway | No | Do not set |
| `ConnectionStrings__FieldOpsDatabase` | Railway API | Yes | **Yes** | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}` (Npgsql format, not the `postgres://` URL) |
| `Cors__AllowedOrigins__0` | Railway API | Yes | No | `https://<site>.vercel.app` (first entry builds email links) |
| `AllowedHosts` | Railway API | Yes (recommended) | No | `<api>.up.railway.app;healthcheck.railway.app` |
| `ForwardedHeaders__Enabled` | Railway API | Yes | No | `true` |
| `ForwardedHeaders__TrustAllProxies` | Railway API | One of the trust options | No | `true` (see Proxies) |
| `ForwardedHeaders__KnownProxies__0` / `__KnownNetworks__0` | Railway API | Alternative to `TrustAllProxies` | No | IP / CIDR; mutually exclusive with `TrustAllProxies` |
| `ForwardedHeaders__ForwardLimit` | Railway API | No | No | Default `1` |
| `DataProtection__KeysPath` | Railway API | Yes (Production) | No | `/data/dataprotection` |
| `DataProtection__ApplicationName` | Railway API | No | No | Default `FieldOps`; never change it after launch |
| `Email__Provider` | Railway API | Yes | No | `Resend` (SMTP is rejected in Production) |
| `Email__SenderAddress` | Railway API | Yes | No | `no-reply@<verified-domain>` |
| `Email__SenderName` | Railway API | Yes | No | `FieldOps Demo` |
| `Email__Resend__ApiKey` | Railway API | Yes | **Yes** | Never logged or echoed |
| `Email__AllowedRecipients__0…n` | Railway API | Recommended for a public demo | No | Address or `@domain`; unset = no restriction |
| `Stripe__SecretKey` / `__PublishableKey` / `__WebhookSecret` | Railway API | No | **Yes** | Test mode only (`sk_test_…`, `pk_test_…`, `whsec_…`); unset keeps card payments disabled |
| `BankDetails__EncryptionKey` | Railway API | No | **Yes** | Base64 of 32 random bytes; enables bank transfer details. Keep a copy: losing it makes stored bank data unreadable |
| `DemoSeed__EmailDomain`, `DemoSeed__Password` | Your machine (loader only) | For the loader | Password: **Yes** | Never set on Railway or committed |

## Container

`backend/Dockerfile` (multi-stage, .NET SDK 10 → ASP.NET 10 runtime, target `linux/amd64`):

- Builds Api → Application / Infrastructure / Domain, publishes `Release`, and builds a self-contained
  `linux-x64` EF Core **migration bundle** (`/app/efbundle`). The bundle is only built, never run, during the image build.
- Embedded Inter fonts are loaded by `InterFontResolver` from the assembly, so PDF generation needs no system fonts on
  Linux. Verify PDFs in the checklist below.
- No `USER` and no `ENTRYPOINT`: Railway mounts volumes root-owned, so `docker-entrypoint.sh` starts as root, creates and
  `chown`s `DataProtection__KeysPath` to the `app` user, then drops privileges with `setpriv` before starting the API
  (the API never runs as root). Pre-deploy commands replace `CMD`, so they are not routed through the entrypoint.
- `.dockerignore` excludes `bin/obj`, tests, markdown, `.env*`, user-secrets and local settings.
- The local setup is unchanged (`dotnet run` with the `http` profile on 5034; `docker-compose.yml` still only runs Postgres and Mailpit).
- CI: `.github/workflows/docker-ci.yml` builds the image on backend changes (no push, no database).

## Proxies, HTTPS and sessions

- `ForwardedHeaders__Enabled=true` runs `UseForwardedHeaders` first (only `X-Forwarded-For` and `X-Forwarded-Proto`; the
  Host header is not trusted). Validation on start requires an explicit trust choice: `KnownProxies`/`KnownNetworks`, or
  `TrustAllProxies=true`.
- Railway does not publish its edge address ranges, and its edge overwrites client-supplied `X-Forwarded-*` headers, so the
  documented choice for this demo is `TrustAllProxies=true` with `ForwardLimit=1`. This is only acceptable because the
  container is reachable through Railway's edge; do not expose the service through anything else, and do not enable
  Railway's TCP proxy for the API.
- **Known limitation:** the API sees the address Railway's edge saw, which is Vercel's egress address, not the visitor's.
  Per-IP rate limits (sign-in, public forms, quote/invoice links) therefore pool all visitors of the demo. Per-account
  throttles are unaffected. Restoring the visitor address needs a shared-secret header added at the proxy; out of scope here.
- With forwarded headers enabled the app does not redirect to HTTPS (the edge does; a redirect would break plain-HTTP health
  checks) and sends HSTS outside Development. `AllowedHosts` must include `healthcheck.railway.app`, the host Railway uses
  for health checks, or the deploy never turns healthy.
- Cookie `Secure` is always on, which is correct because every hop the browser sees is HTTPS.

### Data Protection keys

- Setting: `DataProtection__KeysPath` (required in Production, validated on start by creating and writing a probe file).
- Volume mount path: `/data`; keys in `/data/dataprotection`. The entrypoint creates it with owner `app` and mode `700`.
- Stable application identifier: `DataProtection__ApplicationName` (default `FieldOps`). Changing it, or losing the volume,
  signs everyone out; it does not corrupt data.
- Keys are never generated in the image. One replica only: with several, instances would need to share the volume.
- The volume is **not** mounted during pre-deploy. The migration bundle does not need it.

## Database

- Migrations live in `backend/src/FieldOps.Infrastructure/Persistence/Migrations`; the idempotent
  `backend/database-migration-script.sql` already contains the latest one (`AddUserPreferredTheme`). Regenerate it only when a
  new migration is added.
- Railway runs `./efbundle` before each new deployment starts; if it fails the deployment is not promoted. The API never
  migrates on start-up, and nothing drops or recreates a database.
- Types used: PostgreSQL enums (`user_status`, `request_status`, …), `uuid`, `timestamptz`, `numeric`, `jsonb`, `inet`, `bytea`,
  `date`/`time`, plus the `unaccent` extension.
- No schema change was needed for this deployment.

### Commands you run yourself

Migrations are applied by the pre-deploy command. To initialize or update from your machine instead, use Railway's *public*
connection of the database (Postgres service → Variables → `DATABASE_PUBLIC_URL`, converted to Npgsql format):

```powershell
cd backend
dotnet tool restore
dotnet ef migrations bundle --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj `
  --startup-project src/FieldOps.Api/FieldOps.Api.csproj --configuration Release `
  --target-runtime win-x64 --self-contained --output ./efbundle.exe --force
$env:ConnectionStrings__FieldOpsDatabase = "Host=<public-host>;Port=<public-port>;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require"
./efbundle.exe
```

Demo data (only on a database reserved for the demo; idempotent; sends no email; there is no reset):

```powershell
cd backend
$env:ConnectionStrings__FieldOpsDatabase = "<same Npgsql connection string>"
$env:DemoSeed__EmailDomain = "<domain you control or an allowed recipient domain>"
$env:DemoSeed__Password    = "<12+ character password you choose>"
dotnet run --project tools/FieldOps.DemoSeeder -c Release -- --confirm-demo-database
```

The loader refuses to run without `--confirm-demo-database`, with pending migrations, or when the database already holds
other organizations (override only with `--allow-other-organizations`). Re-running it when the demo organization exists does
nothing. It creates the organization "Summit Home Services (Demo)" (slug `fieldops-demo`), two branches, one user per
role (`owner@`, `operations@`, `dispatcher@`, `accounting@`, `viewer@`, `tech.hvac@`, `tech.plumbing@`, `tech.electric@` your
domain), five customers with properties (`customer1@` can sign in to the portal), skills, availability, catalog, requests,
quotes, work orders, visits, invoices and payments in coherent states, and in-app notifications. All accounts share
`DemoSeed__Password`, which is never stored in the repository. Run it only after the pre-deploy migration has completed
once. The email restriction below still applies to anything the app sends afterwards.

## Email restriction for the demo

`Email__AllowedRecipients__n` accepts exact addresses or `@domain` entries (case-insensitive). When any entry exists:

- A message to anyone else is **not sent**; the call fails like a provider failure and only a category is logged.
- Invitations: the invitation is rolled back and the API answers with its existing delivery-failure response (502).
- Password reset: the response stays the same as for any address (no enumeration); nothing is delivered.
- Quotes, invoices, receipts, visit and portal notices: the existing flows record the delivery as failed; they do not claim success.

## Preventing a release with failing checks

- Protect `main`: require the *Backend CI*, *Integration CI*, *Frontend CI* and *Docker image CI* checks and a pull request.
- Railway: deploy from `main` only and enable *Wait for CI* on the service trigger. Vercel: production branch `main`; other
  branches only create previews. A failing pre-deploy migration or health check also stops the Railway release.
- Locally the existing `.githooks` still run before commit and push.

## Backup and recovery

- **Before each deploy that includes a migration** take a logical backup:
  `pg_dump --format=custom --no-owner --file fieldops-<yyyymmdd>.dump "<DATABASE_PUBLIC_URL>"`. Check whether your Railway
  plan offers volume backups and enable them as well; this guide does not depend on them.
- **Code rollback** (redeploy the previous image from Railway's deployment list) does not revert the schema. Migrations are
  forward-only, so keep each one compatible with the previous release for at least one deploy.
- **Data rollback**: restore into a *new* database (`pg_restore --no-owner --dbname "<new-url>" fieldops-<date>.dump`), repoint
  `ConnectionStrings__FieldOpsDatabase`, redeploy. Never restore over the live database.
- Keep a copy of `BankDetails__EncryptionKey` (if used). The Data Protection volume needs no backup; losing it only signs users out.

## Manual checklist

1. `https://<site>/api/health/ready` returns `Healthy`; `https://<site>/` loads.
2. Internal login (`owner@…`), reload, sign out. Wrong password shows the generic error.
3. Portal login (`customer1@…` at the portal sign-in page) and its dashboard.
4. Open `/jobs`, `/customers` or any deep link directly and reload: the app loads, no 404.
5. Redeploy the API from Railway and reload: the session is still valid (keys survived on the volume).
6. Invitation to an allowed address arrives and its link opens on the Vercel domain; an address outside the allow-list fails
   as documented and leaves no pending invitation.
7. Password recovery for an allowed address arrives and opens `…/auth/reset-password#token=…` on the Vercel domain.
8. Notification bell: a new action (e.g. approve a quote) shows within about 30 seconds (polling).
9. Uploads: logo, user photo, request/visit photos, including a file near 10 MB through `/api` (Vercel proxy limits are not
   documented for external rewrites; if a large upload fails, report it).
10. PDFs: download a quote, an invoice, a receipt and a report; fonts and totals look right.
11. Stripe (only if configured, test cards): payment and webhook (`https://<api>/webhooks/stripe` directly on Railway, not via `/api`).
12. No authenticated response is cached: repeat a GET after a change and confirm fresh data.

## Pending data

- Railway public domain (replace the placeholder in `frontend/vercel.json`).
- Vercel public origin (set `Cors__AllowedOrigins__0`, then redeploy the API).
- Verified Resend sender/domain, API key and the allowed-recipient list.
- Demo password and email domain for the loader.
