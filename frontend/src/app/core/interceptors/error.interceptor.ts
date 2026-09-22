import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { API_CONFIG, isApiUrl } from '../config/api.config';
import { ApiErrorService } from '../services/api-error.service';

/**
 * Converts failed FieldOps API responses into {@link ApiError} values so
 * consumers never handle raw `HttpErrorResponse` or backend internals.
 * Requests to other origins are left untouched.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isApiUrl(inject(API_CONFIG), request.url)) {
    return next(request);
  }

  const apiErrors = inject(ApiErrorService);
  return next(request).pipe(
    catchError((error: unknown) => throwError(() => apiErrors.toApiError(error))),
  );
};
