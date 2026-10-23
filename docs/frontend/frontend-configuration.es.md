[English](frontend-configuration.md) | **Español**

# Configuración del frontend

Configuración base de ejecución de la aplicación Angular de FieldOps
(`frontend/`).

## Entornos

Los archivos de entorno se compilan en JavaScript público. Nunca guardes secretos,
tokens ni cadenas de conexión en ellos.

| Archivo                                      | Usado por                                 | `apiBaseUrl`            |
| -------------------------------------------- | ----------------------------------------- | ----------------------- |
| `src/environments/environment.ts`            | `ng serve`, `ng test`, builds de desarrollo | `http://localhost:5034` |
| `src/environments/environment.production.ts` | `ng build --configuration production`    | `/api`                  |

El archivo de producción reemplaza a `environment.ts` mediante `fileReplacements` en
`angular.json`.

## URL base de la API

Los servicios construyen las URL de los endpoints con `buildApiUrl(inject(API_CONFIG), path)`
(`src/app/core/config/api.config.ts`). Los interceptores solo actúan sobre las peticiones cuya
URL empieza con la URL base configurada.

### Reverse proxy en producción

En producción el frontend llama a la API en el mismo origen bajo `/api`.
La API no tiene prefijo de ruta `/api` (por ejemplo, health se sirve en
`/health`), así que el reverse proxy debe:

- Enrutar `/api/*` al backend.
- Quitar el prefijo `/api` antes de reenviar (`/api/health` → `/health`).
- Servir cualquier otra ruta desde la salida de build de Angular, con fallback a
  `index.html` para las rutas del lado del cliente.

Como las peticiones son del mismo origen, producción no depende de la configuración
CORS de la API.

## Desarrollo local

Ejecuta el backend con el perfil de lanzamiento **`http`**:

```bash
dotnet run --project backend/src/FieldOps.Api --launch-profile http
```

El perfil `https` activa la redirección a HTTPS, que redirige las llamadas entre
orígenes del frontend a `http://localhost:5034` y las rompe por CORS.
`appsettings.Development.json` ya permite el origen del servidor de desarrollo de
Angular, `http://localhost:4200`.

Luego inicia el frontend desde `frontend/` con `npm start`.

## Pipeline HTTP

Registrado en `src/app/app.config.ts`, en orden:

1. `authInterceptor`: define `withCredentials: true` solo en las URL de la API
   (`isApiUrl`), para que el navegador envíe la cookie de sesión HttpOnly. Nunca
   agrega una cabecera `Authorization` ni un token, y las demás URL no se tocan.
2. `portalUnauthorizedInterceptor`: devuelve al cliente al inicio de sesión del portal cuando una
   petición del portal responde `401` mientras había una sesión del portal en memoria (se excluyen los
   endpoints de inicio de sesión, invitación y restablecimiento de contraseña).
3. `errorInterceptor`: convierte las respuestas fallidas de la API en `ApiError`
   (`src/app/core/models/api-error.model.ts`) usando `ApiErrorService`.

## Idioma

Inglés y español, cambio en tiempo de ejecución y persistencia: ver [i18n-architecture.es.md](i18n-architecture.es.md). `app.config.ts` registra `provideI18n()` (lo exige la spec; la lista del CLAUDE.md del frontend es anterior).

## Manejo de errores

La API devuelve ProblemDetails según RFC 9457. `ApiErrorService` asigna cada fallo a
una categoría y a un mensaje fijo y amigable elegido por estado HTTP (localizado mediante los recursos de traducción, con los códigos conocidos `403`/`404`/`409` mapeados a su propio texto):

- El `title` y el `detail` del backend nunca se muestran, y `ApiErrorService` nunca
  depende del texto de excepciones del backend.
- Un `500` nunca contiene el mensaje, tipo ni stack trace de la excepción, en ningún
  entorno, incluido Development: el cliente recibe un ProblemDetails genérico.
  Los detalles completos de la excepción existen solo en los logs del backend.
- `traceId` se conserva como referencia de soporte y se correlaciona con los logs
  del backend.
- Los errores de los endpoints de la API, incluidos los `500` controlados, llevan las
  cabeceras CORS de los orígenes permitidos, de modo que la app servida desde
  `http://localhost:4200` recibe el estado real en lugar de un error de red (ver
  `docs/backend/api-configuration.md`).
- En las respuestas `400`/`422` con un objeto `errors`, los mensajes por campo se
  conservan en `ApiError.fieldErrors`.
- En las respuestas `429`, `ApiError.retryAfterSeconds` contiene la cabecera
  `Retry-After` interpretada como un entero no negativo de segundos (sin espacios,
  solo dígitos, entero seguro). Los valores HTTP-date, negativos, decimales o vacíos
  y cualquier otro estado dan `undefined`. La política CORS de la API expone
  `Retry-After` para que el navegador pueda leerla entre orígenes en desarrollo.
