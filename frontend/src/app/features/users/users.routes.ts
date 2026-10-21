import { Route } from '@angular/router';

import { usersUnsavedChangesGuard } from './guards/users-unsaved-changes.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Users & permissions (`/admin/users`); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: other roles get the forbidden state rendered inside the shell (BR-14).
 */
export const usersAdminRoute: Route = withI18n(
  {
    path: 'admin/users',
    pathMatch: 'full',
    title: 'titles.usersAndPermissions',
    canDeactivate: [usersUnsavedChangesGuard],
    loadComponent: () => import('./pages/users/users').then((m) => m.Users),
  },
  'users',
);
