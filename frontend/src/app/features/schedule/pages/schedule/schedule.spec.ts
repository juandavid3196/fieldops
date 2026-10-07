import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { By } from '@angular/platform-browser';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Confirmation, ConfirmationService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { DispatchDrawer } from '../../components/dispatch-drawer/dispatch-drawer';
import {
  CalendarResponse,
  DispatchOptions,
  UnscheduledResponse,
  VisitDispatchDetail,
} from '../../models/schedule.model';
import { scheduleUnsavedChangesGuard } from '../../guards/schedule-unsaved-changes.guard';
import { SchedulePage } from './schedule';

const API = 'http://api.test';

const OPTIONS: DispatchOptions = {
  branches: [
    { id: 'b-1', name: 'Austin Central', timezone: 'America/Chicago', isMain: true },
    { id: 'b-2', name: 'Denver', timezone: 'America/Denver', isMain: false },
  ],
  defaultBranchId: 'b-1',
  skills: [{ id: 's-1', name: 'Plumbing' }],
};
const CALENDAR: CalendarResponse = {
  timezone: 'America/Chicago',
  from: '2026-09-21',
  to: '2026-09-27',
  days: [
    '2026-09-21',
    '2026-09-22',
    '2026-09-23',
    '2026-09-24',
    '2026-09-25',
    '2026-09-26',
    '2026-09-27',
  ],
  technicians: [
    {
      id: 't-1',
      name: 'Carlos Rivera',
      initials: 'CR',
      colorHex: null,
      primarySkill: 'Plumbing',
      tag: null,
      load: { scheduledMinutes: 180, availableMinutes: 480, state: 'percent' },
      days: [],
      assessments: [],
    },
  ],
  visits: [
    {
      visitId: 'v-9',
      workOrderId: 'w-9',
      displayNumber: 'WO-3090',
      title: 'General repairs',
      street: 'Cedar Park Dr',
      status: 'assigned',
      start: '2026-09-22T14:00:00Z',
      end: '2026-09-22T16:00:00Z',
      technicianIds: ['t-1'],
      primaryTechnicianId: 't-1',
      conflicts: [{ technicianId: 't-1', code: 'overlap', from: null, to: null, label: 'Overlap' }],
    },
  ],
};
const UNSCHEDULED: UnscheduledResponse = {
  total: 2,
  items: ['v-1', 'v-2'].map((visitId, index) => ({
    visitId,
    visitNumber: 1,
    recurrenceCount: null,
    workOrderId: `w-${index}`,
    displayNumber: `WO-309${index}`,
    title: `Job ${index}`,
    priority: 'high' as const,
    estimatedDurationMinutes: 120,
    customerName: 'Sofia Martinez',
    address: '1842 Oak Street',
    preferredStart: '2026-09-23T14:00:00Z',
    preferredEnd: '2026-09-23T17:00:00Z',
    isOverdue: index === 0,
    skills: [],
  })),
};
const detail = (visitId: string): VisitDispatchDetail => ({
  visitId,
  visitNumber: 1,
  recurrence: null,
  status: 'unscheduled',
  updatedAt: '2026-10-06T10:00:00Z',
  canManage: true,
  isLocked: false,
  timezone: 'America/Chicago',
  workOrder: {
    id: 'w-1',
    displayNumber: 'WO-3091',
    title: 'Sink',
    priority: 'high',
    estimatedDurationMinutes: 120,
    requiredSkills: [],
    plannedMaterialsCount: 0,
  },
  customer: { name: 'Sofia', initials: 'S', phone: null, address: 'Austin', hasEmail: false },
  values: {
    date: '2026-12-01',
    start: '09:00',
    end: '10:00',
    arrivalWindow: 'start_plus_2h',
    technicianIds: [],
    primaryTechnicianId: null,
    dispatchNote: null,
    notifyCustomer: false,
    sendTechnicianDetails: false,
  },
  technicians: [],
});

