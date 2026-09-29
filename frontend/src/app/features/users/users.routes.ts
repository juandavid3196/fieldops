import { Route } from '@angular/router';

import { usersUnsavedChangesGuard } from './guards/users-unsaved-changes.guard';

/**
 * Users & permissions (`/admin/users`); mounted inside the app shell, which owns the auth guard.
 * Not role-guarded: other roles get the forbidden state rendered inside the shell (BR-14).
 */
export const usersAdminRoute: Route = {
  path: 'admin/users',
  pathMatch: 'full',
  title: 'Users & permissions · FieldOps',
  canDeactivate: [usersUnsavedChangesGuard],
  loadComponent: () => import('./pages/users/users').then((m) => m.Users),
};
