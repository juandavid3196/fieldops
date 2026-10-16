import { Route } from '@angular/router';

/**
 * Audit trail (`/audit-log`, Design 29); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: roles without access get the forbidden state inside the page (BR-23).
 */
export const auditLogRoute: Route = {
  path: 'audit-log',
  pathMatch: 'full',
  title: 'Audit trail · FieldOps',
  loadComponent: () => import('./pages/audit-log/audit-log').then((m) => m.AuditLog),
};
