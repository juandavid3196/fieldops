import {
  Component,
  computed,
  effect,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { CALC_ERROR_MESSAGE, Calculation } from '../../models/quote.model';

export type CalcStatus = 'idle' | 'loading' | 'ready' | 'error';

/**
 * Quote summary (BR-13 to BR-16): shows only backend-calculated values, "—" when the form is
 * invalid, keeps previous values with a loading indicator during a recalculation and exposes the
 * totals as a polite live region. The internal margin is marked "Internal".
 */
@Component({
  selector: 'app-quote-summary',
  imports: [FormsModule, ButtonDirective, InputText, Message],
  templateUrl: './quote-summary.html',
  styleUrl: './quote-summary.scss',
})
export class QuoteSummary {
  readonly status = input.required<CalcStatus>();
  readonly data = input.required<Calculation | null>();
  /** The form has invalid fields: no calculation runs and values show "—". */
  readonly invalid = input(false);
  readonly currency = input.required<string>();
  readonly discount = model.required<number | null>();
  readonly discountError = input<string | null>(null);
  /** Read-only summaries (the view page) show the values without the discount input. */
  readonly readOnly = input(false);

  readonly retry = output<void>();

  readonly errorMessage = CALC_ERROR_MESSAGE;
  private readonly revealed = signal(false);
  readonly discountOpen = computed(
    () => !this.readOnly() && (this.revealed() || (this.discount() ?? 0) > 0),
  );

  constructor() {
    // A reload that brings a saved discount keeps its input visible.
    effect(() => {
      if ((this.discount() ?? 0) > 0) {
        untracked(() => this.revealed.set(true));
      }
    });
  }

  private readonly shown = computed(() => (this.invalid() ? null : this.data()));
  private readonly money = (value: number): string => formatMoney(value, this.currency());

  readonly rows = computed(() => {
    const data = this.shown();
    return {
      subtotal: data === null ? '—' : this.money(data.subtotal),
      discount: data === null ? '—' : `−${this.money(data.discountTotal)}`,
      taxLabel: data?.taxLabel ?? 'Tax',
      tax: data === null ? '—' : this.money(data.taxTotal),
      total: data === null ? '—' : this.money(data.total),
      margin:
        data === null
          ? '—'
          : data.margin.percent === null
            ? '—'
            : `${data.margin.percent}% · ${this.money(data.margin.grossProfit)}`,
    };
  });

  readonly loading = computed(() => this.status() === 'loading');
  readonly failed = computed(() => this.status() === 'error');

  revealDiscount(): void {
    this.revealed.set(true);
  }
}
