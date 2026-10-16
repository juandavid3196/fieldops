import { Routes } from '@angular/router';

import { accountUnsavedChangesGuard } from './guards/account-unsaved-changes.guard';

/** Authenticated account settings; mounted inside the app shell, which owns the auth guard. */
export default [
  {
    path: '',
    loadComponent: () =>
      import('./pages/account-layout/account-layout').then((m) => m.AccountLayout),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'profile' },
      {
        path: 'profile',
        title: 'My profile · FieldOps',
        canDeactivate: [accountUnsavedChangesGuard],
        loadComponent: () => import('./pages/profile/profile').then((m) => m.Profile),
      },
      {
        path: 'security',
        title: 'Security · FieldOps',
        canDeactivate: [accountUnsavedChangesGuard],
        loadComponent: () => import('./pages/security/security').then((m) => m.Security),
      },
    ],
  },
] satisfies Routes;
