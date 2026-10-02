import { Route } from '@angular/router';

/**
 * Requests pipeline (`/requests`); mounted inside the app shell, which owns the auth guard. Not
 * role-guarded: technicians and unknown roles get the forbidden state inside the page (BR-01).
 */
export const requestsRoute: Route = {
  path: 'requests',
  pathMatch: 'full',
  title: 'Requests · FieldOps',
  loadComponent: () => import('./pages/requests/requests').then((m) => m.Requests),
};
