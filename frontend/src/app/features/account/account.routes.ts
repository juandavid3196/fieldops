import { Routes } from '@angular/router';

import { accountUnsavedChangesGuard } from './guards/account-unsaved-changes.guard';

/**
 * Account settings under `base`: `/account` in the app shell; the technician shell mounts its own
 * copy so technicians stay in their layout. Both shells list Preferences.
 */
export function createAccountRoutes(base: string, withPreferences = false): Routes {
  return [
    {
      path: '',
      data: { accountBase: base, accountPreferences: withPreferences },
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
        ...(withPreferences
          ? [
              {
                path: 'preferences',
                title: 'titles.preferences',
                canDeactivate: [accountUnsavedChangesGuard],
                loadComponent: () =>
                  import('./pages/preferences/preferences').then((m) => m.Preferences),
              },
            ]
          : []),
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
export default createAccountRoutes('/account', true);
