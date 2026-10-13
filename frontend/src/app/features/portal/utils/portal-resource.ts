import { DestroyRef, Signal, inject, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';

import { isApiError } from '../../../core/models/api-error.model';
import { Page } from '../models/portal.model';

export type ResourceState = 'loading' | 'ready' | 'empty' | 'error' | 'not-found';

/**
 * Signal loader of one portal read: `loading` → `ready | empty | error | not-found`. A new `load`
 * cancels the previous one. A `401` is handled by the portal interceptor, never here.
 */
export class PortalResource<T> {
  private readonly stateSignal = signal<ResourceState>('loading');
  private readonly dataSignal = signal<T | null>(null);
  private subscription: Subscription | undefined;

  readonly state: Signal<ResourceState> = this.stateSignal.asReadonly();
  readonly data: Signal<T | null> = this.dataSignal.asReadonly();

  constructor(
    private readonly isEmpty: (value: T) => boolean = () => false,
    destroyRef: DestroyRef = inject(DestroyRef),
  ) {
    destroyRef.onDestroy(() => this.subscription?.unsubscribe());
  }

  /** `keepData` keeps the current content visible while a refresh runs (no skeleton). */
  load(source: Observable<T>, keepData = false): void {
    this.subscription?.unsubscribe();
    if (!keepData) {
      this.stateSignal.set('loading');
    }
    this.subscription = source.subscribe({
      next: (value) => {
        this.dataSignal.set(value);
        this.stateSignal.set(this.isEmpty(value) ? 'empty' : 'ready');
      },
      error: (error: unknown) => {
        this.stateSignal.set(isApiError(error) && error.status === 404 ? 'not-found' : 'error');
      },
    });
  }

  /** Replaces the loaded value in place (dialog results patch cards without a reload). */
  patch(update: (value: T) => T): void {
    const current = this.dataSignal();
    if (current !== null) {
      this.dataSignal.set(update(current));
    }
  }
}

/** A paginated list resource: empty when the page has no items. */
export function pageResource<T>(): PortalResource<Page<T>> {
  return new PortalResource<Page<T>>((page) => page.items.length === 0);
}
