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
  async function answerSessionRequest(ok: boolean): Promise<void> {
    const request = await vi.waitFor(() =>
      httpTesting.expectOne({ method: 'GET', url: SESSION_URL }),
    );
    if (ok) {
      request.flush(SESSION);
    } else {
      request.flush(null, { status: 401, statusText: 'Unauthorized' });
    }
  }

  async function navigate(url: string, sessionResponses: readonly boolean[]): Promise<void> {
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
  });

  it('renders Sign In for a visitor without a session (AC-01)', async () => {
    await navigate('/auth/sign-in', [false]);

    expect(router.url).toBe('/auth/sign-in');
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
  });
});
