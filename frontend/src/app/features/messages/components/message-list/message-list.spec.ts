import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { MessageUnreadService } from '../../../../core/services/message-unread.service';
import { ConversationSummary } from '../../models/messages.model';
import { MessageList } from './message-list';

const API = 'http://api.test';
const LIST = `${API}/messages/conversations`;

@Component({ template: '' })
class Stub {}

function summary(overrides: Partial<ConversationSummary>): ConversationSummary {
  return {
    id: 'c-1',
    type: 'job',
    title: 'Kitchen sink leak repair',
    branchId: 'b-1',
    workOrder: { id: 'wo-1', displayNumber: 'WO-3091' },
    lastMessage: {
      preview: 'The customer confirmed access.',
      authorName: 'Dana Lee',
      authorInitials: 'DL',
      isMine: false,
      createdAt: new Date(Date.now() - 2 * 60_000).toISOString(),
    },
    unreadCount: 3,
    ...overrides,
  };
}

describe('MessageList', () => {
  let fixture: ComponentFixture<MessageList>;
  let httpTesting: HttpTestingController;
  let host: HTMLElement;

  const detect = () => fixture.detectChanges();
  const request = (filter: string, search?: string) =>
    httpTesting.expectOne(
      (req) =>
        req.method === 'GET' &&
        req.url === LIST &&
        req.params.get('filter') === filter &&
        req.params.get('search') === (search ?? null),
    );
  const chip = (label: string) =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('.list__chip')).find((b) =>
      b.textContent?.includes(label),
    )!;
  const text = () => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(() => {
    // Pin the clock to midday so relative labels ("3h") never cross midnight.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date(2026, 0, 15, 12, 0, 0));
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([{ path: '**', component: Stub }]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(MessageUnreadService).set({ unreadMessages: 120, unreadConversations: 2 });
    fixture = TestBed.createComponent(MessageList);
    fixture.componentRef.setInput('basePath', '/today/messages');
    host = fixture.nativeElement as HTMLElement;
    detect();
  });

  afterEach(() => {
    httpTesting.verify();
    vi.useRealTimers();
  });

  it('renders the BR-16 list, filters, debounced search, per-filter empty states, errors with Retry and opens rows (technician-messages AC-10)', () => {
    // Loading, then rows: link rows, previews, chips, unread text equivalents.
    expect(host.querySelector('[aria-busy="true"]')).not.toBeNull();
    request('all').flush({
      items: [
        summary({}),
        summary({
          id: 'c-2',
          title: 'Bathroom remodel',
          workOrder: { id: 'wo-2', displayNumber: 'WO-3104' },
          lastMessage: {
            preview: 'I left the valve in truck 12.',
            authorName: 'Me',
            authorInitials: 'ME',
            isMine: true,
            createdAt: new Date(Date.now() - 3 * 3_600_000).toISOString(),
          },
          unreadCount: 0,
        }),
        summary({
          id: null,
          type: 'team',
          title: 'North team',
          workOrder: null,
          lastMessage: null,
          unreadCount: 0,
        }),
      ],
      nextCursor: null,
    });
    detect();
    expect(host.querySelector('h1')?.textContent).toBe('Messages');
    expect(text()).toContain('Job and team conversations');
    expect(text()).toContain('Messages stay linked to the related job or team conversation.');
    expect(host.querySelector('input')?.getAttribute('placeholder')).toBe('Search messages');
    const rows = Array.from(host.querySelectorAll('.row'));
    expect(rows).toHaveLength(3);
    expect(rows[0].tagName).toBe('A');
    expect(rows[0].getAttribute('href')).toBe('/today/messages/c-1');
    expect(rows[0].textContent).toContain('Dana: The customer confirmed access.');
    expect(rows[0].textContent).toContain('2 min');
    expect(rows[0].textContent).toContain('#WO-3091');
    expect(rows[0].textContent).toContain('3 unread messages');
    expect(rows[1].textContent).toContain('You: I left the valve in truck 12.');
    expect(rows[1].textContent).toContain('3h');
    expect(rows[2].textContent).toContain('No messages yet');
    expect(rows[2].textContent).toContain('Team');
    expect(rows[2].querySelector('.pi-building')).not.toBeNull();
    // The Unread chip shows the shared count ("99+" capped) with a text equivalent.
    expect(chip('Unread').textContent).toContain('99+');
    expect(chip('Unread').textContent).toContain('120 unread messages');
    expect(chip('All').getAttribute('aria-pressed')).toBe('true');

    // Single-select filters; each reloads the first page.
    chip('Unread').click();
    detect();
    expect(chip('Unread').getAttribute('aria-pressed')).toBe('true');
    expect(chip('All').getAttribute('aria-pressed')).toBe('false');
    request('unread').flush({ items: [], nextCursor: null });
    detect();
    expect(text()).toContain('No unread messages.');
    chip('Jobs').click();
    request('jobs').flush({ items: [], nextCursor: null });
    detect();
    expect(text()).toContain('No job conversations yet.');

    // Search: debounced 300 ms, applied from 2 characters only.
    const input = host.querySelector<HTMLInputElement>('input')!;
    const type = (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    type('a');
    vi.advanceTimersByTime(300);
    httpTesting.expectNone(LIST);
    type('ab');
    vi.advanceTimersByTime(299);
    httpTesting.expectNone(LIST);
    vi.advanceTimersByTime(1);
    request('jobs', 'ab').flush({ items: [], nextCursor: null });
    detect();
    expect(text()).toContain('No results for "ab".');

    // Error with Retry.
    chip('All').click();
    request('all', 'ab').flush(null, { status: 500, statusText: 'Server Error' });
    detect();
    expect(text()).toContain("We couldn't load your messages. Try again.");
    host.querySelector<HTMLButtonElement>('p-message button')!.click();
    request('all', 'ab').flush({ items: [summary({})], nextCursor: null });
    detect();
    expect(host.querySelectorAll('.row')).toHaveLength(1);
  });

  it('resolves a virtual Team entry before opening it and reports a failure (BR-04, BR-11)', async () => {
    request('all').flush({
      items: [summary({ id: null, type: 'team', workOrder: null, lastMessage: null })],
      nextCursor: null,
    });
    detect();
    const row = host.querySelector<HTMLButtonElement>('button.row')!;
    row.click();
    const first = httpTesting.expectOne({ method: 'POST', url: `${LIST}/resolve` });
    expect(first.request.body).toEqual({ type: 'team', branchId: 'b-1' });
    first.flush(null, { status: 500, statusText: 'Server Error' });
    detect();
    expect(text()).toContain("We couldn't open the conversation. Try again.");

    row.click();
    httpTesting
      .expectOne({ method: 'POST', url: `${LIST}/resolve` })
      .flush({ conversationId: 'c-team' });
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/today/messages/c-team');
  });
});
