import { Routes } from '@angular/router';

import { accountUnsavedChangesGuard } from './guards/account-unsaved-changes.guard';

/**
 * Account settings under `base`: `/account` in the app shell; the technician shell mounts its own
 * copy so technicians stay in their layout.
 */
export function createAccountRoutes(base: string): Routes {
  return [
    {
      path: '',
      data: { accountBase: base },
      loadComponent: () =>
        import('./pages/account-layout/account-layout').then((m) => m.AccountLayout),
      children: [
        { path: '', pathMatch: 'full', redirectTo: 'profile' },
        {
          path: 'profile',
          title: 'titles.myProfile',
          canDeactivate: [accountUnsavedChangesGuard],
          loadComponent: () => import('./pages/profile/profile').then((m) => m.Profile),
        },
        {
          path: 'security',
          title: 'titles.security',
          canDeactivate: [accountUnsavedChangesGuard],
          loadComponent: () => import('./pages/security/security').then((m) => m.Security),
        },
      ],
    },
  ];
}

/** Authenticated account settings; mounted inside the app shell, which owns the auth guard. */
export default createAccountRoutes('/account');
