import { addDays, todayInTimeZone } from '../../service-request/service-request.validators';
import { PlannerTechnician, RequestDetail, TimeWindow } from '../models/requests.model';
import { toLocalInput } from './requests-format';

/** Editable values of the Assessment visit form (BR-08 to BR-12, BR-14, BR-20). */
export interface AssessmentFormValue {
  readonly branchId: string;
  /** Local date, `YYYY-MM-DD`. */
  readonly date: string;
  /** Arrival window start, `HH:mm` (the window is always one hour long, BR-11). */
  readonly window: string;
  readonly duration: number;
  readonly technicianId: string;
  readonly purpose: string;
  readonly internalInstructions: string;
  readonly notify: boolean;
}

export interface Option<T> {
  readonly code: T;
  readonly label: string;
}

export type AssessmentMode = 'schedule' | 'reschedule';

const MINUTES_PER_DAY = 24 * 60;
export const DEFAULT_WINDOW = '09:00';
export const DEFAULT_DURATION = 60;
export const DURATIONS: readonly number[] = [30, 60, 90, 120, 180, 240, 480];

/** First window inside each preferred time window (`requests-pipeline` labels). */
const PREFERRED_WINDOW_START: Readonly<Record<TimeWindow, string>> = {
  morning: '08:00',
  afternoon: '12:00',
  evening: '17:00',
  any: '08:00',
};

export function minutesOf(time: string): number {
  const [hours, minutes] = time.split(':').map(Number);
  return hours * 60 + (minutes || 0);
}

/** "h:mm a" of minutes since midnight. */
export function clock(minutes: number): string {
  const total = ((Math.round(minutes) % MINUTES_PER_DAY) + MINUTES_PER_DAY) % MINUTES_PER_DAY;
  const hours = Math.floor(total / 60);
  const hour12 = hours % 12 === 0 ? 12 : hours % 12;
  return `${hour12}:${String(total % 60).padStart(2, '0')} ${hours < 12 ? 'AM' : 'PM'}`;
}

/** BR-08: 1-hour windows starting on the hour, 6:00 AM to 7:00 PM. */
export const WINDOW_OPTIONS: Option<string>[] = Array.from({ length: 14 }, (_, index) => {
  const hour = 6 + index;
  return {
    code: `${String(hour).padStart(2, '0')}:00`,
    label: `${clock(hour * 60)} – ${clock((hour + 1) * 60)}`,
  };
});

/** BR-11 labels: "<n> minutes" below 120, "<h> hours" from 120. */
export const DURATION_OPTIONS: Option<number>[] = DURATIONS.map((code) => ({
  code,
  label: code < 120 ? `${code} minutes` : `${code / 60} hours`,
}));

/** `Tue, Sep 22` of a local `YYYY-MM-DD` date (BR-13). */
export function previewDate(date: string): string {
  return new Intl.DateTimeFormat('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${date}T12:00:00Z`));
}

export function isLocalDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && addDays(value, 0) === value;
}

export function hasRecipientEmail(detail: RequestDetail): boolean {
  return (detail.contact.email ?? '').trim().length > 0;
}

/** BR-19 defaults for schedule mode; the notify flag follows the recipient email (BR-14). */
export function defaultFormValue(
  detail: RequestDetail,
  timeZone: string,
  now: Date = new Date(),
): AssessmentFormValue {
  const today = todayInTimeZone(timeZone, now);
  const availability = detail.availability;
  const dated = availability?.dateMode === 'date';
  const preferred = dated ? availability.preferredDate : null;
  const window =
    dated && availability.timeWindow !== null
      ? PREFERRED_WINDOW_START[availability.timeWindow]
      : DEFAULT_WINDOW;
  return {
    branchId: detail.branch?.id ?? '',
    date: preferred !== null && preferred > today ? preferred : addDays(today, 1),
    window,
    duration: DEFAULT_DURATION,
    technicianId: '',
    purpose: '',
    internalInstructions: '',
    notify: hasRecipientEmail(detail),
  };
}

