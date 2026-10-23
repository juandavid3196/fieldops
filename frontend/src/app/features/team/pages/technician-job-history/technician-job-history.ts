import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { IconField } from 'primeng/iconfield';
import { InputIcon } from 'primeng/inputicon';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Subject, catchError, debounceTime, map, of, switchMap, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
import { localizedList, t } from '../../../../core/i18n/translate';
import { TechnicianProfileHeader } from '../../components/technician-profile-header/technician-profile-header';
import {
  JOB_HISTORY_PAGE_SIZES,
  JobHistoryPeriod,
  JobHistoryQuery,
  JobHistoryResponse,
  JobHistorySort,
  JobHistoryStatus,
} from '../../models/team-job-history.model';
import { TeamService } from '../../services/team.service';
import {
  jobHistoryUrlParams,
  needsCanonicalUrl,
  parseJobHistoryUrl,
} from '../../utils/team-url-state';
import {
  OUTCOME_LABELS,
  formatLocalDate,
  formatMinutes,
  optionalMinutes,
  outcomeSeverity,
} from '../../utils/technician-profile-format';
import { injectProfileLoader } from '../../utils/technician-profile-loader';

const SEARCH_DEBOUNCE_MS = 300;

interface Choice<T> {
  readonly code: T;
  readonly label: string;
}

const STATUS_CHOICES: Choice<JobHistoryStatus>[] = localizedList(() => [
  { code: 'all', label: t('team.technicianJobHistory.allStatuses') },
  { code: 'completed', label: t('team.technicianJobHistory.completed') },
  { code: 'reassigned', label: t('team.technicianJobHistory.reassigned') },
  { code: 'cancelled', label: t('team.technicianJobHistory.cancelled') },
]);
const PERIOD_CHOICES: Choice<JobHistoryPeriod>[] = localizedList(() => [
  { code: '30d', label: t('team.technicianJobHistory.last30Days') },
  { code: '90d', label: t('team.technicianJobHistory.last90Days') },
  { code: '12m', label: t('team.technicianJobHistory.last12Months') },
  { code: 'all', label: t('team.technicianJobHistory.allTime') },
]);
const SORT_CHOICES: Choice<JobHistorySort>[] = localizedList(() => [
  { code: 'newest', label: t('team.technicianJobHistory.newestFirst') },
  { code: 'oldest', label: t('team.technicianJobHistory.oldestFirst') },
]);

type HistoryResult =
  | { readonly ok: true; readonly response: JobHistoryResponse }
  | { readonly ok: false; readonly error: unknown };

/**
 * Technician Job history (`/team/:technicianId/job-history`): period metrics and a server-paged
 * table whose filters live in the URL query (BR-16 to BR-19). Backend authorization is
 * authoritative; roles without `TeamView` send no request.
 */
