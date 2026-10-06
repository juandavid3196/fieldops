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
import { QuoteDetail, QuoteVersion, VIEW_FORBIDDEN_MESSAGE } from '../../models/quote.model';
import { NOT_SENT_MESSAGE, QuoteView } from './quote-view';

const API = 'http://api.test';
const QUOTE_ID = '33333333-3333-4333-8333-333333333333';

@Component({ template: '<p>stub</p>' })
class Stub {}

const calculation = {
  lines: [],
  subtotal: 335,
  discountTotal: 0,
  taxLabel: 'Tax (8.25%)',
  taxTotal: 22.29,
  total: 357.29,
  currency: 'USD',
  margin: { percent: 65.4, grossProfit: 113.33 },
};

const detail = (patch: Partial<QuoteDetail> = {}): QuoteDetail => ({
  id: QUOTE_ID,
  number: 2036,
  displayNumber: 'Q-2036',
  status: 'sent',
  displayStatus: 'sent',
  responses: [],
  updatedAt: '2026-10-05T12:00:00Z',
  canManage: true,
  workOrder: null,
  canManageWorkOrders: false,
  request: {
    id: '11111111-1111-4111-8111-111111111111',
    displayNumber: 'REQ-1048',
    title: 'Kitchen sink leak',
    category: 'Plumbing',
    status: 'quoted',
  },
  customer: { id: 'c-1', name: 'Sofia Martinez', phone: null, email: null, address: '1842 Oak St' },
  recipient: { email: 'sofia@example.com' },
  completedAssessment: null,
  organization: { name: 'Northstar', currency: 'USD', defaultTaxRate: 8.25 },
  draft: null,
  sentVersions: [
    { versionNo: 2, sentAt: '2026-10-06T15:00:00Z', total: 357.29, isCurrent: true },
    { versionNo: 1, sentAt: '2026-10-05T15:00:00Z', total: 300, isCurrent: false },
  ],
  ...patch,
});

const version = (versionNo: number): QuoteVersion => ({
  versionNo,
  sentAt: '2026-10-06T15:00:00Z',
  scope: 'Kitchen sink leak',
  customerMessage: 'We will fix it',
  internalNote: 'SECRET NOTE',
  terms: 'Payment due upon completion.',
  validUntil: '2026-11-04',
  currency: 'USD',
  lines: [
    {
      type: 'service',
      name: 'Leak repair labor',
      description: '',
      quantity: 2,
      unit: 'hr',
      unitPrice: 95,
      unitCost: 40,
      taxRate: 8.25,
      lineSubtotal: 190,
      lineTax: 15.68,
      lineTotal: 205.68,
      isOptional: false,
    },
  ],
  subtotal: 190,
  discountTotal: 0,
  taxLabel: 'Tax (8.25%)',
  taxTotal: 15.68,
  total: 205.68,
  margin: { percent: 65.4, grossProfit: 113.33 },
});

