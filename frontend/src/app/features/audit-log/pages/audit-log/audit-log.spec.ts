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
  AuditDetail,
  AuditFilterOptions,
  AuditItem,
  AuditPage,
} from '../../models/audit-log.model';
import { AuditLog } from './audit-log';

const API = 'http://api.test';
const NOW = new Date('2026-10-15T15:00:00Z');
const INVOICE_ID = '7b1f6c1e-0f55-4b33-9d4e-0c2f0d6c1a10';

@Component({ template: '<p>stub</p>' })
class Stub {}

const FILTERS: AuditFilterOptions = {
  actions: [{ code: 'payment.recorded', label: 'Payment recorded' }],
  entityTypes: [{ code: 'invoice', label: 'Invoice' }],
  users: [
    { id: 'system', name: 'System' },
    { id: '3f2504e0-4f89-41d3-9a0c-0305e82c3301', name: 'Daniel Kim' },
  ],
  branches: [{ id: 'br-1', name: 'Main branch', isActive: true }],
};

const item = (patch: Partial<AuditItem> = {}): AuditItem => ({
  id: 7,
  eventId: 'AUD-7',
  // 2026-10-15T03:14:22Z is still Oct 14 in America/Bogota.
  occurredAt: '2026-10-15T03:14:22Z',
  actor: { id: 'u-1', name: 'Daniel Kim' },
  action: { code: 'payment.recorded', label: 'Payment recorded' },
  entity: { type: 'invoice', label: 'Invoice', id: INVOICE_ID },
  reference: 'INV-1049',
  summary: 'Recorded $149.00 card payment',
  branch: null,
  source: 'Web app',
  ipAddress: null,
  impact: 'high',
  ...patch,
});

const page = (items: AuditItem[], total = items.length): AuditPage => ({
  items,
  total,
  page: 1,
  pageSize: 25,
  timeZone: 'America/Bogota',
});

const detail = (patch: Partial<AuditDetail> = {}): AuditDetail => ({
  ...item(),
  actor: { id: 'u-1', name: 'Daniel Kim', roleName: 'Accountant' },
  changes: [{ field: 'status', label: 'Status', before: 'Sent', after: 'Paid' }],
  metadata: [{ key: 'eventId', value: 'AUD-7' }],
  target: { kind: 'invoice', id: 'inv-1' },
  timeZone: 'America/Bogota',
  ...patch,
});

