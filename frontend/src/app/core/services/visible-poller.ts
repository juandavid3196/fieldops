import { DestroyRef } from '@angular/core';
import { Observable, Subscription } from 'rxjs';

export const POLL_INTERVAL_MS = 30_000;

export interface VisiblePoller {
  /** Arms the timer and the visibility listener; idempotent. Does not run `fn` by itself. */
  readonly start: () => void;
  /** Stops the timer, the listener and any in-flight run; idempotent. */
  readonly stop: () => void;
  /** Runs `fn` now unless a run is in flight. */
  readonly trigger: () => void;
}

/**
 * Polls `fn` every `ms` while the document is visible (specs/technician-messages BR-14). Hidden
 * documents pause the timer; becoming visible runs `fn` immediately. At most one run is in flight;
 * a failed run is silent and the next tick retries. The timer re-arms after each run settles.
 */
export function createVisiblePoller(
  document: Document,
  destroyRef: DestroyRef,
  fn: () => Observable<unknown>,
  ms = POLL_INTERVAL_MS,
): VisiblePoller {
  let active = false;
  let running = false;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let subscription: Subscription | null = null;

  const clearTimer = (): void => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
  };
  const arm = (): void => {
    clearTimer();
    if (active && !document.hidden) {
      timer = setTimeout(() => {
        timer = null;
        trigger();
      }, ms);
    }
  };
  const settle = (): void => {
    running = false;
    subscription = null;
    arm();
  };
  const trigger = (): void => {
    if (running) {
      return;
    }
    clearTimer();
    running = true;
    const run = fn().subscribe({ error: settle, complete: settle });
    if (running) {
      subscription = run;
    }
  };
  const onVisibility = (): void => {
    if (document.hidden) {
      clearTimer();
    } else {
      trigger();
    }
  };
  const stop = (): void => {
    active = false;
    document.removeEventListener('visibilitychange', onVisibility);
    clearTimer();
    subscription?.unsubscribe();
    subscription = null;
    running = false;
  };

  destroyRef.onDestroy(stop);

  return {
    start: () => {
      if (!active) {
        active = true;
        document.addEventListener('visibilitychange', onVisibility);
        arm();
      }
    },
    stop,
    trigger,
  };
}
