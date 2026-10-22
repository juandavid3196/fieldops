import {
  AUDIT_MAX_SEARCH_LENGTH,
  AUDIT_PAGE_SIZES,
  AUDIT_RELATED_DAYS,
  AuditFilterOptions,
  AuditImpact,
  AuditTarget,
  AuditView,
} from '../models/audit-log.model';

import { localized, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
export const DEFAULT_VIEW: AuditView = {
  from: null,
  to: null,
  q: '',
  actor: null,
  action: null,
  entity: null,
  entityId: null,
  branch: null,
  impact: null,
  sort: 'occurredAt',
  direction: 'desc',
  page: 1,
  pageSize: AUDIT_PAGE_SIZES[0],
};

export const IMPACT_LABELS: Readonly<Record<AuditImpact, string>> = localized(() => ({
  high: t('auditLog.auditLogFormat.high'),
  medium: t('auditLog.auditLogFormat.medium'),
  low: t('auditLog.auditLogFormat.low'),
}));

export const IMPACT_ICONS: Readonly<Record<AuditImpact, string>> = {
  high: 'pi-exclamation-triangle',
  medium: 'pi-info-circle',
  low: 'pi-check-circle',
};

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DAY_MS = 86_400_000;
const IMPACTS: readonly AuditImpact[] = ['low', 'medium', 'high'];
const PARAM_KEYS = [
  'from',
  'to',
  'q',
  'actor',
  'action',
  'entity',
  'entityId',
  'branch',
  'impact',
  'sort',
  'direction',
  'page',
  'pageSize',
] as const;

// Dates (BR-24)

function dayNumber(day: string): number | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (match === null) {
    return null;
  }
  const time = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return new Date(time).toISOString().slice(0, 10) === day ? time / DAY_MS : null;
}

function safeZone(timeZone: string | null): string | undefined {
  if (timeZone === null) {
    return undefined;
  }
  try {
    new Intl.DateTimeFormat(activeIntlLocale(), { timeZone });
    return timeZone;
  } catch {
    return undefined;
  }
}

