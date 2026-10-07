import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';

import { landingPath } from '../config/landing';
import { SessionService } from '../services/session.service';

/**
 * Sends visitors who already have a valid session (`GET /sessions/current`
 * returns `200`) to their landing page (Overview, or Today's jobs for technicians); everyone else may open the public page.
 */
export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);

  return inject(SessionService)
    .loadCurrent()
    .pipe(
      map(
        (session) =>
          new RedirectCommand(router.parseUrl(landingPath(session.role.code)), {
            replaceUrl: true,
          }),
      ),
      catchError(() => of(true)),
    );
};