/** Reschedule mode: the form starts as the current assessment (BR-01). */
export function prefilledFormValue(
  detail: RequestDetail,
  timeZone: string,
  now: Date = new Date(),
): AssessmentFormValue {
  const base = defaultFormValue(detail, timeZone, now);
  const assessment = detail.assessment;
  const start = assessment === null ? '' : toLocalInput(assessment.start, timeZone);
  if (assessment === null || start === '') {
    return base;
  }
  const minutes = Math.round(
    (new Date(assessment.end).getTime() - new Date(assessment.start).getTime()) / 60_000,
  );
  return {
    ...base,
    date: start.slice(0, 10),
    window: `${start.slice(11, 13)}:00`,
    duration: DURATIONS.includes(minutes) ? minutes : DEFAULT_DURATION,
    technicianId: assessment.technician?.id ?? '',
    purpose: assessment.purpose ?? '',
    internalInstructions: assessment.internalInstructions ?? '',
  };
}

/** Local `YYYY-MM-DDTHH:mm` start (BR-08). */
export function slotStart(value: AssessmentFormValue): string {
  return `${value.date}T${value.window}`;
}

/** Local `YYYY-MM-DDTHH:mm` end = start + duration (BR-11). */
export function slotEnd(value: AssessmentFormValue): string {
  const total = minutesOf(value.window) + value.duration;
  const date = total >= MINUTES_PER_DAY ? addDays(value.date, 1) : value.date;
  const minutes = total % MINUTES_PER_DAY;
  const hh = String(Math.floor(minutes / 60)).padStart(2, '0');
  const mm = String(minutes % 60).padStart(2, '0');
  return `${date}T${hh}:${mm}`;
}

/** BR-13 customer message; `null` until date, window and technician are set. */
export function previewMessage(
  value: AssessmentFormValue,
  technicianName: string | null,
  mode: AssessmentMode,
): string | null {
  if (!isLocalDate(value.date) || value.window === '' || technicianName === null) {
    return null;
  }
  const date = previewDate(value.date);
  const from = clock(minutesOf(value.window));
  const to = clock(minutesOf(value.window) + 60);
  const lead =
    mode === 'reschedule'
      ? `Your assessment visit has been rescheduled to ${date}`
      : `Your assessment visit is scheduled for ${date}`;
  return `${lead} between ${from} and ${to}. ${technicianName} will inspect the issue before we prepare your quote.`;
}

/** Wall-clock minutes of an instant (or an `HH:mm` string) in the organization time zone. */
export function localMinutes(value: string, timeZone: string): number {
  if (/^\d{1,2}:\d{2}/.test(value)) {
    return minutesOf(value);
  }
  const local = toLocalInput(value, timeZone);
  return local === '' ? 0 : minutesOf(local.slice(11));
}

/** BR-05 chip text; never conveyed by color alone. */
export function slotChipText(technician: PlannerTechnician, timeZone: string): string {
  const slot = technician.slot;
  switch (slot.state) {
    case 'available':
      return 'Available';
    case 'available_after':
      return slot.availableAfter === null
        ? 'Available later'
        : `Available after ${clock(localMinutes(slot.availableAfter, timeZone))}`;
    case 'outside_availability':
      return 'Outside availability';
    case 'time_off':
      return 'Time off';
    case 'conflict': {
      if (slot.from === null || slot.to === null) {
        return 'Conflict';
      }
      return `Conflict ${rangeText(localMinutes(slot.from, timeZone), localMinutes(slot.to, timeZone))}`;
    }
  }
}

/** "h:mm – h:mm a"; the first meridiem is kept only when it differs from the second. */
export function rangeText(from: number, to: number): string {
  const start = clock(from);
  const sameHalf = from < 720 === to < 720;
  return `${sameHalf ? start.replace(/ [AP]M$/, '') : start} – ${clock(to)}`;
}

/** BR-06 text. */
export function workloadText(technician: PlannerTechnician): string {
  const workload = technician.workload;
  if (workload.state === 'percent' && workload.percent !== null) {
    return `${workload.percent}%`;
  }
  return workload.state === 'no_availability' ? 'No availability' : '—';
}
