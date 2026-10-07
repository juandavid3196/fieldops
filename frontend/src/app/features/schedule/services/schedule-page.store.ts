import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, map, of, switchMap, tap } from 'rxjs';

import { ApiError, isApiError } from '../../../core/models/api-error.model';
import {
  CalendarResponse,
  DispatchOptions,
  NamedOption,
  ScheduleBranch,
  ScheduleView,
  UnscheduledFilter,
  UnscheduledItem,
  VisitStatusFilter,
} from '../models/schedule.model';
import { ScheduleService } from './schedule.service';

export type LoadStatus = 'loading' | 'ready' | 'error';
export type AccessFailure = 'forbidden' | 'unauthorized' | null;

export interface RouteState {
  readonly branchId: string;
  readonly view: ScheduleView;
  readonly date: string;
}

export interface ProposedBlock {
  readonly date: string | null;
  readonly start: string | null;
  readonly end: string | null;
  readonly technicianIds: readonly string[];
}

const SEARCH_MAX_LENGTH = 100;

type CalendarResult =
  | { readonly ok: true; readonly response: CalendarResponse }
  | { readonly ok: false; readonly error: ApiError | null };

type PanelResult =
  | {
      readonly ok: true;
      readonly items: readonly UnscheduledItem[];
      readonly total: number;
      readonly append: boolean;
    }
  | { readonly ok: false; readonly error: ApiError | null };

interface PanelRequest {
  readonly branchId: string;
  readonly filter: UnscheduledFilter;
  readonly search: string;
  readonly skillId: string | null;
  readonly page: number;
}

/**
 * Page-scoped state of the dispatch calendar: options, filters, calendar, unscheduled panel,
 * selected visit and the drawer's proposed block. The URL query (`branch`, `view`, `date`) is
 * the single source of the route state and is written by the page, never here.
 */
