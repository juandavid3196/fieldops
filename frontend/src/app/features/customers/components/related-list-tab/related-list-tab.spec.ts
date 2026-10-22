import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { RelatedKind } from '../../models/customer.model';
import { RelatedListTab } from './related-list-tab';

const API = 'http://api.test';
const ID = '3f2b8c1e-5a4d-4e6f-9a7b-1c2d3e4f5a6b';

interface Case {
  readonly kind: RelatedKind;
  readonly item: object;
  readonly ariaLabel: string;
  readonly href: string;
  readonly texts: readonly string[];
  readonly emptyTitle: string;
  readonly noun: string;
}

const CASES: readonly Case[] = [
  {
    kind: 'requests',
    item: {
      id: 'r-1',
      number: 'REQ-1048',
      service: 'General services',
      propertyName: null,
      createdAt: '2026-10-20T15:00:00Z',
      status: 'quoted',
    },
    ariaLabel: 'REQ-1048, Quoted',
    href: '/requests?request=r-1',
    texts: ['General services', 'Oct 20, 2026', '—'],
    emptyTitle: 'No requests yet',
    noun: 'requests',
  },
  {
    kind: 'quotes',
    item: {
      id: 'q-1',
      number: 'Q-172',
      request: { id: 'r-1', number: 'REQ-1048', service: 'Plumbing' },
      total: 460,
      validUntil: '2026-11-03',
      status: 'approved',
    },
    ariaLabel: 'Q-172, Approved',
    href: '/quotes/q-1',
    texts: ['REQ-1048 · Plumbing', '$460.00', 'Nov 3, 2026'],
    emptyTitle: 'No quotes yet',
    noun: 'quotes',
  },
  {
    kind: 'jobs',
    item: {
      id: 'j-1',
      number: 'JOB-0834',
      title: 'General services',
      propertyName: 'Home',
      nextVisitAt: '2026-10-24T14:00:00Z',
      technician: { name: 'Carlos Ruiz', initials: 'CR' },
      status: 'scheduled',
    },
    ariaLabel: 'JOB-0834, Scheduled',
    href: '/jobs/j-1',
    texts: ['General services', 'Oct 24 · 9:00 AM', 'Carlos Ruiz', 'CR'],
    emptyTitle: 'No jobs yet',
    noun: 'jobs',
  },
  {
    kind: 'invoices',
    item: {
      id: 'i-1',
      number: 'INV-91',
      job: { id: 'j-1', number: 'JOB-0834' },
      issueDate: '2026-10-20',
      dueDate: '2026-11-03',
      balanceDue: 1280,
      status: 'sent',
    },
    ariaLabel: 'INV-91, Sent',
    href: '/invoices/i-1',
    texts: ['JOB-0834', 'Oct 20, 2026', 'Nov 3, 2026', '$1,280.00', 'Outstanding', '$1,740.00'],
    emptyTitle: 'No invoices yet',
    noun: 'invoices',
  },
];

const page = (kind: RelatedKind, items: readonly object[], customerTotal = items.length) => ({
  items,
  totalCount: items.length,
  customerTotal,
  page: 1,
  pageSize: 10,
  timezone: 'America/Chicago',
  ...(kind === 'quotes' || kind === 'invoices' ? { currency: 'USD' } : {}),
  ...(kind === 'invoices' ? { summary: { outstanding: 1740, overdue: 460, paid: 890 } } : {}),
});

