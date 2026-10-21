import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { Message } from 'primeng/message';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';
import { Toast } from 'primeng/toast';
import { ToggleSwitch } from 'primeng/toggleswitch';
import { TranslocoPipe } from '@jsverse/transloco';

import { canSeeBusinessOverview } from '../../../../core/config/dashboard-roles';
import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { saveCsv } from '../../../../shared/utils/csv-file';
import { DashboardViewSwitch } from '../../../dashboard/components/dashboard-view-switch/dashboard-view-switch';
import { agoLabel } from '../../../dashboard/utils/dashboard-refresh';
import { handleUnauthorized } from '../../../organizations/utils/handle-unauthorized';
import { todayInZone } from '../../../schedule/utils/schedule-range';
import { BranchPerformance } from '../../components/branch-performance/branch-performance';
import { BusinessKpisRow } from '../../components/business-kpis/business-kpis';
import { CashFlowCard } from '../../components/cash-flow/cash-flow';
import { OwnerAttention } from '../../components/owner-attention/owner-attention';
import { RevenueChart } from '../../components/revenue-chart/revenue-chart';
import { SalesPipeline } from '../../components/sales-pipeline/sales-pipeline';
import { ServiceProfitability } from '../../components/service-profitability/service-profitability';
import { TeamCapacityCard } from '../../components/team-capacity/team-capacity';
import { RangePreset } from '../../models/business-overview.model';
import { BusinessOverviewStore } from '../../services/business-overview.store';
import { RANGE_CHOICES, periodLabel, resolveBusinessRoute } from '../../utils/business-params';

import { t } from '../../../../core/i18n/translate';
import { LocalizedRefPipe } from '../../../../core/i18n/localized-ref.pipe';
export const FORBIDDEN_MESSAGE = (): string =>
  t('businessOverview.businessOverview.forbiddenMessage');
export const NO_BRANCHES_MESSAGE = (): string =>
  t('businessOverview.businessOverview.noBranchesMessage');
export const LOAD_ERROR_MESSAGE = (): string =>
  t('businessOverview.businessOverview.loadErrorMessage');
export const REFRESH_ERROR_MESSAGE = (): string =>
  t('businessOverview.businessOverview.refreshErrorMessage');
export const EXPORT_ERROR_MESSAGE = (): string =>
  t('businessOverview.businessOverview.exportErrorMessage');

const ALL = 'all';
const TICK_MS = 10_000;

/**
 * Business view of `/dashboard` (Owner): URL-driven range, branch and comparison; cards load per
 * selection and the comparison toggle never refetches. Backend authorization is authoritative.
 */
