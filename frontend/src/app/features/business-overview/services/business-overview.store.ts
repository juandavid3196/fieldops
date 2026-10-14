import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';

import { ApiError, isApiError } from '../../../core/models/api-error.model';
import { DownloadedCsv } from '../../../shared/utils/csv-file';
import {
  BusinessOptions,
  BusinessOverview,
  BusinessQuery,
  BusinessRoute,
} from '../models/business-overview.model';
import { BusinessOverviewService } from './business-overview.service';

export type LoadStatus = 'loading' | 'ready' | 'error';
export type AccessFailure = 'forbidden' | 'unauthorized' | null;

interface Request {
  readonly silent: boolean;
  readonly query: BusinessQuery;
}

type Result =
  | { readonly ok: true; readonly response: BusinessOverview }
  | { readonly ok: false; readonly error: ApiError | null };

/**
 * Page-scoped state of the Business view. The URL is the single source of the route and is written
 * by the page. A range or branch change cancels the in-flight request (switchMap) and a view change
 * destroys the store; the comparison flag never reaches the request (BR-04, BR-21).
 */
@Injectable()
export class BusinessOverviewStore {
  private readonly api = inject(BusinessOverviewService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly requests = new Subject<Request>();

  readonly options = signal<BusinessOptions | null>(null);
  readonly optionsStatus = signal<LoadStatus>('loading');
  readonly access = signal<AccessFailure>(null);
  readonly route = signal<BusinessRoute | null>(null);

  readonly data = signal<BusinessOverview | null>(null);
  readonly status = signal<LoadStatus>('loading');
  readonly refreshing = signal(false);
  readonly refreshFailed = signal(false);
  readonly lastUpdatedAt = signal<number | null>(null);
  /** The API rejected the selection; the page resets it to the default (replaceUrl). */
  readonly rejected = signal<'branch' | 'range' | null>(null);

  readonly loading = computed(
    () => this.status() === 'loading' || this.optionsStatus() === 'loading',
  );

  constructor() {
    this.requests
      .pipe(
        tap((request) => this.begin(request.silent)),
        switchMap((request) =>
          this.run(this.api.overview(request.query)).pipe(map((result) => ({ result, request }))),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ result, request }) => this.handle(result, request.silent));
  }

  private run(source: Observable<BusinessOverview>): Observable<Result> {
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

  /** Applies the URL state; a different period or branch reloads, a comparison change never does. */
  applyRoute(next: BusinessRoute): void {
    const previous = this.route();
    this.route.set(next);
    if (
      previous === null ||
      previous.from !== next.from ||
      previous.to !== next.to ||
      previous.branchId !== next.branchId
    ) {
      this.load(false);
    }
  }

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

  /** BR-20: the CSV of the current selection; `null` before the route is known. */
  export(): Observable<DownloadedCsv> | null {
    const query = this.query();
    const route = this.route();
    return query === null || route === null ? null : this.api.export(query, route.compare);
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

  private query(): BusinessQuery | null {
    const route = this.route();
    return route === null ? null : { from: route.from, to: route.to, branchId: route.branchId };
  }

  private load(silent: boolean): void {
    const query = this.query();
    if (query !== null) {
      this.requests.next({ silent, query });
    }
  }

  private handle(result: Result, silent: boolean): void {
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
    if (rejection !== null) {
      this.status.set('error');
      this.rejected.set(rejection);
      return;
    }
    if (silent && this.data() !== null) {
      this.refreshFailed.set(true);
    } else {
      this.status.set('error');
    }
  }

  /** BR-03/BR-04: a `404` branch or a `400` range goes back to its default, when not already there. */
  private rejection(error: ApiError | null): 'branch' | 'range' | null {
    const route = this.route();
    if (error === null || route === null) {
      return null;
    }
    if (error.status === 404 && route.branchId !== null) {
      return 'branch';
    }
    const keys = Object.keys(error.fieldErrors).map((key) => key.toLowerCase());
    if (
      error.status === 400 &&
      route.range !== 'this_month' &&
      (keys.includes('from') || keys.includes('to'))
    ) {
      return 'range';
    }
    return null;
  }
}
