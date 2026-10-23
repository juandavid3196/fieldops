[English](schema-audit-report.md) | **Español**

# Informe de auditoría del esquema de base de datos de FieldOps

**Fecha:** 2026-09-22
**Fuente de verdad:** `docs/database/FieldOps_PostgreSQL_Schema.sql` (`docs/database/fieldops-schema.sql`)
**Alcance:** Todas las entidades de Domain, las configuraciones de EF Core, los registros de `FieldOpsDbContext` y las migraciones generadas.

## Estado actual (2026-10-23)

Este informe registra la auditoría inicial del 2026-09-22 (se conserva abajo como registro histórico). El esquema ha crecido desde
entonces; las cifras que importan hoy son:

| Elemento | Auditoría inicial | Actual |
|---|---|---|
| Tablas en `fieldops-schema.sql` | 50 | 70 |
| Tipos de entidad de EF Core en el snapshot del modelo (`ToTable`) y `DbSet<T>` | 50 | 70 |
| Configuraciones de entidad | 50 | 70 |
| Migraciones de EF Core | 10 | 39 (la última: `AddUserPreferredTheme`) |

Las 20 tablas adicionales pertenecen a las funcionalidades especificadas después de la auditoría (portal, mensajería, pagos,
invitaciones, restablecimiento de contraseña, solicitudes de reprogramación, imágenes de catálogo y otras). Según el flujo del proyecto, los cambios de mapeo pasan por una revisión de
persistencia contra el esquema antes de auditar una spec.

**Script idempotente:** `backend/database-migration-script.sql` se regeneró el 2026-10-23 y cubre las 39 migraciones (5.371 líneas).
Regéneralo con el comando de `backend/CLAUDE.md` después de agregar una migración; aplicar las migraciones lo hace siempre el desarrollador con
`dotnet ef database update`.

---

## Alcance

Se auditaron las 50 entidades de Domain, sus configuraciones de EF Core, los registros de
`FieldOpsDbContext` y el historial completo de 10 migraciones frente a
`docs/database/fieldops-schema.sql`, usando el
`FieldOpsDbContextModelSnapshot.cs` autoritativo (el modelo resuelto completo de EF) como
artefacto principal de referencia cruzada, complementado con la comparación directa del esquema
y el análisis del historial de git.

## 1. Tablas esperadas (según el esquema) — 50 en total

`organizations, branches, users, roles, permissions, role_permissions,
organization_users, organization_user_branches, user_invitations,
invitation_branches, customers, customer_contacts, properties,
customer_notes, service_categories, catalog_items, skills,
technician_profiles, technician_skills, technician_weekly_availability,
technician_breaks, technician_exceptions, service_requests,
request_attachments, request_messages, request_status_history, assessments,
assessment_attachments, quotes, quote_versions, quote_lines,
quote_responses, work_orders, work_order_required_skills,
work_order_checklist_templates, visits, visit_assignments,
visit_status_history, visit_time_entries, visit_checklist_items,
visit_materials, visit_evidence, visit_incidents, customer_signoffs,
invoices, invoice_lines, payments, payment_allocations, notifications,
audit_logs`

## 2. Tablas implementadas — 50/50 ✅

Las 50 tablas se encontraron en el modelo de EF Core (conteo de `.ToTable(...)` = 50, coincidencia
exacta), cada una con su `DbSet<T>` correspondiente en `FieldOpsDbContext`
(conteo = 50).

## 3. Elementos faltantes

**No se encontró ninguno.**

## 4. Elementos incorrectos

**No se encontró ninguno.** Verificaciones detalladas realizadas:

