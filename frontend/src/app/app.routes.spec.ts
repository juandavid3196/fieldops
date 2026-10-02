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
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: 'owner', name: 'Owner' },
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

  afterEach(() => httpTesting.verify());

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
    expect(links()).toContain('Requests');
  });

  it.each([
    ['quotes', 'Quotes'],
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

  it('redirects an unknown Coming soon slug to Overview (FR-03, AC-03)', async () => {
    await navigate('/coming-soon/unknown-module', [true, true]);

    expect(router.url).toBe('/overview');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Welcome');
  });

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
