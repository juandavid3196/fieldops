import { Route } from '@angular/router';

import { assessmentUnsavedChangesGuard } from './guards/assessment-unsaved-changes.guard';

/**
 * Requests pipeline (`/requests`); mounted inside the app shell, which owns the auth guard. Not
 * role-guarded: technicians and unknown roles get the forbidden state inside the page (BR-01).
 */
export const requestsRoute: Route = {
  path: 'requests',
  pathMatch: 'full',
  title: 'Requests · FieldOps',
  loadComponent: () => import('./pages/requests/requests').then((m) => m.Requests),
};

/**
 * Schedule assessment page (`/requests/:requestId/assessment`); same shell and role handling as the
 * board: read roles and technicians get the forbidden state inside the page.
 */
export const assessmentRoute: Route = {
  path: 'requests/:requestId/assessment',
  pathMatch: 'full',
  title: 'Schedule assessment · FieldOps',
  canDeactivate: [assessmentUnsavedChangesGuard],
  loadComponent: () => import('./pages/assessment/assessment').then((m) => m.Assessment),
};
