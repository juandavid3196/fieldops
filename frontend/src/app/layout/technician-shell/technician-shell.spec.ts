import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../core/config/api.config';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { SessionService } from '../../core/services/session.service';
import {
  TODAY_BACK,
  technicianComingSoonRoute,
} from '../../features/technician-jobs/technician-jobs.routes';
import { TechnicianVisitsService } from '../../features/technician-jobs/services/technician-visits.service';
import { SIGN_OUT_ERROR_MESSAGE, TechnicianShell } from './technician-shell';

const API = 'http://api.test';
const SESSION_URL = `${API}/sessions/current`;

@Component({ template: '<h1>Page</h1>' })
class PageStub {}

describe('TechnicianShell', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;
  let host: HTMLElement;

  async function setup(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: '',
            component: TechnicianShell,
            children: [
              { path: 'today', component: PageStub, data: { shellTitle: "Today's jobs" } },
              {
                path: 'today/visits/:visitId',
                component: PageStub,
                data: { shellTitle: 'Job details', shellBack: TODAY_BACK },
              },
              technicianComingSoonRoute,
            ],
          },
          { path: 'team', component: PageStub },
          { path: 'auth/sign-in', component: PageStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    TestBed.inject(SessionService).setSession({
      user: { id: 'u-1', firstName: 'Carlos', lastName: 'Rivera', email: 'c@example.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: 'technician', name: 'Technician' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today');
    host = harness.routeNativeElement as HTMLElement;
  }

  const settle = async () => {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  };
  const go = async (url: string) => {
    await router.navigateByUrl(url);
    await settle();
  };
  const navLabels = () =>
    Array.from(host.querySelectorAll('nav[aria-label="Primary"] > *'), (n) =>
      n.textContent?.trim(),
    );
  const current = () =>
    Array.from(host.querySelectorAll('[aria-current="page"]'), (n) => n.textContent?.trim());
  const title = () => host.querySelector('.tshell__title')?.textContent?.trim();
  const button = (label: string) =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.includes(label),
    )!;

  afterEach(() => httpTesting.verify());

  it('marks the current destination, titles the top bar and opens Coming soon inside the shell (AC-16)', async () => {
    await setup();

    expect(navLabels()).toEqual(['Today', 'Schedule', 'Time', 'Messages', 'More']);
    expect(current()).toEqual(['Today']);
    expect(title()).toBe("Today's jobs");
    expect(host.querySelector('a[aria-label="Notifications"]')?.getAttribute('href')).toBe(
      '/today/soon/notifications',
    );
    expect(host.querySelector('.tshell__avatar')?.textContent?.trim()).toBe('CR');
    expect(host.querySelectorAll('main')).toHaveLength(1);
    expect(host.querySelector('.p-badge, [class*="unread"]')).toBeNull();

    await go('/today/visits/v-1');
    expect(current()).toEqual(['Today']);
    expect(title()).toBe('Job details');
    const back = host.querySelector('a[aria-label="Back to Today\'s jobs"]');
    expect(back?.getAttribute('href')).toBe('/today');
    expect(host.querySelector('.tshell__logo')).toBeNull();

    // The job page titles the bar "Job in progress" while Design 9 shows (BR-20), without navigation.
    const visits = TestBed.inject(TechnicianVisitsService);
    visits.setPageTitle('Job in progress');
    harness.fixture.detectChanges();
    expect(title()).toBe('Job in progress');
    visits.setPageTitle(null);
    harness.fixture.detectChanges();
    expect(title()).toBe('Job details');

    for (const name of ['Schedule', 'Time', 'Messages']) {
      await go(`/today/soon/${name.toLowerCase()}`);
      expect(current()).toEqual([name]);
      expect(title()).toBe(name);
      expect(host.querySelector('h1')?.textContent?.trim()).toBe(name);
      expect(host.querySelector('app-coming-soon p')?.textContent?.trim()).toBe(
        `${name} isn't available yet.`,
      );
      const back = host.querySelector('app-coming-soon a')!;
      expect(back.textContent?.trim()).toBe("Back to Today's jobs");
      expect(back.getAttribute('href')).toBe('/today');
    }

    await go('/today/soon/notifications');
    expect(title()).toBe('Notifications');
    expect(current()).toEqual([]);

    await go('/today');
    expect(host.querySelector('.tshell__logo')).not.toBeNull();
    expect(host.querySelector('a[aria-label="Back to Today\'s jobs"]')).toBeNull();

    await go('/today/soon/unknown');
    expect(router.url).toBe('/today');
  });

  it('opens More with My profile and Sign out, and signs out through the existing flow, keeping the session on failure (AC-16)', async () => {
    await setup();

    button('More').click();
    await settle();
    expect(button('More').getAttribute('aria-expanded')).toBe('true');
    expect(host.querySelector('#tshell-more-panel a')?.getAttribute('href')).toBe('/team');

    button('Sign out').click();
    button('Sign out').click();
    await settle();
    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(host.querySelector('p-message')?.textContent?.trim()).toBe(SIGN_OUT_ERROR_MESSAGE);
    expect(TestBed.inject(SessionService).session()).not.toBeNull();
    expect(router.url).toBe('/today');

    host.querySelector<HTMLButtonElement>('.tshell__avatar')!.click();
    await settle();
    expect(host.querySelector('#tshell-account-panel')?.textContent).toContain('Carlos Rivera');
    button('Sign out').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    expect(router.url).toBe('/auth/sign-in');
    expect(TestBed.inject(SessionService).session()).toBeNull();
  });
});
