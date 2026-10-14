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
import {
  DashboardOptions,
  DashboardResponse,
  PerformanceResponse,
} from '../../models/dashboard.model';
import { DashboardPage } from './dashboard';

const API = 'http://api.test';
const ZONE = 'America/Chicago';

const OPTIONS: DashboardOptions = {
  branches: [
    { id: 'b-1', name: 'Austin Central', timezone: ZONE },
    { id: 'b-2', name: 'Denver', timezone: null },
  ],
  organizationTimezone: ZONE,
  technicians: [
    { id: 't-1', fullName: 'Carlos Rivera', branchId: 'b-1' },
    { id: 't-2', fullName: 'Ana Lopez', branchId: 'b-1' },
  ],
  categories: [{ id: 'c-1', name: 'Plumbing' }],
};

const dashboard = (date: string): DashboardResponse => ({
  timezone: ZONE,
  date,
  reference: `${date}T17:00:00Z`,
  kpis: {
    jobs: { total: 24, completed: 18, active: 4, scheduled: 2 },
    unassigned: 3,
    atRiskOrDelayed: 4,
    utilization: { scheduledMinutes: 1200, availableMinutes: 1500, percent: 80 },
    onTimeArrival: { percent: 91, eligible: 11, previousPercent: 88 },
  },
  visits: [
    {
      visitId: 'v-1',
      workOrderId: 'w-1',
      displayNumber: 'WO-1056',
      title: 'Toilet replacement',
      customerName: 'Jennifer Walsh',
      customerPhone: null,
      branchId: 'b-1',
      status: 'assigned',
      statusGroup: 'scheduled',
      isDelayed: true,
      delayMinutes: 38,
      isAtRisk: false,
      scheduledStart: `${date}T16:00:00Z`,
      scheduledEnd: `${date}T18:00:00Z`,
      actualCompletedAt: null,
      projectedEnd: null,
      technician: { id: 't-1', fullName: 'Carlos Rivera' },
      otherTechnicianCount: 0,
    },
  ],
  attention: [
    {
      type: 'delayed',
      visitId: 'v-1',
      workOrderId: 'w-1',
      displayNumber: 'WO-1056',
      title: 'Toilet replacement',
      visitStatus: 'assigned',
      branchId: 'b-1',
      at: `${date}T16:00:00Z`,
      minutes: 38,
      technician: null,
    },
  ],
  technicians: [
    {
      id: 't-1',
      fullName: 'Carlos Rivera',
      initials: 'CR',
      colorHex: null,
      skills: ['Plumbing'],
      extraSkillCount: 0,
      currentStatus: 'on_job',
      scheduledMinutes: 400,
      availableMinutes: 480,
      percent: 83,
      nextAvailable: `${date}T20:30:00Z`,
    },
  ],
  durations: {
    visitCount: 2,
    categories: [
      {
        categoryId: 'c-1',
        name: 'Plumbing',
        visitCount: 2,
        estimatedMinutes: 100,
        actualMinutes: 108,
      },
    ],
  },
  activity: [],
});

const PERFORMANCE: PerformanceResponse = {
  from: '2026-09-14',
  to: '2026-09-20',
  jobsCompleted: { completed: 18, total: 24 },
  firstTimeFix: { fixed: 9, total: 10 },
  rating: { average: 4.8, count: 5 },
  daily: [
    { date: '2026-09-19', completed: 3 },
    { date: '2026-09-20', completed: 5 },
  ],
};