/** `yyyy-MM-dd` of an instant in the organization zone (the browser zone before it is known). */
export function dayIn(instant: Date, timeZone: string | null): string {
  const parts = new Intl.DateTimeFormat(activeIntlLocale(), {
    timeZone: safeZone(timeZone),
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(instant);
  const get = (type: string): string => parts.find((part) => part.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
}

export function addDays(day: string, days: number): string {
  const base = dayNumber(day) ?? 0;
  return new Date((base + days) * DAY_MS).toISOString().slice(0, 10);
}

/** BR-22: from the first day of the widened range up to today. */
export function relatedRange(today: string): { from: string; to: string } {
  return { from: addDays(today, 1 - AUDIT_RELATED_DAYS), to: today };
}

function format(instant: string, timeZone: string, options: Intl.DateTimeFormatOptions): string {
  const date = new Date(instant);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(activeIntlLocale(), { timeZone: safeZone(timeZone), ...options })
    .format(date)
    .replace(/\s/g, ' ');
}

const DATE_PART: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', year: 'numeric' };
const TIME_PART: Intl.DateTimeFormatOptions = {
  hour: 'numeric',
  minute: '2-digit',
  second: '2-digit',
  hour12: true,
};

/** BR-24 table: "MMM d, yyyy" over "h:mm:ss a". */
export function tableDate(instant: string, timeZone: string): string {
  return format(instant, timeZone, DATE_PART);
}

export function tableTime(instant: string, timeZone: string): string {
  return format(instant, timeZone, TIME_PART);
}

/** BR-24 drawer: "MMM d, yyyy 'at' h:mm:ss a". */
export function drawerDateTime(instant: string, timeZone: string): string {
  return t('auditLog.auditLogFormat.at', {
    value: tableDate(instant, timeZone),
    value2: tableTime(instant, timeZone),
  });
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter((part) => part !== '')
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('');
}

// View (BR-20)

function positiveInteger(value: string | null): number | null {
  return value !== null && /^[1-9]\d*$/.test(value) ? Number(value) : null;
}

function validRange(from: string | null, to: string | null): boolean {
  const start = from === null ? null : dayNumber(from);
  const end = to === null ? null : dayNumber(to);
  if ((from !== null && start === null) || (to !== null && end === null)) {
    return false;
  }
  return start === null || end === null || (start <= end && end - start < AUDIT_RELATED_DAYS);
}

/**
 * Normalizes the raw URL values; invalid ones fall back to their defaults. Option lists are
 * checked only once known (`null` before the filter options load).
 */
export function resolveAuditView(
  get: (key: string) => string | null,
  options: AuditFilterOptions | null,
): AuditView {
  const term = (get('q') ?? '').trim();
  const rawFrom = get('from');
  const rawTo = get('to');
  const range = validRange(rawFrom, rawTo);
  const known = <T extends string>(value: string | null, list: readonly T[] | null): T | null =>
    value === null || value === ''
      ? null
      : list === null
        ? (value as T)
        : (list.find((item) => item === value) ?? null);
  const actor = get('actor');
  const entity = known(
    get('entity'),
    options === null ? null : options.entityTypes.map((item) => item.code),
  );
  const rawEntityId = get('entityId');
  const pageSize = positiveInteger(get('pageSize'));
  return {
    from: range ? rawFrom || null : null,
    to: range ? rawTo || null : null,
    q: term.length > AUDIT_MAX_SEARCH_LENGTH ? '' : term,
    actor:
      actor === 'system' || (actor !== null && UUID.test(actor))
        ? known(actor, options === null ? null : options.users.map((user) => user.id))
        : null,
    action: known(get('action'), options === null ? null : options.actions.map((a) => a.code)),
    entity,
    entityId:
      entity !== null && rawEntityId !== null && UUID.test(rawEntityId) ? rawEntityId : null,
    branch: known(get('branch'), options === null ? null : options.branches.map((b) => b.id)),
    impact: IMPACTS.find((impact) => impact === get('impact')) ?? null,
    sort: get('sort') === 'impact' ? 'impact' : 'occurredAt',
    direction: get('direction') === 'asc' ? 'asc' : 'desc',
    page: positiveInteger(get('page')) ?? 1,
    pageSize: AUDIT_PAGE_SIZES.find((size) => size === pageSize) ?? AUDIT_PAGE_SIZES[0],
  };
}

/** Canonical query parameters of a view; defaults are null (omitted from the URL). */
export function auditViewParams(view: AuditView): Record<string, string | null> {
  return {
    from: view.from,
    to: view.to,
    q: view.q === '' ? null : view.q,
    actor: view.actor,
    action: view.action,
    entity: view.entity,
    entityId: view.entity === null ? null : view.entityId,
    branch: view.branch,
    impact: view.impact,
    sort: view.sort === DEFAULT_VIEW.sort ? null : view.sort,
    direction: view.direction === DEFAULT_VIEW.direction ? null : view.direction,
    page: view.page === 1 ? null : String(view.page),
    pageSize: view.pageSize === DEFAULT_VIEW.pageSize ? null : String(view.pageSize),
  };
}

/** `true` when the URL already holds exactly the canonical parameters of the view. */
export function isCanonicalAuditUrl(get: (key: string) => string | null, view: AuditView): boolean {
  const canonical = auditViewParams(view);
  return PARAM_KEYS.every((key) => (get(key) ?? null) === canonical[key]);
}

// Target (BR-25)

export interface AuditRoute {
  readonly commands: string[];
  readonly queryParams: Record<string, string> | null;
}

/** BR-25: the frontend owns the route of each target kind; unknown kinds have no link. */
export function auditTargetRoute(target: AuditTarget | null): AuditRoute | null {
  if (target === null) {
    return null;
  }
  switch (target.kind) {
    case 'service_request':
      return { commands: ['/requests'], queryParams: { request: target.id } };
    case 'customer':
      return { commands: ['/customers', target.id], queryParams: null };
    case 'quote':
      return { commands: ['/quotes', target.id], queryParams: null };
    case 'work_order':
      return { commands: ['/jobs', target.id], queryParams: null };
    case 'visit':
      return { commands: ['/schedule'], queryParams: { visit: target.id } };
    case 'invoice':
      return { commands: ['/invoices', target.id], queryParams: null };
    case 'catalog_item':
      return { commands: ['/admin/products-services'], queryParams: null };
    case 'technician_profile':
      return { commands: ['/team'], queryParams: null };
    case 'organization_user':
    case 'user_invitation':
      return { commands: ['/admin/users'], queryParams: null };
    case 'organization':
    case 'branch':
      return { commands: ['/admin/company'], queryParams: null };
    default:
      return null;
  }
}
