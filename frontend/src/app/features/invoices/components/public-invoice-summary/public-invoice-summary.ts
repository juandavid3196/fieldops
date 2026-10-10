import { NgTemplateOutlet } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { AccordionModule } from 'primeng/accordion';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';

import { MD_QUERY, watchMedia } from '../../../../core/config/breakpoints';
import { PublicInvoice } from '../../models/invoice.model';
import { organizationInitials, quantityText } from '../../utils/invoice-format';
import {
  dueText,
  formatCalendarDate,
  money,
  statusChip,
  timelineSteps,
} from '../../utils/public-invoice-format';
import { PublicInvoiceTimeline } from '../public-invoice-timeline/public-invoice-timeline';
import { PublicPaymentSummary } from '../public-payment-summary/public-payment-summary';

/**
 * Title, status, timeline and, in the payable layout, the invoice card (BR-27). The payment panel
 * or the success view is projected beside/below it. Presentational: actions are outputs.
 */
@Component({
  selector: 'app-public-invoice-summary',
  imports: [
    AccordionModule,
    ButtonDirective,
    NgTemplateOutlet,
    PublicInvoiceTimeline,
    PublicPaymentSummary,
    SpinnerIcon,
  ],
  templateUrl: './public-invoice-summary.html',
  styleUrl: './public-invoice-summary.scss',
})
export class PublicInvoiceSummary {
  readonly invoice = input.required<PublicInvoice>();
  readonly logoUrl = input<string | null>(null);
  /** `false` in the paid view, where the success card replaces the invoice card. */
  readonly showCard = input(true);
  /** Name of the download in progress (`invoice`, `report`), if any. */
  readonly busy = input<string | null>(null);
  readonly downloadError = input<string | null>(null);

  readonly pdfRequested = output<void>();
  readonly reportRequested = output<void>();
  readonly photosRequested = output<void>();

  /** At 768px and above the card is a single block; below it the lines live in an accordion. */
  protected readonly wide = signal(true);

  protected readonly view = computed(() => {
    const invoice = this.invoice();
    const currency = invoice.currency;
    const completed = invoice.timeline.serviceCompletedOn;
    return {
      chip: statusChip(invoice),
      steps: timelineSteps(invoice),
      subtitle:
        completed === null
          ? invoice.service.title
          : `${invoice.service.title} · Completed ${formatCalendarDate(completed)}`,
      initials: organizationInitials(invoice.organization.name),
      balance: money(invoice.balanceDue, currency),
      dueDate: formatCalendarDate(invoice.dueDate),
      due: dueText(invoice),
      lines: invoice.lines.map((line) => ({
        description: line.description,
        detail: line.detail,
        quantity: quantityText(line.quantity, line.unit),
        rate: money(line.unitPrice, currency),
        amount: money(line.amount, currency),
      })),
      subtotal: money(invoice.totals.subtotal, currency),
      discount:
        invoice.totals.discountTotal > 0 ? money(invoice.totals.discountTotal, currency) : null,
      tax: money(invoice.totals.taxTotal, currency),
      total: money(invoice.totals.total, currency),
    };
  });

  constructor() {
    const media = watchMedia(MD_QUERY, (matches) => this.wide.set(matches));
    this.wide.set(media.matches);
    inject(DestroyRef).onDestroy(media.stop);
  }
}
