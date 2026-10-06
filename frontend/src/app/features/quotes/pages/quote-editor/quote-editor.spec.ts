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
import { quoteEditorUnsavedChangesGuard } from '../../guards/quote-editor-unsaved-changes.guard';
import {
  Calculation,
  EDITOR_FORBIDDEN_MESSAGE,
  QUOTE_SAVE_FAILED_MESSAGE,
  QUOTE_UNAVAILABLE_MESSAGE,
  QuoteDetail,
} from '../../models/quote.model';
import { QuoteEditor } from './quote-editor';

const API = 'http://api.test';
const QUOTE_ID = '33333333-3333-4333-8333-333333333333';
const REQUEST_ID = '11111111-1111-4111-8111-111111111111';
/** Saved just now: the header reads "Just now". */
const UPDATED_AT = new Date().toISOString();

@Component({ template: '<p>stub</p>' })
class Stub {}

const calc = (price: number, total = price): Calculation => ({
  lines: [{ lineSubtotal: price, discountShare: 0, taxRate: 0, lineTax: 0, lineTotal: total }],
  subtotal: price,
  discountTotal: 0,
  taxLabel: 'Tax',
  taxTotal: 0,
  total,
  currency: 'USD',
  margin: { percent: 50, grossProfit: price / 2 },
});

const detail = (patch: Partial<QuoteDetail> = {}): QuoteDetail => ({
  id: QUOTE_ID,
  number: 2036,
  displayNumber: 'Q-2036',
  status: 'draft',
  displayStatus: 'draft',
  responses: [],
  updatedAt: UPDATED_AT,
  canManage: true,
  request: {
    id: REQUEST_ID,
    displayNumber: 'REQ-1048',
    title: 'Kitchen sink leak',
    category: 'Plumbing',
    status: 'ready_for_quote',
  },
  customer: {
    id: 'c-1',
    name: 'Sofia Martinez',
    phone: '(512) 555-0198',
    email: 'sofia@example.com',
    address: '1842 Oak Street',
  },
  recipient: { email: 'sofia@example.com' },
  completedAssessment: null,
  organization: { name: 'Northstar', currency: 'USD', defaultTaxRate: 8.25 },
  draft: {
    versionNo: 1,
    lines: [
      {
        catalogItemId: null,
        type: 'service',
        name: 'Leak repair labor',
        description: '',
        quantity: 2,
        unit: 'hr',
        unitPrice: 95,
        unitCost: 40,
        taxable: true,
        isOptional: false,
      },
    ],
    discountTotal: 0,
    customerMessage: 'We will fix it',
    internalNote: 'SECRET NOTE',
    terms: { preset: 'net_30' },
    validUntil: '2026-11-04',
    calculation: calc(190),
  },
  sentVersions: [],
  ...patch,
});

