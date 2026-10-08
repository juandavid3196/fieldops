import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Confirmation, ConfirmationService, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { InvoiceDetail } from '../../models/invoice.model';
import { invoiceBody, sentBody } from '../../testing/invoice-fixtures';
import { InvoiceDetailPage } from './invoice-detail';

const API = 'http://api.test';

@Component({ template: '' })
class Stub {}

describe('InvoiceDetailPage', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: InvoiceDetailPage;
  let host: HTMLElement;
  let toasts: { severity?: string; summary?: string }[];
  let confirmations: Confirmation[];

  async function setup(role = 'owner'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'invoices/:invoiceId', component: InvoiceDetailPage },
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
    harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl('/invoices/inv-1', InvoiceDetailPage);
    host = harness.routeNativeElement as HTMLElement;
    const injector = harness.routeDebugElement!.injector;
    toasts = [];
    confirmations = [];
    vi.spyOn(injector.get(MessageService), 'add').mockImplementation(
      (message) => void toasts.push(message),
    );
    vi.spyOn(injector.get(ConfirmationService), 'confirm').mockImplementation((confirmation) => {
      confirmations.push(confirmation);
      return undefined as never;
    });
  }

  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const field = (suffix: string): HTMLInputElement | HTMLTextAreaElement =>
    host.querySelector<HTMLInputElement | HTMLTextAreaElement>(`[id$="-${suffix}"]`)!;
  const type = async (suffix: string, value: string): Promise<void> => {
    const element = field(suffix);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await settle();
  };
  const get = (): TestRequest => httpTesting.expectOne(`${API}/invoices/inv-1`);
  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/invoices/inv-1/${path}`);
  const load = async (invoice: InvoiceDetail): Promise<void> => {
    get().flush(invoice);
    await settle();
  };
  const problem = (
    status: number,
    body: object = {},
  ): [object, { status: number; statusText: string }] => [body, { status, statusText: 'Error' }];
  const flushError = async (request: TestRequest, status: number, body?: object): Promise<void> => {
    request.flush(...problem(status, body));
    await settle();
  };

  afterEach(() => httpTesting.verify());

  it.each<[string, () => Promise<void>]>([
    [
      'technician: forbidden state without any invoice request',
      async () => {
        await setup('technician');
        expect(text()).toContain("You don't have access to invoices.");
      },
    ],
    [
      'unknown or void invoice: 404 text with a link to Needs review',
      async () => {
        await setup();
        expect(host.querySelector('[aria-busy="true"]')).not.toBeNull();
        await flushError(get(), 404, { code: 'invoice_unavailable', title: 'backend text' });
        expect(text()).toContain("This invoice isn't available.");
        expect(text()).not.toContain('backend text');
        const link = Array.from(host.querySelectorAll('a')).find((a) =>
          a.textContent?.includes('Needs review'),
        );
        expect(link?.getAttribute('href')).toBe('/invoices/review');
        expect(document.activeElement?.tagName).toBe('H1');
      },
    ],
    [
      'load error: message with Retry that reloads',
      async () => {
        await setup();
        await flushError(get(), 500);
        expect(text()).toContain("We couldn't load this invoice. Try again.");
        button('Retry')!.click();
        await settle();
        await load(invoiceBody());
        expect(text()).toContain('Invoice INV-1048');
      },
    ],
    [
      'read-only role: controls read-only, Save and Send hidden, Download PDF stays',
      async () => {
        await setup('viewer');
        await load(invoiceBody({ canAct: false }));
        expect(text()).toContain('Only owners and accounting can edit and send invoices.');
        expect(button('Save draft')).toBeUndefined();
        expect(button('Send invoice')).toBeUndefined();
        expect(button('Download PDF')).toBeDefined();
        expect(button('Resend email')).toBeUndefined();
        expect((field('recipient') as HTMLInputElement).readOnly).toBe(true);
        expect((field('message') as HTMLTextAreaElement).readOnly).toBe(true);
      },
    ],
  ])('%s (FR-09, AC-17, AC-18)', async (_name, run) => {
    await run();
  });

  it('renders the draft per Design 12, recalculates the due date, validates and focuses, saves and sends with the confirmation (FR-09, AC-18, AC-19)', async () => {
    await setup();
    await load(invoiceBody());

    // Header, metadata, preview, panel and checks (BR-03, BR-04, BR-24).
    expect(text()).toContain('Invoice INV-1048 Draft');
    expect(Array.from(host.querySelectorAll('nav li'), (li) => li.textContent?.trim())).toEqual([
      'Billing',
      'Invoices',
      'INV-1048',
    ]);
    expect(host.querySelector('nav a[href="/invoices/review"]')).not.toBeNull();
    expect(host.querySelector('.page__meta a[href="/jobs/wo-1"]')?.textContent).toBe('WO-1048');
    expect(text()).toContain('Issue date:Oct 6, 2026');
    expect(text()).toContain('Invoice created from completed job WO-1048 · Not yet sent');
    expect(Array.from(host.querySelectorAll('th[scope="col"]'), (th) => th.textContent)).toEqual([
      'Description',
      'Qty',
      'Rate',
      'Tax',
      'Amount',
    ]);
    for (const expected of [
      'Diagnose and repair kitchen sink leak',
      '2 hr',
      '8.25%',
      '—',
      'Discount−$5.00',
      'Tax (8.25%)$19.80',
      'Total due$287.30',
      'Service completion note',
      'Thank you for your business!',
      'Send this invoice to your customer by email.',
      "SMS isn't available yet.",
      'Online payments and attachments will be available later.',
      'Recipient email added',
      '32/500',
    ]) {
      expect(text()).toContain(expected);
    }
    // SMS, 3 payment methods and 2 attachments: 6 disabled, unchecked controls, none enabled.
    const boxes = host.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    expect(boxes).toHaveLength(6);
    expect(Array.from(boxes).every((box) => box.disabled && !box.checked)).toBe(true);
    expect(button('Save draft')!.disabled).toBe(true);

    // Terms recompute the read-only due date (issue date + 15) and enable Save.
    page.changeValues({ ...page.values(), paymentTerms: 'net_15' });
    await settle();
    expect((field('due') as HTMLInputElement).value).toBe('Oct 21, 2026');
    expect(button('Save draft')!.disabled).toBe(false);

    // Send validates first: no dialog, linked messages, focus on the first invalid field.
    await type('recipient', '  ');
    await type('message', '');
    button('Send invoice')!.click();
    await settle();
    expect(confirmations).toHaveLength(0);
    expect(text()).toContain("Enter the customer's email address.");
    expect(text()).toContain('Enter a message.');
    expect(document.activeElement).toBe(field('recipient'));
    expect(field('recipient').getAttribute('aria-invalid')).toBe('true');
    expect(
      host.querySelector(`#${field('recipient').getAttribute('aria-describedby')}`),
    ).not.toBeNull();
    await type('recipient', 'bad');
    expect(text()).toContain('Enter a valid email address.');

    // Save sends trimmed values and the updatedAt token; Save is disabled again once stored.
    await type('recipient', ' sofia@example.com ');
    await type('message', ' Hello there ');
    button('Save draft')!.click();
    await settle();
    expect(button('Save draft')!.getAttribute('aria-busy')).toBe('true');
    expect(button('Send invoice')!.disabled).toBe(true);
    expect(button('Download PDF')!.disabled).toBe(true);
    const save = call('PUT', 'draft');
    expect(save.request.body).toEqual({
      paymentTerms: 'net_15',
      recipientEmail: 'sofia@example.com',
      message: 'Hello there',
      updatedAt: '2026-10-06T19:14:00Z',
    });
    save.flush(
      invoiceBody({
        paymentTerms: 'net_15',
        dueDate: '2026-10-21',
        updatedAt: '2026-10-07T10:00:00Z',
        delivery: { recipientEmail: 'sofia@example.com', message: 'Hello there', saved: true },
      }),
    );
    await settle();
    expect(toasts.at(-1)).toMatchObject({ severity: 'success', summary: 'Draft saved.' });
    expect(button('Save draft')!.disabled).toBe(true);
    expect(page.dirty()).toBe(false);

    // Send asks first, then posts the same values and shows the email failure as a warning.
    button('Send invoice')!.click();
    await settle();
    expect(confirmations.at(-1)?.header).toBe('Send invoice?');
    expect(confirmations.at(-1)?.message).toBe(
      "Sofia Martinez will receive INV-1048 for $287.30 at sofia@example.com. A sent invoice can't be edited.",
    );
    confirmations.at(-1)?.accept?.();
    const send = call('POST', 'send');
    expect(send.request.body).toEqual({
      paymentTerms: 'net_15',
      recipientEmail: 'sofia@example.com',
      message: 'Hello there',
      updatedAt: '2026-10-07T10:00:00Z',
    });
    send.flush({ changed: true, emailStatus: 'failed', invoice: sentBody() });
    await settle();
    expect(toasts.at(-1)).toMatchObject({
      severity: 'warn',
      summary: "Invoice sent, but we couldn't email it. Use Resend email to try again.",
    });
    expect(text()).toContain('Delivery');
    expect(text()).toContain('Sent Oct 8, 2026 at 3:30 PM');
    expect(button('Save draft')).toBeUndefined();
    expect(button('Send invoice')).toBeUndefined();
    expect(button('Resend email')).toBeDefined();
  });

  it('keeps entered values on failures, maps 400 to fields, reloads on 409 and already-sent, and asks before discarding unsaved changes (FR-09, AC-19)', async () => {
    await setup();
    await load(invoiceBody());
    await type('message', 'Edited message');

    const send = async (): Promise<TestRequest> => {
      button('Send invoice')!.click();
      await settle();
      confirmations.at(-1)?.accept?.();
      return call('POST', 'send');
    };

    await flushError(await send(), 500);
    expect(toasts.at(-1)).toMatchObject({
      severity: 'error',
      summary: "We couldn't complete this action. Try again.",
    });
    expect((field('message') as HTMLTextAreaElement).value).toBe('Edited message');
    expect(button('Send invoice')!.disabled).toBe(false);

    await flushError(await send(), 400, { errors: { recipientEmail: ['backend text'] } });
    expect(text()).toContain('Enter a valid email address.');
    expect(text()).not.toContain('backend text');
    expect(document.activeElement).toBe(field('recipient'));

    // Leaving with unsaved changes opens the shared discard dialog; Keep editing stays.
    const leave = page.canLeave();
    expect(typeof leave).toBe('object');
    const answers: boolean[] = [];
    (leave as import('rxjs').Observable<boolean>).subscribe((value) => answers.push(value));
    expect(confirmations.at(-1)).toMatchObject({
      key: 'discard-changes',
      header: 'Discard unsaved changes?',
    });
    confirmations.at(-1)?.reject?.();
    expect(answers).toEqual([false]);

    // 409 shows its fixed copy and reloads the latest invoice (edits are dropped).
    await flushError(await send(), 409, { code: 'invoice_changed', title: 'backend text' });
    expect(toasts.at(-1)?.summary).toBe('This invoice changed. Refresh to see the latest.');
    await load(invoiceBody({ updatedAt: '2026-10-07T11:00:00Z' }));
    expect(page.canLeave()).toBe(true);

    // Already sent elsewhere: 200 changed = false announces and reloads the sent state.
    button('Send invoice')!.click();
    await settle();
    confirmations.at(-1)?.accept?.();
    call('POST', 'send').flush({ changed: false, emailStatus: 'not_sent', invoice: sentBody() });
    await settle();
    expect(toasts.at(-1)).toMatchObject({
      severity: 'info',
      summary: 'Invoice INV-1048 was already sent.',
    });
    await load(sentBody());
    expect(text()).toContain('Delivery');
    expect(page.announcement()).toBe('Invoice INV-1048 was already sent.');
  });

  it('shows the sent invoice read-only and covers Resend email outcomes and Download PDF progress and failure (FR-09, AC-18, AC-20)', async () => {
    await setup();
    await load(sentBody());
    expect(text()).toContain('Sent Oct 8, 2026 at 3:30 PM to sofia@example.com');
    expect((field('recipient') as HTMLInputElement).readOnly).toBe(true);
    expect(button('Save draft')).toBeUndefined();

    button('Resend email')!.click();
    await settle();
    expect(button('Resend email')!.getAttribute('aria-busy')).toBe('true');
    expect(button('Download PDF')!.disabled).toBe(true);
    let resend = call('POST', 'resend-email');
    expect(resend.request.body).toEqual({ updatedAt: '2026-10-08T20:30:00Z' });
    resend.flush({ emailStatus: 'sent', invoice: sentBody({ updatedAt: '2026-10-08T21:00:00Z' }) });
    await settle();
    expect(toasts.at(-1)).toMatchObject({
      severity: 'success',
      summary: 'Invoice email sent again.',
    });

    button('Resend email')!.click();
    await settle();
    resend = call('POST', 'resend-email');
    expect(resend.request.body).toEqual({ updatedAt: '2026-10-08T21:00:00Z' });
    resend.flush({
      emailStatus: 'failed',
      invoice: sentBody({ updatedAt: '2026-10-08T21:05:00Z' }),
    });
    await settle();
    expect(toasts.at(-1)?.severity).toBe('warn');

    button('Resend email')!.click();
    await settle();
    await flushError(call('POST', 'resend-email'), 409, { code: 'invoice_not_sent' });
    expect(toasts.at(-1)?.summary).toBe('Only sent invoices can be emailed again.');
    await load(sentBody());

    // PDF: the server's file name is used; a failure keeps the page and explains.
    const downloads: string[] = [];
    URL.createObjectURL = vi.fn(() => 'blob:invoice');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });
    button('Download PDF')!.click();
    await settle();
    expect(button('Download PDF')!.getAttribute('aria-busy')).toBe('true');
    call('GET', 'pdf').flush(new Blob(['%PDF']), {
      headers: { 'Content-Disposition': 'attachment; filename="INV-1048.pdf"' },
    });
    await settle();
    expect(downloads).toEqual(['INV-1048.pdf']);

    button('Download PDF')!.click();
    await settle();
    call('GET', 'pdf').error(new ProgressEvent('error'), { status: 500, statusText: 'Error' });
    await settle();
    expect(toasts.at(-1)).toMatchObject({
      severity: 'error',
      summary: "We couldn't download the PDF. Try again.",
    });
  });
});
