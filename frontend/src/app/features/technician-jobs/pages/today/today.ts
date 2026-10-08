import {
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { ProgressBar } from 'primeng/progressbar';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';
import { Toast } from 'primeng/toast';

import { SessionService } from '../../../../core/services/session.service';
import { TOAST_STATE_KEY, ToastHandoff } from '../../../requests/models/requests.model';
import { VisitActions } from '../../components/visit-actions/visit-actions';
import { TodayResponse, TodayVisit } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  TechnicianLoadFailure,
  accessMessage,
  classifyFailure,
  classifyTravelFailure,
} from '../../utils/technician-errors';
import {
  STATUS_LABELS,
  STATUS_SEVERITIES,
  addressLine,
  arrivalWindowText,
  formatLongDate,
  formatScheduledHours,
  formatTime,
  formatTimeRange,
  greetingFor,
  isCompleted,
  pluralLabel,
  priorityLabel,
  progressPercent,
  updatedText,
} from '../../utils/technician-format';

export const LOAD_ERROR_MESSAGE = "We couldn't load today's jobs. Try again.";
export const NO_JOBS_MESSAGE = 'No jobs scheduled for today.';
export const ALL_DONE_MESSAGE = 'All jobs completed for today.';
export const NO_STARTABLE_MESSAGE = 'No more jobs to start today.';
const TICK_MS = 30_000;
const SKELETON_ROWS = [0, 1, 2];
const IN_PROGRESS_GROUP = ['on_the_way', 'in_progress', 'paused'];

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;

/**
 * Today's jobs (`/today`, Design 7). The only write is Start travel on the next job card, shown to
 * the primary technician of an `assigned` visit (BR-15). Roles other than `technician` get the
 * forbidden state with no request (UX only; the backend decides).
 */
