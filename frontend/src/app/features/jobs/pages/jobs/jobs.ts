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
import { MenuItem } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Menu } from 'primeng/menu';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
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
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import {
  JOBS_EMPTY_MESSAGE,
  JOBS_FILTERED_EMPTY_MESSAGE,
  JOBS_LOAD_ERROR_MESSAGE,
  JOBS_METRICS_ERROR_MESSAGE,
  JOBS_NO_BRANCHES_MESSAGE,
  JOBS_PAGE_SIZES,
  JobRow,
  JobsOptions,
  JobsPage,
  JobsRequest,
  JobsSummary,
  JobsTab,
  JobsView,
} from '../../models/jobs.model';
import { WO_FORBIDDEN_MESSAGE, WORK_ORDER_MANAGE_ROLES } from '../../models/work-order.model';
import { WorkOrdersService } from '../../services/work-orders.service';
import {
  JOBS_PRIORITIES,
  JOBS_SORTS,
  JOBS_TABS,
  JOB_STATUS_CHIPS,
  TAB_STATUSES,
  activeFilterCount,
  technicianAvatarColors,
  fullDateTime,
  isCanonicalJobsUrl,
  jobActionTargets,
  jobsViewParams,
  propertyLocation,
  resolveJobsView,
  scheduleText,
  updatedText,
} from '../../utils/jobs-format';
import { PRIORITY_LABELS } from '../../utils/work-order-format';

import { localizedList, t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
interface Region<T> {
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly data: T | null;
}

interface Choice {
  readonly code: string;
  readonly label: string;
}

interface RowView {
  readonly row: JobRow;
  readonly chip: { readonly label: string; readonly tone: string };
  readonly location: string;
  readonly schedule: string;
  readonly scheduled: boolean;
  readonly updated: string;
  readonly updatedFull: string;
  readonly priority: string;
  readonly emphasized: boolean;
  readonly color: string | null;
}

const IDLE = { status: 'idle', data: null } as const;
const ALL = 'all';
const SEARCH_DEBOUNCE_MS = 300;
const FIELD_PARAMS = ['tab', 'q', 'status', 'priority', 'sort', 'page', 'pageSize'];

/**
 * Jobs workspace (`/jobs`, Design 30): metrics, tabs with counts, filters and a server-paged table.
 * The URL holds the view (BR-16); invalid values are normalized with `replaceUrl` before any request.
 */
@Component({
  selector: 'app-jobs',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
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
  ],
  templateUrl: './jobs.html',
  styleUrl: './jobs.scss',
})
export class Jobs {
  private readonly api = inject(WorkOrdersService);
  private readonly sessionService = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  get forbiddenMessage(): string {
    return WO_FORBIDDEN_MESSAGE();
  }
  get errorMessage(): string {
    return JOBS_LOAD_ERROR_MESSAGE();
  }
  get metricsErrorMessage(): string {
    return JOBS_METRICS_ERROR_MESSAGE();
  }
  get noBranchesMessage(): string {
    return JOBS_NO_BRANCHES_MESSAGE();
  }
  readonly tabs = JOBS_TABS;
  readonly priorityOptions: Choice[] = localizedList(() => [
    { code: ALL, label: t('jobs.jobs.allPriorities') },
    ...JOBS_PRIORITIES.map(({ code }) => ({ code, label: PRIORITY_LABELS[code] })),
  ]);
  readonly sortOptions: Choice[] = JOBS_SORTS.map(({ code, label }) => ({ code, label }));
  readonly pageSizeOptions: number[] = [...JOBS_PAGE_SIZES];
  readonly skeletonRows = [1, 2, 3, 4, 5];

  readonly sessionExpired = signal(false);
  /** UX only: Read = Manage + viewer (BR-01); the backend decides. */
  private readonly allowed = [...WORK_ORDER_MANAGE_ROLES, 'viewer'].includes(
    this.sessionService.session()?.role.code ?? '',
  );
  readonly forbidden = signal(!this.allowed);

  readonly options = signal<Region<JobsOptions>>(IDLE);
  readonly summary = signal<Region<JobsSummary>>(IDLE);
  readonly list = signal<Region<JobsPage>>(IDLE);
  private readonly counts = signal<JobsPage['counts'] | null>(null);
  private readonly now = signal(new Date());
  readonly rowMenu = signal<MenuItem[]>([]);

  private readonly query = toSignal(this.route.queryParamMap, { requireSync: true });
  private readonly branches = computed(() => this.options().data?.branches ?? []);
  readonly noBranches = computed(
    () => this.options().status === 'ready' && this.branches().length === 0,
  );
  private readonly defaultBranchId = computed(() =>
    this.branches().length === 1 ? this.branches()[0].id : null,
  );
  readonly view = computed<JobsView>(() =>
    resolveJobsView(
      (key) => this.query().get(key),
      this.options().data === null ? null : this.branches().map((branch) => branch.id),
      this.defaultBranchId(),
    ),
  );
  private readonly canonical = computed(() =>
    isCanonicalJobsUrl((key) => this.query().get(key), this.view()),
  );
  /** The branch sent to the API: the selected one, or the only one in scope. */
  readonly branchId = computed(() => this.view().branch ?? this.defaultBranchId());

