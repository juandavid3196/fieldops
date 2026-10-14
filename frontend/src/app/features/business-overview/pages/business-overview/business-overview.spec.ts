import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { todayInZone } from '../../../schedule/utils/schedule-range';
import { BusinessOptions, BusinessOverview } from '../../models/business-overview.model';
import { presetPeriod } from '../../utils/business-params';
import { BusinessOverviewPage } from './business-overview';

const API = 'http://api.test';
const ZONE = 'America/Chicago';

const OPTIONS: BusinessOptions = {
  timezone: ZONE,
  currency: 'USD',
  today: '2026-09-20',
  marginTarget: 40,
  branches: [
    { id: 'b-1', name: 'Austin Central' },
    { id: 'b-2', name: 'North Austin' },
  ],
};

const alert = (
  type: BusinessOverview['alerts'][number]['type'],
  values: Partial<BusinessOverview['alerts'][number]>,
): BusinessOverview['alerts'][number] => ({
  type,
  severity: 'warning',
  count: null,
  amount: null,
  branchId: null,
  categoryId: null,
  name: null,
  marginPercent: null,
  overtimeMinutes: null,
  previousOvertimeMinutes: null,
  changePercent: null,
  ...values,
});

const service = (id: number, margin: number): BusinessOverview['services'][number] => ({
  categoryId: `c-${id}`,
  name: `Service ${id}`,
  revenue: 10_000 - id * 100,
  grossProfit: 1000,
  marginPercent: margin,
  jobCount: 4,
  averageJobValue: 312,
});

const overview = (): BusinessOverview => {
  const { from, to } = presetPeriod('this_month', todayInZone(ZONE));
  return {
    from,
    to,
    previousFrom: from,
    previousTo: to,
    timezone: ZONE,
    currency: 'USD',
    marginTarget: 40,
    granularity: 'day',
    kpis: {
      revenue: { current: 128_450, previous: 114_300 },
      grossProfit: { current: 54_820, previous: 0 },
      grossMarginPercent: { current: 42.7, previous: 40 },
      quoteConversion: { accepted: 94, sent: 138, percent: 68.1 },
      outstanding: { amount: 18_760, overdueAmount: 4320, overdueCount: 7 },
      jobsCompleted: { current: 426, previous: 394 },
    },
    series: [
      {
        start: from,
        end: from,
        revenue: 5000,
        grossProfit: 2000,
        collected: 4000,
        previousRevenue: 4000,
        previousGrossProfit: 1500,
        previousCollected: 3000,
      },
      {
        start: to,
        end: to,
        revenue: 7000,
        grossProfit: 3000,
        collected: 6000,
        previousRevenue: 5000,
        previousGrossProfit: 2000,
        previousCollected: 4000,
      },
    ],
    cashFlow: {
      invoiced: 136_900,
      collected: 118_140,
      outstanding: 18_760,
      overdue: 4320,
      aging: { current: 9420, days1To30: 5610, days31To60: 2800, days60Plus: 930 },
    },
    pipeline: { requests: 182, quotesSent: 138, accepted: 94, jobsCreated: 89 },
    branches: [
      {
        branchId: 'b-1',
        name: 'Austin Central',
        isActive: true,
        revenue: 62_310,
        grossProfit: 27_000,
        marginPercent: 44.2,
        jobsCompleted: 208,
        onTimePercent: 93,
        rating: 4.8,
        reviewCount: 10,
      },
      {
        branchId: 'b-3',
        name: 'Round Rock',
        isActive: false,
        revenue: 24_920,
        grossProfit: 9000,
        marginPercent: 36.1,
        jobsCompleted: 72,
        onTimePercent: null,
        rating: null,
        reviewCount: 0,
      },
    ],
    branchTotals: { revenue: 87_230, marginPercent: 42.1, jobsCompleted: 280, onTimePercent: 91 },
    services: [
      service(1, 46.2),
      service(2, 28.6),
      service(3, 41),
      service(4, 41),
      service(5, 41),
      service(6, 41),
    ],
    team: {
      technicians: 18,
      utilizationPercent: 82,
      scheduledMinutes: 3840,
      availableMinutes: 4680,
      overtimeMinutes: 3840,
      previousOvertimeMinutes: 3252,
      next7DaysPercent: null,
    },
    alerts: [
      alert('overdue_invoices', { severity: 'critical', count: 7, amount: 4320 }),
      alert('branch_margin', { branchId: 'b-3', name: 'Round Rock', marginPercent: 36.1 }),
      alert('service_margin', { categoryId: 'c-2', name: 'Service 2', marginPercent: 28.6 }),
      alert('overtime', {
        overtimeMinutes: 3840,
        previousOvertimeMinutes: 3252,
        changePercent: 18,
      }),
    ],
  };
};

