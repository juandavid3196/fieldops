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
import { assessmentUnsavedChangesGuard } from '../../guards/assessment-unsaved-changes.guard';
import {
  CONFLICT_MESSAGE,
  PlannerTechnician,
  RequestDetail,
  RequestStatus,
  SAVE_FAILED_MESSAGE,
  UNAVAILABLE_MESSAGE,
} from '../../models/requests.model';
import { weekStart } from '../../utils/assessment-calendar';
import { previewDate } from '../../utils/assessment-schedule';
import {
  Assessment,
  CONFLICT_COPY,
  FORBIDDEN_MESSAGE,
  NOT_SCHEDULABLE_MESSAGE,
  PURPOSE_REQUIRED_MESSAGE,
} from './assessment';

const API = 'http://api.test';
const ID = '11111111-1111-4111-8111-111111111111';
const TECH_A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const TECH_B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const ZONE = 'America/Chicago';
const FUTURE = '2099-01-10';

@Component({ template: '<p>stub</p>' })
class Stub {}

const DETAIL: RequestDetail = {
  id: ID,
  number: 'REQ-1082',
  title: 'Leaking kitchen sink',
  status: 'needs_review',
  urgency: 'standard',
  source: 'public_form',
  createdAt: '2026-09-20T10:00:00Z',
  branch: { id: 'b-1', name: 'Austin Central' },
  assignee: null,
  customer: { id: 'c-1', name: 'Sofia Martinez' },
  contact: { name: 'Sofia Martinez', phone: null, email: 'sofia@example.com' },
  serviceAddress: '742 Maple Ave, Austin, TX 78704',
  category: null,
  service: null,
  availability: {
    dateMode: 'date',
    preferredDate: FUTURE,
    timeWindow: 'afternoon',
    schedulingNotes: 'Gate code 1234',
  },
  hasActiveDamage: false,
  description: 'Water under the sink.',
  awaitingResponse: false,
  assessment: null,
  attachments: [],
  notes: [],
  activity: [],
};
const detail = (overrides: Partial<RequestDetail> = {}): RequestDetail => ({
  ...DETAIL,
  ...overrides,
});
const RESCHEDULE = detail({
  status: 'assessment_scheduled',
  assessment: {
    id: 'a-1',
    start: '2099-02-03T15:00:00Z', // 9:00 AM Chicago (CST)
    end: '2099-02-03T16:30:00Z',
    technician: { id: TECH_A, name: 'Carlos Rivera' },
    purpose: 'Inspect the shut-off valve',
    internalInstructions: 'Check under the sink',
  },
});
const OPTIONS = {
  requestPrefix: 'REQ',
  timezone: ZONE,
  categories: [],
  assignees: [],
  branches: [{ id: 'b-1', name: 'Austin Central' }],
  technicians: [],
};
const technician = (
  id: string,
  name: string,
  slot: Partial<PlannerTechnician['slot']>,
  percent: number,
): PlannerTechnician => ({
  id,
  name,
  initials: name.slice(0, 2),
  primarySkill: 'Plumbing',
  slot: {
    state: 'available',
    from: null,
    to: null,
    availableAfter: null,
    blocking: false,
    ...slot,
  },
  workload: { percent, state: 'percent' },
});
const PLANNER = {
  timezone: ZONE,
  branchId: 'b-1',
  technicians: [
    technician(TECH_A, 'Carlos Rivera', {}, 60),
    technician(
      TECH_B,
      'Ana López',
      { state: 'conflict', from: `${FUTURE}T16:30:00Z`, to: `${FUTURE}T17:30:00Z`, blocking: true },
      70,
    ),
  ],
};
const EMPTY_CALENDAR = { timezone: ZONE, days: [], events: [] };

