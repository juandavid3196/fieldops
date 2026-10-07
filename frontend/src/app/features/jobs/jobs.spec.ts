import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../core/config/api.config';
import { errorInterceptor } from '../../core/interceptors/error.interceptor';
import { SessionService } from '../../core/services/session.service';
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

describe('Jobs list and detail', { timeout: 20_000 }, () => {
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
      (candidate) => candidate.textContent?.trim() === label,
    );

  async function setup(role: string, url: string): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'jobs', component: Jobs },
          { path: 'jobs/:id', component: JobDetail },
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
    await harness.navigateByUrl(url);
  }

  afterEach(() => httpTesting?.verify());

  it('shows the paged jobs table, the empty and forbidden states, and the read-only detail with Continue editing only for managers on a draft (FR-12, BR-21, BR-22, AC-20, AC-21, AC-22)', async () => {
    // Roles without Read see the forbidden state and make no call.
    await setup('accounting', '/jobs');
    await settle();
    expect(text()).toContain("You don't have access to work orders.");
    TestBed.resetTestingModule();

    // A Viewer reads the table: columns, statuses, and a row opens the detail.
    await setup('viewer', '/jobs');
    const list = httpTesting.expectOne(
      (r) => r.url === `${API}/work-orders` && r.params.get('pageSize') === '25',
    );
    expect(list.request.params.get('page')).toBe('1');
    list.flush({
      items: [
        {
          id: 'wo-1',
          displayNumber: 'WO-7',
          title: 'Kitchen sink leak repair',
          customerName: 'Sofia Martinez',
          branchName: 'Austin Central',
          priority: 'high',
          status: 'ready_to_schedule',
          createdAt: '2026-10-06T12:00:00Z',
        },
        {
          id: 'wo-2',
          displayNumber: 'WO-8',
          title: 'Water heater',
          customerName: 'Ana Lopez',
          branchName: 'Austin North',
          priority: 'normal',
          status: 'draft',
          createdAt: '2026-10-05T12:00:00Z',
        },
      ],
      total: 2,
    });
    await settle();
    expect(Array.from(document.querySelectorAll('th'), (cell) => cell.textContent?.trim())).toEqual(
      ['Job', 'Customer', 'Branch', 'Priority', 'Status', 'Created'],
    );
    expect(text()).toContain('#WO-7 Kitchen sink leak repair');
    expect(text()).toContain('Unscheduled');
    expect(text()).toContain('Draft');
    expect(text()).toContain('Oct 6, 2026');
    document.querySelector<HTMLElement>('tbody tr')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/jobs/wo-1'));
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

    // Read-only detail of a scheduled order (BR-16): aggregate status and per-visit labels.
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
    TestBed.resetTestingModule();

    // Empty organization.
    await setup('owner', '/jobs');
    httpTesting.expectOne((r) => r.url === `${API}/work-orders`).flush({ items: [], total: 0 });
    await settle();
    expect(text()).toContain('No jobs yet. Jobs are created from approved quotes.');
  });
});
