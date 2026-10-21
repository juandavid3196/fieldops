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
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { EMPTY, Observable, Subject, catchError, switchMap, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { saveCsv } from '../../../../shared/utils/csv-file';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { AuditEventDrawer } from '../../components/audit-event-drawer/audit-event-drawer';
import {
  AUDIT_ALL_BRANCHES,
  AUDIT_EMPTY_MESSAGE,
  AUDIT_EXPORT_FAILED_MESSAGE,
  AUDIT_EXPORT_TOO_LARGE_MESSAGE,
  AUDIT_FORBIDDEN_MESSAGE,
  AUDIT_LOAD_ERROR_MESSAGE,
  AUDIT_PAGE_SIZES,
  AuditDetail,
  AuditFilterOptions,
  AuditImpact,
  AuditItem,
  AuditPage,
  AuditQuery,
  AuditSort,
  AuditSummary,
  AuditView,
} from '../../models/audit-log.model';
import { AuditLogService } from '../../services/audit-log.service';
import {
  DEFAULT_VIEW,
  IMPACT_ICONS,
  IMPACT_LABELS,
  auditViewParams,
  dayIn,
  isCanonicalAuditUrl,
  relatedRange,
  resolveAuditView,
  tableDate,
  tableTime,
} from '../../utils/audit-log-format';

import { localizedList, t, tCode } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
import { CodeLabelPipe } from '../../../../core/i18n/code-label.pipe';
interface Region<T> {
  readonly status: 'idle' | 'loading' | 'ready' | 'error';
  readonly data: T | null;
}

interface Choice {
  readonly code: string;
  readonly label: string;
}

interface RowView {
  readonly item: AuditItem;
  readonly date: string;
  readonly time: string;
  readonly branch: string;
  readonly sourceIp: string;
  readonly impactLabel: string;
  readonly impactIcon: string;
}

/** Filters held in the form until Apply (BR-20). */
interface Draft {
  readonly from: string | null;
  readonly to: string | null;
  readonly q: string;
  readonly actor: string | null;
  readonly action: string | null;
  readonly entity: string | null;
  readonly entityId: string | null;
  readonly branch: string | null;
  readonly impact: AuditImpact | null;
}

const IDLE = { status: 'idle', data: null } as const;
const ALL = 'all';
/** Query parameter a server `400` key resets (BR-20: invalid values fall back to defaults). */
const FIELD_PARAMS: Readonly<Record<string, readonly (keyof AuditView)[]>> = {
  from: ['from', 'to'],
  to: ['from', 'to'],
  q: ['q'],
  actorId: ['actor'],
  action: ['action'],
  entityType: ['entity', 'entityId'],
  entityId: ['entityId'],
  impact: ['impact'],
  sort: ['sort'],
  direction: ['direction'],
  page: ['page'],
  pageSize: ['pageSize'],
};

function draftOf(view: AuditView): Draft {
  return {
    from: view.from,
    to: view.to,
    q: view.q,
    actor: view.actor,
    action: view.action,
    entity: view.entity,
    entityId: view.entityId,
    branch: view.branch,
    impact: view.impact,
  };
}

/**
 * Audit trail (`/audit-log`, Design 29): metrics, filters applied on demand, a server-sorted and
 * paged table, an event drawer and CSV export. The URL holds the applied view (BR-20); invalid
 * values are normalized with `replaceUrl` before any list request. The filter options load first,
 * so a `403` stops the rest.
 */
@Component({
  selector: 'app-audit-log',
  imports: [
    CodeLabelPipe,
    LocalizedRefPipe,
    TranslocoPipe,
    AuditEventDrawer,
    ButtonDirective,
    FormsModule,
    IconField,
    InputIcon,
    InputText,
    Paginator,
    Select,
    Skeleton,
    Toast,
  ],
  providers: [MessageService],
  templateUrl: './audit-log.html',
  styleUrl: './audit-log.scss',
})
export class AuditLog {
  private readonly api = inject(AuditLogService);
  private readonly messages = inject(MessageService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  get forbiddenMessage(): string {
    return AUDIT_FORBIDDEN_MESSAGE();
  }
  get errorMessage(): string {
    return AUDIT_LOAD_ERROR_MESSAGE();
  }
  get emptyMessage(): string {
    return AUDIT_EMPTY_MESSAGE();
  }
  readonly skeletonRows = [1, 2, 3, 4, 5];
  readonly metricSkeletons = [1, 2, 3, 4];
  readonly pageSizeOptions: Choice[] = AUDIT_PAGE_SIZES.map((size) => ({
    code: String(size),
    label: t('auditLog.auditLog.page', { size: size }),
  }));
  readonly impactOptions: Choice[] = localizedList(() => [
    { code: ALL, label: t('auditLog.auditLog.allLevels') },
    { code: 'high', label: IMPACT_LABELS.high },
    { code: 'medium', label: IMPACT_LABELS.medium },
    { code: 'low', label: IMPACT_LABELS.low },
  ]);

  readonly sessionExpired = signal(false);
  readonly forbidden = signal(false);
  readonly exporting = signal(false);
  readonly selectedId = signal<number | null>(null);

  readonly options = signal<Region<AuditFilterOptions>>(IDLE);
  readonly summary = signal<Region<AuditSummary>>(IDLE);
  readonly list = signal<Region<AuditPage>>(IDLE);

  private readonly query = toSignal(this.route.queryParamMap, { requireSync: true });
  readonly view = computed<AuditView>(() =>
    resolveAuditView((key) => this.query().get(key), this.options().data),
  );
  private readonly canonical = computed(() =>
    isCanonicalAuditUrl((key) => this.query().get(key), this.view()),
  );
  /** Organization zone once a response carried it; the browser zone before that (BR-24). */
  readonly timeZone = computed(
    () => this.list().data?.timeZone ?? this.summary().data?.timeZone ?? null,
  );
  readonly today = computed(() => dayIn(new Date(), this.timeZone()));

  readonly draft = linkedSignal<AuditView, Draft>({
    source: this.view,
    computation: (view) => draftOf(view),
  });
  readonly controlsDisabled = computed(() => this.options().status !== 'ready');
  readonly fromValue = computed(() => this.draft().from ?? this.today());
  readonly toValue = computed(() => this.draft().to ?? this.today());

  readonly actorChoices = computed<Choice[]>(() => [
    { code: ALL, label: t('auditLog.auditLog.allUsers') },
    ...(this.options().data?.users ?? []).map((user) => ({ code: user.id, label: user.name })),
  ]);
  readonly actionChoices = computed<Choice[]>(() => [
    { code: ALL, label: t('auditLog.auditLog.allActions') },
    ...(this.options().data?.actions ?? []).map((action) => ({
      code: action.code,
      label: tCode('auditActions', action.code, action.label),
    })),
  ]);
  readonly entityChoices = computed<Choice[]>(() => [
    { code: ALL, label: t('auditLog.auditLog.allEntities') },
    ...(this.options().data?.entityTypes ?? []),
  ]);
  readonly branchChoices = computed<Choice[]>(() => [
    { code: ALL, label: AUDIT_ALL_BRANCHES() },
    ...(this.options().data?.branches ?? []).map((branch) => ({
      code: branch.id,
      label: branch.name,
    })),
  ]);

  readonly cards = computed(() => {
    const data = this.summary().data;
    const show = (value: number | undefined): string => (value === undefined ? '—' : String(value));
    return [
      {
        label: t('auditLog.auditLog.eventsToday'),
        value: show(data?.eventsToday),
        icon: 'pi-file',
        tone: 'info',
      },
      {
        label: t('auditLog.auditLog.usersActive'),
        value: show(data?.activeUsersToday),
        icon: 'pi-user',
        tone: 'success',
      },
      {
        label: t('auditLog.auditLog.highImpactChanges'),
        value: show(data?.highImpactToday),
        icon: 'pi-exclamation-triangle',
        tone: 'warning',
      },
      {
        label: t('auditLog.auditLog.accessChanges'),
        value: show(data?.accessChangesToday),
        icon: 'pi-shield',
        tone: 'danger',
      },
    ];
  });

  readonly total = computed(() => this.list().data?.total ?? 0);
  readonly rows = computed<RowView[]>(() => {
    const data = this.list().data;
    if (data === null) {
      return [];
    }
    return data.items.map((item) => ({
      item,
      date: tableDate(item.occurredAt, data.timeZone),
      time: tableTime(item.occurredAt, data.timeZone),
      branch: item.branch?.name ?? AUDIT_ALL_BRANCHES(),
      sourceIp: item.ipAddress ?? item.source,
      impactLabel: IMPACT_LABELS[item.impact],
      impactIcon: IMPACT_ICONS[item.impact],
    }));
  });
  readonly footer = computed(() => {
    const { page, pageSize } = this.view();
    const total = this.total();
    return t('auditLog.auditLog.showingOfEvents', {
      value: (page - 1) * pageSize + 1,
      value2: Math.min(page * pageSize, total),
      total: total,
    });
  });
  readonly announcement = computed(() =>
    this.list().status === 'ready' ? t('auditLog.auditLog.events', { total: this.total() }) : '',
  );
  readonly pageSizeCode = computed(() => String(this.view().pageSize));
  readonly filteredToRecord = computed(() => this.view().entityId !== null);

  /** Request gate: null until the options are known and the URL is canonical. */
  private readonly listRequest = computed<{
    query: AuditQuery;
    page: number;
    pageSize: number;
  } | null>(
    () => {
      if (this.forbidden() || this.options().status !== 'ready' || !this.canonical()) {
        return null;
      }
      const view = this.view();
      return { query: this.appliedQuery(view), page: view.page, pageSize: view.pageSize };
    },
    { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) },
  );

  private readonly options$ = new Subject<void>();
  private readonly summary$ = new Subject<void>();
  private readonly list$ = new Subject<{ query: AuditQuery; page: number; pageSize: number }>();

  constructor() {
    this.options$
      .pipe(
        switchMap(() => this.fetch(this.options, this.api.filters())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.summary$
      .pipe(
        switchMap(() => this.fetch(this.summary, this.api.summary())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
    this.list$
      .pipe(
        switchMap((request) =>
          this.fetch(this.list, this.api.list(request.query, request.page, request.pageSize)).pipe(
            tap((page) => this.clampPage(page)),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    // BR-20: invalid URL values are replaced by their defaults before any list request.
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
    // The metrics load once, after the options proved access; they ignore the filters.
    effect(() => {
      if (this.options().status === 'ready' && !this.forbidden()) {
        untracked(() => this.summary$.next());
      }
    });
    this.options$.next();
  }

  /** Applied (not draft) filters and sort. */
  private appliedQuery(view: AuditView): AuditQuery {
    return {
      from: view.from,
      to: view.to,
      q: view.q,
      actor: view.actor,
      action: view.action,
      entity: view.entity,
      entityId: view.entityId,
      branch: view.branch,
      impact: view.impact,
      sort: view.sort,
      direction: view.direction,
    };
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
    switch (api?.kind) {
      case 'unauthorized':
        this.onSessionExpired();
        break;
      case 'forbidden':
        this.onForbidden();
        break;
      case 'validation':
      case 'bad-request': {
        // A server `400` resets the offending parameters to their defaults.
        const bad = Object.entries(FIELD_PARAMS).flatMap(([key, params]) =>
          api.fieldErrors[key] !== undefined || api.fieldErrors[`errors.${key}`] !== undefined
            ? params
            : [],
        );
        if (bad.length > 0) {
          const reset = { ...this.view() } as Record<keyof AuditView, unknown>;
          for (const key of bad) {
            reset[key] = DEFAULT_VIEW[key];
          }
          this.navigate(reset as unknown as AuditView, true);
        }
        break;
      }
    }
  }

  onForbidden(): void {
    this.forbidden.set(true);
    this.selectedId.set(null);
  }

  onSessionExpired(): void {
    handleUnauthorized(this.router, this.sessionExpired);
  }

  /** BR-05: a page past the last moves to the last page. */
  private clampPage(page: AuditPage): void {
    const last = Math.ceil(page.total / this.view().pageSize);
    if (page.items.length === 0 && page.total > 0 && this.view().page > last) {
      this.navigate({ ...this.view(), page: last }, true);
    }
  }

  private navigate(view: AuditView, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: auditViewParams(view),
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  // Filters (BR-20)

  patchDraft(patch: Partial<Draft>): void {
    this.draft.update((draft) => ({ ...draft, ...patch }));
  }

  setDraftDay(key: 'from' | 'to', value: string): void {
    this.patchDraft({ [key]: value === '' ? null : value });
  }

  setDraftChoice(key: 'actor' | 'action' | 'branch', code: string): void {
    this.patchDraft({ [key]: code === ALL ? null : code });
  }

  setDraftEntity(code: string): void {
    // A related-activity record id never outlives its entity type.
    this.patchDraft({ entity: code === ALL ? null : code, entityId: null });
  }

  setDraftImpact(code: string): void {
    this.patchDraft({ impact: code === ALL ? null : (code as AuditImpact) });
  }

  /** Apply filters or Enter in search: the draft becomes the view and the list resets to page 1. */
  apply(): void {
    if (this.controlsDisabled()) {
      return;
    }
    const draft = this.draft();
    const view = { ...this.view(), ...draft, q: draft.q.trim(), page: 1 };
    if (JSON.stringify(auditViewParams(view)) === JSON.stringify(auditViewParams(this.view()))) {
      this.retryList();
      return;
    }
    this.navigate(view);
  }

  /** Restores the BR-03 defaults, the default sort and page 1, and reloads. */
  reset(): void {
    this.navigate({ ...DEFAULT_VIEW, pageSize: this.view().pageSize });
  }

  // Sorting and paging

  sortBy(sort: AuditSort): void {
    const view = this.view();
    const direction = view.sort === sort ? (view.direction === 'desc' ? 'asc' : 'desc') : 'desc';
    this.navigate({ ...view, sort, direction, page: 1 });
  }

  ariaSort(sort: AuditSort): 'ascending' | 'descending' | 'none' {
    const view = this.view();
    return view.sort !== sort ? 'none' : view.direction === 'asc' ? 'ascending' : 'descending';
  }

  changePage(event: PaginatorState): void {
    const page = (event.page ?? 0) + 1;
    if (page !== this.view().page) {
      this.navigate({ ...this.view(), page });
    }
  }

  changePageSize(code: string): void {
    this.navigate({ ...this.view(), pageSize: Number(code), page: 1 });
  }

  // Retry (only the failed part)

  retryOptions(): void {
    this.options$.next();
  }

  retrySummary(): void {
    this.summary$.next();
  }

  retryList(): void {
    const request = this.listRequest();
    if (request !== null) {
      this.list$.next(request);
    }
  }

  // Drawer (BR-22)

  openEvent(item: AuditItem): void {
    this.selectedId.set(item.id);
  }

  closeEvent(): void {
    this.selectedId.set(null);
  }

  /** Closes the drawer and filters to the entity over the last 366 days. */
  showRelated(detail: AuditDetail): void {
    this.selectedId.set(null);
    if (detail.entity.id === null) {
      return;
    }
    const view = this.view();
    this.navigate({
      ...DEFAULT_VIEW,
      ...relatedRange(dayIn(new Date(), detail.timeZone)),
      entity: detail.entity.type,
      entityId: detail.entity.id,
      sort: view.sort,
      direction: view.direction,
      pageSize: view.pageSize,
    });
  }

  // Export (BR-21)

  exportCsv(): void {
    if (this.exporting() || this.controlsDisabled()) {
      return;
    }
    this.exporting.set(true);
    this.api
      .export(this.appliedQuery(this.view()), this.today())
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
            this.onSessionExpired();
          } else if (kind === 'forbidden') {
            this.onForbidden();
          } else {
            // The only `409` of the export is `export_too_large` (the error body is a blob).
            this.messages.add({
              severity: 'error',
              summary:
                kind === 'conflict'
                  ? AUDIT_EXPORT_TOO_LARGE_MESSAGE()
                  : AUDIT_EXPORT_FAILED_MESSAGE(),
            });
          }
        },
      });
  }
}
