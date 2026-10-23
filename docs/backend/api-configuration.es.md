[English](api-configuration.md) | **Español**

# Configuración de la API

Configuración base de ejecución de `FieldOps.Api`.

## Ajustes obligatorios

La API se niega a arrancar si falta algún ajuste obligatorio o es inválido.

| Clave | Variable de entorno | Notas |
| --- | --- | --- |
| `ConnectionStrings:FieldOpsDatabase` | `ConnectionStrings__FieldOpsDatabase` | Cadena de conexión de PostgreSQL. Nunca se versiona. |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, ... | Orígenes `http`/`https` absolutos, sin ruta, sin barra final ni comodines. |
| `Email:Provider` | `Email__Provider` | `Smtp` o `Resend`. Cualquier proveedor distinto de `Resend` impide el arranque en Production. Se valida al arrancar; los errores nombran la clave, nunca el valor. |
| `Email:SenderAddress` (email válido), `Email:SenderName` | `Email__SenderAddress`, `Email__SenderName` | Remitente de todos los correos (invitaciones y recuperación de contraseña). |
| `Email:Smtp:Host`, `Port` (1-65535) | `Email__Smtp__Host`, ... | Obligatorios solo cuando el proveedor es `Smtp`. |
| `Email:Resend:ApiKey` | `Email__Resend__ApiKey` | Obligatoria cuando el proveedor es `Resend`. Solo variables de entorno o user-secrets, nunca se versiona. |
| `Email:Smtp:EnableSsl` | `Email__Smtp__EnableSsl` | `false` si no está presente (solo proveedor `Smtp`). |
| `Email:Smtp:UserName`, `Password` | `Email__Smtp__UserName`, `Email__Smtp__Password` | Opcionales; se definen juntos; solo user-secrets o variables de entorno, nunca se versionan. |

`appsettings.Development.json` permite `http://localhost:4200` (servidor de desarrollo de Angular) y
selecciona el proveedor `Smtp` en `localhost:1025` sin SSL (Mailpit, ver abajo).
`appsettings.json` trae una lista de orígenes vacía y solo `Email:SenderName`, así que
cualquier otro entorno debe proporcionar el resto de forma explícita.

### Captura local de correos (Mailpit)

`docker-compose.yml` ejecuta `mailpit` (imagen fijada): SMTP en `127.0.0.1:1025`,
interfaz web en `http://127.0.0.1:8025`. Se inicia con
`docker compose up -d mailpit`; las invitaciones y los correos de restablecimiento de contraseña que envía la API aparecen en la interfaz.
Las pruebas de integración nunca lo usan (reemplazan el puerto de envío).

## Ajustes opcionales de pagos

Los pagos de facturas por parte del cliente (`customer-invoice-payments`) leen estos ajustes. Ninguno es obligatorio para arrancar la API; cada
función queda deshabilitada mientras falten sus claves. Un valor presente pero mal formado impide el arranque, y los errores nombran la clave, nunca
el valor. Las claves vienen solo de user-secrets o variables de entorno y nunca se versionan, registran ni devuelven.

| Clave | Variable de entorno | Notas |
| --- | --- | --- |
| `Stripe:SecretKey` | `Stripe__SecretKey` | Debe empezar con `sk_` o `rk_`. |
| `Stripe:PublishableKey` | `Stripe__PublishableKey` | Debe empezar con `pk_`; se devuelve al navegador para el Payment Element. |
| `Stripe:WebhookSecret` | `Stripe__WebhookSecret` | Debe empezar con `whsec_`; verifica `POST /webhooks/stripe`. |
| `BankDetails:EncryptionKey` | `BankDetails__EncryptionKey` | Base64 de exactamente 32 bytes (clave AES-256-GCM del número de cuenta bancaria). |

Los pagos con tarjeta solo están disponibles cuando las tres claves de Stripe están definidas; de lo contrario `card.available` es `false` en la
factura pública y `card-intent` responde `409 card_unavailable`. Sin la clave de datos bancarios, los endpoints de datos bancarios responden
`503 bank_details_unavailable` y la transferencia bancaria no está disponible en la factura pública. El endpoint del webhook
(`POST /webhooks/stripe`) es anónimo, se autoriza solo con la firma de Stripe, el cuerpo es ≤ 64 KB y no tiene límite de tasa.

## Secretos locales

La cadena de conexión no se guarda en ningún archivo `appsettings*.json`. Para
desarrollo local usa user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:FieldOpsDatabase" \
  "Host=localhost;Port=5432;Database=fieldops;Username=<user>;Password=<password>" \
  --project src/FieldOps.Api
