import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';

import { SessionService } from '../services/session.service';

/**
 * Sends visitors who already have a valid session (`GET /sessions/current`
 * returns `200`) to Overview; everyone else may open the public page.
 */
export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  const overviewRedirect = new RedirectCommand(router.parseUrl('/overview'), {
    replaceUrl: true,
  });

  return inject(SessionService)
    .loadCurrent()
    .pipe(
      map(() => overviewRedirect),
      catchError(() => of(true)),
    );
};
