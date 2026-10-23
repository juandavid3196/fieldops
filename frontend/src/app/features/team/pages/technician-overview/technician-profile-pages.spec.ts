import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { JobHistoryItem, JobHistoryResponse } from '../../models/team-job-history.model';
import { TechnicianDetail } from '../../models/team.model';
import { technicianProfileRedirectRoute } from '../../team.routes';
import { TechnicianJobHistory } from '../technician-job-history/technician-job-history';
import { TechnicianOverview } from './technician-overview';

const API = 'http://api.test';

@Component({ template: '<p>stub</p>' })
class Stub {}

const windows = [{ start: '07:00:00', end: '16:00:00' }];
const FULL: TechnicianDetail = {
  id: 't-1',
  firstName: 'Ana',
  lastName: 'López',
  email: 'ana@example.com',
  phone: '555-0101',
  employeeCode: 'TECH-014',
  notes: 'Prefers mornings',
  branch: { id: 'b-1', name: 'Austin Central' },
  profileStatus: 'active',
  account: { email: 'ana@fieldops.com', roleName: 'Technician' },
  skills: [
    { name: 'Plumbing', proficiency: 4, isPrimary: false },
    { name: 'HVAC', proficiency: 5, isPrimary: true },
  ],
  availability: [1, 2, 3, 4].map((dayOfWeek) => ({ dayOfWeek, windows })),
  today: {
    todayStatus: 'on_job',
    currentJob: { label: 'JOB-0834 – Water heater', visitStatus: 'paused' },
    jobs: 4,
    workloadPercent: 110,
    workloadState: 'percent',
    nextAvailable: { kind: 'now' },
  },
  timezone: 'America/Chicago',
  usesBranchHours: true,
  upcomingTimeOff: { date: '2026-11-03', allDay: true, reason: 'Vacation' },
};
const EMPTY: TechnicianDetail = {
  ...FULL,
  email: null,
  phone: null,
  employeeCode: null,
  account: null,
  skills: [],
  availability: [],
  usesBranchHours: false,
  upcomingTimeOff: null,
  today: {
    todayStatus: 'available',
    currentJob: null,
    jobs: 0,
    workloadState: 'no_availability',
    nextAvailable: { kind: 'none' },
  },
};
const INACTIVE: TechnicianDetail = {
  ...FULL,
  profileStatus: 'suspended',
  today: { ...FULL.today, todayStatus: 'suspended' },
};

const item = (n: number, patch: Partial<JobHistoryItem> = {}): JobHistoryItem => ({
  visitId: `v-${n}`,
  workOrderId: `w-${n}`,
  displayNumber: `JOB-08${n}`,
  title: `Title ${n}`,
  visitNumber: 1,
  visitCount: 1,
  property: { name: 'Maple Court', city: 'Austin' },
  participationDate: '2026-03-05',
  loggedMinutes: 135,
  outcome: 'completed',
  canOpen: true,
  ...patch,
});
const history = (
  items: readonly JobHistoryItem[],
  total = items.length,
  page = 1,
  pageSize = 10,
): JobHistoryResponse => ({
  timezone: 'America/Chicago',
  period: { from: '2026-01-01', to: '2026-04-01' },
  summary: { completedJobs: 7, onTimePercent: 86, loggedMinutes: 600, avgDurationMinutes: null },
  items,
  total,
  page,
  pageSize,
});

