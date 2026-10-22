import { Route } from '@angular/router';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Jobs list (`/jobs`); mounted inside the app shell, which owns the auth guard. Not role-guarded:
 * roles without Read get the forbidden state inside the page (BR-21).
 */
export const jobsRoute: Route = withI18n(
  {
    path: 'jobs',
    pathMatch: 'full',
    title: 'titles.jobs',
    loadComponent: () => import('./pages/jobs/jobs').then((m) => m.Jobs),
  },
  'jobs',
);

/** Read-only job detail (`/jobs/:id`, BR-22). */
export const jobDetailRoute: Route = withI18n(
  {
    path: 'jobs/:id',
    pathMatch: 'full',
    title: 'titles.job',
    loadComponent: () => import('./pages/job-detail/job-detail').then((m) => m.JobDetail),
  },
  'jobs',
);