describe('Quote editor page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: QuoteEditor;

  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(document.body.querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const quote = (method: string, path: string): TestRequest =>
    httpTesting.expectOne(
      (r) => r.method === method && r.url === `${API}/quotes/${QUOTE_ID}${path}`,
    );
  const calcCall = (): Promise<TestRequest> =>
    vi.waitFor(() => quote('POST', '/calculate'), { timeout: 2000, interval: 50 });
  const dialog = (): HTMLElement | null =>
    document.querySelector('[role="alertdialog"][aria-modal]');
  const lineUid = (): string => page.lines()[0].uid;
  const edit = async (
    patch: Parameters<QuoteEditor['onLineChange']>[0]['patch'],
  ): Promise<void> => {
    page.onLineChange({ uid: lineUid(), patch });
    await settle();
  };
  const router = (): Router => TestBed.inject(Router);

  async function setup(
    role: string,
    options: { response?: QuoteDetail | 'not-found' | null; id?: string } = {},
  ): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: 'quotes/:quoteId/edit',
            component: QuoteEditor,
            canDeactivate: [quoteEditorUnsavedChangesGuard],
          },
          { path: 'quotes/:quoteId', component: Stub },
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
    page = await harness.navigateByUrl(`/quotes/${options.id ?? QUOTE_ID}/edit`, QuoteEditor);
    const response = options.response;
    if (response === 'not-found') {
      quote('GET', '').flush(null, { status: 404, statusText: 'Not Found' });
    } else if (response !== null && response !== undefined) {
      quote('GET', '').flush(response);
    }
    await settle();
  }

  afterEach(() => httpTesting?.verify());

  it.each<[string, string, QuoteDetail | 'not-found' | null, string]>([
    ['viewer', QUOTE_ID, null, EDITOR_FORBIDDEN_MESSAGE],
    ['accounting', QUOTE_ID, null, EDITOR_FORBIDDEN_MESSAGE],
    ['technician', QUOTE_ID, null, EDITOR_FORBIDDEN_MESSAGE],
    ['custom_role', QUOTE_ID, null, EDITOR_FORBIDDEN_MESSAGE],
    ['owner', 'not-a-uuid', null, QUOTE_UNAVAILABLE_MESSAGE],
    ['owner', QUOTE_ID, 'not-found', QUOTE_UNAVAILABLE_MESSAGE],
  ])(
    '%s opening %s sees a state instead of the editor; read roles and technicians make no calls (quote-builder AC-25, AC-26)',
    async (role, id, response, message) => {
      await setup(role, { id, response });

      expect(text()).toContain(message);
      expect(button('Save draft')).toBeUndefined();
      expect(document.activeElement?.tagName).toBe('H1');
      // `verify()` proves no request at all for the role and id checks.
    },
  );

  it('tracks dirty against the loaded baseline and recalculates after a debounce: one call for a burst, cancellation, "—" and no call for invalid input (quote-builder FR-05, BR-23, AC-13)', async () => {
    await setup('owner', { response: detail() });

    // Loaded: the saved calculation is shown, nothing is dirty and no call is made.
    expect(page.dirty()).toBe(false);
    expect(text()).toContain('Draft saved');
    expect(text()).toContain('Just now');
    expect(text()).toContain('$190.00');
    expect(text()).toContain('Tax');
    expect(text()).toContain('Estimated margin');

    // A burst of edits makes the form dirty, keeps the previous values and calls once.
    await edit({ unitPrice: 100 });
    await edit({ unitPrice: 110 });
    expect(page.dirty()).toBe(true);
    expect(page.calc().status).toBe('loading');
    expect(text()).toContain('Unsaved changes');
    expect(text()).toContain('$190.00');
    const first = await calcCall();
    expect((first.request.body as { lines: object[] }).lines[0]).not.toHaveProperty('uid');
    expect(first.request.body).not.toHaveProperty('updatedAt');

    // A newer edit cancels the call still in flight; the backend values are displayed as sent.
    await edit({ unitPrice: 120 });
    const second = await calcCall();
    expect(first.cancelled).toBe(true);
    second.flush(calc(240, 260.4));
    await settle();
    expect(page.calc().status).toBe('ready');
    expect(text()).toContain('$240.00');
    expect(text()).toContain('$260.40');

    // Editing back to the loaded value is not dirty and needs no call.
    await edit({ unitPrice: 95 });
    expect(page.dirty()).toBe(false);
    const third = await calcCall();
    third.flush(calc(190));
    await settle();

    // An invalid line shows "—" and never calls the backend.
    await edit({ quantity: 0 });
    await new Promise((resolve) => setTimeout(resolve, 600));
    httpTesting.expectNone((r) => r.url.endsWith('/calculate'));
    expect(page.calc().status).toBe('idle');
    expect((document.body.querySelector('output') as HTMLElement).textContent?.trim()).toBe('—');
    expect(text()).toContain('—');
  });

  it('saves with updatedAt and resets the baseline, and maps 409 quote_changed, 409 no_draft, 400 line keys and failures without losing data (quote-builder FR-07, BR-22, AC-08, AC-14)', async () => {
    await setup('owner', { response: detail() });

    // Save is disabled with no changes.
    expect(button('Save draft')!.disabled).toBe(true);
    await edit({ name: 'Leak repair' });
    expect(button('Save draft')!.disabled).toBe(false);
    (await calcCall()).flush(calc(190));

    // 400 on a line key goes to that control and moves focus to it.
    page.save();
    let put = quote('PUT', '/draft');
    expect(put.request.body).toMatchObject({
      updatedAt: UPDATED_AT,
      lines: [{ name: 'Leak repair' }],
      terms: { preset: 'net_30' },
    });
    put.flush(
      { errors: { 'lines[0].name': ['Enter an item name.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();
    expect(text()).toContain('Enter an item name.');
    await vi.waitFor(() =>
      expect(document.activeElement?.getAttribute('data-field-key')).toBe(`${lineUid()}.name`),
    );
    expect(page.dirty()).toBe(true);

    // 409 quote_changed: warning toast with a Refresh action; the form keeps its data.
    page.save();
    put = quote('PUT', '/draft');
    put.flush(
      { title: 'backend text', code: 'quote_changed' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(text()).toContain('This quote changed. Refresh to see the latest.');
    expect(text()).not.toContain('backend text');
    expect(button('Refresh')).toBeDefined();
    expect(page.dirty()).toBe(true);

    // Other failures: generic copy, data kept.
    page.save();
    quote('PUT', '/draft').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain(QUOTE_SAVE_FAILED_MESSAGE);
    expect(page.dirty()).toBe(true);

    // Success: the response replaces the form, the baseline resets and the toast shows.
    page.save();
    const saved = detail({
      updatedAt: '2099-01-01T00:00:00Z',
      draft: { ...detail().draft!, lines: [{ ...detail().draft!.lines[0], name: 'Leak repair' }] },
    });
    quote('PUT', '/draft').flush(saved);
    await settle();
    expect(text()).toContain('Draft saved.');
    expect(page.dirty()).toBe(false);
    expect(page.lines()[0].name).toBe('Leak repair');

    // 409 no_draft: the quote has no draft any more, so the page moves to the read-only view.
    await edit({ name: 'Another' });
    (await calcCall()).flush(calc(190));
    page.save();
    quote('PUT', '/draft').flush({ code: 'no_draft' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => expect(router().url).toBe(`/quotes/${QUOTE_ID}`));
  });

  it('opens the customer preview from the current form without internal data, then sends after the confirm dialog (quote-builder FR-08, FR-09, BR-23, BR-24, AC-16, AC-17, AC-19)', async () => {
    await setup('dispatcher', { response: detail() });

    // Preview shows the customer-visible content only.
    await edit({ unitPrice: 100 });
    button('Preview')!.click();
    await settle();
    expect(document.body.querySelector('p-dialog [aria-busy="true"]')).not.toBeNull();
    (await calcCall()).flush(calc(200));
    await settle();
    const preview = document.body.querySelector('app-quote-customer-view')!.textContent ?? '';
    expect(preview).toContain('Quote Q-2036 · Version 1');
    expect(preview).toContain('$200.00');
    expect(preview).toContain('Payment due within 30 days of the invoice date.');
    expect(preview).toContain('Valid until Nov 4, 2026');
    expect(preview).toContain('We will fix it');
    for (const internal of ['SECRET NOTE', 'Estimated margin', 'Unit cost', 'Internal']) {
      expect(preview).not.toContain(internal);
    }

    // Send validates first: an empty message blocks the confirm dialog and keeps focus on it.
    page.onEmailMessage('');
    await settle();
    page.send();
    await settle();
    expect(text()).toContain('Enter a message.');
    expect(dialog()).toBeNull();
    await vi.waitFor(() => expect(document.activeElement?.id).toBe('quote-email-message'));

    page.onEmailMessage('Hi Sofia, here is your quote.');
    page.send();
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Send quote?'));
    expect(dialog()?.textContent).toContain(
      "Sofia Martinez will receive version 1 of Q-2036 at sofia@example.com. A sent version can't be edited; later changes create a new version.",
    );
    dialog()!
      .querySelectorAll('button')
      .forEach((candidate) => candidate.textContent?.trim() === 'Send quote' && candidate.click());
    const send = quote('POST', '/send');
    expect(send.request.body).toMatchObject({
      updatedAt: UPDATED_AT,
      emailMessage: 'Hi Sofia, here is your quote.',
      validUntil: '2026-11-04',
    });
    send.flush({ quote: detail({ status: 'sent', draft: null }), emailStatus: 'failed' });
    await vi.waitFor(() => expect(router().url).toBe(`/quotes/${QUOTE_ID}`));
    expect(router().lastSuccessfulNavigation()?.extras.state?.['toast']).toEqual({
      severity: 'warn',
      summary: "Quote sent, but we couldn't email it. Use Resend email to try again.",
    });
  });

  it('asks before leaving with unsaved changes, disables Send without a recipient and discards a draft after confirming (quote-builder FR-07, FR-14, BR-21, BR-29, AC-15, AC-19, AC-22)', async () => {
    await setup('owner', {
      response: detail({
        recipient: { email: null },
        customer: { ...detail().customer, email: null },
      }),
    });

    // No recipient: Send is disabled and exposes its helper text; SMS is disabled.
    expect(button('Send quote')!.disabled).toBe(true);
    expect(text()).toContain('This customer has no email address.');
    expect(text()).toContain("SMS isn't available yet.");
    expect(text()).not.toContain('Require deposit');

    // Clean form: leaving shows no dialog.
    // Dirty form: the shared discard dialog appears; keeping the edits stays, discarding leaves.
    await edit({ name: 'Changed' });
    (await calcCall()).flush(calc(190));
    const leaving = router().navigateByUrl('/requests');
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Discard unsaved changes?'));
    button('Keep editing')!.click();
    expect(await leaving).toBe(false);
    expect(router().url).toBe(`/quotes/${QUOTE_ID}/edit`);
    expect(page.dirty()).toBe(true);

    // Discard draft (version 1) cancels the quote after the confirm dialog and returns to the request.
    document.body.querySelector<HTMLElement>('[aria-label="More actions"]')!.click();
    await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
    const item = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]')).find(
      (candidate) => candidate.textContent?.trim() === 'Discard draft',
    )!;
    (item.querySelector<HTMLElement>('a, .p-menu-item-link') ?? item).click();
    await vi.waitFor(() => expect(dialog()?.textContent).toContain('Discard draft?'));
    expect(dialog()?.textContent).toContain(
      "The quote Q-2036 will be cancelled. Its number won't be reused.",
    );
    Array.from(dialog()!.querySelectorAll('button'))
      .find((candidate) => candidate.textContent?.trim() === 'Discard draft')!
      .click();
    quote('POST', '/discard-draft').flush({
      quoteId: QUOTE_ID,
      status: 'cancelled',
      requestId: REQUEST_ID,
    });
    await vi.waitFor(() => expect(router().url).toBe(`/requests?request=${REQUEST_ID}`));
    expect(router().lastSuccessfulNavigation()?.extras.state?.['toast']).toEqual({
      severity: 'success',
      summary: 'Draft discarded.',
    });
  });
});