describe('Schedule assessment page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: Assessment;

  const item = (path: string): string => `${API}/service-requests/${ID}${path}`;
  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === item(path));
  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string, scope: ParentNode = document.body): HTMLElement | undefined =>
    Array.from(scope.querySelectorAll<HTMLElement>('button, a')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const radio = (name: string): HTMLElement | null =>
    document.body.querySelector(`[role="radio"][aria-label^="${name}"]`);
  const confirm = (): HTMLElement | null =>
    document.querySelector('[role="alertdialog"][aria-modal]');
  const opened = (title: string): Promise<void> =>
    vi.waitFor(() => expect(confirm()?.textContent).toContain(title));
  const closed = (): Promise<void> => vi.waitFor(() => expect(confirm()).toBeNull());
  const router = (): Router => TestBed.inject(Router);
  const toastState = (): unknown => router().lastSuccessfulNavigation()?.extras.state?.['toast'];

  /** Opens the page; `respond` flushes the detail and options requests (omitted: no HTTP expected). */
  async function setup(
    role: string,
    options: {
      id?: string;
      detail?: RequestDetail | 'not-found' | null;
    } = {},
  ): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: 'requests/:requestId/assessment',
            component: Assessment,
            canDeactivate: [assessmentUnsavedChangesGuard],
          },
          { path: 'requests', component: Stub },
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
    router().setUpLocationChangeListener();
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl(`/requests/${options.id ?? ID}/assessment`, Assessment);
    const loaded = options.detail;
    if (loaded === 'not-found') {
      call('GET', '').flush(null, { status: 404, statusText: 'Not Found' });
      // forkJoin cancels the sibling options request.
      expect(httpTesting.match(`${API}/service-requests/options`)[0].cancelled).toBe(true);
    } else if (loaded !== null && loaded !== undefined) {
      call('GET', '').flush(loaded);
      httpTesting.expectOne(`${API}/service-requests/options`).flush(OPTIONS);
    }
    await settle();
  }

  /** Reschedule mode with the current technician: planner and calendar load on open. */
  async function openReschedule(): Promise<void> {
    await setup('dispatcher', { detail: RESCHEDULE });
    call('GET', '/assessment/planner').flush(PLANNER);
    await settle();
    call('GET', '/assessment/calendar').flush(EMPTY_CALENDAR);
    await settle();
  }

  afterEach(() => httpTesting?.verify());

  it.each<[string, string, RequestStatus | 'not-found' | null, string]>([
    ['viewer', ID, null, FORBIDDEN_MESSAGE],
    ['technician', ID, null, FORBIDDEN_MESSAGE],
    ['owner', 'not-a-uuid', null, UNAVAILABLE_MESSAGE],
    ['owner', ID, 'not-found', UNAVAILABLE_MESSAGE],
    ['dispatcher', ID, 'ready_for_quote', NOT_SCHEDULABLE_MESSAGE],
    ['dispatcher', ID, 'cancelled', NOT_SCHEDULABLE_MESSAGE],
  ])(
    '%s opening %s sees a state instead of the form, with no planner calls and no calls before the role check (AC-02, AC-03)',
    async (role, id, status, message) => {
      await setup(role, {
        id,
        detail: status === null ? null : status === 'not-found' ? 'not-found' : detail({ status }),
      });

      expect(text()).toContain(message);
      expect(button('Schedule assessment')).toBeUndefined();
      // `httpTesting.verify()` proves no request at all for the role and id checks, and no
      // planner/calendar request for the other states.
      httpTesting.expectNone((r) => r.url.includes('/assessment/'));
      expect(document.activeElement?.tagName).toBe('H1');
    },
  );

  it('schedules a branchless request: defaults, branch first, preview, email and SMS states, inline 400 and one POST (AC-04, AC-05, AC-14, AC-16, AC-20)', async () => {
    await setup('owner', {
      detail: detail({
        branch: null,
        contact: { name: 'Sofia Martinez', phone: null, email: null },
      }),
    });

    // Header, notice, availability, summary, stepper (BR-02, BR-03) and the BR-19 defaults.
    expect(text()).toContain('Request: Needs review');
    expect(text()).toContain('Assessment is optional.');
    expect(text()).toContain('Gate code 1234');
    expect(text()).toContain('These are preferred times, not a confirmed appointment');
    expect(text()).toContain('Leaking kitchen sink');
    expect(document.body.querySelector('[aria-current="step"]')?.textContent).toContain(
      'Assessment scheduled',
    );
    expect(page.form()).toMatchObject({
      date: FUTURE,
      window: '12:00',
      duration: 60,
      technicianId: '',
      notify: false,
    });
    // Branch first: no planner until a branch is chosen. No email: Email off and disabled.
    httpTesting.expectNone((r) => r.url.includes('/assessment/planner'));
    expect(text()).toContain('Choose a branch to see technicians.');
    expect(text()).toContain('This customer has no email address.');
    expect(text()).toContain("SMS isn't available yet.");
    expect(text()).toContain('Complete the date, time and technician to preview the message.');
    expect(
      document.body.querySelector<HTMLInputElement>('#assessment-notify-email')?.disabled,
    ).toBe(true);

    page.onFormChange({ ...page.form()!, branchId: 'b-1' });
    const planner = call('GET', '/assessment/planner');
    expect(planner.request.params.get('date')).toBe(FUTURE);
    expect(planner.request.params.get('start')).toBe('12:00');
    expect(planner.request.params.get('durationMinutes')).toBe('60');
    expect(planner.request.params.get('branchId')).toBe('b-1');
    planner.flush(PLANNER);
    await settle();

    expect(radio('Carlos Rivera')?.getAttribute('aria-label')).toBe(
      'Carlos Rivera, Available, workload 60%',
    );
    expect(radio('Ana López')?.getAttribute('aria-label')).toBe(
      'Ana López, Conflict 10:30 – 11:30 AM, workload 70%',
    );
    radio('Carlos Rivera')!.click();
    const calendar = call('GET', '/assessment/calendar');
    expect(calendar.request.params.get('technicianId')).toBe(TECH_A);
    expect(calendar.request.params.get('from')).toBe(weekStart(FUTURE));
    calendar.flush(EMPTY_CALENDAR);
    await settle();
    expect(radio('Carlos Rivera')?.getAttribute('aria-checked')).toBe('true');
    expect(text()).toContain(
      `Your assessment visit is scheduled for ${previewDate(FUTURE)} between 12:00 PM and 1:00 PM. Carlos Rivera will inspect the issue before we prepare your quote.`,
    );

    // The purpose is required: nothing is sent.
    button('Schedule assessment')!.click();
    await settle();
    httpTesting.expectNone((r) => r.method === 'POST');
    expect(text()).toContain(PURPOSE_REQUIRED_MESSAGE);

    page.onFormChange({
      ...page.form()!,
      purpose: '  Inspect the valve  ',
      internalInstructions: 'Check under the sink',
    });
    page.submit();
    page.submit(); // in flight: still one request
    const post = call('POST', '/assessment');
    expect(post.request.body).toEqual({
      start: `${FUTURE}T12:00`,
      end: `${FUTURE}T13:00`,
      technicianId: TECH_A,
      branchId: 'b-1',
      purpose: 'Inspect the valve',
      internalInstructions: 'Check under the sink',
      notifyCustomer: false,
    });
    post.flush(
      { errors: { start: ['Choose an arrival window.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('Choose an arrival window.');
    expect(page.form()?.purpose).toBe('  Inspect the valve  ');
    expect(router().url).toContain('/assessment');

    page.submit();
    call('POST', '/assessment').flush(detail({ status: 'assessment_scheduled' }));
    await vi.waitFor(() => expect(router().url).toBe(`/requests?request=${ID}`));
    expect(toastState()).toEqual({ severity: 'success', summary: 'Assessment scheduled.' });
  });

  it('keeps the form on a technician 409, reloads planner and calendar, then handles failures and a changed request (AC-11)', async () => {
    await setup('dispatcher', { detail: detail() });
    call('GET', '/assessment/planner').flush(PLANNER);
    await settle();
    page.selectTechnician(TECH_A);
    call('GET', '/assessment/calendar').flush(EMPTY_CALENDAR);
    page.onFormChange({ ...page.form()!, purpose: 'Inspect the valve' });
    expect(page.form()?.notify).toBe(true);

    const submit = (): TestRequest => {
      page.submit();
      return call('POST', '/assessment');
    };
    const reload = async (): Promise<void> => {
      call('GET', '/assessment/planner').flush(PLANNER);
      call('GET', '/assessment/calendar').flush(EMPTY_CALENDAR);
      await settle();
    };

    for (const code of ['technician_conflict', 'technician_time_off']) {
      submit().flush({ title: 'ignored', code }, { status: 409, statusText: 'Conflict' });
      await settle();
      expect(document.body.querySelector('#assessment-technicianId-error')?.textContent).toContain(
        CONFLICT_COPY[code],
      );
      expect(page.form()).toMatchObject({ purpose: 'Inspect the valve', technicianId: TECH_A });
      expect(page.submitting()).toBe(false);
      await reload();
    }

    submit().flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain(SAVE_FAILED_MESSAGE);
    expect(page.form()?.purpose).toBe('Inspect the valve');

    submit().flush({ code: 'request_changed' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => expect(router().url).toBe(`/requests?request=${ID}`));
    expect(toastState()).toEqual({ severity: 'error', summary: CONFLICT_MESSAGE });
  });

  it('prefills reschedule mode, saves with PUT and asks before discarding a changed form (AC-01, AC-13, AC-19)', async () => {
    await openReschedule();

    expect(text()).toContain('Save changes');
    expect(text()).not.toContain('Assessment is optional.');
    expect(document.body.querySelector('[aria-current="step"]')?.textContent).toContain(
      'Assessment completed',
    );
    expect(page.form()).toMatchObject({
      date: '2099-02-03',
      window: '09:00',
      duration: 90,
      technicianId: TECH_A,
      purpose: 'Inspect the shut-off valve',
      internalInstructions: 'Check under the sink',
    });
    expect(page.canLeave()).toBe(true); // unchanged: no dialog

    page.onFormChange({ ...page.form()!, purpose: 'Replace the valve' });
    await settle();
    button('Cancel')!.click();
    await opened('Discard unsaved changes?');
    expect(confirm()?.textContent).not.toContain('Cancel assessment?');
    button('Keep editing', confirm()!)!.click();
    await closed();
    expect(router().url).toBe(`/requests/${ID}/assessment`);
    expect(page.form()?.purpose).toBe('Replace the valve');

    // Save changes: PUT with the edited values, no branch.
    page.submit();
    const put = call('PUT', '/assessment');
    expect(put.request.body).toEqual({
      start: '2099-02-03T09:00',
      end: '2099-02-03T10:30',
      technicianId: TECH_A,
      purpose: 'Replace the valve',
      internalInstructions: 'Check under the sink',
      notifyCustomer: true,
    });
    put.flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    button('Cancel')!.click();
    await opened('Discard unsaved changes?');
    button('Discard changes', confirm()!)!.click();
    await vi.waitFor(() => expect(router().url).toBe(`/requests?request=${ID}`));
  });

  it('cancels the assessment only after the confirm dialog, notifying by default when there is an email (AC-18)', async () => {
    await openReschedule();
    page.onFormChange({ ...page.form()!, purpose: 'Unsaved edit' });
    await settle();

    button('Cancel assessment')!.click();
    await opened('Cancel assessment?');
    expect(confirm()?.textContent).toContain('The request returns to Needs review.');
    expect(confirm()?.textContent).not.toContain('Discard unsaved changes?');
    expect(confirm()?.querySelector<HTMLInputElement>('input[type="checkbox"]')?.checked).toBe(
      true,
    );
    button('Keep', confirm()!)!.click();
    await closed();
    httpTesting.expectNone((r) => r.url.includes('/assessment/cancel'));

    button('Cancel assessment')!.click();
    await opened('Cancel assessment?');
    button('Cancel assessment', confirm()!)!.click();
    const cancel = call('POST', '/assessment/cancel');
    expect(cancel.request.body).toEqual({ notifyCustomer: true });
    cancel.flush(detail({ status: 'needs_review' }));
    // The discard dialog never interferes: the assessment was cancelled, not the edit.
    await vi.waitFor(() => expect(router().url).toBe(`/requests?request=${ID}`));
    expect(toastState()).toEqual({ severity: 'success', summary: 'Assessment cancelled.' });
  });
});
