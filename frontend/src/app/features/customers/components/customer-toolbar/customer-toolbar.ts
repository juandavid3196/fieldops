import { Component, ElementRef, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { MultiSelect } from 'primeng/multiselect';
import { Select } from 'primeng/select';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  BalanceStatus,
  CustomerSort,
  CustomerTab,
  CustomerTag,
  CustomerType,
  TabCounts,
} from '../../models/customer.model';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
import { activeIntlLocale } from '../../../../core/i18n/active-locale';
interface Option<T> {
  readonly code: T;
  readonly label: string;
}

interface Tab {
  readonly code: CustomerTab;
  readonly label: string;
}

const TABS: readonly Tab[] = localizedList(() => [
  { code: 'all', label: t('customers.customerToolbar.allCustomers') },
  { code: 'leads', label: t('customers.customerToolbar.leads') },
  { code: 'active', label: t('common.active') },
  { code: 'archived', label: t('customers.customerToolbar.archived') },
]);

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const TYPE_OPTIONS: Option<'all' | CustomerType>[] = localizedList(() => [
  { code: 'all', label: t('customers.customerToolbar.allTypes') },
  { code: 'residential', label: t('customers.customerToolbar.residential') },
  { code: 'commercial', label: t('customers.customerToolbar.commercial') },
]);
const BALANCE_OPTIONS: Option<BalanceStatus>[] = localizedList(() => [
  { code: 'any', label: t('customers.customerToolbar.anyBalance') },
  { code: 'none', label: t('customers.customerToolbar.noBalance') },
  { code: 'has_balance', label: t('customers.customerToolbar.hasBalance') },
  { code: 'overdue', label: t('customers.customerToolbar.overdue') },
]);
const SORT_OPTIONS: Option<CustomerSort>[] = localizedList(() => [
  { code: 'last_activity', label: t('customers.customerToolbar.lastActivity') },
  { code: 'name_asc', label: t('customers.customerToolbar.nameAZ') },
  { code: 'name_desc', label: t('customers.customerToolbar.nameZA') },
  { code: 'newest', label: t('customers.customerToolbar.newest') },
  { code: 'balance_desc', label: t('customers.customerToolbar.balanceHighLow') },
]);

/** Tabs with counts, search and the Customer type / Branch / Tags / Balance status / Sort controls (BR-06 to BR-08). */
@Component({
  selector: 'app-customer-toolbar',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    MultiSelect,
    Select,
  ],
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
    { code: 'all', label: t('customers.customerToolbar.allBranches') },
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly tagOptions = computed<CustomerTag[]>(() => [...this.tags()]);

  count(tab: Tab): string {
    const counts = this.counts();
    return counts === null ? '—' : counts[tab.code].toLocaleString(activeIntlLocale());
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
