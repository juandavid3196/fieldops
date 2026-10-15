import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { NotificationUnreadService } from './notification-unread.service';

const SUMMARY_URL = 'http://api.test/notifications/summary';

describe('NotificationUnreadService', () => {
  let service: NotificationUnreadService;
  let httpTesting: HttpTestingController;
  let now: number;

  beforeEach(() => {
    now = 1_000_000;
    vi.spyOn(Date, 'now').mockImplementation(() => now);
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: 'http://api.test' } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(NotificationUnreadService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    vi.restoreAllMocks();
  });

  it('reuses a load for 30 s, shares an in-flight request, keeps the value on failure and resets (AC-20)', () => {
    expect(service.count()).toBeNull();

    // In flight: a second refresh sends nothing.
    service.refresh();
    service.refresh();
    httpTesting.expectOne(SUMMARY_URL).flush({ unread: 7, today: 2 });
    expect(service.count()).toBe(7);

    // 10 s later: reused. 40 s later: one request.
    now += 10_000;
    service.refresh();
    httpTesting.expectNone(SUMMARY_URL);
    now += 30_000;
    service.refresh();
    httpTesting.expectOne(SUMMARY_URL).flush({ unread: 3, today: 1 });
    expect(service.count()).toBe(3);

    // A mark response sets the value with no request and restarts the window.
    service.set(2);
    now += 29_000;
    service.refresh();
    httpTesting.expectNone(SUMMARY_URL);
    expect(service.count()).toBe(2);

    // A failed load keeps the previous value and does not stamp the window.
    now += 2_000;
    service.refresh();
    httpTesting.expectOne(SUMMARY_URL).flush(null, { status: 500, statusText: 'Server Error' });
    expect(service.count()).toBe(2);
    service.refresh();
    httpTesting.expectOne(SUMMARY_URL).flush({ unread: 0, today: 0 });
    expect(service.count()).toBe(0);

    service.reset();
    expect(service.count()).toBeNull();
    service.refresh();
    httpTesting.expectOne(SUMMARY_URL).flush({ unread: 1, today: 0 });
  });

  it('polls every 30 s only while visible, retries after failures and stops on 403, reset and destroy (AC-20)', async () => {
    vi.restoreAllMocks();
    vi.useFakeTimers();
    const visibility = (hidden: boolean): void => {
      Object.defineProperty(document, 'hidden', { configurable: true, value: hidden });
      document.dispatchEvent(new Event('visibilitychange'));
    };
    const tick = (ms: number) => vi.advanceTimersByTimeAsync(ms);
    try {
      service.refresh();
      httpTesting.expectOne(SUMMARY_URL).flush({ unread: 1, today: 0 });
      service.startPolling();

      await tick(29_000);
      httpTesting.expectNone(SUMMARY_URL);
      await tick(1_000);
      // One request in flight: further ticks and refreshes add none.
      service.refresh();
      const first = httpTesting.expectOne(SUMMARY_URL);
      first.flush({ unread: 4, today: 0 });
      expect(service.count()).toBe(4);
      expect(service.polledSummary()).toEqual({ unread: 4 });

      // A mark response restarts the interval with no request and does not emit.
      await tick(20_000);
      service.set(3);
      await tick(29_000);
      httpTesting.expectNone(SUMMARY_URL);
      expect(service.polledSummary()).toEqual({ unread: 4 });

      // Hidden: no request. Visible again with a stale load: one request.
      visibility(true);
      await tick(120_000);
      httpTesting.expectNone(SUMMARY_URL);
      visibility(false);
      await tick(0);
      httpTesting.expectOne(SUMMARY_URL).flush({ unread: 5, today: 0 });

      // A failed tick keeps the value and the next tick retries.
      await tick(30_000);
      httpTesting.expectOne(SUMMARY_URL).flush(null, { status: 500, statusText: 'Server Error' });
      expect(service.count()).toBe(5);
      await tick(30_000);
      httpTesting.expectOne(SUMMARY_URL).flush({ unread: 6, today: 0 });

      // 403 stops polling; a restart then reset also stops it.
      await tick(30_000);
      httpTesting.expectOne(SUMMARY_URL).flush(null, { status: 403, statusText: 'Forbidden' });
      await tick(120_000);
      httpTesting.expectNone(SUMMARY_URL);
      service.startPolling();
      service.reset();
      await tick(120_000);
      httpTesting.expectNone(SUMMARY_URL);
      service.startPolling();
      TestBed.resetTestingModule();
      await tick(120_000);
      httpTesting.expectNone(SUMMARY_URL);
    } finally {
      Object.defineProperty(document, 'hidden', { configurable: true, value: false });
      vi.useRealTimers();
    }
  });
});
