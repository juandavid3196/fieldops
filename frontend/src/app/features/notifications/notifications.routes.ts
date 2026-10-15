import { Route } from '@angular/router';

/**
 * Notifications center (`/notifications`, Design 32); mounted inside the app shell, which owns the
 * auth guard. Not role-guarded: roles without the inbox policy get the forbidden state in the page.
 */
export const notificationsRoute: Route = {
  path: 'notifications',
  pathMatch: 'full',
  title: 'Notifications · FieldOps',
  loadComponent: () => import('./pages/notifications/notifications').then((m) => m.Notifications),
};
