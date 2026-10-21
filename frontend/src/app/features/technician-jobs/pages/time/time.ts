import { Component, DestroyRef, ElementRef, computed, inject, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Subscription } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { isApiError } from '../../../../core/models/api-error.model';
import { SessionService } from '../../../../core/services/session.service';
import { TimeEntry, TimeSummaryResponse, TimeView } from '../../models/technician-visits.model';
import { TimeEntryDialog } from '../../components/time-entry-dialog/time-entry-dialog';
import { TimeTypeIcon } from '../../components/time-type-icon/time-type-icon';
import { TechnicianVisitsService } from '../../services/technician-visits.service';
import { addDays, localDateOf, weekdayIndex } from '../../utils/schedule-format';
import {
  TechnicianLoadFailure,
  accessMessage,
  classifyFailure,
} from '../../utils/technician-errors';
import { formatTime } from '../../utils/technician-format';
import {
  TYPE_LABELS,
  dayAlternative,
  detailDate,
  formatSeconds,
  rangeLabel,
  sectionHeading,
  weekdayName,
} from '../../utils/time-format';

import { t } from '../../../../core/i18n/translate';
export const TIME_LOAD_ERROR_MESSAGE = (): string => t('technicianJobs.time.timeLoadErrorMessage');
export const TIME_NOTICE = (): string => t('technicianJobs.time.timeNotice');
export const TIME_READ_ONLY_MESSAGE = (): string => t('technicianJobs.time.timeReadOnlyMessage');
export const REFRESH_MS = 60_000;

type PageState = 'loading' | 'ready' | 'error' | TechnicianLoadFailure;

interface TimeRequest {
  readonly view: TimeView;
  readonly date: string | null;
}

/**
 * Time summary (`/today/time`): totals, week chart and entries grouped by day (BR-06–BR-11).
 * A failed navigation keeps the shown range; stale responses are ignored.
 */
@Component({
  selector: 'app-technician-time',
  imports: [TranslocoPipe, ButtonDirective, Message, Skeleton, TimeEntryDialog, TimeTypeIcon],
  templateUrl: './time.html',
  styleUrl: './time.scss',
})
export class TechnicianTime {
  private readonly visits = inject(TechnicianVisitsService);
  private readonly sessionService = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private inFlight: Subscription | null = null;
  private pending: TimeRequest | null = null;
  private lastTrigger: HTMLElement | null = null;

  get loadErrorMessage(): string {
    return TIME_LOAD_ERROR_MESSAGE();
  }
  get notice(): string {
    return TIME_NOTICE();
  }
  get readOnlyMessage(): string {
    return TIME_READ_ONLY_MESSAGE();
  }
  readonly skeletonRows = [0, 1, 2];
  readonly legend = [
    { type: 'work', label: TYPE_LABELS.work },
    { type: 'travel', label: TYPE_LABELS.travel },
    { type: 'break', label: TYPE_LABELS.break },
  ] as const;

  readonly state = signal<PageState>('loading');
  readonly data = signal<TimeSummaryResponse | null>(null);
  /** Date the shown range was requested with (selected date). */
  readonly selected = signal('');
  readonly changing = signal(false);
  readonly navigationFailed = signal(false);
  readonly openEntry = signal<TimeEntry | null>(null);

  readonly accessText = computed(() => {
    const state = this.state();
    return state === 'loading' || state === 'ready' || state === 'error'
      ? null
      : accessMessage(state);
  });
  readonly view = computed(() => this.data()?.view ?? 'week');
  readonly timezone = computed(() => this.data()?.timezone ?? 'UTC');
  readonly label = computed(() => {
    const data = this.data();
    return data === null ? '' : rangeLabel(data.view, data.from, data.to, data.today);
  });
  readonly unit = computed(() => (this.view() === 'day' ? 'day' : 'week'));
  readonly emptyMessage = computed(() =>
    this.view() === 'day'
      ? t('technicianJobs.time.noTimeRecordedFor')
      : t('technicianJobs.time.noTimeRecordedFor2'),
  );
  readonly totals = computed(() => {
    const totals = this.data()?.totals;
    return {
      total: formatSeconds(totals?.totalSeconds ?? 0),
      tiles: [
        { type: 'work', label: t('technicianJobs.time.work'), seconds: totals?.workSeconds ?? 0 },
        {
          type: 'travel',
          label: t('technicianJobs.time.travel'),
          seconds: totals?.travelSeconds ?? 0,
        },
        {
          type: 'break',
          label: t('technicianJobs.time.break'),
          seconds: totals?.breakSeconds ?? 0,
        },
      ].map((tile) => ({
        ...tile,
        type: tile.type as TimeEntry['type'],
        value: formatSeconds(tile.seconds),
      })),
    };
  });
  readonly bars = computed(() => {
    const data = this.data();
    if (data === null || data.view !== 'week') {
      return [];
    }
    const max = Math.max(0, ...data.days.map((day) => day.totalSeconds));
    return data.days.map((day) => ({
      day,
      name: weekdayName(day.date),
      total: formatSeconds(day.totalSeconds),
      height: max === 0 ? 0 : (day.totalSeconds / max) * 100,
      alternative: dayAlternative(day),
    }));
  });
  readonly sections = computed(() => {
    const data = this.data();
    if (data === null) {
      return [];
    }
    const zone = data.timezone;
    const groups = new Map<string, TimeEntry[]>();
    for (const entry of data.entries) {
      const date = localDateOf(entry.startedAt, zone);
      groups.set(date, [...(groups.get(date) ?? []), entry]);
    }
    return [...groups.keys()]
      .sort()
      .reverse()
      .map((date) => ({
        date,
        heading: sectionHeading(date, data.today),
        total: formatSeconds(data.days.find((day) => day.date === date)?.totalSeconds ?? 0),
        rows: (groups.get(date) ?? []).map((entry) => this.row(entry, zone)),
      }));
  });
  readonly detail = computed(() => {
    const entry = this.openEntry();
    return entry === null ? null : this.row(entry, this.timezone());
  });

