import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RedirectCommand, Router, provideRouter } from '@angular/router';
import { firstValueFrom, isObservable, of } from 'rxjs';

import { API_CONFIG } from '../config/api.config';
import { portalGuard } from '../guards/portal.guard';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { portalUnauthorizedInterceptor } from '../interceptors/portal-unauthorized.interceptor';
import { PortalSession } from '../models/portal-session.model';
import { PortalSessionService } from './portal-session.service';

const API = 'http://api.test';

const SESSION: PortalSession = {
  user: { firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  account: {
    contactId: 'c-1',
    customerName: 'Sofia Martinez',
    organizationName: 'Northstar',
    hasLogo: false,
  },
  accounts: [
    { contactId: 'c-1', customerName: 'Sofia Martinez', organizationName: 'Northstar' },
    { contactId: 'c-2', customerName: 'Sofia Martinez', organizationName: 'Acme' },
  ],
};

describe('portal session, guard and unauthorized interceptor', () => {
  let service: PortalSessionService;
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let router: Router;
  let navigated: string[];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(
          withInterceptors([authInterceptor, portalUnauthorizedInterceptor, errorInterceptor]),
        ),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PortalSessionService);
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    navigated = [];
    vi.spyOn(router, 'navigateByUrl').mockImplementation((url) => {
      navigated.push(String(url));
      return Promise.resolve(true);
    });
  });

  afterEach(() => httpTesting.verify());

  const unauthorized = (path: string): void =>
    httpTesting
      .expectOne(`${API}/${path}`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });

  it('a cold 401 stays silent; with a session in memory it ends it, shows the notice once and goes to sign-in', () => {
    // Cold visit: no session, so a 401 neither sets the notice nor navigates.
    http.get(`${API}/portal/dashboard`).subscribe({ error: () => undefined });
    unauthorized('portal/dashboard');
    expect(service.expiredNotice()).toBe(false);
    expect(navigated).toEqual([]);

    // Sign-in lowercases the email, sends credentials and stores the session.
    service
      .signIn({ email: ' Sofia@Example.com ', password: 'secret-password', rememberMe: true })
      .subscribe();
    const signIn = httpTesting.expectOne(`${API}/portal/sessions`);
    expect(signIn.request.withCredentials).toBe(true);
    expect(signIn.request.body).toEqual({
      email: 'sofia@example.com',
      password: 'secret-password',
      rememberMe: true,
    });
    signIn.flush(SESSION);
    expect(service.session()).toEqual(SESSION);

    // Excluded endpoints (a failed sign-in, recovery) never end the session.
    service.signIn({ email: 'a@b.co', password: 'x', rememberMe: false }).subscribe({
      error: () => undefined,
    });
    unauthorized('portal/sessions');
    http.post(`${API}/portal/password-resets`, {}).subscribe({ error: () => undefined });
    unauthorized('portal/password-resets');
    expect(service.session()).not.toBeNull();
    expect(service.expiredNotice()).toBe(false);

    // Any other portal request returning 401 ends the session and returns to sign-in.
    http.get(`${API}/portal/dashboard`).subscribe({ error: () => undefined });
    unauthorized('portal/dashboard');
    expect(service.session()).toBeNull();
    expect(service.expiredNotice()).toBe(true);
    expect(navigated).toEqual(['/portal/sign-in']);

    // The sign-in page reads the notice once and clears it.
    service.clearExpiredNotice();
    expect(service.expiredNotice()).toBe(false);
  });

  it('switches account and signs out on 204 or 401, keeping the session on other failures', () => {
    service.setSession(SESSION);
    service.switchAccount('c-2').subscribe();
    const switched = httpTesting.expectOne(`${API}/portal/sessions/current/account`);
    expect(switched.request.body).toEqual({ contactId: 'c-2' });
    switched.flush({ ...SESSION, account: { ...SESSION.account, contactId: 'c-2' } });
    expect(service.session()?.account.contactId).toBe('c-2');

    service.signOut().subscribe({ error: () => undefined });
    httpTesting
      .expectOne(`${API}/portal/sessions/current`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    expect(service.session()).not.toBeNull();

    service.signOut().subscribe();
    unauthorized('portal/sessions/current');
    expect(service.session()).toBeNull();
    expect(service.expiredNotice()).toBe(false);
  });

  it('the guard allows a valid session and otherwise redirects to the portal sign-in', async () => {
    const run = (): Promise<unknown> => {
      const result = TestBed.runInInjectionContext(() => portalGuard({} as never, {} as never));
      return firstValueFrom(isObservable(result) ? result : of(result));
    };

    const allowed = run();
    httpTesting.expectOne(`${API}/portal/sessions/current`).flush(SESSION);
    expect(await allowed).toBe(true);

    // A session was in memory and its revalidation fails: redirect plus the notice.
    const redirected = run();
    unauthorized('portal/sessions/current');
    const result = await redirected;
    expect(result).toBeInstanceOf(RedirectCommand);
    expect(router.serializeUrl((result as RedirectCommand).redirectTo)).toBe('/portal/sign-in');
    expect(service.session()).toBeNull();
    expect(service.expiredNotice()).toBe(true);
  });
});
