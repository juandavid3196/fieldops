import { Route } from '@angular/router';

import { skillsAvailabilityUnsavedChangesGuard } from './guards/skills-availability-unsaved-changes.guard';
import { teamUnsavedChangesGuard } from './guards/team-unsaved-changes.guard';

import { withI18n } from '../../core/i18n/i18n-scopes';
/**
 * Team (`/team`); mounted inside the app shell, which owns the auth guard. Not role-guarded:
 * Accounting, Viewer and unknown roles get the forbidden state inside the shell (BR-21).
 */
export const teamRoute: Route = withI18n(
  {
    path: 'team',
    pathMatch: 'full',
    title: 'titles.team',
    canDeactivate: [teamUnsavedChangesGuard],
    loadComponent: () => import('./pages/team/team').then((m) => m.Team),
  },
  'team',
);

/** Technician skills & availability (`/team/:technicianId/skills-availability`); same role rules as Team (BR-21). */
export const skillsAvailabilityRoute: Route = withI18n(
  {
    path: 'team/:technicianId/skills-availability',
    pathMatch: 'full',
    title: 'titles.skillsAndAvailability',
    canDeactivate: [skillsAvailabilityUnsavedChangesGuard],
    loadComponent: () =>
      import('./pages/skills-availability/skills-availability').then(
        (m) => m.SkillsAvailabilityPage,
      ),
  },
  'team',
);

/** `/team/:technicianId` opens the Overview; the redirect replaces the history entry (BR-03). */
export const technicianProfileRedirectRoute: Route = {
  path: 'team/:technicianId',
  pathMatch: 'full',
  redirectTo: '/team/:technicianId/overview',
};

/** Technician Overview (`/team/:technicianId/overview`); same role rules as Team (BR-21). */
export const technicianOverviewRoute: Route = withI18n(
  {
    path: 'team/:technicianId/overview',
    pathMatch: 'full',
    title: 'titles.technicianOverview',
    loadComponent: () =>
      import('./pages/technician-overview/technician-overview').then((m) => m.TechnicianOverview),
  },
  'team',
);

/** Technician Job history (`/team/:technicianId/job-history`); same role rules as Team (BR-21). */
export const technicianJobHistoryRoute: Route = withI18n(
  {
    path: 'team/:technicianId/job-history',
    pathMatch: 'full',
    title: 'titles.technicianJobHistory',
    loadComponent: () =>
      import('./pages/technician-job-history/technician-job-history').then(
        (m) => m.TechnicianJobHistory,
      ),
  },
  'team',
);
