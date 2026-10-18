import { ActivatedRouteSnapshot, Route } from '@angular/router';

import { knownModuleGuard, moduleTitle } from '../coming-soon/coming-soon.routes';
import {
  technicianMessagesRoute,
  technicianMessagesSoonRedirect,
} from '../messages/messages.routes';

export const TODAY_ROUTE_PATH = 'today';

/** Top-bar back button of the job page (`data.shellBack`). */
export const TODAY_BACK = { link: '/today', label: "Back to Today's jobs" };

/** Titles shown in the technician top bar (`data.shellTitle`). */
export const TODAY_TITLE = "Today's jobs";
export const JOB_TITLE = 'Job details';
export const SCHEDULE_TITLE = 'Schedule';
export const SCHEDULE_PATH = `/${TODAY_ROUTE_PATH}/schedule`;
export const TIME_TITLE = 'Time';
export const TIME_PATH = `/${TODAY_ROUTE_PATH}/time`;
export const REVIEW_TITLE = 'Review & complete';

/** Today's jobs (`/today`); inside the technician shell. Not role-guarded: other roles see the forbidden state. */
export const todayRoute: Route = {
  path: TODAY_ROUTE_PATH,
  pathMatch: 'full',
  title: "Today's jobs · FieldOps",
  data: { shellTitle: TODAY_TITLE },
  loadComponent: () => import('./pages/today/today').then((m) => m.Today),
};

/** My schedule (`/today/schedule`); inside the technician shell, same access rules as Today. */
export const scheduleRoute: Route = {
  path: `${TODAY_ROUTE_PATH}/schedule`,
  pathMatch: 'full',
  title: 'Schedule · FieldOps',
  data: { shellTitle: SCHEDULE_TITLE },
  loadComponent: () => import('./pages/schedule/schedule').then((m) => m.TechnicianSchedule),
};

/** The Schedule Coming soon destination was replaced by the real page (BR-20). */
export const scheduleSoonRedirect: Route = {
  path: `${TODAY_ROUTE_PATH}/soon/schedule`,
  pathMatch: 'full',
  redirectTo: SCHEDULE_PATH,
};

/** Time summary (`/today/time`); inside the technician shell, same access rules as Today. */
export const timeRoute: Route = {
  path: `${TODAY_ROUTE_PATH}/time`,
  pathMatch: 'full',
  title: 'Time · FieldOps',
  data: { shellTitle: TIME_TITLE },
  loadComponent: () => import('./pages/time/time').then((m) => m.TechnicianTime),
};

/** The Time Coming soon destination was replaced by the real page (BR-11). */
export const timeSoonRedirect: Route = {
  path: `${TODAY_ROUTE_PATH}/soon/time`,
  pathMatch: 'full',
  redirectTo: TIME_PATH,
};

/** Job page (`/today/visits/:visitId`). */
export const visitDetailRoute: Route = {
  path: `${TODAY_ROUTE_PATH}/visits/:visitId`,
  pathMatch: 'full',
  title: 'Job details · FieldOps',
  data: { shellTitle: JOB_TITLE, shellBack: TODAY_BACK },
  loadComponent: () => import('./pages/visit-detail/visit-detail').then((m) => m.VisitDetail),
};

/** Review placeholder (`/today/visits/:visitId/review`); Design 10 replaces it. Back returns to the job. */
export const visitReviewRoute: Route = {
  path: `${TODAY_ROUTE_PATH}/visits/:visitId/review`,
  pathMatch: 'full',
  title: 'Review & complete · FieldOps',
  data: { shellTitle: REVIEW_TITLE },
  resolve: {
    shellBack: (route: ActivatedRouteSnapshot) => ({
      link: `/${TODAY_ROUTE_PATH}/visits/${encodeURIComponent(route.paramMap.get('visitId') ?? '')}`,
      label: 'Back to job',
    }),
  },
  loadComponent: () => import('./pages/visit-review/visit-review').then((m) => m.VisitReview),
};

/** Messages and notifications: the shared Coming soon page inside the technician shell. */
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
  scheduleRoute,
  scheduleSoonRedirect,
  timeRoute,
  timeSoonRedirect,
  technicianMessagesRoute,
  technicianMessagesSoonRedirect,
  visitDetailRoute,
  visitReviewRoute,
  technicianComingSoonRoute,
];
