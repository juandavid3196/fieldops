import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { NotificationUnreadService } from '../../../../core/services/notification-unread.service';
import { SessionService } from '../../../../core/services/session.service';
import {
  NotificationItem,
  NotificationsOptions,
  NotificationsPage,
} from '../../models/notifications.model';
import { Notifications } from './notifications';

const API = 'http://api.test';
const NOW = new Date('2026-10-15T15:00:00Z');

@Component({ template: '<p>stub</p>' })
class Stub {}

const options = (branches = 2): NotificationsOptions => ({
  branches: [
    { id: 'br-1', name: 'Main branch' },
    { id: 'br-2', name: 'North' },
  ].slice(0, branches),
});

const minutesAgo = (minutes: number): string =>
  new Date(NOW.getTime() - minutes * 60_000).toISOString();

const item = (patch: Partial<NotificationItem> = {}): NotificationItem => ({
  id: 'n-1',
  type: 'quote',
  eventCode: 'quote.approved',
  title: 'Quote Q-15 was approved',
  description: 'Camilo approved the quote for $150.00.',
  entityKind: 'quote',
  entityLabel: 'Q-15',
  secondaryLabel: 'REQ-15',
  branch: { id: 'br-1', name: 'Main branch' },
  createdAt: minutesAgo(12),
  isRead: false,
  target: { kind: 'quote', id: 'q-15' },
  ...patch,
});

const page = (items: NotificationItem[], total = items.length): NotificationsPage => ({
  items,
  total,
  page: 1,
  pageSize: 10,
  counts: { all: total, unread: items.filter((row) => !row.isRead).length },
  timeZone: 'UTC',
});

