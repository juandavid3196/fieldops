import { Route } from '@angular/router';

/**
 * Reports workspace (`/reports`, Design 25); mounted inside the app shell, which owns the auth
 * guard. Not role-guarded: roles without a report get the forbidden state inside the page (BR-02).
 */
export const reportsRoute: Route = {
  path: 'reports',
  pathMatch: 'full',
  title: 'Reports · FieldOps',
  loadComponent: () => import('./pages/reports/reports').then((m) => m.Reports),
};
