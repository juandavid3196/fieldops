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

  const emptyQueue = {
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
  };
  const flushHub = async (): Promise<void> => {
    const get = (path: string) =>
      vi.waitFor(() =>
        httpTesting.expectOne((r) => r.method === 'GET' && r.url === `${API}${path}`),
      );
    (await get('/invoices/options')).flush({
      timezone: 'UTC',
      currency: 'USD',
      paymentPrefix: 'PAY',
      branches: [],
      members: [],
      canAct: true,
    });
    (await get('/invoices/customers')).flush([]);
    (await get('/invoices/overview')).flush({
      metrics: {
        outstanding: 0,
        overdue: 0,
        draft: 0,
        paidThisMonth: 0,
        averageDaysToPay: null,
        currency: 'USD',
      },
      aging: { current: 0, days1To30: 0, days31To60: 0, days60Plus: 0 },
      recentPayments: [],
    });
    (await get('/invoices')).flush({ items: [], page: 1, pageSize: 20, total: 0 });
    (await get('/billing-review/queue')).flush(emptyQueue);
  };

  it('serves /invoices as the hub without redirect, shows the Invoices group with its six sub-items and current marking, keeps /invoices/review and gives technicians the forbidden state (FR-13, AC-14)', async () => {
    await open('/invoices', 'owner');
    await flushHub();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/invoices');

    const root = harness.routeNativeElement as HTMLElement;
    expect(root.querySelector('h1')?.textContent).toContain('Invoices & payments');
    const group = root.querySelector<HTMLButtonElement>('nav .sidebar__link--parent')!;
    expect(group.textContent?.trim()).toBe('Invoices');
    expect(group.getAttribute('aria-expanded')).toBe('true');
    const children = (): HTMLAnchorElement[] =>
      Array.from(
        root.querySelectorAll<HTMLAnchorElement>(`#${group.getAttribute('aria-controls')} a`),
      );
    const current = (): (string | undefined)[] =>
      children()
        .filter((link) => link.getAttribute('aria-current') === 'page')
        .map((link) => link.textContent?.trim());
    expect(children().map((link) => link.textContent?.trim())).toEqual([
      'All invoices',
      'Needs review',
      'Drafts',
      'Sent',
      'Payments',
      'Overdue',
    ]);
    expect(current()).toEqual(['All invoices']);

    // Query-param navigation keeps the hub and moves the current marking.
    await open('/invoices?status=overdue', 'owner');
    const overdue = await vi.waitFor(() =>
      httpTesting.expectOne((r) => r.method === 'GET' && r.url === `${API}/invoices`),
    );
    expect(overdue.request.params.get('status')).toBe('overdue');
    overdue.flush({ items: [], page: 1, pageSize: 20, total: 0 });
    await harness.fixture.whenStable();
    expect(current()).toEqual(['Overdue']);

    // The review queue still loads at its own path.
    await open('/invoices/review', 'owner');
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
    queue.flush(emptyQueue);
    await harness.fixture.whenStable();
    expect(root.querySelector('h1')?.textContent).toContain('Completed jobs review');
    expect(current()).toEqual(['Needs review']);
    group.click();
    await harness.fixture.whenStable();
    expect(group.getAttribute('aria-expanded')).toBe('false');

    // Technician: the hub shows its own forbidden state and makes no request.
    await open('/coming-soon/quotes', 'owner');
    await open('/invoices', 'technician');
    await harness.fixture.whenStable();
    expect(root.textContent).toContain("You don't have access to invoices.");
  }, 20_000);
});
