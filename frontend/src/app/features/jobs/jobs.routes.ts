import { Route } from '@angular/router';

/**
 * Jobs list (`/jobs`); mounted inside the app shell, which owns the auth guard. Not role-guarded:
 * roles without Read get the forbidden state inside the page (BR-21).
 */
export const jobsRoute: Route = {
  path: 'jobs',
  pathMatch: 'full',
  title: 'Jobs · FieldOps',
  loadComponent: () => import('./pages/jobs/jobs').then((m) => m.Jobs),
};

/** Read-only job detail (`/jobs/:id`, BR-22). */
export const jobDetailRoute: Route = {
  path: 'jobs/:id',
  pathMatch: 'full',
  title: 'Job · FieldOps',
  loadComponent: () => import('./pages/job-detail/job-detail').then((m) => m.JobDetail),
};
