import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { VisitDispatchDetail, VisitEvaluation } from '../../models/schedule.model';
import { DispatchDrawer } from './dispatch-drawer';

const API = 'http://api.test';

const DETAIL: VisitDispatchDetail = {
  visitId: 'v-1',
  visitNumber: 1,
  recurrence: { count: 3 },
  status: 'unscheduled',
  updatedAt: '2026-10-06T10:00:00Z',
  canManage: true,
  isLocked: false,
  timezone: 'America/Chicago',
  workOrder: {
    id: 'w-1',
    displayNumber: 'WO-3091',
    title: 'Kitchen sink leak repair',
    priority: 'high',
    estimatedDurationMinutes: 120,
    requiredSkills: [{ id: 's-1', name: 'Plumbing' }],
    plannedMaterialsCount: 2,
  },
  customer: {
    name: 'Sofia Martinez',
    initials: 'SM',
    phone: '(512) 555-0142',
    address: '1842 Oak Street, Austin, TX',
    hasEmail: true,
  },
  values: {
    date: '2026-10-08',
    start: '09:30',
    end: '11:30',
    arrivalWindow: 'start_plus_2h',
    technicianIds: [],
    primaryTechnicianId: null,
    dispatchNote: null,
    notifyCustomer: true,
    sendTechnicianDetails: true,
  },
  technicians: [
    { id: 't-1', name: 'Carlos Rivera', initials: 'CR', colorHex: null, primarySkill: 'Plumbing' },
    { id: 't-2', name: 'Maya Thompson', initials: 'MT', colorHex: null, primarySkill: 'HVAC' },
  ],
};

const CLEAN: VisitEvaluation = {
  conflicts: [],
  skills: { passed: true, code: null, label: 'Skills match (Plumbing)' },
  checks: [
    {
      technicianId: 't-1',
      availability: { passed: true, code: null, label: 'Available' },
      overlap: { passed: true, code: null, label: 'No overlapping jobs' },
    },
  ],
  impact: [
    {
      technicianId: 't-1',
      jobs: 4,
      scheduledMinutes: 450,
      availableMinutes: 480,
      previous: 'Previous job: #WO-3085 ends 9:00 AM',
      next: null,
    },
  ],
  ranking: [{ technicianId: 't-1', bestMatch: true, conflictCount: 0, loadPercent: 94 }],
};
const CONFLICTED: VisitEvaluation = {
  ...CLEAN,
  conflicts: [
    {
      technicianId: 't-1',
      code: 'overlap',
      from: null,
      to: null,
      label: 'Overlaps #WO-3085 9:00 AM – 12:00 PM',
    },
  ],
};

const wait = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

