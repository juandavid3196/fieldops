import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';

import { canSeeBusinessOverview, canSeeDashboard } from '../../../../core/config/dashboard-roles';
import { SessionService } from '../../../../core/services/session.service';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { addDays, todayInZone } from '../../../schedule/utils/schedule-range';
import { DashboardAnalytics } from '../../components/dashboard-analytics/dashboard-analytics';
import { DashboardAttention } from '../../components/dashboard-attention/dashboard-attention';
import { DashboardKpisRow } from '../../components/dashboard-kpis/dashboard-kpis';
import { DashboardOperation } from '../../components/dashboard-operation/dashboard-operation';
import { DashboardViewSwitch } from '../../components/dashboard-view-switch/dashboard-view-switch';
import { DashboardTechnicians } from '../../components/dashboard-technicians/dashboard-technicians';
import { DashboardPeriod } from '../../models/dashboard.model';
import { DashboardPageStore } from '../../services/dashboard-page.store';
import {
  DATE_RANGE_DAYS,
  activeFilterCount,
  boardLink,
  resolveRoute,
} from '../../utils/dashboard-params';
import { agoLabel, hiddenTooLong, shouldAutoRefresh } from '../../utils/dashboard-refresh';
import { longDate } from '../../utils/dashboard-view';

export const FORBIDDEN_MESSAGE = "You don't have access to Dashboard.";
export const NO_BRANCHES_MESSAGE = 'No branches available.';
export const LOAD_ERROR_MESSAGE = "We couldn't load the dashboard.";
export const REFRESH_ERROR_MESSAGE = "Couldn't refresh.";

const ALL = 'all';
const TICK_MS = 1000;

/**
 * Operations dashboard (`/dashboard`): URL-driven branch, date, filters and performance period,
 * KPIs, today's operation, needs attention, technicians and analytics. Roles without Manage get
 * the forbidden state with no data requests (BR-01); backend authorization is authoritative.
 */
