[English](README.md) | **Español**

# Aplicación web de FieldOps

Aplicación de una sola página en Angular 22 para FieldOps: la app del personal, el shell móvil del técnico y el portal del cliente.
La visión general del proyecto, la arquitectura y la configuración están en el [README raíz](../README.es.md).

## Inicio rápido

```bash
npm ci
npm start        # http://localhost:4200, espera la API en http://localhost:5034
```

Inicia primero la API con el perfil de lanzamiento `http` (ver [Primeros pasos](../README.es.md#20-primeros-pasos)).

## Scripts

| Tarea                             | Comando                                                        |
| --------------------------------- | -------------------------------------------------------------- |
| Iniciar el servidor de desarrollo | `npm start`                                                    |
| Build de producción               | `npm run build -- --configuration production`                  |
| Pruebas unitarias (Vitest)        | `npm run test -- --watch=false`                                |
| Lint                              | `npm run lint`                                                 |
| Verificar / aplicar formato       | `npm run format:check` / `npm run format`                      |
| Verificación de i18n              | `npm run i18n:check` (autoprueba: `npm run i18n:check:test`)   |
| Verificación de tema              | `npm run theme:check` (autoprueba: `npm run theme:check:test`) |

## Dónde está cada cosa

- `src/app/core`, `shared`, `layout`, `features`: estructura descrita en [`CLAUDE.md`](CLAUDE.md) y en [Arquitectura del frontend](../README.es.md#8-arquitectura-del-frontend).
- `src/styles`: tokens de diseño y estilos globales ([arquitectura de estilos](../docs/frontend/styling-architecture.es.md)).
- `scripts/i18n-check`, `scripts/theme-check`: verificaciones de consistencia propias que también corren en CI.
- Archivos de entorno y reverse proxy de producción: [configuración del frontend](../docs/frontend/frontend-configuration.es.md).
- Traducciones: [arquitectura de i18n](../docs/frontend/i18n-architecture.es.md).
