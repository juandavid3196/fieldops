# Autenticación en FieldOps

Resumen de cómo funciona el inicio de sesión. Detalle técnico:
[`docs/backend/api-configuration.md`](backend/api-configuration.md) y
[`docs/frontend/frontend-configuration.md`](frontend/frontend-configuration.md).
Requisitos completos: [`specs/sign-in/spec.md`](../specs/sign-in/spec.md).

## La idea en 30 segundos

- El usuario entra con **email y contraseña** en `/auth/sign-in`.
- Si son correctos, el backend responde con una **cookie de sesión**
  (`fieldops_session`). El navegador la guarda y la envía sola en cada
  petición; JavaScript no puede leerla (`HttpOnly`).
- La cookie va cifrada y dice **quién es el usuario y en qué organización
  trabaja**. La organización la decide el servidor, nunca el cliente.
- En **cada petición** el backend vuelve a comprobar que el usuario, su
  membresía y la organización siguen activos. Si no, la sesión se anula.
- No hay JWT, tokens de refresco ni tabla de sesiones.

## Endpoints

| Método | Ruta                | Para qué                          | Respuesta normal |
| ------ | ------------------- | --------------------------------- | ---------------- |
| POST   | `/sessions`         | Iniciar sesión                    | `200` + cookie   |
| GET    | `/sessions/current` | ¿Quién soy? (sesión actual)       | `200` o `401`    |
| DELETE | `/sessions/current` | Cerrar sesión (borra la cookie)   | `204`            |

Respuesta de sesión: `{ user: {id, firstName, lastName, email}, organization: {id, name}, role: {code, name} }`.

## Flujo completo

```text
Navegador (Angular)                         API (.NET)
───────────────────                         ──────────
1. Abre /auth/sign-in
   guestGuard ── GET /sessions/current ──►  ¿cookie válida?
               ◄── 401 (no hay sesión) ──   → muestra el formulario
               ◄── 200 ─────────────────    → redirige a /overview

2. Envía el formulario (valida antes en el cliente)
   ── POST /sessions {email, password, rememberMe} ──►
                                            a. Límites de intentos (IP, global, email)
                                            b. Valida campos
                                            c. Verifica contraseña (PBKDF2)
                                            d. ¿Usuario activo? ¿membresía activa
                                               en una organización activa?
                                            e. Elige la organización (la más antigua)
                                            f. Guarda last_login_at + registro de auditoría
   ◄── 200 + Set-Cookie fieldops_session ──
   Guarda la sesión y va a /overview

3. /overview
   authGuard ── GET /sessions/current ──►   Revalida usuario/membresía/organización
             ◄── 200 ─────────────────      → muestra "Welcome, {nombre}"

4. Sign out
   ── DELETE /sessions/current ──►          Borra la cookie
   ◄── 204 ──  Limpia la sesión y vuelve a /auth/sign-in
```

## Reglas importantes

| Regla | Detalle |
| ----- | ------- |
| Error genérico | Email inexistente, contraseña mala, usuario suspendido o sin organización activa: **siempre el mismo `401`**. Así nadie puede saber si un email existe. |
| Duración | Sin "Remember me": dura mientras el navegador esté abierto, máximo 8 h. Con "Remember me": 14 días sin uso, máximo 30 días en total. |
| Anti fuerza bruta | 10 intentos por IP cada 5 min; 300 globales por minuto; 5 fallos por email en 15 min. Al superar: `429` + `Retry-After`. Los contadores viven en memoria (se pierden al reiniciar). |
| Organización | Nunca se acepta del cliente (ni body ni headers). Se toma de la cookie validada. |
| Revalidación | Si desactivan al usuario, la membresía o la organización, la siguiente petición da `401` y borra la cookie. El rol se lee de la base de datos en cada petición. |
| Logs | Nunca se registran emails, contraseñas, hashes ni cookies. |
| CSRF | Cookie `SameSite=Strict` + solo JSON + CORS con credenciales solo para orígenes configurados. |

## Archivos del backend

**`FieldOps.Api`** (entrada HTTP)

| Archivo | Para qué sirve |
| ------- | -------------- |
| `Controllers/SessionsController.cs` | Los 3 endpoints. Traduce resultados a respuestas HTTP y pone/borra la cookie. |
| `Contracts/SignInRequest.cs` | Forma del JSON que llega en el login. |
| `Authentication/SessionCookie.cs` | Nombre de la cookie y duraciones (8 h, 14 d, 30 d). |
| `Authentication/SessionClaims.cs` | Qué va dentro de la cookie (usuario, organización, membresía, hora de login, "remember me") y cómo leerlo. |
| `Authentication/SessionCookieEvents.cs` | Revalida la sesión en cada petición y responde `401` sin redirecciones. |
| `Extensions/ApiAuthenticationExtensions.cs` | Configura la cookie (HttpOnly, Secure, SameSite=Strict). |
| `Extensions/ApiRateLimitingExtensions.cs` | Límites por IP y global. |
| `Middleware/InvalidSessionCookieMiddleware.cs` | Borra cookies inválidas en cualquier endpoint. |
| `Middleware/BadHttpRequestExceptionHandler.cs` | Devuelve `413` limpio cuando el body supera 4 KB. |
| `Program.cs` | Orden: CORS → rate limiter → autenticación → limpieza de cookie → autorización. |

