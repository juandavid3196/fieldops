import { Route } from '@angular/router';

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
