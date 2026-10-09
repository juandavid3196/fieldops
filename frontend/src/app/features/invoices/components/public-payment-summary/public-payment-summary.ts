import { Component, computed, input } from '@angular/core';

import { PAYMENT_STATUS_LABELS } from '../../models/invoices-hub.model';
import { PublicInvoice } from '../../models/invoice.model';
import { formatCalendarDate, money } from '../../utils/public-invoice-format';

/**
 * "Payment summary" (BR-30): payments with their refunds, total paid and balance. `detailed` adds
 * the invoice/service rows and the subtotal/tax breakdown of the paid view.
 */
@Component({
  selector: 'app-public-payment-summary',
  templateUrl: './public-payment-summary.html',
  styleUrl: './public-payment-summary.scss',
})
export class PublicPaymentSummary {
  readonly invoice = input.required<PublicInvoice>();
  readonly detailed = input(false);

  protected readonly view = computed(() => {
    const invoice = this.invoice();
    const currency = invoice.currency;
    const { totals } = invoice;
    return {
      subtotal: money(totals.subtotal, currency),
      discount: totals.discountTotal > 0 ? money(totals.discountTotal, currency) : null,
      tax: money(totals.taxTotal, currency),
      paid: money(invoice.amountPaid, currency),
      balance: money(invoice.balanceDue, currency),
      payments: invoice.payments.map((payment) => ({
        label: `${payment.number} · ${payment.methodLabel} · ${formatCalendarDate(payment.paidOn)}`,
        amount: money(payment.amount, currency),
        refunded: payment.refundedAmount > 0 ? money(payment.refundedAmount, currency) : null,
        refundTag: PAYMENT_STATUS_LABELS[payment.status],
      })),
    };
  });
}
