import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_CONFIG } from '../config/api.config';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { MessageUnreadService } from './message-unread.service';

const API = 'http://api.test';
const SUMMARY = `${API}/messages/summary`;

describe('MessageUnreadService (visible poller)', () => {
  let httpTesting: HttpTestingController;
  let service: MessageUnreadService;
  let hidden = false;

  const setHidden = (value: boolean) => {
    hidden = value;
    document.dispatchEvent(new Event('visibilitychange'));
  };
  const tick = (ms: number) => vi.advanceTimersByTime(ms);
  const flushSummary = (unreadMessages: number) =>
    httpTesting.expectOne(SUMMARY).flush({ unreadMessages, unreadConversations: 1 });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    hidden = false;
    vi.spyOn(document, 'hidden', 'get').mockImplementation(() => hidden);
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    service = TestBed.inject(MessageUnreadService);
  });

  afterEach(() => {
    httpTesting.verify();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('polls every 30 s only while visible, refreshes on return, never overlaps, stays silent on failures, caps the badge and stops on 403 (technician-messages BR-07, BR-14, AC-14, AC-15)', () => {
    service.startPolling();
    service.startPolling();
    service.refresh();
    flushSummary(120);
    expect(service.badge()).toBe('99+');
    expect(service.label()).toBe('120 unread messages');

    tick(29_999);
    httpTesting.expectNone(SUMMARY);
    tick(1);
    // One request in flight: further ticks and refreshes add none.
    const pending = httpTesting.expectOne(SUMMARY);
    service.refresh();
    tick(60_000);
    httpTesting.expectNone(SUMMARY);
    pending.flush(null, { status: 500, statusText: 'Server Error' });
    expect(service.badge()).toBe('99+');

    tick(30_000);
    flushSummary(1);
    expect(service.badge()).toBe('1');
    expect(service.label()).toBe('1 unread message');

    setHidden(true);
    tick(120_000);
    httpTesting.expectNone(SUMMARY);
    setHidden(false);
    flushSummary(0);
    expect(service.badge()).toBeNull();
    expect(service.label()).toBeNull();

    tick(30_000);
    httpTesting.expectOne(SUMMARY).flush(null, { status: 403, statusText: 'Forbidden' });
    expect(service.unreadMessages()).toBe(0);
    tick(120_000);
    httpTesting.expectNone(SUMMARY);

    service.startPolling();
    flushSummary(4);
    service.reset();
    expect(service.badge()).toBeNull();
    tick(60_000);
    httpTesting.expectNone(SUMMARY);
  });
});
