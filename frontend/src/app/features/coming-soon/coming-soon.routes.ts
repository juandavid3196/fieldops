import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, ResolveFn, Route, Router } from '@angular/router';

import { comingSoonModuleName } from '../../core/config/coming-soon-modules';

/** FR-03: an unknown slug redirects to Overview; no request is made. */
const knownModuleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) =>
  comingSoonModuleName(route.paramMap.get('module') ?? '') !== null ||
  inject(Router).parseUrl('/overview');

const moduleTitle: ResolveFn<string> = (route: ActivatedRouteSnapshot) => {
  const name = comingSoonModuleName(route.paramMap.get('module') ?? '');
  return name === null ? 'FieldOps' : `${name} · FieldOps`;
};

/** Shared "Coming soon" page for every future module and top-bar destination; mounted inside the app shell. */
export const comingSoonRoute: Route = {
  path: 'coming-soon/:module',
  pathMatch: 'full',
  canActivate: [knownModuleGuard],
  title: moduleTitle,
  loadComponent: () => import('./pages/coming-soon/coming-soon').then((m) => m.ComingSoon),
};
