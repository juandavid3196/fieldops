import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { isApiError } from '../models/api-error.model';
import { createVisiblePoller } from './visible-poller';

import { t } from '../i18n/translate';
/** `GET /messages/summary` (specs/technician-messages BR-07). */
export interface MessageSummary {
  readonly unreadMessages: number;
  readonly unreadConversations: number;
}

/**
 * Unread messages of the shells (BR-07, BR-14, BR-15). While polling is started the summary reloads
 * every 30 s, only while the tab is visible, and immediately when it becomes visible again. There is
 * no error UI: a failed load keeps the previous value and the next tick retries; a 403/404 stops
 * polling and clears the count.
 */
@Injectable({ providedIn: 'root' })
export class MessageUnreadService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly current = signal<MessageSummary | null>(null);
  private polling = false;
  private readonly readCount = signal(0);
  /** Conversation just marked read locally; the list clears its unread state at once. */
  readonly lastRead = signal<{ readonly id: string; readonly stamp: number } | null>(null);
  private readonly poller = createVisiblePoller(inject(DOCUMENT), inject(DestroyRef), () =>
    this.load(),
  );

  readonly unreadMessages = computed(() => this.current()?.unreadMessages ?? 0);
  readonly unreadConversations = computed(() => this.current()?.unreadConversations ?? 0);
  /** "99+" above 99; `null` (hidden) at 0. */
  readonly badge = computed(() => {
    const count = this.unreadMessages();
    return count === 0 ? null : count > 99 ? '99+' : String(count);
  });
  /** Text equivalent of the badge ("3 unread messages"); `null` at 0. */
  readonly label = computed(() => {
    const count = this.unreadMessages();
    return count === 0
      ? null
      : t('shell.messageUnreadService.unread', {
          count: count,
          value:
            count === 1
              ? t('shell.messageUnreadService.message')
              : t('shell.messageUnreadService.messages'),
        });
  });

  /** Loads the summary now unless a load is in flight. */
  refresh(): void {
    this.poller.trigger();
  }

  /** Starts the 30 s poll with an immediate load; idempotent. */
  startPolling(): void {
    if (!this.polling) {
      this.polling = true;
      this.poller.start();
      this.refresh();
    }
  }

  stopPolling(): void {
    this.polling = false;
    this.poller.stop();
  }

  /** A conversation was marked read: announces it to the list (which adjusts the summary). */
  markConversationRead(id: string): void {
    this.readCount.update((count) => count + 1);
    this.lastRead.set({ id, stamp: this.readCount() });
  }

  /** Lowers the summary by an already-read conversation, without a request. */
  subtract(messages: number): void {
    const summary = this.current();
    if (summary !== null && messages > 0) {
      this.current.set({
        unreadMessages: Math.max(0, summary.unreadMessages - messages),
        unreadConversations: Math.max(0, summary.unreadConversations - 1),
      });
    }
  }

  /** Stores a summary that arrived without a request. */
  set(summary: MessageSummary): void {
    this.current.set(summary);
  }

  /** Sign-out / shell teardown: stops polling and forgets the count. */
  reset(): void {
    this.stopPolling();
    this.current.set(null);
  }

  private load(): Observable<MessageSummary> {
    return this.http.get<MessageSummary>(buildApiUrl(this.config, 'messages/summary')).pipe(
      tap({
        next: (summary) => this.set(summary),
        error: (error: unknown) => {
          if (isApiError(error) && (error.status === 403 || error.status === 404)) {
            this.reset();
          }
        },
      }),
    );
  }
}