describe('RelatedListTab', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<RelatedListTab>;
  let host: HTMLElement;

  const request = (kind: RelatedKind) =>
    httpTesting.expectOne((r) => r.method === 'GET' && r.url === `${API}/customers/${ID}/${kind}`);
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  function create(kind: RelatedKind, canCreate = false): RelatedListTab {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RelatedListTab);
    fixture.componentRef.setInput('kind', kind);
    fixture.componentRef.setInput('customerId', ID);
    fixture.componentRef.setInput('canCreate', canCreate);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  afterEach(() => {
    vi.useRealTimers();
    httpTesting.verify();
  });

  it.each(CASES)(
    'builds the $kind query and renders rows as one link with the module status (BR-04 to BR-08, AC-02 to AC-06)',
    async ({ kind, item, ariaLabel, href, texts }) => {
      const list = create(kind);
      const first = request(kind);
      expect(first.request.params.keys().sort()).toEqual(['page', 'pageSize']);
      expect(first.request.params.get('page')).toBe('1');
      expect(first.request.params.get('pageSize')).toBe('10');
      first.flush(page(kind, [item], 27));
      await settle();

      const link = host.querySelector<HTMLAnchorElement>('a.related__row')!;
      expect(link.getAttribute('href')).toBe(href);
      expect(link.getAttribute('aria-label')).toBe(ariaLabel);
      texts.forEach((value) => expect(text()).toContain(value));
      expect(text()).toContain(`(27)`);
      expect(text()).toContain(`Showing 1–1 of 1 ${kind}`);
      expect(host.querySelector('.related__cell--status')?.textContent?.trim()).toBe(
        ariaLabel.split(', ')[1],
      );

      // Search is debounced 300 ms, trimmed and returns to page 1.
      vi.useFakeTimers();
      list.page.set(3);
      list.onSearch('  leak  ');
      vi.advanceTimersByTime(299);
      httpTesting.expectNone((r) => r.url === `${API}/customers/${ID}/${kind}`);
      vi.advanceTimersByTime(1);
      const searched = request(kind);
      expect(searched.request.params.get('search')).toBe('leak');
      expect(searched.request.params.get('page')).toBe('1');
      vi.useRealTimers();
      searched.flush(page(kind, [item], 27));

      // Status and page size return to page 1; a page change keeps the size.
      list.onStatus('cancelled');
      const status = request(kind);
      expect(status.request.params.get('status')).toBe('cancelled');
      expect(status.request.params.get('search')).toBe('leak');
      status.flush(page(kind, [item], 27));
      list.onPageChange({ page: 2, rows: 25 });
      const size = request(kind);
      expect(size.request.params.get('pageSize')).toBe('25');
      expect(size.request.params.get('page')).toBe('1');
      size.flush(page(kind, [item], 27));
      list.onPageChange({ page: 1, rows: 25 });
      const next = request(kind);
      expect(next.request.params.get('page')).toBe('2');
      next.flush(page(kind, [item], 27));
    },
    20_000,
  );

  it.each(CASES)(
    'shows empty, filtered-empty with Clear filters and a $kind error that keeps filters on Retry (BR-15, AC-17)',
    async ({ kind, emptyTitle, noun }) => {
      const list = create(kind);
      request(kind).flush(page(kind, [], 0));
      await settle();
      expect(text()).toContain(emptyTitle);
      expect(text()).toContain('for this customer will appear here.');

      list.onStatus('draft');
      request(kind).flush(page(kind, [], 5));
      await settle();
      expect(text()).toContain(`No ${noun} match your filters.`);
      Array.from(host.querySelectorAll('button'))
        .find((b) => b.textContent?.trim() === 'Clear filters')!
        .click();
      const cleared = request(kind);
      expect(cleared.request.params.has('status')).toBe(false);
      expect(cleared.request.params.get('page')).toBe('1');
      expect(list.hasFilters()).toBe(false);

      // Skeleton while loading, then a tab-only error; Retry reloads with the same filters.
      fixture.detectChanges();
      expect(host.querySelector('[aria-busy="true"]')).not.toBeNull();
      cleared.flush(null, { status: 500, statusText: 'Server Error' });
      await settle();
      expect(text()).toContain(`We couldn't load ${noun}.`);
      list.status.set('sent');
      Array.from(host.querySelectorAll('button'))
        .find((b) => b.textContent?.trim() === 'Retry')!
        .click();
      const retried = request(kind);
      expect(retried.request.params.get('status')).toBe('sent');
      retried.flush(page(kind, [], 5));

      // 403 is resolved by the page, not shown as a tab error.
      const failed: unknown[] = [];
      list.failed.subscribe((error) => failed.push(error));
      list.load();
      request(kind).flush(null, { status: 403, statusText: 'Forbidden' });
      expect(failed).toHaveLength(1);
    },
    20_000,
  );
});
