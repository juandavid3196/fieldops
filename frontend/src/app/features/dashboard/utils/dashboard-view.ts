import {
  ActivityEvent,
  Attention,
  DayVisit,
  PerformanceResponse,
  TechnicianAvailability,
  TechnicianStatus,
} from '../models/dashboard.model';
import { formatInstant, monthDay } from '../../schedule/utils/schedule-range';
import { AppLink, boardLink, jobLink, TEAM_LINK, visitLink } from './dashboard-params';

import { activeIntlLocale } from '../../../core/i18n/active-locale';
import { localized, t } from '../../../core/i18n/translate';
const MINUS = '−';
const WEEKDAY_DATE_OPTIONS: Intl.DateTimeFormatOptions = {
  weekday: 'long',
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: 'UTC',
};
const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

/** BR-04: "EEEE, MMM d, yyyy". */
export function longDate(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  return new Intl.DateTimeFormat(activeIntlLocale(), WEEKDAY_DATE_OPTIONS).format(
    new Date(Date.UTC(year, month - 1, day)),
  );
}

/** Hours with at most one decimal. */
export function hoursNumber(minutes: number): number {
  return Math.round((minutes / 60) * 10) / 10;
}

/** "<n>h" with at most one decimal. */
export function hoursText(minutes: number): string {
  return `${hoursNumber(minutes)}h`;
}

/** BR-13: "<h>h <m>m". */
export function durationText(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  return hours > 0
    ? t('dashboard.dashboardView.hM', { hours: hours, value: minutes % 60 })
    : `${minutes}m`;
}

/** BR-13: "+<n> min", "−<n> min" or "On estimate". */
export function varianceText(estimated: number, actual: number): string {
  const diff = actual - estimated;
  if (diff === 0) {
    return t('dashboard.dashboardView.onEstimate');
  }
  return diff > 0
    ? t('dashboard.dashboardView.min', { diff: diff })
    : t('dashboard.dashboardView.min2', { MINUS: MINUS, value: -diff });
}

/** Variance colour: on or under estimate is good, up to 15% over a warning, beyond that a problem. */
export function varianceTone(estimated: number, actual: number): 'good' | 'warning' | 'bad' {
  const diff = actual - estimated;
  if (diff <= 0) {
    return 'good';
  }
  return diff / Math.max(1, estimated) >= 0.15 ? 'bad' : 'warning';
}

export function percentOf(part: number, total: number): number | null {
  return total > 0 ? Math.round((part / total) * 100) : null;
}

export function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

// BR-09 trend

export interface Trend {
  readonly text: string;
  readonly direction: 'up' | 'down' | 'same';
}

/** Comparison with last week in percentage points; hidden when either side has no data. */
export function onTimeTrend(percent: number | null, previous: number | null): Trend | null {
  if (percent === null || previous === null) {
    return null;
  }
  const diff = percent - previous;
  if (diff === 0) {
    return { text: t('dashboard.dashboardView.sameAsLastWeek'), direction: 'same' };
  }
  return diff > 0
    ? { text: t('dashboard.dashboardView.vsLastWeek', { diff: diff }), direction: 'up' }
    : {
        text: t('dashboard.dashboardView.vsLastWeek2', { MINUS: MINUS, value: -diff }),
        direction: 'down',
      };
}

// BR-10 operation rows

export interface StatusChip {
  readonly label: string;
  readonly icon: string;
  readonly tone: 'danger' | 'neutral' | 'info' | 'warning' | 'success';
}

export function statusChip(visit: DayVisit): StatusChip {
  if (visit.isDelayed) {
    return {
      label: t('dashboard.dashboardView.delayed'),
      icon: 'pi-exclamation-circle',
      tone: 'danger',
    };
  }
  switch (visit.status) {
    case 'on_the_way':
      return { label: t('dashboard.dashboardView.onTheWay'), icon: 'pi-send', tone: 'info' };
    case 'in_progress':
      return { label: t('dashboard.dashboardView.inProgress'), icon: 'pi-play', tone: 'info' };
    case 'paused':
      return { label: t('dashboard.dashboardView.paused'), icon: 'pi-pause', tone: 'warning' };
    default:
      return visit.statusGroup === 'completed'
        ? {
            label: t('dashboard.dashboardView.completed'),
            icon: 'pi-check-circle',
            tone: 'success',
          }
        : { label: t('dashboard.dashboardView.scheduled'), icon: 'pi-clock', tone: 'neutral' };
  }
}

