import { Location } from '@angular/common';
import { HttpRequest, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../../../../app.routes';
import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { PublicQuote, PublicTotals } from '../../models/public-quote.model';
import { QuoteApproval } from './quote-approval';

const API = 'http://api.test';
const BASE = `${API}/public/quote-links`;
const STORAGE_KEY = 'fieldops.quote-link-token';
const TOKEN = 'Abc_123-'.repeat(5) + 'xyz';
const OPTIONAL_ID = '44444444-4444-4444-8444-444444444444';

const totals = (total: number): PublicTotals => ({
  subtotal: total,
  discountTotal: 0,
  taxLabel: 'Tax (8.25%)',
  taxTotal: 0,
  total,
  currency: 'USD',
});

const quote = (patch: Partial<PublicQuote> = {}): PublicQuote => ({
  organization: { name: 'Northstar Home Services', phone: '(512) 555-0142', hasLogo: false },
  quote: {
    displayNumber: 'Q-2036',
    versionNo: 1,
    status: 'sent',
    sentOn: '2026-09-21',
    validUntil: '2026-10-05',
    scope: 'kitchen sink repair',
    customerMessage: null,
    terms: 'Payment due upon completion.',
  },
  customer: { name: 'Sofia Martinez', address: '1842 Oak Street' },
  scopeItems: ['Replace P-trap connection'],
  lines: [
    {
      id: null,
      name: 'Leak repair labor',
      description: '',
      quantity: 2,
      unit: 'hr',
      unitPrice: 95,
      lineSubtotal: 190,
      isOptional: false,
    },
    {
      id: OPTIONAL_ID,
      name: 'Shutoff valve upgrade',
      description: '',
      quantity: 1,
      unit: 'unit',
      unitPrice: 50,
      lineSubtotal: 50,
      isOptional: true,
    },
  ],
  versionTotals: totals(190),
  photos: [],
  progress: { requestSubmittedOn: '2026-09-19', assessmentCompletedOn: '2026-09-21' },
  clarification: null,
  response: null,
  ...patch,
});

const finalQuote = (type: 'approved' | 'rejected'): PublicQuote =>
  quote({
    quote: { ...quote().quote, status: type },
    response: {
      type,
      respondedOn: '2026-10-07',
      selectedOptionalLineIds: type === 'approved' ? [OPTIONAL_ID] : [],
      totals: type === 'approved' ? totals(240) : null,
    },
  });

describe('QuoteApproval', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let urlAtRequest: string[];

  async function setup(url: string): Promise<void> {
    urlAtRequest = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter(routes),
        provideHttpClient(
          withInterceptors([
            (request: HttpRequest<unknown>, next) => {
              urlAtRequest.push(TestBed.inject(Location).path(true));
              return next(request);
            },
            authInterceptor,
            errorInterceptor,
          ]),
        ),
        provideHttpClientTesting(),
      ],
    });
    harness = await RouterTestingHarness.create();
    httpTesting = TestBed.inject(HttpTestingController);
    await harness.navigateByUrl(url, QuoteApproval);
    await stable();
  }

  const stable = () => harness.fixture.whenStable();
  const text = () => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const buttons = (label: string) =>
    Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).filter(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const button = (label: string) => buttons(label)[0];
  /** The dialog is appended after the page, so its buttons come last. */
  const lastButton = (label: string) => buttons(label).at(-1);
  const request = (path: string): Promise<TestRequest> =>
    vi.waitFor(() => httpTesting.expectOne(`${BASE}/${path}`));
  async function respond(pending: TestRequest, body: object | null, status = 200): Promise<void> {
    pending.flush(body, { status, statusText: status === 200 ? 'OK' : 'Error' });
    await stable();
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    httpTesting.verify();
    sessionStorage.clear();
  });

  it('keeps an old /quote-approval link working: captures the fragment token, replaces the URL before any request and sends the token only in the POST body (FR-01, AC-01)', async () => {
    await setup(`/quote-approval#token=${TOKEN}&other=1`);

    const view = await request('view');
    expect(view.request.method).toBe('POST');
    expect(view.request.body).toEqual({ token: TOKEN });
    expect(view.request.url).not.toContain(TOKEN);
    expect(urlAtRequest).toEqual(['/quotes/view']);
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
    expect(TestBed.inject(Location).path(true)).toBe('/quotes/view');
    await respond(view, quote());
    expect(text()).toContain('Your quote is ready');
    expect(text()).not.toContain('available soon');
  });

  it.each([
    ['no token', null, 0, "This link isn't available."],
    ['unknown or expired token', 404, 404, "This quote link isn't available."],
    ['superseded version', 410, 410, 'A newer version of this quote is available.'],
    ['rate limited', 429, 429, 'Too many attempts. Please wait a few minutes and try again.'],
    ['server failure', 500, 500, "We couldn't load this quote."],
  ])(
    'shows the %s state without quote data and focuses the heading (FR-02, FR-03, AC-02, AC-03, AC-04, AC-23)',
    async (_name, _code, status, expected) => {
      await setup(_code === null ? '/quotes/view' : `/quotes/view#token=${TOKEN}`);
      if (status !== 0) {
        await respond(await request('view'), { title: 'backend text', code: 'x' }, status);
      }

      expect(text()).toContain(expected);
      expect(text()).not.toContain('backend text');
      expect(text()).not.toContain('Northstar');
      expect(document.activeElement?.tagName).toBe('H1');
      if (status === 429 || status === 500) {
        expect(button('Try again')).toBeDefined();
      }
    },
  );

  it('keeps Approve disabled until the box is checked and while totals are pending, shows only server totals, reverts a failed toggle and approves with the selection (FR-05, FR-06, AC-09, AC-10, AC-12, AC-13)', async () => {
    await setup(`/quotes/view#token=${TOKEN}`);
    await respond(await request('view'), quote());

    const approve = () => button('Approve quote') as HTMLButtonElement;
    const toggle = () =>
      document.body.querySelector<HTMLInputElement>('#optional-' + OPTIONAL_ID) as HTMLInputElement;
    const accept = () => document.body.querySelector<HTMLInputElement>('#accept-terms')!;

    expect(approve().disabled).toBe(true);
    expect(approve().getAttribute('aria-describedby')).toBe('approve-hint');
    expect(text()).toContain('Check the box to approve.');
    accept().click();
    await stable();
    expect(approve().disabled).toBe(false);

    // Pending calculation disables Approve; the displayed total is whatever the server returns.
    toggle().click();
    await stable();
    expect(approve().disabled).toBe(true);
    const calculation = await request('calculate');
    expect(calculation.request.body).toEqual({
      token: TOKEN,
      selectedOptionalLineIds: [OPTIONAL_ID],
    });
    await respond(calculation, totals(240.01));
    expect(text()).toContain('$240.01');
    expect(approve().disabled).toBe(false);

    // A failed calculation reverts the toggle and explains it.
    toggle().click();
    await stable();
    await respond(await request('calculate'), { title: 'x' }, 400);
    expect(toggle().checked).toBe(true);
    expect(text()).toContain("We couldn't update the total. Please try again.");
    expect(text()).toContain('$240.01');

    approve().click();
    const approval = await request('approve');
    expect(approval.request.body).toEqual({
      token: TOKEN,
      selectedOptionalLineIds: [OPTIONAL_ID],
      acceptTerms: true,
    });
    await respond(approval, finalQuote('approved'));
    expect(text()).toContain('Approved on Oct 7, 2026');
    expect(text()).toContain('$240.00');
    expect(button('Approve quote')).toBeUndefined();
    expect(button('Decline quote')).toBeUndefined();

    // PDF failure is explained and does not change the state.
    button('Download PDF')!.click();
    await respond(await request('pdf'), null, 500);
    expect(text()).toContain("We couldn't download the PDF. Please try again.");
  });

  it('validates the Decline dialog inline without a request, and a 409 reloads the final state with the toast (FR-07, FR-09, AC-14, AC-20)', async () => {
    await setup(`/quotes/view#token=${TOKEN}`);
    await respond(await request('view'), quote());

    button('Decline quote')!.click();
    await stable();
    expect(text()).toContain('Decline this quote?');
    lastButton('Decline quote')!.click();
    await stable();
    expect(text()).toContain("Tell us why you're declining.");

    const reason = document.body.querySelector<HTMLTextAreaElement>('#public-quote-text')!;
    reason.value = 'Too expensive';
    reason.dispatchEvent(new Event('input'));
    await stable();
    lastButton('Decline quote')!.click();
    const decline = await request('decline');
    expect(decline.request.body).toEqual({ token: TOKEN, reason: 'Too expensive' });
    await respond(decline, { title: 'x', code: 'quote_already_answered' }, 409);

    expect(text()).toContain('This quote has already been answered.');
    await respond(await request('view'), finalQuote('rejected'));
    expect(text()).toContain('Declined on Oct 7, 2026');
    expect(text()).toContain('Your decision was sent to Northstar Home Services.');
    expect(button('Approve quote')).toBeUndefined();
  });
});
