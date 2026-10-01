import { Component, computed, input, output } from '@angular/core';
import { Tag } from 'primeng/tag';

import { CustomerRow, CustomerSort } from '../../models/customer.model';
import {
  STATUS_LABELS,
  formatDate,
  formatPhone,
  formatTime,
  moneyFormat,
  propertyCountText,
  statusSeverity,
} from '../../utils/customer-format';

export const NO_SERVICE_MESSAGE = 'No completed service';
export const NO_NEXT_SERVICE_MESSAGE = 'None';

interface RowView {
  readonly row: CustomerRow;
  readonly contact: readonly string[];
  readonly properties: string;
  readonly lastDate: string | null;
  readonly lastSummary: string | null;
  readonly nextDate: string | null;
  readonly nextTime: string | null;
  readonly balance: string;
  readonly statusLabel: string;
  readonly severity: 'success' | 'danger' | 'secondary';
}

const COLUMNS: readonly string[] = [
  'Type',
  'Contact information',
  'Properties',
  'Last service',
  'Next service',
  'Balance',
  'Status',
];

/**
 * Customers table (BR-09). Presentational: the page owns data, sorting and the row menu. Below
 * 768px it scrolls horizontally inside its card.
 */
@Component({
  selector: 'app-customer-table',
  imports: [Tag],
  templateUrl: './customer-table.html',
  styleUrl: './customer-table.scss',
})
export class CustomerTable {
  readonly rows = input.required<readonly CustomerRow[]>();
  readonly sort = input<CustomerSort>('last_activity');
  readonly currency = input<string | null>(null);
  readonly timezone = input('UTC');
  /** Row whose mutation is in flight (its menu is locked). */
  readonly busyId = input<string | null>(null);

  readonly sortRequested = output<void>();
  readonly nameClicked = output<CustomerRow>();
  readonly menuRequested = output<{ event: Event; row: CustomerRow }>();

  readonly columns = COLUMNS;
  readonly noService = NO_SERVICE_MESSAGE;
  readonly noNextService = NO_NEXT_SERVICE_MESSAGE;

  readonly ariaSort = computed<'ascending' | 'descending' | 'none'>(() =>
    this.sort() === 'name_asc' ? 'ascending' : this.sort() === 'name_desc' ? 'descending' : 'none',
  );

  readonly views = computed<readonly RowView[]>(() => {
    const money = moneyFormat(this.currency());
    const zone = this.timezone();
    return this.rows().map((row): RowView => {
      const status = row.displayStatus;
      return {
        row,
        contact: [row.primaryEmail ?? '', formatPhone(row.primaryPhone)].filter(
          (line) => line.length > 0,
        ),
        properties: propertyCountText(row.propertyCount),
        lastDate: row.lastService ? formatDate(row.lastService.completedAt, zone) : null,
        lastSummary: row.lastService?.summary?.split(/\r?\n/)[0]?.trim() || null,
        nextDate: row.nextService ? formatDate(row.nextService.startsAt, zone) : null,
        nextTime: row.nextService ? formatTime(row.nextService.startsAt, zone) : null,
        balance: money.format(row.balance),
        statusLabel: STATUS_LABELS[status] ?? status,
        severity: statusSeverity(status),
      };
    });
  });
}
