import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { isApiError } from '../models/api-error.model';
import { DEMO_READ_ONLY_CODE, DemoModeService } from '../services/demo-mode.service';

/**
 * Tells the shell when the API refused a write because the organization is a read-only demo. Registered before the error
 * interceptor so it sees the normalized {@link ApiError}; the error keeps flowing to the caller unchanged.
 */
export const demoModeInterceptor: HttpInterceptorFn = (request, next) => {
  const demo = inject(DemoModeService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (isApiError(error) && error.status === 403 && error.code === DEMO_READ_ONLY_CODE) {
        demo.notifyRefusal();
      }

      return throwError(() => error);
    }),
  );
};