describe('Schedule page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: SchedulePage;

  const call = (method: string, path: string, params?: Record<string, string>): TestRequest =>
    httpTesting.expectOne(
      (r) =>
        r.method === method &&
        r.url === `${API}/dispatch/${path}` &&
        Object.entries(params ?? {}).every(([key, value]) => r.params.get(key) === value),
    );
  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string =>
    (harness.routeNativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ').trim() ?? '';

  async function setup(roleCode: string, url = '/schedule'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: 'schedule',
            component: SchedulePage,
            canDeactivate: [scheduleUnsavedChangesGuard],
          },
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
      role: { code: roleCode, name: roleCode },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, SchedulePage).then((component) => (page = component));
    await settle();
  }

  afterEach(() => httpTesting?.verify());

  it.each(['technician', 'accounting', 'unknown_role'])(
    'shows the forbidden state with no requests for %s (AC-01)',
    async (role) => {
      await setup(role);
      expect(text()).toContain("You don't have access to Schedule.");
      expect(harness.routeNativeElement?.querySelector('app-schedule-calendar')).toBeNull();
    },
  );

  it('keeps branch, view and date in the URL, falls back on a 404 branch and reloads on filters (AC-03, AC-04)', async () => {
    await setup('viewer');
    call('GET', 'options').flush(OPTIONS);
    await settle();

    // Missing query values are normalized into the URL (replaceUrl) and drive the requests.
    const url = TestBed.inject(Router).parseUrl(TestBed.inject(Router).url);
    expect(url.queryParams['branch']).toBe('b-1');
    expect(url.queryParams['view']).toBe('week');
    expect(url.queryParams['date']).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    call('GET', 'calendar', { branchId: 'b-1', view: 'week', status: 'all' }).flush(CALENDAR);
    call('GET', 'unscheduled', { branchId: 'b-1', filter: 'all', pageSize: '25' }).flush(
      UNSCHEDULED,
    );
    await settle();
    expect(text()).toContain('Dispatch calendar');
    expect(text()).toContain('Carlos Rivera');
    expect(text()).toContain('3h / 8h');
    expect(text()).toContain('(Conflict)');
    expect(text()).toContain('Unassigned');

    // Filters reload the calendar with the filter applied; the load figures come from the server.
    page['store'].setSkill('s-1');
    call('GET', 'calendar', { skillId: 's-1' }).flush(CALENDAR);
    call('GET', 'unscheduled', { skillId: 's-1' }).flush(UNSCHEDULED);
    await settle();

    // Navigation writes the date to the URL.
    page.onShift(1);
    await settle();
    expect(TestBed.inject(Router).url).toContain('view=week');
    call('GET', 'calendar').flush(CALENDAR);
    await settle();

    // An out-of-scope branch in the URL resolves to the default branch with no extra prompts.
    await harness.navigateByUrl('/schedule?branch=b-9&view=day&date=2026-09-23');
    await settle();
    call('GET', 'calendar', { branchId: 'b-1', view: 'day' }).flush(CALENDAR);
    await settle();

    // A 404 for the current branch falls back to the default branch.
    await harness.navigateByUrl('/schedule?branch=b-2&view=week&date=2026-09-23');
    await settle();
    call('GET', 'calendar', { branchId: 'b-2' }).flush(null, {
      status: 404,
      statusText: 'Not Found',
    });
    call('GET', 'unscheduled', { branchId: 'b-2' }).flush(UNSCHEDULED);
    await settle();
    call('GET', 'calendar', { branchId: 'b-1' }).flush(CALENDAR);
    call('GET', 'unscheduled', { branchId: 'b-1' }).flush(UNSCHEDULED);
    await settle();
    expect(TestBed.inject(Router).url).toContain('branch=b-1');
  });

  it('opens the drawer from a card and asks before discarding unsaved changes (AC-23)', async () => {
    await setup('owner', '/schedule?branch=b-1&view=week&date=2026-09-23');
    call('GET', 'options').flush(OPTIONS);
    await settle();
    call('GET', 'calendar').flush(CALENDAR);
    call('GET', 'unscheduled').flush(UNSCHEDULED);
    await settle();
    const service = harness.routeDebugElement!.injector.get(ConfirmationService);
    const confirm = vi.spyOn(service, 'confirm');
    const confirmations: Confirmation[] = [];
    confirm.mockImplementation((confirmation) => {
      confirmations.push(confirmation);
      return service;
    });

    page.openVisit('v-1');
    await settle();
    call('GET', 'visits/v-1').flush(detail('v-1'));
    await settle();
    const drawer = harness.routeDebugElement!.query(By.directive(DispatchDrawer))
      .componentInstance as DispatchDrawer;
    expect(drawer.form()?.date).toBe('2026-12-01');
    expect(page['store'].proposed()).toMatchObject({ date: '2026-12-01', start: '09:00' });
    // The calendar follows the proposed date (replaceUrl) without a prompt.
    call('GET', 'calendar', { date: '2026-12-01' }).flush(CALENDAR);
    await settle();
    expect(TestBed.inject(Router).url).toContain('date=2026-12-01');

    // Pristine switch needs no prompt; editing makes another card go through the discard dialog.
    drawer.patch({ note: 'Ring twice' }, 'dispatchNote');
    await settle();
    expect(page['store'].drawerDirty()).toBe(true);
    page.openVisit('v-2');
    expect(confirmations.at(-1)?.header).toBe('Discard unsaved changes?');
    confirmations.at(-1)!.reject!();
    expect(page['store'].selectedVisitId()).toBe('v-1');

    page.onDrawerCloseRequested();
    confirmations.at(-1)!.accept!();
    await settle();
    expect(page['store'].selectedVisitId()).toBeNull();
    expect(page.canLeave()).toBe(true);
  });
});
