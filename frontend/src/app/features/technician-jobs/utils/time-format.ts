import { TimeDay, TimeEntryType, TimeView } from '../models/technician-visits.model';
import { addDays } from './schedule-format';

import { localized, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
function calendar(date: string, options: Intl.DateTimeFormatOptions): string {
  return new Intl.DateTimeFormat(activeIntlLocale(), { ...options, timeZone: 'UTC' }).format(
    new Date(`${date}T12:00:00Z`),
  );
}

/** BR-07: "<h>h <m>m", "<m>m" under one hour, "0m" when zero. */
export function formatSeconds(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds / 60));
  const hours = Math.floor(total / 60);
  return hours > 0
    ? t('technicianJobs.timeFormat.hM', { hours: hours, value: total % 60 })
    : `${total}m`;
}

/** BR-06: "EEE, MMM d" (day) or "MMM d – d" / "MMM d – MMM d" (week); the year is appended when not the current one. */
export function rangeLabel(view: TimeView, from: string, to: string, today: string): string {
  const year = today.slice(0, 4);
  const withYear = from.slice(0, 4) !== year || to.slice(0, 4) !== year;
  const suffix = withYear ? `, ${to.slice(0, 4)}` : '';
  if (view === 'day') {
    return `${calendar(from, { weekday: 'short', month: 'short', day: 'numeric' })}${suffix}`;
  }
  const start = calendar(from, { month: 'short', day: 'numeric' });
  const sameMonth = from.slice(0, 7) === to.slice(0, 7);
  const end = sameMonth
    ? calendar(to, { day: 'numeric' })
    : calendar(to, { month: 'short', day: 'numeric' });
  return `${start} – ${end}${suffix}`;
}

/** BR-08 section heading: "Today · EEEE", "Yesterday · EEEE" or "EEEE, MMM d". */
export function sectionHeading(date: string, today: string): string {
  const weekday = calendar(date, { weekday: 'long' });
  if (date === today) {
    return t('technicianJobs.timeFormat.today', { weekday: weekday });
  }
  return date === addDays(today, -1)
    ? t('technicianJobs.timeFormat.yesterday', { weekday: weekday })
    : calendar(date, { weekday: 'long', month: 'short', day: 'numeric' });
}

/** BR-09: "EEE, MMM d, yyyy" of a local date. */
export function detailDate(date: string): string {
  return calendar(date, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
}

export function weekdayName(date: string): string {
  return calendar(date, { weekday: 'short' });
}

export const TYPE_LABELS: Readonly<Record<TimeEntryType, string>> = localized(() => ({
  work: t('technicianJobs.timeFormat.work'),
  travel: t('technicianJobs.timeFormat.travel'),
  break: t('technicianJobs.timeFormat.break'),
}));

/** BR-11: "Mon: total 4h 20m, work 3h 40m, travel 30m, break 10m". */
export function dayAlternative(day: TimeDay): string {
  return t('technicianJobs.timeFormat.totalWorkTravelBreak', {
    date: weekdayName(day.date),
    totalSeconds: formatSeconds(day.totalSeconds),
    workSeconds: formatSeconds(day.workSeconds),
    travelSeconds: formatSeconds(day.travelSeconds),
    breakSeconds: formatSeconds(day.breakSeconds),
  });
}
