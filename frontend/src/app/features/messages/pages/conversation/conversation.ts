import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Textarea } from 'primeng/textarea';
import { EMPTY, Observable, Subscription, forkJoin, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { watchMedia, LG_QUERY } from '../../../../core/config/breakpoints';
import { MessageUnreadService } from '../../../../core/services/message-unread.service';
import { SessionService } from '../../../../core/services/session.service';
import { createVisiblePoller } from '../../../../core/services/visible-poller';
import { fullNameOf, initialsOf } from '../../../../shared/utils/initials';
import {
  INACTIVE_MESSAGE,
  NOT_LINKED_MESSAGE,
  classifyFailure,
} from '../../../technician-jobs/utils/technician-errors';
import { ConversationDetail, ConversationMessage } from '../../models/messages.model';
import { MessagesService } from '../../services/messages.service';
import {
  COUNTER_FROM,
  CONVERSATION_ERROR_MESSAGE,
  FORBIDDEN_MESSAGE,
  MAX_BODY_LENGTH,
  MESSAGING_ROLES,
  NOT_AVAILABLE_MESSAGE,
  dayKey,
  dayLabel,
  sendFailure,
  timeLabel,
} from '../../utils/messages-format';

import { t } from '../../../../core/i18n/translate';
type PageState =
  'loading' | 'ready' | 'error' | 'not-found' | 'forbidden' | 'inactive' | 'not-linked';
type DeliveryStatus = 'sent' | 'sending' | 'failed';

interface Bubble {
  /** The server id, or the client id until the message is stored. */
  readonly key: string;
  readonly id: string | null;
  readonly clientMessageId: string | null;
  readonly name: string;
  readonly initials: string;
  readonly roleLabel: string;
  readonly isMine: boolean;
  readonly body: string;
  readonly createdAt: string;
  readonly status: DeliveryStatus;
  /** Sent from this screen: shows its delivery state (BR-13). */
  readonly local: boolean;
  readonly failure: string | null;
}

type Entry =
  | { readonly kind: 'day'; readonly key: string; readonly label: string }
  | {
      readonly kind: 'message';
      readonly key: string;
      readonly bubble: Bubble;
      readonly time: string;
    };

const BOTTOM_TOLERANCE_PX = 48;

function fromServer(message: ConversationMessage, local: boolean): Bubble {
  return {
    key: message.id,
    id: message.id,
    clientMessageId: message.clientMessageId,
    name: message.author.name,
    initials: message.author.initials,
    roleLabel: message.author.roleLabel,
    isMine: message.isMine,
    body: message.body,
    createdAt: message.createdAt,
    status: 'sent',
    local,
    failure: null,
  };
}

/**
 * Conversation screen (BR-12, BR-13): message log with day separators, earlier pages, composer with
 * Sending / Sent / Failed delivery states, and a 30 s poll for newer messages while visible.
 */
@Component({
  selector: 'app-conversation',
  imports: [TranslocoPipe, ButtonDirective, Message, RouterLink, Skeleton, Textarea],
  host: { '(document:keydown.escape)': 'onEscape($event)' },
  templateUrl: './conversation.html',
  styleUrl: './conversation.scss',
})
export class Conversation {
  private readonly messages = inject(MessagesService);
  private readonly unread = inject(MessageUnreadService);
  private readonly session = inject(SessionService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly log = viewChild<ElementRef<HTMLElement>>('log');
  private readonly poller = createVisiblePoller(inject(DOCUMENT), this.destroyRef, () =>
    this.poll(),
  );
  private conversationId = '';
  private opening: Subscription | null = null;

  /** Route data `messagesBase` / `split` (set by the messages routes). */
  readonly basePath =
    typeof this.route.snapshot.data['messagesBase'] === 'string'
      ? this.route.snapshot.data['messagesBase']
      : '/messages';
  readonly split = this.route.snapshot.data['split'] === true;
  private readonly wide = signal(true);

  readonly maxLength = MAX_BODY_LENGTH;
  get errorMessage(): string {
    return CONVERSATION_ERROR_MESSAGE();
  }
  get notAvailableMessage(): string {
    return NOT_AVAILABLE_MESSAGE();
  }
  readonly skeletons = [0, 1, 2];

  readonly state = signal<PageState>('loading');
  readonly detail = signal<ConversationDetail | null>(null);
  readonly bubbles = signal<readonly Bubble[]>([]);
  readonly hasMore = signal(false);
  readonly loadingEarlier = signal(false);
  readonly draft = signal('');
  readonly fieldError = signal<string | null>(null);
  readonly announcement = signal('');
  readonly newMessages = signal(false);
  private unreadIncoming = false;
  private reading = false;

  readonly showBack = computed(() => !(this.split && this.wide()));
  readonly isTechnician = this.session.session()?.role.code === 'technician';
  readonly accessText = computed(() => {
    switch (this.state()) {
      case 'forbidden':
        return FORBIDDEN_MESSAGE();
      case 'inactive':
        return INACTIVE_MESSAGE();
      case 'not-linked':
        return NOT_LINKED_MESSAGE();
      default:
        return null;
    }
  });
  readonly openJob = computed(() => {
    const order = this.detail()?.workOrder ?? null;
    if (order === null) {
      return null;
    }
    if (this.isTechnician) {
      return order.visitId === null ? null : `/today/visits/${encodeURIComponent(order.visitId)}`;
    }
    return `/jobs/${encodeURIComponent(order.id)}`;
  });
  readonly entries = computed<Entry[]>(() => {
    const now = new Date();
    const result: Entry[] = [];
    let day = '';
    for (const bubble of this.bubbles()) {
      const key = dayKey(bubble.createdAt);
      if (key !== day) {
        day = key;
        result.push({ kind: 'day', key: `day-${key}`, label: dayLabel(bubble.createdAt, now) });
      }
      result.push({
        kind: 'message',
        key: bubble.key,
        bubble,
        time: timeLabel(bubble.createdAt),
      });
    }
    return result;
  });
  readonly counter = computed(() => {
    const length = this.draft().length;
    return length >= COUNTER_FROM ? `${length}/${MAX_BODY_LENGTH}` : null;
  });
  readonly overLimit = computed(() => this.draft().length > MAX_BODY_LENGTH);
  readonly canSend = computed(() => this.draft().trim() !== '' && !this.overLimit());

  constructor() {
    if (this.split) {
      const query = watchMedia(LG_QUERY, (matches) => this.wide.set(matches));
      this.wide.set(query.matches);
      this.destroyRef.onDestroy(query.stop);
    }
    this.destroyRef.onDestroy(() => this.opening?.unsubscribe());
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.open(params.get('conversationId') ?? ''));
  }

  /** Initial load and Retry: detail and the newest page, then mark read (BR-10). */
  open(id: string = this.conversationId): void {
    this.opening?.unsubscribe();
    this.poller.stop();
    this.unreadIncoming = false;
    if (id !== this.conversationId) {
      this.draft.set('');
      this.fieldError.set(null);
    }
    this.conversationId = id;
    this.bubbles.set([]);
    this.detail.set(null);
    this.hasMore.set(false);
    this.newMessages.set(false);
    const role = this.session.session()?.role.code ?? '';
    if (!MESSAGING_ROLES.includes(role)) {
      this.state.set('forbidden');
      return;
    }
    this.state.set('loading');
    this.opening = forkJoin({
      detail: this.messages.detail(id),
      page: this.messages.messages(id),
    }).subscribe({
      next: ({ detail, page }) => {
        this.detail.set(detail);
        this.bubbles.set(page.items.map((message) => fromServer(message, false)));
        this.hasMore.set(page.hasMore);
        this.state.set('ready');
        this.scrollToBottom();
        this.markRead();
        this.poller.start();
      },
      error: (error: unknown) => {
        const failure = classifyFailure(error);
        this.state.set(failure === 'failed' ? 'error' : failure);
      },
    });
  }

  loadEarlier(): void {
    const oldest = this.bubbles().find((bubble) => bubble.id !== null)?.id;
    if (oldest === undefined || oldest === null || this.loadingEarlier()) {
      return;
    }
    const element = this.log()?.nativeElement;
    const height = element?.scrollHeight ?? 0;
    const top = element?.scrollTop ?? 0;
    this.loadingEarlier.set(true);
    this.messages.messages(this.conversationId, { before: oldest }).subscribe({
      next: (page) => {
        const known = new Set(this.bubbles().map((bubble) => bubble.id));
        const older = page.items
          .filter((message) => !known.has(message.id))
          .map((message) => fromServer(message, false));
        this.bubbles.update((bubbles) => [...older, ...bubbles]);
        this.hasMore.set(page.hasMore);
        this.loadingEarlier.set(false);
        afterNextRender(
          () => {
            if (element !== undefined) {
              element.scrollTop = top + (element.scrollHeight - height);
            }
          },
          { injector: this.injector },
        );
      },
      error: () => this.loadingEarlier.set(false),
    });
  }

  /** True when the message at `index` follows one from another author or the other side. */
  startsGroup(index: number): boolean {
    const entries = this.entries();
    const previous = entries[index - 1];
    const current = entries[index];
    if (previous?.kind !== 'message' || current?.kind !== 'message') {
      return false;
    }
    return (
      previous.bubble.isMine !== current.bubble.isMine ||
      (!current.bubble.isMine && previous.bubble.name !== current.bubble.name)
    );
  }

  onDraft(value: string): void {
    this.draft.set(value);
    this.fieldError.set(null);
  }

  /** Esc closes the conversation (back to the list) unless the key belongs to something else. */
  onEscape(event: Event): void {
    const key = event as KeyboardEvent;
    if (
      key.defaultPrevented ||
      key.isComposing ||
      key.ctrlKey ||
      key.metaKey ||
      key.altKey ||
      key.shiftKey ||
      this.document.querySelector('[role="dialog"], [role="menu"], [role="listbox"], .p-overlay')
    ) {
      return;
    }
    const selected = this.document.querySelector<HTMLElement>('a.row--selected');
    const href = selected?.getAttribute('href') ?? null;
    void this.router.navigateByUrl(this.basePath).then(() => {
      setTimeout(() => {
        const row =
          href === null ? null : this.document.querySelector<HTMLElement>(`a.row[href="${href}"]`);
        (row ?? this.document.querySelector<HTMLElement>('#messages-search'))?.focus();
      });
    });
  }

  /** Enter alone sends (Shift+Enter keeps the newline); ignored while an IME composes. */
  onEnter(event: Event): void {
    if ((event as KeyboardEvent).isComposing) {
      return;
    }
    event.preventDefault();
    this.send();
  }

  /** Enter, Ctrl/Cmd+Enter or the Send button. */
  send(): void {
    if (!this.canSend() || this.state() !== 'ready') {
      return;
    }
    const body = this.draft().trim();
    const user = this.session.session()?.user;
    const name = fullNameOf(user);
    const clientMessageId = crypto.randomUUID();
    this.bubbles.update((bubbles) => [
      ...bubbles,
      {
        key: clientMessageId,
        id: null,
        clientMessageId,
        name,
        initials: initialsOf(name),
        roleLabel: '',
        isMine: true,
        body,
        createdAt: new Date().toISOString(),
        status: 'sending',
        local: true,
        failure: null,
      },
    ]);
    this.draft.set('');
    this.fieldError.set(null);
    this.announcement.set(t('messages.conversation.sendingMessage'));
    this.scrollToBottom();
    this.dispatch(clientMessageId, body);
  }

  /** Resends the same `clientMessageId` (BR-13). */
  retry(bubble: Bubble): void {
    if (bubble.status !== 'failed' || bubble.clientMessageId === null) {
      return;
    }
    this.patch(bubble.key, { status: 'sending', failure: null });
    this.announcement.set(t('messages.conversation.sendingMessage'));
    this.dispatch(bubble.clientMessageId, bubble.body);
  }

  discard(bubble: Bubble): void {
    this.bubbles.update((bubbles) => bubbles.filter((item) => item.key !== bubble.key));
    this.announcement.set(t('messages.conversation.messageDiscarded'));
  }

  showNewMessages(): void {
    this.onInteract();
    this.newMessages.set(false);
    this.scrollToBottom();
  }

  onScroll(): void {
    if (this.atBottom()) {
      if (this.newMessages()) {
        this.onInteract();
      }
      this.newMessages.set(false);
    }
  }

  private dispatch(clientMessageId: string, body: string): void {
    this.messages.send(this.conversationId, { clientMessageId, body }).subscribe({
      next: (message) => {
        const stored = fromServer(message, true);
        this.bubbles.update((bubbles) =>
          // A poll may have stored the message already: keep that one, drop the pending bubble.
          bubbles.some((item) => item.id === message.id)
            ? bubbles.filter((item) => item.key !== clientMessageId)
            : bubbles.map((item) => (item.key === clientMessageId ? stored : item)),
        );
        this.announcement.set(t('messages.conversation.messageSent'));
      },
      error: (error: unknown) => {
        const failure = sendFailure(error);
        if (failure.bodyRejected) {
          this.bubbles.update((bubbles) => bubbles.filter((item) => item.key !== clientMessageId));
          this.draft.update((current) => (current === '' ? body : `${body}\n${current}`));
          this.fieldError.set(failure.message);
          this.announcement.set(failure.message);
          return;
        }
        this.patch(clientMessageId, { status: 'failed', failure: failure.message });
        this.announcement.set(t('messages.conversation.messageFailedToSend'));
      },
    });
  }

  private patch(key: string, changes: Partial<Bubble>): void {
    this.bubbles.update((bubbles) =>
      bubbles.map((item) => (item.key === key ? { ...item, ...changes } : item)),
    );
  }

  /** BR-14: newer than the newest stored message; failures are silent. */
  private poll(): Observable<unknown> {
    if (this.state() !== 'ready') {
      return EMPTY;
    }
    const stored = this.bubbles().filter((bubble) => bubble.id !== null);
    const newest = stored.length === 0 ? undefined : (stored[stored.length - 1].id ?? undefined);
    return this.messages
      .messages(this.conversationId, newest === undefined ? {} : { after: newest })
      .pipe(tap((page) => this.merge(page.items)));
  }

  private merge(incoming: readonly ConversationMessage[]): void {
    const current = this.bubbles();
    const ids = new Set(current.map((bubble) => bubble.id));
    const fresh = incoming.filter((message) => !ids.has(message.id));
    if (fresh.length === 0) {
      return;
    }
    const stickToBottom = this.atBottom();
    let next = [...current];
    for (const message of fresh) {
      const index =
        message.clientMessageId === null
          ? -1
          : next.findIndex((bubble) => bubble.clientMessageId === message.clientMessageId);
      if (index >= 0) {
        next[index] = fromServer(message, true);
      } else {
        next = [...next, fromServer(message, false)];
      }
    }
    this.bubbles.set(next);
    const others = fresh.filter((message) => !message.isMine);
    if (others.length > 0) {
      this.announcement.set(
        t('messages.conversation.newMessageFrom', { value: others[others.length - 1].author.name }),
      );
      this.unreadIncoming = true;
      if (stickToBottom) {
        this.scrollToBottom();
      } else {
        this.newMessages.set(true);
      }
    }
  }

  /** Click or focus inside the pane: marks incoming messages read once per burst. */
  onInteract(): void {
    if (this.unreadIncoming && !this.reading && this.state() === 'ready') {
      this.markRead();
    }
  }

  private markRead(): void {
    this.unreadIncoming = false;
    this.reading = true;
    this.unread.markConversationRead(this.conversationId);
    this.messages.markRead(this.conversationId).subscribe({
      next: () => {
        this.reading = false;
        this.unread.refresh();
      },
      error: () => {
        this.reading = false;
      },
    });
  }

  private atBottom(): boolean {
    const element = this.log()?.nativeElement;
    return (
      element === undefined ||
      element.scrollHeight - element.scrollTop - element.clientHeight <= BOTTOM_TOLERANCE_PX
    );
  }

  private scrollToBottom(): void {
    afterNextRender(
      () => {
        const element = this.log()?.nativeElement;
        if (element !== undefined) {
          element.scrollTop = element.scrollHeight;
        }
      },
      { injector: this.injector },
    );
  }
}
