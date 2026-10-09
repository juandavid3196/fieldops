import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  linkedSignal,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Select } from 'primeng/select';
import { Toast } from 'primeng/toast';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  switchMap,
  tap,
} from 'rxjs';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { DiscardChangesDialog } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { saveCsv } from '../../../../shared/utils/csv-file';
import { QueueResponse } from '../../../billing-review/models/billing-review.model';
import { BillingReviewService } from '../../../billing-review/services/billing-review.service';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { InvoiceOverview } from '../../components/invoice-overview/invoice-overview';
import { InvoicesTable } from '../../components/invoices-table/invoices-table';
import { PaymentsTable } from '../../components/payments-table/payments-table';
import { ReadyTable } from '../../components/ready-table/ready-table';
import {
  DrawerNotice,
  RecordPaymentDrawer,
} from '../../components/record-payment-drawer/record-payment-drawer';
import {
  CUSTOMERS_ERROR_MESSAGE,
  EXPORT_FAILED_MESSAGE,
  EXPORT_TOO_LARGE_MESSAGE,
  HUB_FORBIDDEN_MESSAGE,
  HUB_TABS,
  HubTab,
  IDLE_REGION,
  InvoiceFilters,
  InvoiceRow,
  InvoicesOptions,
  NO_CUSTOMERS_MESSAGE,
  NamedOption,
  OPTIONS_ERROR_MESSAGE,
  OverviewResponse,
  PAYMENT_METHODS,
  PAYMENT_METHOD_LABELS,
  PDF_FAILED_MESSAGE,
  Page,
  PaymentFilters,
  PaymentMethod,
  PaymentRow,
  Region,
  STATUS_FILTERS,
  resolveHubQuery,
} from '../../models/invoices-hub.model';
import { INVOICE_READ_ROLES } from '../../models/invoice.model';
import { InvoiceService } from '../../services/invoice.service';
import { InvoicesHubService } from '../../services/invoices-hub.service';
import { STATUS_CHIPS, todayIn } from '../../utils/invoices-hub-format';

type ListRequest =
  | { readonly tab: 'invoices'; readonly filters: InvoiceFilters; readonly page: number }
  | { readonly tab: 'payments'; readonly filters: PaymentFilters; readonly page: number }
  | null;

interface ReadyRequest {
  readonly branchId: string | null;
  readonly page: number;
}

interface Choice<T> {
  readonly code: T;
  readonly label: string;
}

const SEARCH_DEBOUNCE_MS = 300;
const ALL = 'all';

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const STATUS_OPTIONS: Choice<string>[] = [
  { code: ALL, label: 'All statuses' },
  ...STATUS_FILTERS.map((code) => ({
    code: code as string,
    label: STATUS_CHIPS[code].label,
  })),
];
const METHOD_OPTIONS: Choice<string>[] = [
  { code: ALL, label: 'All methods' },
  ...PAYMENT_METHODS.map((code) => ({ code: code as string, label: PAYMENT_METHOD_LABELS[code] })),
];

const TAB_LABELS: Readonly<Record<HubTab, string>> = {
  invoices: 'Invoices',
  payments: 'Payments',
  ready: 'Completed jobs ready to invoice',
};

/**
 * Invoices and payments hub (`/invoices`, BR-19 to BR-26): owns the options, overview, list,
 * ready-queue and customer loaders, the URL-synced tab and status, the filters and the record
 * payment drawer. `tab` and `status` live in the query params; everything else is local.
 */
