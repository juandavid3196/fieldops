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
import { ActivatedRoute, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { watchMedia } from '../../../../core/config/breakpoints';
import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import { saveCsv } from '../../../../shared/utils/csv-file';
import { RANGE_CHOICES, presetPeriod } from '../../../business-overview/utils/business-params';
import { agoLabel } from '../../../dashboard/utils/dashboard-refresh';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { todayInZone } from '../../../schedule/utils/schedule-range';
import { ExportHistoryDrawer } from '../../components/export-history-drawer/export-history-drawer';
import { ReportCatalog } from '../../components/report-catalog/report-catalog';
import { ReportChart } from '../../components/report-chart/report-chart';
import { ReportDefinitions } from '../../components/report-definitions/report-definitions';
import { FilterField, ReportFilters } from '../../components/report-filters/report-filters';
import { ReportHeader } from '../../components/report-header/report-header';
import { ReportSummary } from '../../components/report-summary/report-summary';
import { ReportTable } from '../../components/report-table/report-table';
import {
  FilterKey,
  RangePreset,
  REPORTS_CSV_TOO_LARGE_MESSAGE,
  REPORTS_EXPORT_FAILED_MESSAGE,
  REPORTS_FORBIDDEN_MESSAGE,
  REPORTS_LOAD_ERROR_MESSAGE,
  REPORTS_NO_BRANCHES_MESSAGE,
  REPORTS_PDF_TOO_LARGE_MESSAGE,
  REPORTS_REFRESH_ERROR_MESSAGE,
  ReportExportItem,
  ReportFilterValues,
  ReportFormat,
  ReportKey,
  ReportsRoute,
} from '../../models/reports.model';
import { ReportPreferences } from '../../services/report-preferences';
import { ReportsStore } from '../../services/reports.store';
import { reportMeta, visibleKeysForRole } from '../../utils/report-catalog';
import {
  REPORT_SCOPED_PARAMS,
  previousPeriod,
  resolveReportsRoute,
  routeFromExport,
  routeParams,
} from '../../utils/report-params';
import { formatDate } from '../../utils/report-view';

/** Period, branch and report filters held in the form until Apply filters (BR-19). */
interface Draft {
  readonly range: RangePreset;
  readonly from: string;
  readonly to: string;
  readonly branch: string | null;
  readonly filters: ReportFilterValues;
}

const ALL = 'all';
const TICK_MS = 15_000;
/** Catalog panel beside the report from 1280 px (BR-21). */
const WIDE_QUERY = '(min-width: 80rem)';
export const AGING_NOTE = 'Aging is calculated as of today.';

function draftOf(route: ReportsRoute | null): Draft {
  return {
    range: route?.range ?? 'this_month',
    from: route?.from ?? '',
    to: route?.to ?? '',
    branch: route?.branchId ?? null,
    filters: route?.filters ?? {},
  };
}

/**
 * Reports workspace (`/reports`, Design 25): a URL-driven report, period, branch, comparison and
 * report filters; the server computes, sorts, pages and exports. Backend authorization is
 * authoritative; the role only drives what the page offers.
 */
@Component({
  selector: 'app-reports',
  imports: [
    ButtonDirective,
    DrawerShell,
    ExportHistoryDrawer,
    FormsModule,
    Message,
    ReportCatalog,
    ReportChart,
    ReportDefinitions,
    ReportFilters,
    ReportHeader,
    ReportSummary,
    ReportTable,
    Select,
    Skeleton,
    Toast,
    ToggleSwitch,
  ],
  providers: [ReportsStore, ReportPreferences, MessageService],
  templateUrl: './reports.html',
  styleUrl: './reports.scss',
})
export class Reports {
  protected readonly store = inject(ReportsStore);
  protected readonly prefs = inject(ReportPreferences);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly activated = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  readonly forbiddenMessage = REPORTS_FORBIDDEN_MESSAGE;
  readonly noBranchesMessage = REPORTS_NO_BRANCHES_MESSAGE;
  readonly loadErrorMessage = REPORTS_LOAD_ERROR_MESSAGE;
  readonly refreshErrorMessage = REPORTS_REFRESH_ERROR_MESSAGE;
  readonly agingNote = AGING_NOTE;
  readonly rangeChoices = [...RANGE_CHOICES];
  readonly summarySkeletons = [0, 1, 2, 3, 4];

  private readonly sessionExpired = signal(false);
  private readonly queryParams = toSignal(this.activated.queryParamMap, {
    initialValue: this.activated.snapshot.queryParamMap,
  });
  private readonly now = signal(Date.now());

  readonly wide = signal(true);
  readonly catalogOpen = signal(false);
  readonly historyOpen = signal(false);
  readonly exporting = signal<ReportFormat | null>(null);

  readonly forbidden = computed(
    () =>
      visibleKeysForRole(this.sessionService.session()?.role.code).length === 0 ||
      this.store.access() === 'forbidden',
  );
  readonly ready = computed(() => this.store.optionsStatus() === 'ready');
  readonly options = this.store.options;
  readonly route = this.store.route;
  readonly noBranches = computed(
    () => this.ready() && (this.options()?.branches.length ?? 0) === 0,
  );
  readonly controlsDisabled = computed(() => !this.ready() || this.noBranches());
  readonly meta = computed(() => {
    const route = this.route();
    return route === null ? null : reportMeta(route.report);
  });
  readonly asOfToday = computed(() => this.meta()?.asOfToday === true);
  readonly compare = computed(() => (this.route()?.compare ?? true) && !this.asOfToday());
  readonly today = computed(() => {
    const options = this.options();
    return options === null ? '' : todayInZone(options.timezone, new Date(this.now()));
  });
  readonly agoLabel = computed(() => agoLabel(this.store.lastUpdatedAt(), this.now()));
  readonly reports = computed(() => this.options()?.reports ?? []);
  readonly favorite = computed(() => {
    const route = this.route();
    return route !== null && this.prefs.isFavorite(route.report);
  });
  readonly announcement = computed(() => {
    const data = this.store.data();
    const meta = this.meta();
    return data === null || meta === null ? '' : `${data.total} ${meta.noun}`;
  });

  /** The draft restarts whenever the applied state changes, but not on the comparison toggle. */
  private readonly appliedKey = computed(() => {
    const route = this.route();
    return route === null ? '' : JSON.stringify({ ...routeParams(route), compare: null });
  });
  readonly draft = linkedSignal<string, Draft>({
    source: this.appliedKey,
    computation: () => draftOf(untracked(this.route)),
  });

  readonly branchChoices = computed(() => {
    const branches = this.options()?.branches ?? [];
    const all = branches.length > 1 ? [{ id: ALL, name: 'All branches' }] : [];
    return [
      ...all,
      ...branches.map((branch) => ({
        id: branch.id,
        name: branch.isActive ? branch.name : `${branch.name} (Inactive)`,
      })),
    ];
  });
  readonly branchValue = computed(() => this.draft().branch ?? this.defaultBranch());
  private readonly defaultBranch = computed(() => {
    const branches = this.options()?.branches ?? [];
    return branches.length === 1 ? branches[0].id : ALL;
  });
  readonly previousLabel = computed(() => {
    const route = this.route();
    if (route === null || this.asOfToday()) {
      return '';
    }
    const previous = previousPeriod(route.from, route.to);
    return `${formatDate(previous.from)} – ${formatDate(previous.to)}`;
  });
  readonly filterFields = computed<FilterField[]>(() => {
    const meta = this.meta();
    const options = this.options();
    if (meta === null || options === null) {
      return [];
    }
    return meta.filters.map((spec) => {
      const choices =
        spec.source === 'categories'
          ? options.categories.map((item) => ({ code: item.id, label: choiceName(item) }))
          : spec.source === 'technicians'
            ? options.technicians.map((item) => ({ code: item.id, label: choiceName(item) }))
            : (spec.choices ?? []);
      return {
        key: spec.key,
        label: spec.label,
        options: [{ code: ALL, label: spec.allLabel }, ...choices],
      };
    });
  });

  constructor() {
    if (!this.forbidden()) {
      this.store.loadOptions();
    }

    const media = watchMedia(WIDE_QUERY, (matches) => this.wide.set(matches));
    this.wide.set(media.matches);
    const timer = setInterval(() => this.now.set(Date.now()), TICK_MS);
    this.destroyRef.onDestroy(() => {
      media.stop();
      clearInterval(timer);
    });

    // BR-18: stored preferences are read only once the session and the visible reports are known.
    effect(() => {
      const session = this.sessionService.session();
      const options = this.options();
      if (session !== null && options !== null) {
        this.prefs.load(
          session.organization.id,
          session.user.id,
          options.reports.map((report) => report.key),
        );
      }
    });

    // BR-05: the URL is the single source; invalid values are replaced before any request.
    effect(() => {
      const params = this.queryParams();
      const options = this.options();
      if (
        this.forbidden() ||
        options === null ||
        options.branches.length === 0 ||
        options.reports.length === 0
      ) {
        return;
      }
      untracked(() => {
        const resolved = resolveReportsRoute(params, options);
        if (resolved.normalized) {
          this.go(resolved.query, true);
        } else {
          this.store.applyRoute(resolved.route);
        }
      });
    });

    // A branch, technician, report or value the API rejected goes back to its default.
    effect(() => {
      const rejected = this.store.rejected();
      if (rejected === null) {
        return;
      }
      untracked(() => {
        this.store.rejected.set(null);
        const params = this.queryParams();
        const present = rejected.filter((name) => params.get(name) !== null);
        if (present.length > 0) {
          this.go(Object.fromEntries(present.map((name) => [name, null])), true);
          if (rejected.includes('branch') || rejected.includes('technician')) {
            this.store.loadOptions();
          }
        }
      });
    });

    // BR-07: a page past the last moves to the last page.
    effect(() => {
      const data = this.store.data();
      const route = untracked(this.route);
      if (data === null || route === null) {
        return;
      }
      const last = Math.ceil(data.total / route.pageSize);
      if (data.items.length === 0 && data.total > 0 && route.page > last) {
        untracked(() => this.go({ page: last > 1 ? String(last) : null }, true));
      }
    });

    // BR-18: Recently viewed records only reports that loaded.
    effect(() => {
      const data = this.store.data();
      if (data !== null) {
        untracked(() => this.prefs.markViewed(data.reportKey));
      }
    });

    effect(() => {
      if (this.store.access() === 'unauthorized') {
        untracked(() => this.onSessionExpired());
      }
    });
  }

  onSessionExpired(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  private go(params: Record<string, string | null>, replaceUrl: boolean): void {
    void this.router.navigate([], {
      relativeTo: this.activated,
      queryParams: params,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  private navigateRoute(route: ReportsRoute): void {
    this.go(routeParams(route), false);
  }

  // Catalog and favorites

  selectReport(key: ReportKey): void {
    this.catalogOpen.set(false);
    if (key !== this.route()?.report) {
      this.go({ report: key, ...REPORT_SCOPED_PARAMS }, false);
    }
  }

  toggleFavorite(): void {
    const route = this.route();
    if (route !== null) {
      this.prefs.toggleFavorite(route.report);
    }
  }

  // Global bar and filters (BR-19): edits stay in the draft until Apply.

  setRange(range: RangePreset): void {
    this.draft.update((draft) =>
      range === 'custom'
        ? { ...draft, range }
        : { ...draft, range, ...presetPeriod(range, this.today()) },
    );
  }

  setDate(key: 'from' | 'to', value: string): void {
    if (value !== '') {
      this.draft.update((draft) => ({ ...draft, range: 'custom', [key]: value }));
    }
  }

  setBranch(code: string): void {
    this.draft.update((draft) => ({ ...draft, branch: code === ALL ? null : code }));
  }

  setFilter(change: { readonly key: FilterKey; readonly code: string }): void {
    this.draft.update((draft) => {
      const kept = Object.entries(draft.filters).filter(([key]) => key !== change.key);
      const filters = change.code === ALL ? kept : [...kept, [change.key, change.code]];
      return { ...draft, filters: Object.fromEntries(filters) };
    });
  }

  /** Apply filters: the draft becomes the URL, the page resets to 1 and the report reloads once. */
  apply(): void {
    const route = this.route();
    if (route === null || this.controlsDisabled()) {
      return;
    }
    const draft = this.draft();
    const period =
      draft.range === 'custom'
        ? { from: draft.from, to: draft.to }
        : presetPeriod(draft.range, this.today());
    const next: ReportsRoute = {
      ...route,
      range: draft.range,
      ...period,
      branchId: draft.branch,
      filters: draft.filters,
      page: 1,
    };
    if (JSON.stringify(routeParams(next)) === JSON.stringify(routeParams(route))) {
      this.store.refresh();
      return;
    }
    this.navigateRoute(next);
  }

  /** Reset: period, branch, filters, sort and page back to their defaults; compare is kept. */
  reset(): void {
    const route = this.route();
    if (route === null) {
      return;
    }
    const meta = reportMeta(route.report);
    this.navigateRoute({
      ...route,
      range: 'this_month',
      ...presetPeriod('this_month', this.today()),
      branchId: null,
      filters: {},
      sort: meta.defaultSort,
      direction: meta.defaultDirection,
      page: 1,
    });
  }

  onCompare(checked: boolean): void {
    this.go({ compare: checked ? null : 'false' }, false);
  }

  // Sorting and paging reload immediately.

  sortBy(sort: string): void {
    const route = this.route();
    const column = this.meta()?.columns.find((item) => item.key === sort);
    if (route === null || column === undefined) {
      return;
    }
    const firstDirection = column.type === 'text' || column.type === 'label' ? 'asc' : 'desc';
    const direction =
      route.sort === sort ? (route.direction === 'asc' ? 'desc' : 'asc') : firstDirection;
    this.navigateRoute({ ...route, sort, direction, page: 1 });
  }

  changePage(page: number): void {
    const route = this.route();
    if (route !== null) {
      this.navigateRoute({ ...route, page });
    }
  }

  changePageSize(pageSize: number): void {
    const route = this.route();
    if (route !== null) {
      this.navigateRoute({ ...route, pageSize, page: 1 });
    }
  }

  // Export history (BR-17)

  openHistoryReport(item: ReportExportItem): void {
    this.historyOpen.set(false);
    this.go(routeFromExport(item.reportKey, item.filters), false);
  }

  // Export (BR-22)

  exportReport(format: ReportFormat): void {
    if (this.exporting() !== null || this.controlsDisabled()) {
      return;
    }
    const request = this.store.exportReport(format);
    if (request === null) {
      return;
    }
    this.exporting.set(format);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (file) => {
        this.exporting.set(null);
        saveCsv(file);
      },
      error: (error: unknown) => {
        this.exporting.set(null);
        const kind = isApiError(error) ? error.kind : null;
        if (kind === 'unauthorized') {
          this.onSessionExpired();
          return;
        }
        // The only `409` of the export is `export_too_large` (the error body is a blob).
        this.messages.add({
          severity: 'error',
          summary:
            kind === 'conflict'
              ? format === 'csv'
                ? REPORTS_CSV_TOO_LARGE_MESSAGE
                : REPORTS_PDF_TOO_LARGE_MESSAGE
              : REPORTS_EXPORT_FAILED_MESSAGE,
        });
      },
    });
  }
}

function choiceName(item: { readonly name: string; readonly isActive: boolean }): string {
  return item.isActive ? item.name : `${item.name} (Inactive)`;
}
