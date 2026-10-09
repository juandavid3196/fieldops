import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import { QueueResponse } from '../../../billing-review/models/billing-review.model';
import { READY_EMPTY_MESSAGE, READY_ERROR_MESSAGE, Region } from '../../models/invoices-hub.model';
import { formatInstantDate, money } from '../../utils/invoices-hub-format';
import { HubPager } from '../hub-pager/hub-pager';

/** Ready tab list (BR-23): completed jobs ready to invoice from the review queue. */
@Component({
  selector: 'app-ready-table',
  imports: [ButtonDirective, HubPager, RouterLink, Skeleton],
  templateUrl: './ready-table.html',
  styleUrl: '../invoices-table/invoices-table.scss',
})
export class ReadyTable {
  readonly region = input.required<Region<QueueResponse>>();
  readonly page = input.required<number>();
  readonly timezone = input('UTC');

  readonly pageChange = output<number>();
  readonly retry = output<void>();

  readonly errorMessage = READY_ERROR_MESSAGE;
  readonly emptyMessage = READY_EMPTY_MESSAGE;
  readonly rows = computed(() =>
    (this.region().data?.items ?? []).map((item) => ({
      item,
      completed: formatInstantDate(item.completedAt, this.timezone()),
      total: money(item.approvedTotal, item.currency),
    })),
  );
}
