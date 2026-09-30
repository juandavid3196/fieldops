import { Component, ElementRef, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';

import {
  CatalogSummary,
  CatalogType,
  StatusFilter,
  TaxStatusFilter,
} from '../../models/catalog.model';
import { METRICS_ERROR_TOOLTIP } from '../catalog-metrics/catalog-metrics';

interface Option<T> {
  readonly code: T;
  readonly label: string;
}

interface Tab {
  readonly type: CatalogType | null;
  readonly label: string;
  readonly count: 'allItems' | 'services' | 'products';
}

const TABS: readonly Tab[] = [
  { type: null, label: 'All items', count: 'allItems' },
  { type: 'service', label: 'Services', count: 'services' },
  { type: 'product', label: 'Products', count: 'products' },
];

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const TYPE_OPTIONS: Option<CatalogType | null>[] = [
  { code: null, label: 'All types' },
  { code: 'service', label: 'Service' },
  { code: 'product', label: 'Product' },
];
const TAX_OPTIONS: Option<TaxStatusFilter | null>[] = [
  { code: null, label: 'All' },
  { code: 'taxable', label: 'Taxable' },
  { code: 'non_taxable', label: 'Non-taxable' },
];
const STATUS_OPTIONS: Option<StatusFilter | null>[] = [
  { code: null, label: 'All' },
  { code: 'active', label: 'Active' },
  { code: 'inactive', label: 'Inactive' },
];

/**
 * Tabs, search and Type/Tax status/Status filters (FR-04). Tabs and the Type filter are one
 * state owned by the page (BR-06); this component is presentational.
 */
@Component({
  selector: 'app-catalog-toolbar',
  imports: [FormsModule, IconField, InputIcon, InputText, Select],
  templateUrl: './catalog-toolbar.html',
  styleUrl: './catalog-toolbar.scss',
})
export class CatalogToolbar {
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly summary = input<CatalogSummary | null>(null);
  readonly summaryFailed = input(false);
  readonly type = input<CatalogType | null>(null);
  readonly searchText = input.required<string>();
  readonly taxStatus = input<TaxStatusFilter | null>(null);
  readonly status = input<StatusFilter | null>(null);

  readonly typeChange = output<CatalogType | null>();
  readonly searchTextChange = output<string>();
  readonly taxStatusChange = output<TaxStatusFilter | null>();
  readonly statusChange = output<StatusFilter | null>();

  readonly tabs = TABS;
  readonly typeOptions = TYPE_OPTIONS;
  readonly taxOptions = TAX_OPTIONS;
  readonly statusOptions = STATUS_OPTIONS;
  readonly errorTooltip = METRICS_ERROR_TOOLTIP;

  count(tab: Tab): string {
    const summary = this.summary();
    return this.summaryFailed() || summary === null ? '—' : String(summary[tab.count]);
  }

  /** Arrow keys move the selection between tabs (roving tabindex). */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0) {
      return;
    }
    event.preventDefault();
    const next = (index + step + TABS.length) % TABS.length;
    this.typeChange.emit(TABS[next].type);
    queueMicrotask(() =>
      this.host.querySelectorAll<HTMLElement>('[role="tab"]').item(next)?.focus(),
    );
  }
}