  readonly searchText = linkedSignal(() => this.view().q);
  readonly filterCount = computed(() => activeFilterCount(this.view()));
  readonly counted = computed(() => this.counts());
  readonly canChooseBranch = computed(() => this.branches().length > 1);
  readonly branchChoices = computed<Choice[]>(() => [
    ...(this.canChooseBranch() ? [{ code: ALL, label: t('jobs.jobs.allBranches') }] : []),
    ...this.branches().map((branch) => ({ code: branch.id, label: branch.name })),
  ]);
  readonly branchValue = computed(
    () => this.branchId() ?? (this.canChooseBranch() ? ALL : (this.branches()[0]?.id ?? ALL)),
  );
  readonly statusChoices = computed<Choice[]>(() => [
    { code: ALL, label: t('jobs.jobs.allStatuses') },
    ...TAB_STATUSES[this.view().tab].map((code) => ({
      code,
      label: JOB_STATUS_CHIPS[code].label,
    })),
  ]);
  readonly scheduleParams = computed(() => ({ branch: this.branchId() }));
  readonly readyBadge = computed(() => this.summary().data?.readyToSchedule ?? 0);
  readonly controlsDisabled = computed(() => this.options().status !== 'ready');

  readonly total = computed(() => this.list().data?.total ?? 0);
  readonly announcement = computed(() =>
    this.list().status === 'ready' ? t('jobs.jobs.jobs2', { total: this.total() }) : '',
  );
  readonly rows = computed<RowView[]>(() => {
    const now = this.now();
    const orgZone = this.options().data?.organizationTimezone ?? 'UTC';
    const items = this.list().data?.items ?? [];
    const avatarColors = technicianAvatarColors(
      items.flatMap((row) => (row.technician === null ? [] : [row.technician])),
    );
    return items.map((row) => ({
      row,
      chip: JOB_STATUS_CHIPS[row.status],
      location: propertyLocation(row),
      schedule: scheduleText(row, now),
      scheduled: row.currentVisit?.scheduledStart != null,
      updated: updatedText(row.updatedAt, orgZone, now),
      updatedFull: fullDateTime(row.updatedAt, orgZone),
      priority: PRIORITY_LABELS[row.priority],
      emphasized: row.priority === 'high' || row.priority === 'urgent',
      color: row.technician === null ? null : (avatarColors.get(row.technician.fullName) ?? null),
    }));
  });
  readonly emptyMessage = computed(() =>
    this.view().tab === 'all' && this.filterCount() === 0 && this.total() === 0
      ? JOBS_EMPTY_MESSAGE()
      : JOBS_FILTERED_EMPTY_MESSAGE(),
  );
  readonly showClearInEmpty = computed(() => this.emptyMessage() === JOBS_FILTERED_EMPTY_MESSAGE());
  readonly footer = computed(() => {
    const { page, pageSize } = this.view();
    const total = this.total();
    return t('jobs.jobs.of', {
      value: (page - 1) * pageSize + 1,
      value2: Math.min(page * pageSize, total),
      total: total,
    });
  });

  /** Request gates: null until the options are known and the URL is canonical. */
  private readonly listRequest = computed<JobsRequest | null>(
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
        priority: view.priority,
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
  private readonly list$ = new Subject<JobsRequest>();
  private readonly searches$ = new Subject<string>();

  constructor() {
    this.options$
      .pipe(
        switchMap(() => this.fetch(this.options, this.api.jobsOptions())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.summary$
      .pipe(
        switchMap((branchId) => this.fetch(this.summary, this.api.jobsSummary(branchId))),
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
    // BR-16: invalid URL values are replaced by their defaults before any request.
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
          const reset: Record<string, string | null> = { ...jobsViewParams(view) };
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
  private clampPage(page: JobsPage, request: JobsRequest): void {
    const last = Math.ceil(page.total / request.pageSize);
    if (page.items.length === 0 && page.total > 0 && request.page > last) {
      this.navigate({ ...this.view(), page: last }, true);
    }
  }

  private navigate(view: JobsView, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: jobsViewParams(view),
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  /** Any change resets to page 1 (BR-16); `page` itself is set explicitly by the pager. */
  private change(patch: Partial<JobsView>): void {
    this.navigate({ ...this.view(), ...patch, page: patch.page ?? 1 });
  }

  // Header and tabs

  readyToSchedule(): void {
    this.change({ tab: 'unscheduled', status: null });
  }

  selectTab(tab: JobsTab): void {
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

  changePriority(code: string): void {
    this.change({
      priority: JOBS_PRIORITIES.find((priority) => priority.code === code)?.code ?? null,
    });
  }

  changeSort(code: string): void {
    this.change({ sort: JOBS_SORTS.find((sort) => sort.code === code)?.code ?? 'newest' });
  }

  clearFilters(): void {
    this.change({ q: '', status: null, priority: null });
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

  // Retry (only the failed part) and row menu

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

  openRowMenu(event: Event, row: JobRow, menu: Menu): void {
    this.rowMenu.set(
      jobActionTargets(row).map((target) => ({
        label: target.label,
        routerLink: [...target.commands],
        ...(target.queryParams === null ? {} : { queryParams: { ...target.queryParams } }),
      })),
    );
    menu.toggle(event);
  }
}
