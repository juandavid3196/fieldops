import { Component, computed, input, output } from '@angular/core';
import { Tag } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

import { CatalogRow, CatalogSort, CatalogSortKey } from '../../models/catalog.model';
import { MoneyFormat, marginText } from '../../utils/catalog-format';

export const MARGIN_TOOLTIP = '(Unit price – unit cost) ÷ unit price';

interface Column {
  readonly key: CatalogSortKey | null;
  readonly label: string;
  readonly cardHidden?: boolean;
}

const COLUMNS: readonly Column[] = [
  { key: 'name', label: 'Name' },
  { key: 'type', label: 'Type' },
  { key: null, label: 'Description', cardHidden: true },
  { key: 'unitCost', label: 'Unit cost', cardHidden: true },
  { key: 'unitPrice', label: 'Unit price' },
  { key: null, label: 'Estimated margin' },
  { key: 'taxable', label: 'Taxable' },
  { key: 'status', label: 'Status' },
  { key: null, label: 'Actions' },
];

/**
 * Catalog table (FR-04): nine columns from md, item cards below. Presentational: the page
 * owns data, sorting and the row menu.
 */
@Component({
  selector: 'app-catalog-table',
  imports: [Tag, TooltipModule],
  templateUrl: './catalog-table.html',
  styleUrl: './catalog-table.scss',
})
export class CatalogTable {
  readonly rows = input.required<readonly CatalogRow[]>();
  readonly sort = input<CatalogSort>('name');
  readonly money = input.required<MoneyFormat>();
  /** Row whose mutation is in flight (its menu is locked). */
  readonly busyId = input<string | null>(null);

  readonly sortRequested = output<CatalogSortKey>();
  readonly menuRequested = output<{ event: Event; row: CatalogRow }>();

  readonly columns = COLUMNS;
  readonly marginTooltip = MARGIN_TOOLTIP;
  readonly marginText = (row: CatalogRow): string => marginText(row.estimatedMarginPercent);

  readonly sortKey = computed(() => this.sort().replace(/^-/, ''));
  readonly descending = computed(() => this.sort().startsWith('-'));

  ariaSort(column: Column): 'ascending' | 'descending' | 'none' | null {
    if (column.key === null) {
      return null;
    }
    if (this.sortKey() !== column.key) {
      return 'none';
    }
    return this.descending() ? 'descending' : 'ascending';
  }
}