export interface Timing {
  readonly label: string;
  readonly note: { readonly text: string; readonly late: boolean } | null;
}

export function timing(visit: DayVisit, timezone: string): Timing {
  if (visit.statusGroup === 'completed') {
    const at =
      visit.actualCompletedAt === null ? '' : formatInstant(visit.actualCompletedAt, timezone);
    return {
      label:
        at === ''
          ? t('dashboard.dashboardView.completed')
          : t('dashboard.dashboardView.completed2', { at: at }),
      note: null,
    };
  }
  const late = visit.isDelayed
    ? { text: t('dashboard.dashboardView.min3', { value: visit.delayMinutes ?? 0 }), late: true }
    : null;
  if (visit.statusGroup === 'active') {
    const end = formatInstant(visit.projectedEnd ?? visit.scheduledEnd, timezone);
    return {
      label: t('dashboard.dashboardView.ends', { end: end }),
      note: late ?? { text: t('dashboard.dashboardView.onTime'), late: false },
    };
  }
  return {
    label: t('dashboard.dashboardView.starts', {
      value: formatInstant(visit.scheduledStart, timezone),
    }),
    note:
      late ??
      (visit.status === 'on_the_way'
        ? { text: t('dashboard.dashboardView.onTime'), late: false }
        : null),
  };
}

export interface RowAction {
  readonly label: string;
  readonly link: AppLink | null;
  /** `tel:` target of Contact. */
  readonly href: string | null;
}

export function rowAction(visit: DayVisit, date: string): RowAction {
  const schedule = visitLink(visit.branchId, date, visit.visitId);
  if (visit.status === 'scheduled') {
    return { label: t('dashboard.dashboardView.assign'), link: schedule, href: null };
  }
  if (visit.status === 'assigned' && visit.isDelayed) {
    return { label: t('dashboard.dashboardView.reassign'), link: schedule, href: null };
  }
  if (visit.status === 'on_the_way' && visit.customerPhone !== null) {
    return {
      label: t('dashboard.dashboardView.contact'),
      link: null,
      href: `tel:${visit.customerPhone.replace(/[^\d+]/g, '')}`,
    };
  }
  return { label: t('common.view'), link: jobLink(visit.workOrderId), href: null };
}

export function technicianLabel(visit: DayVisit): string {
  if (visit.technician === null) {
    return t('dashboard.dashboardView.unassigned');
  }
  return visit.otherTechnicianCount > 0
    ? `${visit.technician.fullName} +${visit.otherTechnicianCount}`
    : visit.technician.fullName;
}

/** BR-10: Time ascending or descending, ties by work order number. */
export function sortVisits(visits: readonly DayVisit[], descending: boolean): readonly DayVisit[] {
  const direction = descending ? -1 : 1;
  return [...visits].sort(
    (a, b) =>
      direction * (Date.parse(a.scheduledStart) - Date.parse(b.scheduledStart)) ||
      a.displayNumber.localeCompare(b.displayNumber, 'en', { numeric: true }),
  );
}

// BR-11 needs attention

export interface AttentionView {
  readonly key: string;
  readonly severity: 'Critical' | 'Warning';
  readonly icon: string;
  readonly title: string;
  readonly subtitle: string;
  readonly time: string | null;
  readonly action: { readonly label: string; readonly link: AppLink };
  readonly menu: { readonly label: string; readonly link: AppLink };
}

