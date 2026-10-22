import {
  ArrivalWindow,
  JobType,
  MaterialSource,
  Priority,
  RecurrenceFrequency,
  WorkOrderStatus,
} from '../models/work-order.model';

import { localized, localizedList, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
export const PRIORITY_LABELS: Readonly<Record<Priority, string>> = localized(() => ({
  urgent: t('jobs.workOrderFormat.urgent'),
  high: t('jobs.workOrderFormat.high'),
  normal: t('jobs.workOrderFormat.normal'),
  low: t('jobs.workOrderFormat.low'),
}));

export const STATUS_LABELS: Readonly<Record<WorkOrderStatus, string>> = localized(() => ({
  draft: t('jobs.workOrderFormat.draft'),
  ready_to_schedule: t('jobs.workOrderFormat.unscheduled'),
  scheduled: t('jobs.workOrderFormat.scheduled'),
}));

export const JOB_TYPE_LABELS: Readonly<Record<JobType, string>> = localized(() => ({
  one_time: t('jobs.workOrderFormat.oneTimeJob'),
  recurring: t('jobs.workOrderFormat.recurringJob'),
}));

export const SOURCE_LABELS: Readonly<Record<MaterialSource, string>> = localized(() => ({
  truck_stock: t('jobs.workOrderFormat.truckStock'),
  warehouse: t('jobs.workOrderFormat.warehouse'),
  to_purchase: t('jobs.workOrderFormat.toPurchase'),
}));

export const WINDOW_LABELS: Readonly<Record<ArrivalWindow, string>> = localized(() => ({
  any: t('jobs.workOrderFormat.anyTime'),
  '08-11': t('jobs.workOrderFormat.n800Am11'),
  '09-12': t('jobs.workOrderFormat.n900Am12'),
  '12-15': t('jobs.workOrderFormat.n1200Pm3'),
  '13-16': t('jobs.workOrderFormat.n100Pm4'),
  '15-18': t('jobs.workOrderFormat.n300Pm6'),
}));

export const FREQUENCY_LABELS: Readonly<Record<RecurrenceFrequency, string>> = localized(() => ({
  weekly: t('jobs.workOrderFormat.weekly'),
  biweekly: t('jobs.workOrderFormat.every2Weeks'),
  monthly: t('jobs.workOrderFormat.monthly'),
  quarterly: t('jobs.workOrderFormat.quarterly'),
}));

/** BR-08: 30 to 720 minutes in steps of 30, shown "30 min", "1h", "1.5h" … "12h". */
export function durationLabel(minutes: number | null): string {
  if (minutes === null) {
    return '—';
  }
  return minutes < 60 ? t('jobs.workOrderFormat.min', { minutes: minutes }) : `${minutes / 60}h`;
}

export const DURATION_OPTIONS: { readonly code: number | null; readonly label: string }[] =
  localizedList(() => [
    { code: null, label: t('jobs.workOrderFormat.notSet') },
    ...Array.from({ length: 24 }, (_, index) => {
      const minutes = (index + 1) * 30;
      return { code: minutes, label: durationLabel(minutes) };
    }),
  ]);

/** Select options of a label record, in declaration order. */
export function optionsOf<T extends string>(
  labels: Readonly<Record<T, string>>,
): { readonly code: T; readonly label: string }[] {
  return (Object.keys(labels) as T[]).map((code) => ({ code, label: labels[code] }));
}

/** "MMM d, yyyy" of a calendar date (`YYYY-MM-DD`), never shifted by a time zone. */
export function plainDate(value: string): string {
  return new Intl.DateTimeFormat(activeIntlLocale(), {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${value}T12:00:00Z`));
}

/** BR-16: visit status labels shown in the job detail ("Visit #<k> · <label>"). */
export const VISIT_STATUS_LABELS: Readonly<Record<string, string>> = localized(() => ({
  unscheduled: t('jobs.workOrderFormat.unscheduled'),
  scheduled: t('jobs.workOrderFormat.scheduled'),
  assigned: t('jobs.workOrderFormat.assigned'),
  on_the_way: t('jobs.workOrderFormat.onTheWay'),
  in_progress: t('jobs.workOrderFormat.inProgress'),
  paused: t('jobs.workOrderFormat.paused'),
  completed: t('jobs.workOrderFormat.completed'),
  needs_correction: t('jobs.workOrderFormat.needsCorrection'),
  approved: t('jobs.workOrderFormat.approved'),
  cancelled: t('jobs.workOrderFormat.cancelled'),
}));

export function visitStatusLabel(status: string): string {
  return Object.hasOwn(VISIT_STATUS_LABELS, status) ? VISIT_STATUS_LABELS[status] : status;
}
