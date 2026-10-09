import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  exhaustMap,
  firstValueFrom,
  map,
  of,
  takeUntil,
  takeWhile,
  timer,
} from 'rxjs';

import { isApiError } from '../../../core/models/api-error.model';
import {
  AttemptState,
  CARD_START_FAILED_MESSAGE,
  CARD_UNAVAILABLE_MESSAGE,
  CardIntent,
  PUBLIC_RATE_LIMITED_MESSAGE,
  PublicInvoice,
  failureMessage,
} from '../models/invoice.model';
import { PublicInvoiceTokenService } from './public-invoice-token.service';
import { PublicInvoiceService } from './public-invoice.service';
import { StripeCardHandle } from './stripe-payment.adapter';

export type CardPhase =
  'idle' | 'submitting' | 'processing' | 'failed' | 'long-pending' | 'succeeded';

/** What the page must do next: re-read the invoice, or show the neutral state. */
export type FlowEvent = 'succeeded' | 'reload' | 'unavailable';

const POLL_INTERVAL_MS = 2000;
const POLL_CAP_MS = 60_000;

type PollResult =
  | { readonly kind: 'state'; readonly state: AttemptState }
  | { readonly kind: 'missing' }
  | { readonly kind: 'retry' };

/**
 * Card payment state of the public page (BR-29), provided per page. The server status is the only
 * truth: a Stripe confirm error never decides the outcome, the overlay stays and polling decides.
 */
@Injectable()
export class PublicPaymentFlow {
  private readonly api = inject(PublicInvoiceService);
  private readonly tokens = inject(PublicInvoiceTokenService);
  private readonly document = inject(DOCUMENT);

  private idempotencyKey: string | null = null;
  private polling: Subscription | null = null;
  private destroyed = false;

  readonly events = new Subject<FlowEvent>();
  readonly phase = signal<CardPhase>('idle');
  readonly cardholderName = signal('');
  /** BR-29 category message of a failed attempt. */
  readonly failure = signal<string | null>(null);
  /** Message of a failure before any attempt exists (`502`, `429`, network). */
  readonly startError = signal<string | null>(null);