describe('Audit log page', () => {
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
  // Query order follows the previous URL (router merge), so compare the sorted pairs.
  const norm = (value: string): string => {
    const [path, query = ''] = value.split('?');
    return query === '' ? path : `${path}?${query.split('&').sort().join('&')}`;
  };
  const url = (): string => norm(TestBed.inject(Router).url);
  const list = (match: Record<string, string | null> = {}) =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/audit-log` &&
        Object.entries(match).every(([key, value]) => r.params.get(key) === value),
    );
  const flushFilters = (): void => {
    httpTesting.expectOne(`${API}/audit-log/filters`).flush(FILTERS);
  };
  const summary = {
    eventsToday: 184,
    activeUsersToday: 17,
    highImpactToday: 6,
    accessChangesToday: 3,
    timeZone: 'America/Bogota',
  };
  const serverError = { status: 500, statusText: 'Server Error' };
  const viewButton = (): HTMLButtonElement =>
    document.querySelector<HTMLButtonElement>('button[aria-label="View AUD-7"]')!;
  const sortHeader = (name: string): HTMLElement =>
    Array.from(document.querySelectorAll<HTMLElement>('th')).find((th) =>
      th.textContent?.includes(name),
    )!;
  const setInput = (selector: string, value: string, event = 'input'): void => {
    const input = document.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event(event, { bubbles: true }));
  };
  const submitFilters = async (): Promise<void> => {
    document.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  };
  const component = (): AuditLog => harness.routeDebugElement!.componentInstance as AuditLog;

  async function setup(startUrl: string, role = 'owner'): Promise<void> {
    vi.useFakeTimers({ toFake: ['Date'], now: NOW });
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'audit-log', component: AuditLog },
          { path: 'invoices/:id', component: Stub },
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

  async function loaded(startUrl = '/audit-log', total = 120): Promise<void> {
    await setup(startUrl);
    flushFilters();
    await settle();
    httpTesting.expectOne(`${API}/audit-log/summary`).flush(summary);
    list().flush(page([item()], total));
    await settle();
  }

  afterEach(() => {
    httpTesting?.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('restores and normalizes the URL, loads only after the options, and applies edits only on Apply/Enter and Reset (FR-13, AC-19)', async () => {
    await setup(
      '/audit-log?q=pump&impact=high&actor=system&action=nope&sort=impact&direction=asc&page=2&pageSize=50&from=2026-13-01',
    );
    httpTesting.expectNone((r) => r.url !== `${API}/audit-log/filters`);
    flushFilters();
    await settle();
    // Invalid values (unknown action, impossible date) fall back to defaults before any list call.
    expect(url()).toBe(
      norm(
        '/audit-log?q=pump&actor=system&impact=high&sort=impact&direction=asc&page=2&pageSize=50',
      ),
    );
    httpTesting.expectOne(`${API}/audit-log/summary`).flush(summary);
    const first = list({ q: 'pump', actorId: 'system', impact: 'high', sort: 'impact' });
    expect(first.request.params.get('direction')).toBe('asc');
    expect(first.request.params.get('page')).toBe('2');
    expect(first.request.params.get('pageSize')).toBe('50');
    expect(first.request.params.has('from')).toBe(false);
    expect(first.request.params.has('action')).toBe(false);
    first.flush(page([item()], 120));
    await settle();
    expect(text()).toContain('Audit trail');
    expect(text()).toContain('Events today184');
    expect(text()).toContain('Access changes3');
    // BR-24: table times use the response time zone (Oct 14 in Bogota), not UTC.
    expect(text()).toContain('Oct 14, 202610:14:22 PM');
    expect(text()).toContain('All branches');
    expect(text()).toContain('Showing 51–100 of 120 events');

    // Edits do nothing until Apply; the request keeps the applied values.
    setInput('input[type="search"]', '  invoice  ');
    setInput('input[aria-label="From date"]', '2026-10-01', 'change');
    await settle();
    httpTesting.expectNone(`${API}/audit-log`);
    expect(url()).toContain('q=pump');

    // Apply (also Enter in the search field) resets to page 1 and writes the URL.
    await submitFilters();
    expect(url()).toBe(
      norm(
        '/audit-log?from=2026-10-01&q=invoice&actor=system&impact=high&sort=impact&direction=asc&pageSize=50',
      ),
    );
    list({ q: 'invoice', from: '2026-10-01', page: '1' }).flush(page([item()], 1));
    await settle();

    // Reset restores the defaults (keeping the page size) and reloads.
    control('Reset')!.click();
    await settle();
    expect(url()).toBe(norm('/audit-log?pageSize=50'));
    const reset = list({ sort: 'occurredAt', direction: 'desc', page: '1', pageSize: '50' });
    expect(['q', 'from', 'to', 'actorId', 'impact'].some((k) => reset.request.params.has(k))).toBe(
      false,
    );
    reset.flush(page([item()], 1));
  });

  it('requests sort and paging from the server and renders loading, empty, error and forbidden states (FR-13, AC-18, AC-20)', async () => {
    await setup('/audit-log');
    flushFilters();
    await settle();
    // Loading: skeletons while the list and metrics are pending.
    expect(document.querySelectorAll('p-skeleton').length).toBeGreaterThan(0);
    // Metrics and list fail independently; Try again reloads only the failed part.
    httpTesting.expectOne(`${API}/audit-log/summary`).flush(null, serverError);
    list().flush(page([item()], 120));
    await settle();
    expect(text()).toContain("We couldn't load audit events.");
    expect(text()).toContain('120 events');
    expect(sortHeader('Timestamp').getAttribute('aria-sort')).toBe('descending');
    expect(sortHeader('Impact').getAttribute('aria-sort')).toBe('none');
    control('Try again')!.click();
    httpTesting.expectOne(`${API}/audit-log/summary`).flush(summary);
    httpTesting.expectNone(`${API}/audit-log`);
    await settle();
    expect(text()).toContain('Showing 1–25 of 120 events');

    control('Impact')!.click();
    await settle();
    list({ sort: 'impact', direction: 'desc', page: '1' }).flush(page([item()], 120));
    await settle();
    expect(sortHeader('Impact').getAttribute('aria-sort')).toBe('descending');
    control('Impact')!.click();
    await settle();
    list({ sort: 'impact', direction: 'asc' }).flush(page([item()], 120));
    await settle();
    expect(sortHeader('Impact').getAttribute('aria-sort')).toBe('ascending');
    control('Timestamp')!.click();
    await settle();
    list({ sort: 'occurredAt', direction: 'desc' }).flush(page([item()], 120));
    await settle();

    document.querySelector<HTMLElement>('p-paginator button[aria-label="2"]')!.click();
    await settle();
    list({ page: '2', pageSize: '25' }).flush(page([item()], 120));
    await settle();
    expect(text()).toContain('Showing 26–50 of 120 events');
    component().changePageSize('100');
    await settle();
    list({ page: '1', pageSize: '100' }).flush(page([item()], 120));
    await settle();
    expect(url()).toBe(norm('/audit-log?pageSize=100'));

    // Empty: message with Reset.
    component().sortBy('impact');
    await settle();
    list({ sort: 'impact' }).flush(page([], 0));
    await settle();
    expect(text()).toContain('No audit events match these filters.');
    control('Reset')!.click();
    await settle();
    list({ sort: 'occurredAt', pageSize: '100' }).flush(page([item()], 1));
    await settle();

    // List error, retry, then a 403 swaps the page for the forbidden state and hides Export.
    component().sortBy('impact');
    await settle();
    list({ sort: 'impact' }).flush(null, serverError);
    await settle();
    expect(text()).toContain("We couldn't load audit events.");
    control('Try again')!.click();
    list({ sort: 'impact' }).flush(null, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(text()).toContain("You don't have access to the audit trail.");
    expect(control('Export CSV')).toBeUndefined();
  });

  it('opens the event drawer with detail, copy, target link and related activity, and closes (FR-14, AC-21, AC-12)', async () => {
    await loaded();
    // 404: unavailable message; the close button closes the dialog (focus return is the shared
    // DrawerShell's job and needs the browser's animation events, so it is a manual check).
    viewButton().focus();
    viewButton().click();
    await settle();
    httpTesting
      .expectOne(`${API}/audit-log/7`)
      .flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby')).toBe(
      'audit-drawer-title',
    );
    expect(text()).toContain('This event is no longer available.');
    document.querySelector<HTMLButtonElement>('button[aria-label="Close audit event"]')!.click();
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());

    // Detail: sections, null IP, read-only note, and a link only with a target.
    viewButton().click();
    await settle();
    httpTesting.expectOne(`${API}/audit-log/7`).flush(detail());
    await settle();
    expect(text()).toContain('Audit event');
    expect(text()).toContain('Oct 14, 2026 at 10:14:22 PM');
    expect(text()).toContain('Accountant');
    expect(text()).toContain('Change summary');
    expect(text()).toContain('Audit events are read-only and cannot be edited or deleted.');
    expect(text()).toContain("Events are retained according to your organization's data policy.");
    expect(document.querySelector('.audit-drawer__facts')?.textContent).toContain('—');
    expect(control('Open Invoice')?.getAttribute('href')).toBe('/invoices/inv-1');
    expect(control('Edit') ?? control('Delete')).toBeUndefined();

    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    control('Copy event ID AUD-7')!.click();
    await settle();
    expect(writeText).toHaveBeenCalledWith('AUD-7');
    expect(text()).toContain('Event ID copied');

    // View related activity filters by the entity over the last 366 days and closes the drawer.
    control('View related activity')!.click();
    await settle();
    expect(url()).toBe(
      norm(`/audit-log?from=2025-10-15&to=2026-10-15&entity=invoice&entityId=${INVOICE_ID}`),
    );
    list({ entityType: 'invoice', entityId: INVOICE_ID, from: '2025-10-15' }).flush(
      page([item()], 1),
    );
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());

    // No target: no Open link; no entity id: no related activity.
    viewButton().click();
    await settle();
    httpTesting.expectOne(`${API}/audit-log/7`).flush(
      detail({
        target: null,
        changes: [],
        entity: { type: 'export', label: 'Export', id: null },
      }),
    );
    await settle();
    expect(control('Open Export')).toBeUndefined();
    expect(control('View related activity')).toBeUndefined();
    expect(text()).not.toContain('Change summary');
  });

  it('exports the applied filters with busy state and maps 409 and other failures to their messages (FR-13, AC-22)', async () => {
    const urlApi = URL as unknown as Record<string, unknown>;
    urlApi['createObjectURL'] = vi.fn(() => 'blob:csv');
    urlApi['revokeObjectURL'] = vi.fn();
    const downloads: string[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      downloads.push(this.download);
    });
    await loaded('/audit-log?q=pump&sort=impact');
    setInput('input[type="search"]', 'unsaved');

    control('Export CSV')!.click();
    await settle();
    const request = httpTesting.expectOne((r) => r.url === `${API}/audit-log/export`);
    expect(request.request.params.get('q')).toBe('pump');
    expect(request.request.params.get('sort')).toBe('impact');
    expect(request.request.params.has('page')).toBe(false);
    expect(request.request.params.has('pageSize')).toBe(false);
    expect(control('Export CSV')!.hasAttribute('disabled')).toBe(true);
    expect(control('Export CSV')!.getAttribute('aria-busy')).toBe('true');
    request.flush(new Blob(['a']), {
      headers: { 'Content-Disposition': 'attachment; filename=audit-log-2026-10-15.csv' },
    });
    await settle();
    expect(downloads).toEqual(['audit-log-2026-10-15.csv']);
    expect(control('Export CSV')!.hasAttribute('disabled')).toBe(false);

    control('Export CSV')!.click();
    httpTesting
      .expectOne((r) => r.url === `${API}/audit-log/export`)
      .flush(new Blob(['x']), { status: 409, statusText: 'Conflict' });
    await settle();
    expect(text()).toContain('Narrow the filters to export 5,000 rows or fewer.');
    control('Export CSV')!.click();
    httpTesting
      .expectOne((r) => r.url === `${API}/audit-log/export`)
      .flush(new Blob(['x']), serverError);
    await settle();
    expect(text()).toContain("We couldn't export the audit log. Try again.");
  });
});
