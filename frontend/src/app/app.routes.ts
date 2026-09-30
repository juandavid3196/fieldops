import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';
import { comingSoonRoute } from './features/coming-soon/coming-soon.routes';
import { companyAdminRoute } from './features/organizations/organizations.routes';
import { productsServicesAdminRoute } from './features/products-services/products-services.routes';
import { usersAdminRoute } from './features/users/users.routes';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'auth/sign-in' },
  {
    path: 'auth',
    loadChildren: () => import('./features/authentication/authentication.routes'),
  },
  {
    // Routes declare their own full paths (public registration lives at `auth/register-company`).
    path: '',
    loadChildren: () => import('./features/organizations/organizations.routes'),
  },
  {
    // Pathless authenticated shell: the guard revalidates the session on every
    // navigation between its children and resolves before the shell renders.
    path: '',
    canActivate: [authGuard],
    runGuardsAndResolvers: 'always',
    loadComponent: () => import('./layout/app-shell/app-shell').then((m) => m.AppShell),
    children: [
      {
        path: 'overview',
        loadChildren: () => import('./features/overview/overview.routes'),
      },
      comingSoonRoute,
      companyAdminRoute,
      usersAdminRoute,
      productsServicesAdminRoute,
    ],
  },
  { path: '**', redirectTo: 'auth/sign-in' },
];
