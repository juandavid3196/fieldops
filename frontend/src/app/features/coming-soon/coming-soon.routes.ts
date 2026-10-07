import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, ResolveFn, Route, Router } from '@angular/router';

import { comingSoonModuleName } from '../../core/config/coming-soon-modules';

/** Where the page returns and where an unknown slug redirects; configured through route `data`. */
export interface ComingSoonTarget {
  readonly link: string;
  readonly label: string;
}

export const DEFAULT_COMING_SOON_TARGET: ComingSoonTarget = {
  link: '/overview',
  label: 'Back to Overview',
};

export function comingSoonTarget(route: ActivatedRouteSnapshot): ComingSoonTarget {
  return (
    (route.data['comingSoonTarget'] as ComingSoonTarget | undefined) ?? DEFAULT_COMING_SOON_TARGET
  );
}

/** FR-03: an unknown slug redirects to the shell's home (Overview by default); no request is made. */
export const knownModuleGuard: CanActivateFn = (route: ActivatedRouteSnapshot) =>
  comingSoonModuleName(route.paramMap.get('module') ?? '') !== null ||
  inject(Router).parseUrl(comingSoonTarget(route).link);

export const moduleTitle: ResolveFn<string> = (route: ActivatedRouteSnapshot) => {
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
