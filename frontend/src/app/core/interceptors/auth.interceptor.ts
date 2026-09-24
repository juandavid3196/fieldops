import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { API_CONFIG, isApiUrl } from '../config/api.config';

/**
 * Sends the HttpOnly session cookie with FieldOps API requests only.
 *
 * The session lives in a cookie the browser manages, so no token or
 * `Authorization` header is ever added. Requests to other origins or static
 * assets are left untouched so the cookie is never exposed to them.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) =>
  isApiUrl(inject(API_CONFIG), request.url)
    ? next(request.clone({ withCredentials: true }))
    : next(request);
