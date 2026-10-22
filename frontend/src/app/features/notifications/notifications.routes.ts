import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Notifications center (`/notifications`, Design 32); mounted inside the app shell, which owns the
 * auth guard. Not role-guarded: roles without the inbox policy get the forbidden state in the page.
 */
export const notificationsRoute: Route = withI18n(
  {
    path: 'notifications',
    pathMatch: 'full',
    title: 'titles.notifications',
    loadComponent: () => import('./pages/notifications/notifications').then((m) => m.Notifications),
  },
  'notifications',
);