  constructor() {
    const timer = setInterval(() => this.refreshOpen(), REFRESH_MS);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      this.inFlight?.unsubscribe();
    });
    if (this.sessionService.session()?.role.code !== 'technician') {
      this.state.set('forbidden');
      return;
    }
    this.request({ view: 'week', date: null });
  }

  /** Retry of the full-page error or of the failed navigation. */
  retry(): void {
    this.request(this.pending ?? { view: 'week', date: null });
  }

  /** BR-06: Day keeps today when the week contains it, else the Monday; Week shows the week of the selected date. */
  setView(view: TimeView): void {
    const data = this.data();
    const base = this.base();
    if (data === null || base === null || base.view === view) {
      return;
    }
    if (view === 'week') {
      this.request({ view, date: base.date });
      return;
    }
    const from = addDays(base.date, -weekdayIndex(base.date));
    const containsToday = data.today >= from && data.today <= addDays(from, 6);
    this.request({ view, date: containsToday ? data.today : from });
  }

  move(direction: -1 | 1): void {
    const base = this.base();
    if (base !== null) {
      const days = direction * (base.view === 'day' ? 1 : 7);
      this.request({ view: base.view, date: addDays(base.date, days) });
    }
  }

  show(entry: TimeEntry, event: Event): void {
    this.lastTrigger = event.currentTarget as HTMLElement;
    this.openEntry.set(entry);
  }

  /** Closes the panel and returns focus to the row that opened it. */
  closeDetail(): void {
    const id = this.openEntry()?.id;
    if (id === undefined) {
      return;
    }
    this.openEntry.set(null);
    const trigger =
      this.lastTrigger?.isConnected === true
        ? this.lastTrigger
        : this.host.nativeElement.querySelector<HTMLElement>(`[data-entry-id="${id}"]`);
    trigger?.focus();
  }

  /** Target of the next navigation: the in-flight request, else the shown range. */
  private base(): { view: TimeView; date: string } | null {
    if (this.pending?.date) {
      return { view: this.pending.view, date: this.pending.date };
    }
    const data = this.data();
    return data === null ? null : { view: data.view, date: this.selected() };
  }

  private row(entry: TimeEntry, zone: string) {
    const start = formatTime(entry.startedAt, zone);
    const end = entry.endedAt === null ? null : formatTime(entry.endedAt, zone);
    const isBreak = entry.type === 'break';
    return {
      entry,
      typeLabel: TYPE_LABELS[entry.type],
      title: isBreak ? t('technicianJobs.time.break') : entry.visit.title,
      secondary: isBreak ? entry.visit.title : null,
      number: `#${entry.visit.displayNumber}`,
      range: `${start} – ${end ?? 'now'}`,
      startText: start,
      endText: end ?? t('technicianJobs.time.inProgress'),
      date: detailDate(localDateOf(entry.startedAt, zone)),
      visitText: t('technicianJobs.time.visit', { visitNumber: entry.visit.visitNumber }),
      duration: formatSeconds(entry.durationSeconds),
    };
  }

  /** Open entries grow with the clock: reload the shown range quietly at least every minute while visible. */
  private refreshOpen(): void {
    const data = this.data();
    if (
      document.hidden ||
      this.inFlight !== null ||
      data === null ||
      !data.entries.some((entry) => entry.isOpen)
    ) {
      return;
    }
    this.request({ view: data.view, date: this.selected() }, true);
  }

  /** One request at a time: a new range replaces the in-flight one, and its response is ignored. */
  private request(range: TimeRequest, quiet = false): void {
    this.inFlight?.unsubscribe();
    this.pending = quiet ? null : range;
    const hasData = this.data() !== null;
    if (!quiet) {
      if (hasData) {
        this.changing.set(true);
        this.navigationFailed.set(false);
      } else {
        this.state.set('loading');
      }
    }

    const subscription = this.visits.time(range.view, range.date).subscribe({
      next: (response) => {
        this.inFlight = null;
        this.pending = null;
        this.data.set(response);
        this.selected.set(range.date ?? response.today);
        this.changing.set(false);
        this.navigationFailed.set(false);
        this.state.set('ready');
      },
      error: (error: unknown) => {
        this.inFlight = null;
        if (quiet) {
          return;
        }
        this.changing.set(false);
        if (isApiError(error) && error.status === 400 && range.date !== null) {
          // Invalid view/date: fall back to the current week.
          this.request({ view: 'week', date: null });
          return;
        }
        const failure = classifyFailure(error);
        if (hasData && (failure === 'failed' || failure === 'not-found')) {
          this.navigationFailed.set(true);
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