@Component({
  selector: 'app-dashboard',
  imports: [
    DashboardAnalytics,
    DashboardAttention,
    DashboardKpisRow,
    DashboardOperation,
    DashboardTechnicians,
    DashboardViewSwitch,
    FormsModule,
    Message,
    RouterLink,
    Select,
    Skeleton,
  ],
  providers: [DashboardPageStore],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardPage {
  protected readonly store = inject(DashboardPageStore);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  readonly forbiddenMessage = FORBIDDEN_MESSAGE;
  readonly noBranchesMessage = NO_BRANCHES_MESSAGE;
  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly refreshErrorMessage = REFRESH_ERROR_MESSAGE;
  readonly skeletons = [0, 1, 2];
  readonly kpiSkeletons = [0, 1, 2, 3, 4];

  private readonly canRead = computed(() =>
    canSeeDashboard(this.sessionService.session()?.role.code),
  );
  /** business-overview BR-01: only the Owner switches between the Business and Operations views. */
  readonly isOwner = computed(() =>
    canSeeBusinessOverview(this.sessionService.session()?.role.code),
  );
  readonly forbidden = computed(() => !this.canRead() || this.store.access() === 'forbidden');
  private readonly sessionExpired = signal(false);

  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  private readonly now = signal(Date.now());
  private hiddenAt: number | null = null;

  readonly filtersOpen = signal(false);
  private readonly attention = viewChild(DashboardAttention);

  readonly ready = computed(() => this.store.optionsStatus() === 'ready');
  readonly noBranches = computed(
    () => this.ready() && (this.store.options()?.branches.length ?? 0) === 0,
  );
  readonly dashboardRoute = computed(() => this.store.route());
  readonly date = computed(() => this.dashboardRoute()?.date ?? '');
  private readonly today = computed(() => todayInZone(this.store.zone(), new Date(this.now())));
  readonly isToday = computed(() => this.date() === this.today());
  readonly isFuture = computed(() => this.date() > this.today());
  readonly minDate = computed(() => addDays(this.today(), -DATE_RANGE_DAYS));
  readonly maxDate = computed(() => addDays(this.today(), DATE_RANGE_DAYS));
  readonly agoLabel = computed(() => agoLabel(this.store.lastUpdatedAt(), this.now()));
  readonly filterCount = computed(() => {
    const value = this.dashboardRoute();
    return value === null ? 0 : activeFilterCount(value);
  });

  readonly branchChoices = computed(() => {
    const branches = this.store.options()?.branches ?? [];
    const all = branches.length > 1 ? [{ id: ALL, name: 'All branches' }] : [];
    return [...all, ...branches.map(({ id, name }) => ({ id, name }))];
  });
  readonly branchValue = computed(() => this.dashboardRoute()?.branchId ?? ALL);
  readonly branchName = computed(() => {
    const id = this.dashboardRoute()?.branchId ?? null;
    return this.store.options()?.branches.find((branch) => branch.id === id)?.name ?? null;
  });
  readonly subtitle = computed(() =>
    this.date() === ''
      ? ''
      : `${longDate(this.date())} · ${this.branchName() === null ? 'All branches' : `${this.branchName()} operations`}`,
  );
  readonly technicianChoices = computed(() => {
    const branchId = this.dashboardRoute()?.branchId ?? null;
    return (this.store.options()?.technicians ?? [])
      .filter((item) => branchId === null || item.branchId === branchId)
      .map((item) => ({ id: item.id, name: item.fullName }));
  });
  readonly categoryChoices = computed(() => [...(this.store.options()?.categories ?? [])]);
  readonly board = computed(() => boardLink(this.dashboardRoute()?.branchId ?? null, this.date()));

  constructor() {
    if (!this.forbidden()) {
      this.store.loadOptions();
    }

    // URL query is the single source of the route; invalid values are replaced by defaults.
    effect(() => {
      const params = this.queryParams();
      const options = this.store.options();
      if (this.forbidden() || options === null || options.branches.length === 0) {
        return;
      }
      untracked(() => {
        const resolved = resolveRoute(params, options);
        if (resolved.normalized) {
          this.go(resolved.query);
        } else {
          this.store.applyRoute(resolved.route);
        }
      });
    });

    // A filter, date or period the API rejected goes back to its default (replaceUrl).
    effect(() => {
      const reset = this.store.reset();
      if (reset === null) {
        return;
      }
      untracked(() => {
        this.store.reset.set(null);
        // ResetField values are the query parameter names.
        this.go({ [reset.field]: null });
      });
    });

    effect(() => {
      if (this.store.access() === 'unauthorized') {
        untracked(() => handleUnauthorized(this.router, this.sessionExpired));
      }
    });

    // One clock feeds the "Updated" label and the 60 s refresh while the tab is visible.
    const timer = setInterval(() => this.onTick(), TICK_MS);
    const onVisibility = (): void => this.onVisibility();
    this.document.addEventListener('visibilitychange', onVisibility);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      this.document.removeEventListener('visibilitychange', onVisibility);
    });
  }

  private onTick(): void {
    const now = Date.now();
    this.now.set(now);
    if (
      !this.forbidden() &&
      this.store.route() !== null &&
      shouldAutoRefresh({
        visible: this.document.visibilityState === 'visible',
        loading: this.store.status() === 'loading',
        lastAttemptAt: this.store.lastAttemptAt,
        now,
      })
    ) {
      this.store.refresh();
    }
  }

  private onVisibility(): void {
    const now = Date.now();
    if (this.document.visibilityState === 'hidden') {
      this.hiddenAt = now;
      return;
    }
    const wasHiddenTooLong = hiddenTooLong(this.hiddenAt, now);
    this.hiddenAt = null;
    if (wasHiddenTooLong && !this.forbidden() && this.store.route() !== null) {
      this.store.refresh();
    }
  }

  private go(params: Record<string, string | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // Header controls (user changes replace the URL: the dashboard has no unsaved state)

  onBranch(value: string): void {
    this.go({ branch: value === ALL ? null : value });
  }

  onDate(value: string): void {
    if (value !== '') {
      this.go({ date: value });
    }
  }

  onToday(): void {
    this.go({ date: this.today() });
  }

  onTechnician(id: string | null): void {
    this.go({ technician: id });
  }

  onCategory(id: string | null): void {
    this.go({ category: id });
  }

  onClearFilters(): void {
    this.go({ technician: null, category: null });
  }

  onPeriod(period: DashboardPeriod): void {
    this.go({ period });
  }

  onReview(): void {
    this.attention()?.focus();
  }
}
