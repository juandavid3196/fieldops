import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { isApiError } from '../../../../core/models/api-error.model';
import {
  PublicInvoice,
  REVIEW_COMMENT_MAX_LENGTH,
  REVIEW_FAILED_MESSAGE,
} from '../../models/invoice.model';
import { InvoiceLinkApi } from '../../services/invoice-link-api';
import { formatCalendarDate } from '../../utils/public-invoice-format';

export const REVIEW_RATING_MESSAGE = 'Select a rating.';
export const REVIEW_COMMENT_MESSAGE = 'Use 500 characters or fewer.';

/** "How was your service?" card (BR-22, BR-30): star radio group, comment and its states. */
@Component({
  selector: 'app-public-review-card',
  imports: [ButtonDirective, SpinnerIcon],
  templateUrl: './public-review-card.html',
  styleUrl: './public-review-card.scss',
})
export class PublicReviewCard {
  private readonly service = inject(InvoiceLinkApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly invoice = input.required<PublicInvoice>();
  readonly unavailable = output<void>();

  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly max = REVIEW_COMMENT_MAX_LENGTH;
  protected readonly failedMessage = REVIEW_FAILED_MESSAGE;

  protected readonly rating = signal(0);
  protected readonly comment = signal('');
  protected readonly ratingError = signal(false);
  protected readonly sending = signal(false);
  protected readonly failed = signal(false);
  protected readonly thanks = signal(false);
  protected readonly dismissed = signal(false);

  protected readonly visible = computed(
    () => this.thanks() || (this.invoice().review.available && !this.dismissed()),
  );
  protected readonly serviceDate = computed(() => {
    const date = this.invoice().service.serviceDate;
    return date === null ? null : formatCalendarDate(date);
  });
  protected readonly commentError = computed(() => this.comment().length > this.max);

  protected onComment(event: Event): void {
    this.comment.set((event.target as HTMLTextAreaElement).value);
  }

  protected submit(): void {
    if (this.sending()) {
      return;
    }
    const comment = this.comment().trim();
    this.ratingError.set(this.rating() === 0);
    if (this.rating() === 0 || comment.length > this.max) {
      return;
    }
    this.sending.set(true);
    this.failed.set(false);
    this.service
      .submitReview(this.rating(), comment)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.finish(),
        error: (error: unknown) => {
          const apiError = isApiError(error) ? error : null;
          if (apiError?.status === 409 && apiError.code === 'review_exists') {
            this.finish();
          } else if (apiError?.status === 404) {
            this.sending.set(false);
            this.unavailable.emit();
          } else {
            this.sending.set(false);
            this.failed.set(true);
          }
        },
      });
  }

  private finish(): void {
    this.sending.set(false);
    this.thanks.set(true);
  }
}