describe('Quote read-only view', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: QuoteView;

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
  const quote = (path: string): TestRequest =>
    httpTesting.expectOne((r) => r.url === `${API}/quotes/${QUOTE_ID}${path}`);

  async function setup(role: string, response: QuoteDetail | null): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'quotes/:quoteId/edit', component: Stub },
          { path: 'quotes/:quoteId', component: QuoteView },
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
    page = await harness.navigateByUrl(`/quotes/${QUOTE_ID}`, QuoteView);
    if (response !== null) {
      quote('').flush(response);
      if (response.sentVersions.length > 0) {
        quote(`/versions/${response.sentVersions.find((v) => v.isCurrent)!.versionNo}`).flush(
          version(2),
        );
      }
    }
    await settle();
  }

  afterEach(() => httpTesting?.verify());

  it('shows the frozen version with internal data and the actions of the role: managers revise, continue or resend; read roles see no actions (quote-builder FR-15, BR-30, AC-25)', async () => {
    await setup('owner', detail({ draft: { ...calculationDraft(), versionNo: 3 } }));

    // Header, version selector and the customer layout plus internal data.
    expect(text()).toContain('#Q-2036');
    expect(text()).toContain('Status: Sent');
    expect(text()).toContain('Quote Q-2036 · Version 2');
    expect(text()).toContain('$205.68');
    expect(text()).toContain('We will fix it');
    expect(text()).toContain('SECRET NOTE');
    expect(text()).toContain('65.4% · $113.33');
    expect(text()).toContain('Version 3 is being revised.');
    expect(control('Continue editing')).toBeDefined();
    expect(control('Resend email')).toBeDefined();
    // A mutable version exists, so Revise is not offered.
    expect(control('Revise quote')).toBeUndefined();

    // Selecting an older version loads that frozen version.
    page.selectVersion(1);
    quote('/versions/1').flush(version(1));
    await settle();
    expect(text()).toContain('Quote Q-2036 · Version 1');
    expect(page.versionOptions().map((option) => option.label.split(' · ').pop())).toEqual([
      'Current',
      'Superseded',
    ]);
  });

  it('offers Revise quote when no revision exists and revises with updatedAt; 409 quote_changed shows the Refresh toast (quote-builder FR-13, BR-28)', async () => {
    await setup('dispatcher', detail());

    expect(control('Revise quote')).toBeDefined();
    expect(control('Continue editing')).toBeUndefined();
    control('Revise quote')!.click();
    const revise = quote('/revise');
    expect(revise.request.body).toEqual({ updatedAt: '2026-10-05T12:00:00Z' });
    revise.flush({ title: 'x', code: 'quote_changed' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(text()).toContain('This quote changed. Refresh to see the latest.');
    expect(control('Refresh')).toBeDefined();

    control('Revise quote')!.click();
    quote('/revise').flush(detail({ draft: { ...calculationDraft(), versionNo: 3 } }));
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/quotes/${QUOTE_ID}/edit`));
  });

  it('shows the derived Expired chip and the Customer response card of the selected version, or the empty text (customer-quote-approval FR-14, BR-23, AC-20, AC-21)', async () => {
    await setup(
      'viewer',
      detail({
        displayStatus: 'expired',
        responses: [
          {
            versionNo: 2,
            type: 'rejected',
            respondedAt: '2026-10-07T15:00:00Z',
            responderName: 'Sofia Martinez',
            comment: 'Too expensive',
            selectedOptionalLines: [],
            totals: null,
          },
          {
            versionNo: 1,
            type: 'clarification_requested',
            respondedAt: '2026-10-06T15:00:00Z',
            responderName: 'Sofia Martinez',
            comment: 'OLD VERSION QUESTION',
            selectedOptionalLines: [],
            totals: null,
          },
        ],
      }),
    );

    expect(text()).toContain('Status: Expired');
    expect(text()).toContain('Customer response');
    expect(text()).toContain('Declined · Sofia Martinez');
    expect(text()).toContain('Too expensive');
    expect(text()).not.toContain('OLD VERSION QUESTION');

    page.selectVersion(1);
    quote('/versions/1').flush(version(1));
    await settle();
    expect(text()).toContain('Question · Sofia Martinez');
    expect(text()).toContain('OLD VERSION QUESTION');

    TestBed.resetTestingModule();
    await setup('viewer', detail());
    expect(text()).toContain('No response from the customer yet.');
  });

  it.each(['viewer', 'operations_manager', 'accounting'])(
    '%s sees the same data with no actions (quote-builder AC-25)',
    async (role) => {
      await setup(role, detail({ draft: { ...calculationDraft(), versionNo: 3 } }));

      expect(text()).toContain('SECRET NOTE');
      expect(text()).toContain('Version 3 is being revised.');
      for (const label of ['Revise quote', 'Continue editing', 'Resend email']) {
        expect(control(label)).toBeUndefined();
      }
    },
  );

  it.each<[string, boolean, QuoteDetail['workOrder'], QuoteDetail['status'], string | null]>([
    ['owner', true, null, 'approved', 'Create work order'],
    [
      'dispatcher',
      true,
      { id: 'wo-1', displayNumber: 'WO-7', status: 'draft' },
      'approved',
      'Continue draft',
    ],
    [
      'operations_manager',
      true,
      { id: 'wo-1', displayNumber: 'WO-7', status: 'ready_to_schedule' },
      'approved',
      'View job',
    ],
    [
      'viewer',
      false,
      { id: 'wo-1', displayNumber: 'WO-7', status: 'ready_to_schedule' },
      'approved',
      'View job',
    ],
    ['viewer', false, null, 'approved', null],
    ['owner', true, null, 'sent', null],
  ])(
    '%s (manage work orders = %s) with work order %j on a %s quote sees the action %s (create-work-order FR-01, BR-04, AC-01)',
    async (role, manage, workOrder, status, label) => {
      await setup(
        role,
        detail({ status, displayStatus: status, workOrder, canManageWorkOrders: manage }),
      );

      const actions = ['Create work order', 'Continue draft', 'View job'];
      expect(actions.filter((action) => control(action) !== undefined)).toEqual(
        label === null ? [] : [label],
      );
      if (label === 'View job') {
        expect(control(label)?.getAttribute('href')).toBe('/jobs/wo-1');
      } else if (label !== null) {
        expect(control(label)?.getAttribute('href')).toBe(`/quotes/${QUOTE_ID}/work-order`);
      }
    },
  );

  it.each(['technician', 'custom_role'])(
    '%s is forbidden with no calls (quote-builder AC-26)',
    async (role) => {
      await setup(role, null);

      expect(text()).toContain(VIEW_FORBIDDEN_MESSAGE);
    },
  );

  it('redirects managers of a never-sent quote to the editor and tells read roles it was not sent (quote-builder BR-30)', async () => {
    const unsent = detail({
      status: 'draft',
      sentVersions: [],
      draft: { ...calculationDraft(), versionNo: 1 },
    });
    await setup('owner', unsent);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(`/quotes/${QUOTE_ID}/edit`));

    TestBed.resetTestingModule();
    await setup('viewer', unsent);
    expect(text()).toContain(NOT_SENT_MESSAGE);
    expect(text()).toContain('SECRET NOTE');
    expect(control('Continue editing')).toBeUndefined();
  });
});

function calculationDraft(): NonNullable<QuoteDetail['draft']> {
  return {
    versionNo: 1,
    lines: [],
    discountTotal: 0,
    customerMessage: null,
    internalNote: 'SECRET NOTE',
    terms: { preset: 'due_on_completion' },
    validUntil: '2026-11-04',
    calculation,
  };
}
