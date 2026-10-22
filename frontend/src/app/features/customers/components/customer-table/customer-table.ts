import { Component, computed, input, output } from '@angular/core';
import { Tag } from 'primeng/tag';
import { TranslocoPipe } from '@jsverse/transloco';

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

import { localizedList, t } from '../../../../core/i18n/translate';
export const NO_SERVICE_MESSAGE = (): string => t('customers.customerTable.noServiceMessage');
export const NO_NEXT_SERVICE_MESSAGE = (): string =>
  t('customers.customerTable.noNextServiceMessage');

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

const COLUMNS: readonly string[] = localizedList(() => [
  t('common.type'),
  t('customers.customerTable.contactInformation'),
  t('customers.customerTable.properties'),
  t('customers.customerTable.lastService'),
  t('customers.customerTable.nextService'),
  t('customers.customerTable.balance'),
  t('common.status'),
]);

/**
 * Customers table (BR-09). Presentational: the page owns data, sorting and the row menu. Below
 * 768px it scrolls horizontally inside its card.
 */
@Component({
  selector: 'app-customer-table',
  imports: [TranslocoPipe, Tag],
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
  get noService(): string {
    return NO_SERVICE_MESSAGE();
  }
  get noNextService(): string {
    return NO_NEXT_SERVICE_MESSAGE();
  }

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
