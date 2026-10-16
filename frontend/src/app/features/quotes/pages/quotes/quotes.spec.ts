import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import {
  QuoteDisplayStatus,
  QuoteRow,
  QuotesOptions,
  QuotesPage,
  QuotesSummary,
} from '../../models/quotes-list.model';
import { QUOTE_STATUS_CHIPS, quoteActionTargets, sentText } from '../../utils/quotes-list-format';
import { Quotes } from './quotes';

const API = 'http://api.test';

@Component({ template: '<p>stub</p>' })
class Stub {}

const options = (branches = 2): QuotesOptions => ({
  branches: [
    { id: 'br-1', name: 'Central' },
    { id: 'br-2', name: 'North' },
  ].slice(0, branches),
  organizationTimezone: 'America/Chicago',
  currency: 'USD',
  canManage: true,
  canManageWorkOrders: true,
  canReadWorkOrders: true,
});

const summary = (readyRequests = 2): QuotesSummary => ({
  currency: 'USD',
  drafts: { count: 3, amount: 1200 },
  awaitingResponse: { count: 5, amount: 8000.5 },
  approvedThisMonth: { count: 2, amount: 4500 },
  needsAttention: { count: 1 },
  readyRequests,
});

const counts = { all: 12, drafts: 3, sent: 5, approved: 2, closed: 2 };

const quote = (patch: Partial<QuoteRow> = {}): QuoteRow => ({
  id: 'q-1',
  displayNumber: 'Q-2036',
  revisionNo: 2,
  revisionInProgress: true,
  customerName: 'Sofia Martinez',
  property: { name: 'Home', postalCode: '78704', city: 'Austin' },
  request: { id: 'req-1', displayNumber: 'REQ-1048', title: 'Kitchen sink leak' },
  branch: { id: 'br-1', name: 'Central' },
  total: 357.28,
  currency: 'USD',
  status: 'awaiting_response',
  sentAt: '2026-10-09T13:30:00Z',
  validUntil: '2026-10-23',
  respondedAt: null,
  updatedAt: '2026-10-09T13:30:00Z',
  createdAt: '2026-10-01T13:30:00Z',
  workOrderId: null,
  actions: ['view', 'continue_revision', 'revise', 'open_request'],
  ...patch,
});

const page = (items: QuoteRow[], total = items.length): QuotesPage => ({ items, total, counts });

