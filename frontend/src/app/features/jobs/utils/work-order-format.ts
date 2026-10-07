import {
  ArrivalWindow,
  JobType,
  MaterialSource,
  Priority,
  RecurrenceFrequency,
  WorkOrderStatus,
} from '../models/work-order.model';

export const PRIORITY_LABELS: Readonly<Record<Priority, string>> = {
  urgent: 'Urgent',
  high: 'High',
  normal: 'Normal',
  low: 'Low',
};

export const STATUS_LABELS: Readonly<Record<WorkOrderStatus, string>> = {
  draft: 'Draft',
  ready_to_schedule: 'Unscheduled',
  scheduled: 'Scheduled',
};

export const JOB_TYPE_LABELS: Readonly<Record<JobType, string>> = {
  one_time: 'One-time job',
  recurring: 'Recurring job',
};

export const SOURCE_LABELS: Readonly<Record<MaterialSource, string>> = {
  truck_stock: 'Truck stock',
  warehouse: 'Warehouse',
  to_purchase: 'To purchase',
};

export const WINDOW_LABELS: Readonly<Record<ArrivalWindow, string>> = {
  any: 'Any time',
  '08-11': '8:00 AM – 11:00 AM',
  '09-12': '9:00 AM – 12:00 PM',
  '12-15': '12:00 PM – 3:00 PM',
  '13-16': '1:00 PM – 4:00 PM',
  '15-18': '3:00 PM – 6:00 PM',
};

export const FREQUENCY_LABELS: Readonly<Record<RecurrenceFrequency, string>> = {
  weekly: 'Weekly',
  biweekly: 'Every 2 weeks',
  monthly: 'Monthly',
  quarterly: 'Quarterly',
};

/** BR-08: 30 to 720 minutes in steps of 30, shown "30 min", "1h", "1.5h" … "12h". */
export function durationLabel(minutes: number | null): string {
  if (minutes === null) {
    return '—';
  }
  return minutes < 60 ? `${minutes} min` : `${minutes / 60}h`;
}

export const DURATION_OPTIONS: { readonly code: number | null; readonly label: string }[] = [
  { code: null, label: 'Not set' },
  ...Array.from({ length: 24 }, (_, index) => {
    const minutes = (index + 1) * 30;
    return { code: minutes, label: durationLabel(minutes) };
  }),
];

/** Select options of a label record, in declaration order. */
export function optionsOf<T extends string>(
  labels: Readonly<Record<T, string>>,
): { readonly code: T; readonly label: string }[] {
  return (Object.keys(labels) as T[]).map((code) => ({ code, label: labels[code] }));
}

/** "MMM d, yyyy" of a calendar date (`YYYY-MM-DD`), never shifted by a time zone. */
export function plainDate(value: string): string {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${value}T12:00:00Z`));
}

/** BR-16: visit status labels shown in the job detail ("Visit #<k> · <label>"). */
export const VISIT_STATUS_LABELS: Readonly<Record<string, string>> = {
  unscheduled: 'Unscheduled',
  scheduled: 'Scheduled',
  assigned: 'Assigned',
  on_the_way: 'On the way',
  in_progress: 'In progress',
  paused: 'Paused',
  completed: 'Completed',
  needs_correction: 'Needs correction',
  approved: 'Approved',
  cancelled: 'Cancelled',
};

export function visitStatusLabel(status: string): string {
  return Object.hasOwn(VISIT_STATUS_LABELS, status) ? VISIT_STATUS_LABELS[status] : status;
}
