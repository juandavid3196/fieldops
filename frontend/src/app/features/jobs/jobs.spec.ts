import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../core/config/api.config';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { SessionService } from '../../core/services/session.service';
import { JobRow, JobsOptions, JobsPage, JobsSummary } from './models/jobs.model';
import { WorkOrderDetail } from './models/work-order.model';
import { JobDetail } from './pages/job-detail/job-detail';
import { Jobs } from './pages/jobs/jobs';

const API = 'http://api.test';

@Component({ template: '<p>stub</p>' })
class Stub {}

const detail = (patch: Partial<WorkOrderDetail> = {}): WorkOrderDetail => ({
  id: 'wo-1',
  displayNumber: 'WO-7',
  status: 'ready_to_schedule',
  updatedAt: '2026-10-06T12:00:00Z',
  canManage: true,
  quote: { id: 'q-1', displayNumber: 'Q-2036', approvedTotal: 357.28, currency: 'USD' },
  customer: { id: 'c-1', name: 'Sofia Martinez' },
  propertyAddress: '1842 Oak Street',
  accessNote: null,
  jobType: 'recurring',
  category: { id: 'cat-1', name: 'Plumbing' },
  branch: { id: 'br-1', name: 'Austin Central', timezone: 'America/Chicago' },
  priority: 'high',
  estimatedDurationMinutes: 90,
  skills: [{ id: 'sk-1', name: 'Leak repair' }],
  recurrence: { frequency: 'monthly', count: 6 },
  preferredStart: '2026-10-20T14:00:00Z',
  preferredEnd: null,
  arrivalWindow: '09-12',
  preferredDate: '2026-10-20',
  tasks: [{ label: 'Confirm shutoff' }],
  materials: [
    {
      quoteLineId: null,
      catalogItemId: null,
      description: 'P-trap assembly',
      quantity: 1,
      unit: 'ea',
      source: 'warehouse',
    },
  ],
  instructions: 'Call before arriving',
  communication: {
    notifyCustomerWhenScheduled: true,
    sendTechnicianDetails: false,
    sendArrivalReminder: true,
  },
  visits: [{ visitNumber: 1, status: 'unscheduled' }],
  ...patch,
});

const options = (branches = 2): JobsOptions => ({
  branches: [
    { id: 'br-1', name: 'Central', timezone: 'America/Chicago' },
    { id: 'br-2', name: 'North', timezone: null },
  ].slice(0, branches),
  organizationTimezone: 'America/Chicago',
  canManage: true,
});

const summary: JobsSummary = {
  timezone: 'America/Chicago',
  activeJobs: { total: 12, inProgress: 4 },
  readyToSchedule: 3,
  today: { total: 7, assigned: 5, completed: 2 },
  needsAttention: 2,
};

const counts = { all: 24, active: 12, unscheduled: 3, completed: 9 };

const job = (patch: Partial<JobRow> = {}): JobRow => ({
  id: 'wo-6',
  displayNumber: 'WO-6',
  title: 'pintura casas',
  customerName: 'Marina Garcia',
  property: { name: 'Home', postalCode: '76968', city: 'Medellin' },
  branch: { id: 'br-2', name: 'North' },
  timezone: 'America/Chicago',
  status: 'completed',
  priority: 'high',
  currentVisit: {
    id: 'v-1',
    scheduledStart: '2026-10-09T13:30:00Z',
    scheduledEnd: '2026-10-09T15:30:00Z',
  },
  technician: { fullName: 'Manuel Merlano', initials: 'MM', colorHex: '#1abc9c' },
  updatedAt: '2026-10-09T13:30:00Z',
  createdAt: '2026-10-01T13:30:00Z',
  actions: ['view', 'open_in_schedule', 'review_completion'],
  ...patch,
});

const page = (items: JobRow[], total = items.length): JobsPage => ({ items, total, counts });

