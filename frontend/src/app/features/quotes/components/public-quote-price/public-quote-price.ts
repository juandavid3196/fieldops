import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToggleSwitch, ToggleSwitchChangeEvent } from 'primeng/toggleswitch';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { PublicLine, PublicQuote, PublicTotals } from '../../models/public-quote.model';

export interface OptionalToggle {
  readonly id: string;
  readonly checked: boolean;
}

/**
 * Price details of the quote link (BR-05, BR-11): regular lines, optional items with toggles and
 * the server-computed totals. Amounts are only displayed, never calculated here.
 */
@Component({
  selector: 'app-public-quote-price',
  imports: [NgTemplateOutlet, FormsModule, ToggleSwitch],
  templateUrl: './public-quote-price.html',
  styleUrl: './public-quote-price.scss',
})
export class PublicQuotePrice {
  readonly quote = input.required<PublicQuote>();
  readonly totals = input.required<PublicTotals>();
  readonly selectedIds = input.required<readonly string[]>();
  /** Toggles are interactive only while the quote awaits a response. */
  readonly editable = input(false);
  readonly calculating = input(false);

  readonly toggled = output<OptionalToggle>();

  readonly regular = computed(() => this.quote().lines.filter((line) => !line.isOptional));
  readonly optional = computed(() => this.quote().lines.filter((line) => line.isOptional));

  money(value: number): string {
    return formatMoney(value, this.totals().currency);
  }

  quantity(line: PublicLine): string {
    return line.unit === 'unit' ? `${line.quantity}` : `${line.quantity} ${line.unit}`;
  }

  checked(line: PublicLine): boolean {
    return line.id !== null && this.selectedIds().includes(line.id);
  }

  toggle(line: PublicLine, event: ToggleSwitchChangeEvent): void {
    if (line.id !== null) {
      this.toggled.emit({ id: line.id, checked: event.checked });
    }
  }
}