@Component({
  selector: 'app-invoices-hub',
  imports: [
    ButtonDirective,
    DiscardChangesDialog,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    InvoiceOverview,
    InvoicesTable,
    Menu,
    NgTemplateOutlet,
    PaymentsTable,
    ReadyTable,
    RecordPaymentDrawer,
    RouterLink,
    Select,
    Toast,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './invoices-hub.html',
  styleUrl: './invoices-hub.scss',
})
export class InvoicesHub {
  private readonly api = inject(InvoicesHubService);
  private readonly invoiceApi = inject(InvoiceService);
  private readonly queueApi = inject(BillingReviewService);
  private readonly sessionService = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly messages = inject(MessageService);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly tabs = HUB_TABS.map((code) => ({ code, label: TAB_LABELS[code] }));
  readonly statusOptions = STATUS_OPTIONS;
  readonly methodOptions = METHOD_OPTIONS;
  readonly forbiddenMessage = HUB_FORBIDDEN_MESSAGE;
  readonly optionsMessage = OPTIONS_ERROR_MESSAGE;
  readonly customersMessage = CUSTOMERS_ERROR_MESSAGE;
  readonly noCustomers = NO_CUSTOMERS_MESSAGE;
  readonly sessionExpired = signal(false);
  readonly announcement = signal('');

  /** UX only (BR-01): the backend decides. */
  readonly allowed = INVOICE_READ_ROLES.includes(this.sessionService.session()?.role.code ?? '');
  readonly forbidden = signal(!this.allowed);

  private readonly query = toSignal(this.route.queryParamMap, { requireSync: true });
  private readonly resolved = computed(() =>
    resolveHubQuery(this.query().get('tab'), this.query().get('status')),
  );
  readonly tab = computed(() => this.resolved().tab);
  readonly status = computed(() => this.resolved().status);

  readonly options = signal<Region<InvoicesOptions>>(IDLE_REGION);
  readonly overview = signal<Region<OverviewResponse>>(IDLE_REGION);
  readonly invoices = signal<Region<Page<InvoiceRow>>>(IDLE_REGION);
  readonly payments = signal<Region<Page<PaymentRow>>>(IDLE_REGION);
  readonly ready = signal<Region<QueueResponse>>(IDLE_REGION);

  readonly branchId = signal<string | null>(null);
  readonly invoiceSearchText = signal('');
  readonly invoiceSearch = signal('');
  readonly invoiceFrom = signal('');
  readonly invoiceTo = signal('');
  readonly customerId = signal<string | null>(null);
  readonly paymentSearchText = signal('');
  readonly paymentSearch = signal('');
  readonly paymentFrom = signal('');
  readonly paymentTo = signal('');
  readonly paymentMethod = signal<PaymentMethod | null>(null);

  readonly invoicePage = linkedSignal<string, number>({
    source: () => `${this.tab()}:${this.status()}`,
    computation: () => 1,
  });
  readonly paymentPage = linkedSignal<HubTab, number>({
    source: this.tab,
    computation: () => 1,
  });
  readonly readyPage = linkedSignal<HubTab, number>({ source: this.tab, computation: () => 1 });

  readonly customers = signal<readonly NamedOption[]>([]);
  readonly customersLoading = signal(false);
  readonly customersFailed = signal(false);
  private readonly selectedCustomer = signal<NamedOption | null>(null);

  readonly pdfBusyId = signal<string | null>(null);
  readonly exporting = signal(false);
  readonly drawerRow = signal<InvoiceRow | null>(null);
  readonly drawerOpen = signal(false);
  readonly rowMenu = signal<MenuItem[]>([]);

  readonly canChooseBranch = computed(() => (this.options().data?.branches.length ?? 0) > 1);
  readonly branchChoices = computed<Choice<string>[]>(() => [
    { code: ALL, label: 'All branches' },
    ...(this.options().data?.branches ?? []).map((b) => ({ code: b.id, label: b.name })),
  ]);
  readonly customerChoices = computed<Choice<string>[]>(() => {
    const selected = this.selectedCustomer();
    const listed = this.customers();
    const extra = selected !== null && !listed.some((c) => c.id === selected.id) ? [selected] : [];
    return [
      { code: ALL, label: 'All customers' },
      ...[...extra, ...listed].map((c) => ({ code: c.id, label: c.name })),
    ];
  });
  readonly readyCount = computed(() => this.ready().data?.tabs.ready ?? null);
  readonly timezone = computed(() => this.options().data?.timezone ?? 'UTC');

  private readonly invoiceFilters = computed<InvoiceFilters>(() => ({
    search: this.invoiceSearch(),
    from: this.invoiceFrom(),
    to: this.invoiceTo(),
    status: this.status(),
    customerId: this.customerId(),
    branchId: this.branchId(),
  }));
  private readonly paymentFilters = computed<PaymentFilters>(() => ({
    search: this.paymentSearch(),
    from: this.paymentFrom(),
    to: this.paymentTo(),
    method: this.paymentMethod(),
    branchId: this.branchId(),
  }));
  readonly invoicesFiltered = computed(() => {
    const f = this.invoiceFilters();
    return (
      f.search.trim() !== '' ||
      f.from !== '' ||
      f.to !== '' ||
      f.status !== null ||
      f.customerId !== null ||
      f.branchId !== null
    );
  });
  readonly paymentsFiltered = computed(() => {
    const f = this.paymentFilters();
    return (
      f.search.trim() !== '' ||
      f.from !== '' ||
      f.to !== '' ||
      f.method !== null ||
      f.branchId !== null
    );
  });

  private readonly listRequest = computed<ListRequest>(() => {
    switch (this.tab()) {
      case 'invoices':
        return { tab: 'invoices', filters: this.invoiceFilters(), page: this.invoicePage() };
      case 'payments':
        return { tab: 'payments', filters: this.paymentFilters(), page: this.paymentPage() };
      default:
        return null;
    }
  });
  private readonly readyRequest = computed<ReadyRequest>(
    () => ({
      branchId: this.branchId(),
      page: this.tab() === 'ready' ? this.readyPage() : 1,
    }),
    { equal: (a, b) => a.branchId === b.branchId && a.page === b.page },
  );

  readonly exportMenu: MenuItem[] = [
    { label: 'Invoices CSV', icon: 'pi pi-download', command: () => this.exportCsv('invoices') },
    { label: 'Payments CSV', icon: 'pi pi-download', command: () => this.exportCsv('payments') },
  ];

  private readonly options$ = new Subject<void>();
  private readonly overview$ = new Subject<string | null>();
  private readonly list$ = new Subject<ListRequest>();
  private readonly ready$ = new Subject<ReadyRequest>();
  private readonly customerLoads$ = new Subject<string>();
  private readonly customerTyped$ = new Subject<string>();
  private readonly invoiceSearches$ = new Subject<string>();
  private readonly paymentSearches$ = new Subject<string>();

  constructor() {
    this.options$
      .pipe(
        switchMap(() => this.fetch(this.options, this.api.options())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.overview$
      .pipe(
        switchMap((branchId) => this.fetch(this.overview, this.api.overview(branchId))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.list$
      .pipe(
        switchMap((request) => {
          if (request === null) {
            return EMPTY;
          }
          return request.tab === 'invoices'
            ? this.fetch(this.invoices, this.api.invoices(request.filters, request.page)).pipe(
                tap((data) => this.clampPage(data, request.page, this.invoicePage)),
              )
            : this.fetch(this.payments, this.api.payments(request.filters, request.page)).pipe(
                tap((data) => this.clampPage(data, request.page, this.paymentPage)),
              );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.ready$
      .pipe(
        switchMap((request) =>
          this.fetch(
            this.ready,
            this.queueApi.queue({
              branchId: request.branchId,
              completed: 'all',
              technicianId: null,
              variance: 'all',
              search: '',
              tab: 'ready',
              page: request.page,
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.customerLoads$
      .pipe(
        switchMap((term) => {
          this.customersLoading.set(true);
          return this.api.customers(term).pipe(
            tap((customers) => {
              this.customersLoading.set(false);
              this.customersFailed.set(false);
              this.customers.set(customers);
            }),
            catchError((error: unknown) => {
              this.customersLoading.set(false);
              this.customersFailed.set(true);
              this.handleFailure(error, false);
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.customerTyped$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((term) => this.customerLoads$.next(term));
    this.invoiceSearches$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((search) => {
        this.invoiceSearch.set(search);
        this.invoicePage.set(1);
      });
    this.paymentSearches$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((search) => {
        this.paymentSearch.set(search);
        this.paymentPage.set(1);
      });

    if (!this.allowed) {
      return;
    }
    effect(() => {
      const request = this.listRequest();
      untracked(() => this.list$.next(request));
    });
    effect(() => {
      const branchId = this.branchId();
      untracked(() => this.overview$.next(branchId));
    });
    effect(() => {
      const request = this.readyRequest();
      untracked(() => this.ready$.next(request));
    });
    this.options$.next();
    this.customerLoads$.next('');
  }

  /** Loads one region (skeleton while empty, kept data dimmed while reloading). */
  private fetch<T>(
    region: ReturnType<typeof signal<Region<T>>>,
    request: Observable<T>,
  ): Observable<T> {
    region.update((current) => ({ status: 'loading', data: current.data }));
    return request.pipe(
      tap((data) => region.set({ status: 'ready', data })),
      catchError((error: unknown) => {
        region.set({ status: 'error', data: null });
        this.handleFailure(error, true);
        return EMPTY;
      }),
    );
  }

  private handleFailure(error: unknown, page: boolean): void {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      handleUnauthorized(this.router, this.sessionExpired);
    } else if (kind === 'forbidden' && page) {
      this.forbidden.set(true);
    }
  }

  /** A page beyond the last (its last row just left the list) reloads the last page. */
  private clampPage(
    data: Page<unknown>,
    requested: number,
    target: ReturnType<typeof signal<number>>,
  ): void {
    const last = Math.ceil(data.total / data.pageSize);
    if (data.items.length === 0 && last > 0 && last < requested) {
      target.set(last);
    }
  }

  // Tabs and URL state (BR-20)

  selectTab(tab: HubTab): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tab === 'invoices' ? null : tab, status: null },
      queryParamsHandling: 'merge',
    });
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const next =
      event.key === 'ArrowRight'
        ? (index + 1) % this.tabs.length
        : event.key === 'ArrowLeft'
          ? (index + last) % this.tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;
    if (next < 0) {
      return;
    }
    event.preventDefault();
    this.selectTab(this.tabs[next].code);
    queueMicrotask(() =>
      this.host.querySelectorAll<HTMLElement>('[role="tab"]').item(next)?.focus(),
    );
  }

  changeStatus(code: string): void {
    const status = STATUS_FILTERS.find((value) => value === code) ?? null;
    this.invoicePage.set(1);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { status },
      queryParamsHandling: 'merge',
    });
  }

  // Filters (any change resets to page 1)

  changeBranch(code: string): void {
    this.branchId.set(code === ALL ? null : code);
    this.invoicePage.set(1);
    this.paymentPage.set(1);
    this.readyPage.set(1);
  }

  invoiceSearchChanged(text: string): void {
    this.invoiceSearchText.set(text);
    this.invoiceSearches$.next(text);
  }

  paymentSearchChanged(text: string): void {
    this.paymentSearchText.set(text);
    this.paymentSearches$.next(text);
  }

  changeInvoiceDates(change: { from?: string; to?: string }): void {
    this.invoiceFrom.set(change.from ?? this.invoiceFrom());
    this.invoiceTo.set(change.to ?? this.invoiceTo());
    this.invoicePage.set(1);
  }

  changePaymentDates(change: { from?: string; to?: string }): void {
    this.paymentFrom.set(change.from ?? this.paymentFrom());
    this.paymentTo.set(change.to ?? this.paymentTo());
    this.paymentPage.set(1);
  }

  changeCustomer(code: string): void {
    this.selectedCustomer.set(
      code === ALL
        ? null
        : (this.customers().find((c) => c.id === code) ?? this.selectedCustomer()),
    );
    this.customerId.set(code === ALL ? null : code);
    this.invoicePage.set(1);
  }

  searchCustomers(term: string): void {
    this.customerTyped$.next(term);
  }

  changeMethod(code: string): void {
    this.paymentMethod.set(PAYMENT_METHODS.find((value) => value === code) ?? null);
    this.paymentPage.set(1);
  }

  clearInvoiceFilters(): void {
    this.invoiceSearchText.set('');
    this.invoiceSearch.set('');
    this.invoiceFrom.set('');
    this.invoiceTo.set('');
    this.customerId.set(null);
    this.selectedCustomer.set(null);
    this.branchId.set(null);
    this.invoicePage.set(1);
    if (this.status() !== null) {
      this.changeStatus(ALL);
    }
  }

  clearPaymentFilters(): void {
    this.paymentSearchText.set('');
    this.paymentSearch.set('');
    this.paymentFrom.set('');
    this.paymentTo.set('');
    this.paymentMethod.set(null);
    this.branchId.set(null);
    this.paymentPage.set(1);
  }

  handleSessionExpired(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  // Retry and reloads

  retryOptions(): void {
    this.options$.next();
  }

  retryOverview(): void {
    this.overview$.next(this.branchId());
  }

  retryList(): void {
    this.list$.next(this.listRequest());
  }

  retryReady(): void {
    this.ready$.next(this.readyRequest());
  }

  /** After a payment: metrics, aging, recent payments, the ready badge and the active list. */
  private reloadAll(): void {
    this.retryOverview();
    this.retryReady();
    this.retryList();
  }

  // Row actions (BR-22, BR-24)

  openRowMenu(event: Event, row: InvoiceRow, menu: Menu): void {
    const optionsReady = this.options().status === 'ready';
    const items: MenuItem[] = [
      { label: 'View invoice', icon: 'pi pi-eye', routerLink: ['/invoices', row.id] },
    ];
    if (row.canRecordPayment) {
      items.push({
        label: 'Record payment',
        icon: 'pi pi-wallet',
        disabled: !optionsReady,
        command: () => this.openDrawer(row),
      });
    }
    items.push({
      label: 'Download PDF',
      icon: 'pi pi-file-pdf',
      disabled: this.pdfBusyId() === row.id,
      command: () => this.downloadPdf(row),
    });
    this.rowMenu.set(items);
    menu.toggle(event);
  }

  private openDrawer(row: InvoiceRow): void {
    this.drawerRow.set(row);
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  drawerSettled(): void {
    this.drawerOpen.set(false);
    this.reloadAll();
  }

  notify(notice: DrawerNotice): void {
    this.messages.add(notice);
    this.announcement.set(notice.summary);
  }

  private downloadPdf(row: InvoiceRow): void {
    if (this.pdfBusyId() === row.id) {
      return;
    }
    this.pdfBusyId.set(row.id);
    this.invoiceApi
      .pdf(row.id, `${row.number}.pdf`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (file) => {
          this.pdfBusyId.set(null);
          saveCsv(file);
        },
        error: (error: unknown) => {
          this.pdfBusyId.set(null);
          if (isApiError(error) && error.kind === 'unauthorized') {
            handleUnauthorized(this.router, this.sessionExpired);
          } else {
            this.notify({ severity: 'error', summary: PDF_FAILED_MESSAGE });
          }
        },
      });
  }

  // Export (BR-21, BR-24)

  exportCsv(kind: 'invoices' | 'payments'): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    const date = todayIn(this.timezone());
    const request =
      kind === 'invoices'
        ? this.api.exportInvoices(this.invoiceFilters(), date)
        : this.api.exportPayments(this.paymentFilters(), date);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (file) => {
        this.exporting.set(false);
        saveCsv(file);
      },
      error: (error: unknown) => {
        this.exporting.set(false);
        const kindOfError = isApiError(error) ? error.kind : null;
        if (kindOfError === 'unauthorized') {
          handleUnauthorized(this.router, this.sessionExpired);
        } else {
          // The only `409` of the export is `export_too_large` (the error body is a blob).
          this.notify({
            severity: 'error',
            summary: kindOfError === 'conflict' ? EXPORT_TOO_LARGE_MESSAGE : EXPORT_FAILED_MESSAGE,
          });
        }
      },
    });
  }
}
