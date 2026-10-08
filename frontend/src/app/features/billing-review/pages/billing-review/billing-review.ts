import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  switchMap,
} from 'rxjs';

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import { saveCsv } from '../../../../shared/utils/csv-file';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { InvoicePanel } from '../../components/invoice-panel/invoice-panel';
import { ReviewDetail } from '../../components/review-detail/review-detail';
import { ReviewQueue } from '../../components/review-queue/review-queue';
import {
  ACTION_FAILED_MESSAGE,
  BillingReviewDetail,
  BillingReviewOptions,
  DETAIL_ERROR_MESSAGE,
  DETAIL_GONE_MESSAGE,
  EXPORT_FAILED_MESSAGE,
  EXPORT_TOO_LARGE_MESSAGE,
  FORBIDDEN_MESSAGE,
  GENERATE_MESSAGES,
  GeneratedInvoice,
  ISSUE_DATE_MESSAGE,
  NOTE_MAX_LENGTH,
  NOTE_TOO_LONG_MESSAGE,
  CompletedRange,
  PaymentTerms,
  QUEUE_ERROR_MESSAGE,
  QueueFilters,
  QueueResponse,
  READ_ONLY_MESSAGE,
  VarianceFilter,
  ReviewState,
  TERMS_MESSAGE,
} from '../../models/billing-review.model';
import { BillingReviewService } from '../../services/billing-review.service';
import {
  GenerateErrors,
  addDays,
  dueDate,
  hasVariance,
  money,
  todayIn,
  validateGenerate,
} from '../../utils/billing-review-format';

type PageState = 'loading' | 'ready' | 'forbidden' | 'error';
type DetailState = 'loading' | 'ready' | 'gone' | 'error';
type Pending = 'return' | 'follow-up' | 'generate' | null;

/** UX only (BR-01): Read roles; the backend decides. */
const READ_ROLES: readonly string[] = [
  'owner',
  'operations_manager',
  'dispatcher',
  'accounting',
  'viewer',
];
interface Option<T> {
  readonly code: T;
  readonly label: string;
}

// Not `readonly`: PrimeNG's `p-select` `[options]` input requires a mutable array type.
const COMPLETED_OPTIONS: Option<CompletedRange>[] = [
  { code: '7d', label: 'Last 7 days' },
  { code: '30d', label: 'Last 30 days' },
  { code: '90d', label: 'Last 90 days' },
  { code: 'all', label: 'All time' },
];
const VARIANCE_OPTIONS: Option<VarianceFilter>[] = [
  { code: 'all', label: 'All' },
  { code: 'none', label: 'No variance' },
  { code: 'any', label: 'Any variance' },
  { code: 'labor', label: 'Labor variance' },
  { code: 'material', label: 'Material variance' },
];
const SEARCH_DEBOUNCE_MS = 300;
const VARIANCE_KEY = 'billing-variance';
const DISCARD_KEY = 'billing-note';

function without(errors: GenerateErrors, key: keyof GenerateErrors): GenerateErrors {
  const rest = { ...errors };
  delete rest[key];
  return rest;
}

export const DEFAULT_FILTERS: QueueFilters = {
  branchId: null,
  completed: '30d',
  technicianId: null,
  variance: 'all',
  search: '',
  tab: 'all',
  page: 1,
};

