import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Operations dashboard (`/dashboard`); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: other roles get the forbidden state with no data request (BR-01).
 */
export const dashboardRoute: Route = withI18n(
  {
    path: 'dashboard',
    pathMatch: 'full',
    title: 'titles.dashboard',
    loadComponent: () =>
      import('./pages/dashboard-host/dashboard-host').then((m) => m.DashboardHost),
  },
  'dashboard',
);
