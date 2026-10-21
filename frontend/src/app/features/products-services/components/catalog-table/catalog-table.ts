import { Component, computed, input, output } from '@angular/core';
import { Tag } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { CatalogRow, CatalogSort, CatalogSortKey } from '../../models/catalog.model';
import { MoneyFormat, marginText } from '../../utils/catalog-format';

import { localizedList, t } from '../../../../core/i18n/translate';
export const MARGIN_TOOLTIP = (): string => t('productsServices.catalogTable.marginTooltip');

interface Column {
  readonly key: CatalogSortKey | null;
  readonly label: string;
  readonly cardHidden?: boolean;
}

const COLUMNS: readonly Column[] = localizedList(() => [
  { key: 'name', label: t('common.name') },
  { key: 'type', label: t('common.type') },
  { key: null, label: t('common.description'), cardHidden: true },
  { key: 'unitCost', label: t('productsServices.catalogTable.unitCost'), cardHidden: true },
  { key: 'unitPrice', label: t('productsServices.catalogTable.unitPrice') },
  { key: null, label: t('productsServices.catalogTable.estimatedMargin') },
  { key: 'taxable', label: t('productsServices.catalogTable.taxable') },
  { key: 'status', label: t('common.status') },
  { key: null, label: t('common.actions') },
]);

/**
 * Catalog table (FR-04): nine columns from md, item cards below. Presentational: the page
 * owns data, sorting and the row menu.
 */
@Component({
  selector: 'app-catalog-table',
  imports: [TranslocoPipe, Tag, TooltipModule],
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
  get marginTooltip(): string {
    return MARGIN_TOOLTIP();
  }
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
