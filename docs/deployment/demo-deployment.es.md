# Despliegue de la demo pública (Vercel + Railway + Resend)

Primera demo pública de FieldOps: Angular en Vercel, API .NET 10 y PostgreSQL 17 en Railway, correo mediante la integración
existente con Resend. Una sola instancia de la API, sin suspensión por inactividad, sin JWT, SignalR, Redis ni almacenamiento
externo. Versión en inglés: [demo-deployment.md](demo-deployment.md).

## Topología

```text
Navegador ──HTTPS──▶ Vercel (https://<sitio>.vercel.app)
                       ├─ /            archivos estáticos de Angular (+ fallback a index.html)
                       └─ /api/*  ──▶  rewrite ──▶ https://<api>.up.railway.app/*   (sin el prefijo /api)
                                                       │  Edge de Railway (TLS, sobrescribe X-Forwarded-*)
                                                       ▼
                                                Contenedor API (puerto $PORT) ──red privada──▶ PostgreSQL
                                                       └─ volumen /data (claves de Data Protection)
```

- El navegador solo habla con el origen de Vercel. `apiBaseUrl` sigue siendo `/api`, así que las cookies de sesión
  (`HttpOnly; Secure; SameSite=Strict`, sin `Domain`) son propias del dominio de Vercel y no se relaja `SameSite`.
- La API no tiene prefijo `/api`; Vercel lo quita: `/api/health/ready` → `/health/ready`.
- Los enlaces de los correos (invitación, recuperación, portal, cotización, factura) usan el primer valor de
  `Cors:AllowedOrigins`, que debe ser el origen público de Vercel.

## Configuración exacta

### Railway – servicio API

| Ajuste | Valor |
| --- | --- |
| Origen | Este repositorio de GitHub |
| Root Directory | `backend` |
| Contexto de build | `backend/` (el Root Directory) |
| Builder / ruta del Dockerfile | Dockerfile / `Dockerfile` (es decir `backend/Dockerfile`) |
| Ruta de config as code | `/backend/railway.json` (indícala explícitamente; Railway marca config as code como obsoleto a favor de Infrastructure as Code, y los mismos valores pueden ponerse en el panel) |
| Puerto | Railway inyecta `PORT`; el entrypoint escucha en `http://0.0.0.0:$PORT` (8080 si falta). No definas `PORT`. |
| Pre-deploy | `./efbundle` (aplica las migraciones; si falla, el despliegue se detiene) |
| Healthcheck | `/health/ready` (comprueba la base de datos); timeout 120 s |
| Réplicas | 1 |
| Suspensión por inactividad (Serverless) | Desactivada |
| Volumen | Montado en `/data` (ver Data Protection) |
| Dominio público | Genéralo en Networking; es el destino del rewrite de `/api` |

`backend/railway.json` contiene solo claves del esquema publicado por Railway: build, pre-deploy, healthcheck, política de
reinicio, `numReplicas: 1` y `sleepApplication: false`.

### Railway – servicio PostgreSQL

PostgreSQL 17 (plantilla de Railway o imagen `postgres:17`). Extensiones: la migración de búsqueda global ejecuta
`CREATE EXTENSION IF NOT EXISTS unaccent` (`gen_random_uuid()` es nativa desde PostgreSQL 13). `unaccent` viene con la imagen
estándar y es "trusted"; el usuario de Railway es propietario, así que no se espera un paso manual. Si el pre-deploy falla por permisos en `CREATE EXTENSION`,
ejecútalo una vez como superusuario en la base demo.

### Vercel

| Ajuste | Valor |
| --- | --- |
| Root Directory | `frontend` |
| Framework preset | Other (`vercel.json` fija `framework: null`) |
| Install / Build | `npm ci` / `node scripts/check-vercel-config.mjs && npm run build -- --configuration production` (de `vercel.json`) |
| Output directory | `dist/fieldops-web/browser` (verificado con el build de producción) |
| Node.js | 24.x (`engines` en `frontend/package.json`; CI usa Node 24) |
| Variables de entorno | Ninguna |
| Rama de producción | `main` |

