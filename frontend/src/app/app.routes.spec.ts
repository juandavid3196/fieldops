import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from './app.routes';
import { API_CONFIG } from './core/config/api.config';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { Session } from './core/models/session.model';

const API_BASE_URL = 'http://api.test';
const SESSION_URL = `${API_BASE_URL}/sessions/current`;

const SESSION: Session = {
  user: {
    id: 'u-1',
    firstName: 'Sofia',
    lastName: 'Martinez',
    email: 'sofia@example.com',
    photoUpdatedAt: null,
  },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'viewer', name: 'Viewer' },
};

describe('app routes', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API_BASE_URL } },
        provideRouter(routes),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    // The shell loads the bell count (notifications-center BR-16); it is irrelevant to these routes.
    httpTesting
      .match({ method: 'GET', url: `${API_BASE_URL}/notifications/summary` })
      .forEach((request) => request.flush({ unread: 0, today: 0 }));
    // technician-messages: the shells poll the Messages summary and the list loads its first page.
    httpTesting
      .match({ method: 'GET', url: `${API_BASE_URL}/messages/summary` })
      .forEach((request) => request.flush({ unreadMessages: 0, unreadConversations: 0 }));
    httpTesting
      .match((request) => request.url === `${API_BASE_URL}/messages/conversations`)
      .forEach((request) => request.flush({ items: [], nextCursor: null }));
    httpTesting.verify();
  });

  /** Answers the next `GET /sessions/current` once the lazy route has issued it. */
  async function answerSessionRequest(ok: boolean | Session): Promise<void> {
    const request = await vi.waitFor(() =>
      httpTesting.expectOne({ method: 'GET', url: SESSION_URL }),
    );
    if (ok) {
      request.flush(ok === true ? SESSION : ok);
    } else {
      request.flush(null, { status: 401, statusText: 'Unauthorized' });
    }
  }

  async function navigate(
    url: string,
    sessionResponses: readonly (boolean | Session)[],
  ): Promise<void> {
    const navigation = harness.navigateByUrl(url);
    for (const ok of sessionResponses) {
      await answerSessionRequest(ok);
    }
    await navigation;
    await harness.fixture.whenStable();
  }

  it.each(['/', '/unknown-path'])(
    'sends a visitor without a session from %s to Sign In (AC-02)',
    async (url) => {
      await navigate(url, [false]);

      expect(router.url).toBe('/auth/sign-in');
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
        'Welcome back',
      );
    },
  );

  it('sends a visitor with a valid session from Sign In to Overview (AC-03)', async () => {
    await navigate('/auth/sign-in', [true, true]);

    expect(router.url).toBe('/overview');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Welcome, Sofia',
    );
  });

  it('sends a visitor without a session from Overview to Sign In (AC-47)', async () => {
    await navigate('/overview', [false, false]);

    expect(router.url).toBe('/auth/sign-in');
    expect(harness.fixture.nativeElement.querySelector('app-shell')).toBeNull();
  });

  it('renders Sign In for a visitor without a session (AC-01)', async () => {
    await navigate('/auth/sign-in', [false]);

    expect(router.url).toBe('/auth/sign-in');
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
  });

  it('wraps only Overview and Company settings in the shell and revalidates the session on each navigation (FR-01, AC-04, AC-10)', async () => {
    await navigate('/overview', [true]);

    expect(harness.routeNativeElement?.tagName).toBe('APP-SHELL');
    const links = () =>
      Array.from(
        harness.routeNativeElement?.querySelectorAll('nav[aria-label="Main navigation"] a') ?? [],
        (link) => link.textContent?.trim(),
      );
    expect(links()).toContain('Administration');
    expect(links()).toContain('Requests');

    // Same shell, one new GET /sessions/current: the role changed to a non-admin one.
    const navigation = harness.navigateByUrl('/admin/company');
    await answerSessionRequest({ ...SESSION, role: { code: 'technician', name: 'Technician' } });
    for (const url of ['organization-settings', 'branches']) {
      const request = await vi.waitFor(() => httpTesting.expectOne(`${API_BASE_URL}/${url}`), {
        timeout: 5000,
      });
      request.flush(null, { status: 500, statusText: 'Server Error' });
    }
    await navigation;
    await harness.fixture.whenStable();

    expect(router.url).toBe('/admin/company');
    expect(harness.routeNativeElement?.tagName).toBe('APP-SHELL');
    expect(links()).not.toContain('Administration');
    // The sidebar offers only the pages the new role can open.
    expect(links()).not.toContain('Requests');
    expect(links()).toContain('Team');
  });

  it.each([
    ['quotes', 'Quotes'],
    ['notifications', 'Notifications'],
    ['business-hours', 'Business hours'],
    ['tax-rates', 'Tax rates'],
  ])(
    'renders the shared Coming soon page for /coming-soon/%s inside the shell with no API request (FR-03, AC-03)',
    async (slug, name) => {
      await navigate(`/coming-soon/${slug}`, [true]);

      const root = harness.routeNativeElement as HTMLElement;
      expect(root.tagName).toBe('APP-SHELL');
      expect(root.querySelector('h1')?.textContent?.trim()).toBe(name);
      expect(root.querySelector('app-coming-soon p')?.textContent?.trim()).toBe(
        `${name} isn't available yet.`,
      );
      expect(root.querySelector('app-coming-soon a')?.getAttribute('href')).toBe('/overview');
    },
  );

  it('redirects the retired /coming-soon/reports to the Reports workspace (reports-workspace BR-23, AC-23)', async () => {
    await navigate('/coming-soon/reports', [true]);

    expect(router.url).toBe('/reports');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe('Reports');
  });

  it.each([
    ['/coming-soon/help', 'viewer'],
    ['/help', 'technician'],
  ])(
    'opens the Help center inside the main shell from %s for a %s (help-support-center FR-01, AC-02, AC-03)',
    async (url, code) => {
      await navigate(url, [{ ...SESSION, role: { code, name: 'Role' } }]);

      expect(router.url).toBe('/help');
      expect(harness.routeNativeElement?.tagName).toBe('APP-SHELL');
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe(
        'Help & support',
      );
    },
  );

  it('redirects an unknown Coming soon slug to Overview (FR-03, AC-03)', async () => {
    await navigate('/coming-soon/unknown-module', [true, true]);

    expect(router.url).toBe('/overview');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Welcome');
  });

  it('mounts the technician Coming soon page in the technician shell, not the desktop shell (FR-07, AC-16)', async () => {
    await navigate('/today/soon/notifications', [
      { ...SESSION, role: { code: 'technician', name: 'Technician' } },
    ]);

    const root = harness.routeNativeElement as HTMLElement;
    expect(root.tagName).toBe('APP-TECHNICIAN-SHELL');
    expect(root.querySelector('h1')?.textContent?.trim()).toBe('Notifications');
    expect(root.querySelector('app-coming-soon a')?.getAttribute('href')).toBe('/today');
    expect(root.querySelector('app-shell')).toBeNull();
  });

  it('redirects the retired /today/soon/messages to the technician Messages page (technician-messages AC-15)', async () => {
    await navigate('/today/soon/messages', [
      { ...SESSION, role: { code: 'technician', name: 'Technician' } },
    ]);

    const root = harness.routeNativeElement as HTMLElement;
    expect(router.url).toBe('/today/messages');
    expect(root.tagName).toBe('APP-TECHNICIAN-SHELL');
    expect(root.querySelector('app-messages-host h1')?.textContent?.trim()).toBe('Messages');
    expect(root.querySelector('app-coming-soon')).toBeNull();
  });

  it.each([
    ['dispatcher', 'Select a conversation'],
    ['viewer', "You don't have access to Messages."],
  ])(
    'opens staff /messages in the desktop shell for %s (technician-messages AC-15, AC-16)',
    async (code, expected) => {
      await navigate('/messages', [{ ...SESSION, role: { code, name: 'Role' } }]);

      const root = harness.routeNativeElement as HTMLElement;
      expect(root.tagName).toBe('APP-SHELL');
      expect(root.querySelector('app-messages-host')?.textContent).toContain(expected);
    },
  );

  it('resolves the compound path /auth/register-company as a sibling of auth, with no guard', async () => {
    await harness.navigateByUrl('/auth/register-company');
    await harness.fixture.whenStable();

    expect(router.url).toBe('/auth/register-company');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Create your organization',
    );
    expect(harness.fixture.nativeElement.querySelector('app-shell')).toBeNull();
  });
});