```

Los valores deben coincidir con los del contenedor de PostgreSQL en el archivo `.env`
del repositorio (ver `.env.example`). CI y los entornos desplegados usan variables
de entorno.

## Endpoints

| Endpoint | Entornos | Propósito |
| --- | --- | --- |
| `GET /health` | Todos | Igual que `/health/ready`. Se mantiene por compatibilidad con el frontend (`ApiHealthService`). |
| `GET /health/live` | Todos | Liveness: solo la comprobación `self`; nunca toca dependencias. Siempre `200` mientras el proceso corre. |
| `GET /health/ready` | Todos | Readiness: todas las comprobaciones registradas, incluida `postgresql`. `200` si está sano, `503` si no. |
| `GET /openapi/v1.json` | Development | Documento OpenAPI. |
| `GET /swagger` | Development | Swagger UI sobre el documento OpenAPI. |
| `POST /sessions` | Todos | Iniciar sesión (anónimo). Solo `application/json`, cuerpo ≤ 4 KB, con límite de tasa. `200` cuerpo de sesión + `Set-Cookie: fieldops_session`, `Cache-Control: no-store`. |
| `GET /sessions/current` | Todos | Sesión actual (`[Authorize]`). `200` cuerpo de sesión o `401`; ambos con `Cache-Control: no-store`. |
| `DELETE /sessions/current` | Todos | Cerrar sesión (anónimo). Siempre `204` con un `Set-Cookie` que borra `fieldops_session`. |
| `GET`, `PUT /account/profile` | Todos | Perfil propio (`[Authorize]`, cualquier rol; toda respuesta de `/account/*` lleva `Cache-Control: no-store`). `PUT` es `application/json` ≤ 4 KB, `{ firstName, lastName, phone }`; las demás propiedades se ignoran. `200` `{ firstName, lastName, email, phone, organization: { name }, role: { name }, photo }`. |
| `GET`, `PUT`, `DELETE /account/photo` | Todos | Foto de perfil propia (tabla `user_photos`). `PUT` es `multipart/form-data` campo `file`, PNG o JPEG según el contenido, ≤ 5 MB (`400` `errors.file`), cuerpo ≤ 6 MB (`413`), `415` en otro caso. `GET` sirve el tipo almacenado con `nosniff`, `default-src 'none'; sandbox` e `inline`; `404` sin foto. `DELETE` siempre es `204`. |
| `PUT /account/language` | Todos | Idioma propio de la interfaz (`[Authorize]`, sesión interna). `application/json` ≤ 4 KB, `{ language }`, exactamente `en` o `es` (distingue mayúsculas); las demás propiedades se ignoran. `200` `{ preferredLanguage }`; `400` clave `language` "Choose a supported language." (`400` sin clave si está mal formado); `401`; `413`; `415`. Mismo valor: sin escritura ni auditoría; si cambia, auditoría `user.language_updated` con metadatos `{ from, to }`. `Cache-Control: no-store`. |
| `POST /account/password` | Todos | Cambiar la contraseña propia. `application/json` ≤ 4 KB, `{ currentPassword, newPassword }`. `204` + cookie de sesión reemitida (las demás sesiones quedan inválidas); `400` errores de campo; `429` + `Retry-After` tras 5 comprobaciones fallidas de la contraseña actual por usuario en una ventana fija de 15 minutos (en memoria, por proceso, no se reinicia con un éxito). |
| `POST /invitations/validate` | Todos | Detalles de la invitación para un token utilizable (anónimo). Cuerpo `{ token }`, ≤ 4 KB. `200` `{ organizationName, inviterName, email, firstName, lastName, role: { code, name }, isAllBranches, branches: [{ name }], expiresAt }`. |
| `POST /invitations/accept` | Todos | Aceptar como usuario nuevo (anónimo). Cuerpo `{ token, firstName, lastName, password, preferredLanguage? }` (no soportado o ausente → `en`, nunca un error). `200` cuerpo de sesión + `Set-Cookie` de la organización invitada. |
| `POST /invitations/accept-existing` | Todos | Aceptar como el usuario con sesión (`[Authorize]`, el email debe coincidir). Cuerpo `{ token }`. `200` cuerpo de sesión + `Set-Cookie` que reemplaza la sesión. |
| `POST /password-resets` | Todos | Solicitar un enlace de restablecimiento (anónimo). Cuerpo `{ email }`, ≤ 4 KB. Siempre `202` con cuerpo vacío y `no-store` si el email está bien formado; `400` `email` en otro caso. |
| `POST /password-resets/validate` | Todos | Cuerpo `{ token }`. `200 { email }` para un token utilizable, `410` idéntico para cualquier token inutilizable. |
| `POST /password-resets/confirm` | Todos | Cuerpo `{ token, password }`. `204`; `400` `token`/`password`; `410` idéntico; `500` con rollback. |
| `GET /public/organizations/{slug}/service-request-form` | Todos | Configuración del formulario público de solicitudes (anónimo, `no-store`). `200 { organizationName, phone, website, requestPrefix, timezone, categories: [{ id, name, services: [{ id, name }] }] }`. Un único `404` idéntico para un slug desconocido o una organización que no acepta solicitudes públicas; `429` + `Retry-After`. |
| `POST /public/organizations/{slug}/service-requests` | Todos | Solicitud de servicio pública (anónimo, `no-store`). `multipart/form-data`: parte `request` (JSON) + 0–5 archivos `attachments`; cuerpo ≤ 27 262 976 bytes (`413`). `201 { requestNumber }`; `400` errores de campo (genérico para el honeypot o un cuerpo mal formado); `404` idéntico; `415`; `429`; `500` con rollback. El tipo del adjunto se detecta por el contenido; los archivos se almacenan en línea. El slug se oculta en los logs de peticiones. |
| `POST /public/quote-links/view` | Todos | Cotización pública (anónimo). Cuerpo `{ token }`, ≤ 16 KB. `200 PublicQuote`; `404` `quote_link_unavailable`; `410` `quote_superseded`; `429`. |
| `POST /public/quote-links/calculate` | Todos | Cuerpo `{ token, selectedOptionalLineIds }`. `200 Totals` a partir de los valores congelados de la versión; no persiste nada; `400` `selectedOptionalLineIds`. |
| `POST /public/quote-links/approve` | Todos | Cuerpo `{ token, selectedOptionalLineIds, acceptTerms }`. `200 PublicQuote`; `400` `acceptTerms`/`selectedOptionalLineIds`; `409` `quote_already_answered`. |
| `POST /public/quote-links/decline` | Todos | Cuerpo `{ token, reason }` (1-1000 caracteres). `200 PublicQuote`; `400` `reason`; `409`. |
| `POST /public/quote-links/clarification` | Todos | Cuerpo `{ token, message }` (1-1000 caracteres). `200 PublicQuote` (una repetición devuelve la aclaración existente); `400` `message`; `409`. |
| `POST /public/quote-links/photos/{photoId}` | Todos | Cuerpo `{ token }`. `200` imagen de la última evaluación completada de la cotización (hasta 6); en otro caso el `404` genérico. |
| `POST /public/quote-links/logo` | Todos | Cuerpo `{ token }`. `200` logo de la organización, o el `404` genérico si no hay. |
| `POST /public/quote-links/pdf` | Todos | Cuerpo `{ token }`. `200 application/pdf` adjunto `<Q-n>-v<versionNo>.pdf`. |
| `PUT /portal/account/language` | Todos | Idioma propio de la interfaz desde la sesión del portal; mismo contrato y respuestas que `PUT /account/language`. La auditoría `user.language_updated` se registra en la organización de la cuenta actual del portal. |
| `POST /portal/sessions` | Todos | Inicio de sesión del portal (anónimo). Solo `application/json`, cuerpo ≤ 4 KB, mismos límites y contadores de fallos que `POST /sessions`. `200 PortalSession` + `Set-Cookie: fieldops_portal_session`; todo fallo de credenciales es el mismo `401`. |
| `GET /portal/sessions/current`, `DELETE /portal/sessions/current`, `POST /portal/sessions/current/account` | Todos | Sesión actual del portal (`401` sin sesión válida del portal), cierre de sesión (`204`, borra solo la cookie del portal) y cambio de cuenta (`{ contactId }`; `404` `portal_account_unavailable`; reemite la cookie conservando la hora de inicio de sesión). |
| `POST /portal/invitations/validate`, `/accept`, `/accept-existing` | Todos | Activación del portal (anónimo, token en el cuerpo, ≤ 4 KB, una partición de límite de tasa). Un único `410` idéntico `portal_invitation_unavailable` para todo token inutilizable o mal formado; `409` `portal_invitation_ineligible`; `401` para una contraseña incorrecta de cuenta existente. `accept` y `accept-existing` responden `200 PortalSession` + la cookie del portal tras el commit. `accept` también acepta `preferredLanguage` opcional (no soportado o ausente → `en`, nunca un error); `accept-existing` nunca cambia el idioma almacenado. |
| Cuerpos de sesión (idioma) | Todos | `user.preferredLanguage` (`en`\|`es`) forma parte del cuerpo de sesión de `GET`/`POST /sessions`, `GET`/`POST /portal/sessions` y `POST /portal/sessions/current/account`. `preferredLanguage` opcional se acepta en `POST /organization-registrations` (mismo fallback a `en`); `POST /invitations/accept-existing` nunca lo cambia. |
| `POST /portal/password-resets` | Todos | Solicitud de restablecimiento del portal (anónimo): las reglas de `/password-resets`, se requiere un vínculo activo, enlace `{primer origen CORS}/portal/reset-password#token={token}`. |
| `GET /portal/dashboard`, `/requests`, `/appointments`, `/quotes`, `/invoices`, `/activity`, `/updates`, `/properties` y sus rutas de detalle | Todos | Lecturas del portal (solo esquema del portal). Todo id, incluido `propertyId`, se valida contra la organización y el cliente de la sesión; cualquier otro id responde el `404` idéntico `portal_resource_unavailable`. |
| `POST /portal/service-requests`, `/portal/appointments/{visitId}/reschedule-requests`, `/portal/properties`, `PATCH /portal/properties/{propertyId}`, `/portal/messages`, `/portal/updates/seen` | Todos | Escrituras del portal. Límites por usuario más abajo; `409` códigos `reschedule_not_available`, `reschedule_already_requested`, `property_changed`, `messaging_unavailable`. |
| `/portal/quotes/{quoteId}/…`, `/portal/invoices/{invoiceId}/…`, `GET /portal/work-orders/{id}/completion-report`, `GET /portal/organization/logo` | Todos | Los casos de uso de aprobación de cotizaciones y pago de facturas alcanzados por id mediante `ResourceAccess.Portal` en lugar de un token público (no se crea, lee ni revoca ninguna fila de token). Una cotización pasada su `valid_until` es de solo lectura `expired` y sus acciones responden `409` `quote_expired`. Los grupos de límite de tasa son los públicos, con clave por usuario y recurso. |
| `POST /customers/{id}/portal-invitation`, `DELETE /customers/{id}/portal-access` | Todos | Personal (`CustomerPolicies.Manage`, alcance de sucursal, `404` entre organizaciones). `200 { portalStatus, portalLinkedOn, invitationExpiresOn }`; `409` `portal_invite_unavailable`. `GET /customers/{id}/detail` agrega los mismos campos a `contact`. |

Endpoints públicos de enlaces de cotización (customer-quote-approval): anónimos, el token en bruto en
el cuerpo JSON es la única credencial y la organización, la cotización y la versión salen de
su fila con hash. Un único `404` ProblemDetails idéntico (`code`
`quote_link_unavailable`, título "This link isn't available.") cubre tokens mal formados,
desconocidos, revocados, cancelados y vencidos; `410` `quote_superseded` cubre
un token de una versión anterior. Un token vencido en una cotización `sent` o
`clarification_requested` primero mueve la cotización a `expired` (una fila de auditoría
`quote.expired`, idempotente bajo el bloqueo de la fila de la cotización). Toda respuesta
lleva `Cache-Control: no-store`, `Referrer-Policy: no-referrer` y
`X-Content-Type-Options: nosniff` (`QuoteLinkPublicHeadersMiddleware`, aplicados cuando
la respuesta empieza para que los errores los conserven); las respuestas de imagen y PDF usan
`Cache-Control: private, no-store`. Los cuerpos están limitados a 16 KB (`413`).
Las respuestas bloquean la fila de la cotización (`SELECT ... FOR UPDATE`), releen el estado bajo
el bloqueo y son idempotentes por versión; los índices únicos parciales
`ux_quote_responses_final` y `ux_quote_responses_clarification` son el respaldo
y una violación repite la decisión una vez. Los handlers registran solo el id de la cotización y una
categoría de resultado: el token, el hash, los motivos, las preguntas y los datos de quien responde
nunca llegan a logs, filas de auditoría ni problem details, y los logs de peticiones ocultan el id
de la foto. El PDF lo genera PDFsharp-MigraDoc (MIT) con la fuente Inter incrustada en
`FieldOps.Infrastructure` (SIL OFL 1.1, `Pdf/Fonts/OFL.txt`); no se usa ninguna fuente
instalada. El enlace de la cotización es `{primer origen CORS}/quotes/view#token={token}`.

Endpoints de invitaciones: toda respuesta lleva `Cache-Control: no-store`
(`InvitationNoStoreMiddleware`). `400` es `ValidationProblemDetails` con claves
`token`, `firstName`, `lastName` o `password` (JSON mal formado o cuerpo vacío:
sin claves); `410` es un único ProblemDetails idéntico para todo token inutilizable
(desconocido, reemplazado, vencido, revocado, consumido, organización inactiva);
`409` lleva solo la extensión `code` (`account_exists`, `identity_mismatch`,
`membership_exists`, `access_unavailable`). Orden de comprobaciones: límite de tasa, sesión
(`accept-existing`), tipo de contenido/cuerpo/forma de los campos, token utilizable, elegibilidad
bajo un bloqueo `SELECT ... FOR UPDATE` de la fila de la invitación. Aceptar es una sola
transacción (usuario, membresía, sucursales, `accepted_at`, vínculo con el perfil de técnico,
fila de auditoría `user.invitation_accepted`); la cookie se emite solo tras el commit.
El enlace de aceptación es `{primer origen CORS}/auth/invitation#token={token}`.
Estas acciones no tienen atributo `[Consumes]`: rechaza durante el enrutamiento,
antes del limitador de tasa y de la comprobación de sesión, por lo que `415` viene del binding del cuerpo.

Endpoints de restablecimiento de contraseña: toda respuesta lleva `Cache-Control: no-store`
(`PasswordResetNoStoreMiddleware`); sin atributo `[Consumes]` (mismo motivo que en
invitaciones). El enlace de restablecimiento es `{primer origen CORS}/auth/reset-password#token={token}`.
El token es el generador de tokens de invitación (32 bytes, base64 seguro para URL, se almacena
el SHA-256), dura 30 minutos y es de un solo uso. Solicitud: límite por email, búsqueda del usuario
elegible (`active`), y luego una transacción que bloquea la fila de `users`
(`FOR UPDATE`), borra los tokens sin usar del usuario e inserta el nuevo; el
correo se encola solo tras el commit y lo envía `PasswordResetEmailDispatcher`
(`Channel` en memoria con límite, descarta si está lleno, los fallos se registran solo por categoría).
Confirmación: primero el hash PBKDF2, luego una transacción que bloquea la fila del token,
vuelve a comprobar token, estado del usuario y contraseña frente al email, y escribe `used_at`,
`users.password_hash`, `password_changed_at` y `updated_at`. La revalidación de sesión
rechaza toda sesión cuyo `signed_in_at` sea anterior al `password_changed_at` del usuario
(`401`, cookie borrada); `password_changed_at` se guarda
truncado a milisegundos para coincidir con el claim de la cookie.

Portal del cliente (`customer-portal-dashboard`): toda respuesta bajo `/portal` lleva `Cache-Control: no-store`
(`PortalNoStoreMiddleware`, también para `401`, `429` y `415`); las respuestas binarias usan `private, no-store`. Los controladores derivan de
`PortalControllerBase`, que autoriza solo el esquema del portal, por lo que la cookie interna nunca autentica un endpoint del portal y la
cookie del portal nunca uno interno (`401`, sin redirección). La organización, el cliente, el contacto y el usuario salen de la sesión
revalidada (`PortalScope`); ningún cuerpo ni cabecera del portal los transporta. Los ids de recurso viajan en la ruta; un id ajeno o desconocido (un
`propertyId` mal formado también) es el `404` idéntico `portal_resource_unavailable`. Los logs y las filas de auditoría contienen solo ids y categorías: nunca
emails, contraseñas, tokens, mensajes, motivos ni direcciones. Cuerpo de `PortalSession`:
`{ user: { firstName, lastName, email }, account: { contactId, customerName, organizationName, hasLogo }, accounts: [{ contactId, customerName, organizationName }] }`.

Cuerpo de sesión: `{ user: { id, firstName, lastName, email, photoUpdatedAt }, organization: { id, name }, role: { code, name } }`.

Las respuestas de health son `application/json` con `Cache-Control: no-store, no-cache`
y contienen solo nombres de comprobaciones, estados y duraciones; sin descripciones,
detalles de conexión ni mensajes de excepción.

## Inventario de endpoints del personal

Todas las rutas siguientes requieren la cookie de sesión interna (`[Authorize]` con la política indicada), responden `401` sin una
sesión válida y `403` cuando el rol no está permitido. La organización y el alcance de sucursal siempre salen de la sesión; un id
ajeno responde `404`. Los conjuntos de roles provienen de `FieldOps.Api/Authorization/*Policies.cs`; los contratos detallados están en cada spec bajo
`specs/`.

| Área (prefijo de ruta) | Operaciones principales | Política y roles |
| --- | --- | --- |
| `/customers`, `/customers/{id}/…`, `/customer-tags` | Listado, métricas, importación CSV, crear/actualizar, archivar/reactivar, propiedades, notas, actividad, solicitudes/cotizaciones/trabajos/facturas relacionados, invitación y acceso al portal | `CustomerView` (owner, operations_manager, dispatcher, accounting, viewer); `CustomerManage` (owner, dispatcher); `CustomerImport` (owner) |
| `/service-requests` | Pipeline, métricas, detalle, crear, revisar, responsable/prioridad/sucursal, notas, solicitudes de información, evaluaciones (planificar, programar, cancelar, completar), listo para cotizar, cancelar, adjuntos | `ServiceRequestView` (roles de lectura anteriores); `ServiceRequestManage` (owner, dispatcher) |
| `/quotes` | Listado, resumen, detalle, versiones, calcular, guardar borrador, enviar, revisar, descartar borrador, reenviar correo | `ServiceRequestView` / `ServiceRequestManage` |
| `/quotes/{id}/work-order`, `/work-orders`, `/checklist-templates` | Editor de órdenes de trabajo, borrador y creación; listado de trabajos, resumen, opciones, detalle | `WorkOrderManage` (owner, operations_manager, dispatcher); `WorkOrderRead` (agrega viewer) |
| `/dispatch` | Opciones, calendario, visitas sin programar, detalle de visita, evaluar, asignar/actualizar, cancelar | `DispatchManage` (owner, operations_manager, dispatcher); `DispatchRead` (agrega viewer) |
| `/operations-dashboard` | Opciones, panel, desempeño | `DispatchManage` |
| `/team` | Técnicos (listado, detalle, crear, actualizar, activar/desactivar, vínculo de cuenta), métricas, historial de trabajos, cobertura de habilidades, catálogo de habilidades, disponibilidad, tiempo libre, excepciones, `GET /team/me` | `TeamView` (owner, operations_manager, dispatcher); `TeamManage` (owner, operations_manager); `TeamSkillsView` (agrega technician); `TeamSelf` (technician) |
| `/technician` | Hoy, agenda, resumen de tiempo, detalle de visita y el ciclo de vida de la visita (iniciar desplazamiento, llegar, iniciar, pausar, reanudar, tareas, materiales, evidencias, notas, completar) | `TeamSelf` (technician) |
| `/messages` | Resumen, conversaciones, mensajes, marcar como leído (las respuestas son `no-store`) | `Messaging` (owner, operations_manager, dispatcher, technician) |
| `/notifications` | Bandeja, resumen, opciones, marcar leído/no leído, leer todo | `NotificationInbox` (owner, operations_manager, dispatcher, accounting, viewer) |
| `/search/global` | Búsqueda global entre los registros permitidos al usuario | `GlobalSearch` (mismos roles que notificaciones) |
| `/billing-review` | Cola, opciones, exportación, detalle, evidencias, actualización de la revisión, generación de factura | `BillingReviewRead` (owner, operations_manager, dispatcher, accounting, viewer); `BillingReviewAct` (owner, accounting) |
| `/invoices` | Hub (opciones, clientes, resumen, listado, exportación, listado/exportación de pagos), detalle, PDF, borrador, enviar, reenviar correo, registrar pago | `BillingReviewRead` / `BillingReviewAct` |
| `/reports` | Opciones, reporte por clave, exportación, historial de exportaciones | `ReportsRead` (roles con al menos un reporte) |
| `/business-overview` | Opciones, resumen, exportación | `BusinessOverviewRead` (owner) |
| `/audit-log` | Listado, resumen, filtros, exportación, detalle de evento | `AuditLogRead` (owner, operations_manager, accounting) |
| `/catalog-items`, `/catalog-categories` | CRUD de ítems y categorías, activar/desactivar, imágenes, exportación/importación CSV | `CatalogView` (roles de lectura anteriores); `CatalogManage` (owner, operations_manager); `CatalogImport` (owner) |
| `/organization-settings`, `/organization-settings/logo`, `/organization-settings/bank-details`, `/branches`, `/users`, `/permission-matrix` | Perfil de la empresa, logo, datos bancarios, sucursales y horarios de atención, invitaciones y acceso de usuarios, matriz de permisos | `CompanySettingsView` (owner, viewer); `CompanySettingsManage` (owner) |
| `/account` | Perfil propio, foto, idioma, tema, contraseña | `[Authorize]`, cualquier rol interno |

`PUT /account/theme` (y `PUT /portal/account/theme`) reciben `{ theme, source }` con `theme` exactamente `system`, `light` o `dark`,
siguen el patrón de los endpoints de idioma (`application/json`, `no-store`) y devuelven la
preferencia guardada; los cuerpos de sesión llevan `preferredTheme` junto a `preferredLanguage`.

Otros puntos de entrada anónimos: `POST /organization-registrations` (con límite de tasa, ver abajo), `POST /webhooks/stripe` (solo firma)
y los enlaces públicos de factura descritos a continuación.

Endpoints de enlaces públicos de factura (`/public/invoice-links/*`, invoice-draft-delivery y customer-invoice-payments): anónimos, el token en bruto en
el cuerpo JSON es la única credencial, y toda respuesta lleva las mismas cabeceras `no-store`, `no-referrer` y `nosniff` que los enlaces de cotización
(`QuoteLinkPublicHeadersMiddleware` cubre ambos prefijos). Operaciones: `view`, `pdf`, `logo`, `receipt`, `completion-report`, `photos`,
`photos/content`, `payments/card-intent`, `payments/status`, `payments/bank-transfer-notice` y `review`. Los pagos con tarjeta necesitan los ajustes
de Stripe descritos arriba. Los usuarios del portal llegan a los mismos casos de uso por id bajo `/portal/invoices/{invoiceId}/…`.

## Respuestas de error

| Caso | Respuesta |
| --- | --- |
| Excepción no controlada | `500` `application/problem+json` con `type`, `title`, `status` y `traceId`, en todos los entornos. Nunca mensajes, tipos ni stack traces de la excepción. |
| Excepción no controlada, el cliente no acepta JSON | `500` con cuerpo vacío. |
| Model binding inválido (`[ApiController]`) | `400` automático `ValidationProblemDetails` con `errors` y `traceId`. Sin personalizar. |
| `4xx` vacío, como una ruta desconocida | `UseStatusCodePages` escribe ProblemDetails. |
| Petición no autenticada a un endpoint `[Authorize]` | `401` ProblemDetails con `Cache-Control: no-store`, nunca una redirección. Prohibido: `403`. |
| Cuerpo mayor que el `[RequestSizeLimit]` del endpoint | `BadHttpRequestExceptionHandler` (antes de `GlobalExceptionHandler`) escribe ProblemDetails con el estado de la excepción (`413`). Lo aplica Kestrel, no el servidor de pruebas en memoria. |
| `Content-Type` ausente o no JSON en un endpoint `[Consumes("application/json")]` | `415` ProblemDetails. `POST /sessions` hace el binding con `EmptyBodyBehavior.Disallow`, así que un `Content-Type` ausente es `415` con o sin cuerpo. |
| Rechazo por límite de tasa | `429` ProblemDetails con `Retry-After` (segundos enteros). |

- `SessionsController` no es un `[ApiController]`: comprueba `ModelState`
  por sí mismo. JSON mal formado, un tipo JSON que no coincide, un cuerpo vacío o un cuerpo
  JSON `null` devuelve un `400` vacío, que `UseStatusCodePages` renderiza como
  ProblemDetails con `traceId` y sin `errors`; las reglas de campo devuelven
  `ValidationProblemDetails` solo con las claves `email` y `password`. Los
  `ApiBehaviorOptions` globales no cambian.
- Todo fallo de credenciales devuelve el mismo cuerpo `401` (escrito por
  `UseStatusCodePages`); solo cambia `traceId`.

- La excepción completa se escribe solo en los logs del servidor, correlacionada por `traceId`.
- Los errores generados en `UseCors` o después (endpoints, autorización), incluidos
  los `500` controlados y los de cuerpo vacío, llevan cabeceras CORS solo para los orígenes
  configurados: CORS las aplica cuando la respuesta empieza, después de que el manejador
  de excepciones la escribe. Los fallos en middleware anterior a `UseCors` no reciben cabeceras
  CORS. Cubierto por `CorsTests`.
- Los futuros mapeos de `404`/`409`/validación pertenecen a la spec del caso de uso
  que los introduzca: un `IExceptionHandler` dedicado registrado antes de
  `GlobalExceptionHandler`, o resultados explícitos desde el endpoint.
- El patrón result queda diferido a su propia spec. La validación del inicio de sesión usa
  FluentValidation en `FieldOps.Application`.

## Autenticación

Autenticación por cookie (ASP.NET Core, ticket de Data Protection), sin almacén de
sesiones en el servidor.

| Atributo | Valor |
| --- | --- |
| Nombre | `fieldops_session` |
| Banderas | `HttpOnly`, `Secure` (siempre), `SameSite=Strict`, `Path=/`, sin `Domain` |
| Ticket | Id de usuario, id de organización, id de membresía, hora de inicio de sesión, bandera Remember me. Sin rol, email ni contraseña. |
| Remember me desactivado | Cookie de sesión del navegador (sin `Expires`/`Max-Age`); el ticket vence 8 h tras iniciar sesión; nunca se renueva. |
| Remember me activado | Cookie persistente, vencimiento deslizante de 14 días renovado en cada petición autenticada; se rechaza 30 días después de iniciar sesión. |

- El inicio de sesión resuelve la organización en el servidor: entre las membresías activas en
  organizaciones activas, la de menor `joined_at` (nulos al final), luego `created_at`,
  luego `id`. Los identificadores del cliente (campos del cuerpo, `X-Organization-Id`) nunca se
  leen. Las funciones posteriores toman `OrganizationId` solo de la sesión validada.
- Toda petición con la cookie se vuelve a comprobar (`SessionCookieEvents`):
  usuario `active`, membresía `active` y perteneciente a ese usuario y organización,
  organización activa, vida absoluta no superada. El rol se lee de
  la membresía en cada petición. Si falla, la petición queda sin autenticar.
- `InvalidSessionCookieMiddleware` (después de `UseAuthentication`) borra la
  cookie cuando una petición la traía pero no se autenticó (manipulada,
  otro anillo de claves, vencida, invalidada), en todos los endpoints, incluidos los
  anónimos. No añade nada cuando la respuesta ya define la cookie
  (nuevo inicio de sesión, cierre de sesión). Las peticiones rechazadas antes de la autenticación (`429`,
  preflight de CORS) no se limpian.
- Contraseñas: PBKDF2-HMAC-SHA256 en
  `pbkdf2-sha256${iterations}${base64-salt}${base64-hash}`, al menos 600.000
  iteraciones, sal de 16 bytes, hash de 32 bytes (`Pbkdf2PasswordHasher`). Los emails
  desconocidos verifican un hash ficticio fijo. La contraseña se verifica antes de las comprobaciones
  de estado y membresía.
- Un inicio de sesión exitoso actualiza `users.last_login_at`/`updated_at` e inserta
  la fila de auditoría `auth.signed_in` en un solo `SaveChangesAsync` (una transacción).
  Si falla, la respuesta es `500` y no se define ninguna cookie.
- Sesión del portal del cliente: un segundo esquema de cookie (`PortalSession`, cookie `fieldops_portal_session`) con las mismas banderas y duraciones.
  Ticket: ids de usuario, contacto, cliente y organización, hora de inicio de sesión, Remember me. `PortalSessionCookieEvents` revalida cada petición
  (usuario `active`, `password_changed_at` no posterior al inicio de sesión, el contacto es un vínculo activo: contacto, cliente y organización activos y
  `customer_contacts.portal_user_id` = usuario) y responde `401` más borrado de la cookie en caso contrario (`InvalidSessionCookieMiddleware` la limpia
  solo bajo `/portal`). El inicio de sesión elige el vínculo activo con el `portal_linked_at` más antiguo; el cambio de cuenta reemite la cookie para otro
  vínculo del usuario. Un restablecimiento de contraseña termina las sesiones internas y del portal emitidas antes.
- Las claves de Data Protection usan el almacén por defecto; perderlas cierra la sesión de todos los
  usuarios. No hay política de autorización de respaldo global: los endpoints optan con
  `[Authorize]`.

## Límite de tasa

En memoria, por proceso, sobre `POST /sessions`, `POST /organization-registrations`,
los tres endpoints `/invitations/*`, los tres endpoints `/password-resets*` los endpoints públicos de enlaces de cotización y de factura, los endpoints públicos de solicitudes y las escrituras del portal.
Cada petición cuenta, sea cual sea su resultado.

| Límite | Clave | Regla |
| --- | --- | --- |
| Por cliente | `RemoteIpAddress` | 10 peticiones por ventana fija de 5 minutos |
| Global | Una partición | 300 peticiones por ventana fija de 1 minuto |
| Invitaciones, por cliente | `RemoteIpAddress`, una partición compartida por los tres endpoints de invitación | 20 peticiones por ventana fija de 5 minutos; global 300 por minuto entre los tres |
| Restablecimiento de contraseña, por cliente | `RemoteIpAddress` | `POST /password-resets`: 5 por ventana fija de 15 minutos (`password-reset-request`). `validate` + `confirm` comparten una partición: 20 por 5 minutos (`password-reset-token`). Global 300 por minuto, una partición compartida por los tres endpoints. |
| Formulario público de solicitudes, por cliente | `RemoteIpAddress` | `GET .../service-request-form`: 60 por ventana fija de 5 minutos (`public-request-form`); global 600 por minuto. |
| Envío de solicitud pública, por cliente | `RemoteIpAddress` | `POST .../service-requests`: 5 por ventana fija de 15 minutos (`public-request-submit`); global 100 por hora. |
| Enlace público de cotización, lectura (`quote-link-read`) | `RemoteIpAddress` | `view`, `calculate`, `photos`, `logo`: 60 por ventana fija de 5 minutos; global 600 por minuto. |
| Enlace público de cotización, acciones (`quote-link-action`) | `RemoteIpAddress` | `approve`, `decline`, `clarification`: 10 por ventana fija de 15 minutos; global 100 por minuto. |
| Enlace público de cotización, PDF (`quote-link-pdf`) | `RemoteIpAddress` | `pdf`: 10 por ventana fija de 5 minutos; global 100 por minuto. |
| Escrituras del portal, por usuario (`IPortalActionThrottle`, en memoria, ventana deslizante de 15 minutos) | Id de usuario del portal | `POST /portal/service-requests` 5, `POST /portal/properties` 10, `POST /portal/messages` 5, solicitudes de reprogramación 5; cada intento cuenta. Global: solicitudes de servicio 100 por hora, las otras tres 300 por minuto. Las acciones de cotización y factura conservan los grupos públicos, con clave por usuario y recurso. El inicio de sesión y las invitaciones del portal reutilizan los límites de inicio de sesión e invitación (por cliente y por email). |
| Restablecimiento de contraseña, por email | SHA-256 del email normalizado | 3 peticiones por 60 minutos (`IPasswordResetEmailThrottle`, en memoria con límite), contadas para todo email bien formado. No es un `429`: la petición devuelve el `202` neutro sin token ni correo. |
| Por email | SHA-256 del email normalizado | Tras 5 intentos fallidos (`401`) en 15 minutos deslizantes, `429` antes de verificar la contraseña hasta que el fallo más antiguo salga de la ventana. Los emails desconocidos cuentan. Un éxito lo borra; `400`, `413`, `415` y `429` no cuentan. |
| Registro de organización, por cliente | `RemoteIpAddress` | `POST /organization-registrations`: 5 por ventana fija de 15 minutos; global 100 por hora. |
| Enlace público de factura, lectura | `RemoteIpAddress` | `view`, `logo`, `photos`, `photos/content`: 60 por ventana fija de 5 minutos; global 600 por minuto. |
| Enlace público de factura, PDF | `RemoteIpAddress` | `pdf`, `receipt`, `completion-report`: 10 por ventana fija de 5 minutos; global 100 por minuto. |
| Enlace público de factura, estado de pago / intención de tarjeta | `RemoteIpAddress` | `payments/status`: 60 por 5 minutos, global 600 por minuto. `payments/card-intent`: 5 por 5 minutos, global 100 por minuto. |
| Enlace público de factura, acciones | Partición global más por factura | `bank-transfer-notice` y `review`: global 100 por minuto y un límite por hora por factura (`IInvoiceActionThrottle`, en memoria). |

- Los rechazos son ProblemDetails `429` con `Retry-After` en segundos enteros y
  cabeceras CORS.
- Los contadores se reinician con el reinicio y no se comparten entre instancias. Intentos
  concurrentes para un mismo email pueden superar brevemente el límite por email (aceptado).

## Pipeline y CORS

Orden del middleware: `RequestLoggingMiddleware`, `UseExceptionHandler`,
`UseStatusCodePages`, `InvitationNoStoreMiddleware`, `PasswordResetNoStoreMiddleware`, `PortalNoStoreMiddleware`, `MessagesNoStoreMiddleware`, `QuoteLinkPublicHeadersMiddleware`, (Development: OpenAPI, Swagger UI),
`UseHttpsRedirection`, `UseCors`, `UseRateLimiter`, `UseAuthentication`,
`InvalidSessionCookieMiddleware`, `UseAuthorization`, endpoints.

La política CORS permite solo los orígenes configurados, cualquier cabecera y método,
credenciales (`Access-Control-Allow-Credentials: true`) y expone
`Retry-After`. Los demás orígenes no reciben cabeceras CORS.

## Logging

El logging usa el proveedor de consola integrado de `ILogger`:

- Development: formateador `simple` de una línea, comandos SQL de EF Core visibles.
- Otros entornos: formateador `json` con marcas de tiempo UTC y scopes (que
  llevan `TraceId`), EF Core en `Warning`.

`RequestLoggingMiddleware` (primero en el pipeline) escribe una línea por petición:

```text
HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMs:0.0} ms (traceId {TraceId})
```

| Estado | Nivel |
| --- | --- |
| `2xx`/`3xx` | `Information` (`Debug` para las sondas `/health*` exitosas) |
| `4xx` | `Warning` |
| `5xx` | `Error` |

- Si una excepción escapa del pipeline, el estado es `500`, o el estado
  ya enviado cuando la respuesta había empezado.
- El `traceId` de ProblemDetails y del log de peticiones es el id completo de Activity W3C
  (`00-<trace-id>-<span-id>-<flags>`); el `TraceId` del scope de la consola JSON es solo
  el trace id de 32 hex, así que correlaciona por el segmento trace-id.

Reglas:

- `GlobalExceptionHandler` registra cada excepción no controlada exactamente una vez. .NET 10
  suprime los diagnósticos propios del middleware de excepciones para las excepciones
  controladas; la línea de la petición no lleva excepción.
- Nunca registrar query strings, cuerpos, cabeceras, contraseñas, tokens, cadenas
  de conexión ni datos sensibles de clientes. Solo se registran método y ruta.
- Mantener `Microsoft.AspNetCore` en `Warning`: los logs de peticiones integrados del
  hosting incluyen el query string.
- El logging de datos sensibles de EF Core se mantiene deshabilitado.
- Inicio de sesión: nunca registrar cuerpos de petición, emails, contraseñas, hashes de contraseña,
  valores de cookie ni cabeceras `Set-Cookie`. Un inicio de sesión fallido registra solo
  `Sign-in failed: {FailureCategory} {UserId}` (id de usuario cuando se conoce).

## Aislamiento en las pruebas

- `FieldOpsApiFactory` pasa los ajustes mediante `UseSetting`; se aplican
  con la precedencia de los argumentos de línea de comandos, por encima de user-secrets y variables
  de entorno.
- Las pruebas sin base de datos usan una cadena de conexión inalcanzable (`127.0.0.1`,
  puerto `1`) que falla rápido.
- Las pruebas que dependen de la base de datos usan Testcontainers (`postgres:17-alpine`), nunca la
  base de datos local de FieldOps.
- Todo ajuste obligatorio nuevo debe definirse en `FieldOpsApiFactory`.
- `FieldOpsApiFactory` usa claves de Data Protection efímeras (un anillo de claves por
  host, nada se escribe en el perfil del desarrollador) y define
  `RemoteIpAddress` a partir de la cabecera exclusiva de pruebas `X-Test-Client-IP`.
- Las pruebas de sesión usan clientes sin contenedor de cookies y reenvían la
  cookie explícitamente (un contenedor descarta las cookies `Secure` sobre HTTP). El tiempo se
  controla sobrescribiendo el `TimeProvider` registrado.
- Las pruebas de sesión comparten una instancia de PostgreSQL de Testcontainers migrada con
  `MigrateAsync`; la prueba del límite de tamaño de petición corre sobre Kestrel (`UseKestrel`).

## Diferido al despliegue

HSTS, forwarded headers, `AllowedHosts` y cómo interactúan las sondas con la redirección
HTTPS se deciden con el trabajo de despliegue. También diferido:

- Persistencia de las claves de Data Protection.
- Forwarded headers: hasta que se configuren, `RemoteIpAddress` detrás del proxy es
  el proxy, así que el límite de inicio de sesión por IP se aplica a todos los clientes en conjunto.