| Categoría | Resultado |
|---|---|
| Propiedades sombra / FK duplicadas | Ninguna detectada: se escanearon todas las propiedades de tipo `Guid` en el snapshot; sin nombres `*Id1`, `*Id2` ni sombra con sufijo de tipo |
| Comportamientos de borrado | Las 132 relaciones se revisaron línea por línea. Las 22 relaciones `Cascade` coinciden exactamente con las cláusulas `ON DELETE CASCADE` del esquema; todas las demás son `NoAction`, incluidas las excepciones deliberadas (`customer_signoffs.visit_id`, `audit_logs.actor_user_id`/`branch_id` correctamente **sin** cascada) |
| Claves foráneas compuestas | Todas las FK compuestas `(organization_id, X)` → `(organization_id, id)` están correctamente emparejadas con `HasPrincipalKey`; no hay compuestas mal formadas |
| Restricciones check | 43/43 presentes, el texto coincide exactamente con las expresiones CHECK del esquema (los *nombres* de las restricciones usan una convención propia `ck_table_column` porque el SQL original no nombra sus checks; es cosmético, no una desviación) |
| Índices | Los 16 índices con nombre del esquema están presentes; ambos índices parciales (`ix_assignments_technician` y el correctivo `ix_visit_assignments_active_technician_unique`) conservan su filtro `WHERE unassigned_at IS NULL`; los 5 índices con orden `DESC` (`ix_requests_pipeline`, `ix_quotes_status`, `ix_payments_customer_date`, `ix_audit_entity`, `ix_audit_actor`) están correctamente configurados mediante `IsDescending` |
| Mapeos de enums | 12/12 enums nativos de PostgreSQL correctamente declarados con el orden exacto de etiquetas; 4 "pseudo-enums" varchar+CHECK (`TechnicianStatus`, `VisitTimeEntryType`, `VisitEvidenceType`, `NotificationChannel`) correctamente mapeados como `character varying`, **no** como enums nativos |
| Mapeos jsonb | 7/7 columnas (`business_hours`, `billing_address`, `service_address`, `payload`, `before_data`, `after_data`, `metadata`) correctamente tipadas |
| Mapeos inet | 2/2 columnas (`audit_logs.ip_address`, `quote_responses.ip_address`) correctamente mapeadas a `System.Net.IPAddress` |
| Precisión numérica | Todas las columnas `numeric(14,2)`, `numeric(7,4)`, `numeric(12,3)`, `numeric(9,6)`, `numeric(4,1)` verificadas contra las llamadas a `HasPrecision` |
| Propiedad del tenant (`organization_id`) | Presente en todos los lugares donde el esquema lo declara, incluido el patrón de doble FK (directa `→organizations` **y** compuesta `→parent`) en cada tabla que lo necesita; correctamente **ausente** en las tablas sin columna `organization_id` en el esquema (`quote_lines`, `assessment_attachments`, hijas `visit_*`, `payment_allocations`, etc.) |
| PK `bigserial` | `audit_logs.id` usa correctamente `UseIdentityByDefaultColumn()` (identidad bigint), igual que el patrón `smallserial` ya usado para `roles`/`permissions` |

## 5. Relaciones configuradas de forma distinta entre navegaciones y mapeos de BD

**No se encontró ninguna.** El emparejamiento `HasForeignKey`/`HasPrincipalKey`/`OnDelete` de cada
navegación se cruzó directamente en la salida resuelta del snapshot (no solo en
los archivos de configuración fuente), de modo que una desviación entre navegación y mapeo
habría aflorado aquí; no ocurrió.

## 6. Integridad de las migraciones

- `dotnet ef migrations list` → 10 migraciones, que coinciden con todas las migraciones
  generadas a lo largo del historial de implementación de este proyecto.
- Cada archivo de migración (`.cs` y `.Designer.cs`, 20 archivos) se rastreó con
  `git log -- <path>` hasta **exactamente un commit cada uno**: sin ediciones posteriores
  a su creación.
- `git status` limpio al inicio y al final de la auditoría.

## 7–9. Migración correctiva

Se generó `AlignDatabaseSchema` para comprobar de forma concluyente si había cambios
pendientes en el modelo. **Resultado: completamente vacía** (`Up`/`Down` no contienen ninguna
operación): prueba definitiva de que el modelo de EF Core coincide exactamente con la última
migración aplicada. Según el requisito de que la migración correctiva "debe
contener solo las diferencias necesarias", una migración vacía representa cero
diferencias, por lo que **se eliminó** en lugar de dejarla en el historial. **No hizo falta
crear ninguna migración correctiva.**

## 10. Script SQL idempotente

Generado en `backend/database-migration-script.sql` (2.333 líneas).
Inspeccionado: empieza con el arranque de `__EFMigrationsHistory`, envuelve cada
operación en guardas `DO $EF$ ... IF NOT EXISTS(...) ... END $EF$;` con clave por
ID de migración, cubre las 10 migraciones en el orden correcto (verificado mediante la extracción
de `migration_id` distintos) y termina con un `COMMIT` limpio. **No se aplicó**
a ninguna base de datos.

## 11. Resultados de validación

| Comando | Resultado |
|---|---|
| `dotnet format --verify-no-changes` | ✅ Pasa |
| `dotnet build` | ✅ Pasa: 0 advertencias, 0 errores |
| `dotnet test` | ✅ Pasa: 232/232 (231 unitarias + 1 de integración) |
| `dotnet ef migrations list` | ✅ 10 migraciones, todas aplicadas según `__EFMigrationsHistory` |
| `dotnet ef migrations script --idempotent` | ✅ Generado e inspeccionado, no aplicado |
| `dotnet ef database update` | No se ejecutó, por instrucción |

## 12. Aprobación final

### ✅ APROBADO

El modelo de persistencia de EF Core de FieldOps está totalmente alineado con
`docs/database/fieldops-schema.sql`. Las 50 tablas, 43 restricciones check,
más de 16 índices, 12 enums, 4 pseudo-enums, mapeos jsonb/inet, claves compuestas
y comportamientos de borrado son correctos y están completos. No se encontraron elementos faltantes, ni
incorrectos, ni propiedades sombra, ni desviación de relaciones, ni manipulación del historial de
migraciones. No hizo falta ninguna migración correctiva.

**Nota sobre git status:** solo `backend/database-migration-script.sql` se
creó como entregable de esta auditoría; no se modificó ningún código fuente ni ninguna migración.