**Dato pendiente – dominio de Railway.** `vercel.json` incluye `https://REPLACE-WITH-RAILWAY-API-DOMAIN/:path*` (un host que
no resuelve). Sustituye `REPLACE-WITH-RAILWAY-API-DOMAIN` por el dominio público de Railway (sin esquema ni barra final, p. ej.
`fieldops-api-production.up.railway.app`) y haz commit. Mientras quede el marcador, el build de Vercel **falla a propósito**
(`scripts/check-vercel-config.mjs`), de modo que la demo no sale con un proxy de API muerto.

Qué hace `vercel.json`:

- `/api/:path*` → Railway, sin el prefijo; método, query string, cuerpo (uploads), cookies y errores pasan intactos.
- Todo lo que no sea `/api/…` y no tenga extensión de archivo → `/index.html` (enlaces directos y recargas). Los archivos
  reales se sirven primero y un asset inexistente no se convierte en HTML porque las rutas con extensión se excluyen.
- `Cache-Control: private, no-store` en `/api/*` (la CDN de Vercel respeta la caché del origen en rewrites externos), más
  `nosniff`, `Referrer-Policy` y `X-Frame-Options: SAMEORIGIN`.

### Resend

1. Crea una API key (acceso de envío) y verifica un dominio de envío (o usa el remitente de prueba de Resend, que solo
   entrega a la dirección del propietario de la cuenta).
2. `Email__SenderAddress` debe pertenecer al dominio verificado.
3. La clave va solo en `Email__Resend__ApiKey` en Railway.

## Variables

Plantilla con todas las claves: [`backend/railway.env.example`](../../backend/railway.env.example). Vercel no necesita ninguna.

| Nombre | Servicio | Obligatoria | Secreto | Ejemplo / nota |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Railway API | Sí (la imagen ya usa `Production`) | No | `Production` |
| `PORT` | Railway API | La inyecta Railway | No | No definir |
| `ConnectionStrings__FieldOpsDatabase` | Railway API | Sí | **Sí** | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}` (formato Npgsql, no la URL `postgres://`) |
| `Cors__AllowedOrigins__0` | Railway API | Sí | No | `https://<sitio>.vercel.app` (el primero construye los enlaces de correo) |
| `AllowedHosts` | Railway API | Sí (recomendada) | No | `<api>.up.railway.app;healthcheck.railway.app` |
| `ForwardedHeaders__Enabled` | Railway API | Sí | No | `true` |
| `ForwardedHeaders__TrustAllProxies` | Railway API | Una de las opciones de confianza | No | `true` (ver Proxies) |
| `ForwardedHeaders__KnownProxies__0` / `__KnownNetworks__0` | Railway API | Alternativa a `TrustAllProxies` | No | IP / CIDR; excluyente con `TrustAllProxies` |
| `ForwardedHeaders__ForwardLimit` | Railway API | No | No | Por defecto `1` |
| `DataProtection__KeysPath` | Railway API | Sí (Production) | No | `/data/dataprotection` |
| `DataProtection__ApplicationName` | Railway API | No | No | Por defecto `FieldOps`; no lo cambies tras el lanzamiento |
| `Email__Provider` | Railway API | Sí | No | `Resend` (SMTP se rechaza en Production) |
| `Email__SenderAddress` | Railway API | Sí | No | `no-reply@<dominio-verificado>` |
| `Email__SenderName` | Railway API | Sí | No | `FieldOps Demo` |
| `Email__Resend__ApiKey` | Railway API | Sí | **Sí** | Nunca se registra ni se devuelve |
| `Email__AllowedRecipients__0…n` | Railway API | Recomendada en una demo pública | No | Dirección o `@dominio`; sin definir = sin restricción |
| `Stripe__SecretKey` / `__PublishableKey` / `__WebhookSecret` | Railway API | No | **Sí** | Solo modo de prueba (`sk_test_…`, `pk_test_…`, `whsec_…`); sin definir, los pagos con tarjeta quedan deshabilitados |
| `BankDetails__EncryptionKey` | Railway API | No | **Sí** | Base64 de 32 bytes aleatorios; habilita datos de transferencia bancaria. Guarda una copia: perderla deja ilegibles los datos bancarios guardados |
| `DemoSeed__EmailDomain`, `DemoSeed__Password` | Tu máquina (solo cargador) | Para el cargador | Password: **Sí** | Nunca en Railway ni en el repositorio |