@Component({
  selector: 'app-technician-job-history',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    FormsModule,
    RouterLink,
    ButtonDirective,
    IconField,
    InputIcon,
    InputText,
    Message,
    Paginator,
    Select,
    Skeleton,
    TechnicianProfileHeader,
  ],
  templateUrl: './technician-job-history.html',
  styleUrl: './technician-job-history.scss',
})
export class TechnicianJobHistory {
  private readonly team = inject(TeamService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly profile = injectProfileLoader();
  protected readonly detail = this.profile.detail;
  protected readonly statusChoices = STATUS_CHOICES;
  protected readonly periodChoices = PERIOD_CHOICES;
  protected readonly sortChoices = SORT_CHOICES;
  protected readonly pageSizes = [...JOB_HISTORY_PAGE_SIZES];
  protected readonly outcomeLabels = OUTCOME_LABELS;
  protected readonly outcomeSeverity = outcomeSeverity;
  protected readonly skeletonRows = [0, 1, 2, 3, 4];

  private readonly requests = new Subject<JobHistoryQuery>();
  private readonly searchInput = new Subject<string>();

  protected readonly query = signal<JobHistoryQuery>(
    parseJobHistoryUrl(this.route.snapshot.queryParamMap),
  );
  protected readonly searchText = signal(this.query().search);
  protected readonly response = signal<JobHistoryResponse | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly hasFilters = computed(
    () => this.query().search !== '' || this.query().status !== 'all',
  );
  protected readonly first = computed(() => (this.query().page - 1) * this.query().pageSize);
  protected readonly total = computed(() => this.response()?.total ?? 0);
  protected readonly report = computed(() => {
    const total = this.total();
    const from = total === 0 ? 0 : this.first() + 1;
    const to = Math.min(this.first() + this.query().pageSize, total);
    return t('team.technicianJobHistory.showing', { from: from, to: to, total: total });
  });
  protected readonly cards = computed(() => {
    const summary = this.response()?.summary;
    return [
      {
        key: 'completed',
        icon: 'pi-briefcase',
        label: t('team.technicianJobHistory.completedJobs'),
        value: summary ? String(summary.completedJobs) : '—',
      },
      {
        key: 'onTime',
        icon: 'pi-clock',
        label: t('team.technicianJobHistory.onTime'),
        value: typeof summary?.onTimePercent === 'number' ? `${summary.onTimePercent}%` : '—',
      },
      {
        key: 'hours',
        icon: 'pi-chart-bar',
        label: t('team.technicianJobHistory.hoursLogged'),
        value: summary ? formatMinutes(summary.loggedMinutes) : '—',
      },
      {
        key: 'duration',
        icon: 'pi-calendar',
        label: t('team.technicianJobHistory.avgDuration'),
        value: optionalMinutes(summary?.avgDurationMinutes),
      },
    ];
  });
  protected readonly rows = computed(() =>
    (this.response()?.items ?? []).map((item) => ({
      item,
      date: formatLocalDate(item.participationDate),
      logged: formatMinutes(item.loggedMinutes),
      link: ['/jobs', item.workOrderId],
    })),
  );
  protected readonly emptyText = computed(() =>
    this.hasFilters()
      ? t('team.technicianJobHistory.noMatch')
      : this.query().period === 'all'
        ? t('team.technicianJobHistory.noHistory')
        : t('team.technicianJobHistory.noJobsInPeriod'),
  );

  constructor() {
    if (this.profile.forbidden()) {
      this.loading.set(false);
      return;
    }
    this.profile.load();

    this.requests
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.loadFailed.set(false);
        }),
        switchMap((query) =>
          this.team.jobHistory(this.profile.technicianId(), query).pipe(
            map((response): HistoryResult => ({ ok: true, response })),
            catchError((error: unknown) => of<HistoryResult>({ ok: false, error })),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.handleResult(result));

    this.searchInput
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        const search = value.trim();
        if (search !== this.query().search) {
          this.go({ search, page: 1 }, true);
        }
      });

    // The URL is the source of truth: invalid values are replaced before any request (BR-19).
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const parsed = parseJobHistoryUrl(params);
      const canonical = jobHistoryUrlParams(parsed);
      if (needsCanonicalUrl(params, canonical)) {
        this.navigate(canonical, true);
        return;
      }
      this.query.set(parsed);
      this.searchText.set(parsed.search);
      this.reload();
    });
  }

  protected reload(): void {
    this.requests.next(this.query());
  }

  private handleResult(result: HistoryResult): void {
    if (!result.ok) {
      this.loading.set(false);
      if (!this.profile.handleAccessFailure(result.error)) {
        this.loadFailed.set(true);
      }
      return;
    }
    const { items, total, pageSize } = result.response;
    const lastPage = Math.max(1, Math.ceil(total / pageSize));
    if (items.length === 0 && total > 0 && this.query().page > lastPage) {
      this.go({ page: lastPage }, true);
      return;
    }
    this.response.set(result.response);
    this.loading.set(false);
  }

  // Filters, sorting, paging (BR-19): each change writes the URL and resets the page.

  private go(patch: Partial<JobHistoryQuery>, replaceUrl = false): void {
    this.navigate(jobHistoryUrlParams({ ...this.query(), ...patch }), replaceUrl);
  }

  private navigate(queryParams: Record<string, string | null>, replaceUrl: boolean): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  protected onSearchText(value: string): void {
    this.searchText.set(value);
    this.searchInput.next(value);
  }

  protected onStatus(status: JobHistoryStatus): void {
    this.go({ status, page: 1 });
  }

  protected onPeriod(period: JobHistoryPeriod): void {
    this.go({ period, page: 1 });
  }

  protected onSort(sort: JobHistorySort): void {
    this.go({ sort, page: 1 });
  }

  /** Clears search and status; the period stays (BR-22). */
  protected clearFilters(): void {
    this.go({ search: '', status: 'all', page: 1 });
  }

  protected onPageChange(event: PaginatorState): void {
    const rows = event.rows ?? this.query().pageSize;
    if (rows !== this.query().pageSize) {
      this.go({ pageSize: rows, page: 1 });
    } else {
      this.go({ page: (event.page ?? 0) + 1 });
    }
  }

  protected retry(): void {
    this.reload();
  }
}
