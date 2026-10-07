import { Route } from '@angular/router';

import { scheduleUnsavedChangesGuard } from './guards/schedule-unsaved-changes.guard';

/**
 * Dispatch calendar (`/schedule`); mounted inside the app shell, which owns the auth guard. Not
 * role-guarded: Technician, Accounting and unknown roles get the forbidden state (BR-01).
 */
export const scheduleRoute: Route = {
  path: 'schedule',
  pathMatch: 'full',
  title: 'Schedule · FieldOps',
  canDeactivate: [scheduleUnsavedChangesGuard],
  loadComponent: () => import('./pages/schedule/schedule').then((m) => m.SchedulePage),
};
