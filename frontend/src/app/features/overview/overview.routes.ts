import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router, Routes } from '@angular/router';

import { canSeeDashboard } from '../../core/config/dashboard-roles';
import { SessionService } from '../../core/services/session.service';

/**
 * operations-dashboard BR-01: the Manage roles land on `/dashboard`; other roles keep the
 * welcome page. The session is already loaded by the shell's auth guard.
 */
export const overviewRedirectGuard: CanActivateFn = () => {
  const router = inject(Router);
  return canSeeDashboard(inject(SessionService).session()?.role.code)
    ? new RedirectCommand(router.parseUrl('/dashboard'), { replaceUrl: true })
    : true;
};

export default [
  {
    path: '',
    title: 'titles.overview',
    canActivate: [overviewRedirectGuard],
    loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview),
  },
] satisfies Routes;
