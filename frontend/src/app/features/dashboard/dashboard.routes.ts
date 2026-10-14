import { Route } from '@angular/router';

/**
 * Operations dashboard (`/dashboard`); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: other roles get the forbidden state with no data request (BR-01).
 */
export const dashboardRoute: Route = {
  path: 'dashboard',
  pathMatch: 'full',
  title: 'Dashboard · FieldOps',
  loadComponent: () => import('./pages/dashboard-host/dashboard-host').then((m) => m.DashboardHost),
};
