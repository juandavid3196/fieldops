import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Audit trail (`/audit-log`, Design 29); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: roles without access get the forbidden state inside the page (BR-23).
 */
export const auditLogRoute: Route = withI18n(
  {
    path: 'audit-log',
    pathMatch: 'full',
    title: 'titles.auditTrail',
    loadComponent: () => import('./pages/audit-log/audit-log').then((m) => m.AuditLog),
  },
  'audit-log',
);
