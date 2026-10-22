import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Subscription } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { SessionService } from '../../../../core/services/session.service';
import { UnresolvedJobs } from '../../components/unresolved-jobs/unresolved-jobs';
import { ScheduleResponse } from '../../models/technician-visits.model';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import {
  addDays,
  cityLine,
  dayHeading,
  dayName,
  dayNumber,
  durationText,
  localDateOf,
  summaryText,
  weekdayIndex,
} from '../../utils/schedule-format';
import {
  TechnicianLoadFailure,
  accessMessage,
  classifyFailure,
} from '../../utils/technician-errors';
import { STATUS_LABELS, formatTime } from '../../utils/technician-format';

import { t } from '../../../../core/i18n/translate';
export const SCHEDULE_LOAD_ERROR_MESSAGE = (): string =>
  t('technicianJobs.schedule.scheduleLoadErrorMessage');
export const NO_JOBS_DAY_MESSAGE = (): string => t('technicianJobs.schedule.noJobsDayMessage');
export const TIMEZONE_NOTICE = (): string => t('technicianJobs.schedule.timezoneNotice');
const SKELETON_CARDS = [0, 1, 2];

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;

interface WeekRequest {
  readonly weekStart: string | null;
  /** Day selected once the week arrives. */
  readonly selected: string | null;
}

/**
 * My schedule (`/today/schedule`): week strip, day summary and timeline of the caller's visits
 * (BR-15–BR-18). A failed week change keeps the shown week; stale responses are ignored.
 */
@Component({
  selector: 'app-technician-schedule',
  imports: [TranslocoPipe, RouterLink, ButtonDirective, Message, Skeleton, UnresolvedJobs],
  templateUrl: './schedule.html',
  styleUrl: './schedule.scss',
})
export class TechnicianSchedule {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly sessionService = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private inFlight: Subscription | null = null;
  private pending: WeekRequest | null = null;

  get loadErrorMessage(): string {
    return SCHEDULE_LOAD_ERROR_MESSAGE();
  }
  get emptyMessage(): string {
    return NO_JOBS_DAY_MESSAGE();
  }
  get timezoneNotice(): string {
    return TIMEZONE_NOTICE();
  }
  readonly skeletonCards = SKELETON_CARDS;
  readonly statusLabels = STATUS_LABELS;

  readonly state = signal<PageState>('loading');
  readonly data = signal<ScheduleResponse | null>(null);
  readonly selected = signal<string>('');
  /** A week change is in flight; the previous week stays visible. */
  readonly changingWeek = signal(false);
  /** The last week change failed; the previous week and selection stay (BR-18). */
  readonly weekFailed = signal(false);

  readonly accessText = computed(() => {
    const state = this.state();
    return state === 'loading' || state === 'ready' || state === 'error'
      ? null
      : accessMessage(state);
  });
  readonly timezone = computed(() => this.data()?.timezone ?? 'UTC');
  readonly days = computed(() =>
    (this.data()?.days ?? []).map((day) => ({
      date: day.date,
      name: dayName(day.date),
      number: dayNumber(day.date),
      isToday: day.date === this.data()?.today,
    })),
  );
  readonly heading = computed(() => (this.selected() === '' ? '' : dayHeading(this.selected())));
  readonly summary = computed(() =>
    summaryText(this.data()?.days.find((day) => day.date === this.selected())),
  );
  readonly dayVisits = computed(() => {
    const data = this.data();
    return (data?.visits ?? [])
      .filter((visit) => localDateOf(visit.start, data?.timezone ?? 'UTC') === this.selected())
      .map((visit) => ({
        visit,
        time: formatTime(visit.start, data?.timezone ?? 'UTC'),
        status: STATUS_LABELS[visit.status],
        duration: durationText(visit.durationMinutes),
        city: cityLine(visit.address),
        cancelled: visit.status === 'cancelled',
      }));
  });

  constructor() {
    this.destroyRef.onDestroy(() => this.inFlight?.unsubscribe());
    if (this.sessionService.session()?.role.code !== 'technician') {
      this.state.set('forbidden');
      return;
    }
    this.request({ weekStart: null, selected: null });
  }

  /** Retry of the full-page error or of the failed week change. */
  retry(): void {
    this.request(this.pending ?? { weekStart: null, selected: null });
  }

  changeWeek(direction: -1 | 1): void {
    const data = this.data();
    if (data === null) {
      return;
    }
    const weekStart = addDays(data.weekStart, direction * 7);
    const selected = this.selected();
    this.request({
      weekStart,
      selected: addDays(weekStart, selected === '' ? 0 : weekdayIndex(selected)),
    });
  }

  select(date: string): void {
    this.selected.set(date);
  }

  /** Arrow keys move the selection inside the week strip. */
  moveSelection(event: KeyboardEvent, index: number): void {
    const days = this.days();
    const next =
      event.key === 'ArrowRight'
        ? index + 1
        : event.key === 'ArrowLeft'
          ? index - 1
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? days.length - 1
              : -1;
    if (next < 0 || next >= days.length) {
      return;
    }
    event.preventDefault();
    this.select(days[next].date);
    const tabs = (event.currentTarget as HTMLElement).parentElement?.querySelectorAll<HTMLElement>(
      '[role="tab"]',
    );
    tabs?.item(next).focus();
  }

  /** One request at a time: a new week replaces the in-flight one, and its response is ignored. */
  private request(week: WeekRequest): void {
    if (
      this.inFlight !== null &&
      this.pending !== null &&
      this.pending.weekStart === week.weekStart &&
      !this.weekFailed()
    ) {
      return;
    }
    this.inFlight?.unsubscribe();
    this.pending = week;
    const hasData = this.data() !== null;
    if (hasData) {
      this.changingWeek.set(true);
      this.weekFailed.set(false);
    } else {
      this.state.set('loading');
    }

    const subscription = this.visits.schedule(week.weekStart).subscribe({
      next: (response) => {
        this.inFlight = null;
        this.pending = null;
        this.data.set(response);
        this.selected.set(
          response.days.some((day) => day.date === week.selected)
            ? (week.selected as string)
            : response.today,
        );
        this.changingWeek.set(false);
        this.weekFailed.set(false);
        this.state.set('ready');
      },
      error: (error: unknown) => {
        this.inFlight = null;
        this.changingWeek.set(false);
        const failure = classifyFailure(error);
        if (hasData && (failure === 'failed' || failure === 'not-found')) {
          this.weekFailed.set(true);
          return;
        }
        this.pending = null;
        this.state.set(failure === 'failed' || failure === 'not-found' ? 'error' : failure);
      },
    });
    if (!subscription.closed) {
      this.inFlight = subscription;
    }
  }
}
