import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { formatCalendarDate, money } from '../../../billing-review/utils/billing-review-format';
import { InvoicePreview as InvoicePreviewData } from '../../models/invoice.model';
import { organizationInitials, quantityText, taxText } from '../../utils/invoice-format';

/**
 * BR-04 invoice document, shared by the internal page and the public link. Presentational: it
 * shows only the contract fields it receives, never recalculates amounts.
 */
@Component({
  selector: 'app-invoice-preview',
  imports: [RouterLink],
  templateUrl: './invoice-preview.html',
  styleUrl: './invoice-preview.scss',
})
export class InvoicePreview {
  readonly preview = input.required<InvoicePreviewData>();
  /** Object URL of the organization logo; initials are shown when `null`. */
  readonly logoUrl = input<string | null>(null);
  /** Internal page only: the work order number links to `/jobs/<id>`. */
  readonly workOrderId = input<string | null>(null);

  protected readonly view = computed(() => {
    const preview = this.preview();
    const currency = preview.currency;
    return {
      initials: organizationInitials(preview.organization.name),
      issueDate: formatCalendarDate(preview.issueDate),
      dueDate: formatCalendarDate(preview.dueDate),
      lines: preview.lines.map((line) => ({
        description: line.description,
        detail: line.detail,
        quantity: quantityText(line.quantity, line.unit),
        rate: money(line.unitPrice, currency),
        tax: taxText(line.taxRate),
        amount: money(line.amount, currency),
      })),
      subtotal: money(preview.totals.subtotal, currency),
      discount:
        preview.totals.discountTotal > 0 ? money(preview.totals.discountTotal, currency) : null,
      tax: money(preview.totals.taxTotal, currency),
      total: money(preview.totals.total, currency),
    };
  });
}