## Contenedor

`backend/Dockerfile` (multietapa, SDK .NET 10 → runtime ASP.NET 10, `linux/amd64`):

- Compila Api → Application / Infrastructure / Domain, publica en `Release` y genera un **bundle de migraciones** EF Core
  autocontenido `linux-x64` (`/app/efbundle`). El bundle solo se construye; nunca se ejecuta durante el build.
- Las fuentes Inter embebidas las carga `InterFontResolver` desde el ensamblado, así que el PDF no necesita fuentes del
  sistema en Linux. Compruébalo en la checklist.
- Sin `USER` ni `ENTRYPOINT`: Railway monta los volúmenes como root, por lo que `docker-entrypoint.sh` arranca como root, crea
  `DataProtection__KeysPath`, lo entrega al usuario `app` y baja privilegios con `setpriv` antes de iniciar la API (la API
  nunca corre como root). El pre-deploy reemplaza `CMD`, así que no pasa por el entrypoint.
- `.dockerignore` excluye `bin/obj`, tests, markdown, `.env*`, user-secrets y ajustes locales.
- El entorno local no cambia (`dotnet run` con el perfil `http` en 5034; `docker-compose.yml` sigue levantando solo Postgres y Mailpit).
- CI: `.github/workflows/docker-ci.yml` construye la imagen cuando cambia `backend/` (sin push ni base de datos).

## Proxies, HTTPS y sesiones

- `ForwardedHeaders__Enabled=true` ejecuta `UseForwardedHeaders` primero (solo `X-Forwarded-For` y `X-Forwarded-Proto`; la
  cabecera Host no se confía). La validación al arrancar exige una confianza explícita: `KnownProxies`/`KnownNetworks` o
  `TrustAllProxies=true`.
- Railway no publica los rangos de su edge y este sobrescribe las `X-Forwarded-*` del cliente, por lo que la opción documentada
  para esta demo es `TrustAllProxies=true` con `ForwardLimit=1`. Solo es aceptable porque el contenedor se alcanza a través
  del edge de Railway; no lo expongas por otra vía ni actives el TCP proxy de Railway para la API.
- **Limitación conocida:** la API ve la dirección que vio el edge de Railway, es decir la de salida de Vercel, no la del
  visitante. Los límites por IP (inicio de sesión, formularios públicos, enlaces de cotización/factura) agrupan a todos los
  visitantes de la demo. Los límites por cuenta no se ven afectados. Recuperar la IP real requiere una cabecera con secreto
  compartido añadida en el proxy; queda fuera de este alcance.
- Con forwarded headers activos la app no redirige a HTTPS (lo hace el edge; una redirección rompería los health checks HTTP
  planos) y envía HSTS fuera de Development. `AllowedHosts` debe incluir `healthcheck.railway.app`, el host que Railway usa
  para sus health checks, o el despliegue nunca pasa a sano.
- La cookie `Secure` siempre está activa, correcto porque todo salto que ve el navegador es HTTPS.

### Claves de Data Protection

