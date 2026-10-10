import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';

import { PortalSessionService } from '../services/portal-session.service';

/**
 * Lets the route activate only when `GET /portal/sessions/current` returns `200`; any other
 * outcome redirects to the portal sign-in. UX only: the backend authorizes.
 */
export const portalGuard: CanActivateFn = () => {
  const router = inject(Router);
  const signInRedirect = new RedirectCommand(router.parseUrl('/portal/sign-in'), {
    replaceUrl: true,
  });

  return inject(PortalSessionService)
    .loadCurrent()
    .pipe(
      map(() => true),
      catchError(() => of(signInRedirect)),
    );
};
