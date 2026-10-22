import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import {
  IDLE_REGION,
  RELATED_PAGE_SIZES,
  RegionState,
  RelatedKind,
  RelatedResponse,
} from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import { formatMoney } from '../../utils/customer-detail-format';
import { TAB_CONFIG } from './related-list.config';

import { t } from '../../../../core/i18n/translate';

const SEARCH_DEBOUNCE_MS = 300;
const DEFAULT_PAGE_SIZE = 10;
/** Failures the page resolves itself (session expiry, forbidden or unavailable customer). */
const PAGE_LEVEL_FAILURES: readonly string[] = ['unauthorized', 'forbidden', 'not-found'];

const NOUN_KEYS: Readonly<Record<RelatedKind, string>> = {
  requests: 'customers.customerDetail.nounRequests',
  quotes: 'customers.customerDetail.nounQuotes',
  jobs: 'customers.customerDetail.nounJobs',
  invoices: 'customers.customerDetail.nounInvoices',
};
const MODULE_KEYS: Readonly<Record<RelatedKind, string>> = {
  requests: 'customers.customerDetail.requests',
  quotes: 'customers.customerDetail.quotes',
  jobs: 'customers.customerDetail.jobs',
  invoices: 'customers.customerDetail.invoices',
};

/**
 * One customer related list (Requests, Quotes, Jobs or Invoices; BR-04 to BR-08). Owns its search,
 * status, page size, page and load state; the page keeps it mounted so they survive tab switches.
 */
@Component({
  selector: 'app-related-list-tab',
  imports: [
    TranslocoPipe,
    RouterLink,
    FormsModule,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Message,
    Paginator,
    Select,
    Skeleton,
  ],
  templateUrl: './related-list-tab.html',
  styleUrl: './related-list-tab.scss',
})
export class RelatedListTab {
  private readonly customers = inject(CustomersService);
  private readonly destroyRef = inject(DestroyRef);

  readonly kind = input.required<RelatedKind>();
  readonly customerId = input.required<string>();
  /** New request is offered (BR-09); only the Requests tab renders it. */
  readonly canCreate = input(false);

  readonly newRequest = output<void>();
  /** A 401/403/404 the page resolves (`handleFailure`). */
  readonly failed = output<unknown>();

  readonly pageSizes = [...RELATED_PAGE_SIZES];
  readonly skeletonRows = [0, 1, 2, 3];

  readonly search = signal('');
  private readonly appliedSearch = signal('');
  readonly status = signal('all');
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly page = signal(1);
  readonly state = signal<RegionState<RelatedResponse>>(IDLE_REGION);
  /** Counts and summary of the last response, kept while a new page loads. */
  private readonly last = signal<RelatedResponse | null>(null);

  private readonly searchTerms = new Subject<string>();
  private subscription: Subscription | null = null;

  private readonly config = computed(() => TAB_CONFIG[this.kind()]);
  readonly columns = computed(() => this.config().columns);
  readonly moduleLabel = computed(() => t(MODULE_KEYS[this.kind()]));
  readonly noun = computed(() => t(NOUN_KEYS[this.kind()]));
  readonly heading = computed(() => {
    const total = this.last()?.customerTotal;
    return total === undefined ? this.moduleLabel() : `${this.moduleLabel()} (${total})`;
  });
  readonly hasFilters = computed(() => this.appliedSearch() !== '' || this.status() !== 'all');
  readonly statusOptions = computed(() => [
    { code: 'all', label: t('customers.relatedList.allStatuses') },
    ...this.config().statuses(),
  ]);

  readonly rows = computed(() => {
    const data = this.state().data;
    if (data === null) {
      return [];
    }
    const context = { timezone: data.timezone, currency: 'currency' in data ? data.currency : '' };
    return data.items.map((item) => this.config().toRow(item, context));
  });
  readonly headings = computed(() => this.rows()[0]?.cells.map((cell) => cell.label) ?? []);
  readonly summary = computed(() => {
    const data = this.last();
    if (data === null || !('summary' in data)) {
      return null;
    }
    return [
      { label: t('customers.relatedList.outstanding'), value: data.summary.outstanding },
      { label: t('customers.relatedList.overdue'), value: data.summary.overdue },
      { label: t('customers.relatedList.paid'), value: data.summary.paid },
    ].map((entry) => ({ label: entry.label, value: formatMoney(entry.value, data.currency) }));
  });
  readonly showing = computed(() => {
    const total = this.state().data?.totalCount ?? 0;
    const first = (this.page() - 1) * this.pageSize() + 1;
    return t('customers.relatedList.showing', {
      first,
      last: Math.min(this.page() * this.pageSize(), total),
      total,
      noun: this.noun(),
    });
  });

  constructor() {
    // The first run loads; a different customer starts over with default filters.
    effect(() => {
      this.customerId();
      untracked(() => this.reset());
    });
    this.searchTerms
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe((term) => {
        const applied = term.trim();
        if (applied !== this.appliedSearch()) {
          this.appliedSearch.set(applied);
          this.page.set(1);
          this.load();
        }
      });
    this.destroyRef.onDestroy(() => this.subscription?.unsubscribe());
  }

  private reset(): void {
    this.search.set('');
    this.appliedSearch.set('');
    this.status.set('all');
    this.pageSize.set(DEFAULT_PAGE_SIZE);
    this.page.set(1);
    this.last.set(null);
    this.load();
  }

  /** Loads the current filters; also used by Retry and by the page to refresh after a change. */
  load(): void {
    this.subscription?.unsubscribe();
    this.state.set({ status: 'loading', data: null });
    this.subscription = this.customers
      .relatedList(this.customerId(), this.kind(), {
        search: this.appliedSearch(),
        status: this.status() === 'all' ? null : this.status(),
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (data) => {
          this.last.set(data);
          this.state.set({ status: 'ready', data });
        },
        error: (error: unknown) => {
          if (isApiError(error) && PAGE_LEVEL_FAILURES.includes(error.kind)) {
            this.failed.emit(error);
          } else {
            this.state.set({ status: 'error', data: null });
          }
        },
      });
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.searchTerms.next(value);
  }

  onStatus(code: string): void {
    this.status.set(code);
    this.page.set(1);
    this.load();
  }

  onPageChange(event: PaginatorState): void {
    const rows = event.rows ?? this.pageSize();
    const page = rows === this.pageSize() ? (event.page ?? 0) + 1 : 1;
    if (rows === this.pageSize() && page === this.page()) {
      return;
    }
    this.pageSize.set(rows);
    this.page.set(page);
    this.load();
  }

  clearFilters(): void {
    this.search.set('');
    this.appliedSearch.set('');
    this.status.set('all');
    this.page.set(1);
    this.load();
  }
}
