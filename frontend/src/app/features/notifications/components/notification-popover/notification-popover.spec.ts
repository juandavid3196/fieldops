import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { NotificationUnreadService } from '../../../../core/services/notification-unread.service';
import { NotificationItem, NotificationsPage } from '../../models/notifications.model';
import { NotificationPopover } from './notification-popover';

const API = 'http://api.test';
const LIST = `${API}/notifications`;

@Component({
  imports: [NotificationPopover],
  template: `
    <button #bell type="button" id="bell" (click)="pop.toggle($event)">Bell</button>
    <app-notification-popover #pop [trigger]="bell" />
  `,
})
class Host {
  readonly popover = viewChild.required(NotificationPopover);
}

@Component({ template: '' })
class Stub {}

const item = (id: string, patch: Partial<NotificationItem> = {}): NotificationItem => ({
  id,
  type: 'quote',
  eventCode: 'quote.approved',
  title: `Quote ${id} was approved`,
  description: 'Camilo approved the quote.',
  entityKind: 'quote',
  entityLabel: id,
  secondaryLabel: null,
  branch: null,
  createdAt: new Date(Date.now() - 12 * 60_000).toISOString(),
  isRead: false,
  target: { kind: 'quote', id: `q-${id}` },
  ...patch,
});

const page = (items: NotificationItem[]): NotificationsPage => ({
  items,
  total: items.length,
  page: 1,
  pageSize: 5,
  counts: { all: items.length, unread: items.filter((row) => !row.isRead).length },
  timeZone: 'UTC',
});

describe('NotificationPopover', () => {
  let fixture: ComponentFixture<Host>;
  let http: HttpTestingController;
  let router: Router;
  let bell: HTMLButtonElement;

  const settle = async (): Promise<void> => {
    await fixture.whenStable();
    if (vi.isFakeTimers()) {
      await vi.advanceTimersByTimeAsync(50);
    } else {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    await fixture.whenStable();
  };
  const panel = (): HTMLElement | null => document.querySelector('[role="dialog"]');
  const rows = (): HTMLButtonElement[] =>
    Array.from(document.querySelectorAll<HTMLButtonElement>('[data-popover-row]'));
  const isListRequest = (req: { url: string; method: string }): boolean =>
    req.method === 'GET' && req.url === LIST;

  async function open(items: NotificationItem[] | 'error' = [item('a')]): Promise<void> {
    bell.click();
    fixture.detectChanges();
    const request = http.expectOne(isListRequest);
    if (items === 'error') {
      request.flush({ title: 'x' }, { status: 500, statusText: 'Server Error' });
    } else {
      request.flush(page(items));
    }
    await settle();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: '**', component: Stub }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(Host);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    bell = fixture.nativeElement.querySelector('#bell');
  });

  afterEach(() => {
    fixture.destroy();
    http.verify();
    document.body.querySelectorAll('.p-popover').forEach((node) => node.remove());
  });

  it('opens with one five-row request, a named dialog, first-row focus and a reload on every open (BR-26, AC-19, AC-26)', async () => {
    bell.click();
    fixture.detectChanges();
    const request = http.expectOne(isListRequest);
    expect(request.request.params.get('tab')).toBe('all');
    expect(request.request.params.get('date')).toBe('any');
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('5');
    request.flush(page([item('a'), item('b', { isRead: true })]));
    await settle();

    expect(panel()?.getAttribute('aria-label')).toBe('Notifications');
    expect(rows().map((row) => row.getAttribute('aria-label'))).toEqual([
      'Quote a was approved, unread, 12 min ago',
      'Quote b was approved, read, 12 min ago',
    ]);
    expect(document.activeElement).toBe(rows()[0]);
    expect(fixture.componentInstance.popover().open()).toBe(true);

    bell.click();
    await settle();
    expect(panel()).toBeNull();
    await open([]);
    expect(document.body.textContent).toContain("You're all caught up.");
    // Unread count unknown: the header action is enabled and receives focus.
    expect(document.activeElement?.textContent?.trim()).toBe('Mark all as read');
  });

  it('shows the error state with Retry and closes on 403 (BR-26, AC-26)', async () => {
    await open('error');
    expect(document.body.textContent).toContain("Couldn't load notifications.");
    const retry = Array.from(document.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Retry',
    )!;
    retry.click();
    http.expectOne(isListRequest).flush({}, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(panel()).toBeNull();
  });

  it('updates the bell from mark responses without a summary request or polling (BR-16, BR-26, AC-26)', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const unread = TestBed.inject(NotificationUnreadService);
    try {
      unread.set(2);
      unread.startPolling();
      await open([item('a'), item('b')]);

      // Selecting an unread row marks it read; the response feeds the bell.
      rows()[0].click();
      const read = http.expectOne({ method: 'POST', url: `${LIST}/a/read` });
      read.flush({ summary: { unread: 1, today: 0 } });
      await vi.advanceTimersByTimeAsync(0);
      expect(unread.count()).toBe(1);

      await vi.advanceTimersByTimeAsync(29_000);
      http.expectNone(`${LIST}/summary`);
      unread.stopPolling();
    } finally {
      vi.useRealTimers();
    }
  });

  it('marks all read and reloads the list; Escape, outside click and NavigationEnd return focus to the bell, row navigation does not (BR-26, AC-26)', async () => {
    const unread = TestBed.inject(NotificationUnreadService);
    unread.set(2);
    await open([item('a'), item('b')]);

    const markAll = Array.from(document.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Mark all as read',
    )!;
    markAll.click();
    http
      .expectOne({ method: 'POST', url: `${LIST}/read-all` })
      .flush({ updated: 2, summary: { unread: 0, today: 0 } });
    expect(unread.count()).toBe(0);
    http.expectOne(isListRequest).flush(page([item('a', { isRead: true })]));
    http.expectNone(`${LIST}/summary`);
    await settle();
    expect(markAll.disabled).toBe(true);

    for (const close of [
      () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })),
      () => document.body.click(),
      () => void router.navigateByUrl('/somewhere'),
    ]) {
      bell.blur();
      close();
      await settle();
      expect(panel()).toBeNull();
      expect(document.activeElement).toBe(bell);
      await open([item('a', { isRead: true })]);
    }

    // Selecting a read row navigates, closes and leaves focus to the destination.
    bell.blur();
    rows()[0].click();
    await settle();
    expect(panel()).toBeNull();
    expect(router.url).toBe('/quotes/q-a');
    expect(document.activeElement).not.toBe(bell);
  });
});
