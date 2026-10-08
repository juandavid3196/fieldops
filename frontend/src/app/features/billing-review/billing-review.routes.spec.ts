import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../../app.routes';
import { API_CONFIG } from '../../core/config/api.config';
import { authInterceptor } from '../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { Session } from '../../core/models/session.model';

const API = 'http://api.test';

const session = (role: string): Session => ({
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: role, name: role },
});

describe('Invoices navigation and route', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter(routes),
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  async function open(url: string, role: string): Promise<void> {
    const navigation = harness.navigateByUrl(url);
    const request = await vi.waitFor(() =>
      httpTesting.expectOne({ method: 'GET', url: `${API}/sessions/current` }),
    );
    request.flush(session(role));
    await navigation;
  }

  it('redirects /invoices to the review queue, shows the Invoices group with its five sub-items and gives technicians the forbidden state without requests (FR-14, AC-19)', async () => {
    await open('/invoices', 'owner');
    expect(TestBed.inject(Router).url).toBe('/invoices/review');
    const options = await vi.waitFor(() =>
      httpTesting.expectOne({ method: 'GET', url: `${API}/billing-review/options` }),
    );
    options.flush({
      timezone: 'UTC',
      currency: 'USD',
      branches: [],
      technicians: [],
      canAct: true,
    });
    const queue = httpTesting.expectOne((r) => r.url === `${API}/billing-review/queue`);
    expect(queue.request.params.get('completed')).toBe('30d');
    queue.flush({
      items: [],
      page: 1,
      pageSize: 20,
      total: 0,
      tabs: { all: 0, variances: 0, ready: 0 },
      metrics: {
        needsReview: 0,
        withVariances: 0,
        readyToInvoice: 0,
        completedValue: 0,
        currency: 'USD',
      },
    });
    await harness.fixture.whenStable();

    const root = harness.routeNativeElement as HTMLElement;
    const group = root.querySelector<HTMLButtonElement>('nav .sidebar__link--parent')!;
    expect(group.textContent?.trim()).toBe('Invoices');
    expect(group.getAttribute('aria-expanded')).toBe('true');
    const children = Array.from(
      root.querySelectorAll<HTMLAnchorElement>(`#${group.getAttribute('aria-controls')} a`),
    );
    expect(children.map((link) => [link.textContent?.trim(), link.getAttribute('href')])).toEqual([
      ['Needs review', '/invoices/review'],
      ['Drafts', '/coming-soon/invoice-drafts'],
      ['Sent', '/coming-soon/invoice-sent'],
      ['Payments', '/coming-soon/invoice-payments'],
      ['Overdue', '/coming-soon/invoice-overdue'],
    ]);
    expect(children[0].getAttribute('aria-current')).toBe('page');
    expect(root.querySelector('h1')?.textContent).toContain('Completed jobs review');
    expect(root.textContent).toContain('No completed jobs need review.');
    group.click();
    await harness.fixture.whenStable();
    expect(group.getAttribute('aria-expanded')).toBe('false');

    // Technician: the page shows its own forbidden state and makes no request.
    await open('/coming-soon/quotes', 'owner');
    await open('/invoices/review', 'technician');
    await harness.fixture.whenStable();
    expect(root.textContent).toContain("You don't have access to the completed jobs review.");
  }, 20_000);
});
