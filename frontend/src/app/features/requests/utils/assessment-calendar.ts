import { addDays } from '../../service-request/service-request.validators';
import { AssessmentCalendar, CalendarDay, TimeRange } from '../models/requests.model';
import { clock, minutesOf, rangeText } from './assessment-schedule';
import { toLocalInput } from './requests-format';

export type CalendarView = 'week' | 'day' | 'agenda';

/** The proposed block (BR-07): form date, window start and duration. */
export interface ProposedSlot {
  readonly date: string;
  readonly startMinutes: number;
  readonly durationMinutes: number;
}

export interface CalendarEventView {
  readonly key: string;
  readonly label: string;
  readonly range: string;
  readonly kind: 'assessment' | 'visit' | 'proposed';
  readonly isCurrent: boolean;
  /** Percent of the grid height. */
  readonly top: number;
  readonly height: number;
  readonly lane: number;
  readonly lanes: number;
}

export interface CalendarShade {
  readonly kind: 'unavailable' | 'time-off';
  readonly top: number;
  readonly height: number;
}

export interface CalendarColumn {
  readonly date: string;
  readonly weekday: string;
  readonly dayLabel: string;
  readonly selected: boolean;
  readonly shades: readonly CalendarShade[];
  readonly events: readonly CalendarEventView[];
}

export interface AgendaDay {
  readonly date: string;
  readonly heading: string;
  readonly events: readonly { readonly range: string; readonly label: string }[];
}

export interface CalendarViewModel {
  readonly columns: readonly CalendarColumn[];
  readonly hours: readonly { readonly label: string; readonly top: number }[];
  /** Number of hour rows of the grid. */
  readonly hourCount: number;
  readonly agenda: readonly AgendaDay[];
  /** Screen-reader list of every commitment with its range and label. */
  readonly listing: readonly string[];
}

type Interval = readonly [number, number];

const DEFAULT_START_HOUR = 8;
const DEFAULT_END_HOUR = 18;
const MINUTES_PER_DAY = 24 * 60;

function parts(date: string): [number, number, number] {
  const [year, month, day] = date.split('-').map(Number);
  return [year, month, day];
}

function utc(date: string): Date {
  const [year, month, day] = parts(date);
  return new Date(Date.UTC(year, month - 1, day, 12));
}

/** Monday of the week containing `date` (BR-07, BR-06). */
export function weekStart(date: string): string {
  return addDays(date, -((utc(date).getUTCDay() + 6) % 7));
}

/** One week (or one day in Day view) back or forward. */
export function shiftDate(view: CalendarView, date: string, direction: -1 | 1): string {
  return addDays(date, (view === 'day' ? 1 : 7) * direction);
}

function format(date: string, options: Intl.DateTimeFormatOptions): string {
  return new Intl.DateTimeFormat('en-US', { ...options, timeZone: 'UTC' }).format(utc(date));
}

