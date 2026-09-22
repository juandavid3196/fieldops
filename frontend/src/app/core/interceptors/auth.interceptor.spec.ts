import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('does not attach credentials while authentication is not implemented', () => {
    http.get('http://api.test/work-orders').subscribe();

    const testRequest = httpTesting.expectOne('http://api.test/work-orders');
    expect(testRequest.request.headers.has('Authorization')).toBe(false);
    expect(testRequest.request.withCredentials).toBe(false);
    testRequest.flush({});
  });
});