@Injectable()
export class SchedulePageStore {
  private readonly api = inject(ScheduleService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly calendarRequests = new Subject<RouteState>();
  private readonly panelRequests = new Subject<PanelRequest>();

  // Options
  readonly options = signal<DispatchOptions | null>(null);
  readonly optionsStatus = signal<LoadStatus>('loading');
  readonly access = signal<AccessFailure>(null);

  // Route state (mirrors the URL query)
  readonly route = signal<RouteState | null>(null);
  readonly branch = computed<ScheduleBranch | null>(() => {
    const id = this.route()?.branchId;
    return this.options()?.branches.find((branch) => branch.id === id) ?? null;
  });
  readonly timezone = computed(() => this.branch()?.timezone ?? 'UTC');

  // Filters
  readonly teamIds = signal<readonly string[]>([]);
  readonly skillId = signal<string | null>(null);
  readonly statusFilter = signal<VisitStatusFilter>('all');
  readonly teamOptions = signal<readonly NamedOption[]>([]);

  // Calendar
  readonly calendar = signal<CalendarResponse | null>(null);
  readonly calendarStatus = signal<LoadStatus>('loading');
  readonly calendarError = signal<ApiError | null>(null);

  // Unscheduled panel
  readonly panelFilter = signal<UnscheduledFilter>('all');
  readonly searchText = signal('');
  private readonly searchTerm = signal('');
  readonly items = signal<readonly UnscheduledItem[]>([]);
  readonly total = signal(0);
  readonly panelStatus = signal<LoadStatus>('loading');
  readonly panelError = signal<ApiError | null>(null);
  readonly loadingMore = signal(false);
  private readonly page = signal(1);
  readonly hasMore = computed(() => this.items().length < this.total());

  // Selection and drawer
  readonly selectedVisitId = signal<string | null>(null);
  readonly proposed = signal<ProposedBlock | null>(null);
  readonly drawerDirty = signal(false);

  constructor() {
    this.calendarRequests
      .pipe(
        tap(() => {
          this.calendarStatus.set('loading');
          this.calendarError.set(null);
        }),
        switchMap((route) =>
          this.api
            .calendar({
              branchId: route.branchId,
              view: route.view,
              date: route.date,
              technicianIds: this.teamIds(),
              skillId: this.skillId(),
              status: this.statusFilter(),
            })
            .pipe(
              map((response): CalendarResult => ({ ok: true, response })),
              catchError((error: unknown) =>
                of<CalendarResult>({ ok: false, error: isApiError(error) ? error : null }),
              ),
            ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleCalendar(result));

    this.panelRequests
      .pipe(
        tap((request) => {
          if (request.page === 1) {
            this.panelStatus.set('loading');
            this.panelError.set(null);
          } else {
            this.loadingMore.set(true);
          }
        }),
        switchMap((request) =>
          this.api.unscheduled(request).pipe(
            map((response): PanelResult => ({
              ok: true,
              items: response.items,
              total: response.total,
              append: request.page > 1,
            })),
            catchError((error: unknown) =>
              of<PanelResult>({ ok: false, error: isApiError(error) ? error : null }),
            ),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handlePanel(result));
  }

  // Options

  loadOptions(): void {
    this.optionsStatus.set('loading');
    this.api.options().subscribe({
      next: (options) => {
        this.options.set(options);
        this.optionsStatus.set('ready');
      },
      error: (error: unknown) => {
        if (!this.handleAccess(error)) {
          this.optionsStatus.set('error');
        }
      },
    });
  }

  /** BR-03: the URL branch when in scope, else the default, else the first by name. */
  resolveBranchId(requested: string | null): string | null {
    const branches = this.options()?.branches ?? [];
    if (requested !== null && branches.some((branch) => branch.id === requested)) {
      return requested;
    }
    return this.defaultBranchId();
  }

  defaultBranchId(): string | null {
    const options = this.options();
    if (options === null || options.branches.length === 0) {
      return null;
    }
    const preferred = options.defaultBranchId;
    if (preferred !== null && options.branches.some((branch) => branch.id === preferred)) {
      return preferred;
    }
    return (
      options.branches.find((branch) => branch.isMain)?.id ??
      [...options.branches].sort((a, b) => a.name.localeCompare(b.name))[0].id
    );
  }

  // Route state

  /** Applies a new URL state: a branch change resets filters and reloads both regions. */
  applyRoute(next: RouteState): void {
    const previous = this.route();
    this.route.set(next);
    if (previous === null || previous.branchId !== next.branchId) {
      this.teamIds.set([]);
      this.skillId.set(null);
      this.statusFilter.set('all');
      this.teamOptions.set([]);
      this.panelFilter.set('all');
      this.searchText.set('');
      this.searchTerm.set('');
      this.loadCalendar();
      this.loadPanel();
    } else if (previous.view !== next.view || previous.date !== next.date) {
      this.loadCalendar();
    }
  }

  // Filters

  setTeam(ids: readonly string[]): void {
    this.teamIds.set(ids);
    this.loadCalendar();
  }

  setSkill(id: string | null): void {
    this.skillId.set(id);
    this.loadCalendar();
    this.loadPanel();
  }

  setStatus(status: VisitStatusFilter): void {
    this.statusFilter.set(status);
    this.loadCalendar();
  }

  setPanelFilter(filter: UnscheduledFilter): void {
    this.panelFilter.set(filter);
    this.loadPanel();
  }

  /** Debounced by the caller; trimmed and capped at 100 characters (BR-07). */
  setSearch(value: string): void {
    const term = value.trim().slice(0, SEARCH_MAX_LENGTH);
    this.searchText.set(value);
    if (term !== this.searchTerm()) {
      this.searchTerm.set(term);
      this.loadPanel();
    }
  }

  // Loading

  loadCalendar(): void {
    const route = this.route();
    if (route !== null) {
      this.calendarRequests.next(route);
    }
  }

  loadPanel(): void {
    this.page.set(1);
    this.requestPanel(1);
  }

  loadMore(): void {
    const next = this.page() + 1;
    this.page.set(next);
    this.requestPanel(next);
  }

  /** Calendar and panel refresh after a successful dispatch. */
  refresh(): void {
    this.loadCalendar();
    this.loadPanel();
  }

  private requestPanel(page: number): void {
    const route = this.route();
    if (route === null) {
      return;
    }
    this.panelRequests.next({
      branchId: route.branchId,
      filter: this.panelFilter(),
      search: this.searchTerm(),
      skillId: this.skillId(),
      page,
    });
  }

  private handleCalendar(result: CalendarResult): void {
    if (result.ok) {
      this.calendar.set(result.response);
      this.calendarStatus.set('ready');
      this.mergeTeamOptions(result.response);
      return;
    }
    if (this.handleAccess(result.error)) {
      return;
    }
    this.calendarStatus.set('error');
    this.calendarError.set(result.error);
  }

  private handlePanel(result: PanelResult): void {
    this.loadingMore.set(false);
    if (result.ok) {
      this.items.set(result.append ? [...this.items(), ...result.items] : result.items);
      this.total.set(result.total);
      this.panelStatus.set('ready');
      return;
    }
    if (this.handleAccess(result.error)) {
      return;
    }
    if (this.page() > 1) {
      this.page.set(this.page() - 1);
    }
    this.panelStatus.set('error');
    this.panelError.set(result.error);
  }

  /** Active technicians of the branch seen so far; the calendar only returns the filtered lanes. */
  private mergeTeamOptions(response: CalendarResponse): void {
    const known = new Map(this.teamOptions().map((option) => [option.id, option]));
    for (const technician of response.technicians) {
      if (technician.tag === null) {
        known.set(technician.id, { id: technician.id, name: technician.name });
      }
    }
    this.teamOptions.set([...known.values()].sort((a, b) => a.name.localeCompare(b.name)));
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

  retryAll(): void {
    if (this.options() === null) {
      this.loadOptions();
      return;
    }
    this.refresh();
  }
}
