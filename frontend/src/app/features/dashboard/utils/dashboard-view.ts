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

const MINUS = '−';
const WEEKDAY_DATE = new Intl.DateTimeFormat('en-US', {
  weekday: 'long',
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: 'UTC',
});
const HEX_COLOR = /^#[0-9a-fA-F]{6}$/;

/** BR-04: "EEEE, MMM d, yyyy". */
export function longDate(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  return WEEKDAY_DATE.format(new Date(Date.UTC(year, month - 1, day)));
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
  return hours > 0 ? `${hours}h ${minutes % 60}m` : `${minutes}m`;
}

/** BR-13: "+<n> min", "−<n> min" or "On estimate". */
export function varianceText(estimated: number, actual: number): string {
  const diff = actual - estimated;
  if (diff === 0) {
    return 'On estimate';
  }
  return diff > 0 ? `+${diff} min` : `${MINUS}${-diff} min`;
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
    return { text: 'Same as last week', direction: 'same' };
  }
  return diff > 0
    ? { text: `+${diff}% vs last week`, direction: 'up' }
    : { text: `${MINUS}${-diff}% vs last week`, direction: 'down' };
}

// BR-10 operation rows

export interface StatusChip {
  readonly label: string;
  readonly icon: string;
  readonly tone: 'danger' | 'neutral' | 'info' | 'warning' | 'success';
}

export function statusChip(visit: DayVisit): StatusChip {
  if (visit.isDelayed) {
    return { label: 'Delayed', icon: 'pi-exclamation-circle', tone: 'danger' };
  }
  switch (visit.status) {
    case 'on_the_way':
      return { label: 'On the way', icon: 'pi-send', tone: 'info' };
    case 'in_progress':
      return { label: 'In progress', icon: 'pi-play', tone: 'info' };
    case 'paused':
      return { label: 'Paused', icon: 'pi-pause', tone: 'warning' };
    default:
      return visit.statusGroup === 'completed'
        ? { label: 'Completed', icon: 'pi-check-circle', tone: 'success' }
        : { label: 'Scheduled', icon: 'pi-clock', tone: 'neutral' };
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
    return { label: at === '' ? 'Completed' : `Completed ${at}`, note: null };
  }
  const late = visit.isDelayed ? { text: `+${visit.delayMinutes ?? 0} min`, late: true } : null;
  if (visit.statusGroup === 'active') {
    const end = formatInstant(visit.projectedEnd ?? visit.scheduledEnd, timezone);
    return { label: `Ends ~${end}`, note: late ?? { text: 'On time', late: false } };
  }
  return {
    label: `Starts ${formatInstant(visit.scheduledStart, timezone)}`,
    note: late ?? (visit.status === 'on_the_way' ? { text: 'On time', late: false } : null),
  };
}

export interface RowAction {
  readonly label: 'Assign' | 'Reassign' | 'Contact' | 'View';
  readonly link: AppLink | null;
  /** `tel:` target of Contact. */
  readonly href: string | null;
}

export function rowAction(visit: DayVisit, date: string): RowAction {
  const schedule = visitLink(visit.branchId, date, visit.visitId);
  if (visit.status === 'scheduled') {
    return { label: 'Assign', link: schedule, href: null };
  }
  if (visit.status === 'assigned' && visit.isDelayed) {
    return { label: 'Reassign', link: schedule, href: null };
  }
  if (visit.status === 'on_the_way' && visit.customerPhone !== null) {
    return {
      label: 'Contact',
      link: null,
      href: `tel:${visit.customerPhone.replace(/[^\d+]/g, '')}`,
    };
  }
  return { label: 'View', link: jobLink(visit.workOrderId), href: null };
}

export function technicianLabel(visit: DayVisit): string {
  if (visit.technician === null) {
    return 'Unassigned';
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
      title: `Technician ${tech.fullName} at ${tech.percent}% capacity`,
      subtitle: `${hoursNumber(tech.scheduledMinutes)}/${hoursNumber(tech.availableMinutes)} hrs scheduled · Consider rebalancing`,
      time,
      action: { label: 'Balance workload', link: boardLink(item.branchId, date) },
      menu: { label: 'View team', link: TEAM_LINK },
    };
  }
  const job = jobLink(item.workOrderId ?? '');
  const schedule = visitLink(item.branchId, date, item.visitId ?? '');
  const action =
    item.visitStatus === 'scheduled'
      ? { label: 'Assign', link: schedule }
      : item.visitStatus === 'assigned'
        ? { label: 'Reassign', link: schedule }
        : { label: 'View job', link: job };
  const base = {
    key: `${item.type}-${item.visitId ?? index}`,
    time,
    action,
    menu: { label: 'View job', link: job },
  };
  switch (item.type) {
    case 'delayed':
      return {
        ...base,
        severity: 'Critical',
        icon: 'pi-exclamation-circle',
        title: `${number} delayed by ${item.minutes ?? 0} min`,
        subtitle: title,
      };
    case 'unassigned':
      return {
        ...base,
        severity: 'Warning',
        icon: 'pi-exclamation-triangle',
        title: `${number} has no technician assigned`,
        subtitle: `${title} · Starts at ${time ?? ''}`,
      };
    default:
      return {
        ...base,
        severity: 'Warning',
        icon: 'pi-stopwatch',
        title: `${number} estimated duration exceeded`,
        subtitle: `${title} · ${item.minutes ?? 0} min over estimate`,
      };
  }
}

// BR-12 technicians

const STATUS_VIEW: Record<TechnicianStatus, { label: string; tone: string }> = {
  time_off: { label: 'Time off', tone: 'neutral' },
  off: { label: 'Off', tone: 'neutral' },
  break: { label: 'Break', tone: 'warning' },
  on_job: { label: 'On job', tone: 'info' },
  available: { label: 'Available', tone: 'success' },
};

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
    workload = { text: 'No availability', bar: 100, over: true };
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
      next = 'Now';
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
  const who = event.technicianName ?? 'A technician';
  const number = event.displayNumber;
  const texts: Record<ActivityEvent['type'], readonly [string, string]> = {
    on_the_way: ['Technician on the way', `${who} is on the way to ${number}`],
    started: ['Job started', `${who} started ${number}`],
    completed: ['Job completed', `${number} · ${event.title} · ${who}`],
    assigned: ['Technician assigned', `${who} assigned to ${number}`],
    reassigned: ['Assignment changed', `${number} reassigned to ${who}`],
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
