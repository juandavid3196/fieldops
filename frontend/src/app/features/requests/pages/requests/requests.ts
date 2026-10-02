import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Toast } from 'primeng/toast';
import {
  Observable,
  Subject,
  Subscription,
  catchError,
  debounceTime,
  map,
  of,
  switchMap,
} from 'rxjs';

import { comingSoonPath } from '../../../../core/config/coming-soon-modules';
import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { ConfirmDialog } from '../../../../shared/components/confirm-dialog/confirm-dialog';
import { DiscardChangesDialog } from '../../../../shared/components/discard-changes-dialog/discard-changes-dialog';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { NewRequestDrawer } from '../../components/new-request-drawer/new-request-drawer';
import {
  DialogKind,
  DialogSubmit,
  RequestActionDialog,
  isDialogKind,
} from '../../components/request-action-dialog/request-action-dialog';
import { RequestBoard } from '../../components/request-board/request-board';
import { DetailState, RequestDetailPanel } from '../../components/request-detail/request-detail';
import { RequestFiltersBar } from '../../components/request-filters/request-filters';
import { RequestMetricsCards } from '../../components/request-metrics/request-metrics';
import {
  BOARD_STATUSES,
  BoardStatus,
  ColumnState,
  EMPTY_COLUMN,
  MANAGER_ROLES,
  NO_FILTERS,
  PipelineResponse,
  READ_ROLES,
  RequestActionId,
  RequestDetail,
  RequestFilters,
  RequestMetrics,
  RequestOptions,
  hasActiveFilters,
} from '../../models/requests.model';
import { RequestsService } from '../../services/requests.service';

export const FORBIDDEN_MESSAGE = "You don't have access to requests.";
export const DESCRIPTION_MESSAGE =
  'Manage incoming service requests and move them through your workflow.';
export const LOAD_ERROR_MESSAGE = "We couldn't load requests.";
export const EMPTY_INITIAL_TITLE = 'No requests yet';
export const EMPTY_INITIAL_MESSAGE =
  'New requests from your public form and your team will appear here.';
export const EMPTY_FILTERED_MESSAGE = 'No requests match your filters.';
/** Fixed copy of the `409` toast (the backend title is never displayed). */
export const CONFLICT_MESSAGE = 'This request changed. Refresh to see the latest.';
export const NOT_STARTED_MESSAGE = "This assessment hasn't started yet.";
export const SAVE_FAILED_MESSAGE = "We couldn't save this change. Please try again.";
export const UNAVAILABLE_MESSAGE = "This request isn't available.";
const SEARCH_DEBOUNCE_MS = 300;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type MutationKey = RequestActionId | 'note' | 'upload';

const SUCCESS_MESSAGES: Readonly<Record<MutationKey, string>> = {
  'schedule-assessment': 'Assessment scheduled.',
  'request-information': 'Information request sent.',
  'mark-ready': 'Request marked ready for quote.',
  'complete-assessment': 'Assessment completed.',
  reschedule: 'Assessment rescheduled.',
  'create-quote': '',
  assign: 'Assignment updated.',
  'change-priority': 'Priority updated.',
  'set-branch': 'Branch updated.',
  'log-response': 'Customer response logged.',
  'start-review': 'Request moved to Needs review.',
  'move-back': 'Request moved back to Needs review.',
  'cancel-assessment': 'Assessment cancelled.',
  'cancel-request': 'Request cancelled.',
  note: 'Note added.',
  upload: 'Files added.',
};

type BoardResult =
  | { readonly ok: true; readonly response: PipelineResponse }
  | { readonly ok: false; readonly error: ApiError | null };

type DetailResult =
  | { readonly kind: 'detail'; readonly id: string; readonly detail: RequestDetail }
  | { readonly kind: 'state'; readonly state: DetailState }
  | { readonly kind: 'failed'; readonly error: ApiError | null };

function emptyColumns(): Record<BoardStatus, ColumnState> {
  return {
    new: EMPTY_COLUMN,
    needs_review: EMPTY_COLUMN,
    assessment_scheduled: EMPTY_COLUMN,
    ready_for_quote: EMPTY_COLUMN,
  };
}

/** `errors.availability.preferredDate` / `errors.attachments[0]` to a camelCase field key. */
function fieldKey(path: string): string {
  const last = (path.split('.').pop() ?? path).replace(/\[\d+\]$/, '');
  return last.charAt(0).toLowerCase() + last.slice(1);
}