describe('Technician profile pages', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  const detailCall = (): TestRequest =>
    httpTesting.expectOne((r) => r.url === `${API}/team/technicians/t-1`);
  const historyCall = (): TestRequest =>
    httpTesting.expectOne((r) => r.url === `${API}/team/technicians/t-1/job-history`);
  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string =>
    (harness.routeNativeElement as HTMLElement).innerHTML
      .replace(/<[^>]+>/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  const links = () => Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll('a'));
  const href = (label: string): string | null | undefined =>
    links()
      .find((link) => link.textContent?.trim().startsWith(label))
      ?.getAttribute('href');

  async function setup(role: string): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          technicianProfileRedirectRoute,
          { path: 'team/:technicianId/overview', component: TechnicianOverview },
          { path: 'team/:technicianId/job-history', component: TechnicianJobHistory },
          { path: 'team', component: Stub },
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
        email: 'a@acme.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    harness = await RouterTestingHarness.create();
  }

  afterEach(() => httpTesting.verify());

  it('redirects /team/:id to Overview with the shared header, tabs and links (FR-03, AC-03, AC-06, AC-07, AC-09)', async () => {
    await setup('dispatcher');
    await harness.navigateByUrl('/team/t-1');
    expect(TestBed.inject(Router).url).toBe('/team/t-1/overview');
    detailCall().flush(FULL);
    await settle();

    expect(text()).toContain('Ana López');
    expect(text()).toContain('Active');
    expect(text()).toContain('Home branch: Austin Central');
    const tabs = (harness.routeNativeElement as HTMLElement).querySelector(
      'nav[aria-label="Technician sections"]',
    );
    const current = tabs?.querySelectorAll('[aria-current="page"]');
    expect(current?.length).toBe(1);
    expect(current?.[0].textContent?.trim()).toBe('Overview');
    expect(href('Overview')).toBe('/team/t-1/overview');
    expect(href('Schedule')).toBe('/schedule');
    expect(href('Skills & availability')).toBe('/team/t-1/skills-availability');
    expect(href('Job history')).toBe('/team/t-1/job-history');
    expect(href('Team')).toBe('/team');
    expect(href('View schedule')).toBe('/schedule');
    expect(href('Manage availability')).toBe('/team/t-1/skills-availability');
    const skills = links().find((link) => link.classList.contains('to__block-link'));
    expect(skills?.getAttribute('aria-label')).toBe(
      'Operational skills, open Skills & availability',
    );
    expect(skills?.getAttribute('href')).toBe('/team/t-1/skills-availability');
    // Read-only: no edit, link or Settings actions.
    for (const hidden of ['Edit', 'Link account', 'Unlink account', 'View access in Settings']) {
      expect(links().some((link) => link.textContent?.includes(hidden))).toBe(false);
    }
  });

  it.each([
    [
      'full profile on a job',
      FULL,
      [
        'Current job JOB-0834 – Water heater',
        'Jobs today 4',
        'Workload 110%',
        'Next available Now',
        'Paused',
        'On job',
        'Linked account ana@fieldops.com Read-only',
        'Role Technician',
        'Account permissions are managed in Settings.',
        'HVAC · Expert Primary',
        'Plumbing · Advanced',
        'Monday – Thursday 7:00 AM – 4:00 PM',
        'Friday Off',
        'Uses branch business hours',
        'Tue, Nov 3, 2026',
        'All day',
        'Vacation',
      ],
      ['Prefers mornings'],
    ],
    [
      'empty profile',
      EMPTY,
      [
        'Current job None',
        'Jobs today 0',
        'Workload No availability',
        'Next available —',
        'Email —',
        'Mobile phone —',
        'Profile ID —',
        'No linked user account',
        'No skills assigned yet.',
        'No availability set',
        'No upcoming time off',
      ],
      ['Read-only', 'Uses branch business hours'],
    ],
    [
      'suspended profile',
      INACTIVE,
      ['Current job None', 'Jobs today —', 'Workload —', 'Next available —', 'Suspended'],
      ['JOB-0834', 'Paused', '110%'],
    ],
  ])(
    'renders the Overview for a %s (FR-04, FR-05, AC-05, AC-07, AC-08, AC-10)',
    async (_n, detail, shown, hidden) => {
      await setup('owner');
      await harness.navigateByUrl('/team/t-1/overview');
      detailCall().flush(detail);
      await settle();
      for (const expected of shown) {
        expect(text(), expected).toContain(expected);
      }
      for (const unexpected of hidden) {
        expect(text(), unexpected).not.toContain(unexpected);
      }
    },
  );

  it('covers forbidden (no requests), 404 not-available and failure with Retry (FR-11, AC-17, AC-18, AC-20)', async () => {
    for (const role of ['technician', 'accounting', 'viewer']) {
      await setup(role);
      await harness.navigateByUrl('/team/t-1/overview');
      expect(text()).toContain("You don't have access to Team.");
      await harness.navigateByUrl('/team/t-1/job-history');
      expect(text()).toContain("You don't have access to Team.");
      // httpTesting.verify() proves no technician request was sent.
      httpTesting.verify();
      TestBed.resetTestingModule();
    }

    await setup('owner');
    await harness.navigateByUrl('/team/t-1/overview');
    detailCall().flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load this technician.");
    const retry = Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim() === 'Retry');
    retry?.click();
    detailCall().flush({}, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain("This technician profile isn't available.");
    expect(href('Back to Team')).toBe('/team');
  });

  it('keeps Job history state in the URL, resets the page and links only openable jobs (FR-08, FR-09, AC-16, AC-19)', async () => {
    await setup('owner');
    const router = TestBed.inject(Router);
    await harness.navigateByUrl(
      '/team/t-1/job-history?q=roof&status=completed&period=12m&sort=oldest&page=3&pageSize=25',
    );
    detailCall().flush(FULL);
    const first = historyCall();
    expect(first.request.params.get('search')).toBe('roof');
    expect(first.request.params.get('status')).toBe('completed');
    expect(first.request.params.get('period')).toBe('12m');
    expect(first.request.params.get('sort')).toBe('oldest');
    expect(first.request.params.get('page')).toBe('3');
    expect(first.request.params.get('pageSize')).toBe('25');
    first.flush(
      history(
        [
          item(1, { visitCount: 2, visitNumber: 2 }),
          item(2, { canOpen: false, outcome: 'reassigned', loggedMinutes: 0 }),
        ],
        120,
        3,
        25,
      ),
    );
    await settle();

    expect(text()).toContain('Showing 51–75 of 120 jobs');
    expect(text()).toContain('Completed jobs 7');
    expect(text()).toContain('On-time 86%');
    expect(text()).toContain('Hours logged 10h 0m');
    expect(text()).toContain('Avg. duration —');
    expect(text()).toContain('Visit 2');
    expect(text()).toContain('Mar 5, 2026');
    expect(text()).toContain('2h 15m');
    expect(text()).toContain('Maple Court · Austin');
    expect(href('JOB-081')).toBe('/jobs/w-1');
    expect(href('JOB-082')).toBeUndefined();
    const rowLinks = links().filter((link) => link.getAttribute('href')?.startsWith('/jobs/'));
    expect(rowLinks.length).toBe(2); // job number and chevron of the openable row only
    expect(text()).toContain('Reassigned');
    expect(href('Job history')).toBe('/team/t-1/job-history');

    // A filter change resets the page and requests the first page again.
    const api = harness.routeDebugElement?.componentInstance as unknown as {
      onStatus(value: string): void;
    };
    api.onStatus('cancelled');
    await settle();
    const changed = historyCall();
    expect(changed.request.params.get('status')).toBe('cancelled');
    expect(changed.request.params.get('page')).toBe('1');
    expect(router.url).toContain('status=cancelled');
    expect(router.url).not.toContain('page=');
    changed.flush(history([item(3)], 1, 1, 25));
    await settle();

    // Invalid values are replaced by defaults before any request.
    await harness.navigateByUrl('/team/t-1/job-history?status=bogus&pageSize=7&page=0&period=1y');
    await settle();
    const defaults = historyCall();
    expect(defaults.request.params.get('status')).toBe('all');
    expect(defaults.request.params.get('period')).toBe('90d');
    expect(defaults.request.params.get('page')).toBe('1');
    expect(defaults.request.params.get('pageSize')).toBe('10');
    expect(router.url).toBe('/team/t-1/job-history');
    // A page past the last requests the last page.
    defaults.flush(history([item(5)], 12, 1, 10));
    await settle();
    await harness.navigateByUrl('/team/t-1/job-history?page=5');
    await settle();
    historyCall().flush(history([], 12, 5, 10));
    await settle();
    const last = historyCall();
    expect(last.request.params.get('page')).toBe('2');
    expect(router.url).toBe('/team/t-1/job-history?page=2');
    last.flush(history([item(4)], 12, 2, 10));
  });

  it('renders the empty, filtered-empty and error states, keeping the header and period (FR-11, AC-20)', async () => {
    await setup('owner');
    await harness.navigateByUrl('/team/t-1/job-history?period=all');
    detailCall().flush(FULL);
    historyCall().flush(history([]));
    await settle();
    expect(text()).toContain('This technician has no job history yet.');
    expect(text()).toContain('Showing 0–0 of 0 jobs');
    expect(links().some((link) => link.textContent?.includes('Clear filters'))).toBe(false);

    await harness.navigateByUrl('/team/t-1/job-history?period=30d');
    await settle();
    historyCall().flush(history([]));
    await settle();
    expect(text()).toContain('No jobs in this period.');

    await harness.navigateByUrl('/team/t-1/job-history?period=30d&q=zzz&status=cancelled');
    await settle();
    historyCall().flush(history([]));
    await settle();
    expect(text()).toContain('No jobs match these filters.');
    const buttons = () =>
      Array.from((harness.routeNativeElement as HTMLElement).querySelectorAll('button'));
    buttons()
      .find((button) => button.textContent?.trim() === 'Clear filters')
      ?.click();
    await settle();
    const cleared = historyCall();
    expect(cleared.request.params.get('period')).toBe('30d');
    expect(cleared.request.params.has('search')).toBe(false);
    expect(cleared.request.params.get('status')).toBe('all');
    cleared.flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load job history.");
    expect(text()).toContain('Ana López');
    buttons()
      .find((button) => button.textContent?.trim() === 'Retry')
      ?.click();
    historyCall().flush(history([item(1)]));
    await settle();
    expect(text()).toContain('JOB-081');
  });
});