describe('Quotes workspace', { timeout: 30_000 }, () => {
  let httpTesting: HttpTestingController;
  let harness: RouterTestingHarness;

  const settle = async (): Promise<void> => {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  };
  const text = (): string => (document.body.textContent ?? '').replace(/\s+/g, ' ').trim();
  const control = (label: string): HTMLElement | undefined =>
    Array.from(document.body.querySelectorAll<HTMLElement>('button, a')).find(
      (candidate) => candidate.textContent?.replace(/\s+/g, ' ').trim() === label,
    );
  const url = (): string => TestBed.inject(Router).url;
  const list = (match: Record<string, string | null> = {}) =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/quotes` &&
        Object.entries(match).every(([key, value]) => r.params.get(key) === value),
    );
  const summaryRequest = (branchId: string | null = null) =>
    httpTesting.expectOne(
      (r) => r.url === `${API}/quotes/summary` && r.params.get('branchId') === branchId,
    );
  const flushOptions = (body: QuotesOptions = options()): void => {
    httpTesting.expectOne(`${API}/quotes/options`).flush(body);
  };
  const serverError = { status: 500, statusText: 'Server Error' };
  const openMenu = async (number: string): Promise<HTMLElement[]> => {
    document
      .querySelector<HTMLButtonElement>(`button[aria-label="More actions for ${number}"]`)!
      .click();
    await vi.waitFor(() =>
      expect(document.querySelectorAll('[role="menuitem"]').length).toBeGreaterThan(0),
    );
    return Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'));
  };

  async function setup(role: string, startUrl: string, keepSession = false): Promise<void> {
    if (!keepSession) {
      sessionStorage.clear();
    }
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'quotes', component: Quotes },
          { path: 'quotes/:quoteId', component: Stub },
          { path: 'quotes/:quoteId/edit', component: Stub },
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
      user: {
        id: 'u-1',
        firstName: 'Alex',
        lastName: 'Morgan',
        email: 'alex@acme.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: role, name: role },
    });
    TestBed.inject(Router).setUpLocationChangeListener();
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(startUrl);
  }

  afterEach(() => httpTesting?.verify());

  it('normalizes the URL before requesting, resets the page on every change and drives search, tabs and Clear filters (FR-09, AC-12, AC-19)', async () => {
    await setup('owner', '/quotes?tab=bogus&page=0&pageSize=7&sort=zzz&date=1y&status=expired');
    httpTesting.expectNone((r) => r.url !== `${API}/quotes/options`);
    flushOptions();
    await settle();
    expect(url()).toBe('/quotes?status=expired');
    summaryRequest().flush(summary());
    list({
      tab: 'all',
      sort: 'updated',
      date: 'any',
      page: '1',
      pageSize: '10',
      status: 'expired',
    }).flush(page([quote()], 12));
    await settle();
    expect(text()).toContain('All quotes 12');
    expect(text()).toContain('1–10 of 12');
    expect(control('Clear filters 1')).toBeDefined();
    expect(document.querySelector('[role="status"]')?.textContent).toContain('12 quotes');

    // A tab change resets the status and the page; the metrics are not reloaded.
    control('Sent 5')!.click();
    await settle();
    expect(url()).toBe('/quotes?tab=sent');
    list({ tab: 'sent', status: null, page: '1' }).flush(page([], 0));
    await settle();
    expect(text()).toContain('No quotes match the current view.');

    // Search is debounced by 300 ms; Clear filters resets search, status and date but keeps the tab.
    const input = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = '  sink ';
    input.dispatchEvent(new Event('input'));
    httpTesting.expectNone((r) => r.url === `${API}/quotes`);
    await vi.waitFor(() => expect(url()).toBe('/quotes?tab=sent&q=sink'), { timeout: 2000 });
    await settle();
    list({ tab: 'sent', q: 'sink' }).flush(page([], 0));
    await settle();
    control('Clear filters 1')!.click();
    await settle();
    expect(url()).toBe('/quotes?tab=sent');
    list({ tab: 'sent', q: null }).flush(page([], 0));
    await settle();
    TestBed.resetTestingModule();

    // A foreign branch resets to the default; a page past the last moves to the last page.
    await setup('dispatcher', '/quotes?branch=other&page=9');
    flushOptions();
    await settle();
    expect(url()).toBe('/quotes?page=9');
    summaryRequest().flush(summary());
    list({ page: '9', branchId: null }).flush({ items: [], total: 23, counts });
    await settle();
    expect(url()).toBe('/quotes?page=3');
    list({ page: '3' }).flush(page([quote()], 23));
    await settle();
    expect(text()).toContain('21–23 of 23');
    TestBed.resetTestingModule();

    // A 404 branch from the server resets to the default branch.
    await setup('viewer', '/quotes?branch=br-2');
    flushOptions();
    await settle();
    summaryRequest('br-2').flush(summary());
    list({ branchId: 'br-2' }).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(url()).toBe('/quotes');
    summaryRequest().flush(summary());
    list({ branchId: null }).flush(page([]));
  });

  it('renders rows, the fixed status map and the server-built inline button and menu targets (FR-05, FR-06, AC-13, AC-14, AC-15)', async () => {
    await setup('owner', '/quotes');
    flushOptions();
    await settle();
    summaryRequest().flush(summary());
    list().flush(
      page([
        quote(),
        quote({
          id: 'q-2',
          displayNumber: 'Q-2037',
          revisionNo: 1,
          revisionInProgress: false,
          status: 'draft',
          property: null,
          sentAt: null,
          validUntil: null,
          actions: ['view', 'continue_draft', 'open_request'],
        }),
        quote({
          id: 'q-3',
          displayNumber: 'Q-2038',
          revisionInProgress: false,
          status: 'approved',
          respondedAt: '2026-10-10T13:30:00Z',
          workOrderId: 'wo-9',
          actions: ['view', 'view_job', 'create_work_order', 'open_request'],
        }),
      ]),
    );
    await settle();
    expect(text()).toContain('Q-2036');
    expect(text()).toContain('Revision 2');
    expect(text()).toContain('Revision 3 in progress');
    expect(text()).toContain('Home · 78704 Austin');
    expect(text()).toContain('No property');
    expect(text()).toContain('$357.28');
    expect(text()).toContain('Sent Oct 9 · Valid until Oct 23');
    expect(text()).toContain('Draft · Not sent');
    // Date-only validity never shifts a day west of UTC (BR-13, AC-12).
    const now = new Date('2026-10-14T12:00:00Z');
    expect(sentText(quote({ status: 'expired' }), 'America/Chicago', now)).toBe('Expired Oct 23');
    expect(sentText(quote({ validUntil: '2027-01-02' }), 'America/Chicago', now)).toContain(
      'Valid until Jan 2, 2027',
    );
    expect(text()).toContain('Approved Oct 10');
    expect(text()).toContain('Awaiting response');
    expect(document.querySelector('a[href="/requests?request=req-1"]')?.textContent?.trim()).toBe(
      'REQ-1048',
    );
    expect(document.querySelector('time')?.getAttribute('aria-label')).toContain('Oct 9, 2026');
    expect(document.querySelector('a[aria-label="View Q-2036"]')?.getAttribute('href')).toBe(
      '/quotes/q-1',
    );
    expect(document.querySelector('a[aria-label="Continue Q-2037"]')?.getAttribute('href')).toBe(
      '/quotes/q-2/edit',
    );

    // The menu holds the remaining actions, never the inline one.
    let items = await openMenu('Q-2037');
    expect(items.map((item) => item.textContent?.trim())).toEqual(['View quote', 'Open request']);
    document.body.click();
    await settle();
    items = await openMenu('Q-2038');
    expect(items.map((item) => item.textContent?.trim())).toEqual([
      'View job',
      'Create work order',
      'Open request',
    ]);

    // Targets and the status label map are fixed by code (BR-03, BR-15).
    const targets = quoteActionTargets(
      quote({
        workOrderId: 'wo-9',
        actions: [
          'continue_revision',
          'create_work_order',
          'continue_work_order',
          'view_job',
          'open_request',
        ],
      }),
    );
    expect(targets.map((target) => [target.label, target.commands?.join('/')])).toEqual([
      ['Continue revision', '/quotes/q-1/edit'],
      ['Create work order', '/quotes/q-1/work-order'],
      ['Continue work order', '/quotes/q-1/work-order'],
      ['View job', '/jobs/wo-9'],
      ['Open request', '/requests'],
    ]);
    const labels: Record<QuoteDisplayStatus, string> = {
      draft: 'Draft',
      awaiting_response: 'Awaiting response',
      clarification_requested: 'Clarification requested',
      expired: 'Expired',
      approved: 'Approved',
      rejected: 'Rejected',
      cancelled: 'Cancelled',
    };
    for (const [code, label] of Object.entries(labels)) {
      expect(QUOTE_STATUS_CHIPS[code as QuoteDisplayStatus].label).toBe(label);
    }
  });

  it('revises from the menu: success opens the editor, 409 reloads with the changed message, 403 and 404 show unavailable (FR-06, AC-16)', async () => {
    await setup('owner', '/quotes');
    flushOptions();
    await settle();
    summaryRequest().flush(summary());
    list().flush(page([quote()]));
    await settle();

    const reviseItem = async (): Promise<void> => {
      const items = await openMenu('Q-2036');
      (
        items.find((item) => item.textContent?.trim() === 'Revise quote')!.querySelector('a') ??
        items[0]
      ).click();
    };
    const revise = () =>
      httpTesting.expectOne((r) => r.method === 'POST' && r.url === `${API}/quotes/q-1/revise`);

    await reviseItem();
    const conflict = revise();
    expect(conflict.request.body).toEqual({ updatedAt: '2026-10-09T13:30:00Z' });
    conflict.flush(
      { code: 'quote_changed', status: 409, title: 'x' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(text()).toContain('This quote changed. Refresh to see the latest.');
    list().flush(page([quote()]));
    summaryRequest().flush(summary());
    await settle();

    document.body.click();
    await settle();
    await reviseItem();
    revise().flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain("This quote isn't available.");
    list().flush(page([quote()]));
    await settle();

    document.body.click();
    await settle();
    await reviseItem();
    revise().flush(quote(), { status: 200, statusText: 'OK' });
    await vi.waitFor(() => expect(url()).toBe('/quotes/q-1/edit'));
  });

  it('shows the ready-requests badge and notice from the metrics, dismisses it for the session and hides both at 0 (FR-04, FR-07, AC-17)', async () => {
    await setup('viewer', '/quotes');
    flushOptions();
    await settle();
    summaryRequest().flush(summary(1));
    list().flush(page([]));
    await settle();
    expect(text()).toContain('$1,200.00 total');
    expect(text()).toContain('$8,000.50 sent');
    expect(text()).toContain('$4,500.00 this month');
    expect(text()).toContain('Questions or expiring soon');
    expect(control('View ready requests 1')?.getAttribute('href')).toBe(
      '/requests?status=ready_for_quote',
    );
    expect(text()).toContain('1 service request is ready for a quote.');
    expect(control('Review requests')?.getAttribute('href')).toBe(
      '/requests?status=ready_for_quote',
    );

    control('Dismiss')!.click();
    await settle();
    expect(text()).not.toContain('is ready for a quote.');
    expect(sessionStorage.length).toBe(1);

    // A reload in the same tab session keeps it hidden (the badge stays).
    TestBed.resetTestingModule();
    await setup('viewer', '/quotes', true);
    flushOptions();
    await settle();
    summaryRequest().flush(summary(2));
    list().flush(page([]));
    await settle();
    expect(control('View ready requests 2')).toBeDefined();
    expect(text()).not.toContain('is ready for a quote.');
    TestBed.resetTestingModule();

    await setup('viewer', '/quotes');
    flushOptions();
    await settle();
    summaryRequest().flush(summary(3));
    list().flush(page([]));
    await settle();
    expect(text()).toContain('3 service requests are ready for a quote.');
    TestBed.resetTestingModule();

    // At 0 the badge and the notice are hidden.
    await setup('viewer', '/quotes');
    flushOptions();
    await settle();
    summaryRequest().flush(summary(0));
    list().flush(page([]));
    await settle();
    expect(control('View ready requests')).toBeDefined();
    expect(text()).not.toMatch(/service requests? (is|are) ready/);
  });

  it('renders forbidden, options failure, no branches, metrics failure and list failure with fixed copy and partial retries (FR-09, AC-18, AC-21, AC-22)', async () => {
    await setup('technician', '/quotes');
    await settle();
    expect(text()).toContain("You don't have access to quotes.");
    TestBed.resetTestingModule();

    await setup('owner', '/quotes');
    httpTesting.expectOne(`${API}/quotes/options`).flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load quotes. Try again.");
    control('Retry')!.click();
    flushOptions(options(0));
    await settle();
    expect(text()).toContain('No branches available.');
    TestBed.resetTestingModule();

    // Metrics failure hides badge and notice, leaves the table working and retries only the metrics.
    await setup('owner', '/quotes');
    flushOptions(options(1));
    await settle();
    summaryRequest('br-1').flush(null, serverError);
    list({ branchId: 'br-1' }).flush(page([], 0));
    await settle();
    expect(text()).toContain("We couldn't load quote metrics. Try again.");
    expect(text()).toContain(
      'No quotes yet. Quotes are created from requests that are ready for a quote.',
    );
    expect(text()).not.toContain('is ready for a quote.');
    control('Retry')!.click();
    summaryRequest('br-1').flush(summary());
    await settle();
    expect(text()).toContain('Drafts3');
    TestBed.resetTestingModule();

    // List failure keeps the metrics and retries only the list; a 403 shows the forbidden state.
    await setup('viewer', '/quotes');
    flushOptions();
    await settle();
    summaryRequest().flush(summary());
    list().flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load quotes. Try again.");
    expect(text()).toContain('Drafts3');
    control('Retry')!.click();
    list().flush(null, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(text()).toContain("You don't have access to quotes.");
  });
});
