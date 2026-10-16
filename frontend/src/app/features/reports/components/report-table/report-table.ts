import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';

import { REPORT_PAGE_SIZES, ReportRow, SortDirection } from '../../models/reports.model';
import { ReportColumn, ReportMeta } from '../../utils/report-catalog';
import {
  BELOW_TARGET_TEXT,
  CellContext,
  CellView,
  cellView,
  totalView,
} from '../../utils/report-view';

export const TABLE_EMPTY_MESSAGE = 'No rows match these filters.';

interface RowView {
  readonly id: string;
  readonly cells: readonly CellView[];
}

/** BR-07/BR-21: sortable headers (`aria-sort`), totals row of the whole set, footer and paging. */
@Component({
  selector: 'app-report-table',
  imports: [ButtonDirective, FormsModule, NgTemplateOutlet, Paginator, RouterLink, Select],
  templateUrl: './report-table.html',
  styleUrl: './report-table.scss',
})
export class ReportTable {
  readonly meta = input.required<ReportMeta>();
  readonly rows = input.required<readonly ReportRow[]>();
  readonly totals = input<Readonly<Record<string, number | string | null>>>({});
  readonly total = input.required<number>();
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly sort = input.required<string>();
  readonly direction = input.required<SortDirection>();
  readonly timezone = input.required<string>();
  readonly currency = input.required<string>();
  readonly marginTarget = input.required<number>();

  readonly sortRequested = output<string>();
  readonly pageRequested = output<number>();
  readonly pageSizeRequested = output<number>();
  readonly resetRequested = output<void>();

  readonly emptyMessage = TABLE_EMPTY_MESSAGE;
  readonly belowTarget = BELOW_TARGET_TEXT;
  readonly pageSizeOptions = REPORT_PAGE_SIZES.map((size) => ({
    code: String(size),
    label: `${size} / page`,
  }));

  private readonly context = computed<CellContext>(() => ({
    timezone: this.timezone(),
    currency: this.currency(),
    marginTarget: this.marginTarget(),
  }));
  readonly rowViews = computed<RowView[]>(() =>
    this.rows().map((row) => ({
      id: row.id,
      cells: this.meta().columns.map((column) => cellView(column, row, this.context())),
    })),
  );
  readonly totalCells = computed<(CellView | null)[]>(() =>
    this.meta().columns.map((column) => totalView(column, this.totals(), this.context())),
  );
  readonly hasTotals = computed(() => this.totalCells().some((cell) => cell !== null));
  readonly footer = computed(() => {
    const first = (this.page() - 1) * this.pageSize() + 1;
    const last = Math.min(this.page() * this.pageSize(), this.total());
    return `Showing ${first}–${last} of ${this.total()} ${this.meta().noun}`;
  });
  readonly pageSizeCode = computed(() => String(this.pageSize()));

  ariaSort(column: ReportColumn): 'ascending' | 'descending' | 'none' | null {
    if (!column.sortable) {
      return null;
    }
    return this.sort() !== column.key
      ? 'none'
      : this.direction() === 'asc'
        ? 'ascending'
        : 'descending';
  }

  isMoney(index: number): boolean {
    return this.meta().columns[index].type === 'money';
  }

  changePage(event: PaginatorState): void {
    const page = (event.page ?? 0) + 1;
    if (page !== this.page()) {
      this.pageRequested.emit(page);
    }
  }
}
