import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../core/config/api.config';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { MessageUnreadService } from '../../core/services/message-unread.service';
import { SessionService } from '../../core/services/session.service';
import { technicianMessagesSoonRedirect } from '../../features/messages/messages.routes';
import {
  TODAY_BACK,
  scheduleSoonRedirect,
  timeSoonRedirect,
  technicianComingSoonRoute,
} from '../../features/technician-jobs/technician-jobs.routes';
import { TechnicianVisitsService } from '../../features/technician-jobs/services/technician-visits.service';
import { SIGN_OUT_ERROR_MESSAGE, TechnicianShell } from './technician-shell';

const API = 'http://api.test';
const SESSION_URL = `${API}/sessions/current`;
const SUMMARY_URL = `${API}/messages/summary`;

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
              { path: 'today/schedule', component: PageStub, data: { shellTitle: 'Schedule' } },
              { path: 'today/time', component: PageStub, data: { shellTitle: 'Time' } },
              { path: 'today/messages', component: PageStub, data: { shellTitle: 'Messages' } },
              {
                path: 'today/messages/:conversationId',
                component: PageStub,
                data: { shellTitle: 'Messages' },
              },
              scheduleSoonRedirect,
              timeSoonRedirect,
              technicianMessagesSoonRedirect,
              technicianComingSoonRoute,
            ],
          },
          { path: 'auth/sign-in', component: PageStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    TestBed.inject(SessionService).setSession({
      user: {
        id: 'u-1',
        firstName: 'Carlos',
        lastName: 'Rivera',
        email: 'c@example.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: 'technician', name: 'Technician' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today');
    // technician-messages BR-14: the shell loads the unread summary once and then polls.
    httpTesting
      .expectOne({ method: 'GET', url: SUMMARY_URL })
      .flush({ unreadMessages: 0, unreadConversations: 0 });
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

  it('marks the current destination, titles the top bar, redirects the old Schedule route and opens Coming soon inside the shell (AC-16, AC-18)', async () => {
    await setup();

    expect(navLabels()).toEqual(['Today', 'Schedule', 'Time', 'Messages', 'More']);
    expect(current()).toEqual(['Today']);
    expect(title()).toBe("Today's jobs");
    // Technicians have no notifications: there is no bell.
    expect(host.querySelector('a[aria-label^="Notifications"]')).toBeNull();
    // help-support-center FR-15 / AC-21: the top-bar help icon opens the help center in the shell.
    expect(host.querySelector('a[aria-label="Help"]')?.getAttribute('href')).toBe('/today/help');
    expect(host.querySelector('a[aria-label="Help"] .pi-question-circle')).not.toBeNull();
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

    // The job page's step progress replaces the bottom nav's current-item line.
    const progress = () => host.querySelector('[role="progressbar"][aria-label="Job progress"]');
    expect(progress()).toBeNull();
    visits.setJobProgress(75);
    harness.fixture.detectChanges();
    expect(progress()?.getAttribute('aria-valuenow')).toBe('75');
    visits.setJobProgress(null);
    harness.fixture.detectChanges();
    expect(progress()).toBeNull();

    // Schedule (AC-18): own destination; Today is not current there; the old Coming soon route redirects.
    expect(host.querySelector('nav a[href="/today/schedule"]')).not.toBeNull();
    await go('/today/schedule');
    expect(current()).toEqual(['Schedule']);
    expect(title()).toBe('Schedule');
    await go('/today/soon/schedule');
    expect(router.url).toBe('/today/schedule');
    expect(current()).toEqual(['Schedule']);

    // Time (AC-10): own destination; the old Coming soon route redirects.
    expect(host.querySelector('nav a[href="/today/time"]')).not.toBeNull();
    await go('/today/soon/time');
    expect(router.url).toBe('/today/time');
    expect(current()).toEqual(['Time']);
    expect(title()).toBe('Time');
    expect(host.querySelector('nav a[aria-current="page"]')?.textContent).toContain('Time');

    // technician-messages AC-15: Messages is a real destination, current on conversation pages too;
    // the old Coming soon route redirects; the tab shows the unread badge ("99+" cap).
    await go('/today/soon/messages');
    expect(router.url).toBe('/today/messages');
    expect(current()).toEqual(['Messages']);
    expect(title()).toBe('Messages');
    await go('/today/messages/c-1');
    expect(current()).toEqual(['Messages']);
    expect(title()).toBe('Messages');
    const unread = TestBed.inject(MessageUnreadService);
    unread.set({ unreadMessages: 120, unreadConversations: 2 });
    harness.fixture.detectChanges();
    const tab = host.querySelector('nav a[href="/today/messages"]')!;
    expect(tab.querySelector('.tshell__badge')?.textContent?.trim()).toBe('99+');
    expect(tab.textContent).toContain('120 unread messages');
    unread.set({ unreadMessages: 0, unreadConversations: 0 });
    harness.fixture.detectChanges();
    expect(host.querySelector('.tshell__badge')).toBeNull();

    await go('/today/soon/notifications');
    expect(title()).toBe('Notifications');
    expect(current()).toEqual([]);

    await go('/today');
    expect(host.querySelector('.tshell__logo')).not.toBeNull();
    expect(host.querySelector('a[aria-label="Back to Today\'s jobs"]')).toBeNull();

    await go('/today/soon/unknown');
    expect(router.url).toBe('/today');
  });

  it('opens More with My profile, Account settings, Help & support and Sign out in order (FR-04, AC-02; help-support-center FR-02, AC-03), and signs out through the existing flow, keeping the session on failure (AC-16)', async () => {
    await setup();

    button('More').click();
    await settle();
    expect(button('More').getAttribute('aria-expanded')).toBe('true');
    const panelItems = Array.from(host.querySelectorAll('#tshell-more-panel .tshell__panel-item'));
    expect(panelItems.map((item) => item.textContent?.trim())).toEqual([
      'My profile',
      'Account settings',
      'Help & support',
      'Sign out',
    ]);
    // FR-03: the Language item lives in the More panel.
    expect(host.querySelector('#tshell-more-panel app-language-menu-item')).not.toBeNull();
    // Every destination stays inside the technician shell.
    expect(panelItems[0].getAttribute('href')).toBe('/today/profile');
    expect(panelItems[1].getAttribute('href')).toBe('/today/account');
    expect(panelItems[2].getAttribute('href')).toBe('/today/help');

    button('Sign out').click();
    button('Sign out').click();
    await settle();
    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(host.querySelector('p-message')?.textContent?.trim()).toBe(SIGN_OUT_ERROR_MESSAGE());
    expect(TestBed.inject(SessionService).session()).not.toBeNull();
    expect(router.url).toBe('/today');

    // The avatar is identity only; More is the only menu with Sign out.
    expect(host.querySelector('.tshell__avatar')?.hasAttribute('href')).toBe(false);
    button('More').click();
    await settle();
    button('Sign out').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: SESSION_URL })
      .flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    expect(router.url).toBe('/auth/sign-in');
    expect(TestBed.inject(SessionService).session()).toBeNull();
  });
});
