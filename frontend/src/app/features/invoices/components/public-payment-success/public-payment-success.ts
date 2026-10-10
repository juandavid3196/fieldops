import { Component, computed, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { PublicInvoice, PublicPayment } from '../../models/invoice.model';
import { cardBrand, formatCalendarDate, money, netText } from '../../utils/public-invoice-format';
import { PublicPaymentSummary } from '../public-payment-summary/public-payment-summary';

/**
 * Success and paid view (BR-30): confirmation card with the latest payment, downloads, the payment
 * summary, a slot for the review card and the closing info band.
 */
@Component({
  selector: 'app-public-payment-success',
  imports: [ButtonDirective, PublicPaymentSummary, SpinnerIcon],
  templateUrl: './public-payment-success.html',
  styleUrl: './public-payment-success.scss',
})
export class PublicPaymentSuccess {
  readonly invoice = input.required<PublicInvoice>();
  /** Name of the download in progress (`invoice`, `receipt`), if any. */
  readonly busy = input<string | null>(null);
  readonly receiptRequested = output<void>();
  readonly invoiceRequested = output<void>();

  protected readonly view = computed(() => {
    const invoice = this.invoice();
    const payment: PublicPayment | null = invoice.payments.at(0) ?? null;
    const paidOn = invoice.timeline.paidOn ?? payment?.paidOn ?? null;
    return {
      thanks:
        invoice.customerFirstName === null
          ? 'Thank you. Your payment has been confirmed.'
          : `Thank you, ${invoice.customerFirstName}. Your payment has been confirmed.`,
      amount:
        payment === null
          ? money(invoice.amountPaid, invoice.currency)
          : netText(payment, invoice.currency),
      paidOn: paidOn === null ? null : formatCalendarDate(paidOn),
      payment,
      brand: payment === null ? null : cardBrand(payment.methodLabel),
    };
  });
}
