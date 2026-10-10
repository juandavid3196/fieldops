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
import {
  CARD_DECLINED_MESSAGE,
  CARD_EXPIRED_MESSAGE,
  CARD_OTHER_FAILURE_MESSAGE,
  CARD_START_FAILED_MESSAGE,
  CARD_STILL_CONFIRMING_MESSAGE,
  PUBLIC_RATE_LIMITED_MESSAGE,
  PublicInvoice,
} from '../../models/invoice.model';
import { STRIPE_PAYMENT_ADAPTER } from '../../services/stripe-payment.adapter';
import { FakeStripePaymentAdapter } from '../../services/testing/fake-stripe-payment.adapter';
import { cardPayment, paidInvoiceBody, publicInvoiceBody } from '../../testing/invoice-fixtures';
import { PublicInvoicePage } from './public-invoice';

const API = 'http://api.test';
const BASE = `${API}/public/invoice-links`;
const STORAGE_KEY = 'fieldops.invoice-link-token';
const ATTEMPT_KEY = 'fieldops.invoice-link-attempt';
const TOKEN = 'Abc_123-'.repeat(5) + 'xyz';
const STRIPE_PARAMS =
  'payment_intent=pi_secret_1&payment_intent_client_secret=cs_secret_1&redirect_status=succeeded';

const INTENT = {
  attemptId: 'att-1',
  clientSecret: 'cs_test_1',
  amount: 287.3,
  currency: 'USD',
  status: 'pending',
};
const attemptState = (
  status: string,
  failureCategory: string | null = null,
  attemptId = 'att-1',
) => ({
  attemptId,
  status,
  failureCategory,
  payment: status === 'succeeded' ? cardPayment() : null,
  invoice: { status: 'paid', amountPaid: 287.3, balanceDue: 0 },
});

