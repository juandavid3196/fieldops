import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';

import { SessionService } from '../services/session.service';

/**
 * Lets the route activate only when `GET /sessions/current` returns `200`;
 * any other outcome redirects to Sign In. UX only: the backend authorizes.
 */
export const authGuard: CanActivateFn = () => {
  const router = inject(Router);
  const signInRedirect = new RedirectCommand(router.parseUrl('/auth/sign-in'), {
    replaceUrl: true,
  });

  return inject(SessionService)
    .loadCurrent()
    .pipe(
      map(() => true),
      catchError(() => of(signInRedirect)),
    );
};