- Ajuste: `DataProtection__KeysPath` (obligatorio en Production; al arrancar se valida creando y escribiendo un archivo de prueba).
- Ruta de montaje del volumen: `/data`; claves en `/data/dataprotection`. El entrypoint la crea con propietario `app` y modo `700`.
- Identificador estable de la aplicación: `DataProtection__ApplicationName` (por defecto `FieldOps`). Cambiarlo, o perder el
  volumen, cierra la sesión a todos; no corrompe datos.
- Las claves nunca se generan en la imagen. Una sola réplica: con varias habría que compartir el volumen.
- El volumen **no** está montado durante el pre-deploy; el bundle de migraciones no lo necesita.

## Base de datos

- Las migraciones están en `backend/src/FieldOps.Infrastructure/Persistence/Migrations`; el script idempotente
  `backend/database-migration-script.sql` ya contiene la última (`AddUserPreferredTheme`). Regenéralo solo al añadir otra.
- Railway ejecuta `./efbundle` antes de cada nuevo despliegue; si falla, no se promueve. La API nunca migra al arrancar y nada
  elimina ni recrea bases de datos.
- Tipos usados: enums de PostgreSQL (`user_status`, `request_status`, …), `uuid`, `timestamptz`, `numeric`, `jsonb`, `inet`,
  `bytea`, `date`/`time`, más la extensión `unaccent`.
- Este despliegue no requirió cambios de esquema.

### Comandos que ejecutas tú

Las migraciones las aplica el pre-deploy. Para inicializar o actualizar desde tu máquina, usa la conexión *pública* de la base en
Railway (servicio Postgres → Variables → `DATABASE_PUBLIC_URL`, convertida al formato Npgsql):

```powershell
cd backend
dotnet tool restore
dotnet ef migrations bundle --project src/FieldOps.Infrastructure/FieldOps.Infrastructure.csproj `
  --startup-project src/FieldOps.Api/FieldOps.Api.csproj --configuration Release `
  --target-runtime win-x64 --self-contained --output ./efbundle.exe --force
$env:ConnectionStrings__FieldOpsDatabase = "Host=<host-publico>;Port=<puerto-publico>;Database=<db>;Username=<usuario>;Password=<password>;SSL Mode=Require"
./efbundle.exe
```

Datos demo (solo en una base reservada para la demo; idempotente; no envía correos; no hay reset):

```powershell
cd backend
$env:ConnectionStrings__FieldOpsDatabase = "<la misma cadena Npgsql>"
$env:DemoSeed__EmailDomain = "<dominio que controlas o dominio de un destinatario permitido>"
$env:DemoSeed__Password    = "<contraseña de 12+ caracteres que elijas>"
dotnet run --project tools/FieldOps.DemoSeeder -c Release -- --confirm-demo-database
```

El cargador se niega a ejecutarse sin `--confirm-demo-database`, con migraciones pendientes o si la base ya tiene otras
organizaciones (solo se anula con `--allow-other-organizations`). Si la organización demo ya existe, no hace nada. Crea la
organización "Summit Home Services (Demo)" (slug `fieldops-demo`), dos sucursales, un usuario por rol (`owner@`,
`operations@`, `dispatcher@`, `accounting@`, `viewer@`, `tech.hvac@`, `tech.plumbing@`, `tech.electric@` tu dominio), cinco
clientes con propiedades (`customer1@` entra al portal), habilidades, disponibilidad, catálogo, solicitudes, cotizaciones,
órdenes, visitas, facturas y pagos en estados coherentes, y notificaciones in-app. Todas las cuentas comparten
`DemoSeed__Password`, que nunca se guarda en el repositorio. Ejecútalo solo después de que el pre-deploy haya aplicado las
migraciones al menos una vez. La restricción de correo de abajo sigue aplicando a todo lo que la app envíe después.

## Restricción de correo para la demo

`Email__AllowedRecipients__n` acepta direcciones exactas o entradas `@dominio` (sin distinguir mayúsculas). Con al menos una:

