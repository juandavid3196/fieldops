import { Route } from '@angular/router';

import { knownModuleGuard, moduleTitle } from '../coming-soon/coming-soon.routes';

export const TODAY_ROUTE_PATH = 'today';

/** Top-bar back button of the job page (`data.shellBack`). */
export const TODAY_BACK = { link: '/today', label: "Back to Today's jobs" };

/** Titles shown in the technician top bar (`data.shellTitle`). */
export const TODAY_TITLE = "Today's jobs";
export const JOB_TITLE = 'Job details';

/** Today's jobs (`/today`); inside the technician shell. Not role-guarded: other roles see the forbidden state. */
export const todayRoute: Route = {
  path: TODAY_ROUTE_PATH,
  pathMatch: 'full',
  title: "Today's jobs · FieldOps",
  data: { shellTitle: TODAY_TITLE },
  loadComponent: () => import('./pages/today/today').then((m) => m.Today),
};

/** Job page (`/today/visits/:visitId`). */
export const visitDetailRoute: Route = {
  path: `${TODAY_ROUTE_PATH}/visits/:visitId`,
  pathMatch: 'full',
  title: 'Job details · FieldOps',
  data: { shellTitle: JOB_TITLE, shellBack: TODAY_BACK },
  loadComponent: () => import('./pages/visit-detail/visit-detail').then((m) => m.VisitDetail),
};

/** Schedule, Time, Messages and notifications: the shared Coming soon page inside the technician shell. */
export const technicianComingSoonRoute: Route = {
  path: `${TODAY_ROUTE_PATH}/soon/:module`,
  pathMatch: 'full',
  canActivate: [knownModuleGuard],
  title: moduleTitle,
  data: { comingSoonTarget: { link: '/today', label: "Back to Today's jobs" } },
  loadComponent: () =>
    import('../coming-soon/pages/coming-soon/coming-soon').then((m) => m.ComingSoon),
};

export const TECHNICIAN_SHELL_ROUTES: Route[] = [
  todayRoute,
  visitDetailRoute,
  technicianComingSoonRoute,
];
