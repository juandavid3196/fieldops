import {
  Component,
  DestroyRef,
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
import { MenuItem, MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
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

import { ApiError, isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { formatMoney } from '../../../customers/utils/customer-detail-format';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { MANAGER_ROLES, READ_ROLES } from '../../../requests/models/requests.model';
import {
  QUOTES_EMPTY_MESSAGE,
  QUOTES_FILTERED_EMPTY_MESSAGE,
  QUOTES_LOAD_ERROR_MESSAGE,
  QUOTES_METRICS_ERROR_MESSAGE,
  QUOTES_NO_BRANCHES_MESSAGE,
  QUOTES_PAGE_SIZES,
  QuoteRow,
  QuotesOptions,
  QuotesPage,
  QuotesRequest,
  QuotesSummary,
  QuotesTab,
  QuotesView,
} from '../../models/quotes-list.model';
import {
  QUOTE_CHANGED_MESSAGE,
  QUOTE_SAVE_FAILED_MESSAGE,
  QUOTE_UNAVAILABLE_MESSAGE,
  VIEW_FORBIDDEN_MESSAGE,
} from '../../models/quote.model';
import { QuotesService } from '../../services/quotes.service';
import {
  QUOTES_DATES,
  QUOTES_SORTS,
  QUOTES_TABS,
  QUOTE_STATUS_CHIPS,
  TAB_STATUSES,
  activeFilterCount,
  fullDateTime,
  inlineAction,
  isCanonicalQuotesUrl,
  propertyText,
  quoteActionTargets,
  quotesViewParams,
  readyNoticeText,
  resolveQuotesView,
  sentText,
  totalText,
  updatedText,
} from '../../utils/quotes-list-format';

interface Region<T> {
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly data: T | null;
}

interface Choice {
  readonly code: string;
  readonly label: string;
}

interface RowView {
  readonly row: QuoteRow;
  readonly chip: { readonly label: string; readonly tone: string };
  readonly property: string;
  readonly total: string;
  readonly sent: string;
  readonly updated: string;
  readonly updatedFull: string;
  readonly inline: { readonly label: string; readonly commands: readonly string[] };
}

const IDLE = { status: 'idle', data: null } as const;
const ALL = 'all';
const SEARCH_DEBOUNCE_MS = 300;
const FIELD_PARAMS = ['tab', 'q', 'status', 'date', 'sort', 'page', 'pageSize'];
const NOTICE_KEY = 'fieldops.quotes.ready-notice-dismissed';

function noticeDismissed(): boolean {
  try {
    return sessionStorage.getItem(NOTICE_KEY) === '1';
  } catch {
    return false;
  }
}

/**
 * Quotes workspace (`/quotes`, Design 31): metrics, ready-requests notice, tabs with counts,
 * filters and a server-paged table. The URL holds the view (BR-19); invalid values are normalized
 * with `replaceUrl` before any request.
 */
@Component({
  selector: 'app-quotes',
  imports: [
    ButtonDirective,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    Menu,
    Paginator,
    RouterLink,
    Select,
    Skeleton,
    Toast,
  ],
  providers: [MessageService],
  templateUrl: './quotes.html',
  styleUrl: './quotes.scss',
})
export class Quotes {
  private readonly api = inject(QuotesService);
  private readonly sessionService = inject(SessionService);
  private readonly messageService = inject(MessageService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly forbiddenMessage = VIEW_FORBIDDEN_MESSAGE;
  readonly errorMessage = QUOTES_LOAD_ERROR_MESSAGE;
  readonly metricsErrorMessage = QUOTES_METRICS_ERROR_MESSAGE;
  readonly noBranchesMessage = QUOTES_NO_BRANCHES_MESSAGE;
  readonly tabs = QUOTES_TABS;
  readonly dateOptions: Choice[] = QUOTES_DATES.map(({ code, label }) => ({ code, label }));
  readonly sortOptions: Choice[] = QUOTES_SORTS.map(({ code, label }) => ({ code, label }));
  readonly pageSizeOptions: number[] = [...QUOTES_PAGE_SIZES];
  readonly skeletonRows = [1, 2, 3, 4, 5];

  readonly sessionExpired = signal(false);
  /** UX only: Read = Manage + read roles; the backend decides (BR-01). */
  private readonly allowed = [...MANAGER_ROLES, ...READ_ROLES].includes(
    this.sessionService.session()?.role.code ?? '',
  );
  readonly forbidden = signal(!this.allowed);

  readonly options = signal<Region<QuotesOptions>>(IDLE);
  readonly summary = signal<Region<QuotesSummary>>(IDLE);
  readonly list = signal<Region<QuotesPage>>(IDLE);
  private readonly counts = signal<QuotesPage['counts'] | null>(null);
  private readonly now = signal(new Date());
  readonly rowMenu = signal<MenuItem[]>([]);
  private readonly revising = signal<string | null>(null);
  private readonly noticeHidden = signal(noticeDismissed());

  private readonly query = toSignal(this.route.queryParamMap, { requireSync: true });
  private readonly branches = computed(() => this.options().data?.branches ?? []);
  readonly noBranches = computed(
    () => this.options().status === 'ready' && this.branches().length === 0,
  );
  private readonly defaultBranchId = computed(() =>
    this.branches().length === 1 ? this.branches()[0].id : null,
  );
  readonly view = computed<QuotesView>(() =>
    resolveQuotesView(
      (key) => this.query().get(key),
      this.options().data === null ? null : this.branches().map((branch) => branch.id),
      this.defaultBranchId(),
    ),
  );
  private readonly canonical = computed(() =>
    isCanonicalQuotesUrl((key) => this.query().get(key), this.view()),
  );
  /** The branch sent to the API: the selected one, or the only one in scope. */
  readonly branchId = computed(() => this.view().branch ?? this.defaultBranchId());

  readonly searchText = linkedSignal(() => this.view().q);
  readonly filterCount = computed(() => activeFilterCount(this.view()));
  readonly counted = computed(() => this.counts());
  readonly canChooseBranch = computed(() => this.branches().length > 1);
  readonly branchChoices = computed<Choice[]>(() => [
    ...(this.canChooseBranch() ? [{ code: ALL, label: 'All branches' }] : []),
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly branchValue = computed(
    () => this.branchId() ?? (this.canChooseBranch() ? ALL : (this.branches()[0]?.id ?? ALL)),
  );
  readonly statusChoices = computed<Choice[]>(() => [
    { code: ALL, label: 'All statuses' },
    ...TAB_STATUSES[this.view().tab].map((code) => ({
      code,
      label: QUOTE_STATUS_CHIPS[code].label,
    })),
  ]);
  readonly controlsDisabled = computed(() => this.options().status !== 'ready');

  // BR-18: badge and notice need the metrics to be loaded.
  readonly readyCount = computed(() =>
    this.summary().status === 'ready' ? (this.summary().data?.readyRequests ?? 0) : 0,
  );
  readonly noticeText = computed(() => readyNoticeText(this.readyCount()));
  readonly showNotice = computed(() => this.readyCount() > 0 && !this.noticeHidden());
  readonly readyParams = { status: 'ready_for_quote' };

  readonly metrics = computed(() => {
    const data = this.summary().data;
    if (data === null) {
      return null;
    }
    const money = (amount: number): string => formatMoney(amount, data.currency);
    return [
      {
        label: 'Drafts',
        value: data.drafts.count,
        caption: `${money(data.drafts.amount)} total`,
        icon: 'pi-file-edit',
        tone: '',
      },
      {
        label: 'Awaiting response',
        value: data.awaitingResponse.count,
        caption: `${money(data.awaitingResponse.amount)} sent`,
        icon: 'pi-send',
        tone: 'info',
      },
      {
        label: 'Approved',
        value: data.approvedThisMonth.count,
        caption: `${money(data.approvedThisMonth.amount)} this month`,
        icon: 'pi-check-circle',
        tone: 'success',
      },
      {
        label: 'Needs attention',
        value: data.needsAttention.count,
        caption: 'Questions or expiring soon',
        icon: 'pi-exclamation-triangle',
        tone: 'warning',
      },
    ];
  });

  readonly total = computed(() => this.list().data?.total ?? 0);
  readonly announcement = computed(() =>
    this.list().status === 'ready' ? `${this.total()} quotes` : '',
  );
  readonly rows = computed<RowView[]>(() => {
    const now = this.now();
    const zone = this.options().data?.organizationTimezone ?? 'UTC';
    return (this.list().data?.items ?? []).map((row) => {
      const inline = inlineAction(row);
      return {
        row,
        chip: QUOTE_STATUS_CHIPS[row.status],
        property: propertyText(row),
        total: totalText(row),
        sent: sentText(row, zone, now),
        updated: updatedText(row.updatedAt, zone, now),
        updatedFull: fullDateTime(row.updatedAt, zone),
        inline: {
          label: inline.label === 'Continue draft' ? 'Continue' : 'View',
          commands: inline.commands ?? [],
        },
      };
    });
  });
  readonly emptyMessage = computed(() =>
    this.view().tab === 'all' && this.filterCount() === 0 && this.total() === 0
      ? QUOTES_EMPTY_MESSAGE
      : QUOTES_FILTERED_EMPTY_MESSAGE,
  );
  readonly showClearInEmpty = computed(() => this.emptyMessage() === QUOTES_FILTERED_EMPTY_MESSAGE);
  readonly footer = computed(() => {
    const { page, pageSize } = this.view();
    const total = this.total();
    return `${(page - 1) * pageSize + 1}–${Math.min(page * pageSize, total)} of ${total}`;
  });

  /** Request gates: null until the options are known and the URL is canonical. */
  private readonly listRequest = computed<QuotesRequest | null>(
    () => {
      if (this.forbidden() || this.options().status !== 'ready' || this.noBranches()) {
        return null;
      }
      if (!this.canonical()) {
        return null;
      }
      const view = this.view();
      return {
        tab: view.tab,
        q: view.q,
        branchId: this.branchId(),
        status: view.status,
        date: view.date,
        sort: view.sort,
        page: view.page,
        pageSize: view.pageSize,
      };
    },
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );
  private readonly summaryBranch = computed<string | null | false>(() =>
    this.forbidden() || this.options().status !== 'ready' || this.noBranches()
      ? false
      : this.branchId(),
  );

  private readonly options$ = new Subject<void>();
  private readonly summary$ = new Subject<string | null>();
  private readonly list$ = new Subject<QuotesRequest>();
  private readonly searches$ = new Subject<string>();

  constructor() {
    this.options$
      .pipe(
        switchMap(() => this.fetch(this.options, this.api.listOptions())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.summary$
      .pipe(
        switchMap((branchId) => this.fetch(this.summary, this.api.listSummary(branchId))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.list$
      .pipe(
        switchMap((request) =>
          this.fetch(this.list, this.api.list(request)).pipe(
            tap((page) => {
              this.counts.set(page.counts);
              this.now.set(new Date());
              this.clampPage(page, request);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.searches$
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((text) => {
        const term = text.trim();
        if (term !== this.view().q) {
          this.change({ q: term });
        }
      });

    if (!this.allowed) {
      return;
    }
    // BR-19: invalid URL values are replaced by their defaults before any request.
    effect(() => {
      if (this.options().status === 'ready' && !this.canonical()) {
        const view = this.view();
        untracked(() => this.navigate(view, true));
      }
    });
    effect(() => {
      const request = this.listRequest();
      if (request !== null) {
        untracked(() => this.list$.next(request));
      }
    });
    // BR-11: metrics and the ready count refresh only when the branch changes.
    effect(() => {
      const branch = this.summaryBranch();
      if (branch !== false) {
        untracked(() => this.summary$.next(branch));
      }
    });
    this.options$.next();
  }

  /** Loads one region, keeping the previous data while reloading. */
  private fetch<T>(
    region: ReturnType<typeof signal<Region<T>>>,
    request: Observable<T>,
  ): Observable<T> {
    region.update((current) => ({ status: 'loading', data: current.data }));
    return request.pipe(
      tap((data) => region.set({ status: 'ready', data })),
      catchError((error: unknown) => {
        region.set({ status: 'error', data: null });
        this.handleFailure(error);
        return EMPTY;
      }),
    );
  }

  private handleFailure(error: unknown): void {
    const api = isApiError(error) ? error : null;
    const view = this.view();
    switch (api?.kind) {
      case 'unauthorized':
        handleUnauthorized(this.router, this.sessionExpired);
        break;
      case 'forbidden':
        this.forbidden.set(true);
        break;
      case 'not-found':
        // A foreign, inactive or out-of-scope branch resets to the default branch.
        if (view.branch !== null) {
          this.navigate({ ...view, branch: null }, true);
        }
        break;
      case 'validation':
      case 'bad-request': {
        // A server `400` resets the offending parameter to its default.
        const bad = FIELD_PARAMS.filter(
          (key) => api.fieldErrors[key] !== undefined && this.query().get(key) !== null,
        );
        if (bad.length > 0) {
          const reset: Record<string, string | null> = { ...quotesViewParams(view) };
          for (const key of bad) {
            reset[key] = null;
          }
          void this.router.navigate([], {
            relativeTo: this.route,
            queryParams: reset,
            queryParamsHandling: 'merge',
            replaceUrl: true,
          });
        }
        break;
      }
    }
  }

  /** BR-10: a page past the last moves to the last page. */
  private clampPage(page: QuotesPage, request: QuotesRequest): void {
    const last = Math.ceil(page.total / request.pageSize);
    if (page.items.length === 0 && page.total > 0 && request.page > last) {
      this.navigate({ ...this.view(), page: last }, true);
    }
  }

  private navigate(view: QuotesView, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: quotesViewParams(view),
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  /** Any change resets to page 1 (BR-19); `page` itself is set explicitly by the pager. */
  private change(patch: Partial<QuotesView>): void {
    this.navigate({ ...this.view(), ...patch, page: patch.page ?? 1 });
  }

  // Notice (BR-18)

  dismissNotice(): void {
    this.noticeHidden.set(true);
    try {
      sessionStorage.setItem(NOTICE_KEY, '1');
    } catch {
      // Storage unavailable: the notice is hidden for this page only.
    }
  }

  // Tabs

  selectTab(tab: QuotesTab): void {
    if (tab !== this.view().tab) {
      this.change({ tab, status: null });
    }
  }

  onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const keys: Record<string, number> = {
      ArrowRight: (index + 1) % this.tabs.length,
      ArrowLeft: (index + last) % this.tabs.length,
      Home: 0,
      End: last,
    };
    const next = keys[event.key];
    if (next === undefined) {
      return;
    }
    event.preventDefault();
    this.selectTab(this.tabs[next].code);
    queueMicrotask(() =>
      (event.currentTarget as HTMLElement | null)?.parentElement
        ?.querySelectorAll<HTMLElement>('[role="tab"]')
        .item(next)
        ?.focus(),
    );
  }

  // Filters

  searchChanged(text: string): void {
    this.searchText.set(text);
    this.searches$.next(text);
  }

  changeBranch(code: string): void {
    this.change({ branch: code === ALL || code === this.defaultBranchId() ? null : code });
  }

  changeStatus(code: string): void {
    this.change({
      status: TAB_STATUSES[this.view().tab].find((status) => status === code) ?? null,
    });
  }

  changeDate(code: string): void {
    this.change({ date: QUOTES_DATES.find((date) => date.code === code)?.code ?? 'any' });
  }

  changeSort(code: string): void {
    this.change({ sort: QUOTES_SORTS.find((sort) => sort.code === code)?.code ?? 'updated' });
  }

  clearFilters(): void {
    this.change({ q: '', status: null, date: 'any' });
  }

  // Pagination

  changePage(event: PaginatorState): void {
    const page = (event.page ?? 0) + 1;
    if (page !== this.view().page) {
      this.change({ page });
    }
  }

  changePageSize(size: number): void {
    this.change({ pageSize: size });
  }

  // Retry (only the failed part)

  retryOptions(): void {
    this.options$.next();
  }

  retrySummary(): void {
    this.summary$.next(this.branchId());
  }

  retryList(): void {
    const request = this.listRequest();
    if (request !== null) {
      this.list$.next(request);
    }
  }

  // Row menu and Revise (BR-15, BR-16)

  openRowMenu(event: Event, row: QuoteRow, menu: Menu): void {
    const inlineCode = inlineAction(row).code;
    this.rowMenu.set(
      quoteActionTargets(row)
        .filter((target) => target.code !== inlineCode)
        .map((target) =>
          target.commands === null
            ? {
                label: target.label,
                disabled: this.revising() !== null,
                command: () => this.revise(row),
              }
            : {
                label: target.label,
                routerLink: [...target.commands],
                ...(target.queryParams === null ? {} : { queryParams: { ...target.queryParams } }),
              },
        ),
    );
    menu.toggle(event);
  }

  revise(row: QuoteRow): void {
    if (this.revising() !== null) {
      return;
    }
    this.revising.set(row.id);
    this.api
      .revise(row.id, row.updatedAt)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.revising.set(null);
          void this.router.navigate(['/quotes', row.id, 'edit']);
        },
        error: (error: unknown) => this.reviseFailed(isApiError(error) ? error : null),
      });
  }

  private reviseFailed(error: ApiError | null): void {
    this.revising.set(null);
    switch (error?.kind) {
      case 'unauthorized':
        handleUnauthorized(this.router, this.sessionExpired);
        return;
      case 'conflict':
        this.messageService.add({ severity: 'warn', summary: QUOTE_CHANGED_MESSAGE });
        this.retryList();
        this.retrySummary();
        return;
      case 'forbidden':
      case 'not-found':
        this.messageService.add({ severity: 'error', summary: QUOTE_UNAVAILABLE_MESSAGE });
        this.retryList();
        return;
      default:
        this.messageService.add({ severity: 'error', summary: QUOTE_SAVE_FAILED_MESSAGE });
    }
  }
}
