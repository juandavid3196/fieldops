import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { ApiError } from '../models/api-error.model';
import { ApiErrorService } from './api-error.service';

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error, url: 'http://api.test/resource' });
}

describe('ApiErrorService', () => {
  let service: ApiErrorService;

  beforeEach(() => {
    service = TestBed.inject(ApiErrorService);
  });

  it('maps a status 0 response to a network error', () => {
    const result = service.toApiError(httpError(0));

    expect(result.kind).toBe('network');
    expect(result.status).toBe(0);
    expect(result.message).toContain('Unable to reach the server');
  });

  it('maps a 500 ProblemDetails without exposing backend detail or title', () => {
    const result = service.toApiError(
      httpError(500, {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
        title: 'An error occurred while processing your request.',
        status: 500,
        detail: 'NpgsqlException: connection refused at Host=db;Password=secret',
        traceId: '00-abc-def-01',
      }),
    );

    expect(result.kind).toBe('server');
    expect(result.traceId).toBe('00-abc-def-01');
    expect(result.message).not.toContain('Npgsql');
    expect(JSON.stringify(result)).not.toContain('Password');
  });

  it('maps a ValidationProblemDetails to validation field errors', () => {
    const result = service.toApiError(
      httpError(400, {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { Name: ['Name is required.'], Email: ['Email is invalid.', 42] },
      }),
    );

    expect(result.kind).toBe('validation');
    expect(result.fieldErrors).toEqual({
      Name: ['Name is required.'],
      Email: ['Email is invalid.'],
    });
  });

  it('maps a 400 without field errors to bad-request', () => {
    const result = service.toApiError(httpError(400, { title: 'Bad Request', status: 400 }));

    expect(result.kind).toBe('bad-request');
    expect(result.fieldErrors).toEqual({});
  });

  it.each([
    [401, 'unauthorized'],
    [403, 'forbidden'],
    [404, 'not-found'],
    [409, 'conflict'],
    [415, 'bad-request'],
    [429, 'rate-limited'],
    [501, 'server'],
    [503, 'unavailable'],
  ] as const)('maps status %i to %s', (status, kind) => {
    expect(service.toApiError(httpError(status)).kind).toBe(kind);
  });

  it('ignores non-ProblemDetails bodies', () => {
    const result = service.toApiError(httpError(500, '<html>Proxy error</html>'));

    expect(result.kind).toBe('server');
    expect(result.traceId).toBeUndefined();
    expect(result.fieldErrors).toEqual({});
  });

  it('maps unknown errors to a generic message', () => {
    const result = service.toApiError(new Error('TypeError: x is undefined'));

    expect(result.kind).toBe('unknown');
    expect(result.message).not.toContain('TypeError');
  });

  it('returns an existing ApiError unchanged', () => {
    const apiError: ApiError = { kind: 'forbidden', status: 403, message: 'm', fieldErrors: {} };

    expect(service.toApiError(apiError)).toBe(apiError);
  });

  it('provides the user-friendly message directly', () => {
    expect(service.getMessage(httpError(403))).toBe(
      'You do not have permission to perform this action.',
    );
  });
});
