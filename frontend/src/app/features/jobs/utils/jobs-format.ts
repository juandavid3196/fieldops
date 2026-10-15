import {
  JOBS_PAGE_SIZES,
  JOBS_QUERY_KEYS,
  JobActionCode,
  JobRow,
  JobStatusCode,
  JobsPriority,
  JobsSort,
  JobsTab,
  JobsView,
} from '../models/jobs.model';

export type ChipTone = 'neutral' | 'info' | 'purple' | 'warning' | 'success' | 'danger';

/** BR-05: code -> fixed label and chip tone (the API never sends labels). */
export const JOB_STATUS_CHIPS: Readonly<
  Record<JobStatusCode, { readonly label: string; readonly tone: ChipTone }>
> = {
  ready_to_schedule: { label: 'Ready to schedule', tone: 'neutral' },
  scheduled: { label: 'Scheduled', tone: 'info' },
  assigned: { label: 'Assigned', tone: 'purple' },
  on_the_way: { label: 'On the way', tone: 'info' },
  in_progress: { label: 'In progress', tone: 'warning' },
  paused: { label: 'Paused', tone: 'warning' },
  completed: { label: 'Completed', tone: 'success' },
  needs_correction: { label: 'Needs correction', tone: 'danger' },
  approved_for_billing: { label: 'Approved for billing', tone: 'success' },
  cancelled: { label: 'Cancelled', tone: 'neutral' },
};

/** BR-06: status codes allowed per tab. */
export const TAB_STATUSES: Readonly<Record<JobsTab, readonly JobStatusCode[]>> = {
  all: Object.keys(JOB_STATUS_CHIPS) as JobStatusCode[],
  active: ['scheduled', 'assigned', 'on_the_way', 'in_progress', 'paused'],
  unscheduled: ['ready_to_schedule'],
  completed: ['completed', 'needs_correction', 'approved_for_billing'],
};

export const JOBS_TABS: readonly { readonly code: JobsTab; readonly label: string }[] = [
  { code: 'all', label: 'All jobs' },
  { code: 'active', label: 'Active' },
  { code: 'unscheduled', label: 'Unscheduled' },
  { code: 'completed', label: 'Completed' },
];

export const JOBS_PRIORITIES: readonly { readonly code: JobsPriority; readonly label: string }[] = [
  { code: 'urgent', label: 'Urgent' },
  { code: 'high', label: 'High' },
  { code: 'normal', label: 'Normal' },
  { code: 'low', label: 'Low' },
];

export const JOBS_SORTS: readonly { readonly code: JobsSort; readonly label: string }[] = [
  { code: 'newest', label: 'Newest' },
  { code: 'oldest', label: 'Oldest' },
  { code: 'updated', label: 'Recently updated' },
  { code: 'schedule', label: 'Schedule' },
  { code: 'priority', label: 'Priority' },
];

const MAX_SEARCH_LENGTH = 100;

function positiveInteger(value: string | null): number | null {
  if (value === null || !/^\d{1,9}$/.test(value)) {
    return null;
  }
  const parsed = Number(value);
  return parsed >= 1 ? parsed : null;
}

/**
 * BR-16: the valid view of the raw URL parameters; every unknown or invalid value takes its default.
 * `branchIds` is null until the options are known; `defaultBranchId` is the branch whose URL form is
 * "absent".
 */
export function resolveJobsView(
  get: (key: string) => string | null,
  branchIds: readonly string[] | null,
  defaultBranchId: string | null,
): JobsView {
  const tab = JOBS_TABS.find((candidate) => candidate.code === get('tab'))?.code ?? 'all';
  const term = (get('q') ?? '').trim();
  const rawBranch = get('branch');
  const branch =
    rawBranch === null ||
    rawBranch === '' ||
    rawBranch === defaultBranchId ||
    (branchIds !== null && !branchIds.includes(rawBranch))
      ? null
      : rawBranch;
  const pageSize = positiveInteger(get('pageSize'));
  return {
    tab,
    q: term.length > MAX_SEARCH_LENGTH ? '' : term,
    branch,
    status: TAB_STATUSES[tab].find((code) => code === get('status')) ?? null,
    priority: JOBS_PRIORITIES.find((candidate) => candidate.code === get('priority'))?.code ?? null,
    sort: JOBS_SORTS.find((candidate) => candidate.code === get('sort'))?.code ?? 'newest',
    page: positiveInteger(get('page')) ?? 1,
    pageSize: JOBS_PAGE_SIZES.find((size) => size === pageSize) ?? JOBS_PAGE_SIZES[0],
  };
}