describe('PublicInvoicePage', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let urlAtRequest: string[];
  let fake: FakeStripePaymentAdapter;
  let downloads: string[];

  async function setup(url: string): Promise<void> {
    urlAtRequest = [];
    fake = new FakeStripePaymentAdapter();
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        { provide: STRIPE_PAYMENT_ADAPTER, useValue: fake },
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
    await harness.navigateByUrl(url, PublicInvoicePage);
    await stable();
  }

  /** Verifies and discards the current app so a test can run several scenarios. */
  function teardown(): void {
    httpTesting.verify();
    TestBed.resetTestingModule();
    sessionStorage.clear();
  }

  const stable = async (): Promise<void> => {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  };
  /** Advances the fake `setInterval` clock (RxJS polling) and lets Angular settle. */
  const tick = async (ms: number): Promise<void> => {
    if (vi.isFakeTimers()) {
      await vi.advanceTimersByTimeAsync(ms);
    }
    await stable();
  };
  const text = () => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const button = (label: string) =>
    Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  const click = async (label: string): Promise<void> => {
    button(label)!.click();
    await tick(0);
  };
  const input = (selector: string) => document.body.querySelector<HTMLInputElement>(selector)!;
  const type = async (selector: string, value: string): Promise<void> => {
    const field = input(selector);
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await stable();
  };
  const request = (path: string): TestRequest => httpTesting.expectOne(`${BASE}/${path}`);
  async function respond(pending: TestRequest, body: object | null, status = 200): Promise<void> {
    pending.flush(body, { status, statusText: status < 400 ? 'OK' : 'Error' });
    await tick(0);
  }
  async function load(
    invoice: PublicInvoice,
    url = `/invoices/view#token=${TOKEN}`,
  ): Promise<void> {
    await setup(url);
    await respond(request('view'), invoice);
  }
  /** Fills the name, presses Pay and answers the card-intent request. */
  async function pay(intent: object | null, status = 201): Promise<string> {
    await type('#cardholder-name', 'Sofia Martinez');
    await click('Pay $287.30');
    const intentRequest = request('payments/card-intent');
    const key = (intentRequest.request.body as { idempotencyKey: string }).idempotencyKey;
    await respond(intentRequest, intent, status);
    return key;
  }

  beforeEach(() => {
    sessionStorage.clear();
    downloads = [];
    URL.createObjectURL = vi.fn(() => 'blob:public');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });
  });
  afterEach(() => {
    httpTesting.verify();
    sessionStorage.clear();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('captures the token and drops Stripe parameters before any request, then renders the invoice, report, photos, payment methods, bank notice, cash contacts and neutral states (FR-15, AC-20, AC-21)', async () => {
    // Token and Stripe return parameters leave the URL before the request; the token travels in the body only.
    await setup(`/invoices/view?${STRIPE_PARAMS}#token=${TOKEN}&other=1`);
    const view = request('view');
    expect(view.request.method).toBe('POST');
    expect(view.request.body).toEqual({ token: TOKEN });
    expect(view.request.url).not.toContain(TOKEN);
    expect(urlAtRequest).toEqual(['/invoices/view']);
    expect(TestBed.inject(Location).path(true)).toBe('/invoices/view');
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe(TOKEN);
    await respond(view, publicInvoiceBody());

    for (const expected of [
      'Northstar Home Services',
      'Invoice INV-1048',
      'Sent',
      'Kitchen sink leak repair · Completed Oct 5, 2026',
      'Service completed',
      'Oct 6, 2026',
      'Pending',
      'Amount due',
      '$287.30',
      'Due date: Oct 6, 2026',
      'Plumbing service',
      'Subtotal$272.50',
      'Service details',
      'WO-1048',
      'Kitchen sink leak repaired and tested.',
      'Pay invoice',
      'Pay securely online using your preferred payment method.',
      'Cash',
      'Arrange with Northstar Home Services',
      'Secure encrypted payment',
      'You will receive a receipt by email after payment.',
      'I have a question about this invoice',
    ]) {
      expect(text()).toContain(expected);
    }
    for (const absent of ['Back to invoices', 'Save this payment method', 'My requests']) {
      expect(text()).not.toContain(absent);
    }
    // The work order is text, not a link; the question link carries the organization email.
    expect(
      Array.from(document.body.querySelectorAll('a'), (anchor) => anchor.getAttribute('href')),
    ).toEqual(['mailto:hello@northstar.example?subject=Invoice%20INV-1048']);
    // The first available method (card) is selected and mounted with the balance in minor units.
    expect(input('input[value="card"]').checked).toBe(true);
    expect(fake.mounts[0]).toMatchObject({
      publishableKey: 'pk_test_fieldops',
      amountMinor: 28730,
      currency: 'USD',
    });

    // Downloads: invoice PDF, completion report in a new tab with a download fallback.
    await click('Download PDF');
    request('pdf').flush(new Blob(['%PDF']));
    await tick(0);
    expect(downloads).toEqual(['INV-1048.pdf']);
    const open = vi.spyOn(window, 'open').mockReturnValueOnce({} as Window);
    await click('View completion report');
    request('completion-report').flush(new Blob(['%PDF']));
    await tick(0);
    expect(open).toHaveBeenCalledTimes(1);
    expect(downloads).toEqual(['INV-1048.pdf']);
    open.mockReturnValueOnce(null);
    await click('View completion report');
    request('completion-report').flush(new Blob(['%PDF']));
    await tick(0);
    expect(downloads).toEqual(['INV-1048.pdf', 'WO-1048-completion-report.pdf']);
    await click('Download PDF');
    request('pdf').error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    await tick(0);
    expect(text()).toContain("We couldn't download the file. Please try again.");

    // Gallery: lazy image per photo, keyboard navigation and a per-image failure.
    await click('View before & after photos');
    await respond(request('photos'), [
      { photoId: 'ph-1', type: 'before', caption: 'Leak under sink', takenOn: '2026-10-05' },
      { photoId: 'ph-2', type: 'after', caption: null, takenOn: '2026-10-05' },
    ]);
    const first = request('photos/content');
    expect(first.request.body).toEqual({ token: TOKEN, photoId: 'ph-1' });
    first.flush(new Blob(['png'], { type: 'image/png' }));
    await tick(0);
    expect(document.body.querySelector('img.gallery__image')?.getAttribute('src')).toBe(
      'blob:public',
    );
    expect(text()).toContain('Before');
    expect(text()).toContain('Leak under sink');
    expect(text()).toContain('Photo 1 of 2');
    document.body
      .querySelector('.gallery__frame')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await tick(0);
    const second = request('photos/content');
    expect(second.request.body).toEqual({ token: TOKEN, photoId: 'ph-2' });
    second.error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    await tick(0);
    expect(text()).toContain("We couldn't load this photo.");
    expect(text()).toContain('Photo 2 of 2');
    teardown();

    // Unavailable methods are disabled with their reasons; bank transfer with copy and notice.
    const bank = publicInvoiceBody().paymentOptions!.bankTransfer;
    await load(
      publicInvoiceBody({
        transferReportedOn: null,
        paymentOptions: {
          card: { available: false, publishableKey: null },
          bankTransfer: bank,
          cash: { phone: null, email: null },
        },
      }),
    );
    expect(input('input[value="card"]').disabled).toBe(true);
    expect(text()).toContain("Card payments aren't available right now.");
    expect(input('input[value="bank"]').checked).toBe(true);
    expect(fake.mounts).toHaveLength(0);
    for (const expected of [
      'Bank transfer instructions',
      "Send a payment using your bank's online bill pay or ACH transfer with the following details.",
      'FieldOps Payments',
      '000123454821',
      '021000021',
      'INV-1048',
      'Use the invoice number as the payment reference.',
      'Bank transfers may take 1–3 business days to appear.',
      'Prefer cash? Contact Northstar Home Services to arrange payment.',
    ]) {
      expect(text()).toContain(expected);
    }
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    document.body
      .querySelector<HTMLButtonElement>('button[aria-label="Copy Account number"]')!
      .click();
    await stable();
    expect(writeText).toHaveBeenCalledWith('000123454821');
    expect(text()).toContain('Copied Account number');
    await click('I sent the transfer');
    const failed = request('payments/bank-transfer-notice');
    const noticeKey = (failed.request.body as { idempotencyKey: string }).idempotencyKey;
    expect(Object.keys(failed.request.body as object).sort()).toEqual(['idempotencyKey', 'token']);
    failed.error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    await tick(0);
    expect(text()).toContain("We couldn't send your notice. Try again.");
    await click('I sent the transfer');
    const retried = request('payments/bank-transfer-notice');
    expect((retried.request.body as { idempotencyKey: string }).idempotencyKey).toBe(noticeKey);
    await respond(retried, { changed: true, reportedOn: '2026-10-09' }, 201);
    expect(text()).toContain("Transfer reported on Oct 9, 2026. We'll confirm it once it arrives.");
    expect(button('I sent the transfer')).toBeUndefined();
    teardown();

    // Neither card nor bank: cash is selected with phone and email links; no question link without email.
    await load(
      publicInvoiceBody({
        organization: { ...publicInvoiceBody().organization, email: null },
        paymentOptions: {
          card: { available: false, publishableKey: null },
          bankTransfer: { available: false },
          cash: { phone: '(512) 555-0199', email: 'cash@northstar.example' },
        },
      }),
    );
    expect(input('input[value="cash"]').checked).toBe(true);
    expect(text()).toContain("Bank transfer isn't available for this invoice.");
    expect(text()).toContain('Contact Northstar Home Services to arrange a cash payment.');
    expect(
      Array.from(document.body.querySelectorAll('a'), (anchor) => anchor.getAttribute('href')),
    ).toEqual(['tel:5125550199', 'mailto:cash@northstar.example']);
    teardown();
  });

  it('shows neutral states without organization or invoice data and a retry only where it helps (FR-15, AC-20)', async () => {
    for (const [url, status, expected] of [
      ['/invoices/view', 0, 'This invoice is no longer available'],
      [`/invoices/view#token=${TOKEN}`, 404, 'This invoice is no longer available'],
      [`/invoices/view#token=${TOKEN}`, 429, PUBLIC_RATE_LIMITED_MESSAGE],
      [`/invoices/view#token=${TOKEN}`, 500, "We couldn't load this invoice. Try again."],
    ] as const) {
      await setup(url);
      if (status !== 0) {
        await respond(request('view'), { title: 'backend text', code: 'x' }, status);
      }
      expect(text()).toContain(expected);
      expect(text()).not.toContain('backend text');
      expect(text()).not.toContain('Northstar');
      expect(text()).toContain('FieldOps');
      expect(document.activeElement?.tagName).toBe('H1');
      expect(document.body.querySelectorAll('a, input, table')).toHaveLength(0);
      if (status === 0 || status === 404) {
        expect(text()).toContain('For your protection, invoice details are not shown.');
        expect(button('Retry')).toBeUndefined();
        expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
      } else {
        await click('Retry');
        await respond(request('view'), publicInvoiceBody());
        expect(text()).toContain('Invoice INV-1048');
      }
      teardown();
    }
  });

  it('validates before one card-intent, retries a network failure with the same key, blocks double submission and shows the overlay until polling confirms the payment (FR-15, AC-22)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    await load(publicInvoiceBody({ customerFirstName: 'Sofia' }));
    expect(button('Pay $287.30')!.disabled).toBe(false);

    // Client validation: name first, then the Payment Element; neither reaches the server.
    await click('Pay $287.30');
    expect(text()).toContain('Enter the cardholder name.');
    expect(fake.validations).toBe(0);
    await type('#cardholder-name', 'x'.repeat(121));
    await click('Pay $287.30');
    expect(text()).toContain('Use 120 characters or fewer.');
    await type('#cardholder-name', '  Sofia Martinez  ');
    fake.valid = false;
    await click('Pay $287.30');
    expect(fake.validations).toBe(1);
    expect(fake.confirms).toHaveLength(0);
    expect(text()).not.toContain('Processing your payment');
    fake.valid = true;

    // Two submissions in the same tick produce one request; the panel is busy and locked.
    const form = document.body.querySelector('form')!;
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await tick(0);
    const first = request('payments/card-intent');
    const key = (first.request.body as { idempotencyKey: string }).idempotencyKey;
    expect(first.request.body).toEqual({ token: TOKEN, idempotencyKey: key });
    expect(text()).toContain('Processing your payment');
    expect(text()).toContain('Please keep this page open. This may take a few moments.');
    expect(button('Processing $287.30')!.disabled).toBe(true);
    expect(input('#cardholder-name').disabled).toBe(true);
    expect(document.body.querySelector('.panel')?.getAttribute('aria-busy')).toBe('true');
    expect(document.body.querySelector('.overlay')?.parentElement?.getAttribute('aria-live')).toBe(
      'polite',
    );
    expect(
      Array.from(document.body.querySelectorAll<HTMLInputElement>('.method input')).every(
        (r) => r.disabled,
      ),
    ).toBe(true);

    // A network failure without a response keeps the key: the retry is the same submission.
    first.error(new ProgressEvent('error'), { status: 0, statusText: '' });
    await tick(0);
    expect(text()).toContain(CARD_START_FAILED_MESSAGE);
    expect(text()).not.toContain('Processing your payment');
    await click('Pay $287.30');
    const retry = request('payments/card-intent');
    expect((retry.request.body as { idempotencyKey: string }).idempotencyKey).toBe(key);
    await respond(retry, INTENT, 201);

    // The attempt id is stored before confirming; the confirm carries only the Stripe inputs.
    expect(sessionStorage.getItem(ATTEMPT_KEY)).toBe('att-1');
    expect(fake.confirms).toEqual([
      {
        clientSecret: 'cs_test_1',
        billingName: 'Sofia Martinez',
        returnUrl: `${window.location.origin}/invoices/view`,
      },
    ]);

    // Polling: immediate, then every 2 s; the server decides, then the page re-reads the invoice.
    await tick(0);
    const poll = request('payments/status');
    expect(poll.request.body).toEqual({ token: TOKEN, attemptId: 'att-1' });
    await respond(poll, attemptState('pending'));
    expect(text()).toContain('Processing your payment');
    await tick(1999);
    httpTesting.expectNone(`${BASE}/payments/status`);
    await tick(1);
    await respond(request('payments/status'), attemptState('succeeded'));
    await respond(request('view'), paidInvoiceBody());

    expect(sessionStorage.getItem(ATTEMPT_KEY)).toBeNull();
    expect(text()).toContain('Payment successful');
    expect(text()).toContain('Thank you, Sofia. Your payment has been confirmed.');
    expect(document.body.querySelector('.panel')).toBeNull();
    expect(fake.destroyed).toBe(1);
    expect(document.activeElement?.id).toBe('success-title');
  });

  it('shows each failure category, retries with a new key and lets the customer choose another method (FR-15, AC-22)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    await load(publicInvoiceBody());
    const keys: string[] = [await pay(INTENT)];
    for (const [category, message] of [
      ['card_declined', CARD_DECLINED_MESSAGE],
      ['expired', CARD_EXPIRED_MESSAGE],
      ['processing_error', CARD_OTHER_FAILURE_MESSAGE],
      ['incorrect_cvc', CARD_DECLINED_MESSAGE],
    ] as const) {
      await tick(0);
      await respond(request('payments/status'), attemptState('failed', category));
      expect(document.body.querySelector('[role="alert"]')?.textContent).toContain(
        "Payment wasn't completed",
      );
      expect(text()).toContain(message);
      expect(input('#cardholder-name').value).toBe('Sofia Martinez');
      expect(input('#cardholder-name').disabled).toBe(false);
      expect(sessionStorage.getItem(ATTEMPT_KEY)).toBeNull();
      if (category !== 'incorrect_cvc') {
        await click('Try payment again');
        const next = request('payments/card-intent');
        keys.push((next.request.body as { idempotencyKey: string }).idempotencyKey);
        await respond(next, INTENT, 201);
      }
    }
    expect(new Set(keys).size).toBe(keys.length);
    await click('Choose another payment method');
    expect(text()).not.toContain("Payment wasn't completed");
    expect(document.activeElement).toBe(input('input[name="payment-method"]:checked'));
  });

  it('stops polling after 60 s without a final status and keeps the still-confirming notice (FR-15, AC-22)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    await load(publicInvoiceBody());
    await pay(INTENT);
    for (let elapsed = 0; elapsed < 60_000; elapsed += 2000) {
      await tick(elapsed === 0 ? 0 : 2000);
      for (const pending of httpTesting.match(`${BASE}/payments/status`)) {
        pending.flush(attemptState('pending'));
      }
      await stable();
    }
    await tick(2000);
    for (const pending of httpTesting.match(`${BASE}/payments/status`)) {
      pending.flush(attemptState('pending'));
    }
    await stable();
    expect(text()).toContain(CARD_STILL_CONFIRMING_MESSAGE);
    expect(button('Try payment again')).toBeUndefined();
    expect(document.body.querySelector('.overlay')).toBeNull();
    await tick(6000);
    httpTesting.expectNone(`${BASE}/payments/status`);
  });

  it('handles start errors, conflicts, a paid-in-the-meantime invoice and a replay without client secret (FR-15, AC-22)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    for (const [status, code, message, newKey] of [
      [502, 'payment_provider_unavailable', CARD_START_FAILED_MESSAGE, true],
      [429, undefined, PUBLIC_RATE_LIMITED_MESSAGE, false],
      [409, 'payment_in_progress', CARD_STILL_CONFIRMING_MESSAGE, false],
      [409, 'idempotency_conflict', CARD_START_FAILED_MESSAGE, true],
    ] as const) {
      await load(publicInvoiceBody());
      const key = await pay({ code }, status);
      expect(text()).toContain(message);
      expect(document.body.querySelector('.overlay')).toBeNull();
      if (status === 409 && code === 'payment_in_progress') {
        expect(button('Pay $287.30')!.disabled).toBe(true);
      } else {
        await click('Pay $287.30');
        const again = request('payments/card-intent');
        expect((again.request.body as { idempotencyKey: string }).idempotencyKey !== key).toBe(
          newKey,
        );
        await respond(again, INTENT, 201);
        await tick(0);
        request('payments/status').flush(attemptState('pending'));
      }
      teardown();
    }
    // A paid-in-the-meantime invoice reloads into the success view.
    await load(publicInvoiceBody());
    await pay({ code: 'invoice_not_payable' }, 409);
    await respond(request('view'), paidInvoiceBody());
    expect(text()).toContain('Payment successful');
    teardown();

    // Replay without a client secret follows its status; no confirm is attempted.
    await load(publicInvoiceBody());
    await pay({ ...INTENT, clientSecret: null, status: 'failed' }, 200);
    expect(fake.confirms).toHaveLength(0);
    await tick(0);
    await respond(request('payments/status'), attemptState('failed', 'card_declined'));
    expect(text()).toContain(CARD_DECLINED_MESSAGE);
  });

  it('resumes a pending attempt on load and after the Stripe redirect, and clears a stored attempt of a paid invoice (FR-15, AC-22)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });

    // A pending attempt reported by the view is polled at once, with no new card-intent.
    await load(publicInvoiceBody({ activeAttempt: { attemptId: 'att-9', status: 'pending' } }));
    await tick(0);
    const active = request('payments/status');
    expect(active.request.body).toEqual({ token: TOKEN, attemptId: 'att-9' });
    expect(text()).toContain('Processing your payment');
    await respond(active, attemptState('succeeded', null, 'att-9'));
    await respond(request('view'), paidInvoiceBody());
    expect(text()).toContain('Payment successful');
    teardown();

    // Return from authentication: parameters removed first, never sent; the stored attempt resumes.
    sessionStorage.setItem(ATTEMPT_KEY, 'att-7');
    await setup(`/invoices/view?${STRIPE_PARAMS}#token=${TOKEN}`);
    const view = request('view');
    expect(urlAtRequest).toEqual(['/invoices/view']);
    await respond(view, publicInvoiceBody());
    await tick(0);
    const stored = request('payments/status');
    expect(stored.request.body).toEqual({ token: TOKEN, attemptId: 'att-7' });
    expect(JSON.stringify([view.request.body, stored.request.body])).not.toMatch(/pi_|cs_secret/);
    await respond(stored, attemptState('failed', 'authentication_failed', 'att-7'));
    expect(text()).toContain(CARD_DECLINED_MESSAGE);
    teardown();

    // A paid invoice with a stored attempt and none active: cleared, success view, no status call.
    sessionStorage.setItem(ATTEMPT_KEY, 'att-7');
    await load(paidInvoiceBody());
    expect(sessionStorage.getItem(ATTEMPT_KEY)).toBeNull();
    expect(text()).toContain('Payment successful');
  });

  it('renders the paid view, downloads receipt and invoice, handles the review states and returns refunded invoices to the payable layout (FR-15, AC-23)', async () => {
    // Success card, rows, summary, downloads and the review with a failure kept for retry.
    await load(paidInvoiceBody());
    for (const expected of [
      'Paid',
      'Payment successful',
      'Thank you, Sofia. Your payment has been confirmed.',
      '$287.30',
      'Paid on Oct 9, 2026',
      'Receipt number',
      'RCT-1048-01',
      'Transaction reference',
      'PAY-12',
      'Visa ending in 4242',
      'VISA',
      'A receipt was sent to sofia@example.com',
      'Payment summary',
      'Subtotal$272.50',
      'Discount−$5.00',
      'Total paid$287.30',
      'Balance$0.00',
      'How was your service?',
      'Carlos Rivera',
      'Oct 5, 2026',
      'Your feedback helps us improve.',
      '0/500',
      'What happens next?',
      'This invoice is now closed. Your receipt is available anytime.',
    ]) {
      expect(text()).toContain(expected);
    }
    expect(text()).not.toContain('Pending');
    expect(text()).not.toContain('Back to invoices');
    expect(document.body.querySelector('.panel')).toBeNull();
    expect(document.body.querySelectorAll('input[name="rating"]')).toHaveLength(5);

    await click('Download receipt');
    const receipt = request('receipt');
    expect(receipt.request.body).toEqual({ token: TOKEN, paymentId: 'pay-opaque-1' });
    receipt.flush(new Blob(['%PDF']));
    await tick(0);
    await click('Download invoice');
    request('pdf').flush(new Blob(['%PDF']));
    await tick(0);
    expect(downloads).toEqual(['RCT-1048-01.pdf', 'INV-1048.pdf']);
    await click('Download receipt');
    request('receipt').error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    await tick(0);
    expect(text()).toContain("We couldn't download the file. Please try again.");

    await click('Submit review');
    expect(text()).toContain('Select a rating.');
    httpTesting.expectNone(`${BASE}/review`);
    input('input[name="rating"][value="4"]').click();
    const comment = document.body.querySelector<HTMLTextAreaElement>('#review-comment')!;
    comment.value = '  Great work  ';
    comment.dispatchEvent(new Event('input'));
    await stable();
    expect(text()).toContain('14/500');
    await click('Submit review');
    const failed = request('review');
    expect(failed.request.body).toEqual({ token: TOKEN, rating: 4, comment: 'Great work' });
    await respond(failed, { code: 'x' }, 500);
    expect(text()).toContain("We couldn't send your review. Try again.");
    expect(comment.value).toBe('  Great work  ');
    expect(input('input[name="rating"][value="4"]').checked).toBe(true);
    await click('Submit review');
    await respond(request('review'), { submitted: true }, 201);
    expect(text()).toContain('Thanks for your feedback!');
    expect(button('Submit review')).toBeUndefined();
    teardown();

    // No customer name, receipt number or recipient: those parts are hidden; `review_exists` thanks.
    await load(
      paidInvoiceBody({
        customerFirstName: null,
        receiptEmail: null,
        payments: [cardPayment({ receiptNumber: null })],
      }),
    );
    expect(text()).toContain('Thank you. Your payment has been confirmed.');
    expect(text()).not.toContain('Receipt number');
    expect(text()).not.toContain('Email confirmation');
    expect(button('Download receipt')).toBeUndefined();
    input('input[name="rating"][value="5"]').click();
    await click('Submit review');
    await respond(request('review'), { code: 'review_exists' }, 409);
    expect(text()).toContain('Thanks for your feedback!');
    teardown();

    // "Maybe later" hides the card; an unavailable review is never offered.
    await load(paidInvoiceBody());
    await click('Maybe later');
    expect(text()).not.toContain('How was your service?');
    teardown();
    await load(paidInvoiceBody({ review: { available: false, submitted: true } }));
    expect(text()).not.toContain('How was your service?');
    teardown();

    // A refunded invoice is `sent` again: payable layout with the refund listed under Payment summary.
    await load(
      publicInvoiceBody({
        amountPaid: 0,
        payments: [cardPayment({ status: 'refunded', refundedAmount: 287.3 })],
      }),
    );
    expect(text()).toContain('Pay invoice');
    expect(text()).toContain('Payment summary');
    expect(text()).toContain('PAY-12 · Visa ending in 4242 · Oct 9, 2026');
    expect(text()).toContain('Refunded');
    expect(text()).toContain('−$287.30');
    expect(text()).not.toContain('Payment successful');
  });
});
