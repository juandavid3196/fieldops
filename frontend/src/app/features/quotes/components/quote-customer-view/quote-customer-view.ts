import { NgTemplateOutlet } from '@angular/common';
import { Component, input } from '@angular/core';

import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { CustomerView, CustomerViewLine } from '../../models/quote.model';

/**
 * The customer's view of a quote (BR-23): shared by the editor preview and the read-only view.
 * It only renders customer-visible content; internal data never reaches its input.
 */
@Component({
  imports: [NgTemplateOutlet],
  selector: 'app-quote-customer-view',
  templateUrl: './quote-customer-view.html',
  styleUrl: './quote-customer-view.scss',
})
export class QuoteCustomerView {
  readonly view = input.required<CustomerView>();

  money(value: number): string {
    return formatMoney(value, this.view().currency);
  }

  quantity(line: CustomerViewLine): string {
    return line.unit === 'unit' ? `${line.quantity}` : `${line.quantity} ${line.unit}`;
  }
}
