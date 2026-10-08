import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { Menu } from 'primeng/menu';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
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
import { saveCsv } from '../../../../shared/utils/csv-file';
import { AdministrationNav } from '../../../organizations/components/administration-nav/administration-nav';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { CategoryDialog } from '../../components/category-dialog/category-dialog';
import { CatalogMetrics } from '../../components/catalog-metrics/catalog-metrics';
import { CatalogTable } from '../../components/catalog-table/catalog-table';
import { CatalogToolbar } from '../../components/catalog-toolbar/catalog-toolbar';
import {
  ImportDialog,
  TEMPLATE_FAILED_MESSAGE,
} from '../../components/import-dialog/import-dialog';
import {
  ITEM_UNAVAILABLE_MESSAGE,
  ItemDrawer,
  ItemDrawerMode,
} from '../../components/item-drawer/item-drawer';
import {
  CatalogCategory,
  CatalogFilters,
  CatalogListQuery,
  CatalogListResponse,
  CatalogRow,
  CatalogSort,
  CatalogSortKey,
  CatalogSummary,
  CatalogType,
  PAGE_SIZE,
  PublicRequestReadiness,
  StatusFilter,
  TaxStatusFilter,
} from '../../models/catalog.model';
import { CatalogCategoriesService } from '../../services/catalog-categories.service';
import { CatalogItemsService } from '../../services/catalog-items.service';
import { moneyFormat } from '../../utils/catalog-format';
import {
  DiscardChangesDialog,
  discardChangesConfirmation,
} from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';

export const FORBIDDEN_MESSAGE = "You don't have access to products and services.";
export const READ_ONLY_MESSAGE =
  'You have view-only access. Adding and editing items requires the Owner or Operations Manager role.';
export const DESCRIPTION_MESSAGE = 'Manage reusable line items for quotes, jobs, and invoices.';
export const LIST_ERROR_MESSAGE = "We couldn't load items. Check your connection and try again.";
export const INFO_NOTE =
  'Catalog items are defaults. Price and description may be adjusted on an individual quote or invoice without changing this list.';
export const WARNING_NOTE =
  'Discounts are added as adjustments on a quote or invoice and are not saved as products or services.';
export const EMPTY_INITIAL_MESSAGE = 'No products or services yet.';
export const EMPTY_FILTERED_MESSAGE = 'No items match these filters.';
export const EXPORT_FAILED_MESSAGE = "We couldn't export items. Try again.";
export const READINESS_MESSAGES: Readonly<Record<PublicRequestReadiness, string>> = {
  ready: 'Public service requests are ready.',
  no_active_categories: 'Create and activate a category to accept public requests.',
  no_active_services: 'Assign an active service to an active category to accept public requests.',
};
const SEARCH_DEBOUNCE_MS = 300;
const SKELETON_ROWS = [0, 1, 2, 3, 4, 5];
const MANAGE_ROLES: readonly string[] = ['owner', 'operations_manager'];
const READ_ROLES: readonly string[] = ['dispatcher', 'accounting', 'viewer'];
const ADMIN_NAV_ROLES: readonly string[] = ['owner', 'viewer'];

type ListResult =
  | { readonly ok: true; readonly response: CatalogListResponse }
  | { readonly ok: false; readonly error: ApiError | null };

/**
 * Products & services page (`/admin/products-services`): metrics, tabs, filterable server-paged
 * table, item drawer, row actions, CSV export/template/import. Single owner of the page state;
 * managers edit, read roles view, any other role sees the forbidden state (BR-01, BR-02).
 */