  readonly overlay = computed(() =>
    ['submitting', 'processing', 'succeeded'].includes(this.phase()),
  );
  readonly locked = computed(() => this.overlay() || this.phase() === 'long-pending');

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.stopPolling();
      this.events.complete();
    });
  }

  /** The key is created when the card form is shown and kept across validation/network retries. */
  ensureKey(): void {
    this.idempotencyKey ??= crypto.randomUUID();
  }

  /** Called when the page loads or reloads: follows an attempt left pending or stored in the tab. */
  resume(invoice: PublicInvoice): void {
    if (this.phase() !== 'idle') {
      return;
    }
    const active = invoice.activeAttempt?.attemptId ?? null;
    if (active !== null) {
      this.poll(active);
      return;
    }
    const stored = this.tokens.readAttempt();
    if (stored === null) {
      return;
    }
    if (invoice.status === 'paid') {
      this.tokens.clearAttempt();
      return;
    }
    this.poll(stored);
  }

  /** Back to a clean form (a new payment may follow a partial refund or payment). */
  reset(): void {
    this.stopPolling();
    this.tokens.clearAttempt();
    this.idempotencyKey = null;
    this.failure.set(null);
    this.startError.set(null);
    this.phase.set('idle');
  }

  /** "Choose another payment method": the failure alert goes away and the next try gets a new key. */
  dismissFailure(): void {
    if (this.phase() === 'failed') {
      this.failure.set(null);
      this.idempotencyKey = null;
      this.phase.set('idle');
    }
  }

  async submit(handle: StripeCardHandle, name: string): Promise<void> {
    const previous = this.phase();
    if (previous !== 'idle' && previous !== 'failed') {
      return;
    }
    // Synchronous guard: no second submission can pass before the first await.
    this.phase.set('submitting');
    this.startError.set(null);
    if (previous === 'failed') {
      this.failure.set(null);
      this.idempotencyKey = null;
    }
    this.ensureKey();

    let valid: boolean;
    try {
      valid = await handle.validate();
    } catch {
      valid = false;
    }
    if (this.destroyed) {
      return;
    }
    if (!valid) {
      this.phase.set('idle');
      return;
    }

    let intent: CardIntent;
    try {
      intent = await firstValueFrom(this.api.cardIntent(this.idempotencyKey ?? ''));
    } catch (error) {
      if (!this.destroyed) {
        this.intentFailed(error);
      }
      return;
    }
    if (this.destroyed) {
      return;
    }
    this.tokens.storeAttempt(intent.attemptId);
    this.phase.set('processing');
    // A replay without a client secret, or for a non-pending attempt, follows its status directly.
    if (intent.clientSecret !== null && intent.status === 'pending') {
      try {
        await handle.confirm({
          clientSecret: intent.clientSecret,
          billingName: name.trim(),
          returnUrl: `${this.document.location.origin}/invoices/view`,
        });
      } catch {
        // The server status decides.
      }
    }
    if (!this.destroyed) {
      this.poll(intent.attemptId);
    }
  }

  private intentFailed(error: unknown): void {
    const apiError = isApiError(error) ? error : null;
    this.phase.set('idle');
    if (apiError?.status === 404) {
      this.events.next('unavailable');
    } else if (apiError?.kind === 'rate-limited') {
      this.startError.set(PUBLIC_RATE_LIMITED_MESSAGE);
    } else if (apiError?.status === 409 && apiError.code === 'payment_in_progress') {
      this.phase.set('long-pending');
    } else if (apiError?.status === 409 && apiError.code === 'invoice_not_payable') {
      this.events.next('reload');
    } else if (apiError?.status === 409 && apiError.code === 'card_unavailable') {
      this.startError.set(CARD_UNAVAILABLE_MESSAGE);
      this.events.next('reload');
    } else {
      if (apiError?.status === 502 || apiError?.code === 'idempotency_conflict') {
        this.idempotencyKey = null;
      }
      // Network failures and other errors keep the key so the retry is the same submission.
      this.startError.set(CARD_START_FAILED_MESSAGE);
    }
  }

  private status(attemptId: string): Observable<PollResult> {
    return this.api.paymentStatus(attemptId).pipe(
      map((state): PollResult => ({ kind: 'state', state })),
      catchError((error: unknown) =>
        of<PollResult>(
          isApiError(error) && error.status === 404 ? { kind: 'missing' } : { kind: 'retry' },
        ),
      ),
    );
  }

  /** Polls every 2 s for up to 60 s; the first call is immediate. 429, network and 5xx keep polling. */
  private poll(attemptId: string): void {
    this.stopPolling();
    this.phase.set('processing');
    let settled = false;
    this.polling = timer(0, POLL_INTERVAL_MS)
      .pipe(
        exhaustMap(() => this.status(attemptId)),
        takeWhile((result) => result.kind === 'retry' || this.isPending(result), true),
        takeUntil(timer(POLL_CAP_MS)),
      )
      .subscribe({
        next: (result) => {
          if (result.kind === 'missing') {
            settled = true;
            this.tokens.clearAttempt();
            this.phase.set('idle');
            this.events.next('unavailable');
          } else if (result.kind === 'state' && !this.isPending(result)) {
            settled = true;
            this.tokens.clearAttempt();
            if (result.state.status === 'failed') {
              this.failure.set(failureMessage(result.state.failureCategory));
              this.phase.set('failed');
            } else {
              this.phase.set('succeeded');
              this.events.next('succeeded');
            }
          }
        },
        complete: () => {
          if (!settled) {
            this.phase.set('long-pending');
          }
        },
      });
  }

  private isPending(result: PollResult): boolean {
    return result.kind === 'state' && result.state.status === 'pending';
  }

  private stopPolling(): void {
    this.polling?.unsubscribe();
    this.polling = null;
  }
}