@Component({
  selector: 'app-today',
  imports: [RouterLink, ButtonDirective, Message, ProgressBar, Skeleton, Tag, Toast, VisitActions],
  providers: [MessageService],
  templateUrl: './today.html',
  styleUrls: ['./today.scss', './today-route.scss'],
})
export class Today {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly sessionService = inject(SessionService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly messageService = inject(MessageService);

  readonly loadErrorMessage = LOAD_ERROR_MESSAGE;
  readonly statusLabels = STATUS_LABELS;
  readonly statusSeverities = STATUS_SEVERITIES;
  readonly skeletonRows = SKELETON_ROWS;

  readonly state = signal<PageState>('loading');
  readonly data = signal<TodayResponse | null>(null);
  readonly refreshing = signal(false);
  readonly refreshFailed = signal(false);
  /** Start travel in flight on the next job card; blocks repeats (BR-15). */
  readonly starting = signal(false);
  readonly startError = signal<string | null>(null);
  private readonly loadedAt = signal(0);
  private readonly now = signal(Date.now());
  private readonly greetingText = signal('');

  readonly accessText = computed(() => {
    const state = this.state();
    return state === 'loading' || state === 'ready' || state === 'error'
      ? null
      : accessMessage(state);
  });

  readonly timezone = computed(() => this.data()?.timezone ?? 'UTC');
  readonly greeting = computed(
    () => `${this.greetingText()}, ${this.data()?.technician.firstName ?? ''}`,
  );
  readonly dateText = computed(() => formatLongDate(this.data()?.date ?? ''));
  readonly updated = computed(() => updatedText(this.loadedAt(), this.now()));

  readonly metrics = computed(() => {
    const metrics = this.data()?.metrics;
    if (metrics === undefined) {
      return [];
    }
    const hours = formatScheduledHours(metrics.scheduledMinutes);
    return [
      {
        icon: 'pi-briefcase',
        value: String(metrics.jobs),
        label: pluralLabel(metrics.jobs, 'job', 'jobs'),
      },
      {
        icon: 'pi-clock',
        value: hours,
        label: pluralLabel(hours, 'scheduled hour', 'scheduled hours'),
      },
      { icon: 'pi-flag', value: String(metrics.remaining), label: 'remaining' },
    ];
  });

  readonly percent = computed(() => {
    const metrics = this.data()?.metrics;
    return metrics === undefined ? 0 : progressPercent(metrics.completed, metrics.jobs);
  });
  readonly progressText = computed(() => {
    const metrics = this.data()?.metrics;
    return `${metrics?.completed ?? 0} of ${metrics?.jobs ?? 0} completed`;
  });

  readonly nextVisit = computed(() => {
    const data = this.data();
    return data?.visits.find((visit) => visit.visitId === data.nextVisitId) ?? null;
  });
  readonly nextLabel = computed(() =>
    IN_PROGRESS_GROUP.includes(this.nextVisit()?.status ?? '') ? 'IN PROGRESS' : 'NEXT',
  );
  readonly emptyText = computed(() => {
    const metrics = this.data()?.metrics;
    if (metrics === undefined) {
      return '';
    }
    if (metrics.jobs === 0) {
      return NO_JOBS_MESSAGE;
    }
    return metrics.remaining === 0 ? ALL_DONE_MESSAGE : NO_STARTABLE_MESSAGE;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), TICK_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    // Job completion hands over its "Job completed." toast through the navigation state.
    const handoff = this.router.currentNavigation()?.extras.state?.[TOAST_STATE_KEY] as
      ToastHandoff | undefined;
    if (handoff !== undefined) {
      afterNextRender(() => this.messageService.add(handoff), { injector: this.injector });
    }

    if (this.sessionService.session()?.role.code !== 'technician') {
      this.state.set('forbidden');
      return;
    }
    this.load();
  }

  /** Initial load and Retry (full-page states) when no data is shown yet; otherwise a refresh. */
  load(): void {
    if (this.refreshing() || this.state() === 'forbidden') {
      return;
    }
    const hasData = this.data() !== null;
    if (hasData) {
      this.refreshing.set(true);
      this.refreshFailed.set(false);
    } else {
      this.state.set('loading');
    }

    this.visits.today().subscribe({
      next: (response) => {
        this.data.set(response);
        this.greetingText.set(greetingFor(new Date(), response.timezone));
        this.loadedAt.set(Date.now());
        this.now.set(Date.now());
        this.refreshing.set(false);
        this.state.set('ready');
      },
      error: (error: unknown) => {
        this.refreshing.set(false);
        if (hasData) {
          this.refreshFailed.set(true);
          return;
        }
        const failure = classifyFailure(error);
        this.state.set(failure === 'failed' || failure === 'not-found' ? 'error' : failure);
      },
    });
  }

  /** BR-15: only an `assigned` next visit where the caller is primary. */
  canStartTravel(visit: TodayVisit): boolean {
    return visit.status === 'assigned' && visit.isPrimary;
  }

  /** Records the transition, then opens the job page; a failure stays on the card. */
  startTravel(visit: TodayVisit): void {
    if (this.starting()) {
      return;
    }
    this.starting.set(true);
    this.startError.set(null);
    this.visits.startTravel(visit.visitId).subscribe({
      next: () => {
        this.router.navigate(['/today/visits', visit.visitId]).finally(() => {
          this.starting.set(false);
        });
      },
      error: (error: unknown) => {
        this.starting.set(false);
        this.startError.set(classifyTravelFailure(error).message);
      },
    });
  }

  time(visit: TodayVisit): string {
    return formatTime(visit.start, this.timezone());
  }

  range(visit: TodayVisit): string {
    return formatTimeRange(visit.start, visit.end, this.timezone());
  }

  /** Design 7: time range with an "Arrival window" caption, or "Arrival at" for a single time. */
  arrival(visit: TodayVisit): { main: string; caption: string | null } | null {
    const text = arrivalWindowText(visit, this.timezone());
    if (text === null) {
      return null;
    }
    const prefix = 'Arrival window: ';
    return text.startsWith(prefix)
      ? { main: text.slice(prefix.length), caption: 'Arrival window' }
      : { main: text, caption: null };
  }

  address(visit: TodayVisit): string {
    return addressLine(visit.address);
  }

  priority(visit: TodayVisit): string | null {
    return priorityLabel(visit.priority);
  }

  completed(visit: TodayVisit): boolean {
    return isCompleted(visit.status);
  }

  isNext(visit: TodayVisit): boolean {
    return visit.visitId === this.data()?.nextVisitId;
  }

  chipLabel(visit: TodayVisit): string {
    return this.isNext(visit) ? 'Next' : STATUS_LABELS[visit.status];
  }

  chipClass(visit: TodayVisit): string {
    if (this.isNext(visit)) {
      return 'today__chip today__chip--next';
    }
    const severity = STATUS_SEVERITIES[visit.status];
    return `today__chip today__chip--${severity === 'info' ? 'active' : severity}`;
  }

  materialsText(visit: TodayVisit): string | null {
    const count = visit.plannedMaterialsCount;
    return count > 0 ? `${count} planned ${pluralLabel(count, 'material', 'materials')}` : null;
  }
}
