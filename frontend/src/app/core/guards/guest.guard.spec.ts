import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  GuardResult,
  MaybeAsync,
  RedirectCommand,
  Router,
  RouterStateSnapshot,
  provideRouter,
} from '@angular/router';
import { Observable } from 'rxjs';

import { API_CONFIG } from '../config/api.config';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { Session } from '../models/session.model';
import { guestGuard } from './guest.guard';

const API_BASE_URL = 'http://api.test';
const SESSION_URL = `${API_BASE_URL}/sessions/current`;

const SESSION: Session = {
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'owner', name: 'Owner' },
};

describe('guestGuard', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  function runGuard(): { value?: GuardResult } {
    const captured: { value?: GuardResult } = {};
    const result = TestBed.runInInjectionContext(() =>
      guestGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    ) as MaybeAsync<GuardResult>;
    (result as Observable<GuardResult>).subscribe((value) => (captured.value = value));
    return captured;
  }

  it('redirects to Overview, replacing history, when the session request returns 200 (AC-03)', () => {
    const result = runGuard();

    httpTesting.expectOne(SESSION_URL).flush(SESSION);

    expect(result.value).toBeInstanceOf(RedirectCommand);
    const redirect = result.value as RedirectCommand;
    expect(TestBed.inject(Router).serializeUrl(redirect.redirectTo)).toBe('/overview');
    expect(redirect.navigationBehaviorOptions?.replaceUrl).toBe(true);
  });

  it.each([
    [401, 'Unauthorized'],
    [500, 'Server Error'],
  ])('lets the visitor open the page on %i', (status, statusText) => {
    const result = runGuard();

    httpTesting.expectOne(SESSION_URL).flush(null, { status, statusText });

    expect(result.value).toBe(true);
  });

  it('lets the visitor open the page on a network error', () => {
    const result = runGuard();

    httpTesting.expectOne(SESSION_URL).error(new ProgressEvent('error'));

    expect(result.value).toBe(true);
  });
});