/** Canonical query parameters of a view; defaults are null (omitted from the URL). */
export function jobsViewParams(view: JobsView): Record<string, string | null> {
  return {
    tab: view.tab === 'all' ? null : view.tab,
    q: view.q === '' ? null : view.q,
    branch: view.branch,
    status: view.status,
    priority: view.priority,
    sort: view.sort === 'newest' ? null : view.sort,
    page: view.page === 1 ? null : String(view.page),
    pageSize: view.pageSize === JOBS_PAGE_SIZES[0] ? null : String(view.pageSize),
  };
}

/** True when the raw URL already is the canonical form of its view. */
export function isCanonicalJobsUrl(get: (key: string) => string | null, view: JobsView): boolean {
  const canonical = jobsViewParams(view);
  return JOBS_QUERY_KEYS.every((key) => (get(key) ?? null) === canonical[key]);
}

/** BR-16: the badge of Clear filters counts the active search, status and priority. */
export function activeFilterCount(view: JobsView): number {
  return (
    (view.q !== '' ? 1 : 0) + (view.status !== null ? 1 : 0) + (view.priority !== null ? 1 : 0)
  );
}

// Dates (BR-12, BR-13)

const dayKey = (date: Date, timeZone: string): string =>
  new Intl.DateTimeFormat('en-CA', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date);

const dayNumber = (key: string): number => Date.parse(`${key}T00:00:00Z`) / 86_400_000;

/** "MMM d", with ", yyyy" when the year differs from the reference year. */
export function monthDay(date: Date, timeZone: string, now: Date): string {
  const sameYear = dayKey(date, timeZone).slice(0, 4) === dayKey(now, timeZone).slice(0, 4);
  return new Intl.DateTimeFormat('en-US', {
    timeZone,
    month: 'short',
    day: 'numeric',
    ...(sameYear ? {} : { year: 'numeric' }),
  }).format(date);
}

function clock(date: Date, timeZone: string): { time: string; period: string } {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  }).formatToParts(date);
  const pick = (type: string): string => parts.find((part) => part.type === type)?.value ?? '';
  return { time: `${pick('hour')}:${pick('minute')}`, period: pick('dayPeriod').toUpperCase() };
}

/** BR-12 schedule: "Today · 1:00–3:00 PM", "Tomorrow · …", "Oct 9 · …" or "Not scheduled". */
export function scheduleText(row: JobRow, now: Date): string {
  const start = row.currentVisit?.scheduledStart ?? null;
  if (start === null) {
    return 'Not scheduled';
  }
  const from = new Date(start);
  const zone = row.timezone;
  const diff = dayNumber(dayKey(from, zone)) - dayNumber(dayKey(now, zone));
  const day = diff === 0 ? 'Today' : diff === 1 ? 'Tomorrow' : monthDay(from, zone, now);
  const first = clock(from, zone);
  const endIso = row.currentVisit?.scheduledEnd ?? null;
  if (endIso === null) {
    return `${day} · ${first.time} ${first.period}`;
  }
  const last = clock(new Date(endIso), zone);
  const range =
    first.period === last.period
      ? `${first.time}–${last.time} ${last.period}`
      : `${first.time} ${first.period}–${last.time} ${last.period}`;
  return `${day} · ${range}`;
}

