import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';

import { ApiError, isApiError } from '../../../core/models/api-error.model';
import {
  DashboardOptions,
  DashboardQuery,
  DashboardResponse,
  DashboardRoute,
  PerformanceQuery,
  PerformanceResponse,
} from '../models/dashboard.model';
import { zoneOf } from '../utils/dashboard-params';
import { todayInZone } from '../../schedule/utils/schedule-range';
import { DashboardService } from './dashboard.service';

export type LoadStatus = 'loading' | 'ready' | 'error';
export type AccessFailure = 'forbidden' | 'unauthorized' | null;
/** A URL value the API rejected; the page resets it to its default (replaceUrl). */
export type ResetField = 'technician' | 'category' | 'date' | 'period';

interface Request<TQuery> {
  readonly silent: boolean;
  readonly query: TQuery;
}

type Result<T> =
  | { readonly ok: true; readonly response: T }
  | { readonly ok: false; readonly error: ApiError | null };

const ROUTE_KEYS = ['branchId', 'date', 'technicianId', 'categoryId'] as const;

/**
 * Page-scoped state of the operations dashboard. The URL query is the single source of the route
 * and is written by the page, never here. Dashboard and performance load independently so a
 * performance failure never blanks the page (BR-16).
 */
@Injectable()
export class DashboardPageStore {
  private readonly api = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly dashboardRequests = new Subject<Request<DashboardQuery>>();
  private readonly performanceRequests = new Subject<Request<PerformanceQuery>>();

  readonly options = signal<DashboardOptions | null>(null);
  readonly optionsStatus = signal<LoadStatus>('loading');
  readonly access = signal<AccessFailure>(null);
  readonly route = signal<DashboardRoute | null>(null);

  readonly data = signal<DashboardResponse | null>(null);
  readonly status = signal<LoadStatus>('loading');
  readonly refreshing = signal(false);
  readonly refreshFailed = signal(false);
  readonly lastUpdatedAt = signal<number | null>(null);
  /** Start of the last load attempt (ms); drives the 60 s tick. */
  lastAttemptAt: number | null = null;

  readonly performance = signal<PerformanceResponse | null>(null);
  readonly performanceStatus = signal<LoadStatus>('loading');

  readonly reset = signal<{ readonly field: ResetField } | null>(null);

  readonly zone = computed(() => {
    const options = this.options();
    return options === null ? 'UTC' : zoneOf(options, this.route()?.branchId ?? null);
  });
  readonly loading = computed(
    () => this.status() === 'loading' || this.optionsStatus() === 'loading',
  );

