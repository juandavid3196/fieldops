import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  filter,
  map,
  of,
  switchMap,
  tap,
} from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import {
  BranchOption,
  CUSTOMER_UNAVAILABLE_MESSAGE,
  CustomerDrawer,
  CustomerDrawerMode,
} from '../../components/customer-drawer/customer-drawer';
import { CustomerMetricsCards } from '../../components/customer-metrics/customer-metrics';
import { CustomerTable } from '../../components/customer-table/customer-table';
import { CustomerToolbar } from '../../components/customer-toolbar/customer-toolbar';
import { ImportDialog } from '../../components/import-dialog/import-dialog';
import {
  BalanceStatus,
  CustomerFilters,
  CustomerListQuery,
  CustomerListResponse,
  CustomerMetrics,
  CustomerRow,
  CustomerSort,
  CustomerTab,
  CustomerTag,
  CustomerType,
  PAGE_SIZE,
  TabCounts,
} from '../../models/customer.model';
import { CustomersService } from '../../services/customers.service';
import { pageItems } from '../../utils/customer-format';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';

export const FORBIDDEN_MESSAGE = "You don't have access to customers.";
export const DESCRIPTION_MESSAGE =
  'Manage customer contact information, properties, and service history.';
export const LIST_ERROR_MESSAGE = "We couldn't load customers.";
export const EMPTY_INITIAL_TITLE = 'No customers yet';
export const EMPTY_INITIAL_MESSAGE = 'Add your first customer or import a CSV.';
export const EMPTY_FILTERED_MESSAGE = 'No customers match your filters.';
export const EMPTY_ARCHIVED_MESSAGE = 'No archived customers.';
export const UPDATE_FAILED_MESSAGE = "We couldn't update the customer. Try again.";
export const ALREADY_ARCHIVED_MESSAGE = 'This customer is already archived.';
export const ALREADY_ACTIVE_MESSAGE = 'This customer is already active.';
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];
const MUTATE_ROLES: readonly string[] = ['owner', 'dispatcher'];
const READ_ROLES: readonly string[] = ['operations_manager', 'accounting', 'viewer'];

type ListResult =
  | { readonly ok: true; readonly response: CustomerListResponse }
  | { readonly ok: false; readonly error: ApiError | null };

/**
 * Customers page (`/customers`): metrics, tabs, filterable server-paged table, customer drawer,
 * archive/reactivate and CSV import. Single owner of the page state; Owner/Dispatcher mutate,
 * read roles view, any other role sees the forbidden state with no data requests (BR-18).
 */