@Component({
  selector: 'app-products-services',
  imports: [
    CategoryDialog,
    ConfirmDialog,
    DiscardChangesDialog,
    RouterLink,
    ButtonDirective,
    Menu,
    Message,
    Paginator,
    Skeleton,
    SpinnerIcon,
    Toast,
    AdministrationNav,
    CatalogMetrics,
    CatalogTable,
    CatalogToolbar,
    ImportDialog,
    ItemDrawer,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './products-services.html',
  styleUrl: './products-services.scss',
})
export class ProductsServices {
  private readonly catalog = inject(CatalogItemsService);
  private readonly categoriesService = inject(CatalogCategoriesService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly drawer = viewChild(ItemDrawer);
  private readonly menu = viewChild.required(Menu);

  private readonly listRequests = new Subject<CatalogListQuery>();
  private readonly searchInput = new Subject<string>();

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly readOnlyMessage = READ_ONLY_MESSAGE;
  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly listErrorMessage = LIST_ERROR_MESSAGE;
  readonly infoNote = INFO_NOTE;
  readonly warningNote = WARNING_NOTE;
  readonly emptyInitialMessage = EMPTY_INITIAL_MESSAGE;
  readonly emptyFilteredMessage = EMPTY_FILTERED_MESSAGE;
  readonly pageSize = PAGE_SIZE;
  readonly skeletonRows = SKELETON_ROWS;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  readonly canManage = computed(() => MANAGE_ROLES.includes(this.roleCode()));
  readonly canImport = computed(() => this.roleCode() === 'owner');
  readonly isReadOnly = computed(() => READ_ROLES.includes(this.roleCode()));
  /** BR-02: the Administration column follows the shell rule (Owner and Viewer only). */
  readonly showAdminNav = computed(() => ADMIN_NAV_ROLES.includes(this.roleCode()));
  private readonly serverForbidden = signal(false);
  readonly forbidden = computed(
    () => !(this.canManage() || this.isReadOnly()) || this.serverForbidden(),
  );

  // Filters and paging (API query only, not the URL).
  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly type = signal<CatalogType | null>(null);
  readonly taxStatus = signal<TaxStatusFilter | null>(null);
  readonly status = signal<StatusFilter | null>(null);
  readonly sort = signal<CatalogSort>('name');
  readonly page = signal(1);
  readonly hasFilters = computed(
    () =>
      this.searchText().trim().length > 0 ||
      this.type() !== null ||
      this.taxStatus() !== null ||
      this.status() !== null,
  );
  private readonly filters = computed<CatalogFilters>(() => ({
    type: this.type(),
    search: this.searchTerm(),
    taxStatus: this.taxStatus(),
    status: this.status(),
    sort: this.sort(),
  }));

  readonly rows = signal<readonly CatalogRow[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(true);
  readonly listError = signal<ApiError | null>(null);
  readonly first = computed(() => (this.page() - 1) * PAGE_SIZE);
  readonly report = computed(() => {
    const total = this.totalCount();
    const from = total === 0 ? 0 : this.first() + 1;
    const to = Math.min(this.first() + PAGE_SIZE, total);
    return `Showing ${from} – ${to} of ${total} items`;
  });

  readonly summary = signal<CatalogSummary | null>(null);
  readonly summaryLoading = signal(true);
  readonly summaryFailed = signal(false);
  readonly money = computed(() => moneyFormat(this.summary()?.currency ?? null));

  /** Readiness note (BR-13): hidden while the summary loads or fails. */
  readonly readiness = computed(() => {
    const status = this.summary()?.publicRequestReadiness;
    return this.summaryLoading() || this.summaryFailed() || status === undefined
      ? null
      : { ready: status === 'ready', message: READINESS_MESSAGES[status] };
  });

  // Categories (managers only): shared by the Manage categories dialog and the drawer selector.
  readonly categories = signal<readonly CatalogCategory[]>([]);
  readonly categoriesLoading = signal(false);
  readonly categoriesFailed = signal(false);
  readonly categoriesOpen = signal(false);

  readonly drawerOpen = signal(false);
  readonly drawerMode = signal<ItemDrawerMode>('create');
  readonly drawerItemId = signal<string | null>(null);
  readonly mutatingId = signal<string | null>(null);
  readonly menuModel = signal<MenuItem[]>([]);
  readonly exporting = signal(false);
  readonly downloadingTemplate = signal(false);
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
          this.catalog.list(query).pipe(
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
      this.loadSummary();
      if (this.canManage()) {
        this.loadCategories();
      }
    } else {
      this.loading.set(false);
    }
  }

  // Data loading

  loadList(): void {
    const query: CatalogListQuery = { ...this.filters(), page: this.page() };
    this.listRequests.next(query);
  }

  private handleListResult(result: ListResult): void {
    if (result.ok) {
      const { items, totalCount } = result.response;
      if (items.length === 0 && totalCount > 0 && this.page() > 1) {
        this.page.set(Math.max(1, Math.ceil(totalCount / PAGE_SIZE)));
        this.loadList();
        return;
      }
      this.loading.set(false);
      this.rows.set(items);
      this.totalCount.set(totalCount);
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

  loadSummary(): void {
    this.summaryFailed.set(false);
    if (this.summary() === null) {
      this.summaryLoading.set(true);
    }
    this.catalog
      .summary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => {
          this.summaryLoading.set(false);
          this.summary.set(summary);
        },
        error: (error: unknown) => {
          this.summaryLoading.set(false);
          this.summaryFailed.set(true);
          if (isApiError(error) && error.kind === 'unauthorized') {
            this.onUnauthorized();
          }
        },
      });
  }

  loadCategories(): void {
    this.categoriesLoading.set(true);
    this.categoriesFailed.set(false);
    this.categoriesService
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (categories) => {
          this.categoriesLoading.set(false);
          this.categories.set(categories);
        },
        error: (error: unknown) => {
          this.categoriesLoading.set(false);
          this.categoriesFailed.set(true);
          if (isApiError(error) && error.kind === 'unauthorized') {
            this.onUnauthorized();
          }
        },
      });
  }

  openCategories(): void {
    this.categoriesOpen.set(true);
    this.loadCategories();
  }

  onCategoriesChange(categories: readonly CatalogCategory[]): void {
    this.categories.set(categories);
    this.loadSummary();
  }

  private refresh(): void {
    this.loadList();
    this.loadSummary();
  }

  // Filters, sorting, paging (BR-06)

  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  /** Tabs and the Type filter are one state. */
  onType(type: CatalogType | null): void {
    this.type.set(type);
    this.filtersChanged();
  }

  onTaxStatus(value: TaxStatusFilter | null): void {
    this.taxStatus.set(value);
    this.filtersChanged();
  }

  onStatus(value: StatusFilter | null): void {
    this.status.set(value);
    this.filtersChanged();
  }

  clearFilters(): void {
    this.searchText.set('');
    this.searchTerm.set('');
    this.type.set(null);
    this.taxStatus.set(null);
    this.status.set(null);
    this.filtersChanged();
  }

  onSort(key: CatalogSortKey): void {
    this.sort.set(this.sort() === key ? `-${key}` : key);
    this.page.set(1);
    this.loadList();
  }

  onPageChange(event: PaginatorState): void {
    this.page.set((event.page ?? 0) + 1);
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
  private showDrawer(mode: ItemDrawerMode, id: string | null): void {
    const show = () => {
      this.drawerMode.set(mode);
      this.drawerItemId.set(id);
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

  onDrawerClosed(): void {
    this.drawerOpen.set(false);
  }

  onDrawerSaved(): void {
    this.refresh();
  }

  onItemUnavailable(): void {
    this.messageService.add({ severity: 'error', summary: ITEM_UNAVAILABLE_MESSAGE });
    this.drawerOpen.set(false);
    this.refresh();
  }

  /** Consulted by `productsServicesUnsavedChangesGuard` on route leave. */
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
          subject: 'this item',
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

  // Row actions (BR-12)

  openMenu(event: Event, row: CatalogRow): void {
    this.menuModel.set(this.buildMenu(row));
    this.menu().toggle(event);
  }

  buildMenu(row: CatalogRow): MenuItem[] {
    if (!this.canManage()) {
      return [
        {
          label: 'View details',
          icon: 'pi pi-eye',
          command: () => this.showDrawer('view', row.id),
        },
      ];
    }
    const busy = this.mutatingId() !== null;
    return [
      {
        label: 'Edit',
        icon: 'pi pi-pencil',
        command: () => this.showDrawer('edit', row.id),
      },
      row.isActive
        ? {
            label: 'Deactivate',
            icon: 'pi pi-ban',
            disabled: busy,
            command: () => this.confirmDeactivate(row),
          }
        : {
            label: 'Activate',
            icon: 'pi pi-check-circle',
            disabled: busy,
            command: () => this.mutate(row, this.catalog.activate(row.id), `${row.name} activated`),
          },
    ];
  }

  private confirmDeactivate(row: CatalogRow): void {
    this.confirmationService.confirm({
      header: 'Deactivate item',
      message: `Deactivate ${row.name}? It stays on existing quotes, jobs and invoices, and can be reactivated at any time.`,
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Deactivate', severity: 'danger' },
      rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
      accept: () => this.mutate(row, this.catalog.deactivate(row.id), `${row.name} deactivated`),
    });
  }

  private mutate(row: CatalogRow, request$: Observable<unknown>, success: string): void {
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
      error: (error: unknown) => this.handleMutationFailed(error),
    });
  }

  private handleMutationFailed(error: unknown): void {
    this.mutatingId.set(null);
    const apiError: ApiError | null = isApiError(error) ? error : null;
    if (apiError?.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    if (apiError?.kind === 'not-found') {
      this.messageService.add({ severity: 'error', summary: ITEM_UNAVAILABLE_MESSAGE });
      this.refresh();
      return;
    }
    this.messageService.add({
      severity: 'error',
      summary: apiError?.message ?? 'An unexpected error occurred. Please try again.',
    });
  }

  // CSV export, template and import (BR-13)

  exportList(): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.catalog
      .export(this.filters())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (csv) => {
          this.exporting.set(false);
          saveCsv(csv);
        },
        error: (error: unknown) =>
          this.handleDownloadFailed(error, EXPORT_FAILED_MESSAGE, this.exporting),
      });
  }

  downloadTemplate(): void {
    if (this.downloadingTemplate()) {
      return;
    }
    this.downloadingTemplate.set(true);
    this.catalog
      .importTemplate()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (csv) => {
          this.downloadingTemplate.set(false);
          saveCsv(csv);
        },
        error: (error: unknown) =>
          this.handleDownloadFailed(error, TEMPLATE_FAILED_MESSAGE, this.downloadingTemplate),
      });
  }

  private handleDownloadFailed(
    error: unknown,
    message: string,
    busy: { set(value: boolean): void },
  ): void {
    busy.set(false);
    if (isApiError(error) && error.kind === 'unauthorized') {
      this.onUnauthorized();
      return;
    }
    this.messageService.add({ severity: 'error', summary: message });
  }

  openImport(): void {
    this.importOpen.set(true);
  }

  onImported(count: number): void {
    this.messageService.add({ severity: 'success', summary: `Imported ${count} items` });
    this.refresh();
  }
}