describe('Business overview page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let router: Router;
  let page: BusinessOverviewPage;

  const pending = (path: string): TestRequest[] =>
    httpTesting.match((r) => r.url === `${API}/business-overview${path}`);
  const one = (path: string, params: Record<string, string | null> = {}): TestRequest =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/business-overview${path}` &&
        Object.entries(params).every(([key, value]) => r.params.get(key) === value),
    );
  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string =>
    (harness.routeNativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const query = (): Record<string, string> => router.parseUrl(router.url).queryParams;
  const click = async (label: string): Promise<void> => {
    const button = Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll('button'),
    ).find((item) => item.textContent?.trim() === label);
    button?.click();
    await settle();
  };
  const fail = (request: TestRequest, status = 500): void =>
    request.flush(null, { status, statusText: 'Error' });

  async function setup(url = '/dashboard', role = 'owner'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'dashboard', component: BusinessOverviewPage }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne(`${API}/sessions/current`).flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl(url, BusinessOverviewPage);
    await settle();
  }

  async function load(url = '/dashboard'): Promise<void> {
    await setup(url);
    one('/options').flush(OPTIONS);
    await settle();
    one('').flush(overview());
    await settle();
  }

  afterEach(() => {
    httpTesting?.verify();
  });

  it('renders every card with BR-09/BR-11 markings, empties and alerts, and compares without refetching (AC-08, AC-11, AC-12, AC-14, AC-15)', async () => {
    await load();
    const period = presetPeriod('this_month', todayInZone(ZONE));
    expect(text()).toContain('Business Overview');
    expect(text()).toContain('Updated just now');
    expect(text()).toContain('· All branches');

    // KPIs: money in whole units, one-decimal margin, integer conversion, deltas only where defined.
    expect(text()).toContain('$128,450');
    expect(text()).toContain('+12.4%');
    expect(text()).toContain('+8.1%');
    expect(text()).toContain('42.7%');
    expect(text()).toContain('68%');
    expect(text()).toContain('94 accepted of 138 sent');
    expect(text()).toContain('$4,320 overdue');
    expect(text()).toContain('Estimated gross profit');
    expect(text()).toContain('Revenue (previous)');
    // Estimated profit's previous value is 0: its delta is hidden (2 deltas shown).
    expect(
      (harness.routeNativeElement as HTMLElement).querySelectorAll(
        'app-business-kpis app-delta-badge',
      ),
    ).toHaveLength(2);

    // Cash flow, pipeline, branches (inactive label, marking, Total), top 5 services, team.
    expect(text()).toContain('$136,900');
    expect(text()).toContain('60+ days');
    expect(text()).toContain('Quotes sent');
    expect(text()).toContain('Round Rock (inactive)');
    expect(text()).toContain('Below 40% target');
    expect(text()).toMatch(/Total\s*\$87,230\s*42\.1%/);
    expect(text()).toContain('Service 5');
    expect(text()).not.toContain('Service 6');
    expect(text()).toContain('Scheduled overtime 64h');
    expect(text()).toContain('18%');
    expect(text()).toContain('Next 7 days (scheduled) —');
    expect(text()).not.toContain('Open roles');

    // Owner attention: 3 of 4, expandable; disclaimer verbatim; no report button.
    expect(text()).toContain('7 overdue invoices');
    expect(text()).toContain('Totaling $4,320');
    expect(text()).toContain('Round Rock estimated margin below target');
    expect(text()).toContain('36.1% vs 40.0% target');
    expect(text()).not.toContain('Scheduled overtime up 18%');
    expect(text()).toContain('View all (4)');
    await click('View all (4)');
    expect(text()).toContain('Scheduled overtime up 18%');
    expect(text()).toContain('64h vs 54h in previous period');
    expect(text()).toContain('Show less');
    expect(text()).toContain(
      'Profitability figures are operational estimates based on configured product and service costs. They exclude overhead, payroll, payroll taxes, depreciation, and other accounting expenses.',
    );
    expect(text()).not.toContain('Job profitability report');
    expect(
      (harness.routeNativeElement as HTMLElement).querySelector(
        'a[href*="/invoices"][href*="overdue"]',
      ),
    ).not.toBeNull();

    // Comparison off hides deltas and previous lines with no request; the URL records it.
    page.onCompare(false);
    await settle();
    expect(query()['compare']).toBe('false');
    expect(pending('')).toHaveLength(0);
    expect(text()).not.toContain('+12.4%');
    expect(text()).not.toContain('Revenue (previous)');
    page.onCompare(true);
    await settle();
    expect(query()['compare']).toBeUndefined();
    expect(text()).toContain('+12.4%');

    // The overview request carries the resolved period and never the comparison flag.
    page.onRange('last_month');
    await settle();
    const request = one('');
    expect(request.request.params.get('from')).not.toBe(period.from);
    expect(request.request.params.has('compare')).toBe(false);
    request.flush(overview());
  });

  it('cancels in-flight requests on change, resets rejected values and shows the load, refresh and empty states (AC-05, AC-17, AC-18)', async () => {
    // Invalid range and unknown branch are replaced in the URL; a later change cancels the request.
    await setup('/dashboard?range=custom&from=2026-09-10&branch=b-9&compare=maybe');
    one('/options').flush(OPTIONS);
    await settle();
    expect(query()).toEqual({});
    const first = one('');
    page.onBranch('b-2');
    await settle();
    expect(first.cancelled).toBe(true);
    expect(query()['branch']).toBe('b-2');

    // A rejected branch (404) goes back to All and reloads the options.
    fail(one('', { branchId: 'b-2' }), 404);
    await settle();
    expect(query()['branch']).toBeUndefined();
    one('/options').flush(OPTIONS);
    await settle();
    one('').flush(overview());
    await settle();
    expect(text()).toContain('$128,450');

    // A failed refresh keeps the data and offers Try again.
    page['store'].refresh();
    fail(one(''));
    await settle();
    expect(text()).toContain("Couldn't refresh.");
    expect(text()).toContain('$128,450');
    await click('Try again');
    one('').flush(overview());
    await settle();
    expect(text()).not.toContain("Couldn't refresh.");

    // A load without data shows the page alert with the controls usable.
    page.onRange('this_year');
    await settle();
    fail(one(''));
    await settle();
    expect(text()).toContain("We couldn't load the business overview.");
    expect(text()).toContain('Export report');
    await click('Try again');
    one('').flush(overview());
    await settle();
    expect(text()).toContain('$128,450');
  });

  it('shows the options failure, the permission state and the no-branch state without overview requests (AC-03, AC-17, AC-18)', async () => {
    await setup();
    fail(one('/options'));
    await settle();
    expect(text()).toContain("We couldn't load the business overview.");
    expect(text()).toContain('Export report');
    expect(pending('')).toHaveLength(0);
    await click('Try again');
    one('/options').flush({ ...OPTIONS, branches: [] });
    await settle();
    expect(text()).toContain('No branches available.');
    expect(pending('')).toHaveLength(0);
    TestBed.resetTestingModule();

    await setup();
    fail(one('/options'), 403);
    await settle();
    expect(text()).toContain("You don't have access to Dashboard.");
    expect(pending('')).toHaveLength(0);
  });

  it('exports the current selection, shows a busy state and reports a failure (AC-16)', async () => {
    const urlApi = URL as unknown as Record<string, unknown>;
    const createObjectURL = vi.fn(() => 'blob:csv');
    urlApi['createObjectURL'] = createObjectURL;
    urlApi['revokeObjectURL'] = vi.fn();
    await load('/dashboard?compare=false');
    const period = presetPeriod('this_month', todayInZone(ZONE));

    await click('Export report');
    expect(page.exporting()).toBe(true);
    const download = one('/export', { from: period.from, to: period.to, compare: 'false' });
    expect(download.request.params.has('branchId')).toBe(false);
    download.flush(new Blob(['Section,Metric']), {
      headers: {
        'Content-Disposition': `attachment; filename="business-overview-${period.from}-to-${period.to}.csv"`,
      },
    });
    await settle();
    expect(page.exporting()).toBe(false);
    expect(createObjectURL).toHaveBeenCalledTimes(1);

    await click('Export report');
    fail(one('/export'), 500);
    await settle();
    expect(document.body.textContent).toContain("We couldn't export the report.");
  });
});
