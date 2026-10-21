import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Dialog } from 'primeng/dialog';
import { TranslocoPipe } from '@jsverse/transloco';

import { UnresolvedVisit } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import { formatInstantShortDate, pluralLabel, statusLabelOf } from '../../utils/technician-format';
import { statusSinceText } from '../../utils/schedule-format';
import { classifyResumeFailure } from '../../utils/technician-errors';

import { localized, t } from '../../../../core/i18n/translate';
/** Technician shell bottom nav (4rem) plus safe area; the sheet and backdrop end above it. */
const NAV_OFFSET = 'calc(4rem + env(safe-area-inset-bottom, 0px))';

/** BR-17: Unfinished jobs block on Schedule and the modal bottom sheet with its resolution actions. */
@Component({
  selector: 'app-unresolved-jobs',
  imports: [TranslocoPipe, RouterLink, ButtonDirective, Dialog],
  templateUrl: './unresolved-jobs.html',
  styleUrl: './unresolved-jobs.scss',
})
export class UnresolvedJobs {
  private readonly visitsService = inject(TechnicianVisitsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');

  readonly visits = input.required<readonly UnresolvedVisit[]>();
  readonly timezone = input.required<string>();

  /** Bottom sheet constrained to the technician column (480px), anchored to the bottom. */
  protected readonly sheetStyle = localized(() => ({
    width: '100%',
    maxWidth: '30rem',
    maxHeight: `min(85vh, calc(100dvh - ${NAV_OFFSET} - 4rem))`,
    margin: '0',
    borderRadius: '1.5rem 1.5rem 0 0',
  }));
  protected readonly maskStyle = {
    bottom: NAV_OFFSET,
    display: 'flex',
    alignItems: 'flex-end',
    justifyContent: 'center',
  };

  readonly open = signal(false);
  /** Visit whose Resume job call is in flight; blocks repeats. */
  readonly resuming = signal<string | null>(null);
  readonly errors = signal<Readonly<Record<string, string>>>({});

  readonly count = computed(() => this.visits().length);
  readonly summary = computed(() =>
    t('technicianJobs.unresolvedJobs.fromPreviousDays', {
      count: this.count(),
      value: pluralLabel(
        this.count(),
        t('technicianJobs.plurals.job'),
        t('technicianJobs.plurals.jobs'),
      ),
    }),
  );
  readonly cards = computed(() => {
    const now = new Date();
    const errors = this.errors();
    return this.visits().map((visit) => ({
      visit,
      status: statusLabelOf(visit.status),
      date:
        visit.scheduledStart === null
          ? null
          : formatInstantShortDate(visit.scheduledStart, this.timezone()),
      since: statusSinceText(visit.status, visit.statusSince, now),
      customer: (visit.customerName as string | null | undefined)?.trim() || null,
      paused: visit.status === 'paused',
      error: errors[visit.visitId] || null,
    }));
  });

  show(): void {
    this.open.set(true);
  }

  /** Dialog visibility change; closing returns focus to the block. */
  setVisible(visible: boolean): void {
    if (visible) {
      this.open.set(true);
      return;
    }
    this.close();
  }

  close(): void {
    this.open.set(false);
    afterNextRender(() => this.trigger()?.nativeElement.focus(), { injector: this.injector });
  }

  /** Navigating to the visit closes the sheet without moving focus back. */
  navigated(): void {
    this.open.set(false);
  }

  resume(visitId: string): void {
    if (this.resuming() !== null) {
      return;
    }
    this.resuming.set(visitId);
    this.errors.update((current) => ({ ...current, [visitId]: '' }));
    this.visitsService
      .resume(visitId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.resuming.set(null);
          this.open.set(false);
          void this.router.navigate(['/today/visits', visitId]);
        },
        error: (error: unknown) => {
          this.resuming.set(null);
          const { message } = classifyResumeFailure(error);
          this.errors.update((current) => ({ ...current, [visitId]: message }));
        },
      });
  }
}
