import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { ApiError, isApiError } from '../models/api-error.model';
import { errorInterceptor } from './error.interceptor';

const API_BASE_URL = 'http://api.test';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  function captureError(url: string): { value?: unknown } {
    const captured: { value?: unknown } = {};
    http.get(url).subscribe({ error: (error: unknown) => (captured.value = error) });
    return captured;
  }

  it('converts API ProblemDetails failures into ApiError', () => {
    const captured = captureError(`${API_BASE_URL}/work-orders`);

    httpTesting
      .expectOne(`${API_BASE_URL}/work-orders`)
      .flush(
        { title: 'Not Found', status: 404, traceId: 'trace-1' },
        { status: 404, statusText: 'Not Found' },
      );

    expect(isApiError(captured.value)).toBe(true);
    const error = captured.value as ApiError;
    expect(error.kind).toBe('not-found');
    expect(error.status).toBe(404);
    expect(error.traceId).toBe('trace-1');
  });

  it('converts network failures into ApiError', () => {
    const captured = captureError(`${API_BASE_URL}/health`);

    httpTesting.expectOne(`${API_BASE_URL}/health`).error(new ProgressEvent('error'));

    expect((captured.value as ApiError).kind).toBe('network');
  });

  it('passes successful responses through unchanged', () => {
    let body: unknown;
    http.get(`${API_BASE_URL}/health`).subscribe((response) => (body = response));

    httpTesting.expectOne(`${API_BASE_URL}/health`).flush({ status: 'Healthy' });

    expect(body).toEqual({ status: 'Healthy' });
  });

  it('does not transform errors from non-API URLs', () => {
    const captured = captureError('https://third-party.test/resource');

    httpTesting
      .expectOne('https://third-party.test/resource')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(captured.value).toBeInstanceOf(HttpErrorResponse);
  });

  it('does not treat URLs that merely share the base prefix as API URLs', () => {
    const captured = captureError(`${API_BASE_URL}.evil.test/resource`);

    httpTesting
      .expectOne(`${API_BASE_URL}.evil.test/resource`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(captured.value).toBeInstanceOf(HttpErrorResponse);
  });
});