**`FieldOps.Application`** (lógica, sin HTTP ni base de datos)

| Archivo | Para qué sirve |
| ------- | -------------- |
| `Authentication/SignInHandler.cs` | El caso de uso "iniciar sesión": ejecuta los pasos a–f del flujo. |
| `Authentication/SignInCommandValidator.cs` | Reglas de email y contraseña con los mensajes exactos. |
| `Authentication/EmailNormalizer.cs` | Quita espacios y pasa el email a minúsculas. |
| `Authentication/ActiveOrganizationSelector.cs` | Elige la organización: membresía activa más antigua en organización activa. |
| `Authentication/GetCurrentSessionHandler.cs` | Carga la sesión actual (usado en cada petición). |
| `Authentication/SignInResult.cs`, `SessionView.cs`, `SignInCommand.cs`, `MembershipCandidate.cs`, `SignInFailureCategory.cs` | Datos de entrada/salida del caso de uso. |
| `Authentication/IPasswordHasher.cs`, `ISignInThrottle.cs`, `IAuthenticationStore.cs` | Interfaces que implementa Infrastructure. |

**`FieldOps.Infrastructure`** (detalles técnicos)

| Archivo | Para qué sirve |
| ------- | -------------- |
| `Authentication/Pbkdf2PasswordHasher.cs` | Crea y verifica contraseñas (PBKDF2-SHA256, 600.000 iteraciones). Usa un hash falso cuando el email no existe para que el tiempo de respuesta no lo delate. |
| `Authentication/InMemorySignInThrottle.cs` | Cuenta fallos por email (5 en 15 min). |
| `Persistence/AuthenticationStore.cs` | Consultas a la base de datos: usuario, membresías, sesión, y guardado de `last_login_at` + auditoría en una transacción. |

**`FieldOps.Domain`**: `User.RecordSignIn` (fecha del último login) y
`AuditLog.Create` (ahora acepta la IP).

## Archivos del frontend (`frontend/src/app`)

| Archivo | Para qué sirve |
| ------- | -------------- |
| `core/services/session.service.ts` | Guarda la sesión actual (signal) y llama a los 3 endpoints. |
| `core/models/session.model.ts` | Tipos de la sesión y del login. |
| `core/interceptors/auth.interceptor.ts` | Añade `withCredentials` a las peticiones de la API para que viaje la cookie. |
| `core/guards/auth.guard.ts` | Protege `/overview`: sin sesión → `/auth/sign-in`. |
| `core/guards/guest.guard.ts` | Si ya hay sesión y abres `/auth/sign-in` → `/overview`. |
| `core/services/api-error.service.ts` | Convierte errores HTTP en `ApiError`; lee `Retry-After` en los `429`. |
| `app.routes.ts` | `''` y rutas desconocidas → `/auth/sign-in`. |
| `features/authentication/pages/sign-in/` | Página de login: formulario, validación, mensajes y estados. |
| `.../sign-in.validators.ts` / `sign-in.messages.ts` | Reglas del formulario y textos que ve el usuario. |
| `features/authentication/components/sign-in-brand-panel/` | Panel decorativo de la izquierda (solo en pantallas grandes). |
| `features/overview/pages/overview/` | Página mínima tras el login: nombre, organización, rol y **Sign out**. |

Los guards son solo experiencia de usuario; **quien decide es el backend**.

## Qué ve el usuario ante errores

| Situación | Mensaje |
| --------- | ------- |
| Credenciales incorrectas (`401`) | "The email or password is incorrect…" (borra y enfoca la contraseña) |
| Demasiados intentos (`429`) | "Too many sign-in attempts. Try again in {n} minutes." (botón bloqueado ese tiempo) |
| Cualquier otro fallo | "We couldn't sign you in right now. Try again in a moment." |

Nunca se muestran textos técnicos del backend.

## Limitaciones conocidas

- Cerrar sesión solo borra la cookie de ese navegador; no hay revocación
  por sesión ni al cambiar contraseña.
- Los contadores de intentos y las claves de cifrado (Data Protection) no
  persisten: al reiniciar se pierden los contadores; perder las claves cierra
  todas las sesiones. Resolverlo es trabajo de despliegue.
- Detrás de un proxy, el límite por IP afecta a todos a la vez hasta
  configurar forwarded headers.
- En local, la cookie es `Secure` sobre `http://localhost`: funciona en
  Chrome y Firefox, puede fallar en Safari. Arranca la API con
  `--launch-profile http`.
