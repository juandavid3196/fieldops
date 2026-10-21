import { formatTime12 } from '../../../shared/utils/time-of-day';
import {
  BUSINESS_HOURS_DAYS,
  BusinessHoursBody,
  BusinessHoursDayName,
  BusinessHoursErrors,
  BusinessHoursRequest,
  BusinessHoursRow,
} from '../models/business-hours.model';

import { t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
/** BR-10: a reopened or closed row proposes 08:00-17:00. */
export const DEFAULT_OPENING = '08:00';
export const DEFAULT_CLOSING = '17:00';

export const REQUIRED_MESSAGE = (): string => t('organizations.businessHoursForm.requiredMessage');
export const CLOSING_AFTER_OPENING_MESSAGE = (): string =>
  t('organizations.businessHoursForm.closingAfterOpeningMessage');

export type BusinessHoursPart = 'openingTime' | 'closingTime';

export interface TimeOption {
  readonly value: string;
  readonly label: string;
}

export function errorKey(day: BusinessHoursDayName, part: BusinessHoursPart): string {
  return `days.${day}.${part}`;
}

/** DOM id of a row control; the inline error uses the `-error` suffix. */
export function controlId(day: BusinessHoursDayName, part: 'toggle' | BusinessHoursPart): string {
  return `business-hours-${day}-${part}`;
}

const STEP_VALUES: readonly string[] = Array.from({ length: 48 }, (_, index) => {
  const hours = String(Math.floor(index / 2)).padStart(2, '0');
  return `${hours}:${index % 2 === 0 ? '00' : '30'}`;
});

function toOption(value: string): TimeOption {
  return { value, label: formatTime12(value) };
}

/**
 * BR-10: 30-minute steps (opening 12:00 AM-11:30 PM, closing 12:30 AM-11:30 PM). A loaded time
 * off the step grid is added as an extra option so it is shown and preserved.
 */
export function timeOptions(part: BusinessHoursPart, loaded: string | null): TimeOption[] {
  const values = part === 'openingTime' ? STEP_VALUES : STEP_VALUES.slice(1);
  const all = loaded !== null && !values.includes(loaded) ? [...values, loaded].sort() : values;
  return all.map(toOption);
}

export function rowsFromBody(body: BusinessHoursBody): BusinessHoursRow[] {
  return BUSINESS_HOURS_DAYS.map((day) => {
    const entry = body.days.find((candidate) => candidate.dayOfWeek === day);
    const isOpen = entry?.isOpen ?? false;
    return {
      day,
      isOpen,
      opening: isOpen ? (entry?.openingTime ?? null) : DEFAULT_OPENING,
      closing: isOpen ? (entry?.closingTime ?? null) : DEFAULT_CLOSING,
    };
  });
}

/** Comparable snapshot; closed rows ignore their placeholder times. */
export function rowsSignature(rows: readonly BusinessHoursRow[]): string {
  return JSON.stringify(
    rows.map((row) => (row.isOpen ? [row.day, true, row.opening, row.closing] : [row.day, false])),
  );
}

export function validateRow(row: BusinessHoursRow): BusinessHoursErrors {
  if (!row.isOpen) {
    return {};
  }
  const errors: Record<string, string> = {};
  if (!row.opening) {
    errors[errorKey(row.day, 'openingTime')] = REQUIRED_MESSAGE();
  }
  if (!row.closing) {
    errors[errorKey(row.day, 'closingTime')] = REQUIRED_MESSAGE();
  } else if (row.opening && row.closing <= row.opening) {
    errors[errorKey(row.day, 'closingTime')] = CLOSING_AFTER_OPENING_MESSAGE();
  }
  return errors;
}

export function validateRows(rows: readonly BusinessHoursRow[]): BusinessHoursErrors {
  return Object.assign({}, ...rows.map(validateRow)) as BusinessHoursErrors;
}

/** FR-06: closed days always send `null` times. */
export function buildRequest(
  rows: readonly BusinessHoursRow[],
  updatedAt: string,
): BusinessHoursRequest {
  return {
    updatedAt,
    days: rows.map((row) => ({
      dayOfWeek: row.day,
      isOpen: row.isOpen,
      openingTime: row.isOpen ? row.opening : null,
      closingTime: row.isOpen ? row.closing : null,
    })),
  };
}

/** Keeps server `days.{day}.openingTime|closingTime` errors; the first message wins per field. */
export function mapServerErrors(
  fieldErrors: Readonly<Record<string, readonly string[]>>,
): BusinessHoursErrors {
  const errors: Record<string, string> = {};
  for (const [key, messages] of Object.entries(fieldErrors)) {
    const match = /^days\.(\w+)\.(openingTime|closingTime)$/i.exec(key);
    if (match !== null && messages.length > 0) {
      const day = match[1].toLowerCase() as BusinessHoursDayName;
      const part: BusinessHoursPart = match[2].toLowerCase().startsWith('o')
        ? 'openingTime'
        : 'closingTime';
      if (BUSINESS_HOURS_DAYS.includes(day)) {
        errors[errorKey(day, part)] = messages[0];
      }
    }
  }
  return errors;
}

/** BR-13: `{IANA id} (GMT±H[:MM])` with the current offset, or "Not set". */
export function timezoneLabel(timezone: string | null, now: Date = new Date()): string {
  if (timezone === null || timezone === '') {
    return t('organizations.businessHoursForm.notSet');
  }
  try {
    const part = new Intl.DateTimeFormat(activeIntlLocale(), {
      timeZone: timezone,
      timeZoneName: 'longOffset',
    })
      .formatToParts(now)
      .find((entry) => entry.type === 'timeZoneName')?.value;
    const match = /^GMT(?:([+-])(\d{2}):(\d{2}))?$/.exec(part ?? '');
    if (match === null) {
      return timezone;
    }
    const sign = match[1] ?? '+';
    const hours = Number(match[2] ?? '0');
    const minutes = match[3] === undefined || match[3] === '00' ? '' : `:${match[3]}`;
    return t('organizations.businessHoursForm.gmt', {
      timezone: timezone,
      sign: sign,
      hours: hours,
      minutes: minutes,
    });
  } catch {
    return timezone;
  }
}
