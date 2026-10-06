import { Location } from '@angular/common';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  CompleteAssessmentDialog,
  assessmentErrors,
} from '../../components/complete-assessment-dialog/complete-assessment-dialog';
import { RequestActionDialog } from '../../components/request-action-dialog/request-action-dialog';
import {
  CONFLICT_MESSAGE,
  RequestCard,
  RequestDetail,
  UNAVAILABLE_MESSAGE,
} from '../../models/requests.model';
import { EMPTY_FILTERED_MESSAGE, FORBIDDEN_MESSAGE, Requests } from './requests';

const API = 'http://api.test';
const ID_A = '11111111-1111-4111-8111-111111111111';
const ID_B = '22222222-2222-4222-8222-222222222222';
const QUOTE_ID = '33333333-3333-4333-8333-333333333333';

@Component({ template: '<p>stub</p>' })
class Stub {}

const card = (id: string, number: string, title: string): RequestCard => ({
  id,
  number,
  title,
  customerName: 'Sofia Martinez',
  categoryName: 'Plumbing',
  dateKind: 'preferred',
  date: '2026-09-21',
  urgency: 'urgent',
  avatar: null,
  createdAt: '2026-09-20T10:00:00Z',
  awaitingResponse: false,
});
const CARD_A = card(ID_A, 'REQ-1048', 'Kitchen sink leak');
const CARD_B = card(ID_B, 'REQ-1049', 'Tree trimming');

const columns = (items: Record<string, readonly RequestCard[]>, total?: number) => ({
  timezone: 'America/Chicago',
  columns: ['new', 'needs_review', 'assessment_scheduled', 'ready_for_quote'].map((status) => ({
    status,
    total: total ?? (items[status] ?? []).length,
    items: items[status] ?? [],
  })),
});
const PIPELINE = columns({ new: [CARD_B], needs_review: [CARD_A] });
const EMPTY_PIPELINE = columns({});
const METRICS = {
  newToday: { value: 6, deltaPercent: 50 },
  awaitingResponse: { value: 4, deltaPercent: null },
  assessmentsToday: { value: 3, deltaPercent: 200 },
  conversionRate: { value: 28, deltaPoints: 7 },
};
const OPTIONS = {
  requestPrefix: 'REQ',
  timezone: 'America/Chicago',
  categories: [{ id: 'cat-1', name: 'Plumbing', services: [] }],
  assignees: [{ userId: 'u-2', name: 'James Diaz', initials: 'JD', branchIds: 'all' }],
  branches: [{ id: 'b-1', name: 'Austin Central' }],
  technicians: [],
};
const detail = (overrides: Partial<RequestDetail> = {}): RequestDetail => ({
  id: ID_A,
  number: 'REQ-1048',
  title: 'Kitchen sink leak',
  status: 'needs_review',
  urgency: 'urgent',
  source: 'public_form',
  createdAt: '2026-09-20T10:00:00Z',
  branch: { id: 'b-1', name: 'Austin Central' },
  assignee: null,
  customer: { id: 'c-1', name: 'Sofia Martinez' },
  contact: { name: 'Sofia Martinez', phone: '(512) 555-0198', email: 'sofia@example.com' },
  serviceAddress: '1842 Oak Street, Austin, TX 78704',
  category: { id: 'cat-1', name: 'Plumbing' },
  service: null,
  availability: { dateMode: 'asap', preferredDate: null, timeWindow: null, schedulingNotes: null },
  hasActiveDamage: false,
  description: 'Water is pooling under the sink.',
  awaitingResponse: false,
  assessment: null,
  attachments: [],
  notes: [],
  activity: [
    {
      kind: 'created',
      label: 'Request submitted',
      detail: 'Online form',
      actorName: 'Customer',
      occurredAt: '2026-09-20T10:00:00Z',
    },
  ],
  ...overrides,
});

