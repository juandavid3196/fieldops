import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';

import { API_CONFIG, isApiUrl } from '../config/api.config';
import { isApiError } from '../models/api-error.model';
import { PortalSessionService } from '../services/portal-session.service';

/** Portal endpoints whose `401` is a normal outcome, not an ended session. */
function isExcluded(path: string): boolean {
  return (
    path === 'portal/sessions' ||
    path.startsWith('portal/sessions/current') ||
    path.startsWith('portal/invitations/') ||
    path.startsWith('portal/password-resets')
  );
}

/**
 * Sends the contact back to the portal sign-in when a portal request returns `401` while a portal
 * session was in memory. A cold visit without a session stays silent. Declared after the auth
 * interceptor and before the error interceptor, so it sees `ApiError` values.
 */
export const portalUnauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const config = inject(API_CONFIG);
  if (!isApiUrl(config, request.url)) {
    return next(request);
  }
  const base = config.baseUrl.replace(/\/+$/, '');
  const path = request.url.slice(base.length + 1).split(/[?#]/)[0];
  if (!path.startsWith('portal/') || isExcluded(path)) {
    return next(request);
  }

  const session = inject(PortalSessionService);
  const router = inject(Router);
  return next(request).pipe(
    tap({
      error: (error: unknown) => {
        if (isApiError(error) && error.status === 401 && session.session() !== null) {
          session.expire();
          void router.navigateByUrl('/portal/sign-in', { replaceUrl: true });
        }
      },
    }),
  );
};