  constructor() {
    this.dashboardRequests
      .pipe(
        tap((request) => this.begin(request.silent)),
        switchMap((request) =>
          this.run(this.api.dashboard(request.query)).pipe(map((result) => ({ result, request }))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ result, request }) => this.handleDashboard(result, request.silent));

    this.performanceRequests
      .pipe(
        tap((request) => {
          if (!request.silent) {
            this.performance.set(null);
            this.performanceStatus.set('loading');
          }
        }),
        switchMap((request) =>
          this.run(this.api.performance(request.query)).pipe(
            map((result) => ({ result, request })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ result, request }) => this.handlePerformance(result, request.silent));
  }

  private run<T>(source: Observable<T>): Observable<Result<T>> {
    return source.pipe(
      map((response): Result<T> => ({ ok: true, response })),
      catchError((error: unknown) =>
        of<Result<T>>({ ok: false, error: isApiError(error) ? error : null }),
      ),
    );
  }

  private begin(silent: boolean): void {
    if (silent) {
      this.refreshing.set(true);
      return;
    }
    this.refreshing.set(false);
    this.data.set(null);
    this.status.set('loading');
    this.refreshFailed.set(false);
  }

  // Options

  loadOptions(): void {
    if (this.options() === null) {
      this.optionsStatus.set('loading');
    }
    this.api.options().subscribe({
      next: (options) => {
        this.options.set(options);
        this.optionsStatus.set('ready');
      },
      error: (error: unknown) => {
        if (!this.handleAccess(error) && this.options() === null) {
          this.optionsStatus.set('error');
        }
      },
    });
  }

  // Route and loading

  /** Applies the URL state: any change reloads both regions, a period change only performance. */
  applyRoute(next: DashboardRoute): void {
    const previous = this.route();
    this.route.set(next);
    if (previous === null || ROUTE_KEYS.some((key) => previous[key] !== next[key])) {
      this.loadDashboard(false);
      this.loadPerformance(false);
    } else if (previous.period !== next.period) {
      this.loadPerformance(false);
    }
  }

  /** On-demand and automatic refresh keep the shown cards. */
  refresh(): void {
    this.loadDashboard(this.data() !== null);
    this.loadPerformance(this.performance() !== null);
  }

  /** "Try again" from the load-error alert or the performance card. */
  retry(): void {
    if (this.options() === null) {
      this.loadOptions();
      return;
    }
    this.loadDashboard(false);
    this.loadPerformance(false);
  }

  retryPerformance(): void {
    this.loadPerformance(false);
  }

  private loadDashboard(silent: boolean): void {
    const route = this.route();
    if (route !== null) {
      this.lastAttemptAt = Date.now();
      this.dashboardRequests.next({ silent, query: route });
    }
  }

  private loadPerformance(silent: boolean): void {
    const route = this.route();
    if (route !== null) {
      this.performanceRequests.next({ silent, query: route });
    }
  }

  private handleDashboard(result: Result<DashboardResponse>, silent: boolean): void {
    this.refreshing.set(false);
    if (result.ok) {
      this.data.set(result.response);
      this.status.set('ready');
      this.lastUpdatedAt.set(Date.now());
      this.refreshFailed.set(false);
      return;
    }
    if (this.handleRejection(result.error)) {
      return;
    }
    if (silent && this.data() !== null) {
      this.refreshFailed.set(true);
    } else {
      this.status.set('error');
    }
  }

  private handlePerformance(result: Result<PerformanceResponse>, silent: boolean): void {
    if (result.ok) {
      this.performance.set(result.response);
      this.performanceStatus.set('ready');
      return;
    }
    if (this.handleRejection(result.error)) {
      return;
    }
    if (silent && this.performance() !== null) {
      this.refreshFailed.set(true);
    } else {
      this.performanceStatus.set('error');
    }
  }

  /** 401/403 and rejected filters; returns true when the failure was fully handled. */
  private handleRejection(error: ApiError | null): boolean {
    if (this.handleAccess(error)) {
      return true;
    }
    const route = this.route();
    if (error === null || route === null) {
      return false;
    }
    const field = resetField(error, route, todayInZone(this.zone()));
    if (field === 'branch') {
      // The branch left the caller's scope: reload the options; the page re-resolves the URL.
      this.status.set('error');
      this.loadOptions();
      return true;
    }
    if (field !== null) {
      this.reset.set({ field });
      return true;
    }
    return false;
  }

  /** 401 leaves to Sign In and 403 shows the forbidden state; returns true when handled. */
  private handleAccess(error: unknown): boolean {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized' || kind === 'forbidden') {
      this.access.set(kind);
      return true;
    }
    return false;
  }
}

/** BR-04/BR-05: which URL value the API rejected, only when it differs from its default. */
export function resetField(
  error: ApiError,
  route: DashboardRoute,
  today: string,
): ResetField | 'branch' | null {
  const keys = Object.keys(error.fieldErrors).map((key) => key.toLowerCase());
  if (error.status === 404) {
    return route.technicianId !== null ? 'technician' : route.branchId !== null ? 'branch' : null;
  }
  if (error.status !== 400) {
    return null;
  }
  if (keys.includes('categoryid') && route.categoryId !== null) {
    return 'category';
  }
  if (keys.includes('technicianid') && route.technicianId !== null) {
    return 'technician';
  }
  if (keys.includes('branchid') && route.branchId !== null) {
    return 'branch';
  }
  if (keys.includes('date') && route.date !== today) {
    return 'date';
  }
  if (keys.includes('period') && route.period !== '7') {
    return 'period';
  }
  return null;
}