/**
 * Requests page (`/requests`): metrics, filters, four-column board and the detail panel. Single
 * owner of the page state; the URL (`?request=<id>`) is the only source of the selection.
 * Owner/Dispatcher manage, read roles view, any other role sees the forbidden state with no data
 * requests (BR-01, BR-03).
 */
@Component({
  selector: 'app-requests',
  imports: [
    ButtonDirective,
    ConfirmDialog,
    DiscardChangesDialog,
    Message,
    Toast,
    NewRequestDrawer,
    RequestActionDialog,
    RequestBoard,
    RequestDetailPanel,
    RequestFiltersBar,
    RequestMetricsCards,
  ],
  providers: [MessageService, ConfirmationService],
  templateUrl: './requests.html',
  styleUrl: './requests.scss',
})
export class Requests {
  private readonly requests = inject(RequestsService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messageService = inject(MessageService);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly descriptionMessage = DESCRIPTION_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly emptyInitialTitle = EMPTY_INITIAL_TITLE;
  readonly emptyInitialMessage = EMPTY_INITIAL_MESSAGE;
  readonly emptyFilteredMessage = EMPTY_FILTERED_MESSAGE;

  private readonly roleCode = computed(() => this.sessionService.session()?.role.code ?? '');
  /** UX only: the backend decides (BR-01). */
  readonly canManage = computed(() => MANAGER_ROLES.includes(this.roleCode()));
  private readonly isReadOnly = computed(() => READ_ROLES.includes(this.roleCode()));
  private readonly serverForbidden = signal(false);
  readonly forbidden = computed(
    () => !(this.canManage() || this.isReadOnly()) || this.serverForbidden(),
  );
  readonly sessionExpired = signal(false);

  // Filters (page state, not the URL; BR-06).
  readonly filters = signal<RequestFilters>(NO_FILTERS);
  readonly searchText = signal('');
  readonly hasFilters = computed(() => hasActiveFilters(this.filters()));

  // Board.
  readonly columns = signal<Readonly<Record<BoardStatus, ColumnState>>>(emptyColumns());
  readonly boardLoaded = signal(false);
  readonly boardRefreshing = signal(false);
  readonly boardError = signal<ApiError | null>(null);
  readonly timezone = signal('UTC');
  readonly isEmpty = computed(
    () =>
      this.boardLoaded() &&
      !this.boardError() &&
      BOARD_STATUSES.every((status) => this.columns()[status].total === 0),
  );

  // Metrics and options.
  readonly metrics = signal<RequestMetrics | null>(null);
  readonly metricsLoading = signal(true);
  readonly metricsFailed = signal(false);
  readonly options = signal<RequestOptions | null>(null);
  readonly optionsLoading = signal(false);

  // Selection: the URL is the single source of truth.
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  readonly selectedId = computed(() => this.query().get('request'));
  readonly detail = signal<RequestDetail | null>(null);
  private readonly detailState = signal<DetailState>('loading');
  readonly newOpen = signal(false);
  readonly panelOpen = computed(
    () => this.selectedId() !== null && !this.forbidden() && !this.newOpen(),
  );
  readonly panelDetail = computed(() => {
    const detail = this.detail();
    return detail !== null && detail.id === this.selectedId() ? detail : null;
  });
  readonly panelState = computed<DetailState>(() =>
    this.panelDetail() !== null ? 'ready' : this.detailState(),
  );

  // Mutations.
  readonly mutating = signal(false);
  readonly dialogKind = signal<DialogKind | null>(null);
  readonly dialogErrors = signal<Readonly<Record<string, string>>>({});
  readonly noteText = signal('');
  readonly noteError = signal<string | null>(null);
  readonly uploadErrors = signal<readonly string[]>([]);

  private readonly boardRequests = new Subject<RequestFilters>();
  private readonly detailLoads = new Subject<{ id: string | null; silent: boolean }>();
  private readonly searchInput = new Subject<string>();
  private metricsLoad: Subscription | null = null;
  private boardVersion = 0;
  private originCardId: string | null = null;
  private confirmOpen = false;
  private previousSelected: string | null = null;

  constructor() {
    this.boardRequests
      .pipe(
        switchMap((filters) => {
          this.boardRefreshing.set(this.boardLoaded());
          return this.requests.pipeline(filters).pipe(
            map((response): BoardResult => ({ ok: true, response })),
            catchError((error: unknown) =>
              of<BoardResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleBoardResult(result));

    this.detailLoads
      .pipe(
        switchMap(({ id, silent }) => {
          if (id === null) {
            return of<DetailResult>({ kind: 'state', state: 'loading' });
          }
          if (!UUID.test(id)) {
            return of<DetailResult>({ kind: 'state', state: 'not-found' });
          }
          if (!silent) {
            this.detail.set(null);
            this.detailState.set('loading');
          }
          return this.requests.detail(id).pipe(
            map((detail): DetailResult => ({ kind: 'detail', id, detail })),
            catchError((error: unknown) =>
              of<DetailResult>(
                isApiError(error) && error.kind === 'not-found'
                  ? { kind: 'state', state: 'not-found' }
                  : { kind: 'failed', error: isApiError(error) ? error : null },
              ),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleDetailResult(result));

    this.searchInput
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        const search = value.trim();
        if (search !== this.filters().search) {
          this.filters.update((filters) => ({ ...filters, search }));
          this.loadBoard();
        }
      });

    // The selected id drives the panel; leaving it closes the panel and restores focus (a11y).
    this.previousSelected = this.selectedId();
    effect(() => {
      const id = this.selectedId();
      const previous = this.previousSelected;
      this.previousSelected = id;
      untracked(() => {
        if (this.forbidden()) {
          return;
        }
        this.detailLoads.next({ id, silent: false });
        if (previous !== null && id === null) {
          this.restoreFocus(previous);
        }
      });
    });

    if (!this.forbidden()) {
      this.loadBoard();
      this.loadMetrics();
      this.loadOptions();
    }
  }

  // Loading

  loadBoard(): void {
    this.boardVersion++;
    this.boardError.set(null);
    this.boardRequests.next(this.filters());
  }

  private handleBoardResult(result: BoardResult): void {
    this.boardRefreshing.set(false);
    if (!result.ok) {
      this.boardLoaded.set(true);
      if (this.isUnauthorizedOrForbidden(result.error)) {
        return;
      }
      this.boardError.set(
        result.error ?? {
          kind: 'unknown',
          status: 0,
          message: LOAD_ERROR_MESSAGE,
          fieldErrors: {},
        },
      );
      return;
    }
    const next = emptyColumns();
    for (const column of result.response.columns) {
      if (BOARD_STATUSES.includes(column.status)) {
        next[column.status] = {
          items: column.items,
          total: column.total,
          loadingMore: false,
          moreFailed: false,
        };
      }
    }
    this.columns.set(next);
    this.timezone.set(result.response.timezone);
    this.boardLoaded.set(true);
  }

  private isUnauthorizedOrForbidden(error: ApiError | null): boolean {
    if (error?.kind === 'unauthorized') {
      this.onUnauthorized();
      return true;
    }
    if (error?.kind === 'forbidden') {
      this.serverForbidden.set(true);
      return true;
    }
    return false;
  }

  loadMetrics(): void {
    this.metricsLoad?.unsubscribe();
    this.metricsFailed.set(false);
    if (this.metrics() === null) {
      this.metricsLoading.set(true);
    }
    this.metricsLoad = this.requests
      .metrics()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (metrics) => {
          this.metricsLoading.set(false);
          this.metrics.set(metrics);
        },
        error: (error: unknown) => {
          this.metricsLoading.set(false);
          this.metricsFailed.set(true);
          this.isUnauthorizedOrForbidden(isApiError(error) ? error : null);
        },
      });
  }

  private loadOptions(): void {
    this.optionsLoading.set(true);
    this.requests
      .options()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => {
          this.optionsLoading.set(false);
          this.options.set(options);
        },
        error: (error: unknown) => {
          this.optionsLoading.set(false);
          this.isUnauthorizedOrForbidden(isApiError(error) ? error : null);
        },
      });
  }

  /** Per-column "Load more": the next page of one column, appended (BR-05). */
  loadMore(status: BoardStatus): void {
    const column = this.columns()[status];
    if (column.loadingMore) {
      return;
    }
    const version = this.boardVersion;
    this.patchColumn(status, { loadingMore: true, moreFailed: false });
    this.requests
      .pipeline(this.filters(), { status, offset: column.items.length })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (version !== this.boardVersion) {
            return;
          }
          const incoming = response.columns.find((candidate) => candidate.status === status);
          const current = this.columns()[status];
          const known = new Set(current.items.map((card) => card.id));
          this.patchColumn(status, {
            items: [...current.items, ...(incoming?.items ?? []).filter((c) => !known.has(c.id))],
            total: incoming?.total ?? current.total,
            loadingMore: false,
          });
        },
        error: () => {
          if (version === this.boardVersion) {
            this.patchColumn(status, { loadingMore: false, moreFailed: true });
          }
        },
      });
  }

