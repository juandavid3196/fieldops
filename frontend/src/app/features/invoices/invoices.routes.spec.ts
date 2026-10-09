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
import { invoiceBody } from './testing/invoice-fixtures';

const API = 'http://api.test';

const session = (role: string): Session => ({
  user: { id: 'u-1', firstName: 'Sofia', lastName: 'Martinez', email: 'sofia@example.com' },
  organization: { id: 'o-1', name: 'Acme Services' },
  role: { code: role, name: role },
});

describe('Invoice routes', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();
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

  async function openInShell(url: string, role: string): Promise<void> {
    const navigation = harness.navigateByUrl(url);
    const request = await vi.waitFor(() =>
      httpTesting.expectOne({ method: 'GET', url: `${API}/sessions/current` }),
    );
    request.flush(session(role));
    await navigation;
  }

  it('opens /invoices/:id in the shell with the Invoices group highlighted, keeps /invoices/review first and serves /invoices/view outside the shell without a session (FR-09, AC-17)', async () => {
    await openInShell('/invoices/inv-1', 'owner');
    const detail = await vi.waitFor(() =>
      httpTesting.expectOne({ method: 'GET', url: `${API}/invoices/inv-1` }),
    );
    detail.flush(invoiceBody());
    await harness.fixture.whenStable();

    const root = harness.routeNativeElement as HTMLElement;
    expect(root.tagName).toBe('APP-SHELL');
    expect(root.querySelector('h1')?.textContent).toContain('Invoice INV-1048');
    expect(root.querySelector('nav[aria-label="Breadcrumb"] a')?.getAttribute('href')).toBe(
      '/invoices',
    );
    const group = root.querySelector('nav .sidebar__link--parent')!;
    expect(group.textContent?.trim()).toBe('Invoices');
    expect(group.classList).toContain('sidebar__link--active');

    // `review` is declared before `:invoiceId`: the queue loads, never `GET /invoices/review`.
    await openInShell('/invoices/review', 'owner');
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
    httpTesting
      .expectOne((r) => r.url === `${API}/billing-review/queue`)
      .flush({
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
    expect(root.querySelector('h1')?.textContent).toContain('Completed jobs review');

    // The public link is never matched as an invoice id and needs no session request.
    await harness.navigateByUrl('/invoices/view');
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/invoices/view');
    expect(harness.fixture.nativeElement.querySelector('app-shell')).toBeNull();
    expect(harness.fixture.nativeElement.textContent).toContain(
      'This invoice is no longer available',
    );
  }, 20_000);
});
