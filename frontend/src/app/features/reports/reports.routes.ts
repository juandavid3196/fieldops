import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Reports workspace (`/reports`, Design 25); mounted inside the app shell, which owns the auth
 * guard. Not role-guarded: roles without a report get the forbidden state inside the page (BR-02).
 */
export const reportsRoute: Route = withI18n(
  {
    path: 'reports',
    pathMatch: 'full',
    title: 'titles.reports',
    loadComponent: () => import('./pages/reports/reports').then((m) => m.Reports),
  },
  'reports',
);