describe('Jobs workspace and detail', { timeout: 30_000 }, () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const control = (label: string): HTMLElement | undefined =>
    Array.from(document.body.querySelectorAll<HTMLElement>('button, a')).find(
      (candidate) => candidate.textContent?.replace(/\s+/g, ' ').trim() === label,
    );
  const url = (): string => TestBed.inject(Router).url;
  const list = (match: Record<string, string | null> = {}) =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/work-orders` &&
        Object.entries(match).every(([key, value]) => r.params.get(key) === value),
    );
  const summaryRequest = (branchId: string | null = null) =>
    httpTesting.expectOne(
      (r) => r.url === `${API}/work-orders/summary` && r.params.get('branchId') === branchId,
    );
  const flushOptions = (body: JobsOptions = options()): void => {
    httpTesting.expectOne(`${API}/work-orders/options`).flush(body);
  };
  const serverError = { status: 500, statusText: 'Server Error' };

  async function setup(role: string, startUrl: string): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'jobs', component: Jobs },
          { path: 'jobs/:id', component: JobDetail },
          { path: 'schedule', component: Stub },
          { path: 'invoices/review', component: Stub },
          { path: 'quotes/:quoteId', component: Stub },
          { path: 'quotes/:quoteId/work-order', component: Stub },
          { path: 'auth/sign-in', component: Stub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne(`${API}/sessions/current`).flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    TestBed.inject(Router).setUpLocationChangeListener();
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(startUrl);
  }

  afterEach(() => httpTesting?.verify());

  it('normalizes the URL before requesting, keeps the view in the URL and runs the header actions, search and Clear filters (FR-07, FR-08, AC-06, AC-09, AC-16, AC-17)', async () => {
    await setup('owner', '/jobs?tab=bogus&page=0&pageSize=7&sort=zzz&status=paused');
    // Neither summary nor list is requested until the options load and the URL is canonical.
    httpTesting.expectNone((r) => r.url !== `${API}/work-orders/options`);
    flushOptions();
    await settle();
    expect(url()).toBe('/jobs?status=paused');
    summaryRequest().flush(summary);
    list({ tab: 'all', sort: 'newest', page: '1', pageSize: '10', status: 'paused' }).flush(
      page([job()], 24),
    );
    await settle();
    expect(text()).toContain('Ready to schedule 3');
    expect(text()).toContain('4 in progress');
    expect(text()).toContain('5 assigned · 2 completed');
    expect(text()).toContain('All jobs 24');
    expect(text()).toContain('1–10 of 24');
    expect(control('Open schedule')?.getAttribute('href')).toBe('/schedule');
    expect(control('Clear filters 1')).toBeDefined();
    expect(document.querySelector('[role="status"]')?.textContent).toContain('24 jobs');

    // Ready to schedule: Unscheduled tab, status cleared, page 1; metrics are not reloaded.
    control('Ready to schedule 3')!.click();
    await settle();
    expect(url()).toBe('/jobs?tab=unscheduled');
    list({ tab: 'unscheduled', page: '1' }).flush({ items: [], total: 0, counts });
    await settle();
    expect(text()).toContain('No jobs match the current view.');

    // Search is debounced by 300 ms, resets the page and drives Clear filters.
    const input = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = '  plumb ';
    input.dispatchEvent(new Event('input'));
    httpTesting.expectNone((r) => r.url === `${API}/work-orders`);
    await vi.waitFor(() => expect(url()).toBe('/jobs?tab=unscheduled&q=plumb'), {
      timeout: 2000,
    });
    await settle();
    list({ tab: 'unscheduled', q: 'plumb' }).flush({ items: [], total: 0, counts });
    await settle();
    expect(control('Clear filters 1')).toBeDefined();
    control('Clear filters 1')!.click();
    await settle();
    expect(url()).toBe('/jobs?tab=unscheduled');
    list({ tab: 'unscheduled', q: null }).flush({ items: [], total: 0, counts });
    await settle();
    TestBed.resetTestingModule();

    // A foreign branch is reset to the default; a page past the last moves to the last page.
    await setup('dispatcher', '/jobs?branch=other&page=9');
    flushOptions();
    await settle();
    expect(url()).toBe('/jobs?page=9');
    summaryRequest().flush(summary);
    list({ page: '9', branchId: null }).flush({ items: [], total: 23, counts });
    await settle();
    expect(url()).toBe('/jobs?page=3');
    list({ page: '3' }).flush(page([job()], 23));
    await settle();
    expect(text()).toContain('21–23 of 23');
  });

  it('shows one row per job with status, schedule, technician and the server-built menu with BR-14 targets (FR-05, FR-06, AC-11, AC-13, AC-14)', async () => {
    await setup('viewer', '/jobs?branch=br-2&pageSize=25');
    flushOptions();
    await settle();
    summaryRequest('br-2').flush(summary);
    list({ branchId: 'br-2', pageSize: '25' }).flush(
      page([
        job(),
        job({
          id: 'wo-7',
          displayNumber: 'WO-7',
          status: 'ready_to_schedule',
          priority: 'normal',
          currentVisit: { id: 'v-2', scheduledStart: null, scheduledEnd: null },
          technician: null,
          property: { name: 'Main', postalCode: null, city: 'Medellin' },
          actions: ['view'],
        }),
      ]),
    );
    await settle();
    expect(control('Open schedule')?.getAttribute('href')).toBe('/schedule?branch=br-2');
    expect(text()).toContain('#WO-6pintura casas');
    expect(text()).toContain('Home · 76968 Medellin');
    expect(text()).toContain('Oct 9 · 8:30–10:30 AM');
    expect(text()).toContain('Manuel Merlano');
    expect(text()).toContain('Completed');
    expect(text()).toContain('Not scheduled');
    expect(text()).toContain('Unassigned');
    expect(document.querySelector('time')?.getAttribute('aria-label')).toContain('Oct 9, 2026');

    document.querySelector<HTMLButtonElement>('button[aria-label="Actions for #WO-6"]')!.click();
    await vi.waitFor(() => expect(document.querySelectorAll('[role="menuitem"]')).toHaveLength(3));
    const items = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'));
    expect(items.map((item) => item.textContent?.trim())).toEqual([
      'View job',
      'Open in schedule',
      'Review completion',
    ]);
    (items[1].querySelector<HTMLElement>('a') ?? items[1]).click();
    await vi.waitFor(() =>
      expect(url()).toBe('/schedule?branch=br-2&view=day&date=2026-10-09&visit=v-1'),
    );
  });

  it('renders forbidden, options failure, no branches, partial failures and retries with fixed copy (FR-08, AC-01, AC-18, AC-19)', async () => {
    // Roles without Read make no call.
    await setup('accounting', '/jobs');
    await settle();
    expect(text()).toContain("You don't have access to work orders.");
    TestBed.resetTestingModule();

    // Options failure: full-page error, neither summary nor list; Retry reloads options only.
    await setup('owner', '/jobs');
    httpTesting.expectOne(`${API}/work-orders/options`).flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load jobs. Try again.");
    control('Retry')!.click();
    flushOptions(options(0));
    await settle();
    expect(text()).toContain('No branches available.');
    TestBed.resetTestingModule();

    // Summary failure leaves the table working; the only branch is the default (no URL parameter).
    await setup('owner', '/jobs');
    flushOptions(options(1));
    await settle();
    summaryRequest('br-1').flush(null, serverError);
    list({ branchId: 'br-1' }).flush(page([], 0));
    await settle();
    expect(text()).toContain("We couldn't load job metrics. Try again.");
    expect(text()).toContain('No jobs yet. Jobs are created from approved quotes.');
    control('Retry')!.click();
    summaryRequest('br-1').flush(summary);
    await settle();
    expect(text()).toContain('Active jobs12');
    TestBed.resetTestingModule();

    // List failure keeps the metrics and retries only the list; a 403 shows the forbidden state.
    await setup('viewer', '/jobs');
    flushOptions();
    await settle();
    summaryRequest().flush(summary);
    list().flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load jobs. Try again.");
    expect(text()).toContain('Active jobs12');
    control('Retry')!.click();
    list().flush(null, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(text()).toContain("You don't have access to work orders.");
  });

  it('shows the read-only detail with Continue editing only for managers on a draft (FR-12, BR-22, AC-21, AC-22)', async () => {
    await setup('viewer', '/jobs/wo-1');
    httpTesting.expectOne(`${API}/work-orders/wo-1`).flush(
      detail({
        status: 'scheduled',
        visits: [
          { visitNumber: 1, status: 'scheduled' },
          { visitNumber: 2, status: 'unscheduled' },
        ],
      }),
    );
    await settle();
    expect(text()).toContain('#WO-7');
    expect(text()).toContain('Scheduled');
    expect(text()).toContain('Visit #1 · Scheduled');
    expect(text()).toContain('Visit #2 · Unscheduled');
    expect(text()).toContain('Monthly × 6');
    expect(text()).toContain('Plumbing');
    expect(text()).toContain('Austin Central');
    expect(text()).toContain('Leak repair');
    expect(text()).toContain('Oct 20, 2026 · 9:00 AM – 12:00 PM');
    expect(text()).toContain('P-trap assembly');
    expect(text()).toContain('Approved total $357.28');
    expect(control('Continue editing')).toBeUndefined();
    TestBed.resetTestingModule();

    // A manager sees Continue editing on a draft, which has no visits; a Viewer does not.
    await setup('dispatcher', '/jobs/wo-2');
    httpTesting
      .expectOne(`${API}/work-orders/wo-2`)
      .flush(detail({ id: 'wo-2', status: 'draft', visits: [], recurrence: null }));
    await settle();
    expect(text()).toContain('No visits yet.');
    expect(control('Continue editing')?.getAttribute('href')).toBe('/quotes/q-1/work-order');
    TestBed.resetTestingModule();

    await setup('viewer', '/jobs/wo-2');
    httpTesting
      .expectOne(`${API}/work-orders/wo-2`)
      .flush(detail({ id: 'wo-2', status: 'draft', visits: [], canManage: false }));
    await settle();
    expect(control('Continue editing')).toBeUndefined();
  });
});