export function attentionView(
  item: Attention,
  index: number,
  date: string,
  timezone: string,
): AttentionView {
  const time = item.at === null ? null : formatInstant(item.at, timezone);
  const number = item.displayNumber ?? '';
  const title = item.title ?? '';
  if (item.type === 'over_capacity' && item.technician !== null) {
    const tech = item.technician;
    return {
      key: `capacity-${tech.id}`,
      severity: 'Warning',
      icon: 'pi-users',
      title: t('dashboard.dashboardView.technicianAtCapacity', {
        fullName: tech.fullName,
        percent: tech.percent,
      }),
      subtitle: t('dashboard.dashboardView.hrsScheduledConsiderRebalancing', {
        scheduledMinutes: hoursNumber(tech.scheduledMinutes),
        availableMinutes: hoursNumber(tech.availableMinutes),
      }),
      time,
      action: {
        label: t('dashboard.dashboardView.balanceWorkload'),
        link: boardLink(item.branchId, date),
      },
      menu: { label: t('dashboard.dashboardView.viewTeam'), link: TEAM_LINK },
    };
  }
  const job = jobLink(item.workOrderId ?? '');
  const schedule = visitLink(item.branchId, date, item.visitId ?? '');
  const action =
    item.visitStatus === 'scheduled'
      ? { label: t('dashboard.dashboardView.assign'), link: schedule }
      : item.visitStatus === 'assigned'
        ? { label: t('dashboard.dashboardView.reassign'), link: schedule }
        : { label: t('dashboard.dashboardView.viewJob'), link: job };
  const base = {
    key: `${item.type}-${item.visitId ?? index}`,
    time,
    action,
    menu: { label: t('dashboard.dashboardView.viewJob'), link: job },
  };
  switch (item.type) {
    case 'delayed':
      return {
        ...base,
        severity: 'Critical',
        icon: 'pi-exclamation-circle',
        title: t('dashboard.dashboardView.delayedByMin', {
          number: number,
          value: item.minutes ?? 0,
        }),
        subtitle: title,
      };
    case 'unassigned':
      return {
        ...base,
        severity: 'Warning',
        icon: 'pi-exclamation-triangle',
        title: t('dashboard.dashboardView.hasNoTechnicianAssigned', { number: number }),
        subtitle: t('dashboard.dashboardView.startsAt', { title: title, value: time ?? '' }),
      };
    default:
      return {
        ...base,
        severity: 'Warning',
        icon: 'pi-stopwatch',
        title: t('dashboard.dashboardView.estimatedDurationExceeded', { number: number }),
        subtitle: t('dashboard.dashboardView.minOverEstimate', {
          title: title,
          value: item.minutes ?? 0,
        }),
      };
  }
}

// BR-12 technicians

const STATUS_VIEW: Record<TechnicianStatus, { label: string; tone: string }> = localized(() => ({
  time_off: { label: t('dashboard.dashboardView.timeOff'), tone: 'neutral' },
  off: { label: t('dashboard.dashboardView.off'), tone: 'neutral' },
  break: { label: t('dashboard.dashboardView.break'), tone: 'warning' },
  on_job: { label: t('dashboard.dashboardView.onJob'), tone: 'info' },
  available: { label: t('dashboard.dashboardView.available'), tone: 'success' },
}));

export interface TechnicianRow {
  readonly id: string;
  readonly fullName: string;
  readonly initials: string;
  readonly color: string | null;
  readonly skills: string;
  readonly status: { readonly label: string; readonly tone: string } | null;
  readonly workload: { readonly text: string; readonly bar: number; readonly over: boolean } | null;
  readonly next: string;
}

export function technicianRow(
  tech: TechnicianAvailability,
  isToday: boolean,
  timezone: string,
): TechnicianRow {
  const skills =
    tech.skills.length === 0
      ? '—'
      : tech.skills.join(', ') + (tech.extraSkillCount > 0 ? ` +${tech.extraSkillCount}` : '');
  let workload: TechnicianRow['workload'] = null;
  if (tech.availableMinutes === 0 && tech.scheduledMinutes > 0) {
    workload = { text: t('dashboard.dashboardView.noAvailability'), bar: 100, over: true };
  } else if (tech.percent !== null) {
    workload = {
      text: `${tech.percent}%`,
      bar: Math.min(100, tech.percent),
      over: tech.percent > 100,
    };
  }
  let next = '—';
  if (isToday) {
    if (tech.currentStatus === 'available') {
      next = t('dashboard.dashboardView.now');
    } else if (tech.nextAvailable !== null) {
      next = formatInstant(tech.nextAvailable, timezone);
    }
  }
  return {
    id: tech.id,
    fullName: tech.fullName,
    initials: tech.initials,
    color: tech.colorHex !== null && HEX_COLOR.test(tech.colorHex) ? tech.colorHex : null,
    skills,
    status: isToday && tech.currentStatus !== null ? STATUS_VIEW[tech.currentStatus] : null,
    workload,
    next,
  };
}