describe('Dispatch drawer', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<DispatchDrawer>;
  let drawer: DispatchDrawer;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/dispatch/${path}`);
  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (): string => document.body.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const footerButtons = (): string[] =>
    Array.from(document.querySelectorAll<HTMLElement>('.drawer-shell__footer button')).map(
      (button) => button.textContent?.trim() ?? '',
    );

  async function setup(detail: VisitDispatchDetail = DETAIL, evaluate = true): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DispatchDrawer);
    drawer = fixture.componentInstance;
    fixture.componentRef.setInput('visitId', 'v-1');
    await settle();
    call('GET', 'visits/v-1').flush(detail);
    await settle();
    if (evaluate) {
      await wait(350);
      call('POST', 'visits/v-1/evaluation').flush(CLEAN);
      await settle();
    }
  }

  afterEach(() => {
    httpTesting?.verify();
    fixture?.destroy();
  });

  it('prefills, debounces one evaluation per burst and keeps save enabled when it fails (AC-06, AC-07, AC-19, AC-20)', async () => {
    await setup(DETAIL, false);
    expect(text()).toContain('WO-3091');
    expect(text()).toContain('Visit 1 of 3');
    expect(text()).toContain('Planned materials: 2');
    expect(drawer.form()).toMatchObject({ date: '2026-10-08', start: '09:30', end: '11:30' });
    expect(drawer.isDirty()).toBe(false);

    // Nothing is sent before the debounce, and a burst produces a single evaluation.
    httpTesting.expectNone(`${API}/dispatch/visits/v-1/evaluation`);
    drawer.setTechnicians(['t-1']);
    drawer.setTechnicians(['t-1', 't-2']);
    await wait(350);
    const request = call('POST', 'visits/v-1/evaluation');
    expect(request.request.body).toMatchObject({
      date: '2026-10-08',
      start: '09:30',
      end: '11:30',
      technicianIds: ['t-1', 't-2'],
      primaryTechnicianId: 't-1',
    });
    request.flush(CLEAN);
    await settle();
    expect(drawer.bannerText()).toBe('No scheduling conflicts');
    expect(text()).toContain('4 jobs · 7.5h / 8h after assignment');
    expect(text()).toContain('Previous job: #WO-3085 ends 9:00 AM');
    expect(text()).toContain('No next job');
    expect(text()).not.toContain('travel');
    expect(drawer.isDirty()).toBe(true);

    // A failed evaluation shows Retry and never blocks saving.
    drawer.patch({ end: '12:00' }, 'end');
    await wait(350);
    call('POST', 'visits/v-1/evaluation').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(drawer.evaluationFailed()).toBe(true);
    expect(text()).toContain("We couldn't check availability.");
    expect(footerButtons()).toContain('Assign & notify');
    expect(
      Array.from(
        document.querySelectorAll<HTMLButtonElement>('.drawer-shell__footer button'),
      ).every((button) => !button.disabled),
    ).toBe(true);
  });

  it.each([
    ['unscheduled with email', {}, {}, {}, 'Schedule & notify'],
    ['email unchecked', {}, {}, { notifyCustomer: false }, 'Schedule'],
    ['customer without email', {}, { hasEmail: false }, {}, 'Schedule'],
    ['already scheduled and unchanged', { status: 'scheduled' }, {}, {}, 'Update'],
  ])(
    'labels the primary button for %s (AC-19, AC-20)',
    async (_name, top, customer, values, label) => {
      await setup(
        {
          ...DETAIL,
          ...top,
          customer: { ...DETAIL.customer, ...customer },
          values: { ...DETAIL.values, ...values },
        },
        false,
      );
      await wait(350);
      call('POST', 'visits/v-1/evaluation').flush(CLEAN);
      await settle();
      expect(drawer.primaryLabel()).toBe(label);
      expect(footerButtons()).toContain(label);
      expect(drawer.emailDisabled()).toBe('hasEmail' in customer);
      expect(text()).toContain("SMS isn't available yet.");
      if ('hasEmail' in customer) {
        expect(text()).toContain('This customer has no email address.');
      }
    },
  );

  it('requires a justification, sends one request, handles 409 and reports success (AC-10, AC-14, AC-23)', async () => {
    await setup(DETAIL, false);
    drawer.setTechnicians(['t-1']);
    await wait(350);
    call('POST', 'visits/v-1/evaluation').flush(CONFLICTED);
    await settle();
    expect(drawer.bannerText()).toBe('1 scheduling conflicts');
    expect(text()).toContain('Overlaps #WO-3085 9:00 AM – 12:00 PM');

    // Missing and short reasons never reach the API.
    drawer.submit();
    expect(drawer.errors().overrideReason).toBe('Enter a reason to confirm despite the conflicts.');
    drawer.patch({ reason: 'short' }, 'overrideReason');
    drawer.submit();
    expect(drawer.errors().overrideReason).toBe('Enter at least 10 characters.');

    // Double submit sends a single PUT; a stale version keeps the input and offers Reload.
    drawer.patch(
      { reason: '  Customer approved the overlap  ', note: ' Gate code 42 ' },
      'overrideReason',
    );
    drawer.submit();
    drawer.submit();
    expect(drawer.saving()).toBe(true);
    let put = call('PUT', 'visits/v-1');
    expect(put.request.body).toMatchObject({
      technicianIds: ['t-1'],
      primaryTechnicianId: 't-1',
      overrideReason: 'Customer approved the overlap',
      dispatchNote: 'Gate code 42',
      notifyCustomer: true,
      updatedAt: DETAIL.updatedAt,
    });
    put.flush(
      { title: 'Conflict', status: 409, code: 'visit_changed' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(drawer.saving()).toBe(false);
    expect(drawer.saveProblem()).toBe('changed');
    expect(text()).toContain(
      'This visit was changed by someone else. Reload to see the latest version.',
    );
    expect(drawer.form()?.reason).toBe('  Customer approved the overlap  ');

    // Server-side conflicts focus the justification and refresh the evaluation.
    drawer.submit();
    put = call('PUT', 'visits/v-1');
    put.flush(
      { title: 'Conflict', status: 409, code: 'scheduling_conflicts' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(drawer.errors().overrideReason).toBe('Enter a reason to confirm despite the conflicts.');
    await wait(350);
    call('POST', 'visits/v-1/evaluation').flush(CONFLICTED);
    await settle();

    // Success reports the toast text and closes through the page.
    const saved = vi.fn();
    drawer.saved.subscribe(saved);
    drawer.patch({ reason: 'Customer approved the overlap' }, 'overrideReason');
    drawer.submit();
    call('PUT', 'visits/v-1').flush({
      visit: { ...DETAIL, status: 'assigned' },
      workOrderStatus: 'scheduled',
      notified: true,
      materializedVisitId: null,
    });
    expect(saved).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'Visit assigned. Customer notified by email.' }),
    );
  });

  it.each([
    ['viewer', { ...DETAIL, canManage: false }, undefined],
    [
      'locked visit',
      { ...DETAIL, isLocked: true },
      "This visit has started or is closed and can't be changed.",
    ],
  ])('opens read-only with Close only for a %s (AC-12, AC-21)', async (_name, detail, note) => {
    await setup(detail, false);
    expect(drawer.readOnly()).toBe(true);
    expect(footerButtons()).toEqual(['Close']);
    if (note) {
      expect(text()).toContain(note);
    }
    // Read-only drawers never call the evaluation or dispatch endpoints.
    await wait(350);
    httpTesting.expectNone(`${API}/dispatch/visits/v-1/evaluation`);
    drawer.submit();
    httpTesting.expectNone(`${API}/dispatch/visits/v-1`);
  });
});