/** BR-13 relative text in the organization time zone. */
export function updatedText(iso: string, timeZone: string, now: Date): string {
  const at = new Date(iso);
  const minutes = Math.floor((now.getTime() - at.getTime()) / 60_000);
  if (minutes < 1) {
    return 'Just now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  const diff = dayNumber(dayKey(now, timeZone)) - dayNumber(dayKey(at, timeZone));
  if (diff <= 0) {
    return 'Today';
  }
  return diff === 1 ? 'Yesterday' : monthDay(at, timeZone, now);
}

/** Full date and time, the accessible label and tooltip of the Updated cell. */
export function fullDateTime(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('en-US', {
    timeZone,
    dateStyle: 'medium',
    timeStyle: 'short',
  })
    .format(new Date(iso))
    .replaceAll(' ', ' ');
}

/** "<postal code> <city>" with the postal code omitted when null. */
export function propertyLocation(row: JobRow): string {
  const { postalCode, city } = row.property;
  return [postalCode, city].filter((part) => part !== null && part !== '').join(' ');
}

// Row actions (BR-14)

export interface JobActionTarget {
  readonly code: JobActionCode;
  readonly label: string;
  readonly commands: readonly string[];
  readonly queryParams: Readonly<Record<string, string>> | null;
}

/** The menu items of a row: the server's `actions`, in order, with the BR-14 destinations. */
export function jobActionTargets(row: JobRow): JobActionTarget[] {
  const visit = row.currentVisit;
  const targets: JobActionTarget[] = [];
  for (const code of row.actions) {
    switch (code) {
      case 'view':
        targets.push({
          code,
          label: 'View job',
          commands: ['/jobs', row.id],
          queryParams: null,
        });
        break;
      case 'schedule_visit':
        if (visit !== null) {
          targets.push({
            code,
            label: 'Schedule visit',
            commands: ['/schedule'],
            queryParams: { branch: row.branch.id, visit: visit.id },
          });
        }
        break;
      case 'open_in_schedule':
        if (visit !== null && visit.scheduledStart !== null) {
          targets.push({
            code,
            label: 'Open in schedule',
            commands: ['/schedule'],
            queryParams: {
              branch: row.branch.id,
              view: 'day',
              date: dayKey(new Date(visit.scheduledStart), row.timezone),
              visit: visit.id,
            },
          });
        }
        break;
      case 'review_completion':
        targets.push({
          code,
          label: 'Review completion',
          commands: ['/invoices', 'review'],
          queryParams: { workOrderId: row.id },
        });
        break;
    }
  }
  return targets;
}

const AVATAR_FALLBACKS = [
  '#14b8a6',
  '#3b82f6',
  '#ec4899',
  '#8b5cf6',
  '#f59e0b',
  '#22c55e',
  '#ef4444',
  '#06b6d4',
  '#a855f7',
  '#84cc16',
  '#f97316',
  '#6366f1',
];

/**
 * Avatar colors for the technicians of one page, keyed by full name. A strict `#rrggbb` profile
 * color wins; otherwise a palette color seeded by the name and probed forward so that different
 * technicians on the page never share a color (while the palette lasts).
 */
export function technicianAvatarColors(
  technicians: readonly { fullName: string; colorHex: string | null }[],
): ReadonlyMap<string, string> {
  const colors = new Map<string, string>();
  const taken = new Set<string>();
  for (const { fullName, colorHex } of technicians) {
    if (colorHex !== null && /^#[0-9a-f]{6}$/i.test(colorHex) && !colors.has(fullName)) {
      colors.set(fullName, colorHex);
      taken.add(colorHex.toLowerCase());
    }
  }
  for (const { fullName } of technicians) {
    if (colors.has(fullName)) {
      continue;
    }
    let hash = 0;
    for (const char of fullName.toLowerCase()) {
      hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
    }
    let index = hash % AVATAR_FALLBACKS.length;
    for (let step = 0; step < AVATAR_FALLBACKS.length; step++) {
      const candidate = AVATAR_FALLBACKS[(index + step) % AVATAR_FALLBACKS.length];
      if (!taken.has(candidate)) {
        index = (index + step) % AVATAR_FALLBACKS.length;
        break;
      }
    }
    colors.set(fullName, AVATAR_FALLBACKS[index]);
    taken.add(AVATAR_FALLBACKS[index]);
  }
  return colors;
}
