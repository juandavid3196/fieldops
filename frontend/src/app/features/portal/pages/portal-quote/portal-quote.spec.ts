import { Location } from '@angular/common';
import { HttpRequest, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { authInterceptor } from '../../../../core/interceptors/auth.interceptor';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { InvoiceLinkApi } from '../../../invoices/services/invoice-link-api';
import { paidInvoiceBody } from '../../../invoices/testing/invoice-fixtures';
import { PublicQuote } from '../../../quotes/models/public-quote.model';
import { QuoteLinkApi } from '../../../quotes/services/quote-link-api';
import { PortalInvoice } from '../portal-invoice/portal-invoice';
import { PortalQuote } from './portal-quote';

const API = 'http://api.test';

const totals = {
  subtotal: 190,
  discountTotal: 0,
  taxLabel: 'Tax (8.25%)',
  taxTotal: 0,
  total: 190,
  currency: 'USD',
};

const quote = (status: PublicQuote['quote']['status'] = 'sent'): PublicQuote => ({
  organization: { name: 'Northstar Home Services', phone: '(512) 555-0142', hasLogo: false },
  quote: {
    displayNumber: 'Q-2036',
    versionNo: 1,
    status,
    sentOn: '2026-09-21',
    validUntil: '2026-10-05',
    scope: 'kitchen sink repair',
    customerMessage: null,
    terms: null,
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
  ],
  versionTotals: totals,
  photos: [],
  progress: { requestSubmittedOn: '2026-09-19', assessmentCompletedOn: '2026-09-21' },
  clarification: null,
  response: null,
});

describe('portal quote and invoice pages', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let urlAtRequest: string[];

  async function setup(url: string): Promise<void> {
    urlAtRequest = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'portal/quotes/:quoteId', component: PortalQuote },
          { path: 'portal/invoices/:invoiceId', component: PortalInvoice },
        ]),
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
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();
  }

  const settle = async (): Promise<void> => {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  };
  const text = () => (harness.fixture.nativeElement.textContent ?? '').replace(/\s+/g, ' ').trim();

  beforeEach(() => sessionStorage.clear());
  afterEach(() => httpTesting.verify());

  it('serves the quote through /portal/quotes/{id} with the session, never a token, and shows expired read-only', async () => {
    await setup('/portal/quotes/q-1?stale=1#token=should-be-ignored');
    const view = httpTesting.expectOne(`${API}/portal/quotes/q-1`);
    expect(view.request.method).toBe('GET');
    expect(view.request.withCredentials).toBe(true);
    // Embedded: nothing is captured from the fragment and the address bar is left alone.
    expect(urlAtRequest[0]).toContain('#token=should-be-ignored');
    expect(sessionStorage.getItem('fieldops.quote-link-token')).toBeNull();
    view.flush(quote());
    await settle();
    expect(text()).toContain('Q-2036');
    expect(text()).not.toContain('Sign in to your portal');
    expect(text()).not.toContain('Powered by FieldOps');
    expect(text()).toContain('Back to Quotes');

    // Every action uses the id from the route, JSON bodies without a token.
    const api = harness.routeDebugElement!.injector.get(QuoteLinkApi);
    api.calculate(['l-1']).subscribe();
    api.approve(['l-1']).subscribe();
    api.decline('too expensive').subscribe();
    api.askQuestion('When can you start?').subscribe();
    api.pdf().subscribe();
    api.photo('ph 1').subscribe();
    api.logo().subscribe();
    const expected: [string, string, unknown][] = [
      ['POST', 'portal/quotes/q-1/calculate', { selectedOptionalLineIds: ['l-1'] }],
      [
        'POST',
        'portal/quotes/q-1/approve',
        { selectedOptionalLineIds: ['l-1'], acceptTerms: true },
      ],
      ['POST', 'portal/quotes/q-1/reject', { reason: 'too expensive' }],
      ['POST', 'portal/quotes/q-1/clarification', { message: 'When can you start?' }],
      ['GET', 'portal/quotes/q-1/pdf', null],
      ['GET', 'portal/quotes/q-1/photos/ph%201/content', null],
      ['GET', 'portal/organization/logo', null],
    ];
    for (const [method, path, body] of expected) {
      const request = httpTesting.expectOne(`${API}/${path}`);
      expect(request.request.method).toBe(method);
      expect(request.request.body).toEqual(body);
      request.flush(method === 'POST' ? quote() : new Blob());
    }

    // An approve answered with 409 quote_expired reloads the read-only expired quote.
    harness.routeDebugElement!.injector.get(QuoteLinkApi).view().subscribe();
    httpTesting.expectOne(`${API}/portal/quotes/q-1`).flush(quote('expired'));
    TestBed.resetTestingModule();
    await setup('/portal/quotes/q-2');
    httpTesting.expectOne(`${API}/portal/quotes/q-2`).flush(quote('expired'));
    await settle();
    expect(text()).toContain(
      'This quote has expired. Contact Northstar Home Services for an updated quote.',
    );
    expect(text()).toContain('Expired');
    expect(harness.fixture.nativeElement.querySelector('p-checkbox')).toBeNull();
  });

  it('shows "This item isn\'t available." with a way back on a 404 and serves the invoice by id', async () => {
    await setup('/portal/quotes/foreign');
    httpTesting
      .expectOne(`${API}/portal/quotes/foreign`)
      .flush({ code: 'portal_resource_unavailable' }, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain("This item isn't available.");
    expect(text()).toContain('Back to Quotes');
    TestBed.resetTestingModule();

    await setup('/portal/invoices/i-1?payment_intent=pi_1&redirect_status=succeeded');
    // Stripe return parameters are dropped from the address bar (never read or sent).
    httpTesting.expectOne(`${API}/portal/invoices/i-1`).flush(paidInvoiceBody());
    await settle();
    expect(TestBed.inject(Location).path()).toBe('/portal/invoices/i-1');
    expect(text()).not.toContain('Sign in to your portal');
    expect(text()).not.toContain('Powered by FieldOps');
    expect(text()).toContain('Back to Invoices');

    const api = harness.routeDebugElement!.injector.get(InvoiceLinkApi);
    expect(api.returnPath()).toBe('/portal/invoices/i-1');
    api.cardIntent('key-1').subscribe();
    api.paymentStatus('att 1').subscribe();
    api.bankTransferNotice('key-2').subscribe();
    api.receipt('pay-1').subscribe();
    api.completionReport().subscribe();
    api.pdf().subscribe();
    api.photos().subscribe();
    api.photoContent('ph-1').subscribe();
    api.submitReview(5, 'Great').subscribe();
    const expected: [string, string, unknown][] = [
      ['POST', 'portal/invoices/i-1/payment-attempts', { idempotencyKey: 'key-1' }],
      ['GET', 'portal/invoices/i-1/payment-attempts/att%201', null],
      ['POST', 'portal/invoices/i-1/bank-transfer-notice', { idempotencyKey: 'key-2' }],
      ['GET', 'portal/invoices/i-1/payments/pay-1/receipt', null],
      ['GET', 'portal/invoices/i-1/completion-report', null],
      ['GET', 'portal/invoices/i-1/pdf', null],
      ['GET', 'portal/invoices/i-1/photos', null],
      ['GET', 'portal/invoices/i-1/photos/ph-1/content', null],
      ['POST', 'portal/invoices/i-1/review', { rating: 5, comment: 'Great' }],
    ];
    for (const [method, path, body] of expected) {
      const request = httpTesting.expectOne(`${API}/${path}`);
      expect(request.request.method).toBe(method);
      expect(request.request.body).toEqual(body);
      request.flush(method === 'POST' || path.endsWith('/photos') ? [] : new Blob());
    }
    TestBed.resetTestingModule();

    await setup('/portal/invoices/foreign');
    httpTesting
      .expectOne(`${API}/portal/invoices/foreign`)
      .flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain("This item isn't available.");
    expect(text()).toContain('Back to Invoices');
  });
});
