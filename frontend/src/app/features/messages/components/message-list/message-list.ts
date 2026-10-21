import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Observable, Subscription, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { MessageUnreadService } from '../../../../core/services/message-unread.service';
import { createVisiblePoller } from '../../../../core/services/visible-poller';
import {
  INACTIVE_MESSAGE,
  NOT_LINKED_MESSAGE,
  classifyFailure,
} from '../../../technician-jobs/utils/technician-errors';
import { ConversationPage, ConversationSummary, MessageFilter } from '../../models/messages.model';
import { MessagesService } from '../../services/messages.service';
import {
  FORBIDDEN_MESSAGE,
  LIST_ERROR_MESSAGE,
  OPEN_ERROR_MESSAGE,
  SEARCH_MIN_LENGTH,
  previewLine,
  relativeTime,
  unreadText,
} from '../../utils/messages-format';

import { localized, localizedList, t } from '../../../../core/i18n/translate';
export const SEARCH_DEBOUNCE_MS = 300;
const PAGE_SIZE = 30;
export const LIST_NOTICE = (): string => t('messages.messageList.listNotice');

type ListState = 'loading' | 'ready' | 'error' | 'forbidden' | 'inactive' | 'not-linked';

interface Row {
  readonly key: string;
  readonly item: ConversationSummary;
  readonly preview: string;
  readonly time: string;
  readonly chip: string;
  readonly unreadLabel: string | null;
  readonly initials: string;
}

const FILTERS: readonly { readonly value: MessageFilter; readonly label: string }[] = localizedList(
  () => [
    { value: 'all', label: t('messages.messageList.all') },
    { value: 'unread', label: t('messages.messageList.unread') },
    { value: 'jobs', label: t('messages.messageList.jobs') },
    { value: 'team', label: t('messages.messageList.team') },
  ],
);

const EMPTY_TEXT: Readonly<Record<MessageFilter, string>> = localized(() => ({
  all: t('messages.messageList.noConversationsYet'),
  unread: t('messages.messageList.noUnreadMessages'),
  jobs: t('messages.messageList.noJobConversationsYet'),
  team: t('messages.messageList.noTeamConversationsYet'),
}));

function keyOf(item: ConversationSummary): string {
  return item.id ?? `team-${item.branchId}`;
}