describe('Notifications page', () => {
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
  const row = (name: string): HTMLButtonElement =>
    document.querySelector<HTMLButtonElement>(`button[aria-label="${name}"]`)!;
  const url = (): string => TestBed.inject(Router).url;
  const list = (match: Record<string, string | null> = {}) =>
    httpTesting.expectOne(
      (r) =>
        r.url === `${API}/notifications` &&
        Object.entries(match).every(([key, value]) => r.params.get(key) === value),
    );
  const flushOptions = (body: NotificationsOptions = options()): void => {
    httpTesting.expectOne(`${API}/notifications/options`).flush(body);
  };
  const summary = (unread = 2, today = 3) => ({ unread, today });
  const serverError = { status: 500, statusText: 'Server Error' };
  const openMenu = async (title: string): Promise<HTMLElement> => {
    document
      .querySelector<HTMLButtonElement>(`button[aria-label="More actions for ${title}"]`)!
      .click();
    await vi.waitFor(() =>
      expect(document.querySelectorAll('[role="menuitem"]').length).toBeGreaterThan(0),
    );
    return document.querySelector<HTMLElement>('[role="menuitem"]')!;
  };

  async function setup(startUrl: string, role = 'owner'): Promise<void> {
    vi.useFakeTimers({ toFake: ['Date'], now: NOW });
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          { path: 'notifications', component: Notifications },
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

  afterEach(() => {
    httpTesting?.verify();
    vi.useRealTimers();
  });

  it('normalizes the URL before requesting and drives tabs, debounced search, Clear filters, past-last page and branch 404 (FR-08, AC-16, AC-17)', async () => {
    await setup('/notifications?tab=bogus&type=zzz&date=1y&page=0&pageSize=7');
    httpTesting.expectNone((r) => r.url !== `${API}/notifications/options`);
    flushOptions();
    await settle();
    expect(url()).toBe('/notifications');
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary());
    list({ tab: 'all', date: 'any', page: '1', pageSize: '10', type: null, branchId: null }).flush({
      ...page([item()], 24),
      counts: { all: 24, unread: 6 },
    });
    await settle();
    expect(text()).toContain('All notifications 24');
    expect(text()).toContain('Unread 6');
    expect(text()).toContain('1–10 of 24');
    expect(document.querySelector('[role="status"]')?.textContent).toContain('24 notifications');

    // A tab change resets the page; the summary is not reloaded.
    control('Unread 6')!.click();
    await settle();
    expect(url()).toBe('/notifications?tab=unread');
    list({ tab: 'unread', page: '1' }).flush(page([]));
    await settle();
    expect(text()).toContain('No notifications match your filters');

    // Search is debounced by 300 ms; Clear filters resets search, type, branch and date.
    const input = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = '  sink ';
    input.dispatchEvent(new Event('input'));
    httpTesting.expectNone((r) => r.url === `${API}/notifications`);
    await vi.waitFor(() => expect(url()).toBe('/notifications?tab=unread&q=sink'), {
      timeout: 2000,
    });
    await settle();
    list({ tab: 'unread', q: 'sink' }).flush(page([]));
    await settle();
    control('Clear filters 1')!.click();
    await settle();
    expect(url()).toBe('/notifications?tab=unread');
    list({ tab: 'unread', q: null }).flush(page([]));
    await settle();
    expect((control('Clear filters') as HTMLButtonElement).disabled).toBe(true);
    TestBed.resetTestingModule();

    // A page past the last moves to the last page.
    await setup('/notifications?page=9', 'dispatcher');
    flushOptions();
    await settle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary());
    list({ page: '9' }).flush({ ...page([], 23), counts: { all: 23, unread: 0 } });
    await settle();
    expect(url()).toBe('/notifications?page=3');
    list({ page: '3' }).flush(page([item()], 23));
    await settle();
    expect(text()).toContain('21–23 of 23');
    TestBed.resetTestingModule();

    // A foreign branch resets to All branches (before and after the server answers).
    await setup('/notifications?branch=other');
    flushOptions();
    await settle();
    expect(url()).toBe('/notifications');
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary());
    list({ branchId: null }).flush(page([]));
    TestBed.resetTestingModule();
    await setup('/notifications?branch=br-2');
    flushOptions();
    await settle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary());
    list({ branchId: 'br-2' }).flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(url()).toBe('/notifications');
    list({ branchId: null }).flush(page([]));
  });

  it('renders grouped rows and handles row selection, menu toggles, error recovery and Mark all as read (FR-05, FR-06, AC-12 to AC-15)', async () => {
    await setup('/notifications');
    flushOptions();
    await settle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary(2, 3));
    list().flush(
      page([
        item(),
        item({
          id: 'n-2',
          type: 'visit',
          eventCode: 'visit.started',
          title: 'Visit started',
          description: 'Dan started WO-9.',
          entityKind: 'visit',
          entityLabel: 'WO-9',
          secondaryLabel: 'Visit 2',
          branch: null,
          createdAt: minutesAgo(120),
          isRead: true,
          target: { kind: 'visit', id: 'v-9' },
        }),
        item({
          id: 'n-3',
          type: 'payment',
          eventCode: 'payment.received',
          title: 'Payment received',
          description: 'Payment of $10.00 was recorded for invoice INV-3.',
          entityKind: 'invoice',
          entityLabel: 'INV-3',
          secondaryLabel: null,
          branch: null,
          createdAt: minutesAgo(60 * 24),
          target: null,
        }),
      ]),
    );
    await settle();
    expect(Array.from(document.querySelectorAll('h2'), (h) => h.textContent?.trim())).toEqual([
      'Today',
      'Earlier',
    ]);
    expect(text()).toContain('12 min ago');
    expect(text()).toContain('2 hr ago');
    expect(text()).toContain('Yesterday');
    expect(text()).toContain('REQ-15 · Main branch');
    expect(text()).toContain('Unread 2');
    expect(document.querySelector('a[href="/quotes/q-15"]')?.textContent).toBe('Q-15');
    expect(document.querySelector('.notif__strong')?.textContent).toBe('INV-3');
    expect(row('Visit started, read, 2 hr ago')).not.toBeNull();

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    // No target: marked read without waiting, toast, no navigation.
    row('Payment received, unread, Yesterday').click();
    await settle();
    expect(row('Payment received, read, Yesterday')).not.toBeNull();
    httpTesting
      .expectOne({ method: 'POST', url: `${API}/notifications/n-3/read` })
      .flush({ summary: summary(1, 3) });
    await settle();
    expect(text()).toContain('This item is no longer available.');
    expect(navigate).not.toHaveBeenCalled();

    // Read row with a target: navigates without any call.
    row('Visit started, read, 2 hr ago').click();
    expect(navigate).toHaveBeenLastCalledWith(['/schedule'], { queryParams: { visit: 'v-9' } });
    httpTesting.expectNone({ method: 'POST' });

    // Unread row with a target: shown as read at once, then navigates; a failed call does not block.
    row('Quote Q-15 was approved, unread, 12 min ago').click();
    expect(navigate).toHaveBeenLastCalledWith(['/quotes', 'q-15'], {});
    httpTesting
      .expectOne({ method: 'POST', url: `${API}/notifications/n-1/read` })
      .flush(null, serverError);
    await settle();
    expect(text()).toContain("Couldn't update the notification. Try again.");
    expect(row('Quote Q-15 was approved, unread, 12 min ago')).not.toBeNull();

    // Menu: 404 reloads the list and the summary; other errors restore the state.
    const menuItem = await openMenu('Visit started');
    expect(menuItem.textContent?.trim()).toBe('Mark as unread');
    (menuItem.querySelector('a') ?? menuItem).click();
    await settle();
    httpTesting
      .expectOne({ method: 'POST', url: `${API}/notifications/n-2/unread` })
      .flush(null, { status: 404, statusText: 'Not Found' });
    await settle();
    expect(text()).toContain("This notification isn't available.");
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary(1, 3));
    list().flush(page([item({ isRead: false })]));
    await settle();

    // Mark all as read reloads the list, empties the counts and the bell, and disables the button.
    const markAll = control('Mark all as read')! as HTMLButtonElement;
    markAll.click();
    await settle();
    expect(markAll.disabled).toBe(true);
    httpTesting
      .expectOne({ method: 'POST', url: `${API}/notifications/read-all` })
      .flush({ updated: 1, summary: summary(0, 3) });
    await settle();
    list().flush(page([item({ isRead: true })]));
    await settle();
    expect(TestBed.inject(NotificationUnreadService).count()).toBe(0);
    expect((control('Mark all as read')! as HTMLButtonElement).disabled).toBe(true);
    expect(text()).toContain('Unread 0');
  });

  it('renders the error, summary-error, empty and forbidden states and stops requests after a 403 (FR-08, AC-18)', async () => {
    // Options failure: retry reloads.
    await setup('/notifications');
    httpTesting.expectOne(`${API}/notifications/options`).flush(null, serverError);
    await settle();
    expect(text()).toContain("Couldn't load notifications.");
    control('Retry')!.click();
    await settle();
    flushOptions(options(1));
    await settle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(null, serverError);
    list().flush(page([]));
    await settle();
    // The summary failing alone shows dashes; one branch hides the branch filter.
    expect(Array.from(document.querySelectorAll('dd'), (d) => d.textContent)).toEqual(['—', '—']);
    expect(text()).toContain("You're all caught up");
    expect(text()).toContain('New activity for you will appear here.');
    expect(document.querySelectorAll('.notif__field')).toHaveLength(3);
    TestBed.resetTestingModule();

    // A list failure keeps the page and retries only the list.
    await setup('/notifications');
    flushOptions();
    await settle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary());
    list().flush(null, serverError);
    await settle();
    expect(text()).toContain("Couldn't load notifications.");
    control('Retry')!.click();
    await settle();
    list().flush(page([item()]));
    TestBed.resetTestingModule();

    // 403: the forbidden state and no further requests.
    await setup('/notifications', 'technician');
    httpTesting
      .expectOne(`${API}/notifications/options`)
      .flush(null, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(text()).toContain("You don't have access to notifications.");
    httpTesting.expectNone((r) => r.url !== `${API}/notifications/options`);
  });

  it('shows the refresh notice only when a poll finds more unread and clears it on any list reload (AC-25)', async () => {
    await setup('/notifications');
    flushOptions();
    await settle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary());
    list().flush(page([item()]));
    await settle();
    expect(text()).not.toContain('New notifications available');

    const fakeSettle = async (): Promise<void> => {
      await vi.advanceTimersByTimeAsync(1);
      harness.detectChanges();
    };
    // Polls: the page last received unread 1 from the list; equal or lower shows nothing.
    vi.useFakeTimers({ now: NOW });
    const unread = TestBed.inject(NotificationUnreadService);
    const poll = async (value: number): Promise<void> => {
      await vi.advanceTimersByTimeAsync(30_000);
      httpTesting.expectOne(`${API}/notifications/summary`).flush(summary(value));
      await fakeSettle();
    };
    unread.startPolling();
    await poll(1);
    expect(text()).not.toContain('New notifications available');
    await poll(3);
    expect(text()).toContain('New notifications available');
    expect(document.querySelector('.notif__notice')?.getAttribute('aria-live')).toBe('polite');
    expect(url()).toBe('/notifications');

    // Refresh reloads list and summary and dismisses.
    control('Refresh')!.click();
    await fakeSettle();
    httpTesting.expectOne(`${API}/notifications/summary`).flush(summary(3));
    list().flush(page([item()]));
    await fakeSettle();
    expect(text()).not.toContain('New notifications available');

    // A filter change dismisses it too.
    await poll(9);
    expect(text()).toContain('New notifications available');
    control('Unread 1')!.click();
    await fakeSettle();
    list({ tab: 'unread' }).flush(page([]));
    await fakeSettle();
    expect(text()).not.toContain('New notifications available');
    unread.stopPolling();
  });
});