describe('Requests page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: Requests;

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/service-requests${path}`);
  const pipelines = (): TestRequest[] =>
    httpTesting.match((r) => r.method === 'GET' && r.url === `${API}/service-requests/pipeline`);
  const flushRefresh = (): void => {
    const latest = pipelines();
    latest[latest.length - 1].flush(PIPELINE);
    call('GET', '/metrics').flush(METRICS);
  };

  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const root = (): HTMLElement => harness.fixture.nativeElement as HTMLElement;
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const cardButton = (id: string): HTMLButtonElement =>
    root().querySelector<HTMLButtonElement>(`[data-request-id="${id}"]`)!;
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.body.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const dialog = (): RequestActionDialog =>
    harness.fixture.debugElement.query(By.directive(RequestActionDialog)).componentInstance;

  async function setup(
    roleCode: string,
    options: { url?: string; flush?: boolean } = {},
  ): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'requests', component: Requests },
          { path: 'requests/:requestId/assessment', component: Stub },
          { path: 'auth/sign-in', component: Stub },
          { path: 'coming-soon/:module', component: Stub },
          { path: 'quotes/:quoteId/edit', component: Stub },
          { path: 'quotes/:quoteId', component: Stub },
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
    // Browser Back/Forward reach the router only through its location listener.
    TestBed.inject(Router).setUpLocationChangeListener();
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl(options.url ?? '/requests', Requests);
    if (options.flush ?? true) {
      call('GET', '/pipeline').flush(PIPELINE);
      call('GET', '/metrics').flush(METRICS);
      call('GET', '/options').flush(OPTIONS);
    }
    await settle();
  }

  afterEach(() => {
    vi.useRealTimers();
    httpTesting?.verify();
  });

  it('groups cards in four columns and syncs the panel with ?request= through open, Back, Forward, unknown and invalid ids (AC-01, AC-08, FR-05)', async () => {
    await setup('owner');
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);

    expect(text()).toContain('Needs review 1');
    expect(text()).toContain('New requests 1');
    expect(text()).toContain('No requests');
    expect(cardButton(ID_A).getAttribute('aria-label')).toBe(
      'Kitchen sink leak, Sofia Martinez, Needs review',
    );

    cardButton(ID_A).click();
    await settle();
    call('GET', `/${ID_A}`).flush(detail());
    await settle();

    expect(router.url).toBe(`/requests?request=${ID_A}`);
    expect(document.body.querySelector('#request-detail-title')?.textContent).toContain(
      'Kitchen sink leak',
    );
    expect(text()).toContain('Request submitted');
    expect(cardButton(ID_A).getAttribute('aria-current')).toBe('true');

    // Back closes the panel and focus returns to the originating card.
    location.back();
    await vi.waitFor(() => expect(router.url).toBe('/requests'));
    await settle();
    await vi.waitFor(() => expect(document.activeElement).toBe(cardButton(ID_A)));

    // Forward reopens it with a fresh detail request.
    location.forward();
    await vi.waitFor(() => expect(router.url).toBe(`/requests?request=${ID_A}`));
    await settle();
    call('GET', `/${ID_A}`).flush(detail());
    await settle();
    expect(text()).toContain('Water is pooling under the sink.');

    // Unknown id: 404 panel with Close, board untouched.
    await harness.navigateByUrl(`/requests?request=${ID_B}`);
    await settle();
    call('GET', `/${ID_B}`).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain(UNAVAILABLE_MESSAGE);
    expect(button('Close')).toBeDefined();

    // Invalid id: 404 panel without any detail request; Close removes the parameter.
    await harness.navigateByUrl('/requests?request=not-a-uuid');
    await settle();
    expect(text()).toContain(UNAVAILABLE_MESSAGE);
    button('Close')!.click();
    await vi.waitFor(() => expect(router.url).toBe('/requests'));
    expect(page.selectedId()).toBeNull();
  });

  it('debounces search, reloads only the board for filter changes, drops stale responses and clears filters (AC-06)', async () => {
    await setup('owner');
    vi.useFakeTimers();

    page.onSearchText('1');
    page.onSearchText('1048');
    vi.advanceTimersByTime(299);
    httpTesting.expectNone(`${API}/service-requests/pipeline`);
    vi.advanceTimersByTime(1);
    const search = call('GET', '/pipeline');
    expect(search.request.params.get('search')).toBe('1048');
    search.flush(PIPELINE);

    // Two quick changes: the first response is dropped (cancelled), only the last one counts.
    page.onFilterChange({ urgency: 'emergency' });
    page.onFilterChange({ assignee: 'unassigned' });
    const [stale, latest] = pipelines();
    expect(stale.cancelled).toBe(true);
    expect(latest.request.params.get('assigneeUserId')).toBe('unassigned');
    expect(latest.request.params.get('urgency')).toBe('emergency');
    expect(latest.request.params.get('search')).toBe('1048');
    latest.flush(EMPTY_PIPELINE);
    await settle();

    expect(text()).toContain(EMPTY_FILTERED_MESSAGE);
    button('Clear filters')!.click();
    const cleared = call('GET', '/pipeline');
    expect(cleared.request.params.keys()).toEqual([]);
    cleared.flush(PIPELINE);
    await settle();
    expect(cardButton(ID_A)).toBeTruthy();
    // httpTesting.verify() proves no metrics request was made for any filter change.
  });

  it('applies a mutation response, reloads board and metrics, and keeps the card selected; cancel needs a reason (AC-18, AC-19, AC-22)', async () => {
    await setup('dispatcher', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(detail());
    await settle();

    expect(button('Schedule assessment')).toBeDefined();
    button('Mark ready for quote')!.click();
    const post = call('POST', `/${ID_A}/ready-for-quote`);
    post.flush(detail({ status: 'ready_for_quote' }));
    await settle();
    flushRefresh();
    await settle();

    expect(text()).toContain('Ready for quote');
    expect(button('Create quote')).toBeDefined();
    expect(button('Mark ready for quote')).toBeUndefined();
    expect(text()).toContain('Request marked ready for quote.');
    expect(cardButton(ID_A).getAttribute('aria-current')).toBe('true');

    // Create quote posts once and opens the editor of the returned quote (quote-builder BR-04, BR-05).
    button('Create quote')!.click();
    httpTesting
      .expectOne((r) => r.method === 'POST' && r.url === `${API}/quotes`)
      .flush({ id: QUOTE_ID }, { status: 201, statusText: 'Created' });
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/quotes/${QUOTE_ID}/edit`));
  });

  it('cancels with a required reason through the dialog and handles 400, 409 and failures per surface (AC-19, FR-13)', async () => {
    await setup('owner', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(detail());
    await settle();

    // Cancel request: the reason is required before anything is sent.
    page.onAction('cancel-request');
    await settle();
    expect(dialog().title()).toBe('Cancel REQ-1048?');
    dialog().submit();
    httpTesting.expectNone(`${API}/service-requests/${ID_A}/cancel`);
    expect(dialog().error('reason')).toBe('Enter a reason.');
    dialog().body.set('Customer called to cancel');
    dialog().submit();
    const cancel = call('POST', `/${ID_A}/cancel`);
    expect(cancel.request.body).toEqual({ reason: 'Customer called to cancel' });
    // 409: dialog closes, fixed toast, detail + board + metrics reload.
    cancel.flush({ title: 'ignored' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(page.dialogKind()).toBeNull();
    expect(text()).toContain(CONFLICT_MESSAGE);
    call('GET', `/${ID_A}`).flush(detail({ status: 'cancelled' }));
    flushRefresh();
    await settle();
    expect(button('Schedule assessment')).toBeUndefined();

    // 400 keeps the dialog open with the field error inline.
    await harness.navigateByUrl(`/requests?request=${ID_B}`);
    await settle();
    call('GET', `/${ID_B}`).flush(detail({ id: ID_B, number: 'REQ-1049' }));
    await settle();
    page.onAction('log-response');
    await settle();
    dialog().body.set('Called back');
    dialog().submit();
    call('POST', `/${ID_B}/customer-responses`).flush(
      { errors: { body: ['This request has no customer contact.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(page.dialogKind()).toBe('log-response');
    expect(text()).toContain('This request has no customer contact.');

    // Any other failure: toast, dialog and data kept.
    dialog().submit();
    call('POST', `/${ID_B}/customer-responses`).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();
    expect(page.dialogKind()).toBe('log-response');
    expect(dialog().body()).toBe('Called back');
    expect(text()).toContain("We couldn't save this change. Please try again.");
  });

  it.each<[RequestDetail['status'], string]>([
    ['needs_review', 'Schedule assessment'],
    ['assessment_scheduled', 'Reschedule'],
  ])(
    '%s: the panel footer navigates to the assessment page and no dialog opens (AC-01)',
    async (status, label) => {
      await setup('dispatcher', { url: `/requests?request=${ID_A}` });
      call('GET', `/${ID_A}`).flush(detail({ status }));
      await settle();

      button(label)!.click();

      await vi.waitFor(() =>
        expect(TestBed.inject(Router).url).toBe(`/requests/${ID_A}/assessment`),
      );
      expect(page.dialogKind()).toBeNull();
    },
  );

  it('cancels an assessment from the panel with the notify checkbox off by default and shows a toast handed over by the assessment page (AC-18, AC-20)', async () => {
    await setup('dispatcher', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(
      detail({
        status: 'assessment_scheduled',
        assessment: {
          id: 'a-1',
          start: '2026-09-22T15:00:00Z',
          end: '2026-09-22T16:00:00Z',
          technician: { id: 't-1', name: 'Carlos Rivera' },
          purpose: 'Inspect the valve',
          internalInstructions: 'Check under the sink',
        },
      }),
    );
    await settle();
    // BR-17: the panel shows the purpose and the internal instructions.
    expect(text()).toContain('Purpose: Inspect the valve');
    expect(text()).toContain('Check under the sink');

    page.onAction('cancel-assessment');
    const confirm = (): HTMLElement | null =>
      document.querySelector('[role="alertdialog"][aria-modal]');
    await vi.waitFor(() => expect(confirm()?.textContent).toContain('Cancel assessment?'));
    expect(confirm()?.querySelector<HTMLInputElement>('input[type="checkbox"]')?.checked).toBe(
      false,
    );
    button('Cancel assessment')!.click();
    const cancel = call('POST', `/${ID_A}/assessment/cancel`);
    expect(cancel.request.body).toEqual({ notifyCustomer: false });
    cancel.flush(detail({ status: 'needs_review' }));
    await settle();
    flushRefresh();
    await settle();
    expect(text()).toContain('Assessment cancelled.');

    // A toast handed over in the navigation state shows once the board renders.
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/auth/sign-in');
    await router.navigate(['/requests'], {
      state: { toast: { severity: 'success', summary: 'Assessment scheduled.' } },
    });
    call('GET', '/pipeline').flush(PIPELINE);
    call('GET', '/metrics').flush(METRICS);
    call('GET', '/options').flush(OPTIONS);
    await settle();
    await vi.waitFor(() => expect(text()).toContain('Assessment scheduled.'));
  });

  it.each(['technician', 'custom_role'])(
    '%s sees the forbidden state and makes no data requests (AC-05)',
    async (role) => {
      await setup(role, { flush: false });

      expect(text()).toContain(FORBIDDEN_MESSAGE);
      expect(button('New request')).toBeUndefined();
      httpTesting.expectNone((r) => r.url.includes('/service-requests'));
    },
  );

  it('completes an assessment through the findings dialog: client checks, multipart POST, inline 400 and discard prompt (quote-builder FR-01, BR-01, BR-02)', async () => {
    await setup('dispatcher', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(
      detail({
        status: 'assessment_scheduled',
        assessment: {
          id: 'a-1',
          start: '2020-01-01T15:00:00Z',
          end: '2020-01-01T16:00:00Z',
          technician: { id: 't-1', name: 'Carlos Rivera' },
          purpose: null,
          internalInstructions: null,
        },
      }),
    );
    await settle();
    const dialog = harness.fixture.debugElement.query(By.directive(CompleteAssessmentDialog))
      .componentInstance as CompleteAssessmentDialog;

    // BR-01/BR-02 client rules (the backend re-validates and detects the content type).
    const file = (name: string, size: number): File => {
      const f = new File(['x'], name);
      Object.defineProperty(f, 'size', { value: size });
      return f;
    };
    const MB = 1024 * 1024;
    expect(assessmentErrors('', '', [])).toEqual({ diagnosis: 'Enter the diagnosis.' });
    expect(assessmentErrors('d'.repeat(2001), 's'.repeat(2001), [])).toEqual({
      diagnosis: 'Diagnosis must be 2000 characters or fewer.',
      recommendedScope: 'Recommended scope must be 2000 characters or fewer.',
    });
    expect(
      assessmentErrors(
        'd',
        '',
        Array.from({ length: 7 }, (_, i) => file(`${i}.png`, 5)),
      ),
    ).toEqual({
      photos: 'Add up to 6 photos.',
    });
    for (const bad of [file('a.pdf', 5), file('a.png', 11 * MB), file('a.jpg', 0)]) {
      expect(assessmentErrors('d', '', [bad])['photos']).toBe(
        'Photos must be JPG or PNG files of 10 MB or less.',
      );
    }
    expect(assessmentErrors('d', '', [file('a.JPEG', 5 * MB), file('b.png', MB)])).toEqual({});

    // Opening shows the dialog; an empty diagnosis blocks the request with the inline error.
    button('Complete assessment')!.click();
    await settle();
    expect(dialog.open()).toBe(true);
    dialog.submit();
    await settle();
    expect(text()).toContain('Enter the diagnosis.');
    httpTesting.expectNone((r) => r.url.endsWith('/assessment/complete'));

    // A server 400 stays inline and keeps the data.
    dialog.diagnosis.set('  Failed P-trap  ');
    dialog.scope.set('');
    dialog.submit();
    const failed = call('POST', `/${ID_A}/assessment/complete`);
    const sent = failed.request.body as FormData;
    expect(sent.get('diagnosis')).toBe('Failed P-trap');
    expect(sent.has('recommendedScope')).toBe(false);
    failed.flush(
      { errors: { photos: ['Photos must be JPG or PNG files of 10 MB or less.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('Photos must be JPG or PNG files of 10 MB or less.');
    expect(dialog.open()).toBe(true);
    expect(dialog.diagnosis()).toBe('  Failed P-trap  ');

    // Success closes the dialog, refreshes the panel and toasts.
    dialog.submit();
    call('POST', `/${ID_A}/assessment/complete`).flush(
      detail({
        status: 'ready_for_quote',
        completedAssessment: {
          id: 'a-1',
          start: '2020-01-01T15:00:00Z',
          completedAt: '2020-01-01T16:00:00Z',
          technician: { id: 't-1', name: 'Carlos Rivera' },
          diagnosis: 'Failed P-trap',
          recommendedScope: null,
          photos: [],
        },
      }),
    );
    await settle();
    flushRefresh();
    await settle();
    expect(text()).toContain('Assessment completed.');
    expect(text()).toContain('Assessment findings');
    expect(text()).toContain('Failed P-trap');
    expect(dialog.open()).toBe(false);
  });

  it('offers Create, Continue and View quote per role and handles the draft-quote conflict on Move back (quote-builder FR-02, FR-14, BR-04, BR-31)', async () => {
    await setup('owner', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(
      detail({
        status: 'ready_for_quote',
        quote: { id: QUOTE_ID, status: 'draft', hasDraft: true },
      }),
    );
    await settle();

    expect(button('Continue quote')).toBeDefined();
    expect(button('Create quote')).toBeUndefined();

    // Move back is blocked by the draft quote: fixed copy, no raw backend text, panel reloaded.
    page.onAction('move-back');
    call('POST', `/${ID_A}/move-to-review`).flush(
      { title: 'backend text', code: 'quote_draft_exists' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(text()).toContain('This request has a draft quote. Discard it first.');
    expect(text()).not.toContain('backend text');
    call('GET', `/${ID_A}`).flush(
      detail({
        status: 'ready_for_quote',
        quote: { id: QUOTE_ID, status: 'draft', hasDraft: true },
      }),
    );
    flushRefresh();
    await settle();

    button('Continue quote')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/quotes/${QUOTE_ID}/edit`));
  });

  it('shows View quote on a quoted request to read roles and opens the read-only quote page (quote-builder BR-04, BR-30)', async () => {
    await setup('viewer', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(
      detail({ status: 'quoted', quote: { id: QUOTE_ID, status: 'sent', hasDraft: false } }),
    );
    await settle();

    expect(button('Create quote')).toBeUndefined();
    button('View quote')!.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/quotes/${QUOTE_ID}`));
  });

  it('read roles see board and panel with no New request, footer, menu, note input or Add file (AC-05)', async () => {
    await setup('viewer', { url: `/requests?request=${ID_A}` });
    call('GET', `/${ID_A}`).flush(detail());
    await settle();

    expect(text()).toContain('Kitchen sink leak');
    expect(button('New request')).toBeUndefined();
    expect(button('Schedule assessment')).toBeUndefined();
    expect(document.body.querySelector('[aria-label="More actions"]')).toBeNull();
    expect(document.body.querySelector('textarea[aria-label="Internal note"]')).toBeNull();
    expect(text()).not.toContain('Add file');
  });
});