describe('Dashboard page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let router: Router;
  let visibility: 'visible' | 'hidden' = 'visible';

  const today = (): string => todayInZone(ZONE);
  const pending = (path: string): TestRequest[] =>
    httpTesting.match((r) => r.url === `${API}/operations-dashboard${path}`);
  const one = (path: string, params: Record<string, string | null> = {}): TestRequest =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/operations-dashboard${path}` &&
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
  const fail = (request: TestRequest, status = 500): void =>
    request.flush(null, { status, statusText: 'Error' });

  async function setup(role: string, url = '/dashboard'): Promise<DashboardPage> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: 'dashboard', component: DashboardPage }]),
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
    const page = await harness.navigateByUrl(url, DashboardPage);
    await settle();
    return page;
  }

  /** Loads options, then answers the first dashboard and performance requests. */
  async function load(role = 'dispatcher', url = '/dashboard'): Promise<void> {
    await setup(role, url);
    one('/options').flush(OPTIONS);
    await settle();
    one('', { date: today() }).flush(dashboard(today()));
    one('/performance', { period: '7' }).flush(PERFORMANCE);
    await settle();
  }

  beforeEach(() => {
    visibility = 'visible';
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => visibility,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    httpTesting?.verify();
  });

  it.each([
    ['technician', null, "You don't have access to Dashboard."],
    [
      'dispatcher',
      { branches: [], organizationTimezone: ZONE, technicians: [], categories: [] },
      'No branches available.',
    ],
    ['dispatcher', 403, "You don't have access to Dashboard."],
    ['dispatcher', 500, "We couldn't load the dashboard."],
  ])(
    'shows the %s permission/empty/error state without dashboard data requests (AC-01, AC-05)',
    async (role, options, expected) => {
      await setup(role);
      if (options !== null) {
        const request = one('/options');
        if (typeof options === 'number') {
          fail(request, options);
        } else {
          request.flush(options);
        }
        await settle();
      }
      expect(text()).toContain(expected);
      expect(pending('')).toHaveLength(0);
      expect(pending('/performance')).toHaveLength(0);
    },
  );

  it('keeps the state in the URL, resets invalid values and cancels in-flight requests on change (AC-04, AC-11, AC-16)', async () => {
    const page = await setup(
      'dispatcher',
      '/dashboard?branch=b-9&technician=t-9&category=c-9&date=2001-01-01&period=5',
    );
    one('/options').flush(OPTIONS);
    await settle();

    // Invalid branch, technician, category, date and period resolve to the defaults (replaceUrl).
    expect(query()).toEqual({ date: today() });
    one('', { date: today() }).flush(dashboard(today()));
    one('/performance', { period: '7' }).flush(PERFORMANCE);
    await settle();
    expect(text()).toContain('Operations Dashboard');
    expect(text()).toContain('· All branches');
    expect(text()).toContain("Today's jobs");
    expect(text()).toContain('Updated just now');
    expect(text()).toContain('WO-1056 delayed by 38 min');
    expect(text()).toContain('Reassign');

    // A change reloads with the filters applied; the next change cancels the in-flight request.
    await harness.navigateByUrl(
      `/dashboard?branch=b-1&technician=t-1&category=c-1&date=${today()}`,
    );
    await settle();
    expect(page['filterCount']()).toBe(2);
    const first = one('', { branchId: 'b-1', technicianId: 't-1', categoryId: 'c-1' });
    const firstPerformance = one('/performance', { categoryId: 'c-1' });
    await harness.navigateByUrl(`/dashboard?branch=b-1&technician=t-2&date=${today()}&period=30`);
    await settle();
    expect(first.cancelled).toBe(true);
    expect(firstPerformance.cancelled).toBe(true);
    one('', { technicianId: 't-2' }).flush(dashboard(today()));
    one('/performance', { technicianId: 't-2', period: '30' }).flush(PERFORMANCE);
    await settle();

    // A rejected technician (404) goes back to All through the URL.
    await harness.navigateByUrl(`/dashboard?branch=b-1&technician=t-1&date=${today()}`);
    await settle();
    fail(one('', { technicianId: 't-1' }), 404);
    one('/performance', { technicianId: 't-1' }).flush(PERFORMANCE);
    await settle();
    expect(query()['technician']).toBeUndefined();
    one('', { branchId: 'b-1' }).flush(dashboard(today()));
    one('/performance', { branchId: 'b-1' }).flush(PERFORMANCE);
  });

  it('refreshes every 60 s while visible, keeps data on a failed refresh and recovers from a first-load error (AC-17, AC-18)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    await load();

    // A hidden tab does not tick; returning within 60 s does not reload.
    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
    vi.advanceTimersByTime(30_000);
    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
    expect(pending('')).toHaveLength(0);

    // The 60 s tick refreshes silently; a failure keeps the cards and offers Try again.
    vi.advanceTimersByTime(30_000);
    fail(one('', { date: today() }));
    one('/performance').flush(PERFORMANCE);
    await settle();
    expect(text()).toContain("Couldn't refresh.");
    expect(text()).toContain('WO-1056');
    expect(text()).not.toContain('Updated just now');

    // The next tick retries and a success clears the notice.
    vi.advanceTimersByTime(60_000);
    one('', { date: today() }).flush(dashboard(today()));
    one('/performance').flush(PERFORMANCE);
    await settle();
    expect(text()).not.toContain("Couldn't refresh.");
    expect(text()).toContain('Updated just now');

    // Hidden for more than 60 s: no requests while hidden, one reload when visible again.
    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
    vi.advanceTimersByTime(61_000);
    expect(pending('')).toHaveLength(0);
    visibility = 'visible';
    document.dispatchEvent(new Event('visibilitychange'));
    one('', { date: today() }).flush(dashboard(today()));
    one('/performance').flush(PERFORMANCE);
    await settle();

    // A load without data shows the page alert with the controls kept; performance fails alone.
    await harness.navigateByUrl(`/dashboard?branch=b-2&date=${today()}`);
    await settle();
    fail(one('', { branchId: 'b-2' }));
    one('/performance', { branchId: 'b-2' }).flush(PERFORMANCE);
    await settle();
    expect(text()).toContain("We couldn't load the dashboard.");
    expect(text()).toContain('Branch');
    const retry = Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim() === 'Try again');
    retry?.click();
    await settle();
    one('', { branchId: 'b-2' }).flush(dashboard(today()));
    fail(one('/performance', { branchId: 'b-2' }));
    await settle();
    expect(text()).toContain("We couldn't load performance.");
    expect(text()).toContain('WO-1056');
  });
});