@Component({
  selector: 'app-business-overview',
  imports: [
    LocalizedRefPipe,
    TranslocoPipe,
    BranchPerformance,
    BusinessKpisRow,
    CashFlowCard,
    DashboardViewSwitch,
    FormsModule,
    Message,
    OwnerAttention,
    RevenueChart,
    SalesPipeline,
    Select,
    ServiceProfitability,
    Skeleton,
    TeamCapacityCard,
    Toast,
    ToggleSwitch,
  ],
  providers: [BusinessOverviewStore, MessageService],
  templateUrl: './business-overview.html',
  styleUrl: './business-overview.scss',
})
export class BusinessOverviewPage {
  protected readonly store = inject(BusinessOverviewStore);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);

  get forbiddenMessage(): string {
    return FORBIDDEN_MESSAGE();
  }
  get noBranchesMessage(): string {
    return NO_BRANCHES_MESSAGE();
  }
  get loadErrorMessage(): string {
    return LOAD_ERROR_MESSAGE();
  }
  get refreshErrorMessage(): string {
    return REFRESH_ERROR_MESSAGE();
  }
  readonly rangeChoices = [...RANGE_CHOICES];
  readonly kpiSkeletons = [0, 1, 2, 3, 4, 5];

  readonly forbidden = computed(
    () =>
      !canSeeBusinessOverview(this.sessionService.session()?.role.code) ||
      this.store.access() === 'forbidden',
  );
  private readonly sessionExpired = signal(false);
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  private readonly now = signal(Date.now());

  readonly exporting = signal(false);
  readonly ready = computed(() => this.store.optionsStatus() === 'ready');
  readonly noBranches = computed(
    () => this.ready() && (this.store.options()?.branches.length ?? 0) === 0,
  );
  readonly selection = computed(() => this.store.route());
  readonly today = computed(() => {
    const options = this.store.options();
    return options === null ? '' : todayInZone(options.timezone, new Date(this.now()));
  });
  readonly agoLabel = computed(() => agoLabel(this.store.lastUpdatedAt(), this.now()));
  readonly compare = computed(() => this.selection()?.compare ?? true);

  readonly branchChoices = computed(() => {
    const branches = this.store.options()?.branches ?? [];
    const all =
      branches.length > 1
        ? [{ id: ALL, name: t('businessOverview.businessOverview.allBranches') }]
        : [];
    return [...all, ...branches];
  });
  readonly branchValue = computed(() => this.selection()?.branchId ?? ALL);
  readonly subtitle = computed(() => {
    const value = this.selection();
    if (value === null) {
      return '';
    }
    const branch = this.store.options()?.branches.find((item) => item.id === value.branchId);
    return `${periodLabel(value.from, value.to)} · ${branch?.name ?? t('businessOverview.businessOverview.allBranches')}`;
  });
  /** Range and comparison carried by the Owner attention branch links (BR-18). */
  readonly keptQuery = computed((): Readonly<Record<string, string>> => {
    const value = this.selection();
    if (value === null) {
      return { view: 'business' };
    }
    return {
      view: 'business',
      range: value.range,
      ...(value.range === 'custom' ? { from: value.from, to: value.to } : {}),
      ...(value.compare ? {} : { compare: 'false' }),
    };
  });

  constructor() {
    if (!this.forbidden()) {
      this.store.loadOptions();
    }

    // The URL is the single source of the selection; invalid values are replaced by defaults.
    effect(() => {
      const params = this.queryParams();
      const options = this.store.options();
      if (this.forbidden() || options === null || options.branches.length === 0) {
        return;
      }
      untracked(() => {
        const resolved = resolveBusinessRoute(params, options);
        if (resolved.normalized) {
          this.go(resolved.query, true);
        } else {
          this.store.applyRoute(resolved.route);
        }
      });
    });

    // A branch or range the API rejected goes back to its default (replaceUrl).
    effect(() => {
      const rejected = this.store.rejected();
      if (rejected === null) {
        return;
      }
      untracked(() => {
        this.store.rejected.set(null);
        if (rejected === 'branch') {
          this.go({ branch: null }, true);
          this.store.loadOptions();
        } else {
          this.go({ range: null, from: null, to: null }, true);
        }
      });
    });

    effect(() => {
      if (this.store.access() === 'unauthorized') {
        untracked(() => handleUnauthorized(this.router, this.sessionExpired));
      }
    });

    // One clock feeds the "Updated" label; there is no automatic refresh (BR-04).
    const timer = setInterval(() => this.now.set(Date.now()), TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  private go(params: Record<string, string | null>, replaceUrl: boolean): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }

  // Header controls: user changes add history entries so back restores the selection (AC-05).

  onBranch(value: string): void {
    this.go({ branch: value === ALL ? null : value }, false);
  }

  onRange(range: RangePreset): void {
    const value = this.selection();
    if (range === 'custom') {
      this.go({ range, from: value?.from ?? null, to: value?.to ?? null }, false);
    } else {
      this.go({ range, from: null, to: null }, false);
    }
  }

  onCustomDate(field: 'from' | 'to', value: string): void {
    if (value !== '') {
      this.go({ range: 'custom', [field]: value }, false);
    }
  }

  onCompare(checked: boolean): void {
    this.go({ compare: checked ? null : 'false' }, false);
  }

  onExport(): void {
    const request = this.store.export();
    if (request === null || this.exporting()) {
      return;
    }
    this.exporting.set(true);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (file) => {
        this.exporting.set(false);
        saveCsv(file);
      },
      error: (error: unknown) => {
        this.exporting.set(false);
        if (isApiError(error) && error.kind === 'unauthorized') {
          handleUnauthorized(this.router, this.sessionExpired);
        } else {
          this.messages.add({ severity: 'error', summary: EXPORT_ERROR_MESSAGE() });
        }
      },
    });
  }
}
