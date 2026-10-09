import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Confirmation, ConfirmationService, MessageService } from 'primeng/api';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { BillingReviewDetail, QueueItem, QueueResponse } from '../../models/billing-review.model';
import { addDays, todayIn } from '../../utils/billing-review-format';
import { BillingReview } from './billing-review';

const API = 'http://api.test/billing-review';
const TZ = 'America/Chicago';
const TODAY = todayIn(TZ);

@Component({ template: '<p>stub</p>' })
class Stub {}

const ITEM_1: QueueItem = {
  workOrderId: 'wo-1',
  number: 'WO-3091',
  title: 'Kitchen sink leak repair',
  customerName: 'Sofia Martinez',
  approvedTotal: 357.28,
  currency: 'USD',
  completedAt: '2026-09-23T16:13:00Z',
  laborVariance: 'under',
  materialVariance: false,
  ready: false,
  followUp: false,
};
const ITEM_2: QueueItem = {
  ...ITEM_1,
  workOrderId: 'wo-2',
  number: 'WO-3090',
  title: 'AC not cooling',
  customerName: 'David Chen',
  approvedTotal: 286.15,
  laborVariance: 'none',
  ready: true,
};
const queueBody = (items: readonly QueueItem[], total = items.length): QueueResponse => ({
  items,
  page: 1,
  pageSize: 20,
  total,
  tabs: { all: total, variances: 1, ready: 1 },
  metrics: {
    needsReview: 6,
    withVariances: 2,
    readyToInvoice: 4,
    completedValue: 3842.5,
    currency: 'USD',
  },
});

const detailBody = (
  id: string,
  overrides: Partial<BillingReviewDetail> = {},
): BillingReviewDetail => ({
  header: {
    workOrderId: id,
    number: id === 'wo-1' ? 'WO-3091' : 'WO-3090',
    title: id === 'wo-1' ? 'Kitchen sink leak repair' : 'AC not cooling',
    quoteNumber: 'Q-2036',
    customerName: 'Sofia Martinez',
    propertyAddress: '1842 Oak Street, Austin, TX 78704',
    completedByName: 'Carlos Rivera',
    completedAt: '2026-09-23T16:13:00Z',
    timezone: TZ,
  },
  lines: [
    {
      index: 1,
      kind: 'quote',
      name: 'Leak repair labor',
      approved: '2h',
      actual: '1h 42m',
      billable: '2h',
      tax: 'taxable',
      amount: 190,
      status: 'matches',
    },
    {
      index: 2,
      kind: 'quote',
      name: 'P-trap assembly',
      approved: '1',
      actual: '2',
      billable: '1',
      tax: 'non_taxable',
      amount: 38,
      status: 'over',
    },
    {
      index: null,
      kind: 'not_in_quote',
      name: 'Extra fitting',
      approved: null,
      actual: '1',
      billable: '0',
      tax: null,
      amount: null,
      status: 'not_in_quote',
    },
  ],
  laborVariance: 'under',
  materialVariance: true,
  totals: {
    approvedSubtotal: 335,
    invoiceSubtotal: 335,
    discountTotal: 10,
    taxLabel: 'Tax (8.25%)',
    taxTotal: 22.28,
    invoiceTotal: 357.28,
    variance: 0,
    currency: 'USD',
  },
  verification: [
    { key: 'checklist', met: true, mandatory: true, label: '5/5 tasks complete' },
    { key: 'photos', met: true, mandatory: true, label: 'Before and after photos' },
    { key: 'materials', met: false, mandatory: false, label: 'No materials recorded' },
  ],
  ready: false,
  evidence: {
    count: 3,
    thumbnails: [
      { id: 'e-1', type: 'before' },
      { id: 'e-2', type: 'after' },
    ],
  },
  workEvidence: [
    {
      visitId: 'v-1',
      visitNumber: 1,
      checklist: [{ label: 'Replace trap', required: true, completed: true }],
      materials: [{ description: 'P-trap', quantity: '2', unit: 'ea', origin: 'added' }],
      evidence: [{ id: 'e-1', type: 'before', caption: 'Leak' }],
      completionSummary: 'Fixed the leak',
      acknowledgment: {
        method: 'signed',
        signerName: 'Sofia Martinez',
        relationship: 'family_member',
        comment: null,
        signatureCaptured: true,
      },
    },
  ],
  auditTrail: [],
  invoiceDefaults: {
    numberPreview: 'INV-5084',
    issueDate: TODAY,
    paymentTerms: 'due_upon_receipt',
    dueDate: TODAY,
    taxLabel: 'Tax (8.25%)',
    currency: 'USD',
  },
  note: null,
  followUp: null,
  canAct: true,
  ...overrides,
});

