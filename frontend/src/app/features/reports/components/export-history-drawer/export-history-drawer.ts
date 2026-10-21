import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';
import { Subject, catchError, of, switchMap, tap } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { DrawerShell } from '../../../../shared/components/drawer-shell/drawer-shell';
import {
  ReportChoice,
  ReportExportItem,
  REPORTS_HISTORY_EMPTY_MESSAGE,
  REPORTS_HISTORY_ERROR_MESSAGE,
} from '../../models/reports.model';
import { ReportsService } from '../../services/reports.service';
import { formatDate, formatDateTime } from '../../utils/report-view';

import { t } from '../../../../core/i18n/translate';
type HistoryState =
  | { readonly status: 'idle' | 'loading' | 'error' }
  | { readonly status: 'ready'; readonly items: readonly ReportExportItem[] };

interface HistoryRow {
  readonly item: ReportExportItem;
  readonly when: string;
  readonly format: string;
  readonly period: string;
  readonly branch: string;
}

const UNAVAILABLE_BRANCH = (): string => t('reports.exportHistoryDrawer.unavailableBranch');

/**
 * BR-17: the caller's own recent report exports from the audit trail, with Open report. A modal
 * dialog that traps focus and returns it to the Export history button.
 */
@Component({
  selector: 'app-export-history-drawer',
  imports: [TranslocoPipe, ButtonDirective, DrawerShell, Skeleton],
  templateUrl: './export-history-drawer.html',
  styleUrl: './export-history-drawer.scss',
})
export class ExportHistoryDrawer {
  private readonly api = inject(ReportsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly loads = new Subject<void>();

  readonly open = input(false);
  readonly timezone = input('UTC');
  readonly branches = input<readonly ReportChoice[]>([]);

  readonly closed = output<void>();
  readonly reportOpened = output<ReportExportItem>();
  readonly sessionExpired = output<void>();

  readonly titleId = 'export-history-title';
  get errorMessage(): string {
    return REPORTS_HISTORY_ERROR_MESSAGE();
  }
  get emptyMessage(): string {
    return REPORTS_HISTORY_EMPTY_MESSAGE();
  }
  readonly skeletons = [1, 2, 3];
  readonly state = signal<HistoryState>({ status: 'idle' });

  readonly rows = computed<HistoryRow[]>(() => {
    const state = this.state();
    return state.status !== 'ready'
      ? []
      : state.items.map((item) => ({
          item,
          when: formatDateTime(item.occurredAt, this.timezone()),
          format: item.format.toUpperCase(),
          period: this.period(item),
          branch: this.branch(item),
        }));
  });

  constructor() {
    this.loads
      .pipe(
        tap(() => this.state.set({ status: 'loading' })),
        switchMap(() =>
          this.api.exports().pipe(
            tap((items) => this.state.set({ status: 'ready', items })),
            catchError((error: unknown) => {
              this.state.set({ status: 'error' });
              if (isApiError(error) && error.kind === 'unauthorized') {
                this.sessionExpired.emit();
              }
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    let wasOpen = false;
    effect(() => {
      const isOpen = this.open();
      if (isOpen && !wasOpen) {
        this.loads.next();
      }
      wasOpen = isOpen;
    });
  }

  retry(): void {
    this.loads.next();
  }

  private period(item: ReportExportItem): string {
    const { from, to, asOf } = item.filters;
    if (from && to) {
      return `${formatDate(from)} – ${formatDate(to)}`;
    }
    return asOf ? t('reports.exportHistoryDrawer.asOf', { asOf: formatDate(asOf) }) : '';
  }

  private branch(item: ReportExportItem): string {
    const id = item.filters.branchId;
    if (!id) {
      return t('reports.exportHistoryDrawer.allBranches');
    }
    return this.branches().find((branch) => branch.id === id)?.name ?? UNAVAILABLE_BRANCH();
  }
}
