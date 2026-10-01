import { Route } from '@angular/router';

import { skillsAvailabilityUnsavedChangesGuard } from './guards/skills-availability-unsaved-changes.guard';
import { teamUnsavedChangesGuard } from './guards/team-unsaved-changes.guard';

/**
 * Team (`/team`); mounted inside the app shell, which owns the auth guard. Not role-guarded:
 * Accounting, Viewer and unknown roles get the forbidden state inside the shell (BR-21).
 */
export const teamRoute: Route = {
  path: 'team',
  pathMatch: 'full',
  title: 'Team · FieldOps',
  canDeactivate: [teamUnsavedChangesGuard],
  loadComponent: () => import('./pages/team/team').then((m) => m.Team),
};

/** Technician skills & availability (`/team/:technicianId/skills-availability`); same role rules as Team (BR-21). */
export const skillsAvailabilityRoute: Route = {
  path: 'team/:technicianId/skills-availability',
  pathMatch: 'full',
  title: 'Skills & availability · FieldOps',
  canDeactivate: [skillsAvailabilityUnsavedChangesGuard],
  loadComponent: () =>
    import('./pages/skills-availability/skills-availability').then((m) => m.SkillsAvailabilityPage),
};
