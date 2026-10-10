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
import { MenuItem, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  DRAFT_ROW,
  OVERDUE_ROW,
  PAID_ROW,
  invoiceRow,
  optionsBody,
  overviewBody,
  pageBody,
  paymentRow,
  readyBody,
} from '../../testing/invoices-hub-fixtures';
import { InvoicesHub } from './invoices-hub';

const API = 'http://api.test';

@Component({ template: '<p>stub</p>' })
class Stub {}

describe('Invoices hub page', () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;
  let page: InvoicesHub;
  let host: HTMLElement;
  let toasts: { severity?: string; summary?: string }[];

  const get = (path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === 'GET' && r.url === `${API}${path}`);
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const tab = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find((b) =>
      b.textContent?.trim().startsWith(label),
    );
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const menuLabels = (): string[] => page.rowMenu().map((item) => item.label ?? '');

  async function open(url: string, role = 'owner'): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'invoices', component: InvoicesHub },
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
    page = await harness.navigateByUrl(url, InvoicesHub);
    host = harness.routeNativeElement as HTMLElement;
    toasts = [];
    vi.spyOn(harness.routeDebugElement!.injector.get(MessageService), 'add').mockImplementation(
      (message) => void toasts.push(message),
    );
  }

  afterEach(() => httpTesting.verify());

  it('syncs tab and status with the URL (invalid falls back), renders the hub states, lets Branch drive overview, list and ready badge, resets to page 1 and cancels stale loads (FR-11, FR-13, AC-14, AC-15, AC-17)', async () => {
    await open('/invoices?tab=bogus&status=nope');
    const rows = [invoiceRow(), OVERDUE_ROW, PAID_ROW, DRAFT_ROW];
    get('/invoices/options').flush(optionsBody());
    get('/invoices/customers').flush([{ id: 'c-1', name: 'Daniel Kim' }]);
    get('/invoices/overview').flush(overviewBody());
    let list = get('/invoices');
    expect([list.request.params.get('status'), list.request.params.get('page')]).toEqual([
      null,
      '1',
    ]);
    list.flush(pageBody(rows));
    const ready = get('/billing-review/queue');
    expect(
      ['completed', 'tab', 'page', 'branchId'].map((key) => ready.request.params.get(key)),
    ).toEqual(['all', 'ready', '1', null]);
    ready.flush(readyBody(6));
    await settle();

    // Header, metrics, aging, recent payments and the Invoices tab.
    expect(text()).toContain('Invoices & payments');
    expect(text()).toContain(
      'Track invoices, outstanding balances, due dates, and customer payments.',
    );
    expect(host.querySelector('a.hub__back')?.getAttribute('href')).toBe('/invoices');
    expect(
      Array.from(host.querySelectorAll('.overview__metric'), (n) =>
        [n.querySelector('dt')?.textContent, n.querySelector('dd')?.textContent].join(' '),
      ),
    ).toEqual([
      'Outstanding $18,760.00',
      'Overdue $4,320.00',
      'Draft $2,845.00',
      'Paid this month $118,140.00',
      'Average time to pay 8.4 days',
    ]);
    expect(text()).toContain('Aging summary');
    expect(text()).toContain('1 – 30 days$5,610.00');
    expect(text()).toContain('Recent payments');
    expect(host.querySelector('.overview__table a')?.getAttribute('href')).toBe('/invoices/inv-3');
    expect(tab('Invoices')?.getAttribute('aria-selected')).toBe('true');
    expect(tab('Completed jobs ready to invoice')?.textContent).toContain('6');
    expect(host.querySelectorAll('.table__table thead th[scope="col"]')).toHaveLength(10);
    const body = Array.from(host.querySelectorAll('.table__table tbody tr'), (tr) =>
      tr.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(body[0]).toContain('Sent Sep 19, 2026');
    expect(body[1]).toContain('Overdue 4 days');
    expect(body[2]).toContain('Paid Sep 20, 2026');
    expect(body[3]).toContain('Draft');
    expect(body[3]).toContain('—');
    expect(text()).toContain('Showing 1 – 4 of 4 invoices');
    expect(text()).toContain('Branch');

    // Page 2, then Branch: page 1 again; overview, list and ready badge all use the branch.
    page.invoicePage.set(2);
    await settle();
    get('/invoices').flush(pageBody(rows, 45, 2));
    page.changeBranch('b-1');
    await settle();
    expect(get('/invoices/overview').request.params.get('branchId')).toBe('b-1');
    list = get('/invoices');
    expect([list.request.params.get('branchId'), list.request.params.get('page')]).toEqual([
      'b-1',
      '1',
    ]);
    expect(get('/billing-review/queue').request.params.get('branchId')).toBe('b-1');

    // The status filter lives in the URL; a newer load cancels the stale one.
    page.changeStatus('sent');
    await harness.fixture.whenStable();
    const stale = get('/invoices');
    expect(stale.request.params.get('status')).toBe('sent');
    page.changeStatus('paid');
    await harness.fixture.whenStable();
    expect(stale.cancelled).toBe(true);
    const fresh = get('/invoices');
    expect(fresh.request.params.get('status')).toBe('paid');
    fresh.flush(pageBody([PAID_ROW]));
    await settle();
    expect(TestBed.inject(Router).url).toContain('status=paid');
    expect(text()).toContain('Showing 1 – 1 of 1 invoices');

    // Payments tab: clears the status, syncs the URL and keeps the branch.
    await tab('Payments')!.click();
    await settle();
    expect(TestBed.inject(Router).url).toBe('/invoices?tab=payments');
    const payments = get('/invoices/payments');
    expect([payments.request.params.get('page'), payments.request.params.get('branchId')]).toEqual([
      '1',
      'b-1',
    ]);
    // BR-31: gross amount minus refunds, text tags for refunds, online card with no recorder.
    const online = {
      method: 'card_online',
      amount: 357.28,
      receivedByName: null,
    } as const;
    payments.flush(
      pageBody([
        paymentRow(),
        paymentRow({ id: 'pay-2', method: 'cash', reference: 'R-1' }),
        paymentRow({
          ...online,
          id: 'pay-3',
          status: 'partially_refunded',
          refundedAmount: 100,
        }),
        paymentRow({ ...online, id: 'pay-4', status: 'refunded', refundedAmount: 357.28 }),
      ]),
    );
    await settle();
    expect(host.querySelectorAll('.table__table thead th[scope="col"]')).toHaveLength(8);
    expect(text()).toContain('External card payment');
    expect(text()).toContain('R-1');
    expect(text()).toContain('Showing 1 – 4 of 4 payments');
    const paymentRows = Array.from(host.querySelectorAll('.table__table tbody tr'), (row) =>
      row.textContent?.replace(/\s+/g, ' ').trim(),
    );
    expect(paymentRows[2]).toContain('Online card');
    expect(paymentRows[2]).toContain('$257.28 Partially refunded');
    expect(paymentRows[2]).toMatch(/—$/);
    expect(paymentRows[3]).toContain('$0.00 Refunded');
    expect(paymentRows[0]).not.toContain('refunded');
    expect(page.methodOptions.map((option) => option.label)).toContain('Online card');

    // Ready tab: same ready response, no extra requests.
    await tab('Completed jobs ready to invoice')!.click();
    await settle();
    expect(TestBed.inject(Router).url).toBe('/invoices?tab=ready');
    httpTesting.expectNone((r) => r.url === `${API}/invoices` || r.url.endsWith('/payments'));
    expect(text()).toContain('No completed jobs are ready to invoice.');
    expect(host.querySelector('.table a[href="/invoices/review"]')).not.toBeNull();
  });

  it('shows the options failure (Branch hidden, Record payment disabled with Retry), the customer and metrics errors, the actions menu by permission, PDF progress and failure and export messages (FR-11, AC-15, AC-16, AC-21)', async () => {
    await open('/invoices');
    get('/invoices/options').flush(null, { status: 500, statusText: 'Server Error' });
    get('/invoices/customers').flush(null, { status: 500, statusText: 'Server Error' });
    get('/invoices/overview').flush(null, { status: 500, statusText: 'Server Error' });
    get('/invoices').flush(pageBody([invoiceRow(), PAID_ROW]));
    get('/billing-review/queue').flush(readyBody(2));
    await settle();

    expect(text()).toContain("We couldn't load payment options. Try again.");
    expect(text()).toContain("We couldn't load customers.");
    expect(text()).toContain("We couldn't load billing metrics. Try again.");
    expect(text()).not.toContain('Branch');
    expect(text()).toContain('INV-1049');

    // Record payment is offered only for `canRecordPayment` rows and is disabled until options load.
    const menus = host.querySelectorAll<HTMLButtonElement>('.table__menu');
    menus[0].click();
    expect(menuLabels()).toEqual(['View invoice', 'Record payment', 'Download PDF']);
    expect(page.rowMenu()[1].disabled).toBe(true);
    menus[1].click();
    expect(menuLabels()).toEqual(['View invoice', 'Download PDF']);

    button('Retry')!.click(); // metrics
    get('/invoices/overview').flush(overviewBody());
    await settle();
    expect(text()).not.toContain("We couldn't load billing metrics.");
    host.querySelector<HTMLButtonElement>('.hub__message button')!.click(); // options
    get('/invoices/options').flush(optionsBody({ branches: [{ id: 'b-1', name: 'Austin' }] }));
    await settle();
    expect(text()).not.toContain("We couldn't load payment options.");
    expect(text()).not.toContain('Branch'); // a single branch in scope
    menus[0].click();
    expect(page.rowMenu()[1].disabled).toBeFalsy();

    // Download PDF: progress on the row, failure text.
    const download = page.rowMenu().find((item) => item.label === 'Download PDF') as MenuItem;
    download.command!({ originalEvent: new Event('click'), item: download });
    await settle();
    expect(page.pdfBusyId()).toBe('inv-1');
    expect(menus[0].getAttribute('aria-busy')).toBe('true');
    get('/invoices/inv-1/pdf').flush(null, { status: 500, statusText: 'Server Error' });
    expect(page.pdfBusyId()).toBeNull();

    // Export uses that tab's filters; too large and other failures show their messages.
    page.changeInvoiceDates({ from: '2026-09-01' });
    await settle();
    get('/invoices').flush(pageBody([invoiceRow()]));
    page.exportCsv('invoices');
    const exported = get('/invoices/export');
    expect(exported.request.params.get('from')).toBe('2026-09-01');
    expect(exported.request.params.has('page')).toBe(false);
    exported.flush(new Blob(['x']), { status: 409, statusText: 'Conflict' });
    page.exportCsv('payments');
    get('/invoices/payments/export').flush(new Blob(['x']), {
      status: 500,
      statusText: 'Server Error',
    });
    const urlApi = URL as unknown as Record<string, unknown>;
    urlApi['createObjectURL'] = vi.fn(() => 'blob:csv');
    urlApi['revokeObjectURL'] = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    page.exportCsv('invoices');
    get('/invoices/export').flush(new Blob(['a']), {
      headers: { 'Content-Disposition': 'attachment; filename=invoices-2026-10-09.csv' },
    });
    expect(urlApi['createObjectURL']).toHaveBeenCalledTimes(1);
    expect(toasts.map((t) => t.summary)).toEqual([
      "We couldn't download the PDF. Try again.",
      'Narrow the filters to export 5,000 rows or fewer.',
      "We couldn't export. Try again.",
    ]);
  });

  it('gives roles without read access the forbidden state without requests (BR-20, AC-14)', async () => {
    await open('/invoices', 'technician');
    await settle();
    expect(text()).toContain("You don't have access to invoices.");
    expect(host.querySelector('[role="tablist"]')).toBeNull();
  });
});
