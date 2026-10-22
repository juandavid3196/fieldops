import { ArrivalWindowCode, DispatchPriority } from '../models/schedule.model';
import { clockMinutes, formatMinutes } from './schedule-range';

import { localized, t } from '../../../core/i18n/translate';
export interface Choice<T> {
  readonly code: T;
  readonly label: string;
}

export const PRIORITY_TEXT: Readonly<Record<DispatchPriority, string>> = localized(() => ({
  urgent: t('schedule.dispatchForm.urgent'),
  high: t('schedule.dispatchForm.high'),
  normal: t('schedule.dispatchForm.normal'),
  low: t('schedule.dispatchForm.low'),
}));

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
      { code: 'at_start', label: t('schedule.dispatchForm.atStartTime') },
      { code: 'start_plus_1h', label: t('schedule.dispatchForm.startTimeTo1') },
      { code: 'start_plus_2h', label: t('schedule.dispatchForm.startTimeTo2') },
      { code: 'around_1h', label: t('schedule.dispatchForm.n1HourBeforeTo') },
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
  return t('schedule.dispatchForm.jobsAfterAssignment', {
    jobs: impact.jobs,
    scheduledMinutes: hoursText(impact.scheduledMinutes),
    availableMinutes: hoursText(impact.availableMinutes),
  });
}

export function previousText(value: string | null): string {
  if (value === null) {
    return t('schedule.dispatchForm.noPreviousJob');
  }
  return value.startsWith(t('schedule.dispatchForm.previousJob')) ||
    value.startsWith(t('schedule.dispatchForm.noPrevious'))
    ? value
    : t('schedule.dispatchForm.previousJob2', { value: value });
}

export function nextText(value: string | null): string {
  if (value === null) {
    return t('schedule.dispatchForm.noNextJob');
  }
  return value.startsWith(t('schedule.dispatchForm.nextJob')) ||
    value.startsWith(t('schedule.dispatchForm.noNext'))
    ? value
    : t('schedule.dispatchForm.nextJob2', { value: value });
}

/** Validation messages (BR-11) for the client-side checks that mirror the server. */
export const MESSAGES = localized(
  () =>
    ({
      date: t('schedule.dispatchForm.selectADate'),
      start: t('schedule.dispatchForm.selectAStartTime'),
      end: t('schedule.dispatchForm.selectAnEndTime'),
      endOrder: t('schedule.dispatchForm.endTimeMustBe'),
      primary: t('schedule.dispatchForm.chooseAPrimaryTechnician'),
      technicians: t('schedule.dispatchForm.selectUpTo5'),
      note: t('schedule.dispatchForm.noteMustBe1000'),
      reasonShort: t('schedule.dispatchForm.enterAtLeast10'),
      reasonLong: t('schedule.dispatchForm.reasonMustBe500'),
      reasonRequired: t('schedule.dispatchForm.enterAReasonTo'),
    }) as const,
);

export const MAX_TECHNICIANS = 5;
export const MAX_NOTE = 1000;
export const MIN_REASON = 10;
export const MAX_REASON = 500;
