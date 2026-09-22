import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Placeholder for attaching credentials to FieldOps API requests.
 *
 * Authentication is not implemented yet, so requests pass through unchanged
 * and no token is sent. When authentication is specified, credentials must
 * only be attached to requests that satisfy `isApiUrl`.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => next(request);
