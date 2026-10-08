import { Component, ElementRef, computed, input, output, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';

import {
  BillingReviewDetail,
  PaymentTerms,
  READ_ONLY_MESSAGE,
} from '../../models/billing-review.model';
import {
  GenerateErrors,
  TERMS_OPTIONS,
  formatCalendarDate,
  formatInstantDate,
  money,
  taxJurisdiction,
} from '../../utils/billing-review-format';

let nextId = 0;

/** Invoice panel (BR-25, BR-26): draft defaults, disabled delivery/payment sections, summary, actions. */
@Component({
  selector: 'app-invoice-panel',
  imports: [ButtonDirective, FormsModule, InputText, Select, SpinnerIcon],
  templateUrl: './invoice-panel.html',
  styleUrl: './invoice-panel.scss',
})
export class InvoicePanel {
  private readonly issueDateField = viewChild<ElementRef<HTMLInputElement>>('issueDateField');
  private readonly termsField = viewChild(Select);

  readonly detail = input.required<BillingReviewDetail>();
  readonly issueDate = input.required<string>();
  readonly paymentTerms = input.required<PaymentTerms>();
  readonly dueDate = input.required<string>();
  readonly minDate = input.required<string>();
  readonly maxDate = input.required<string>();
  readonly errors = input<GenerateErrors>({});
  readonly pending = input<'return' | 'follow-up' | 'generate' | null>(null);
  readonly readOnly = input(false);
  readonly timezone = input('UTC');

  readonly issueDateChange = output<string>();
  readonly paymentTermsChange = output<PaymentTerms>();
  readonly returnToQueue = output<void>();
  readonly toggleFollowUp = output<void>();
  readonly generate = output<void>();

  protected readonly id = `invoice-panel-${nextId++}`;
  protected readonly termsOptions = TERMS_OPTIONS.map((option) => ({ ...option }));
  protected readonly readOnlyMessage = READ_ONLY_MESSAGE;

  protected readonly view = computed(() => {
    const detail = this.detail();
    const totals = detail.totals;
    const followUp = detail.followUp;
    return {
      jurisdiction: taxJurisdiction(detail.invoiceDefaults.taxLabel),
      due: this.dueDate() === '' ? '' : formatCalendarDate(this.dueDate()),
      subtotal: money(totals.invoiceSubtotal, totals.currency),
      discount: totals.discountTotal > 0 ? money(totals.discountTotal, totals.currency) : null,
      tax: money(totals.taxTotal, totals.currency),
      total: money(totals.invoiceTotal, totals.currency),
      followUp:
        followUp === null
          ? null
          : `Follow-up marked by ${followUp.byName} on ${formatInstantDate(followUp.at, this.timezone())}`,
    };
  });

  /** BR-27: focuses the first invalid field of this panel. */
  focusFirstInvalid(errors: GenerateErrors): void {
    if (errors.issueDate) {
      this.issueDateField()?.nativeElement.focus();
    } else if (errors.paymentTerms) {
      this.termsField()?.focus();
    }
  }
}