/** Conversation list (BR-16): search, filter chips, rows as links, 30 s polling while visible. */
@Component({
  selector: 'app-message-list',
  imports: [
    TranslocoPipe,
    NgTemplateOutlet,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Message,
    RouterLink,
    Skeleton,
  ],
  templateUrl: './message-list.html',
  styleUrl: './message-list.scss',
})
export class MessageList {
  private readonly messages = inject(MessagesService);
  private readonly unread = inject(MessageUnreadService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private inFlight: Subscription | null = null;
  private debounce: ReturnType<typeof setTimeout> | null = null;
  private cursor: string | null = null;
  private extended = false;

  readonly basePath = input.required<string>();
  readonly selectedId = input<string | null>(null);

  get notice(): string {
    return LIST_NOTICE();
  }
  get errorMessage(): string {
    return LIST_ERROR_MESSAGE();
  }
  get openErrorMessage(): string {
    return OPEN_ERROR_MESSAGE();
  }
  readonly filters = FILTERS;
  readonly skeletonRows = [0, 1, 2, 3];

  readonly state = signal<ListState>('loading');
  readonly filter = signal<MessageFilter>('all');
  readonly text = signal('');
  /** Search applied to the request: trimmed, at least 2 characters (BR-16). */
  readonly search = signal('');
  readonly items = signal<readonly ConversationSummary[]>([]);
  readonly hasMore = signal(false);
  readonly loadingMore = signal(false);
  readonly openFailed = signal(false);
  readonly unreadCount = this.unread.unreadMessages;
  readonly unreadBadge = this.unread.badge;
  readonly unreadFilterLabel = computed(() => {
    const count = this.unread.unreadMessages();
    return count === 0 ? null : unreadText(count);
  });

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
  readonly emptyText = computed(() =>
    this.search() === ''
      ? EMPTY_TEXT[this.filter()]
      : t('messages.messageList.noResultsFor', { search: this.search() }),
  );
  readonly rows = computed<Row[]>(() => {
    const now = new Date();
    return this.items().map((item) => ({
      key: keyOf(item),
      item,
      preview: previewLine(item),
      time: item.lastMessage === null ? '' : relativeTime(item.lastMessage.createdAt, now),
      chip:
        item.workOrder === null
          ? t('messages.messageList.team')
          : `#${item.workOrder.displayNumber}`,
      unreadLabel: item.unreadCount > 0 ? unreadText(item.unreadCount) : null,
      initials: item.lastMessage?.authorInitials ?? '',
    }));
  });

  constructor() {
    effect(() => {
      const read = this.unread.lastRead();
      if (read === null) {
        return;
      }
      const cleared = untracked(() => this.items()).find(
        (item) => item.id === read.id && item.unreadCount > 0,
      );
      if (cleared !== undefined) {
        this.items.update((items) =>
          items.map((item) => (item.id === read.id ? { ...item, unreadCount: 0 } : item)),
        );
        this.unread.subtract(cleared.unreadCount);
      }
    });
    const poller = createVisiblePoller(inject(DOCUMENT), this.destroyRef, () => this.poll());
    this.destroyRef.onDestroy(() => {
      this.inFlight?.unsubscribe();
      if (this.debounce !== null) {
        clearTimeout(this.debounce);
      }
    });
    this.load();
    poller.start();
  }

  setFilter(filter: MessageFilter): void {
    if (filter !== this.filter()) {
      this.filter.set(filter);
      this.load();
    }
  }

  /** Debounced 300 ms; applied from 2 characters, cleared below that. */
  onSearch(value: string): void {
    this.text.set(value);
    if (this.debounce !== null) {
      clearTimeout(this.debounce);
    }
    this.debounce = setTimeout(() => {
      this.debounce = null;
      const term = value.trim();
      const applied = term.length >= SEARCH_MIN_LENGTH ? term : '';
      if (applied !== this.search()) {
        this.search.set(applied);
        this.load();
      }
    }, SEARCH_DEBOUNCE_MS);
  }

  retry(): void {
    this.load();
  }

  /** First page for the current filter and search; replaces any in-flight request. */
  load(): void {
    this.inFlight?.unsubscribe();
    this.cursor = null;
    this.state.set('loading');
    this.inFlight = this.messages.list(this.filter(), this.search(), null).subscribe({
      next: (page) => {
        this.apply(page, false);
        this.state.set('ready');
      },
      error: (error: unknown) => this.fail(error),
    });
  }

  loadMore(): void {
    if (this.loadingMore() || this.cursor === null) {
      return;
    }
    this.loadingMore.set(true);
    this.messages.list(this.filter(), this.search(), this.cursor).subscribe({
      next: (page) => {
        this.items.update((items) => [...items, ...page.items]);
        this.extended = true;
        this.cursor = page.nextCursor;
        this.hasMore.set(page.nextCursor !== null);
        this.loadingMore.set(false);
      },
      error: () => this.loadingMore.set(false),
    });
  }

  /** A virtual Team entry has no id yet: resolve it, then open it. */
  openTeam(item: ConversationSummary): void {
    this.openFailed.set(false);
    this.messages.resolve({ type: 'team', branchId: item.branchId }).subscribe({
      next: (id) => void this.router.navigate([this.basePath(), id]),
      error: () => this.openFailed.set(true),
    });
  }

  /** Silent first-page refresh; items beyond the first page stay. */
  private poll(): Observable<ConversationPage> {
    return this.messages.list(this.filter(), this.search(), null).pipe(
      tap((page) => {
        if (this.state() === 'ready') {
          this.apply(page, true);
        }
      }),
    );
  }

  /** Replaces the list; a poll after "Load more" keeps the items beyond the first page and their cursor. */
  private apply(page: ConversationPage, poll: boolean): void {
    if (poll && this.extended) {
      const fresh = new Set(page.items.map(keyOf));
      const tail = this.items()
        .slice(PAGE_SIZE)
        .filter((item) => !fresh.has(keyOf(item)));
      this.items.set([...page.items, ...tail]);
      return;
    }
    this.extended = false;
    this.items.set(page.items);
    this.cursor = page.nextCursor;
    this.hasMore.set(page.nextCursor !== null);
  }

  private fail(error: unknown): void {
    const failure = classifyFailure(error);
    this.state.set(
      failure === 'forbidden' || failure === 'inactive' || failure === 'not-linked'
        ? failure
        : 'error',
    );
  }
}