- Las páginas pueden mostrar textos propios elegidos por `ApiError.kind` (por ejemplo
  los mensajes BR-15 del inicio de sesión), nunca texto del backend.

## Autenticación y acceso a rutas

- `SessionService` (`core/services/session.service.ts`) mantiene la sesión actual
  como un signal de solo lectura. `loadCurrent()` llama a `GET /sessions/current`
  y limpia el signal si falla; `signIn()` envía `POST /sessions` con el
  email recortado y en minúsculas; `signOut()` llama a `DELETE /sessions/current`
  y limpia el signal solo si tiene éxito. Al cargar o crear una sesión también se
  aplican el idioma y el tema guardados del usuario. `PortalSessionService` hace lo mismo para
  `/portal/sessions`.
- `authGuard` (`core/guards/auth.guard.ts`) permite una ruta solo cuando
  `GET /sessions/current` devuelve `200`; cualquier otra cosa redirige a
  `/auth/sign-in`. `guestGuard` redirige a un visitante que ya tiene sesión a
  `landingPath(role)` (`/today` para técnicos, `/overview` para el resto) y deja abrir
  la página en otro caso. `portalGuard` hace lo mismo para el portal del cliente
  (`GET /portal/sessions/current`, redirección a `/portal/sign-in`). Todos llaman al
  endpoint en cada activación y redirigen con `RedirectCommand` y `replaceUrl: true`.
- `app.routes.ts` redirige `''` y toda ruta sin coincidencia (`**`) a
  `/auth/sign-in`. Cada área de feature se carga de forma lazy (`loadChildren`/`loadComponent`) y
  registra su scope de traducción con `withI18n`. Las áreas públicas (`auth`,
  `request/:slug`, `quotes/view`, `quote-approval`, facturas públicas, registro de
  organización) están fuera de los shells protegidos.
- `/overview` envía a los roles que gestionan la operación (`owner`, `operations_manager`,
  `dispatcher`) a `/dashboard`; los demás roles ven la página de bienvenida.
- Los guards son solo UX: el backend autoriza cada petición.

## Shells de la aplicación

Tres shells envuelven las rutas protegidas. Los shells del personal y del técnico comparten las
reglas de `authGuard` (`runGuardsAndResolvers: 'always'`, así que la sesión se revalida en cada
navegación); el shell del portal usa `portalGuard`.

- `layout/app-shell` (personal). El modo de layout viene de `matchMedia` (reflejos de
  `_breakpoints.scss` en `core/config/breakpoints.ts`): drawer por debajo de 768px, rail de 72px desde 768px,
  sidebar de 256px desde 1100px. La elección del usuario sobre el sidebar (rail o completo) se guarda en
  `localStorage` (`fieldops.sidebar.collapsed`); sin ella decide el modo de layout. La
  navegación vive en `layout/shell-sidebar` y son datos en `layout/app-shell/app-shell.nav.ts`
  (`NAV_GROUPS`); los ítems y grupos se filtran por código de rol, solo como UX.
- Barra superior: búsqueda global (`layout/app-shell/global-search`), menú de organización (solo la
  organización de la sesión, sin petición), enlace a Help, campana de notificaciones (popover con
  insignia de no leídas para los roles internos) y el menú de usuario con cuenta, idioma y apariencia.
- `layout/technician-shell`: shell mobile-first para técnicos (Today's jobs, Schedule, Time,
  Messages, More). Los técnicos aterrizan en `/today`.
- `layout/portal-shell`: shell del portal del cliente con su propia navegación y menú de cuenta.
- `/coming-soon/:module` (`features/coming-soon`) es una página marcador compartida para
  destinos sin una pantalla propia; los slugs y nombres están en
  `core/config/coming-soon-modules.ts`. Un slug desconocido redirige al inicio del shell; la
  página no hace peticiones. `/coming-soon/help` y `/coming-soon/reports` redirigen a las páginas
  reales de Help y Reports.
- Inter autoalojada en `public/fonts/inter/` (incluye el archivo de licencia OFL) y `primeicons`
  aportan la tipografía y los iconos.

## Errores por campo

`ApiErrorService` conserva los mensajes por campo de `ValidationProblemDetails.errors` en
`ApiError.fieldErrors` y los traduce mediante `FieldErrorCatalog` (con clave por campo y el
mensaje exacto aprobado en inglés). Un mensaje que no está en el catálogo se convierte en un texto
localizado genérico, de modo que los detalles del framework (por ejemplo "The JSON value could not be
converted to System.Guid") nunca llegan al usuario. La página de inicio de sesión además mantiene una
lista permitida de los mensajes de `email` y `password` que muestra.
