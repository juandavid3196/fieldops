import { HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';

import { ApiError, ApiErrorKind, isApiError } from '../models/api-error.model';
import { ProblemDetails } from '../models/problem-details.model';

const MESSAGES: Readonly<Record<ApiErrorKind, string>> = {
  network: 'Unable to reach the server. Check your connection and try again.',
  'bad-request': 'The request could not be processed. Review the information and try again.',
  validation: 'Some fields are invalid. Review the highlighted information and try again.',
  unauthorized: 'Your session is not valid. Please sign in again.',
  forbidden: 'You do not have permission to perform this action.',
  'not-found': 'The requested resource was not found.',
  conflict: 'The resource was changed or already exists. Refresh and try again.',
  'rate-limited': 'Too many requests. Wait a moment and try again.',
  unavailable: 'The service is temporarily unavailable. Please try again later.',
  server: 'An unexpected error occurred. Please try again later.',
  unknown: 'An unexpected error occurred. Please try again.',
};

/**
 * Converts HTTP failures into user-safe {@link ApiError} values.
 *
 * Messages are chosen by HTTP status only; backend `title` and `detail` are
 * never shown or parsed. A `500` is a generic ProblemDetails in every
 * environment, including Development: the exception details exist only in
 * backend logs, correlated by the preserved `traceId`.
 */
@Injectable({ providedIn: 'root' })
export class ApiErrorService {
  toApiError(error: unknown): ApiError {
    if (isApiError(error)) {
      return error;
    }

    if (!(error instanceof HttpErrorResponse)) {
      return createApiError('unknown', 0);
    }

    if (error.status === 0) {
      return createApiError('network', 0);
    }

    const problem = readProblemDetails(error.error);
    const fieldErrors = readFieldErrors(problem);
    const kind = resolveKind(error.status, Object.keys(fieldErrors).length > 0);

    return createApiError(
      kind,
      error.status,
      kind === 'validation' ? fieldErrors : {},
      readTraceId(problem),
    );
  }

  getMessage(error: unknown): string {
    return this.toApiError(error).message;
  }
}

function createApiError(
  kind: ApiErrorKind,
  status: number,
  fieldErrors: ApiError['fieldErrors'] = {},
  traceId?: string,
): ApiError {
  return traceId
    ? { kind, status, message: MESSAGES[kind], fieldErrors, traceId }
    : { kind, status, message: MESSAGES[kind], fieldErrors };
}

function resolveKind(status: number, hasFieldErrors: boolean): ApiErrorKind {
  switch (status) {
    case 400:
    case 422:
      return hasFieldErrors ? 'validation' : 'bad-request';
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'not-found';
    case 409:
      return 'conflict';
    case 429:
      return 'rate-limited';
    case 502:
    case 503:
    case 504:
      return 'unavailable';
  }

  if (status >= 500) {
    return 'server';
  }
  return status >= 400 ? 'bad-request' : 'unknown';
}

function readProblemDetails(body: unknown): ProblemDetails | null {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
    ? (body as ProblemDetails)
    : null;
}

function readTraceId(problem: ProblemDetails | null): string | undefined {
  return typeof problem?.traceId === 'string' ? problem.traceId : undefined;
}

function readFieldErrors(problem: ProblemDetails | null): ApiError['fieldErrors'] {
  const errors = problem?.['errors'];
  if (typeof errors !== 'object' || errors === null || Array.isArray(errors)) {
    return {};
  }

  const result: Record<string, readonly string[]> = {};
  for (const [field, messages] of Object.entries(errors)) {
    if (Array.isArray(messages)) {
      const valid = messages.filter((message): message is string => typeof message === 'string');
      if (valid.length > 0) {
        result[field] = valid;
      }
    }
  }
  return result;
}
