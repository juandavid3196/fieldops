import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import {
  PAYMENTS_EMPTY_MESSAGE,
  PAYMENTS_ERROR_MESSAGE,
  PAYMENTS_FILTERED_EMPTY_MESSAGE,
  PAYMENT_METHOD_LABELS,
  Page,
  PaymentRow,
  Region,
} from '../../models/invoices-hub.model';
import { formatCalendarDate, money } from '../../utils/invoices-hub-format';
import { HubPager } from '../hub-pager/hub-pager';

/** Payments tab list (BR-23, BR-24): table, footer and its own states. */
@Component({
  selector: 'app-payments-table',
  imports: [ButtonDirective, HubPager, RouterLink, Skeleton],
  templateUrl: './payments-table.html',
  styleUrl: '../invoices-table/invoices-table.scss',
})
export class PaymentsTable {
  readonly region = input.required<Region<Page<PaymentRow>>>();
  readonly page = input.required<number>();
  readonly filtered = input(false);

  readonly pageChange = output<number>();
  readonly retry = output<void>();
  readonly clearFilters = output<void>();

  readonly errorMessage = PAYMENTS_ERROR_MESSAGE;
  readonly rows = computed(() =>
    (this.region().data?.items ?? []).map((row) => ({
      row,
      date: formatCalendarDate(row.paidDate),
      method: PAYMENT_METHOD_LABELS[row.method],
      amount: money(row.amount, row.currency),
    })),
  );
  readonly emptyMessage = computed(() =>
    this.filtered() ? PAYMENTS_FILTERED_EMPTY_MESSAGE : PAYMENTS_EMPTY_MESSAGE,
  );
}
