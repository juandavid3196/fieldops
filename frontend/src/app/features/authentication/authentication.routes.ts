import { Routes } from '@angular/router';

import { guestGuard } from '../../core/guards/guest.guard';

export default [
  {
    path: 'sign-in',
    canActivate: [guestGuard],
    title: 'Sign in · FieldOps',
    loadComponent: () => import('./pages/sign-in/sign-in').then((m) => m.SignIn),
  },
  {
    // Public on purpose (FR-11): an existing session is ignored until acceptance replaces it.
    path: 'invitation',
    title: 'Accept invitation · FieldOps',
    loadComponent: () => import('./pages/invitation/invitation').then((m) => m.Invitation),
  },
  {
    // Public on purpose: an existing session is ignored (password-recovery FR-09, FR-10).
    path: 'forgot-password',
    title: 'Reset your password · FieldOps',
    loadComponent: () =>
      import('./pages/forgot-password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Create a new password · FieldOps',
    loadComponent: () =>
      import('./pages/reset-password/reset-password').then((m) => m.ResetPassword),
  },
] satisfies Routes;