/** Toolbar label: "Sep 21 – 25, 2026", "Tue, Sep 22, 2026". */
export function rangeLabel(view: CalendarView, date: string, dates?: readonly string[]): string {
  if (view === 'day') {
    return format(date, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
  }
  const first = dates?.[0] ?? weekStart(date);
  const last = dates?.[dates.length - 1] ?? addDays(first, view === 'agenda' ? 6 : 4);
  const sameYear = first.slice(0, 4) === last.slice(0, 4);
  const sameMonth = sameYear && first.slice(5, 7) === last.slice(5, 7);
  const month = (value: string): string => format(value, { month: 'short' });
  const day = (value: string): string => String(Number(value.slice(8, 10)));
  const start = `${month(first)} ${day(first)}${sameYear ? '' : `, ${first.slice(0, 4)}`}`;
  const end = `${sameMonth ? '' : `${month(last)} `}${day(last)}, ${last.slice(0, 4)}`;
  return `${start} – ${end}`;
}

/** Local day and minutes of an instant; an `HH:mm` value carries no day. */
function localPoint(value: string, timeZone: string): { date: string | null; minutes: number } {
  if (/^\d{1,2}:\d{2}/.test(value)) {
    return { date: null, minutes: minutesOf(value) };
  }
  const local = toLocalInput(value, timeZone);
  return local === ''
    ? { date: null, minutes: 0 }
    : { date: local.slice(0, 10), minutes: minutesOf(local.slice(11)) };
}

/** A range as minutes of `date`, clamped to that day; `null` when it does not touch it. */
function onDay(range: TimeRange, date: string, timeZone: string): Interval | null {
  const start = localPoint(range.start, timeZone);
  const end = localPoint(range.end, timeZone);
  const from =
    start.date === null || start.date === date ? start.minutes : start.date < date ? 0 : null;
  const to =
    end.date === null || end.date === date ? end.minutes : end.date > date ? MINUTES_PER_DAY : null;
  return from === null || to === null || to <= from ? null : [from, to];
}

function intervals(ranges: readonly TimeRange[], date: string, timeZone: string): Interval[] {
  return ranges.flatMap((range): Interval[] => {
    const interval = onDay(range, date, timeZone);
    return interval === null ? [] : [interval];
  });
}

function subtract(base: readonly Interval[], cut: readonly Interval[]): Interval[] {
  let result: Interval[] = [...base];
  for (const [cutFrom, cutTo] of cut) {
    result = result.flatMap(([from, to]): Interval[] => {
      if (cutTo <= from || cutFrom >= to) {
        return [[from, to]];
      }
      const pieces: Interval[] = [];
      if (cutFrom > from) {
        pieces.push([from, cutFrom]);
      }
      if (cutTo < to) {
        pieces.push([cutTo, to]);
      }
      return pieces;
    });
  }
  return result;
}

interface RawEvent {
  readonly key: string;
  readonly label: string;
  readonly kind: 'assessment' | 'visit' | 'proposed';
  readonly isCurrent: boolean;
  readonly from: number;
  readonly to: number;
}

/** Greedy lane assignment so overlapping events sit side by side. */
function layout(
  events: readonly RawEvent[],
  startMinutes: number,
  total: number,
): CalendarEventView[] {
  const sorted = [...events].sort((a, b) => a.from - b.from || a.to - b.to);
  const placed: { event: RawEvent; lane: number; cluster: number }[] = [];
  const clusterLanes: number[] = [];
  let clusterEnd = -1;
  let cluster = -1;
  let laneEnds: number[] = [];
  for (const event of sorted) {
    if (event.from >= clusterEnd) {
      cluster++;
      clusterEnd = event.to;
      laneEnds = [];
    }
    let lane = laneEnds.findIndex((end) => end <= event.from);
    if (lane === -1) {
      lane = laneEnds.length;
      laneEnds.push(event.to);
    } else {
      laneEnds[lane] = event.to;
    }
    clusterEnd = Math.max(clusterEnd, event.to);
    clusterLanes[cluster] = Math.max(clusterLanes[cluster] ?? 0, lane + 1);
    placed.push({ event, lane, cluster });
  }
  return placed.map(({ event, lane, cluster: index }) => ({
    key: event.key,
    label: event.label,
    range: rangeText(event.from, event.to),
    kind: event.kind,
    isCurrent: event.isCurrent,
    top: ((event.from - startMinutes) / total) * 100,
    height: ((event.to - event.from) / total) * 100,
    lane,
    lanes: clusterLanes[index],
  }));
}

export interface CalendarViewInput {
  readonly calendar: AssessmentCalendar;
  readonly view: CalendarView;
  readonly viewDate: string;
  readonly proposed: ProposedSlot | null;
  readonly timeZone: string;
}

/**
 * Pure view model of the read-only calendar (BR-07): visible columns, hour range, shading,
 * positioned commitments with the current assessment and the proposed block, the agenda and the
 * screen-reader listing.
 */
export function buildCalendarView(input: CalendarViewInput): CalendarViewModel {
  const { calendar, view, viewDate, proposed, timeZone } = input;
  const monday = weekStart(viewDate);
  const week = Array.from({ length: 7 }, (_, index) => addDays(monday, index));
  const days = new Map<string, CalendarDay>(calendar.days.map((day) => [day.date, day]));

  const commitments = calendar.events.flatMap((event) => {
    const start = localPoint(event.start, timeZone);
    const date = start.date;
    const range = date === null ? null : onDay(event, date, timeZone);
    return date === null || range === null ? [] : [{ event, date, from: range[0], to: range[1] }];
  });

  const hasContent = (date: string): boolean =>
    (days.get(date)?.availability.length ?? 0) > 0 || commitments.some((c) => c.date === date);
  const dates =
    view === 'day'
      ? [viewDate]
      : week.filter((date, index) => index < 5 || hasContent(date) || date === proposed?.date);

  // Hour range: earliest and latest availability or commitment of the visible columns.
  const points: number[] = [];
  for (const date of dates) {
    for (const range of days.get(date)?.availability ?? []) {
      const interval = onDay(range, date, timeZone);
      if (interval !== null) {
        points.push(interval[0], interval[1]);
      }
    }
    for (const commitment of commitments.filter((c) => c.date === date)) {
      points.push(commitment.from, commitment.to);
    }
    if (proposed?.date === date) {
      points.push(proposed.startMinutes, proposed.startMinutes + proposed.durationMinutes);
    }
  }
  const startHour = points.length === 0 ? DEFAULT_START_HOUR : Math.floor(Math.min(...points) / 60);
  const endHour =
    points.length === 0
      ? DEFAULT_END_HOUR
      : Math.min(24, Math.max(startHour + 1, Math.ceil(Math.max(...points) / 60)));
  const startMinutes = startHour * 60;
  const total = (endHour - startHour) * 60;
  const percent = (minutes: number): number => ((minutes - startMinutes) / total) * 100;

  const columns = dates.map((date): CalendarColumn => {
    const day = days.get(date);
    const free = subtract(
      intervals(day?.availability ?? [], date, timeZone),
      intervals(day?.breaks ?? [], date, timeZone),
    ).sort((a, b) => a[0] - b[0]);
    const unavailable = subtract([[startMinutes, endHour * 60]], free);
    const timeOff = intervals(day?.timeOff ?? [], date, timeZone);
    const shades: CalendarShade[] = [
      ...unavailable.map(([from, to]) => ({ kind: 'unavailable' as const, from, to })),
      ...timeOff.map(([from, to]) => ({ kind: 'time-off' as const, from, to })),
    ].map(({ kind, from, to }) => {
      const top = Math.max(0, percent(from));
      return { kind, top, height: Math.min(100, percent(to)) - top };
    });

    const raw: RawEvent[] = commitments
      .filter((c) => c.date === date)
      .map((c, index) => ({
        key: `${date}-${index}-${c.event.start}`,
        label: c.event.isCurrent ? 'Current assessment' : c.event.label,
        kind: c.event.kind,
        isCurrent: c.event.isCurrent,
        from: c.from,
        to: c.to,
      }));
    if (proposed?.date === date) {
      raw.push({
        key: `${date}-proposed`,
        label: 'Proposed assessment',
        kind: 'proposed',
        isCurrent: false,
        from: proposed.startMinutes,
        to: Math.min(MINUTES_PER_DAY, proposed.startMinutes + proposed.durationMinutes),
      });
    }
    return {
      date,
      weekday: format(date, { weekday: 'short' }),
      dayLabel: format(date, { month: 'short', day: 'numeric' }),
      selected: proposed?.date === date,
      shades: shades.filter((shade) => shade.height > 0),
      events: layout(raw, startMinutes, total),
    };
  });

  const ordered = [...commitments].sort((a, b) => a.date.localeCompare(b.date) || a.from - b.from);
  const agenda = week.flatMap((date): AgendaDay[] => {
    const events = ordered
      .filter((c) => c.date === date)
      .map((c) => ({
        range: rangeText(c.from, c.to),
        label: c.event.isCurrent ? 'Current assessment' : c.event.label,
      }));
    return events.length === 0
      ? []
      : [
          {
            date,
            heading: format(date, { weekday: 'short', month: 'short', day: 'numeric' }),
            events,
          },
        ];
  });
  const listing = ordered
    .filter((c) => dates.includes(c.date))
    .map(
      (c) =>
        `${format(c.date, { weekday: 'long', month: 'long', day: 'numeric' })}, ${clock(c.from)} to ${clock(c.to)}: ${c.event.isCurrent ? 'Current assessment' : c.event.label}`,
    );

  return {
    columns,
    hours: Array.from({ length: endHour - startHour }, (_, index) => ({
      label: clock((startHour + index) * 60).replace(':00', ''),
      top: (index / (endHour - startHour)) * 100,
    })),
    hourCount: endHour - startHour,
    agenda,
    listing,
  };
}