@Component({
  selector: 'app-customers',
  imports: [
    ConfirmDialog,
    DiscardChangesDialog,
    ButtonDirective,
    Menu,
    Message,
    Skeleton,
    Toast,
    CustomerDrawer,
    CustomerMetricsCards,
    CustomerTable,
    CustomerToolbar,
    ImportDialog,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './customers.html',
  styleUrl: './customers.scss',
})
export class Customers {
  private readonly customers = inject(CustomersService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly drawer = viewChild(CustomerDrawer);
  private readonly menu = viewChild.required(Menu);

  private readonly listRequests = new Subject<CustomerListQuery>();
  private readonly searchInput = new Subject<string>();

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly listErrorMessage = LIST_ERROR_MESSAGE;
  readonly emptyInitialTitle = EMPTY_INITIAL_TITLE;
  readonly emptyInitialMessage = EMPTY_INITIAL_MESSAGE;
  readonly emptyFilteredMessage = EMPTY_FILTERED_MESSAGE;
  readonly emptyArchivedMessage = EMPTY_ARCHIVED_MESSAGE;
  readonly pageSize = PAGE_SIZE;
  readonly skeletonRows = SKELETON_ROWS;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  readonly canMutate = computed(() => MUTATE_ROLES.includes(this.roleCode()));
  readonly canImport = computed(() => this.roleCode() === 'owner');
  readonly isReadOnly = computed(() => READ_ROLES.includes(this.roleCode()));
  private readonly serverForbidden = signal(false);
  readonly forbidden = computed(
    () => !(this.canMutate() || this.isReadOnly()) || this.serverForbidden(),
  );

  // Filters and paging (API query only, not the URL).
  readonly tab = signal<CustomerTab>('all');
  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly type = signal<CustomerType | null>(null);
  readonly branchId = signal<string | null>(null);
  readonly tagIds = signal<readonly string[]>([]);
  readonly balanceStatus = signal<BalanceStatus>('any');
  readonly sort = signal<CustomerSort>('last_activity');
  readonly page = signal(1);
  readonly hasFilters = computed(
    () =>
      this.searchText().trim().length > 0 ||
      this.type() !== null ||
      this.branchId() !== null ||
      this.tagIds().length > 0 ||
      this.balanceStatus() !== 'any',
  );
  private readonly filters = computed<CustomerFilters>(() => ({
    tab: this.tab(),
    search: this.searchTerm(),
    type: this.type(),
    branchId: this.branchId(),
    tagIds: this.tagIds(),
    balanceStatus: this.balanceStatus(),
    sort: this.sort(),
  }));

  readonly rows = signal<readonly CustomerRow[]>([]);
  readonly totalCount = signal(0);
  readonly tabCounts = signal<TabCounts | null>(null);
  readonly currency = signal<string | null>(null);
  readonly timezone = signal('UTC');
  readonly loading = signal(true);
  readonly listError = signal<ApiError | null>(null);
  readonly first = computed(() => (this.page() - 1) * PAGE_SIZE);
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  readonly pages = computed(() => pageItems(this.page(), this.totalPages()));
  readonly report = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : this.first() + 1;
    const to = Math.min(this.first() + PAGE_SIZE, total);
    return `Showing ${from} – ${to} of ${total.toLocaleString('en-US')} customers`;
  });
  readonly emptyKind = computed<'filtered' | 'archived' | 'tab' | 'initial'>(() =>
    this.hasFilters()
      ? 'filtered'
      : this.tab() === 'archived'
        ? 'archived'
        : this.tab() === 'all'
          ? 'initial'
          : 'tab',
  );

  readonly metrics = signal<CustomerMetrics | null>(null);
  readonly metricsLoading = signal(true);
  readonly metricsFailed = signal(false);

  readonly branches = signal<readonly BranchOption[]>([]);
  private readonly branchCountry = signal<string | null>(null);
  readonly countryCode = this.branchCountry.asReadonly();
  readonly tags = signal<readonly CustomerTag[]>([]);
  readonly tagsLoading = signal(false);
  private readonly tagsFailed = signal(false);

  readonly drawerOpen = signal(false);
  readonly drawerMode = signal<CustomerDrawerMode>('create');
  readonly drawerCustomerId = signal<string | null>(null);
  readonly mutatingId = signal<string | null>(null);
  readonly menuModel = signal<MenuItem[]>([]);
  readonly importOpen = signal(false);
  readonly sessionExpired = signal(false);

  constructor() {
    this.listRequests
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.listError.set(null);
        }),
        switchMap((query) =>
          this.customers.list(query).pipe(
            map((response): ListResult => ({ ok: true, response })),
            catchError((error: unknown) =>
              of<ListResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleListResult(result));

    this.searchInput
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        map((value) => value.trim()),
        filter((value) => value !== this.searchTerm()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((value) => {
        this.searchTerm.set(value);
        this.filtersChanged();
      });

    if (!this.forbidden()) {
      this.loadList();
      this.loadMetrics();
      this.loadBranches();
      this.loadTags();
    } else {
      this.loading.set(false);
    }
  }

  // Data loading

  loadList(): void {
    this.listRequests.next({ ...this.filters(), page: this.page() });
  }

  private handleListResult(result: ListResult): void {
    if (result.ok) {
      const { items, totalCount, tabCounts, currency, timezone } = result.response;
      if (items.length === 0 && totalCount > 0 && this.page() > 1) {
        this.page.set(Math.max(1, Math.ceil(totalCount / PAGE_SIZE)));
        this.loadList();
        return;
      }
      this.loading.set(false);
      this.rows.set(items);
      this.totalCount.set(totalCount);
      this.tabCounts.set(tabCounts);
      this.currency.set(currency);
      this.timezone.set(timezone);
      return;
    }
    this.loading.set(false);
    if (result.error?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (result.error?.kind === 'forbidden') {
      this.serverForbidden.set(true);
      return;
    }
    this.listError.set(
      result.error ?? { kind: 'unknown', status: 0, message: LIST_ERROR_MESSAGE, fieldErrors: {} },
    );
  }

  loadMetrics(): void {
    this.metricsFailed.set(false);
    if (this.metrics() === null) {
      this.metricsLoading.set(true);
    }
    this.customers
      .metrics(this.branchId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (metrics) => {
          this.metricsLoading.set(false);
          this.metrics.set(metrics);
        },
        error: (error: unknown) => {
          this.metricsLoading.set(false);
          this.metricsFailed.set(true);
          this.handleSecondaryFailure(error);
        },
      });
  }

  private loadBranches(): void {
    this.customers
      .branchOptions()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.branches.set(
            response.branches.map((branch) => ({ id: branch.id, name: branch.name })),
          );
          this.branchCountry.set(response.countryCode);
        },
        error: (error: unknown) => this.handleSecondaryFailure(error),
      });
  }

  private loadTags(): void {
    this.tagsLoading.set(true);
    this.tagsFailed.set(false);
    this.customers
      .tags()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tags) => {
          this.tagsLoading.set(false);
          this.tags.set(tags);
        },
        error: (error: unknown) => {
          this.tagsLoading.set(false);
          this.tagsFailed.set(true);
          this.handleSecondaryFailure(error);
        },
      });
  }

  private handleSecondaryFailure(error: unknown): void {
    if (isApiError(error) && error.kind === 'unauthorized') {
      this.onUnauthorized();
    }
  }

  private refresh(): void {
    this.loadList();
    this.loadMetrics();
  }

  // Filters, sorting, paging (BR-06 to BR-08)

  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  onTab(tab: CustomerTab): void {
    this.tab.set(tab);
    this.filtersChanged();
  }

  onType(type: CustomerType | null): void {
    this.type.set(type);
    this.filtersChanged();
  }

  /** The Branch filter also scopes the metrics. */
  onBranch(branchId: string | null): void {
    this.branchId.set(branchId);
    this.loadMetrics();
    this.filtersChanged();
  }

  onTags(tagIds: readonly string[]): void {
    this.tagIds.set(tagIds);
    this.filtersChanged();
  }

  onBalance(value: BalanceStatus): void {
    this.balanceStatus.set(value);
    this.filtersChanged();
  }

  onSort(sort: CustomerSort): void {
    this.sort.set(sort);
    this.filtersChanged();
  }

  /** The Name header toggles Name A–Z / Z–A. */
  toggleNameSort(): void {
    this.onSort(this.sort() === 'name_asc' ? 'name_desc' : 'name_asc');
  }

  clearFilters(): void {
    const branchChanged = this.branchId() !== null;
    this.searchText.set('');
    this.searchTerm.set('');
    this.type.set(null);
    this.branchId.set(null);
    this.tagIds.set([]);
    this.balanceStatus.set('any');
    if (branchChanged) {
      this.loadMetrics();
    }
    this.filtersChanged();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) {
      return;
    }
    this.page.set(page);
    this.loadList();
  }

  private filtersChanged(): void {
    this.page.set(1);
    this.loadList();
  }

  // Drawer

  openCreate(): void {
    this.showDrawer('create', null);
  }

  /** Switching the open drawer while it has unsaved edits asks first. */
  private showDrawer(mode: CustomerDrawerMode, id: string | null): void {
    const show = () => {
      if (this.tagsFailed()) {
        this.loadTags();
      }
      this.drawerMode.set(mode);
      this.drawerCustomerId.set(id);
      this.drawerOpen.set(true);
    };
    if (this.drawerOpen() && (this.drawer()?.isDirty() ?? false)) {
      this.confirmDiscard().subscribe((leave) => {
        if (leave) {
          show();
        }
      });
      return;
    }
    show();
  }

  private openCustomer(id: string): void {
    this.showDrawer(this.canMutate() ? 'edit' : 'view', id);
  }

  /** "View existing customer": the drawer already confirmed any discard. */
  onOpenExisting(id: string): void {
    this.drawerMode.set(this.canMutate() ? 'edit' : 'view');
    this.drawerCustomerId.set(id);
    this.drawerOpen.set(true);
  }

  onNameClicked(row: CustomerRow): void {
    this.openCustomer(row.id);
  }

  onDrawerClosed(): void {
    this.drawerOpen.set(false);
  }

  onDrawerSaved(): void {
    this.refresh();
  }

  onTagCreated(tag: CustomerTag): void {
    if (!this.tags().some((existing) => existing.id === tag.id)) {
      this.tags.update((tags) =>
        [...tags, tag].sort((a, b) =>
          a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
        ),
      );
    }
  }

  onCustomerUnavailable(): void {
    this.messageService.add({ severity: 'error', summary: CUSTOMER_UNAVAILABLE_MESSAGE });
    this.drawerOpen.set(false);
    this.refresh();
  }

  /** Consulted by `customersUnsavedChangesGuard` on route leave. */
  canLeave(): boolean | Observable<boolean> {
    if (this.sessionExpired()) {
      return true;
    }
    return this.drawerOpen() && (this.drawer()?.isDirty() ?? false) ? this.confirmDiscard() : true;
  }

  private confirmDiscard(): Observable<boolean> {
    return new Observable<boolean>((subscriber) => {
      this.confirmationService.confirm(
        discardChangesConfirmation({
          subject: 'this customer',
          accept: () => {
            subscriber.next(true);
            subscriber.complete();
          },
          reject: () => {
            subscriber.next(false);
            subscriber.complete();
          },
        }),
      );
    });
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  // Row actions (BR-16)

  openMenu(event: Event, row: CustomerRow): void {
    this.menuModel.set(this.buildMenu(row));
    this.menu().toggle(event);
  }

  buildMenu(row: CustomerRow): MenuItem[] {
    if (!this.canMutate()) {
      return [{ label: 'View', icon: 'pi pi-eye', command: () => this.showDrawer('view', row.id) }];
    }
    const archived = row.displayStatus === 'archived';
    return [
      { label: 'Edit', icon: 'pi pi-pencil', command: () => this.showDrawer('edit', row.id) },
      archived
        ? {
            label: 'Reactivate',
            icon: 'pi pi-replay',
            disabled: this.mutatingId() !== null,
            command: () => this.reactivate(row),
          }
        : {
            label: 'Archive',
            icon: 'pi pi-box',
            disabled: this.mutatingId() !== null,
            command: () => this.confirmArchive(row),
          },
    ];
  }

  private confirmArchive(row: CustomerRow): void {
    this.confirmationService.confirm({
      header: `Archive ${row.displayName}?`,
      message:
        'Archived customers are hidden from active lists. Their contacts, properties and history are kept.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Archive', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () =>
        this.mutate(
          row,
          this.customers.archive(row.id),
          'Customer archived.',
          ALREADY_ARCHIVED_MESSAGE,
        ),
    });
  }

  private reactivate(row: CustomerRow): void {
    this.mutate(
      row,
      this.customers.reactivate(row.id),
      'Customer reactivated.',
      ALREADY_ACTIVE_MESSAGE,
    );
  }

  private mutate(
    row: CustomerRow,
    request$: Observable<unknown>,
    success: string,
    conflictMessage: string,
  ): void {
    if (this.mutatingId() !== null) {
      return;
    }
    this.mutatingId.set(row.id);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.mutatingId.set(null);
        this.messageService.add({ severity: 'success', summary: success });
        this.refresh();
      },
      error: (error: unknown) => {
        this.mutatingId.set(null);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.onUnauthorized();
          return;
        }
        if (kind === 'conflict') {
          this.messageService.add({ severity: 'error', summary: conflictMessage });
          this.refresh();
        } else if (kind === 'not-found') {
          this.messageService.add({ severity: 'error', summary: CUSTOMER_UNAVAILABLE_MESSAGE });
          this.refresh();
        } else {
          this.messageService.add({ severity: 'error', summary: UPDATE_FAILED_MESSAGE });
        }
      },
    });
  }

  // Import (BR-17)

  openImport(): void {
    this.importOpen.set(true);
  }

  onImported(count: number): void {
    this.messageService.add({
      severity: 'success',
      summary: `${count.toLocaleString('en-US')} ${count === 1 ? 'customer' : 'customers'} imported.`,
    });
    this.loadTags();
    this.refresh();
  }
}