  private patchColumn(status: BoardStatus, patch: Partial<ColumnState>): void {
    this.columns.update((columns) => ({ ...columns, [status]: { ...columns[status], ...patch } }));
  }

  private handleDetailResult(result: DetailResult): void {
    if (result.kind === 'detail') {
      if (result.id === this.selectedId()) {
        this.detail.set(result.detail);
        this.detailState.set('ready');
      }
      return;
    }
    if (result.kind === 'state') {
      this.detail.set(null);
      this.detailState.set(result.state);
      return;
    }
    if (this.isUnauthorizedOrForbidden(result.error)) {
      return;
    }
    this.detailState.set('error');
  }

  retryDetail(): void {
    this.detailLoads.next({ id: this.selectedId(), silent: false });
  }

  // Filters (BR-06)

  onFilterChange(change: Partial<RequestFilters>): void {
    this.filters.update((filters) => ({ ...filters, ...change }));
    this.loadBoard();
  }

  /** Search is debounced 300 ms; the board reloads, the metrics never do. */
  onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  clearFilters(): void {
    this.filters.set(NO_FILTERS);
    this.searchText.set('');
    this.loadBoard();
  }

  // Selection and focus (the URL is the source of truth)

  openRequest(id: string): void {
    this.originCardId = id;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { request: id },
      queryParamsHandling: 'merge',
    });
  }

  closePanel(): void {
    if (this.dialogKind() !== null || this.confirmOpen) {
      return;
    }
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { request: null },
      queryParamsHandling: 'merge',
    });
  }

  /** Focus returns to the originating card when it is still rendered. */
  private restoreFocus(previousId: string): void {
    const target = this.originCardId ?? previousId;
    afterNextRender(
      () => {
        const card = Array.from(this.host.querySelectorAll<HTMLElement>('[data-request-id]')).find(
          (element) => element.dataset['requestId'] === target,
        );
        card?.focus();
      },
      { injector: this.injector },
    );
  }

  // Actions (BR-03)

  onAction(id: RequestActionId): void {
    const detail = this.detail();
    if (detail === null || !this.canManage() || this.mutating()) {
      return;
    }
    switch (id) {
      case 'create-quote':
        void this.router.navigateByUrl(comingSoonPath('quotes'));
        return;
      case 'start-review':
        this.run(id, this.requests.startReview(detail.id));
        return;
      case 'mark-ready':
        this.run(id, this.requests.markReadyForQuote(detail.id));
        return;
      case 'complete-assessment':
        this.run(id, this.requests.completeAssessment(detail.id));
        return;
      case 'move-back':
        this.run(id, this.requests.moveToReview(detail.id));
        return;
      case 'cancel-assessment':
        this.confirmCancelAssessment(detail);
        return;
      default:
        if (isDialogKind(id)) {
          this.dialogErrors.set({});
          this.dialogKind.set(id);
        }
    }
  }

  private confirmCancelAssessment(detail: RequestDetail): void {
    this.confirmOpen = true;
    this.confirmationService.confirm({
      header: 'Cancel assessment?',
      message: 'The request returns to Needs review.',
      defaultFocus: 'reject',
      acceptButtonProps: { label: 'Cancel assessment', severity: 'danger' },
      rejectButtonProps: { label: 'Keep', severity: 'secondary', outlined: true },
      accept: () => {
        this.confirmOpen = false;
        this.run('cancel-assessment', this.requests.cancelAssessment(detail.id));
      },
      reject: () => (this.confirmOpen = false),
    });
  }

  dismissDialog(): void {
    if (!this.mutating()) {
      this.dialogKind.set(null);
      this.dialogErrors.set({});
    }
  }

  onDialogSubmit(payload: DialogSubmit): void {
    const detail = this.detail();
    if (detail === null || this.mutating()) {
      return;
    }
    this.dialogErrors.set({});
    const id = detail.id;
    switch (payload.kind) {
      case 'assign':
        this.run(payload.kind, this.requests.assign(id, payload.assigneeUserId));
        break;
      case 'change-priority':
        this.run(payload.kind, this.requests.changePriority(id, payload.urgency));
        break;
      case 'set-branch':
        this.run(payload.kind, this.requests.setBranch(id, payload.branchId));
        break;
      case 'request-information':
        this.run(payload.kind, this.requests.requestInformation(id, payload.body));
        break;
      case 'log-response':
        this.run(payload.kind, this.requests.logResponse(id, payload.body));
        break;
      case 'schedule-assessment':
        this.run(payload.kind, this.requests.scheduleAssessment(id, payload.body));
        break;
      case 'reschedule':
        this.run(payload.kind, this.requests.rescheduleAssessment(id, payload.body));
        break;
      case 'cancel-request':
        this.run(payload.kind, this.requests.cancel(id, payload.reason));
        break;
    }
  }

  onNote(body: string): void {
    const detail = this.detail();
    if (detail === null || this.mutating()) {
      return;
    }
    this.noteError.set(null);
    this.run('note', this.requests.addNote(detail.id, body));
  }

  onFiles(files: File[]): void {
    const detail = this.detail();
    if (detail === null || this.mutating()) {
      return;
    }
    this.uploadErrors.set([]);
    this.run('upload', this.requests.uploadAttachments(detail.id, files));
  }

  private run(key: MutationKey, request$: Observable<RequestDetail>): void {
    if (this.mutating()) {
      return;
    }
    this.mutating.set(true);
    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (detail) => this.afterMutation(key, detail),
      error: (error: unknown) => this.mutationFailed(key, error),
    });
  }

  /** The response is the new detail; the board and metrics reload in parallel (AC-22). */
  private afterMutation(key: MutationKey, detail: RequestDetail): void {
    this.mutating.set(false);
    this.dialogKind.set(null);
    this.dialogErrors.set({});
    if (key === 'note') {
      this.noteText.set('');
    }
    if (detail.id === this.selectedId()) {
      this.detail.set(detail);
      this.detailState.set('ready');
    }
    this.loadBoard();
    this.loadMetrics();
    this.messageService.add({ severity: 'success', summary: SUCCESS_MESSAGES[key] });
  }

  private mutationFailed(key: MutationKey, error: unknown): void {
    this.mutating.set(false);
    const apiError = isApiError(error) ? error : null;
    switch (apiError?.kind) {
      case 'unauthorized':
        this.onUnauthorized();
        return;
      case 'conflict':
        this.onConflict(key);
        return;
      case 'not-found':
        this.dialogKind.set(null);
        this.messageService.add({ severity: 'error', summary: UNAVAILABLE_MESSAGE });
        this.reloadAll();
        return;
      case 'validation':
      case 'bad-request':
        if (this.showFieldErrors(key, apiError)) {
          return;
        }
        break;
    }
    this.messageService.add({ severity: 'error', summary: SAVE_FAILED_MESSAGE });
  }

  /** `400` field errors stay inline in the owning surface; returns false when there are none. */
  private showFieldErrors(key: MutationKey, error: ApiError): boolean {
    const entries = Object.entries(error.fieldErrors);
    if (key === 'upload') {
      this.uploadErrors.set(
        entries.length > 0
          ? entries.flatMap(([, messages]) => messages)
          : [
              error.status === 413
                ? 'These files are too large to send.'
                : "One or more files can't be accepted.",
            ],
      );
      return true;
    }
    if (error.status === 413 || error.status === 415) {
      return false;
    }
    if (entries.length === 0) {
      return false;
    }
    const mapped = Object.fromEntries(
      entries.map(([path, messages]) => [fieldKey(path), messages[0]]),
    );
    if (key === 'note') {
      this.noteError.set(mapped['body'] ?? Object.values(mapped)[0]);
    } else {
      this.dialogErrors.set(mapped);
    }
    return true;
  }

  /** `409`: close the dialog, toast the fixed title and reload detail, board and metrics. */
  private onConflict(key: MutationKey): void {
    const assessment = this.detail()?.assessment;
    const notStarted =
      key === 'complete-assessment' &&
      assessment !== null &&
      assessment !== undefined &&
      new Date(assessment.start).getTime() > Date.now();
    this.dialogKind.set(null);
    this.messageService.add({
      severity: 'error',
      summary: notStarted ? NOT_STARTED_MESSAGE : CONFLICT_MESSAGE,
    });
    this.reloadAll();
  }

  private reloadAll(): void {
    this.detailLoads.next({ id: this.selectedId(), silent: true });
    this.loadBoard();
    this.loadMetrics();
  }

  // New request (FR-15)

  openNewRequest(): void {
    if (this.canManage()) {
      this.newOpen.set(true);
    }
  }

  closeNewRequest(): void {
    this.newOpen.set(false);
  }

  onCreated(detail: RequestDetail): void {
    this.newOpen.set(false);
    this.originCardId = null;
    this.messageService.add({ severity: 'success', summary: 'Request created.' });
    this.detail.set(detail);
    this.detailState.set('ready');
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { request: detail.id },
      queryParamsHandling: 'merge',
    });
    this.loadBoard();
    this.loadMetrics();
  }

  onUnauthorized(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }
}
