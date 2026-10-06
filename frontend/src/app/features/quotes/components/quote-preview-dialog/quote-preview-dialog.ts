import { Component, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { CustomerView, PREVIEW_ERROR_MESSAGE } from '../../models/quote.model';
import { QuoteCustomerView } from '../quote-customer-view/quote-customer-view';
import { CalcStatus } from '../quote-summary/quote-summary';

export const PREVIEW_INVALID_MESSAGE = 'Fix the highlighted fields to preview this quote.';

/** Preview dialog (BR-23): the customer view of the current form from its latest calculation. */
@Component({
  selector: 'app-quote-preview-dialog',
  imports: [ButtonDirective, Dialog, Message, Skeleton, QuoteCustomerView],
  templateUrl: './quote-preview-dialog.html',
  styleUrl: './quote-preview-dialog.scss',
})
export class QuotePreviewDialog {
  readonly open = input.required<boolean>();
  readonly status = input.required<CalcStatus>();
  /** Null while the calculation is missing, stale or the form is invalid. */
  readonly view = input.required<CustomerView | null>();
  readonly invalid = input(false);

  readonly dismissed = output<void>();
  readonly retry = output<void>();

  readonly errorMessage = PREVIEW_ERROR_MESSAGE;
  readonly invalidMessage = PREVIEW_INVALID_MESSAGE;
}
