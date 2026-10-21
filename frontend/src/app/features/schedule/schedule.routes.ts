import { Route } from '@angular/router';

import { scheduleUnsavedChangesGuard } from './guards/schedule-unsaved-changes.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Dispatch calendar (`/schedule`); mounted inside the app shell, which owns the auth guard. Not
 * role-guarded: Technician, Accounting and unknown roles get the forbidden state (BR-01).
 */
export const scheduleRoute: Route = withI18n(
  {
    path: 'schedule',
    pathMatch: 'full',
    title: 'titles.schedule',
    canDeactivate: [scheduleUnsavedChangesGuard],
    loadComponent: () => import('./pages/schedule/schedule').then((m) => m.SchedulePage),
  },
  'schedule',
);
