import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { ApiError } from '../models/api-error.model';
import { Session } from '../models/session.model';
import { SessionService } from './session.service';

const API_BASE_URL = 'http://api.test';

const SESSION: Session = {
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'owner', name: 'Owner' },
};

describe('SessionService', () => {
  let service: SessionService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(SessionService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  function loadSession(): void {
    service.loadCurrent().subscribe();
    httpTesting.expectOne(`${API_BASE_URL}/sessions/current`).flush(SESSION);
  }

  it('starts without a session', () => {
    expect(service.session()).toBeNull();
  });

  describe('loadCurrent', () => {
    it('GETs the current session with credentials and stores it', () => {
      let result: Session | undefined;
      service.loadCurrent().subscribe((session) => (result = session));

      const request = httpTesting.expectOne(`${API_BASE_URL}/sessions/current`);
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBe(true);
      request.flush(SESSION);

      expect(result).toEqual(SESSION);
      expect(service.session()).toEqual(SESSION);
    });

    it('clears the session and rethrows an ApiError on 401', () => {
      loadSession();
      let error: ApiError | undefined;
      service.loadCurrent().subscribe({ error: (e: ApiError) => (error = e) });

      httpTesting
        .expectOne(`${API_BASE_URL}/sessions/current`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });

      expect(error?.kind).toBe('unauthorized');
      expect(service.session()).toBeNull();
    });
  });

  describe('signIn', () => {
    it('POSTs the normalized email, the untrimmed password and rememberMe', () => {
      service
        .signIn({ email: '  User@Example.COM ', password: ' secret ', rememberMe: true })
        .subscribe();

      const request = httpTesting.expectOne(`${API_BASE_URL}/sessions`);
      expect(request.request.method).toBe('POST');
      expect(request.request.withCredentials).toBe(true);
      expect(request.request.body).toEqual({
        email: 'user@example.com',
        password: ' secret ',
        rememberMe: true,
      });
      request.flush(SESSION);

      expect(service.session()).toEqual(SESSION);
    });

    it('sends no organization or other identifier', () => {
      service.signIn({ email: 'a@b.co', password: 'x', rememberMe: false }).subscribe();

      const request = httpTesting.expectOne(`${API_BASE_URL}/sessions`);
      expect(Object.keys(request.request.body as object).sort()).toEqual([
        'email',
        'password',
        'rememberMe',
      ]);
      request.flush(SESSION);
    });

    it('keeps no session when sign-in fails', () => {
      let error: ApiError | undefined;
      service
        .signIn({ email: 'a@b.co', password: 'x', rememberMe: false })
        .subscribe({ error: (e: ApiError) => (error = e) });

      httpTesting
        .expectOne(`${API_BASE_URL}/sessions`)
        .flush(null, { status: 401, statusText: 'Unauthorized' });

      expect(error?.kind).toBe('unauthorized');
      expect(service.session()).toBeNull();
    });
  });

  describe('signOut', () => {
    it('DELETEs the current session and clears it on 204', () => {
      loadSession();
      let completed = false;
      service.signOut().subscribe({ complete: () => (completed = true) });

      const request = httpTesting.expectOne(`${API_BASE_URL}/sessions/current`);
      expect(request.request.method).toBe('DELETE');
      expect(request.request.withCredentials).toBe(true);
      request.flush(null, { status: 204, statusText: 'No Content' });

      expect(completed).toBe(true);
      expect(service.session()).toBeNull();
    });

    it('keeps the session when sign-out fails', () => {
      loadSession();
      let error: ApiError | undefined;
      service.signOut().subscribe({ error: (e: ApiError) => (error = e) });

      httpTesting
        .expectOne(`${API_BASE_URL}/sessions/current`)
        .flush(null, { status: 500, statusText: 'Server Error' });

      expect(error?.kind).toBe('server');
      expect(service.session()).toEqual(SESSION);
    });
  });
});
