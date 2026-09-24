import { Routes } from '@angular/router';

import { guestGuard } from '../../core/guards/guest.guard';

export default [
  {
    path: 'sign-in',
    canActivate: [guestGuard],
    title: 'Sign in · FieldOps',
    loadComponent: () => import('./pages/sign-in/sign-in').then((m) => m.SignIn),
  },
] satisfies Routes;
