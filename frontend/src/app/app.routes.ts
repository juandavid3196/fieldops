import { Routes } from '@angular/router';

import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'auth/sign-in' },
  {
    path: 'auth',
    loadChildren: () => import('./features/authentication/authentication.routes'),
  },
  {
    path: 'overview',
    canActivate: [authGuard],
    loadChildren: () => import('./features/overview/overview.routes'),
  },
  { path: '**', redirectTo: 'auth/sign-in' },
];
