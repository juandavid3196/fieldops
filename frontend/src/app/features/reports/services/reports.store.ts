import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';

import { ApiError, isApiError } from '../../../core/models/api-error.model';
import {
  ReportData,
  ReportFile,
  ReportFormat,
  ReportOptions,
  ReportsRoute,
} from '../models/reports.model';
import { reportQueryParams } from '../utils/report-params';
import { ReportsService } from './reports.service';

export type LoadStatus = 'loading' | 'ready' | 'error';
export type AccessFailure = 'forbidden' | 'unauthorized' | null;

interface Request {
  readonly silent: boolean;
  readonly route: ReportsRoute;
}

type Result =
  | { readonly ok: true; readonly response: ReportData }
  | { readonly ok: false; readonly error: ApiError | null };

/** BR-05: URL parameters a server `400` key resets to their defaults. */
const PARAMS_OF_ERROR: Readonly<Record<string, readonly string[]>> = {
  from: ['range', 'from', 'to'],
  to: ['range', 'from', 'to'],
  sort: ['sort', 'direction'],
  direction: ['sort', 'direction'],
  page: ['page'],
  pagesize: ['pageSize'],
  status: ['status'],
  categoryid: ['category'],
  technicianid: ['technician'],
  bucket: ['bucket'],
  event: ['event'],
  ontime: ['onTime'],
};

/**
 * Page-scoped state of the Reports workspace. The URL is the single source of the route and is
 * written by the page. A report, period, branch, filter, sort or page change cancels the in-flight
 * request (switchMap); the comparison flag never reaches the request (BR-06, BR-19).
 */
@Injectable()
export class ReportsStore {
  private readonly api = inject(ReportsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<Request>();

  readonly options = signal<ReportOptions | null>(null);
  readonly optionsStatus = signal<LoadStatus>('loading');
  readonly access = signal<AccessFailure>(null);
  readonly route = signal<ReportsRoute | null>(null);

  readonly data = signal<ReportData | null>(null);
  readonly status = signal<LoadStatus>('loading');
  readonly refreshing = signal(false);
  readonly refreshFailed = signal(false);
  readonly lastUpdatedAt = signal<number | null>(null);
  /** URL parameters the API rejected (`404` branch/technician/report, `400`); the page resets them. */
  readonly rejected = signal<readonly string[] | null>(null);

  readonly loading = computed(
    () => this.status() === 'loading' || this.optionsStatus() === 'loading',
  );

  constructor() {
    this.requests
      .pipe(
        tap((request) => this.begin(request.silent)),
        switchMap((request) =>
          this.run(this.api.report(request.route)).pipe(map((result) => ({ result, request }))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ result, request }) => this.handle(result, request));
  }

  private run(source: Observable<ReportData>): Observable<Result> {
    return source.pipe(
      map((response): Result => ({ ok: true, response })),
      catchError((error: unknown) =>
        of<Result>({ ok: false, error: isApiError(error) ? error : null }),
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

  /** Applies the URL state; only the request-relevant keys reload (compare never does). */
  applyRoute(next: ReportsRoute): void {
    const previous = this.route();
    this.route.set(next);
    if (
      previous === null ||
      previous.report !== next.report ||
      JSON.stringify(reportQueryParams(previous)) !== JSON.stringify(reportQueryParams(next))
    ) {
      this.load(false);
    }
  }

  /** The refresh button and Apply with an unchanged state: keeps the data while reloading. */
  refresh(): void {
    this.load(this.data() !== null);
  }

  /** "Try again" from the load-error alert. */
  retry(): void {
    if (this.options() === null) {
      this.loadOptions();
      return;
    }
    this.load(false);
  }

  /** BR-14/BR-15: the file of the applied state; `null` before the route is known. */
  exportReport(format: ReportFormat): Observable<ReportFile> | null {
    const route = this.route();
    const options = this.options();
    return route === null || options === null
      ? null
      : this.api.export(route, format, options.today);
  }

  /** 401 leaves to Sign In and 403 shows the forbidden state; returns true when handled. */
  handleAccess(error: unknown): boolean {
    const kind = isApiError(error) ? error.kind : null;
    if (kind === 'unauthorized' || kind === 'forbidden') {
      this.access.set(kind);
      return true;
    }
    return false;
  }

  private load(silent: boolean): void {
    const route = this.route();
    if (route !== null) {
      this.requests.next({ silent, route });
    }
  }

  private handle(result: Result, request: Request): void {
    this.refreshing.set(false);
    if (result.ok) {
      this.data.set(result.response);
      this.status.set('ready');
      this.lastUpdatedAt.set(Date.now());
      this.refreshFailed.set(false);
      return;
    }
    if (this.handleAccess(result.error)) {
      return;
    }
    const rejection = this.rejection(result.error);
    if (rejection.length > 0) {
      this.status.set('error');
      this.rejected.set(rejection);
      return;
    }
    if (request.silent && this.data() !== null) {
      this.refreshFailed.set(true);
    } else {
      this.status.set('error');
    }
  }

  /** BR-05: URL parameters to reset after a `404` (branch, technician, report) or a `400`. */
  private rejection(error: ApiError | null): readonly string[] {
    if (error === null) {
      return [];
    }
    if (error.status === 404) {
      if (error.code === 'branch_not_found') {
        return ['branch'];
      }
      return error.code === 'technician_not_found' ? ['technician'] : ['report'];
    }
    if (error.status !== 400) {
      return [];
    }
    return Object.keys(error.fieldErrors).flatMap(
      (key) => PARAMS_OF_ERROR[key.replace(/^errors\./i, '').toLowerCase()] ?? [],
    );
  }
}