describe('Billing review page', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<BillingReview>;
  let page: BillingReview;
  let host: HTMLElement;
  let toasts: { key?: string; severity?: string; summary?: string; data?: unknown }[];

  const call = (method: string, path: string): TestRequest =>
    httpTesting.expectOne((r) => r.method === method && r.url === `${API}/${path}`);
  const flushInit = (items: readonly QueueItem[] = [ITEM_1, ITEM_2], canAct = true): void => {
    call('GET', 'options').flush({
      timezone: TZ,
      currency: 'USD',
      branches: [{ id: 'b-1', name: 'Austin' }],
      technicians: [{ id: 't-1', name: 'Carlos Rivera' }],
      canAct,
    });
    call('GET', 'queue').flush(queueBody(items, items.length));
    if (items.length > 0) {
      call('GET', `work-orders/${items[0].workOrderId}`).flush(
        detailBody(items[0].workOrderId, { canAct }),
      );
    }
  };

  async function setup(roleCode = 'owner', init = true, url: string | null = null): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideRouter([
          { path: 'auth/sign-in', component: Stub },
          { path: 'review', component: Stub },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).loadCurrent().subscribe();
    httpTesting.expectOne('http://api.test/sessions/current').flush({
      user: { id: 'u-1', firstName: 'Alex', lastName: 'Morgan', email: 'alex@acme.com' },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: roleCode, name: roleCode },
    });
    if (url !== null) {
      await TestBed.inject(Router).navigateByUrl(url);
    }
    fixture = TestBed.createComponent(BillingReview);
    page = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    toasts = [];
    vi.spyOn(fixture.debugElement.injector.get(MessageService), 'add').mockImplementation(
      (message) => void toasts.push(message),
    );
    fixture.detectChanges();
    if (init) {
      flushInit();
    }
    await settle();
  }

  const settle = async (): Promise<void> => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const text = (): string => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const button = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
  const tab = (label: string): HTMLButtonElement | undefined =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find((b) =>
      b.textContent?.trim().startsWith(label),
    );
  const confirmations = (): Confirmation[] => {
    const captured: Confirmation[] = [];
    vi.spyOn(fixture.debugElement.injector.get(ConfirmationService), 'confirm').mockImplementation(
      (confirmation) => {
        captured.push(confirmation);
        return undefined as never;
      },
    );
    return captured;
  };
  const type = async (selector: string, value: string): Promise<void> => {
    const field = host.querySelector<HTMLInputElement | HTMLTextAreaElement>(selector)!;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    await settle();
  };

  afterEach(() => httpTesting.verify());

  it('renders metrics, tabs and items, and syncs tabs, search, paging, export and the empty, filtered and error states (FR-14, AC-20, AC-25)', async () => {
    await setup();

    expect(text()).toContain('Completed jobs review');
    expect(text()).toContain('Verify completed work before invoicing.');
    expect(
      Array.from(host.querySelectorAll('.review__metric'), (n) =>
        Array.from(n.children, (child) => child.textContent?.trim()).join(' '),
      ),
    ).toEqual([
      'Needs review 6',
      'With variances 2',
      'Ready to invoice 4',
      'Completed value $3,842.50',
    ]);
    expect(text()).toContain('Needs review (2)');
    expect(
      Array.from(host.querySelectorAll('.queue [role="tab"]'), (n) => n.textContent?.trim()),
    ).toEqual(['All (2)', 'Variances (1)', 'Ready (1)']);
    const items = host.querySelectorAll<HTMLButtonElement>('.queue__item');
    expect(items[0].textContent).toContain('$357.28');
    expect(items[0].textContent).toContain('#WO-3091');
    expect(items[0].textContent).toContain('Completed Sep 23, 2026, 11:13 AM');
    expect(items[0].textContent).toContain('Labor variance');
    expect(items[1].textContent).toContain('No variance');
    expect(items[0].getAttribute('aria-current')).toBe('true');
    expect(items[1].getAttribute('aria-current')).toBeNull();

    // A tab resets to page 1 and selects the first item.
    tab('Variances')!.click();
    let queue = call('GET', 'queue');
    expect(queue.request.params.get('tab')).toBe('variances');
    expect(queue.request.params.get('page')).toBe('1');
    expect(queue.request.params.get('completed')).toBe('30d');
    queue.flush(queueBody([ITEM_1], 1));
    call('GET', 'work-orders/wo-1').flush(detailBody('wo-1'));
    await settle();

    // Search is debounced by 300 ms and trimmed.
    await type('input[type="search"]', ' WO-3091 ');
    httpTesting.expectNone((r) => r.url === `${API}/queue`);
    await new Promise((resolve) => setTimeout(resolve, 350));
    queue = call('GET', 'queue');
    expect(queue.request.params.get('search')).toBe('WO-3091');
    queue.flush(queueBody([ITEM_1, ITEM_2], 45));
    call('GET', 'work-orders/wo-1').flush(detailBody('wo-1'));
    await settle();

    // Paging: 45 jobs at 20 per page.
    expect(text()).toContain('Page 1 of 3');
    button('Next')!.click();
    queue = call('GET', 'queue');
    expect(queue.request.params.get('page')).toBe('2');
    queue.flush({ ...queueBody([ITEM_2], 45), page: 2 });
    call('GET', 'work-orders/wo-2').flush(detailBody('wo-2'));
    await settle();
    expect(text()).toContain('Page 2 of 3');

    // A page emptied by jobs leaving the queue reloads the last page instead of the empty state.
    button('Next')!.click();
    queue = call('GET', 'queue');
    expect(queue.request.params.get('page')).toBe('3');
    queue.flush({ ...queueBody([], 40), page: 3 });
    queue = call('GET', 'queue');
    expect(queue.request.params.get('page')).toBe('2');
    queue.flush({ ...queueBody([ITEM_2], 40), page: 2 });
    call('GET', 'work-orders/wo-2').flush(detailBody('wo-2'));
    await settle();
    expect(text()).toContain('Page 2 of 2');
    expect(text()).not.toContain('No completed jobs need review.');

    // Filtered empty offers Clear filters; the unfiltered empty is the plain message.
    page.changeFilters({ variance: 'labor' });
    call('GET', 'queue').flush(queueBody([], 0));
    await settle();
    expect(text()).toContain('No jobs match these filters.');
    button('Clear filters')!.click();
    queue = call('GET', 'queue');
    expect([queue.request.params.get('variance'), queue.request.params.get('search')]).toEqual([
      'all',
      null,
    ]);
    queue.flush(queueBody([], 0));
    await settle();
    expect(text()).toContain('No completed jobs need review.');

    // Load error with Retry.
    page.changeFilters({ tab: 'ready' });
    call('GET', 'queue').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load completed jobs. Try again.");
    button('Retry')!.click();
    call('GET', 'queue').flush(queueBody([ITEM_1], 1));
    call('GET', 'work-orders/wo-1').flush(detailBody('wo-1'));
    await settle();

    // Export: CSV of the filtered queue; too large and other failures show their messages.
    const urlApi = URL as unknown as Record<string, unknown>;
    urlApi['createObjectURL'] = vi.fn(() => 'blob:csv');
    urlApi['revokeObjectURL'] = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    page.exportCsv();
    let exportRequest = call('GET', 'export');
    expect(exportRequest.request.params.get('tab')).toBe('ready');
    expect(exportRequest.request.params.has('page')).toBe(false);
    exportRequest.flush(new Blob(['a']), {
      headers: {
        'Content-Disposition': 'attachment; filename=completed-jobs-review-2026-10-08.csv',
      },
    });
    expect(urlApi['createObjectURL']).toHaveBeenCalledTimes(1);
    page.exportCsv();
    call('GET', 'export').flush(new Blob(['x']), { status: 409, statusText: 'Conflict' });
    page.exportCsv();
    exportRequest = call('GET', 'export');
    exportRequest.flush(new Blob(['x']), { status: 500, statusText: 'Server Error' });
    expect(toasts.map((t) => t.summary)).toEqual([
      'Narrow the filters to export 5,000 jobs or fewer.',
      "We couldn't export the queue. Try again.",
    ]);
  });

  it('renders the comparison, totals, verification, evidence, note and tabs, the read-only role and the detail 404 and error states (FR-15, AC-21)', async () => {
    await setup();

    expect(text()).toContain('Kitchen sink leak repair');
    expect(text()).toContain('#WO-3091 | Quote #Q-2036');
    expect(text()).toContain('Completed byCarlos Rivera');
    expect(text()).toContain('Sep 23, 2026, 11:13 AM');
    expect(host.querySelectorAll('.detail__table thead th[scope="col"]')).toHaveLength(8);
    const rows = Array.from(host.querySelectorAll('.detail__table tbody tr'), (tr) =>
      Array.from(tr.children, (cell) => cell.textContent?.replace(/\s+/g, ' ').trim()).join(' '),
    );
    expect(rows[0]).toBe('1 Leak repair labor 2h 1h 42m 2h Taxable $190.00 Matches quote');
    expect(rows[1]).toContain('Non-taxable $38.00 Over quote');
    expect(rows[2].trim()).toBe('Extra fitting — 1 0 — — Not in quote');
    expect(text()).toContain('Additional charges require customer approval.');
    expect(text()).toContain('Adjustments will be available in invoice drafts.');
    expect(button('Add adjustment')!.disabled).toBe(true);
    expect(text()).toContain('Discount$10.00');
    expect(text()).toContain('Tax (8.25%)$22.28');
    expect(text()).toContain('Actual billable subtotal$335.00');
    expect(text()).toContain('Variance$0.00');
    expect(text()).toContain('Evidence (3)');
    expect(host.querySelector('.detail__thumb img')?.getAttribute('src')).toBe(
      `${API}/work-orders/wo-1/evidence/e-1`,
    );
    expect(text()).toContain('0/500');

    // Work evidence and Audit trail tabs; a thumbnail opens Work evidence.
    host.querySelector<HTMLButtonElement>('.detail__thumb')!.click();
    await settle();
    expect(tab('Work evidence')!.getAttribute('aria-selected')).toBe('true');
    expect(text()).toContain('Replace trap');
    expect(text()).toContain('1/1 required tasks');
    expect(text()).toContain('2 ea');
    expect(text()).toContain('Added');
    expect(text()).toContain('Fixed the leak');
    expect(text()).toContain('Signature captured');
    expect(text()).toContain('Family member');
    tab('Audit trail')!.click();
    await settle();
    expect(text()).toContain('No activity recorded.');

    // Read-only role: the actions are replaced by the permission text and the note is read-only.
    page.select('wo-2');
    call('GET', 'work-orders/wo-2').flush(
      detailBody('wo-2', {
        canAct: false,
        laborVariance: 'none',
        materialVariance: false,
        verification: [
          { key: 'photos', met: false, mandatory: true, label: 'Before and after photos missing' },
        ],
      }),
    );
    await settle();
    expect(tab('Billing review')!.getAttribute('aria-selected')).toBe('true');
    expect(text()).toContain('Before and after photos missing Required before invoicing');
    expect(text()).toContain('Only owners and accounting can review and generate invoices.');
    expect(button('Generate invoice')).toBeUndefined();
    expect(button('Return to quote')).toBeUndefined();
    expect(host.querySelector<HTMLTextAreaElement>('textarea')!.readOnly).toBe(true);

    // Detail 404 shows the text and reloads the queue; other failures offer Retry.
    page.select('wo-1');
    call('GET', 'work-orders/wo-1').flush(null, { status: 404, statusText: 'Not Found' });
    call('GET', 'queue').flush(queueBody([ITEM_2], 1));
    call('GET', 'work-orders/wo-2').flush(detailBody('wo-2'));
    await settle();
    page.select('wo-1');
    await settle();
    call('GET', 'work-orders/wo-1').flush(null, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain("We couldn't load this job. Try again.");
    host.querySelector<HTMLButtonElement>('.review__detail .p-button')!.click();
    call('GET', 'work-orders/wo-1').flush(detailBody('wo-1'));
    await settle();
    expect(text()).toContain('Kitchen sink leak repair');
  });

  it('recalculates the due date and validates the issue date, terms and note before sending (FR-15, AC-22)', async () => {
    await setup();

    const due = (): string =>
      host.querySelector<HTMLInputElement>('.invoice input[id$="-due"]')!.value;
    const iso = (days: number): string => {
      const label = new Date(`${addDays(TODAY, days)}T12:00:00Z`);
      return new Intl.DateTimeFormat('en-US', {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
        timeZone: 'UTC',
      }).format(label);
    };
    expect(text()).toContain('Invoice number (preview)');
    expect(host.querySelector<HTMLInputElement>('.invoice input[id$="-number"]')!.value).toBe(
      '#INV-5084',
    );
    expect(host.querySelector<HTMLInputElement>('.invoice input[id$="-tax"]')!.value).toBe(
      'Organization default • 8.25%',
    );
    expect(host.querySelectorAll('.invoice fieldset[disabled]')).toHaveLength(2);
    expect(text()).toContain('Sending and online payments will be available later.');
    expect(text()).toContain('Generating creates a draft invoice. Review it before sending.');
    expect(due()).toBe(iso(0));

    page.setTerms('net_15');
    await settle();
    expect(due()).toBe(iso(15));
    await type('.invoice input[type="date"]', addDays(TODAY, -10));
    expect(due()).toBe(iso(5));
    page.setTerms('net_30');
    await settle();
    expect(due()).toBe(iso(20));

    // Invalid issue date: field error, focus and no request.
    await type('.invoice input[type="date"]', addDays(TODAY, -31));
    button('Generate invoice')!.click();
    await settle();
    expect(text()).toContain('Choose an issue date within the last 30 days.');
    expect(document.activeElement?.id).toMatch(/-issue$/);
    expect(
      host.querySelector('.invoice input[type="date"]')!.getAttribute('aria-describedby'),
    ).toMatch(/-issue-error$/);
    httpTesting.expectNone((r) => r.method === 'POST');
  });

  it('runs the return, follow-up and generate flows: variance dialog only for variances, blocked generation, 201 and 200 messages, retained values and the unsaved note prompt (FR-16, AC-23)', async () => {
    await setup();
    const confirm = confirmations();

    // Unsaved note: switching items asks to discard; Keep editing stays, Discard switches.
    await type('textarea', '  Check the P-trap  ');
    expect(text()).toContain('20/500');
    page.select('wo-2');
    expect(confirm[0].header).toBe('Discard unsaved note?');
    expect([confirm[0].acceptButtonProps?.label, confirm[0].rejectButtonProps?.label]).toEqual([
      'Discard',
      'Keep editing',
    ]);
    confirm[0].reject?.();
    expect(page.selectedId()).toBe('wo-1');

    // Mark for follow-up sends the current note and updates the badge.
    button('Mark for follow-up')!.click();
    await settle();
    expect(button('Generate invoice')!.disabled).toBe(true);
    let request = call('PATCH', 'work-orders/wo-1/review');
    expect(request.request.body).toEqual({
      note: 'Check the P-trap',
      followUp: true,
      reason: 'manual',
    });
    request.flush({
      note: 'Check the P-trap',
      followUp: { at: '2026-10-08T15:00:00Z', byName: 'Alex Morgan' },
    });
    await settle();
    expect(toasts.at(-1)?.summary).toBe('Marked for follow-up.');
    expect(text()).toContain('Follow-up marked by Alex Morgan on Oct 8, 2026');
    expect(text()).toContain('Remove follow-up');
    expect(host.querySelector('.queue__badge--follow-up')).not.toBeNull();

    // Return to quote clears follow-up, keeps the job and selects the next item.
    button('Return to quote')!.click();
    request = call('PATCH', 'work-orders/wo-1/review');
    expect(request.request.body).toEqual({
      note: 'Check the P-trap',
      followUp: false,
      reason: 'returned_to_queue',
    });
    request.flush({ note: 'Check the P-trap', followUp: null });
    expect(toasts.at(-1)?.summary).toBe('Returned to quote.');
    call('GET', 'queue').flush(queueBody([ITEM_1, ITEM_2], 2));
    call('GET', 'work-orders/wo-2').flush(detailBody('wo-2', { laborVariance: 'under' }));
    await settle();
    expect(page.selectedId()).toBe('wo-2');

    // Variances: the dialog opens first and nothing is sent until Generate draft acknowledges.
    button('Generate invoice')!.click();
    expect(confirm.at(-1)?.header).toBe('Generate invoice with variances?');
    expect(confirm.at(-1)?.message).toContain('variances stay for review only');
    expect(confirm.at(-1)?.acceptButtonProps?.label).toBe('Generate draft');
    httpTesting.expectNone((r) => r.method === 'POST');
    confirm.at(-1)?.accept?.();
    request = call('POST', 'work-orders/wo-2/invoice');
    expect(request.request.body).toEqual({
      issueDate: TODAY,
      paymentTerms: 'due_upon_receipt',
      note: '',
      acknowledgeVariances: true,
    });
    // A 409 keeps every entered value and shows its fixed message.
    request.flush({ code: 'invoice_totals_mismatch' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(toasts.at(-1)?.summary).toBe(
      "The approved quote amounts don't match its lines. Review the quote before invoicing.",
    );
    expect(page.issueDate()).toBe(TODAY);

    // 201 announces the number, reloads the queue and selects the next item; 200 says it exists.
    button('Generate invoice')!.click();
    confirm.at(-1)?.accept?.();
    call('POST', 'work-orders/wo-2/invoice').flush(
      {
        changed: true,
        invoice: { id: 'i-1', number: 'INV-5084', status: 'draft', total: 1, currency: 'USD' },
      },
      { status: 201, statusText: 'Created' },
    );
    expect(toasts.at(-1)?.summary).toBe('Draft invoice INV-5084 created.');
    // BR-27: the toast carries View invoice → /invoices/<id> (rendered through the real service).
    expect(toasts.at(-1)).toMatchObject({ key: 'invoice-link', data: { invoiceId: 'i-1' } });
    MessageService.prototype.add.call(
      fixture.debugElement.injector.get(MessageService),
      toasts.at(-1)!,
    );
    await settle();
    expect(host.querySelector('a[href="/invoices/i-1"]')?.textContent?.trim()).toBe('View invoice');
    call('GET', 'queue').flush(queueBody([ITEM_1], 1));
    call('GET', 'work-orders/wo-1').flush(detailBody('wo-1'));
    await settle();
    expect(page.selectedId()).toBe('wo-1');

    // Unmet mandatory item: no dialog; the request goes out and the follow-up state is refreshed.
    const blocked = detailBody('wo-1', {
      verification: [
        { key: 'photos', met: false, mandatory: true, label: 'Before and after photos missing' },
      ],
    });
    page.detail.set(blocked);
    const before = confirm.length;
    button('Generate invoice')!.click();
    expect(confirm).toHaveLength(before);
    request = call('POST', 'work-orders/wo-1/invoice');
    expect(request.request.body.acknowledgeVariances).toBe(false);
    request.flush(
      { code: 'completion_requirements_unmet' },
      { status: 409, statusText: 'Conflict' },
    );
    expect(toasts.at(-1)?.summary).toContain('This job was marked for follow-up.');
    call('GET', 'work-orders/wo-1').flush(
      detailBody('wo-1', {
        followUp: { at: '2026-10-08T15:00:00Z', byName: 'Alex Morgan' },
      }),
    );
    await settle();
    expect(text()).toContain('Follow-up marked by Alex Morgan');

    // Already invoiced elsewhere: 200 changed = false.
    page.detail.set(detailBody('wo-1', { laborVariance: 'none', materialVariance: false }));
    await settle();
    button('Generate invoice')!.click();
    call('POST', 'work-orders/wo-1/invoice').flush({
      changed: false,
      invoice: { id: 'i-2', number: 'INV-5000', status: 'draft', total: 1, currency: 'USD' },
    });
    expect(toasts.at(-1)?.summary).toBe('Draft invoice INV-5000 already exists.');
    call('GET', 'queue').flush(queueBody([ITEM_2], 1));
    call('GET', 'work-orders/wo-2').flush(detailBody('wo-2'));
    await settle();
  });
  it.each([
    ['a job outside the default window is selected and loaded', 200],
    ['a missing job shows the existing 404 state', 404],
  ])('preselects ?workOrderId= with "All time": %s (BR-29, AC-20)', async (_name, status) => {
    await setup('owner', false, '/review?workOrderId=wo-9');
    // The detail loads directly, without waiting for the queue.
    const detailRequest = call('GET', 'work-orders/wo-9');
    call('GET', 'options').flush({
      timezone: TZ,
      currency: 'USD',
      branches: [],
      technicians: [],
      canAct: true,
    });
    const queue = call('GET', 'queue');
    expect(queue.request.params.get('completed')).toBe('all');
    queue.flush(queueBody([ITEM_1, ITEM_2], 2));
    if (status === 200) {
      detailRequest.flush(detailBody('wo-9'));
    } else {
      detailRequest.flush(null, { status: 404, statusText: 'Not Found' });
    }
    await settle();
    // The listed first job is never auto-selected over the preselection.
    httpTesting.expectNone((r) => r.url === `${API}/work-orders/wo-1`);
    expect(page.filters().completed).toBe('all');
    expect(text()).toContain('All time');
    if (status === 200) {
      expect(page.selectedId()).toBe('wo-9');
      expect(text()).toContain('Kitchen sink leak repair');
    } else {
      expect(text()).toContain('This job is no longer in the review queue.');
    }
  });
});
