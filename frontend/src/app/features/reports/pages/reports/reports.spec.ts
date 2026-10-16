import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  ReportData,
  ReportExportItem,
  ReportInfo,
  ReportKey,
  ReportOptions,
  ReportRow,
} from '../../models/reports.model';
import { REPORT_CATALOG, visibleKeysForRole } from '../../utils/report-catalog';
import { Reports } from './reports';

const API = 'http://api.test';
const NOW = new Date('2026-10-15T15:00:00Z');
const STORAGE_KEY = 'fieldops.reports.o-1.u-1';
const serverError = { status: 500, statusText: 'Server Error' };

@Component({ template: '<p>stub</p>' })
class Stub {}

const optionsFor = (keys: readonly ReportKey[], branches = true): ReportOptions => ({
  timezone: 'America/Bogota',
  currency: 'USD',
  today: '2026-10-15',
  marginTarget: 40,
  reports: REPORT_CATALOG.filter((meta) => keys.includes(meta.key)).map((meta): ReportInfo => ({
    key: meta.key,
    group: meta.group,
    name: meta.name,
    description: meta.description,
  })),
  branches: branches
    ? [
        { id: 'br-1', name: 'Main', isActive: true },
        { id: 'br-2', name: 'North', isActive: false },
      ]
    : [],
  categories: [{ id: 'cat-1', name: 'Plumbing', isActive: true }],
  technicians: [{ id: 't-1', name: 'Ana', isActive: true, branchId: 'br-1' }],
});

const reportData = (key: ReportKey, patch: Partial<ReportData> = {}): ReportData => ({
  reportKey: key,
  from: key === 'invoice_aging' ? null : '2026-10-01',
  to: key === 'invoice_aging' ? null : '2026-10-15',
  previousFrom: null,
  previousTo: null,
  asOf: '2026-10-15',
  timezone: 'America/Bogota',
  currency: 'USD',
  granularity: null,
  marginTarget: 40,
  summary: [],
  notes: { otherCurrencyCount: null },
  chart: { kind: 'bucket_bars', series: [], points: [] },
  items: [],
  totals: {},
  total: 0,
  page: 1,
  pageSize: 10,
  ...patch,
});

const row = (id: string, patch: Record<string, unknown> = {}): ReportRow => ({
  id,
  links: {},
  ...patch,
});

const profitRows = (): ReportRow[] => [
  row('wo-1', {
    job: 'WO-1048',
    completedAt: '2026-10-15T03:00:00Z',
    customer: 'Sofia Martinez',
    service: 'Plumbing',
    technician: 'Ana',
    revenue: 357.28,
    estimatedCost: 178.5,
    grossProfit: 178.78,
    margin: 50,
    branch: 'Main',
    invoiced: true,
    links: { job: 'job-1', customer: 'cust-1', technician: 't-1' },
  }),
  row('wo-2', {
    job: 'WO-1035',
    completedAt: '2026-10-14T18:00:00Z',
    customer: 'BrightLine',
    service: 'Cleaning',
    technician: 'Unassigned',
    belowTarget: true,
    revenue: 1560,
    estimatedCost: 1114,
    grossProfit: 446,
    margin: 28.6,
    branch: 'North',
    invoiced: true,
    links: { job: 'job-2', customer: null },
  }),
  row('wo-3', { job: 'WO-1056', service: 'HVAC', margin: null, invoiced: false }),
];

const profitData = (patch: Partial<ReportData> = {}): ReportData =>
  reportData('job_profitability', {
    summary: [
      { key: 'revenue', label: 'Revenue', unit: 'currency', value: 1000, previous: 800 },
      { key: 'notInvoiced', label: 'Not invoiced', unit: 'count', value: 1, previous: 0 },
      {
        key: 'margin',
        label: 'Estimated gross margin',
        unit: 'percent',
        value: 42.5,
        previous: 40,
      },
    ],
    chart: {
      kind: 'category_bars',
      series: [
        { key: 'revenue', label: 'Revenue' },
        { key: 'estimatedCost', label: 'Estimated cost' },
      ],
      points: [{ label: 'Plumbing', start: null, values: { revenue: 1000, estimatedCost: 600 } }],
    },
    items: profitRows(),
    totals: { revenue: 1917.28, estimatedCost: 1292.5, grossProfit: 624.78, margin: 32.6 },
    total: 3,
    ...patch,
  });