- Un mensaje a cualquier otro destinatario **no se envía**; falla como un fallo del proveedor y solo se registra una categoría.
- Invitaciones: la invitación se revierte y la API responde con su error de entrega existente (502).
- Recuperación de contraseña: la respuesta es la misma que para cualquier dirección (sin enumeración); no se entrega nada.
- Cotizaciones, facturas, recibos, avisos de visita y portal: los flujos existentes registran la entrega como fallida; no
  declaran éxito.

## Evitar publicar con checks fallidos

- Protege `main`: exige los checks *Backend CI*, *Integration CI*, *Frontend CI* y *Docker image CI* y un pull request.
- Railway: despliega solo desde `main` y activa *Wait for CI* en el trigger del servicio. Vercel: rama de producción `main`;
  las demás solo generan previews. Un pre-deploy de migraciones o un health check fallidos también detienen la release en Railway.
- En local, los `.githooks` existentes siguen corriendo antes de commit y push.

## Backup y recuperación

- **Antes de cada despliegue con migración** haz un backup lógico:
  `pg_dump --format=custom --no-owner --file fieldops-<aaaammdd>.dump "<DATABASE_PUBLIC_URL>"`. Comprueba si tu plan de
  Railway ofrece backups de volumen y actívalos también; esta guía no depende de ellos.
- **Rollback de código** (redesplegar la imagen anterior desde la lista de despliegues de Railway) no revierte el esquema. Las
  migraciones solo avanzan, así que mantén cada una compatible con la versión anterior durante al menos un despliegue.
- **Rollback de datos**: restaura en una base *nueva* (`pg_restore --no-owner --dbname "<url-nueva>" fieldops-<fecha>.dump`),
  cambia `ConnectionStrings__FieldOpsDatabase` y redespliega. Nunca restaures sobre la base en uso.
- Conserva una copia de `BankDetails__EncryptionKey` (si se usa). El volumen de Data Protection no necesita backup; perderlo solo cierra sesiones.

## Checklist manual

1. `https://<sitio>/api/health/ready` responde `Healthy`; `https://<sitio>/` carga.
2. Login interno (`owner@…`), recarga y cierre de sesión. Una contraseña incorrecta muestra el error genérico.
3. Login del portal (`customer1@…` en la pantalla de acceso del portal) y su panel.
4. Abre directamente `/jobs`, `/customers` u otro enlace profundo y recarga: la app carga, sin 404.
5. Redespliega la API desde Railway y recarga: la sesión sigue válida (las claves sobrevivieron en el volumen).
6. La invitación a una dirección permitida llega y su enlace abre el dominio de Vercel; una dirección fuera de la lista falla
   como se documenta y no deja invitación pendiente.
7. La recuperación de contraseña para una dirección permitida llega y abre `…/auth/reset-password#token=…` en Vercel.
8. Campana de notificaciones: una acción nueva (p. ej. aprobar una cotización) aparece en unos 30 segundos (polling).
9. Uploads: logo, foto de usuario, fotos de solicitud/visita, incluido un archivo cercano a 10 MB por `/api` (los límites del
   proxy de Vercel no están documentados para rewrites externos; si un upload grande falla, repórtalo).
10. PDFs: descarga una cotización, una factura, un recibo y un reporte; fuentes y totales correctos.
11. Stripe (solo si se configuró, tarjetas de prueba): pago y webhook (`https://<api>/webhooks/stripe` directo a Railway, no por `/api`).
12. Ninguna respuesta autenticada se cachea: repite un GET tras un cambio y confirma datos frescos.

## Datos pendientes

- Dominio público de Railway (sustituir el marcador en `frontend/vercel.json`).
- Origen público de Vercel (definir `Cors__AllowedOrigins__0` y redesplegar la API).
- Remitente/dominio verificado en Resend, API key y lista de destinatarios permitidos.
- Contraseña demo y dominio de correo para el cargador.