// BR-15 activity

const ACTIVITY_ICONS: Record<ActivityEvent['type'], string> = {
  on_the_way: 'pi-send',
  started: 'pi-play',
  completed: 'pi-check',
  assigned: 'pi-user-plus',
  reassigned: 'pi-sync',
};

const ACTIVITY_TONES: Record<ActivityEvent['type'], 'success' | 'info' | 'primary' | 'neutral'> = {
  on_the_way: 'primary',
  started: 'success',
  completed: 'success',
  assigned: 'neutral',
  reassigned: 'info',
};

export interface ActivityView {
  readonly key: string;
  readonly icon: string;
  readonly tone: 'success' | 'info' | 'primary' | 'neutral';
  readonly time: string;
  readonly title: string;
  readonly subtitle: string;
  readonly link: AppLink;
}

export function activityView(event: ActivityEvent, index: number, timezone: string): ActivityView {
  const who = event.technicianName ?? t('dashboard.dashboardView.aTechnician');
  const number = event.displayNumber;
  const texts: Record<ActivityEvent['type'], readonly [string, string]> = {
    on_the_way: [
      t('dashboard.dashboardView.technicianOnTheWay'),
      t('dashboard.dashboardView.isOnTheWay', { who: who, number: number }),
    ],
    started: [
      t('dashboard.dashboardView.jobStarted'),
      t('dashboard.dashboardView.started', { who: who, number: number }),
    ],
    completed: [t('dashboard.dashboardView.jobCompleted'), `${number} · ${event.title} · ${who}`],
    assigned: [
      t('dashboard.dashboardView.technicianAssigned'),
      t('dashboard.dashboardView.assignedTo', { who: who, number: number }),
    ],
    reassigned: [
      t('dashboard.dashboardView.assignmentChanged'),
      t('dashboard.dashboardView.reassignedTo', { number: number, who: who }),
    ],
  };
  const [title, subtitle] = texts[event.type];
  return {
    key: `${event.type}-${event.visitId}-${index}`,
    icon: ACTIVITY_ICONS[event.type],
    tone: ACTIVITY_TONES[event.type],
    time: formatInstant(event.at, timezone),
    title,
    subtitle,
    link: jobLink(event.workOrderId),
  };
}

// BR-14 chart

export const CHART_WIDTH = 300;
export const CHART_HEIGHT = 100;
const CHART_PAD = 6;
/** Room for the y-axis labels. */
const CHART_LEFT = 20;

export interface ChartPoint {
  readonly x: number;
  readonly y: number;
  readonly date: string;
  readonly completed: number;
}

export interface ChartModel {
  readonly max: number;
  readonly points: readonly ChartPoint[];
  readonly line: string;
  readonly area: string;
  readonly labels: readonly { readonly x: number; readonly text: string }[];
}

export function chartModel(daily: PerformanceResponse['daily']): ChartModel {
  const max = Math.max(1, ...daily.map((day) => day.completed));
  const span = Math.max(1, daily.length - 1);
  const points = daily.map((day, index) => ({
    x: CHART_LEFT + (index / span) * (CHART_WIDTH - CHART_LEFT - CHART_PAD - 8),
    y: CHART_HEIGHT - CHART_PAD - (day.completed / max) * (CHART_HEIGHT - 2 * CHART_PAD),
    date: day.date,
    completed: day.completed,
  }));
  const line = points.map((p) => `${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ');
  const base = CHART_HEIGHT - CHART_PAD;
  const area =
    points.length === 0
      ? ''
      : `${line} ${points[points.length - 1].x.toFixed(1)},${base} ${points[0].x.toFixed(1)},${base}`;
  const step = Math.max(1, Math.ceil(points.length / 7));
  const labels = points
    .filter((_, index) => index % step === 0 || index === points.length - 1)
    .map((p) => ({ x: p.x, text: monthDay(p.date) }));
  return { max, points, line, area, labels };
}
