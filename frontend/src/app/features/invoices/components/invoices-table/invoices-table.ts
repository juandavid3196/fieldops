import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import {
  INVOICES_EMPTY_MESSAGE,
  INVOICES_ERROR_MESSAGE,
  INVOICES_FILTERED_EMPTY_MESSAGE,
  InvoiceRow,
  Page,
  Region,
} from '../../models/invoices-hub.model';
import {
  STATUS_CHIPS,
  formatCalendarDate,
  lastActivityText,
  money,
  statusText,
} from '../../utils/invoices-hub-format';
import { HubPager } from '../hub-pager/hub-pager';

/** Invoices tab list (BR-22, BR-24): table, row actions trigger, footer and its own states. */
@Component({
  selector: 'app-invoices-table',
  imports: [ButtonDirective, HubPager, RouterLink, Skeleton],
  templateUrl: './invoices-table.html',
  styleUrl: './invoices-table.scss',
})
export class InvoicesTable {
  readonly region = input.required<Region<Page<InvoiceRow>>>();
  readonly page = input.required<number>();
  readonly filtered = input(false);
  /** Invoice id whose PDF is downloading. */
  readonly pdfBusyId = input<string | null>(null);

  readonly pageChange = output<number>();
  readonly retry = output<void>();
  readonly clearFilters = output<void>();
  readonly openReady = output<void>();
  readonly menuRequested = output<{ event: Event; row: InvoiceRow }>();

  readonly errorMessage = INVOICES_ERROR_MESSAGE;
  readonly rows = computed(() =>
    (this.region().data?.items ?? []).map((row) => ({
      row,
      chip: STATUS_CHIPS[row.status],
      status: statusText(row),
      issueDate: formatCalendarDate(row.issueDate),
      dueDate: formatCalendarDate(row.dueDate),
      total: money(row.total, row.currency),
      balance: money(row.balanceDue, row.currency),
      activity: lastActivityText(row.lastActivity),
    })),
  );
  readonly emptyMessage = computed(() =>
    this.filtered() ? INVOICES_FILTERED_EMPTY_MESSAGE : INVOICES_EMPTY_MESSAGE,
  );
}
