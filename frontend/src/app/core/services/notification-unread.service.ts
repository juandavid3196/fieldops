import { HttpClient } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';

import { API_CONFIG, buildApiUrl } from '../config/api.config';
import { isApiError } from '../models/api-error.model';

/** BR-16: successful loads are reused for this long. */
export const UNREAD_REUSE_MS = 30_000;

/**
 * Unread count of the shell bell (specs/notifications-center BR-16). `count` is `null` until a load
 * succeeds (no badge). While polling is started (internal roles) the summary reloads every 30 s
 * after the last successful load, only while the tab is visible. There is no error UI: a failed
 * load keeps the previous value and the next tick retries; a 403 or `reset()` stops polling.
 */
@Injectable({ providedIn: 'root' })
export class NotificationUnreadService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(API_CONFIG);
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<number | null>(null);
  private loadedAt: number | null = null;
  private inFlight: Subscription | null = null;
  private polling = false;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private readonly polled = signal<{ unread: number } | null>(null);
  private readonly onVisibility = (): void => this.visibilityChanged();

  readonly count = this.current.asReadonly();
  /** BR-25: a new object per poll/tab-focus load; navigation and mark responses never emit. */
  readonly polledSummary = this.polled.asReadonly();

  /** Loads `GET /notifications/summary` unless one is in flight or the last success is under 30 s old. */
  refresh(): void {
    if (this.loadedAt !== null && Date.now() - this.loadedAt < UNREAD_REUSE_MS) {
      return;
    }
    this.load(false);
  }

  /** BR-16: starts the 30 s poll and the tab-visibility listener (internal roles only). */
  startPolling(): void {
    if (this.polling) {
      return;
    }
    this.polling = true;
    this.document.addEventListener('visibilitychange', this.onVisibility);
    this.schedule(this.untilNext());
  }

  /** Stops the poll and removes the listener; the value is kept. */
  stopPolling(): void {
    this.polling = false;
    this.document.removeEventListener('visibilitychange', this.onVisibility);
    this.clearTimer();
  }

  private load(polled: boolean): void {
    if (this.inFlight !== null) {
      return;
    }
    const subscription = this.http
      .get<{ unread: number }>(buildApiUrl(this.config, 'notifications/summary'))
      .subscribe({
        next: (summary) => {
          this.set(summary.unread);
          if (polled) {
            this.polled.set({ unread: summary.unread });
          }
        },
        error: (error: unknown) => {
          if (isApiError(error) && error.kind === 'forbidden') {
            this.stopPolling();
          } else {
            this.schedule(UNREAD_REUSE_MS);
          }
        },
      });
    if (!subscription.closed) {
      this.inFlight = subscription;
      subscription.add(() => {
        if (this.inFlight === subscription) {
          this.inFlight = null;
        }
      });
    }
  }

  private untilNext(): number {
    return this.loadedAt === null ? 0 : Math.max(0, UNREAD_REUSE_MS - (Date.now() - this.loadedAt));
  }

  private schedule(delay: number): void {
    this.clearTimer();
    if (!this.polling || this.document.hidden) {
      return;
    }
    this.timer = setTimeout(() => {
      this.timer = null;
      this.load(true);
    }, delay);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private visibilityChanged(): void {
    if (this.document.hidden) {
      this.clearTimer();
    } else if (this.inFlight === null) {
      this.schedule(this.untilNext());
    }
  }

  /** Stores the count of a mark response (no extra request) and restarts the reuse window. */
  set(count: number): void {
    this.current.set(count);
    this.loadedAt = Date.now();
    this.schedule(UNREAD_REUSE_MS);
  }

  /** Sign-out: forgets the count so the next user starts without a badge. */
  reset(): void {
    this.stopPolling();
    this.polled.set(null);
    this.inFlight?.unsubscribe();
    this.inFlight = null;
    this.current.set(null);
    this.loadedAt = null;
  }

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.stopPolling();
      this.inFlight?.unsubscribe();
    });
  }
}
