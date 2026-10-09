import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Confirmation, ConfirmationService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { InvoiceRow } from '../../models/invoices-hub.model';
import {
  invoiceRow,
  optionsBody,
  paymentRow,
  resultBody,
} from '../../testing/invoices-hub-fixtures';
import { todayIn } from '../../utils/invoices-hub-format';
import { DrawerNotice, RecordPaymentDrawer } from './record-payment-drawer';

const API = 'http://api.test';
const TODAY = todayIn('America/Chicago');

describe('Record payment drawer', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<RecordPaymentDrawer>;
  let drawer: RecordPaymentDrawer;
  let notices: DrawerNotice[];
  let settled: number;
  let closed: number;
  let confirmations: Confirmation[];

  async function setup(row: InvoiceRow = invoiceRow({ balanceDue: 149 })): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        ConfirmationService,
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne(`${API}/sessions/current`).flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: 'owner', name: 'owner' },
    });
    confirmations = [];
    vi.spyOn(TestBed.inject(ConfirmationService), 'confirm').mockImplementation((c) => {
      confirmations.push(c);
      return undefined as never;
    });
    fixture = TestBed.createComponent(RecordPaymentDrawer);
    drawer = fixture.componentInstance;
    notices = [];
    settled = 0;
    closed = 0;
    drawer.notice.subscribe((n) => notices.push(n));
    drawer.settled.subscribe(() => settled++);
    drawer.closed.subscribe(() => closed++);
    fixture.componentRef.setInput('row', row);
    fixture.componentRef.setInput('options', optionsBody());
    await openDrawer();
  }

  async function openDrawer(): Promise<void> {
    fixture.componentRef.setInput('open', true);
    await stable();
  }

  async function stable(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const post = (): TestRequest =>
    httpTesting.expectOne((r) => r.method === 'POST' && r.url === `${API}/invoices/inv-1/payments`);
  const fill = (values: {
    amount?: string;
    method?: 'cash' | 'check';
    reference?: string;
    note?: string;
  }): void => {
    if (values.amount !== undefined) drawer.amount.set(values.amount);
    if (values.method !== undefined) drawer.method.set(values.method);
    if (values.reference !== undefined) drawer.reference.set(values.reference);
    if (values.note !== undefined) drawer.note.set(values.note);
  };
  const body = (): HTMLElement => document.body;

  afterEach(() => httpTesting.verify());

  it('applies the defaults, the read-only data, the reference marker per method and client validation with focus on the first invalid field (FR-12, AC-18)', async () => {
    await setup();
    expect(body().textContent).toContain('Record external payment');
    expect(body().textContent).toContain(
      'Use this form only for money already received outside FieldOps. No card will be charged.',
    );
    expect(
      Array.from(body().querySelectorAll('.record__summary > *'), (n) => n.textContent),
    ).toEqual(['Invoice', 'INV-1049', 'Customer', 'Daniel Kim', 'Outstanding balance', '$149.00']);
    expect(drawer.amount()).toBe('149.00');
    expect(drawer.paidDate()).toBe(TODAY);
    expect(body().querySelector('#record-payment-paidDate')?.getAttribute('min')).toBe(
      '2026-09-19',
    );
    expect(body().querySelector('#record-payment-paidDate')?.getAttribute('max')).toBe(TODAY);
    expect(drawer.method()).toBeNull();
    expect(drawer.receivedByUserId()).toBe('u-1');
    expect(drawer.sendReceipt()).toBe(true);
    expect(body().textContent).toContain('0/500');
    expect(body().textContent).toContain(
      'This creates a payment record and updates the invoice balance.',
    );

    // The required marker follows the method.
    const marker = (): boolean =>
      body().querySelector('label[for="record-payment-reference"] .form-field__required') !== null;
    expect(marker()).toBe(false);
    drawer.method.set('bank_transfer');
    await stable();
    expect(marker()).toBe(true);
    drawer.method.set('cash');
    await stable();
    expect(marker()).toBe(false);

    // Invalid values: messages, no request, focus on the first invalid field.
    const cases: [string, string][] = [
      ['0', 'Enter an amount greater than 0.'],
      ['1.234', 'Enter an amount greater than 0.'],
      ['150', "Amount can't exceed the outstanding balance of $149.00."],
    ];
    for (const [amount, message] of cases) {
      fill({ amount });
      drawer.save();
      await stable();
      expect(drawer.errors().amount).toBe(message);
      expect(document.activeElement?.id).toBe('record-payment-amount');
    }
    fill({ amount: '10', method: 'check', reference: ' ' });
    drawer.paidDate.set('2026-09-18');
    drawer.save();
    await stable();
    expect(drawer.errors()).toEqual({
      paidDate: "Payment date can't be before the invoice date.",
      reference: 'Enter the reference number.',
    });
    expect(document.activeElement?.id).toBe('record-payment-paidDate');
    drawer.paidDate.set('9999-01-01');
    fill({ note: 'n'.repeat(501), reference: 'r'.repeat(161) });
    drawer.save();
    expect(drawer.errors()).toEqual({
      paidDate: "Payment date can't be in the future.",
      reference: 'Use 160 characters or fewer.',
      note: 'Use 500 characters or fewer.',
    });
    drawer.method.set(null);
    drawer.save();
    expect(drawer.errors().method).toBe('Select a payment method.');
    httpTesting.expectNone(() => true);

    // No recipient: the receipt switch is off, disabled and explained.
    fixture.componentRef.setInput('open', false);
    await stable();
    fixture.componentRef.setInput('row', invoiceRow({ hasRecipient: false }));
    await openDrawer();
    expect(drawer.sendReceipt()).toBe(false);
    expect(body().querySelector('p-toggleswitch')?.className).toContain('p-disabled');
    expect(body().textContent).toContain('This invoice has no recipient email.');
  });

  it('submits the contract body with one key per open (reused on retry), maps every outcome and guards discard (FR-12, AC-19)', async () => {
    await setup();
    fill({ method: 'check', reference: ' CHK-77 ', note: ' ' });
    drawer.amount.set('100');
    drawer.save();
    expect(drawer.submitting()).toBe(true);
    let request = post();
    const first = request.request.body as Record<string, unknown>;
    expect(first).toEqual({
      idempotencyKey: expect.stringMatching(/^[0-9a-f-]{36}$/),
      amount: 100,
      paidDate: TODAY,
      method: 'check',
      reference: 'CHK-77',
      receivedByUserId: 'u-1',
      sendReceipt: true,
      note: null,
      updatedAt: '2026-09-19T15:00:00Z',
    });
    drawer.save(); // pending: no second request
    httpTesting.expectNone(() => true);

    // A network failure keeps the values and the key; the retry reuses it.
    request.error(new ProgressEvent('error'));
    expect(drawer.submitting()).toBe(false);
    expect(drawer.amount()).toBe('100');
    expect(notices.pop()?.summary).toBe("We couldn't record this payment. Try again.");
    drawer.save();
    request = post();
    expect((request.request.body as Record<string, unknown>)['idempotencyKey']).toBe(
      first['idempotencyKey'],
    );

    // Outcomes: [status, body, notice, settled].
    const outcomes: [number, object | null, Partial<DrawerNotice>, boolean][] = [
      [
        201,
        resultBody(),
        { severity: 'success', summary: 'Payment PAY-7 recorded. Receipt sent to the customer.' },
        true,
      ],
      [
        201,
        resultBody({ emailStatus: 'not_sent' }),
        { severity: 'success', summary: 'Payment PAY-7 recorded.' },
        true,
      ],
      [
        201,
        resultBody({ emailStatus: 'failed' }),
        { severity: 'warn', summary: "Payment recorded, but we couldn't email the receipt." },
        true,
      ],
      [
        200,
        resultBody({ changed: false, emailStatus: 'not_sent' }),
        { severity: 'info', summary: 'This payment was already recorded.' },
        true,
      ],
      [
        409,
        { code: 'invoice_changed' },
        { summary: 'This invoice changed. Refresh to see the latest.' },
        true,
      ],
      [
        409,
        { code: 'invoice_not_payable' },
        { summary: "This invoice can't receive payments." },
        true,
      ],
      [
        409,
        { code: 'idempotency_conflict' },
        { summary: "We couldn't record this payment. Try again." },
        false,
      ],
      [404, { code: 'invoice_unavailable' }, { summary: "This invoice isn't available." }, true],
      [
        403,
        { code: 'forbidden' },
        { summary: 'Only owners and accounting can record payments.' },
        false,
      ],
      [500, null, { summary: "We couldn't record this payment. Try again." }, false],
    ];
    let attempt = request;
    for (const [status, payload, notice, done] of outcomes) {
      settled = 0;
      notices.length = 0;
      attempt.flush(payload, { status, statusText: String(status) });
      expect(notices[0]).toMatchObject(notice);
      expect(settled).toBe(done ? 1 : 0);
      expect(drawer.submitting()).toBe(false);
      drawer.save();
      attempt = post();
    }

    // 400 shows the field messages and focuses the first invalid field.
    attempt.flush(
      { errors: { amount: ["Amount can't exceed the outstanding balance of $149.00."] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await stable();
    expect(drawer.errors().amount).toBe("Amount can't exceed the outstanding balance of $149.00.");
    expect(document.activeElement?.id).toBe('record-payment-amount');
    expect(settled).toBe(0);

    // Reopening generates a new key and restores the defaults.
    fixture.componentRef.setInput('open', false);
    await stable();
    await openDrawer();
    expect(drawer.amount()).toBe('149.00');
    fill({ method: 'cash' });
    drawer.save();
    request = post();
    expect((request.request.body as Record<string, unknown>)['idempotencyKey']).not.toBe(
      first['idempotencyKey'],
    );
    request.flush(resultBody({ payment: paymentRow() }), { status: 201, statusText: 'Created' });

    // Discard: a clean close is immediate; changed values ask first.
    fixture.componentRef.setInput('open', false);
    await stable();
    await openDrawer();
    drawer.requestClose();
    expect(closed).toBe(1);
    fixture.componentRef.setInput('open', false);
    await stable();
    await openDrawer();
    fill({ note: 'x' });
    drawer.requestClose();
    expect(closed).toBe(1);
    expect(confirmations[0]).toMatchObject({
      key: 'discard-changes',
      header: 'Discard unsaved changes?',
    });
    confirmations[0].accept!();
    expect(closed).toBe(2);
  });
  it('overlays the page at wide viewports instead of docking beside it (BR-30)', async () => {
    const original = window.matchMedia;
    window.matchMedia = ((query: string) => ({
      matches: true,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
    try {
      await setup();
      expect(body().querySelector('aside.drawer-shell--docked')).toBeNull();
      expect(body().querySelector('[role="dialog"][aria-modal="true"]')).not.toBeNull();
    } finally {
      window.matchMedia = original;
    }
  });
});
