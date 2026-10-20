import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_CONFIG } from '../../../../core/config/api.config';
import { errorInterceptor } from '../../../../core/interceptors/error.interceptor';
import { SessionService } from '../../../../core/services/session.service';
import { ConversationDetail, ConversationMessage } from '../../models/messages.model';
import { Conversation } from './conversation';

const API = 'http://api.test';
const BASE = `${API}/messages/conversations/c-1`;
const DAY = 86_400_000;

const DETAIL: ConversationDetail = {
  id: 'c-1',
  type: 'job',
  title: 'Kitchen sink leak repair',
  branchName: 'North',
  workOrder: {
    id: 'wo-1',
    displayNumber: 'WO-3091',
    title: 'Kitchen sink leak repair',
    visitId: 'v-1',
  },
};

function message(id: string, offsetMs: number, overrides: Partial<ConversationMessage> = {}) {
  return {
    id,
    clientMessageId: null,
    author: { name: 'Dana Lee', initials: 'DL', roleLabel: 'Dispatcher' },
    isMine: false,
    body: `Body ${id}`,
    createdAt: new Date(Date.now() - offsetMs).toISOString(),
    ...overrides,
  } satisfies ConversationMessage;
}

describe('Conversation', () => {
  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let host: HTMLElement;

  const detect = () => harness.fixture.detectChanges();
  const text = () => host.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const textarea = () => host.querySelector<HTMLTextAreaElement>('textarea')!;
  const button = (label: string) =>
    Array.from(host.querySelectorAll<HTMLButtonElement>('button')).find(
      (b) => b.textContent?.trim() === label,
    )!;
  const bubbles = () => Array.from(host.querySelectorAll('.msg'));
  const announcement = () => host.querySelector('.conv__sr')?.textContent?.trim();
  const type = (value: string) => {
    textarea().value = value;
    textarea().dispatchEvent(new Event('input'));
    detect();
  };
  const send = () => {
    host.querySelector('form')!.dispatchEvent(new Event('submit'));
    detect();
  };
  const post = () => httpTesting.expectOne({ method: 'POST', url: `${BASE}/messages` });
  const markRead = () => httpTesting.expectOne({ method: 'POST', url: `${BASE}/read` });
  const stored = (id: string, clientMessageId: string, body: string): ConversationMessage =>
    message(id, 0, {
      clientMessageId,
      isMine: true,
      body,
      author: { name: 'Carlos Rivera', initials: 'CR', roleLabel: 'Technician' },
    });

  beforeEach(async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    TestBed.configureTestingModule({
      providers: [
        { provide: API_CONFIG, useValue: { baseUrl: API } },
        provideRouter([
          {
            path: 'today/messages/:conversationId',
            component: Conversation,
            data: { messagesBase: '/today/messages' },
          },
        ]),
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    TestBed.inject(SessionService).setSession({
      user: {
        id: 'u-1',
        firstName: 'Carlos',
        lastName: 'Rivera',
        email: 'c@example.com',
        photoUpdatedAt: null,
      },
      organization: { id: 'o-1', name: 'Acme' },
      role: { code: 'technician', name: 'Technician' },
    });
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/today/messages/c-1');
    host = harness.routeNativeElement as HTMLElement;
  });

  afterEach(() => {
    httpTesting.verify();
    vi.useRealTimers();
  });

  it('renders the conversation, pages earlier messages, delivers with retry/discard/400 and polls newer messages (technician-messages AC-11, AC-12, AC-14)', () => {
    expect(host.querySelector('[aria-busy="true"]')).not.toBeNull();
    httpTesting.expectOne(BASE).flush(DETAIL);
    httpTesting
      .expectOne((req) => req.url === `${BASE}/messages` && req.params.keys().length === 0)
      .flush({
        items: [
          message('m-2', 2 * DAY, { body: 'Parts are ordered.' }),
          message('m-3', 60_000, {
            isMine: true,
            body: 'On my way.',
            author: { name: 'Carlos Rivera', initials: 'CR', roleLabel: 'Technician' },
          }),
        ],
        hasMore: true,
      });
    markRead().flush(null, { status: 204, statusText: 'No Content' });
    httpTesting
      .expectOne(`${API}/messages/summary`)
      .flush({ unreadMessages: 0, unreadConversations: 0 });
    detect();

    // Header, context strip, separators, alignment, author/role, time.
    expect(host.querySelector('h1')?.textContent).toBe('Kitchen sink leak repair');
    expect(host.querySelector('a[aria-label="Back to messages"]')?.getAttribute('href')).toBe(
      '/today/messages',
    );
    expect(text()).toContain('#WO-3091');
    expect(
      Array.from(host.querySelectorAll('.conv__context a'), (a) => [
        a.textContent,
        a.getAttribute('href'),
      ]),
    ).toEqual([['Open job', '/today/visits/v-1']]);
    expect(Array.from(host.querySelectorAll('.conv__day'), (d) => d.textContent)).toEqual([
      new Date(Date.now() - 2 * DAY).toLocaleDateString(undefined, {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
      }),
      'Today',
    ]);
    expect(bubbles().map((b) => b.classList.contains('msg--mine'))).toEqual([false, true]);
    expect(bubbles()[0].textContent).toContain('Dana Lee');
    expect(bubbles()[0].textContent).toContain('Dispatcher');
    expect(host.querySelector('[role="log"]')).not.toBeNull();
    expect(host.querySelector('label[for="conversation-message"]')?.textContent).toBe('Message');
    expect(button('Send').disabled).toBe(true);

    // Load earlier messages: before = oldest loaded id; older messages prepend.
    button('Load earlier messages').click();
    httpTesting
      .expectOne((req) => req.url === `${BASE}/messages` && req.params.get('before') === 'm-2')
      .flush({ items: [message('m-1', 3 * DAY, { body: 'First note.' })], hasMore: false });
    detect();
    expect(bubbles()[0].textContent).toContain('First note.');
    expect(host.textContent).not.toContain('Load earlier messages');

    // Counter from 1800 characters; over the limit disables Send.
    type('x'.repeat(1800));
    expect(host.querySelector('.conv__counter')?.textContent?.trim()).toBe('1800/2000');
    expect(button('Send').disabled).toBe(false);
    type('x'.repeat(2001));
    expect(button('Send').disabled).toBe(true);

    // Sending -> Failed -> Retry with the same clientMessageId -> Sent, no duplicate.
    type('Arrived');
    send();
    expect(textarea().value).toBe('');
    expect(text()).toContain('Sending…');
    expect(announcement()).toBe('Sending message.');
    const first = post();
    const clientMessageId = (first.request.body as { clientMessageId: string }).clientMessageId;
    expect(first.request.body).toEqual({ clientMessageId, body: 'Arrived' });
    expect(clientMessageId).toMatch(/^[0-9a-f-]{36}$/);
    first.flush(null, { status: 500, statusText: 'Server Error' });
    detect();
    expect(text()).toContain('Failed');
    expect(announcement()).toBe('Message failed to send.');
    button('Retry').click();
    detect();
    expect(text()).toContain('Sending…');
    const retried = post();
    expect(retried.request.body).toEqual({ clientMessageId, body: 'Arrived' });
    retried.flush(stored('m-4', clientMessageId, 'Arrived'), {
      status: 201,
      statusText: 'Created',
    });
    detect();
    expect(text()).toContain('Sent');
    expect(announcement()).toBe('Message sent.');
    expect(bubbles().filter((b) => b.textContent?.includes('Arrived'))).toHaveLength(1);

    // Rate limit shows its message as Failed; Discard removes it.
    type('Again');
    send();
    post().flush(null, {
      status: 429,
      statusText: 'Too Many Requests',
      headers: { 'Retry-After': '5' },
    });
    detect();
    expect(text()).toContain("You're sending messages too quickly. Try again in a moment.");
    button('Discard').click();
    detect();
    expect(text()).not.toContain('Again');

    // 400 keeps the text in the composer with the field message.
    type('   ');
    expect(button('Send').disabled).toBe(true);
    type('Rejected');
    send();
    post().flush(null, { status: 400, statusText: 'Bad Request' });
    detect();
    expect(textarea().value).toBe('Rejected');
    expect(host.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
      'Enter a message of up to 2000 characters.',
    );
    expect(bubbles().filter((b) => b.textContent?.includes('Rejected'))).toHaveLength(0);

    // Enter sends; Shift+Enter, IME composition keep the text; empty Enter does nothing and no newline.
    const press = (init: KeyboardEventInit) => {
      const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, ...init });
      textarea().dispatchEvent(event);
      detect();
      return event;
    };
    type('   ');
    expect(press({}).defaultPrevented).toBe(true);
    type('Line');
    expect(press({ shiftKey: true }).defaultPrevented).toBe(false);
    expect(press({ isComposing: true }).defaultPrevented).toBe(false);
    httpTesting.expectNone({ method: 'POST', url: `${BASE}/messages` });
    expect(press({}).defaultPrevented).toBe(true);
    expect(textarea().value).toBe('');
    post().flush(null, { status: 500, statusText: 'Server Error' });
    detect();
    button('Discard').click();
    detect();
    type('Ctrl');
    press({ ctrlKey: true });
    post().flush(null, { status: 500, statusText: 'Server Error' });
    detect();
    button('Discard').click();
    detect();

    // Polling: after = newest stored id; new message is announced, read is marked, summary refreshed.
    vi.advanceTimersByTime(30_000);
    httpTesting
      .expectOne((req) => req.url === `${BASE}/messages` && req.params.get('after') === 'm-4')
      .flush({ items: [message('m-5', 0, { body: 'Thanks!' })], hasMore: false });
    detect();
    expect(text()).toContain('Thanks!');
    expect(announcement()).toBe('New message from Dana Lee');
    // Not marked read until the user interacts with the pane (pointerdown), then once.
    httpTesting.expectNone(`${BASE}/read`);
    host.querySelector('.conv')!.dispatchEvent(new Event('pointerdown'));
    host.querySelector('.conv')!.dispatchEvent(new Event('pointerdown'));
    markRead().flush(null, { status: 204, statusText: 'No Content' });
    detect();
    httpTesting
      .expectOne(`${API}/messages/summary`)
      .flush({ unreadMessages: 0, unreadConversations: 0 });

    // A polling failure is silent; the next tick retries.
    vi.advanceTimersByTime(30_000);
    httpTesting
      .expectOne((req) => req.params.get('after') === 'm-5')
      .flush(null, { status: 500, statusText: 'Server Error' });
    detect();
    expect(text()).toContain('Thanks!');
    expect(host.querySelector('p-message')).toBeNull();
    vi.advanceTimersByTime(30_000);
    httpTesting
      .expectOne((req) => req.params.get('after') === 'm-5')
      .flush({ items: [], hasMore: false });
  });

  it.each([
    [404, "This conversation isn't available.", 'Back to messages'],
    [500, "We couldn't load this conversation. Try again.", 'Retry'],
  ])(
    'shows the %s load state with its action (technician-messages BR-12)',
    (status, expected, action) => {
      httpTesting.expectOne(BASE).flush(null, { status, statusText: 'Error' });
      httpTesting.match(`${BASE}/messages`);
      detect();
      expect(text()).toContain(expected);
      expect(text()).toContain(action);
    },
  );
});
