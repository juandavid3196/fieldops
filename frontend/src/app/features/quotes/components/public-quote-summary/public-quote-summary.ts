import { Component, computed, input, model, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import {
  APPROVE_HINT,
  PublicQuote,
  PublicTotals,
  QUOTE_EXPIRED_NOTICE,
} from '../../models/public-quote.model';
import { dateOnlyLabel } from '../../utils/quote-format';

export type PdfState = 'idle' | 'downloading' | 'failed';

/** Quote total card of the quote link (BR-06): totals, approval controls and final states. */
@Component({
  selector: 'app-public-quote-summary',
  imports: [FormsModule, ButtonDirective, Checkbox, SpinnerIcon],
  templateUrl: './public-quote-summary.html',
  styleUrl: './public-quote-summary.scss',
})
export class PublicQuoteSummary {
  readonly quote = input.required<PublicQuote>();
  readonly totals = input.required<PublicTotals>();
  readonly calculating = input(false);
  readonly submitting = input(false);
  /** Names and amounts of the approved optional items. */
  readonly pdfState = input<PdfState>('idle');
  readonly actionError = input<string | null>(null);
  readonly acceptTerms = model(false);

  readonly approved = output<void>();
  readonly expiredNotice = computed(() => QUOTE_EXPIRED_NOTICE(this.quote().organization.name));
  readonly asked = output<void>();
  readonly declined = output<void>();
  readonly pdfRequested = output<void>();

  readonly hint = APPROVE_HINT;
  readonly open = computed(() => {
    const status = this.quote().quote.status;
    return status === 'sent' || status === 'clarification_requested';
  });
  readonly approveDisabled = computed(
    () => !this.acceptTerms() || this.calculating() || this.submitting(),
  );
  readonly validUntil = computed(() => dateOnlyLabel(this.quote().quote.validUntil));
  readonly respondedOn = computed(() => {
    const date = this.quote().response?.respondedOn;
    return date === undefined ? '' : dateOnlyLabel(date);
  });
  readonly askedOn = computed(() => {
    const date = this.quote().clarification?.askedOn;
    return date === undefined ? '' : dateOnlyLabel(date);
  });
  readonly approvedOptional = computed(() => {
    const ids = this.quote().response?.selectedOptionalLineIds ?? [];
    return this.quote().lines.filter((line) => line.id !== null && ids.includes(line.id));
  });

  money(value: number): string {
    return formatMoney(value, this.totals().currency);
  }
}
