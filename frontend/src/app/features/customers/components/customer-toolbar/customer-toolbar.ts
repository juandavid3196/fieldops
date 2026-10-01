import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';

import {
  BalanceStatus,
  CustomerSort,
  CustomerTab,
  CustomerTag,
  CustomerType,
  TabCounts,
} from '../../models/customer.model';

interface Option<T> {
  readonly code: T;
  readonly label: string;
}

interface Tab {
  readonly code: CustomerTab;
  readonly label: string;
}

const TABS: readonly Tab[] = [
  { code: 'all', label: 'All customers' },
  { code: 'leads', label: 'Leads' },
  { code: 'active', label: 'Active' },
  { code: 'archived', label: 'Archived' },
];

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const TYPE_OPTIONS: Option<'all' | CustomerType>[] = [
  { code: 'all', label: 'All types' },
  { code: 'residential', label: 'Residential' },
  { code: 'commercial', label: 'Commercial' },
];
const BALANCE_OPTIONS: Option<BalanceStatus>[] = [
  { code: 'any', label: 'Any balance' },
  { code: 'none', label: 'No balance' },
  { code: 'has_balance', label: 'Has balance' },
  { code: 'overdue', label: 'Overdue' },
];
const SORT_OPTIONS: Option<CustomerSort>[] = [
  { code: 'last_activity', label: 'Last activity' },
  { code: 'name_asc', label: 'Name A–Z' },
  { code: 'name_desc', label: 'Name Z–A' },
  { code: 'newest', label: 'Newest' },
  { code: 'balance_desc', label: 'Balance high–low' },
];

/** Tabs with counts, search and the Customer type / Branch / Tags / Balance status / Sort controls (BR-06 to BR-08). */
@Component({
  selector: 'app-customer-toolbar',
  imports: [FormsModule, IconField, InputIcon, InputText, MultiSelect, Select],
  templateUrl: './customer-toolbar.html',
  styleUrl: './customer-toolbar.scss',
})
export class CustomerToolbar {
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly tab = input.required<CustomerTab>();
  readonly counts = input<TabCounts | null>(null);
  readonly searchText = input.required<string>();
  readonly type = input<CustomerType | null>(null);
  readonly branchId = input<string | null>(null);
  readonly branches = input<readonly { readonly id: string; readonly name: string }[]>([]);
  readonly tags = input<readonly CustomerTag[]>([]);
  readonly tagIds = input<readonly string[]>([]);
  readonly balanceStatus = input<BalanceStatus>('any');
  readonly sort = input<CustomerSort>('last_activity');

  readonly tabChange = output<CustomerTab>();
  readonly searchTextChange = output<string>();
  readonly typeChange = output<CustomerType | null>();
  readonly branchChange = output<string | null>();
  readonly tagIdsChange = output<string[]>();
  readonly balanceChange = output<BalanceStatus>();
  readonly sortChange = output<CustomerSort>();

  readonly tabs = TABS;
  readonly typeOptions = TYPE_OPTIONS;
  readonly balanceOptions = BALANCE_OPTIONS;
  readonly sortOptions = SORT_OPTIONS;

  readonly branchOptions = computed<Option<string>[]>(() => [
    { code: 'all', label: 'All branches' },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly tagOptions = computed<CustomerTag[]>(() => [...this.tags()]);

  count(tab: Tab): string {
    const counts = this.counts();
    return counts === null ? '—' : counts[tab.code].toLocaleString('en-US');
  }

  /** Arrow keys move the selection between tabs (roving tabindex). */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0) {
      return;
    }
    event.preventDefault();
    const next = (index + step + TABS.length) % TABS.length;
    this.tabChange.emit(TABS[next].code);
    queueMicrotask(() =>
      this.host.querySelectorAll<HTMLElement>('[role="tab"]').item(next)?.focus(),
    );
  }
}
