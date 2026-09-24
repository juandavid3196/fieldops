import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { authInterceptor } from './auth.interceptor';

const API_BASE_URL = 'http://api.test';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('sends credentials with API requests (AC-46)', () => {
    http.get(`${API_BASE_URL}/sessions/current`).subscribe();

    const testRequest = httpTesting.expectOne(`${API_BASE_URL}/sessions/current`);
    expect(testRequest.request.withCredentials).toBe(true);
    expect(testRequest.request.headers.has('Authorization')).toBe(false);
    testRequest.flush({});
  });

  it('does not send credentials with non-API requests (AC-46)', () => {
    http.get('https://third-party.test/resource').subscribe();

    const testRequest = httpTesting.expectOne('https://third-party.test/resource');
    expect(testRequest.request.withCredentials).toBe(false);
    expect(testRequest.request.headers.has('Authorization')).toBe(false);
    testRequest.flush({});
  });

  it('does not treat URLs that merely share the base prefix as API URLs', () => {
    http.get(`${API_BASE_URL}.evil.test/resource`).subscribe();

    const testRequest = httpTesting.expectOne(`${API_BASE_URL}.evil.test/resource`);
    expect(testRequest.request.withCredentials).toBe(false);
    testRequest.flush({});
  });
});
