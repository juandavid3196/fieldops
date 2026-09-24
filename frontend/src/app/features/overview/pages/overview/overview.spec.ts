import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import axe from 'axe-core';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { Session } from '../../../../core/models/session.model';
import { SessionService } from '../../../../core/services/session.service';
import { Overview } from './overview';

const API_BASE_URL = 'http://api.test';
const SESSION_URL = `${API_BASE_URL}/sessions/current`;

const SESSION: Session = {
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'owner', name: 'Owner' },
};

@Component({ template: '<p>Sign in stub</p>' })
class SignInStub {}

describe('Overview', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let sessionService: SessionService;
  let host: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter([
          { path: 'overview', component: Overview },
          { path: 'auth/sign-in', component: SignInStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    sessionService = TestBed.inject(SessionService);

    // Seed the session as authGuard would.
    sessionService.loadCurrent().subscribe();
    httpTesting.expectOne({ method: 'GET', url: SESSION_URL }).flush(SESSION);

    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/overview', Overview);
    await harness.fixture.whenStable();
    host = harness.routeNativeElement as HTMLElement;
  });

  afterEach(() => httpTesting.verify());

  const signOutButton = () =>
    host.querySelector<HTMLButtonElement>('.overview__sign-out') as HTMLButtonElement;

  async function clickSignOut(): Promise<void> {
    signOutButton().click();
    await harness.fixture.whenStable();
  }

  it('shows the welcome heading, organization, role and Sign out (AC-48)', () => {
    expect(host.querySelectorAll('h1')).toHaveLength(1);
    expect(host.querySelector('h1')?.textContent?.trim()).toBe('Welcome, Sofia');

    const terms = Array.from(host.querySelectorAll('dl dt'), (node) => node.textContent?.trim());
    const values = Array.from(host.querySelectorAll('dl dd'), (node) => node.textContent?.trim());
    expect(terms).toEqual(['Organization', 'Role']);
    expect(values).toEqual(['Acme Services', 'Owner']);

    expect(signOutButton().textContent?.trim()).toBe('Sign out');
    expect(host.querySelector('p-message')).toBeNull();
  });

  it('clears the session and navigates to Sign In on 204 (AC-49)', async () => {
    await clickSignOut();

    const request = httpTesting.expectOne({ method: 'DELETE', url: SESSION_URL });
    expect(signOutButton().disabled).toBe(true);
    expect(signOutButton().querySelector('svg[data-p-icon="spinner"]')).not.toBeNull();

    request.flush(null, { status: 204, statusText: 'No Content' });
    await harness.fixture.whenStable();

    expect(sessionService.session()).toBeNull();
    expect(router.url).toBe('/auth/sign-in');
  });

  it('shows the error, stays and re-enables the button when sign-out fails (AC-50)', async () => {
    await clickSignOut();

    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 500, statusText: 'Server Error' });
    await harness.fixture.whenStable();

    const message = host.querySelector('p-message');
    expect(message?.textContent?.trim()).toBe("We couldn't sign you out. Try again.");
    expect(message?.getAttribute('role')).toBe('alert');
    expect(router.url).toBe('/overview');
    expect(sessionService.session()).toEqual(SESSION);
    expect(signOutButton().disabled).toBe(false);
    expect(document.activeElement).toBe(signOutButton());
  });

  it('ignores repeat clicks while signing out', async () => {
    await clickSignOut();
    await clickSignOut();
    const component = harness.routeDebugElement?.componentInstance as Overview;
    component.signOut();

    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 204, statusText: 'No Content' });
    await harness.fixture.whenStable();
  });

  describe('accessibility (AC-56, axe in jsdom)', () => {
    // jsdom has no layout, so `color-contrast` is verified in the browser during the final audit.
    const options: axe.RunOptions = { rules: { 'color-contrast': { enabled: false } } };

    it('default state', async () => {
      const results = await axe.run(harness.fixture.nativeElement as HTMLElement, options);
      expect(results.passes.length).toBeGreaterThan(0);
      expect(results.violations.map((violation) => violation.id)).toEqual([]);
    });

    it('sign-out error state', async () => {
      await clickSignOut();
      httpTesting
        .expectOne({ method: 'DELETE', url: SESSION_URL })
        .flush(null, { status: 500, statusText: 'Server Error' });
      await harness.fixture.whenStable();

      const results = await axe.run(harness.fixture.nativeElement as HTMLElement, options);
      expect(results.passes.length).toBeGreaterThan(0);
      expect(results.violations.map((violation) => violation.id)).toEqual([]);
    });
  });
});
