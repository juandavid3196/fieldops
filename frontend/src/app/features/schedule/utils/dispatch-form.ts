import { ArrivalWindowCode, DispatchPriority } from '../models/schedule.model';
import { clockMinutes, formatMinutes } from './schedule-range';

export interface Choice<T> {
  readonly code: T;
  readonly label: string;
}

export const PRIORITY_TEXT: Readonly<Record<DispatchPriority, string>> = {
  urgent: 'Urgent',
  high: 'High',
  normal: 'Normal',
  low: 'Low',
};

export const PRIORITY_ICONS: Readonly<Record<DispatchPriority, string>> = {
  urgent: 'pi-exclamation-circle',
  high: 'pi-arrow-up',
  normal: 'pi-minus',
  low: 'pi-arrow-down',
};

const STEP_MINUTES = 15;

export function toClock(total: number): string {
  const hours = Math.floor(total / 60);
  const minutes = total % 60;
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`;
}

/** BR-11: 15-minute steps; a stored off-step value stays selectable. */
export function timeChoices(current: string | null): Choice<string>[] {
  const choices: Choice<string>[] = [];
  for (let minutes = 0; minutes < 24 * 60; minutes += STEP_MINUTES) {
    choices.push({ code: toClock(minutes), label: formatMinutes(minutes) });
  }
  if (current && !choices.some((choice) => choice.code === current)) {
    choices.push({ code: current, label: formatMinutes(clockMinutes(current)) });
    choices.sort((a, b) => a.code.localeCompare(b.code));
  }
  return choices;
}

/** BR-11: arrival window presets relative to the start time. */
export function arrivalChoices(start: string | null): Choice<ArrivalWindowCode>[] {
  if (!start) {
    return [
      { code: 'at_start', label: 'At start time' },
      { code: 'start_plus_1h', label: 'Start time to 1 hour later' },
      { code: 'start_plus_2h', label: 'Start time to 2 hours later' },
      { code: 'around_1h', label: '1 hour before to 1 hour after' },
    ];
  }
  const from = clockMinutes(start);
  const text = (a: number, b: number): string => `${formatMinutes(a)} – ${formatMinutes(b)}`;
  return [
    { code: 'at_start', label: formatMinutes(from) },
    { code: 'start_plus_1h', label: text(from, from + 60) },
    { code: 'start_plus_2h', label: text(from, from + 120) },
    { code: 'around_1h', label: text(Math.max(0, from - 60), from + 60) },
  ];
}

export function hoursText(minutes: number): string {
  return `${Math.round((minutes / 60) * 10) / 10}h`;
}

/** BR-09: "<jobs> jobs · <x>h / <y>h after assignment". */
export function impactText(impact: {
  readonly jobs: number;
  readonly scheduledMinutes: number;
  readonly availableMinutes: number;
}): string {
  return `${impact.jobs} jobs · ${hoursText(impact.scheduledMinutes)} / ${hoursText(impact.availableMinutes)} after assignment`;
}

export function previousText(value: string | null): string {
  if (value === null) {
    return 'No previous job';
  }
  return value.startsWith('Previous job') || value.startsWith('No previous')
    ? value
    : `Previous job: ${value}`;
}

export function nextText(value: string | null): string {
  if (value === null) {
    return 'No next job';
  }
  return value.startsWith('Next job') || value.startsWith('No next') ? value : `Next job: ${value}`;
}

/** Validation messages (BR-11) for the client-side checks that mirror the server. */
export const MESSAGES = {
  date: 'Select a date.',
  start: 'Select a start time.',
  end: 'Select an end time.',
  endOrder: 'End time must be after start time.',
  primary: 'Choose a primary technician.',
  technicians: 'Select up to 5 technicians.',
  note: 'Note must be 1000 characters or fewer.',
  reasonShort: 'Enter at least 10 characters.',
  reasonLong: 'Reason must be 500 characters or fewer.',
  reasonRequired: 'Enter a reason to confirm despite the conflicts.',
} as const;

export const MAX_TECHNICIANS = 5;
export const MAX_NOTE = 1000;
export const MIN_REASON = 10;
export const MAX_REASON = 500;
