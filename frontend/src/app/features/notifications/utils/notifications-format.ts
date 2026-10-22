import {
  MAX_SEARCH_LENGTH,
  NOTIFICATIONS_PAGE_SIZES,
  NOTIFICATIONS_QUERY_KEYS,
  NotificationEntityKind,
  NotificationItem,
  NotificationType,
  NotificationsDate,
  NotificationsTab,
  NotificationsView,
} from '../models/notifications.model';

import { localizedList, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
/** BR-02: type code -> fixed label. */
export const NOTIFICATION_TYPES: readonly {
  readonly code: NotificationType;
  readonly label: string;
}[] = localizedList(() => [
  { code: 'request', label: t('notifications.notificationsFormat.requests') },
  { code: 'message', label: t('notifications.notificationsFormat.messages') },
  { code: 'quote', label: t('notifications.notificationsFormat.quotes') },
  { code: 'visit', label: t('notifications.notificationsFormat.visits') },
  { code: 'job', label: t('notifications.notificationsFormat.jobs') },
  { code: 'invoice', label: t('notifications.notificationsFormat.invoices') },
  { code: 'payment', label: t('notifications.notificationsFormat.payments') },
]);

/** BR-11: date window codes and labels. */
export const NOTIFICATION_DATES: readonly {
  readonly code: NotificationsDate;
  readonly label: string;
}[] = localizedList(() => [
  { code: 'any', label: t('notifications.notificationsFormat.anyDate') },
  { code: 'today', label: t('notifications.notificationsFormat.today') },
  { code: '7d', label: t('notifications.notificationsFormat.last7Days') },
  { code: '30d', label: t('notifications.notificationsFormat.last30Days') },
]);

export const NOTIFICATION_TABS: readonly {
  readonly code: NotificationsTab;
  readonly label: string;
}[] = localizedList(() => [
  { code: 'all', label: t('notifications.notificationsFormat.allNotifications') },
  { code: 'unread', label: t('notifications.notificationsFormat.unread') },
]);

export type IconTone = 'info' | 'success' | 'danger' | 'warning' | 'neutral';

const QUOTE_ICONS: Readonly<Record<string, { icon: string; tone: IconTone }>> = {
  'quote.approved': { icon: 'pi-check', tone: 'success' },
  'quote.rejected': { icon: 'pi-times', tone: 'danger' },
  'quote.clarification_requested': { icon: 'pi-question-circle', tone: 'warning' },
};

const TYPE_ICONS: Readonly<Record<NotificationType, { icon: string; tone: IconTone }>> = {
  request: { icon: 'pi-file', tone: 'info' },
  message: { icon: 'pi-comment', tone: 'info' },
  quote: { icon: 'pi-check', tone: 'success' },
  visit: { icon: 'pi-calendar', tone: 'info' },
  job: { icon: 'pi-wrench', tone: 'neutral' },
  invoice: { icon: 'pi-receipt', tone: 'info' },
  payment: { icon: 'pi-dollar', tone: 'success' },
};

/** BR-02: fixed icon from the type code, and for quotes from the event code. */
export function iconFor(item: Pick<NotificationItem, 'type' | 'eventCode'>): {
  icon: string;
  tone: IconTone;
} {
  return (item.type === 'quote' ? QUOTE_ICONS[item.eventCode] : undefined) ?? TYPE_ICONS[item.type];
}

/** BR-12: the frontend owns the route of each target kind and never builds a link from labels. */
export function targetRoute(
  kind: NotificationEntityKind,
  id: string,
): { commands: string[]; queryParams: Record<string, string> | null } {
  switch (kind) {
    case 'request':
      return { commands: ['/requests'], queryParams: { request: id } };
    case 'customer':
      return { commands: ['/customers', id], queryParams: null };
    case 'quote':
      return { commands: ['/quotes', id], queryParams: null };
    case 'visit':
      return { commands: ['/schedule'], queryParams: { visit: id } };
    case 'work_order':
      return { commands: ['/jobs', id], queryParams: null };
    case 'invoice':
      return { commands: ['/invoices', id], queryParams: null };
  }
}

// View (BR-20)

function positiveInteger(value: string | null): number | null {
  return value !== null && /^[1-9]\d*$/.test(value) ? Number(value) : null;
}

/** Normalizes the raw URL values; unknown or invalid ones fall back to their defaults. */
export function resolveNotificationsView(
  get: (key: string) => string | null,
  branchIds: readonly string[] | null,
): NotificationsView {
  const term = (get('q') ?? '').trim();
  const rawBranch = get('branch');
  const pageSize = positiveInteger(get('pageSize'));
  return {
    tab: NOTIFICATION_TABS.find((tab) => tab.code === get('tab'))?.code ?? 'all',
    q: term.length > MAX_SEARCH_LENGTH ? '' : term,
    type: NOTIFICATION_TYPES.find((type) => type.code === get('type'))?.code ?? null,
    branch:
      rawBranch === null ||
      rawBranch === '' ||
      (branchIds !== null && !branchIds.includes(rawBranch))
        ? null
        : rawBranch,
    date: NOTIFICATION_DATES.find((date) => date.code === get('date'))?.code ?? 'any',
    page: positiveInteger(get('page')) ?? 1,
    pageSize:
      NOTIFICATIONS_PAGE_SIZES.find((size) => size === pageSize) ?? NOTIFICATIONS_PAGE_SIZES[0],
  };
}

/** Canonical query parameters of a view; defaults are null (omitted from the URL). */
export function notificationsViewParams(view: NotificationsView): Record<string, string | null> {
  return {
    tab: view.tab === 'all' ? null : view.tab,
    q: view.q === '' ? null : view.q,
    type: view.type,
    branch: view.branch,
    date: view.date === 'any' ? null : view.date,
    page: view.page === 1 ? null : String(view.page),
    pageSize: view.pageSize === NOTIFICATIONS_PAGE_SIZES[0] ? null : String(view.pageSize),
  };
}

export function isCanonicalNotificationsUrl(
  get: (key: string) => string | null,
  view: NotificationsView,
): boolean {
  const canonical = notificationsViewParams(view);
  return NOTIFICATIONS_QUERY_KEYS.every((key) => (get(key) ?? null) === canonical[key]);
}

/** BR-20: the badge of Clear filters counts the active search, type, branch and date. */
export function activeFilterCount(view: NotificationsView): number {
  return (
    (view.q !== '' ? 1 : 0) +
    (view.type !== null ? 1 : 0) +
    (view.branch !== null ? 1 : 0) +
    (view.date !== 'any' ? 1 : 0)
  );
}

// Time (BR-15), always in the organization time zone

function safeZone(timeZone: string): string {
  try {
    new Intl.DateTimeFormat(activeIntlLocale(), { timeZone });
    return timeZone;
  } catch {
    return 'UTC';
  }
}

/** Local calendar day as a day number (comparable and subtractable). */
function dayNumber(date: Date, timeZone: string): number {
  const parts = new Intl.DateTimeFormat(activeIntlLocale(), {
    timeZone: safeZone(timeZone),
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
  }).formatToParts(date);
  const part = (type: string): number =>
    Number(parts.find((candidate) => candidate.type === type)?.value ?? 0);
  return Math.floor(Date.UTC(part('year'), part('month') - 1, part('day')) / 86_400_000);
}

function year(date: Date, timeZone: string): string {
  return new Intl.DateTimeFormat(activeIntlLocale(), {
    timeZone: safeZone(timeZone),
    year: 'numeric',
  }).format(date);
}

/** BR-15: "Just now", "<n> min ago", "<n> hr ago", "Yesterday", "MMM d[, yyyy]". */
export function relativeTime(createdAt: string, timeZone: string, now: Date): string {
  const created = new Date(createdAt);
  const minutes = Math.floor((now.getTime() - created.getTime()) / 60_000);
  if (minutes < 1) {
    return t('notifications.notificationsFormat.justNow');
  }
  if (minutes < 60) {
    return t('notifications.notificationsFormat.minAgo', { minutes: minutes });
  }
  const days = dayNumber(now, timeZone) - dayNumber(created, timeZone);
  if (days <= 0) {
    return t('notifications.notificationsFormat.hrAgo', { value: Math.floor(minutes / 60) });
  }
  if (days === 1) {
    return t('notifications.notificationsFormat.yesterday');
  }
  const sameYear = year(created, timeZone) === year(now, timeZone);
  return new Intl.DateTimeFormat(activeIntlLocale(), {
    timeZone: safeZone(timeZone),
    month: 'short',
    day: 'numeric',
    ...(sameYear ? {} : { year: 'numeric' }),
  }).format(created);
}

/** Tooltip and accessible label of the time. */
export function fullDateTime(createdAt: string, timeZone: string): string {
  return new Intl.DateTimeFormat(activeIntlLocale(), {
    timeZone: safeZone(timeZone),
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(createdAt));
}

/** BR-15: rows created during the current local day go under Today. */
export function isToday(createdAt: string, timeZone: string, now: Date): boolean {
  return dayNumber(new Date(createdAt), timeZone) === dayNumber(now, timeZone);
}