describe('Reports workspace', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  const settle = async (): Promise<void> => {
    for (let round = 0; round < 2; round++) {
      harness.detectChanges();
      await harness.fixture.whenStable();
    }
    harness.detectChanges();
  };
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const control = (label: string): HTMLElement | undefined =>
    Array.from(document.body.querySelectorAll<HTMLElement>('button, a')).find(
      (candidate) => candidate.textContent?.replace(/\s+/g, ' ').trim() === label,
    );
  const byLabel = (label: string): HTMLElement | null =>
    document.querySelector<HTMLElement>(`[aria-label="${label}"]`);
  const norm = (value: string): string => {
    const [path, query = ''] = value.split('?');
    return query === '' ? path : `${path}?${query.split('&').sort().join('&')}`;
  };
  const url = (): string => norm(TestBed.inject(Router).url);
  const data = (key: ReportKey, match: Record<string, string> = {}) =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/reports/${key}` &&
        Object.entries(match).every(([name, value]) => r.params.get(name) === value),
    );
  const exportRequest = (key: ReportKey) =>
    httpTesting.expectOne((r) => r.url === `${API}/reports/${key}/export`);
  const component = (): Reports => harness.routeDebugElement!.componentInstance as Reports;
  const sortHeader = (name: string): HTMLElement =>
    Array.from(document.querySelectorAll<HTMLElement>('.rtable th')).find((th) =>
      th.textContent?.includes(name),
    )!;
  const submitFilters = async (): Promise<void> => {
    document
      .querySelector('app-report-filters form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  };

  async function setup(
    startUrl: string,
    role = 'owner',
    options: ReportOptions | null = optionsFor(visibleKeysForRole(role)),
  ): Promise<void> {
    vi.useFakeTimers({ toFake: ['Date'], now: NOW });
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'reports', component: Reports },
          { path: 'jobs/:id', component: Stub },
          { path: 'auth/sign-in', component: Stub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne(`${API}/sessions/current`).flush({
      user: {
        id: 'u-1',
        firstName: 'Alex',
        lastName: 'Morgan',
        email: 'alex@acme.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    TestBed.inject(Router).setUpLocationChangeListener();
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(startUrl);
    if (options !== null && visibleKeysForRole(role).length > 0) {
      httpTesting.expectOne(`${API}/reports/options`).flush(options);
      await settle();
    }
  }

  const restart = (): void => {
    httpTesting.verify();
    TestBed.resetTestingModule();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => {
    try {
      httpTesting?.verify();
    } finally {
      vi.useRealTimers();
      vi.restoreAllMocks();
      localStorage.clear();
    }
  });

  it('restores and normalizes the URL, applies edits only on Apply/Reset, never refetches on compare and resets a rejected branch (FR-03, FR-09, AC-05, AC-12, AC-19)', async () => {
    await setup(
      '/reports?report=job_profitability&range=bogus&from=2026-10-01&branch=nope&bucket=current&status=completed&sort=bad&direction=up&page=0&pageSize=30',
    );
    // Invalid or not-applicable values are replaced before any data request (BR-05).
    expect(url()).toBe(norm('/reports?report=job_profitability&status=completed'));
    const first = data('job_profitability', {
      from: '2026-10-01',
      to: '2026-10-15',
      status: 'completed',
      sort: 'completedAt',
      direction: 'desc',
      page: '1',
      pageSize: '10',
    });
    for (const name of ['branchId', 'bucket', 'categoryId', 'technicianId', 'compare']) {
      expect(first.request.params.has(name)).toBe(false);
    }
    first.flush(profitData());
    await settle();
    expect(text()).toContain('Job profitability');

    // Edits wait for Apply; nothing reloads meanwhile.
    component().setRange('last_month');
    component().setBranch('br-1');
    component().setFilter({ key: 'category', code: 'cat-1' });
    await settle();
    httpTesting.expectNone(`${API}/reports/job_profitability`);
    expect(url()).toBe(norm('/reports?report=job_profitability&status=completed'));

    await submitFilters();
    expect(url()).toBe(
      norm(
        '/reports?report=job_profitability&range=last_month&branch=br-1&status=completed&category=cat-1',
      ),
    );
    data('job_profitability', {
      from: '2026-09-01',
      to: '2026-09-30',
      branchId: 'br-1',
      categoryId: 'cat-1',
      page: '1',
    }).flush(profitData());
    await settle();

    // Comparison changes the URL and the display only.
    component().onCompare(false);
    await settle();
    httpTesting.expectNone(`${API}/reports/job_profitability`);
    expect(url()).toContain('compare=false');
    expect(text()).not.toContain('+25.0%');

    // A server 404 for the branch resets only that value.
    component().setBranch('br-2');
    await submitFilters();
    data('job_profitability', { branchId: 'br-2' }).flush(
      { code: 'branch_not_found' },
      { status: 404, statusText: 'Not Found' },
    );
    await settle();
    expect(url()).not.toContain('branch=');
    httpTesting.expectOne(`${API}/reports/options`).flush(optionsFor(visibleKeysForRole('owner')));
    data('job_profitability').flush(profitData());
    await settle();

    // Reset restores period, branch, filters, sort and page; compare stays.
    control('Revenue')!.click();
    await settle();
    data('job_profitability', { sort: 'revenue', direction: 'desc', page: '1' }).flush(
      profitData(),
    );
    await settle();
    control('Reset')!.click();
    await settle();
    expect(url()).toBe(norm('/reports?report=job_profitability&compare=false'));
    data('job_profitability', { sort: 'completedAt', direction: 'desc', from: '2026-10-01' }).flush(
      profitData(),
    );
  });

  it.each([
    ['owner', ['Billing & payments2', 'Sales & quotes1', 'Operations2'], 'job_profitability'],
    ['dispatcher', ['Sales & quotes1', 'Operations2'], 'quote_conversion'],
    ['accounting', ['Billing & payments2'], 'job_profitability'],
    ['viewer', [], null],
  ])(
    'shows only the reports of the %s role, falls back from a not-permitted report and keeps the forbidden role request-free (FR-01, AC-01, AC-02)',
    async (role, groups, first) => {
      await setup('/reports?report=job_profitability', role);
      if (first === null) {
        expect(text()).toContain("You don't have access to Reports.");
        expect(document.querySelector('app-report-catalog')).toBeNull();
        return;
      }
      expect(
        Array.from(document.querySelectorAll('.catalog__heading'), (heading) =>
          (heading.textContent ?? '').replace(/\s+/g, ''),
        ).filter((heading) => !/Favorites|Recently/.test(heading)),
      ).toEqual(groups.map((group) => group.replace(/\s+/g, '')));
      expect(url()).toBe(norm(`/reports?report=${first}`));
      data(first as ReportKey).flush(reportData(first as ReportKey));
      await settle();
      expect(control('Export CSV')).toBeDefined();
    },
  );

  it('keeps Favorites and Recently viewed per user, ignores unknown or invisible keys, filters by search and survives a failing storage (FR-14, AC-20)', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        favorites: ['invoice_aging', 'bogus', 'job_profitability'],
        recent: ['quote_conversion', 'nope'],
      }),
    );
    await setup('/reports?report=job_profitability', 'accounting');
    data('job_profitability').flush(profitData());
    await settle();
    const section = (id: string): string =>
      document.querySelector(`#${id}`)?.parentElement?.textContent?.replace(/\s+/g, ' ').trim() ??
      '';
    // quote_conversion is not visible to Accounting; the loaded report is newest.
    expect(section('catalog-favorites')).toMatch(/Favorites\s*2/);
    expect(section('catalog-favorites')).toContain('Invoice aging');
    expect(section('catalog-recent')).toBe('Recently viewed Job profitability');

    const search = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    search.value = 'days past due';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(section('catalog-recent') ?? '').toBe('');
    expect(document.querySelectorAll('.catalog__item')).toHaveLength(2);
    search.value = '';
    search.dispatchEvent(new Event('input', { bubbles: true }));

    byLabel('Remove from favorites')!.click();
    await settle();
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!)).toEqual({
      favorites: ['invoice_aging'],
      recent: ['job_profitability', 'quote_conversion'],
    });
    control('Invoice aging')!.click();
    await settle();
    expect(url()).toBe(norm('/reports?report=invoice_aging'));
    data('invoice_aging').flush(reportData('invoice_aging'));
    await settle();
    expect(section('catalog-recent')).toBe('Recently viewed Invoice aging Job profitability');
    expect(byLabel('Remove from favorites')).not.toBeNull();

    // A storage failure leaves both lists empty and the page usable.
    restart();
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied');
    });
    await setup('/reports?report=job_profitability', 'accounting');
    data('job_profitability').flush(profitData());
    await settle();
    expect(section('catalog-favorites')).toMatch(/Favorites\s*0/);
    expect(document.querySelector('#catalog-recent')).toBeNull();
    byLabel('Add to favorites')!.click();
    await settle();
    expect(section('catalog-favorites')).toMatch(/Favorites\s*0/);
    expect(control('Export CSV')).toBeDefined();
  });

  it('requests sort, paging and refresh from the server and renders loading, empty, error, refresh, forbidden and no-branch states (FR-10, FR-15, AC-13, AC-19, AC-21)', async () => {
    await setup('/reports?report=job_profitability&sort=revenue&direction=asc&page=9', 'owner');
    expect(document.querySelectorAll('p-skeleton').length).toBeGreaterThan(0);
    // A page past the last returns no items; the page moves to the last one with replaceUrl.
    data('job_profitability', { page: '9' }).flush(profitData({ items: [], total: 25, page: 9 }));
    await settle();
    expect(url()).toBe(norm('/reports?report=job_profitability&sort=revenue&direction=asc&page=3'));
    data('job_profitability', { page: '3', sort: 'revenue', direction: 'asc' }).flush(
      profitData({ total: 25, page: 3 }),
    );
    await settle();
    expect(text()).toContain('Showing 21–25 of 25 jobs');
    expect(text()).toContain('25 jobs');
    expect(sortHeader('Revenue').getAttribute('aria-sort')).toBe('ascending');
    expect(sortHeader('Completed').getAttribute('aria-sort')).toBe('none');
    expect(sortHeader('Invoicing').hasAttribute('aria-sort')).toBe(false);

    control('Revenue')!.click();
    await settle();
    data('job_profitability', { sort: 'revenue', direction: 'desc', page: '1' }).flush(
      profitData({ total: 25 }),
    );
    await settle();
    expect(sortHeader('Revenue').getAttribute('aria-sort')).toBe('descending');
    component().changePage(2);
    await settle();
    data('job_profitability', { page: '2', pageSize: '10' }).flush(profitData({ total: 25 }));
    await settle();
    component().changePageSize(25);
    await settle();
    data('job_profitability', { page: '1', pageSize: '25' }).flush(profitData({ total: 25 }));
    await settle();
    // The totals row covers the whole filtered set.
    expect(document.querySelector('tfoot')?.textContent).toContain('$1,917.28');

    // Refresh keeps the data when it fails.
    byLabel('Refresh report')!.click();
    await settle();
    data('job_profitability').flush(null, serverError);
    await settle();
    expect(text()).toContain("Couldn't refresh.");
    expect(text()).toContain('WO-1048');
    expect(text()).toMatch(/Updated just now/);

    // Empty: the chart and table texts, with Reset.
    component().changePageSize(10);
    await settle();
    data('job_profitability', { pageSize: '10' }).flush(profitData({ items: [], total: 0 }));
    await settle();
    expect(text()).toContain('No data for this report and filters.');
    expect(text()).toContain('No rows match these filters.');

    // Load error with Try again, then a 403 swaps the page for the forbidden state.
    component().changePageSize(25);
    await settle();
    data('job_profitability', { pageSize: '25' }).flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load this report.");
    control('Try again')!.click();
    await settle();
    data('job_profitability', { pageSize: '25' }).flush(null, { status: 403, statusText: 'No' });
    await settle();
    expect(text()).toContain("You don't have access to Reports.");

    // No branch in scope: its message and no data request.
    restart();
    await setup('/reports', 'owner', optionsFor(visibleKeysForRole('owner'), false));
    expect(text()).toContain('No branches available.');
    httpTesting.expectNone(
      (r) => r.url.startsWith(`${API}/reports/`) && !r.url.endsWith('options'),
    );
  });

  it('exports the applied state with busy, 409 and error handling and reopens a report from Export history (FR-12, FR-13, AC-18, AC-22)', async () => {
    const urlApi = URL as unknown as Record<string, unknown>;
    urlApi['createObjectURL'] = vi.fn(() => 'blob:file');
    urlApi['revokeObjectURL'] = vi.fn();
    const downloads: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });
    await setup('/reports?report=job_profitability&status=completed');
    data('job_profitability').flush(profitData());
    await settle();
    component().setFilter({ key: 'category', code: 'cat-1' });

    control('Export CSV')!.click();
    await settle();
    const csv = exportRequest('job_profitability');
    expect(csv.request.params.get('format')).toBe('csv');
    expect(csv.request.params.get('compare')).toBe('true');
    expect(csv.request.params.get('status')).toBe('completed');
    expect(csv.request.params.has('categoryId')).toBe(false);
    expect(csv.request.params.has('page')).toBe(false);
    expect(control('Export CSV')!.hasAttribute('disabled')).toBe(true);
    expect(control('Download PDF')!.hasAttribute('disabled')).toBe(true);
    expect(control('Export CSV')!.getAttribute('aria-busy')).toBe('true');
    csv.flush(new Blob(['a']), {
      headers: { 'Content-Disposition': 'attachment; filename=job-profitability-server.csv' },
    });
    await settle();
    expect(downloads).toEqual(['job-profitability-server.csv']);
    expect(control('Export CSV')!.hasAttribute('disabled')).toBe(false);

    component().onCompare(false);
    await settle();
    control('Download PDF')!.click();
    const pdf = exportRequest('job_profitability');
    expect(pdf.request.params.get('format')).toBe('pdf');
    expect(pdf.request.params.get('compare')).toBe('false');
    pdf.flush(new Blob(['x']), { status: 409, statusText: 'Conflict' });
    await settle();
    expect(text()).toContain('Narrow the filters to export 1,000 rows or fewer.');
    control('Export CSV')!.click();
    exportRequest('job_profitability').flush(new Blob(['x']), { status: 409, statusText: 'No' });
    await settle();
    expect(text()).toContain('Narrow the filters to export 5,000 rows or fewer.');
    control('Export CSV')!.click();
    exportRequest('job_profitability').flush(new Blob(['x']), serverError);
    await settle();
    expect(text()).toContain("We couldn't export the report. Try again.");

    // Export history: error, retry, then the list; Open report restores the export's state.
    const opener = control('Export history')!;
    opener.focus();
    opener.click();
    await settle();
    expect(document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby')).toBe(
      'export-history-title',
    );
    httpTesting.expectOne(`${API}/reports/exports`).flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load export history.");
    control('Try again')!.click();
    await settle();
    const items: ReportExportItem[] = [
      {
        id: 1,
        occurredAt: '2026-10-14T20:00:00Z',
        reportKey: 'invoice_aging',
        reportName: 'Invoice aging',
        format: 'pdf',
        rowCount: 12,
        filters: {
          asOf: '2026-10-14',
          branchId: 'gone',
          status: 'overdue',
          sort: 'dueDate',
          direction: 'asc',
          compare: false,
        },
      },
      {
        id: 2,
        occurredAt: '2026-10-13T20:00:00Z',
        reportKey: 'job_profitability',
        reportName: 'Job profitability',
        format: 'csv',
        rowCount: 3,
        filters: { from: '2026-09-01', to: '2026-09-30', branchId: 'br-1' },
      },
    ];
    httpTesting.expectOne(`${API}/reports/exports`).flush({ items });
    await settle();
    expect(text()).toContain('Oct 14, 2026 3:00 PM · PDF · 12 rows');
    expect(text()).toContain('As of Oct 14, 2026 · Unavailable branch');
    expect(text()).toContain('Sep 1, 2026 – Sep 30, 2026 · Main');
    expect(text()).toContain("Files aren't stored. Open a report to export it again.");

    control('Open report')!.click();
    await settle();
    expect(url()).toBe(
      norm('/reports?report=invoice_aging&status=overdue&sort=dueDate&direction=asc&compare=false'),
    );
    data('invoice_aging', { status: 'overdue', sort: 'dueDate', direction: 'asc' }).flush(
      reportData('invoice_aging'),
    );
    await settle();
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  });

  it.each([
    {
      name: 'Job profitability: deltas, previous percent, below target, not invoiced, links, estimated labels, disclaimer',
      start: '/reports?report=job_profitability',
      key: 'job_profitability' as ReportKey,
      data: profitData(),
      expected: [
        '+25.0%',
        'vs 40.0% previous',
        '42.5%',
        'Below 40% target',
        'Not invoiced',
        'Unassigned',
        'Oct 14, 2026',
        'Estimated gross profit',
        'Estimated gross margin',
        'Profitability figures are operational estimates based on configured product and service costs.',
        'Quantity × configured unit cost of the product or service on each invoice line.',
        'Total',
      ],
      absent: ['Labor', 'labor'],
      links: {
        'WO-1048': '/jobs/job-1',
        'Sofia Martinez': '/customers/cust-1',
        Ana: '/team/t-1/skills-availability',
      },
      plain: ['BrightLine'],
    },
    {
      name: 'Invoice aging: range and compare disabled with the note, no delta',
      start: '/reports?report=invoice_aging',
      key: 'invoice_aging' as ReportKey,
      data: reportData('invoice_aging', {
        summary: [
          {
            key: 'outstanding',
            label: 'Outstanding',
            unit: 'currency',
            value: 500,
            previous: null,
          },
        ],
        items: [
          row('inv-1', {
            invoice: 'INV-9',
            customer: 'Acme',
            job: 'WO-5',
            issueDate: '2026-09-01',
            dueDate: '2026-09-30',
            daysOverdue: 15,
            bucket: '1_30',
            bucketLabel: '1–30 days',
            status: 'partially_paid',
            statusLabel: 'Partially paid',
            total: 500,
            balanceDue: 250,
            branch: 'Main',
            links: { invoice: 'inv-1', job: 'job-5' },
          }),
        ],
        total: 1,
        totals: { total: 500, balanceDue: 250 },
      }),
      expected: [
        'Aging is calculated as of today.',
        '$500',
        'INV-9',
        'Sep 30, 2026',
        'Partially paid',
        'Balance due',
      ],
      absent: ['vs'],
      links: { 'INV-9': '/invoices/inv-1', 'WO-5': '/jobs/job-5' },
      plain: ['Acme'],
      agingDisabled: true,
    },
    {
      name: 'Quote conversion: other-currency note, conversion over 100%, previous unavailable, definitions note',
      start: '/reports?report=quote_conversion',
      key: 'quote_conversion' as ReportKey,
      data: reportData('quote_conversion', {
        summary: [
          { key: 'conversion', label: 'Conversion', unit: 'percent', value: 125, previous: null },
          { key: 'quotesSent', label: 'Quotes sent', unit: 'count', value: 4, previous: 2 },
        ],
        notes: { otherCurrencyCount: 2 },
        chart: {
          kind: 'grouped_series',
          series: [
            { key: 'sent', label: 'Sent' },
            { key: 'accepted', label: 'Accepted' },
          ],
          points: [{ label: 'Oct 1', start: '2026-10-01', values: { sent: 3, accepted: 5 } }],
        },
        items: [
          row('q-1', {
            quote: 'Q-12',
            customer: 'Acme',
            request: 'REQ-3',
            service: 'Uncategorized',
            branch: 'No branch',
            firstSentAt: '2026-10-14T20:30:00Z',
            acceptedAt: null,
            status: 'awaiting_response',
            statusLabel: 'Awaiting response',
            total: 99,
            acceptedValue: null,
            links: { quote: 'q-1', request: 'r-1' },
          }),
        ],
        total: 1,
        totals: { firstSentAt: 3, acceptedAt: 5, acceptedValue: 400 },
      }),
      expected: [
        '2 quotes in other currencies are counted but not summed.',
        '125%',
        'vs — previous',
        '+100.0%',
        'Oct 14, 2026 3:30 PM',
        'No branch',
        'Uncategorized',
        'Awaiting response',
        '3 sent',
        '5 accepted',
        'Conversion can exceed 100% when quotes sent in earlier periods are accepted in this one.',
      ],
      absent: [],
      links: { 'Q-12': '/quotes/q-1' },
      plain: ['Acme'],
    },
    {
      name: 'Completed jobs & on-time: minute metrics as text, Jobs completed caption, on-time result',
      start: '/reports?report=jobs_on_time',
      key: 'jobs_on_time' as ReportKey,
      data: reportData('jobs_on_time', {
        summary: [
          {
            key: 'variance',
            label: 'Avg duration variance',
            unit: 'minutes',
            value: -5,
            previous: 3,
          },
          { key: 'jobsCompleted', label: 'Jobs completed', unit: 'count', value: 8, previous: 8 },
        ],
        items: [
          row('v-1', {
            job: 'WO-7 · Visit 1',
            scheduledStart: '2026-10-14T20:30:00Z',
            customer: 'Acme',
            service: 'HVAC',
            technician: 'Ana',
            status: 'completed',
            statusLabel: 'Completed',
            onTime: 'late',
            onTimeLabel: 'Late',
            estimatedMinutes: 60,
            actualMinutes: 60,
            varianceMinutes: 0,
            branch: 'Main',
            links: { visit: 'job-7', technician: 't-1' },
          }),
        ],
        total: 1,
        totals: { job: 1, statusLabel: 1, onTimeLabel: 0 },
      }),
      expected: [
        '−5 min',
        'vs +3 min previous',
        'Work orders completed in the period',
        'No change',
        'Late',
        'On estimate',
        '60 min',
        'Started by the arrival window end, or within 15 minutes of the scheduled start.',
      ],
      absent: [],
      links: { 'WO-7 · Visit 1': '/jobs/job-7' },
      plain: [],
    },
    {
      name: 'Technician utilization: hour metrics, overtime delta, hidden chart table',
      start: '/reports?report=technician_utilization',
      key: 'technician_utilization' as ReportKey,
      data: reportData('technician_utilization', {
        summary: [
          {
            key: 'overtimeHours',
            label: 'Scheduled overtime',
            unit: 'hours',
            value: 6,
            previous: 4,
          },
          { key: 'utilization', label: 'Utilization', unit: 'percent', value: 82, previous: 70 },
        ],
        chart: {
          kind: 'technician_bars',
          series: [{ key: 'utilization', label: 'Utilization' }],
          points: [{ label: 'Ana', start: null, values: { utilization: 82 } }],
        },
        items: [
          row('t-1', {
            technician: 'Ana',
            branch: 'Main',
            availableMinutes: 4800,
            scheduledMinutes: 3900,
            utilization: 81.25,
            overtimeMinutes: 120,
            completedVisits: 9,
            onTime: null,
            links: { technician: 't-1' },
          }),
        ],
        total: 1,
        totals: { availableMinutes: 4800, utilization: 81.25 },
      }),
      expected: ['6h', '+50.0%', 'vs 70% previous', '81%', '80h', '65h', '—'],
      absent: [],
      links: { Ana: '/team/t-1/skills-availability' },
      plain: [],
      chartCell: 'Ana82%',
    },
  ])(
    '$name (FR-04 … FR-09, FR-11, FR-15, AC-04, AC-12, AC-24)',
    async ({
      start,
      key,
      data: payload,
      expected,
      absent,
      links,
      plain,
      agingDisabled,
      chartCell,
    }) => {
      await setup(start);
      data(key).flush(payload);
      await settle();
      const content = text();
      for (const fragment of expected) {
        expect(content, fragment).toContain(fragment);
      }
      for (const fragment of absent) {
        expect(content, fragment).not.toContain(fragment);
      }
      for (const [label, href] of Object.entries(links)) {
        expect(control(label)?.getAttribute('href'), label).toBe(href);
      }
      for (const label of plain) {
        expect(control(label), label).toBeUndefined();
      }
      if (agingDisabled) {
        expect(document.querySelector<HTMLInputElement>('#reports-compare')!.disabled).toBe(true);
        expect(document.querySelector('p-select')!.className).toMatch(/disabled/);
      }
      if (chartCell !== undefined) {
        const cells = document.querySelector('.chart__sr tbody')?.textContent?.replace(/\s+/g, '');
        expect(cells).toBe(chartCell);
      }
      if (key === 'job_profitability') {
        // Turning comparison off hides the deltas without a request.
        component().onCompare(false);
        await settle();
        httpTesting.expectNone(`${API}/reports/${key}`);
        expect(text()).not.toContain('+25.0%');
        expect(text()).not.toContain('vs 40.0% previous');
        expect(url()).toContain('compare=false');
      }
    },
  );
});