/** Completed jobs review (`/invoices/review`, BR-22 to BR-27): queue, detail and invoice panel. */
@Component({
  selector: 'app-billing-review',
  imports: [
    ButtonDirective,
    ConfirmDialog,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    InvoicePanel,
    Menu,
    ReviewDetail,
    ReviewQueue,
    Select,
    Skeleton,
    Toast,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './billing-review.html',
  styleUrl: './billing-review.scss',
})
export class BillingReview {
  private readonly api = inject(BillingReviewService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly messages = inject(MessageService);
  private readonly confirmations = inject(ConfirmationService);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly panel = viewChild(InvoicePanel);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly errorMessage = QUEUE_ERROR_MESSAGE;
  readonly detailErrorMessage = DETAIL_ERROR_MESSAGE;
  readonly goneMessage = DETAIL_GONE_MESSAGE;
  readonly varianceKey = VARIANCE_KEY;
  readonly discardKey = DISCARD_KEY;
  readonly noteMaxLength = NOTE_MAX_LENGTH;
  readonly sessionExpired = signal(false);

  readonly state = signal<PageState>('loading');
  readonly options = signal<BillingReviewOptions | null>(null);
  readonly filters = signal<QueueFilters>(DEFAULT_FILTERS);
  readonly searchText = signal('');
  readonly queue = signal<QueueResponse | null>(null);
  readonly queueLoading = signal(true);
  readonly queueFailed = signal(false);
  readonly selectedId = signal<string | null>(null);
  /** Mobile (< 768 px): the detail replaces the queue until Back to quote. */
  readonly detailOpen = signal(false);

  readonly detailState = signal<DetailState>('loading');
  readonly detail = signal<BillingReviewDetail | null>(null);
  readonly note = signal('');
  readonly issueDate = signal('');
  readonly paymentTerms = signal<PaymentTerms>('due_upon_receipt');
  readonly errors = signal<GenerateErrors>({});
  readonly pending = signal<Pending>(null);
  readonly exporting = signal(false);

  readonly timezone = computed(() => this.options()?.timezone ?? 'UTC');
  readonly today = computed(() => todayIn(this.timezone()));
  readonly minDate = computed(() => addDays(this.today(), -30));
  readonly canAct = computed(
    () => (this.detail()?.canAct ?? false) && this.options()?.canAct === true,
  );
  readonly noteDirty = computed(() => this.note() !== (this.detail()?.note ?? ''));
  readonly dueDate = computed(() => dueDate(this.issueDate(), this.paymentTerms()));
  readonly filtered = computed(() => {
    const f = this.filters();
    return (
      f.branchId !== null ||
      f.technicianId !== null ||
      f.completed !== DEFAULT_FILTERS.completed ||
      f.variance !== 'all' ||
      f.search.trim() !== ''
    );
  });
  readonly metrics = computed(() => {
    const metrics = this.queue()?.metrics;
    return metrics
      ? { ...metrics, valueText: money(metrics.completedValue, metrics.currency) }
      : null;
  });
  readonly completedOptions = COMPLETED_OPTIONS;
  readonly varianceOptions = VARIANCE_OPTIONS;
  readonly branchOptions = computed<Option<string>[]>(() => [
    { code: 'all', label: 'All branches' },
    ...(this.options()?.branches ?? []).map((b) => ({ code: b.id, label: b.name })),
  ]);
  readonly technicianOptions = computed<Option<string>[]>(() => [
    { code: 'all', label: 'All technicians' },
    ...(this.options()?.technicians ?? []).map((t) => ({ code: t.id, label: t.name })),
  ]);
  readonly exportMenu: MenuItem[] = [
    { label: 'Export CSV', icon: 'pi pi-download', command: () => this.exportCsv() },
  ];

  private readonly queueLoads = new Subject<string | null>();
  private readonly detailLoads = new Subject<string>();
  private readonly searches = new Subject<string>();
  private actionSubscription: Subscription | null = null;

  constructor() {
    this.queueLoads
      .pipe(
        switchMap((prefer) => {
          this.queueLoading.set(true);
          return this.api.queue(this.filters()).pipe(
            map((queue) => ({ queue, prefer, error: null as unknown })),
            catchError((error: unknown) => of({ queue: null, prefer, error })),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ queue, prefer, error }) => this.queueLoaded(queue, prefer, error));

    this.detailLoads
      .pipe(
        switchMap((id) => {
          this.detailState.set('loading');
          return this.api.detail(id).pipe(
            map((detail) => ({ detail, error: null as unknown })),
            catchError((error: unknown) => of({ detail: null, error })),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ detail, error }) => this.detailLoaded(detail, error));

    this.searches
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((search) => this.changeFilters({ search }));

    this.destroyRef.onDestroy(() => this.actionSubscription?.unsubscribe());

    const role = this.sessionService.session()?.role.code ?? '';
    if (READ_ROLES.includes(role)) {
      this.loadOptions();
    } else {
      this.settle('forbidden');
    }
  }

  private settle(state: PageState): void {
    this.state.set(state);
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }

  private loadOptions(): void {
    this.state.set('loading');
    this.api
      .options()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.options.set(options);
          this.state.set('ready');
          this.queueLoads.next(null);
        },
        error: (error: unknown) => this.pageFailed(error),
      });
  }

  private pageFailed(error: unknown): void {
    switch (isApiError(error) ? error.kind : null) {
      case 'unauthorized':
        handleUnauthorized(this.router, this.sessionExpired);
        break;
      case 'forbidden':
        this.settle('forbidden');
        break;
      default:
        this.settle('error');
    }
  }

  retry(): void {
    if (this.options() === null) {
      this.loadOptions();
    } else {
      this.reloadQueue();
    }
  }

  // Queue

  private queueLoaded(queue: QueueResponse | null, prefer: string | null, error: unknown): void {
    this.queueLoading.set(false);
    if (queue === null) {
      if (this.options() === null) {
        this.pageFailed(error);
      } else if (isApiError(error) && error.kind === 'unauthorized') {
        handleUnauthorized(this.router, this.sessionExpired);
      } else if (isApiError(error) && error.kind === 'forbidden') {
        this.settle('forbidden');
      } else {
        this.queueFailed.set(true);
      }
      return;
    }
    this.queueFailed.set(false);
    this.queue.set(queue);
    const ids = queue.items.map((item) => item.workOrderId);
    const keep = this.selectedId();
    const next =
      prefer !== null && ids.includes(prefer)
        ? prefer
        : prefer === null && keep !== null && ids.includes(keep)
          ? keep
          : (ids[0] ?? null);
    if (next === null) {
      this.selectedId.set(null);
      this.detail.set(null);
      this.detailOpen.set(false);
      this.detailState.set('ready');
    } else if (next !== keep || this.detail() === null) {
      this.openJob(next, false);
    }
  }

  /** Reloads the queue and metrics; `prefer` null keeps the selection when still listed. */
  private reloadQueue(prefer: string | null = null): void {
    this.queueFailed.set(false);
    this.queueLoads.next(prefer);
  }

  /** Reloads and selects the item after `id` in the previous list, else the first. */
  private reloadAfter(id: string): void {
    const ids = this.queue()?.items.map((item) => item.workOrderId) ?? [];
    const next = ids[ids.indexOf(id) + 1] ?? null;
    this.selectedId.set(null);
    this.reloadQueue(next);
  }

  changeFilters(change: Partial<QueueFilters>): void {
    this.filters.update((filters) => ({ ...filters, ...change, page: change.page ?? 1 }));
    this.selectedId.set(null);
    this.reloadQueue();
  }

  searchChanged(text: string): void {
    this.searchText.set(text);
    this.searches.next(text);
  }

  clearFilters(): void {
    this.searchText.set('');
    this.changeFilters({ ...DEFAULT_FILTERS });
  }

  // Selection and detail

  select(id: string): void {
    if (id === this.selectedId()) {
      this.detailOpen.set(true);
      return;
    }
    if (!this.noteDirty() || this.canAct() === false) {
      this.openJob(id, true);
      return;
    }
    this.confirmations.confirm({
      key: DISCARD_KEY,
      header: 'Discard unsaved note?',
      message: 'You have an unsaved accounting note. If you leave now, the note will be lost.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Discard', severity: 'secondary', outlined: true },
      rejectButtonProps: { label: 'Keep editing' },
      accept: () => this.openJob(id, true),
    });
  }

  private openJob(id: string, openDetail: boolean): void {
    this.selectedId.set(id);
    this.detailOpen.set(openDetail);
    this.detailLoads.next(id);
  }

  backToQueue(): void {
    this.detailOpen.set(false);
  }

  retryDetail(): void {
    const id = this.selectedId();
    if (id !== null) {
      this.detailLoads.next(id);
    }
  }

  private detailLoaded(detail: BillingReviewDetail | null, error: unknown): void {
    if (detail !== null) {
      this.detail.set(detail);
      this.note.set(detail.note ?? '');
      this.issueDate.set(detail.invoiceDefaults.issueDate);
      this.paymentTerms.set(detail.invoiceDefaults.paymentTerms);
      this.errors.set({});
      this.detailState.set('ready');
      return;
    }
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized') {
      handleUnauthorized(this.router, this.sessionExpired);
    } else if (kind === 'forbidden') {
      this.settle('forbidden');
    } else if (kind === 'not-found') {
      this.detail.set(null);
      this.detailState.set('gone');
      this.selectedId.set(null);
      this.reloadQueue();
    } else {
      this.detail.set(null);
      this.detailState.set('error');
    }
  }

  setIssueDate(value: string): void {
    this.issueDate.set(value);
    this.errors.update((errors) => without(errors, 'issueDate'));
  }

  setTerms(value: PaymentTerms): void {
    this.paymentTerms.set(value);
    this.errors.update((errors) => without(errors, 'paymentTerms'));
  }

  setNote(value: string): void {
    this.note.set(value);
    this.errors.update((errors) => without(errors, 'note'));
  }

  // Actions (BR-27)

  private focusInvalid(errors: GenerateErrors): void {
    if (errors.issueDate || errors.paymentTerms) {
      this.panel()?.focusFirstInvalid(errors);
    } else if (errors.note) {
      this.host.querySelector('textarea')?.focus();
    }
  }

  private noteInvalid(): boolean {
    if (this.note().trim().length <= NOTE_MAX_LENGTH) {
      return false;
    }
    this.errors.update((errors) => ({
      ...errors,
      ...validateGenerate(this.formValues(), this.today()),
    }));
    return true;
  }

  private formValues(): { issueDate: string; paymentTerms: PaymentTerms; note: string } {
    return {
      issueDate: this.issueDate(),
      paymentTerms: this.paymentTerms(),
      note: this.note(),
    };
  }

  returnToQueue(): void {
    const detail = this.detail();
    if (detail === null || this.pending() !== null || this.noteInvalid()) {
      return;
    }
    this.pending.set('return');
    this.run(
      this.api.updateReview(detail.header.workOrderId, {
        note: this.note().trim(),
        followUp: false,
        reason: 'returned_to_queue',
      }),
      () => {
        this.toast('success', 'Returned to quote.');
        this.reloadAfter(detail.header.workOrderId);
      },
    );
  }

  toggleFollowUp(): void {
    const detail = this.detail();
    if (detail === null || this.pending() !== null || this.noteInvalid()) {
      return;
    }
    const mark = detail.followUp === null;
    this.pending.set('follow-up');
    this.run(
      this.api.updateReview(detail.header.workOrderId, {
        note: this.note().trim(),
        followUp: mark,
        reason: 'manual',
      }),
      (state: ReviewState) => {
        this.toast('success', mark ? 'Marked for follow-up.' : 'Follow-up removed.');
        this.applyReview(detail.header.workOrderId, state);
      },
    );
  }

  /** Mirrors the saved note and follow-up in the detail and the queue badge. */
  private applyReview(workOrderId: string, state: ReviewState): void {
    this.detail.update((current) =>
      current?.header.workOrderId === workOrderId
        ? { ...current, note: state.note, followUp: state.followUp }
        : current,
    );
    this.note.set(state.note ?? '');
    this.queue.update((queue) =>
      queue === null
        ? queue
        : {
            ...queue,
            items: queue.items.map((item) =>
              item.workOrderId === workOrderId
                ? { ...item, followUp: state.followUp !== null }
                : item,
            ),
          },
    );
  }

  generate(): void {
    const detail = this.detail();
    if (detail === null || this.pending() !== null) {
      return;
    }
    const errors = validateGenerate(this.formValues(), this.today());
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.focusInvalid(errors);
      return;
    }
    const blocked = detail.verification.some((item) => item.mandatory && !item.met);
    if (hasVariance(detail.laborVariance, detail.materialVariance) && !blocked) {
      this.confirmations.confirm({
        key: VARIANCE_KEY,
        header: 'Generate invoice with variances?',
        message:
          'This job has labor or material variances. The draft invoice uses the approved quote amounts; variances stay for review only.',
        defaultFocus: 'reject',
        acceptButtonProps: { label: 'Generate draft' },
        rejectButtonProps: { label: 'Cancel', severity: 'secondary', outlined: true },
        accept: () => this.sendGenerate(detail, true),
      });
      return;
    }
    this.sendGenerate(detail, false);
  }

  private sendGenerate(detail: BillingReviewDetail, acknowledgeVariances: boolean): void {
    if (this.pending() !== null) {
      return;
    }
    this.pending.set('generate');
    const id = detail.header.workOrderId;
    this.run(
      this.api.generateInvoice(id, {
        issueDate: this.issueDate(),
        paymentTerms: this.paymentTerms(),
        note: this.note().trim(),
        acknowledgeVariances,
      }),
      (result: GeneratedInvoice) => {
        this.toast(
          'success',
          result.changed
            ? `Draft invoice ${result.invoice.number} created.`
            : `Draft invoice ${result.invoice.number} already exists.`,
        );
        this.reloadAfter(id);
      },
      (error) => this.generateFailed(error, id),
    );
  }

  private generateFailed(error: ApiError | null, id: string): void {
    const code = error?.code ?? '';
    if (error?.kind === 'validation' || error?.kind === 'bad-request') {
      const keys = Object.keys(error.fieldErrors).map((key) => key.toLowerCase());
      const errors = validateGenerate(this.formValues(), this.today());
      const next: GenerateErrors = {
        ...errors,
        ...(keys.includes('issuedate') && !errors.issueDate
          ? { issueDate: ISSUE_DATE_MESSAGE }
          : {}),
        ...(keys.includes('paymentterms') && !errors.paymentTerms
          ? { paymentTerms: TERMS_MESSAGE }
          : {}),
        ...(keys.includes('note') && !errors.note ? { note: NOTE_TOO_LONG_MESSAGE } : {}),
      };
      this.errors.set(next);
      if (Object.keys(next).length > 0) {
        this.focusInvalid(next);
      } else {
        this.toast('error', ACTION_FAILED_MESSAGE);
      }
      return;
    }
    if (error?.kind === 'conflict' && code in GENERATE_MESSAGES) {
      this.toast('error', GENERATE_MESSAGES[code]);
      if (code === 'work_order_status_invalid') {
        this.reloadAfter(id);
      } else if (code === 'completion_requirements_unmet') {
        this.retryDetail();
        this.markQueueFollowUp(id);
      }
      return;
    }
    this.actionFailed(error);
  }

  private markQueueFollowUp(id: string): void {
    this.queue.update((queue) =>
      queue === null
        ? queue
        : {
            ...queue,
            items: queue.items.map((item) =>
              item.workOrderId === id ? { ...item, followUp: true } : item,
            ),
          },
    );
  }

  private run<T>(
    request: Observable<T>,
    onSuccess: (value: T) => void,
    onFailure: (error: ApiError | null) => void = (error) => this.actionFailed(error),
  ): void {
    this.actionSubscription?.unsubscribe();
    this.actionSubscription = request.subscribe({
      next: (value) => {
        this.pending.set(null);
        onSuccess(value);
      },
      error: (error: unknown) => {
        this.pending.set(null);
        const apiError = isApiError(error) ? error : null;
        if (apiError?.kind === 'unauthorized') {
          handleUnauthorized(this.router, this.sessionExpired);
          return;
        }
        onFailure(apiError);
      },
    });
  }

  private actionFailed(error: ApiError | null): void {
    if (error?.kind === 'forbidden') {
      this.toast('error', READ_ONLY_MESSAGE);
    } else if (error?.kind === 'not-found') {
      this.toast('error', DETAIL_GONE_MESSAGE);
      this.selectedId.set(null);
      this.reloadQueue();
    } else {
      this.toast('error', ACTION_FAILED_MESSAGE);
    }
  }

  private toast(severity: 'success' | 'error', summary: string): void {
    this.messages.add({ severity, summary });
  }

  // Export (BR-21, BR-27)

  exportCsv(): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.api
      .export(this.filters())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (file) => {
          this.exporting.set(false);
          saveCsv(file);
        },
        error: (error: unknown) => {
          this.exporting.set(false);
          const kind = isApiError(error) ? error.kind : null;
          if (kind === 'unauthorized') {
            handleUnauthorized(this.router, this.sessionExpired);
          } else {
            // The only `409` of the export is `export_too_large` (the error body is a blob).
            this.toast(
              'error',
              kind === 'conflict' ? EXPORT_TOO_LARGE_MESSAGE : EXPORT_FAILED_MESSAGE,
            );
          }
        },
      });
  }
}
