import {
  EvidenceKind,
  ISSUE_DATE_MESSAGE,
  LaborVariance,
  LineStatus,
  NOTE_MAX_LENGTH,
  NOTE_TOO_LONG_MESSAGE,
  PaymentTerms,
  TERMS_MESSAGE,
} from '../models/billing-review.model';

import { localized, localizedList, t } from '../../../core/i18n/translate';
import { activeIntlLocale } from '../../../core/i18n/active-locale';
export const TERMS_OPTIONS: readonly { readonly code: PaymentTerms; readonly label: string }[] =
  localizedList(() => [
    { code: 'due_upon_receipt', label: t('billingReview.billingReviewFormat.dueUponReceipt') },
    { code: 'net_15', label: t('billingReview.billingReviewFormat.net15') },
    { code: 'net_30', label: t('billingReview.billingReviewFormat.net30') },
  ]);

const TERMS_DAYS: Readonly<Record<PaymentTerms, number>> = {
  due_upon_receipt: 0,
  net_15: 15,
  net_30: 30,
};

/** Status text and icon (never colour alone, BR-29). */
export const LINE_STATUS: Readonly<
  Record<LineStatus, { readonly label: string; readonly icon: string; readonly tone: string }>
> = localized(() => ({
  matches: {
    label: t('billingReview.billingReviewFormat.matchesQuote'),
    icon: 'pi-check',
    tone: 'ok',
  },
  over: {
    label: t('billingReview.billingReviewFormat.overQuote'),
    icon: 'pi-arrow-up',
    tone: 'warn',
  },
  under: {
    label: t('billingReview.billingReviewFormat.underQuote'),
    icon: 'pi-arrow-down',
    tone: 'info',
  },
  not_in_quote: {
    label: t('billingReview.billingReviewFormat.notInQuote'),
    icon: 'pi-exclamation-triangle',
    tone: 'warn',
  },
  not_tracked: {
    label: t('billingReview.billingReviewFormat.notTracked'),
    icon: 'pi-minus-circle',
    tone: 'muted',
  },
  see_labor_total: {
    label: t('billingReview.billingReviewFormat.seeLaborTotal'),
    icon: 'pi-arrow-down',
    tone: 'muted',
  },
}));

/** `mobile-job-completion` BR-05 acknowledgment method labels. */
export const ACK_METHOD_LABELS: Readonly<Record<string, string>> = localized(() => ({
  signed: t('billingReview.billingReviewFormat.signatureObtained'),
  customer_absent: t('billingReview.billingReviewFormat.customerNotAvailable'),
  customer_refused: t('billingReview.billingReviewFormat.customerDeclinedToSign'),
  remote_confirmation: t('billingReview.billingReviewFormat.remoteConfirmation'),
}));

/** `mobile-job-completion` BR-06 signer relationship labels. */
export const RELATIONSHIP_LABELS: Readonly<Record<string, string>> = localized(() => ({
  customer: t('common.customer'),
  family_member: t('billingReview.billingReviewFormat.familyMember'),
  tenant: t('billingReview.billingReviewFormat.tenant'),
  property_manager: t('billingReview.billingReviewFormat.propertyManager'),
  employee: t('billingReview.billingReviewFormat.employee'),
  other: t('billingReview.billingReviewFormat.other'),
}));

export const EVIDENCE_GROUPS: readonly { readonly type: EvidenceKind; readonly label: string }[] =
  localizedList(() => [
    { type: 'before', label: t('billingReview.billingReviewFormat.before') },
    { type: 'during', label: t('billingReview.billingReviewFormat.during') },
    { type: 'after', label: t('billingReview.billingReviewFormat.after') },
    { type: 'incident', label: t('billingReview.billingReviewFormat.incident') },
    { type: 'other', label: t('billingReview.billingReviewFormat.other') },
  ]);

export function hasVariance(labor: LaborVariance, material: boolean): boolean {
  return labor !== 'none' || material;
}

/** BR-09 badge text. */
export function varianceBadge(labor: LaborVariance, material: boolean): string {
  const hasLabor = labor !== 'none';
  if (hasLabor && material) {
    return t('billingReview.billingReviewFormat.laborAndMaterialVariance');
  }
  if (hasLabor) {
    return t('billingReview.billingReviewFormat.laborVariance');
  }
  return material
    ? t('billingReview.billingReviewFormat.materialVariance')
    : t('billingReview.billingReviewFormat.noVariance');
}

/** Backend amounts are shown, never recalculated. */
export function money(value: number, currency: string): string {
  try {
    return new Intl.NumberFormat(activeIntlLocale(), { style: 'currency', currency }).format(value);
  } catch {
    return value.toFixed(2);
  }
}

function zoned(iso: string, timeZone: string, options: Intl.DateTimeFormatOptions): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  try {
    return new Intl.DateTimeFormat(activeIntlLocale(), { ...options, timeZone }).format(date);
  } catch {
    return new Intl.DateTimeFormat(activeIntlLocale(), { ...options, timeZone: 'UTC' }).format(
      date,
    );
  }
}

/** "MMM d, yyyy, h:mm a" in the organization time zone. */
export function formatDateTime(iso: string, timeZone: string): string {
  return zoned(iso, timeZone, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  });
}

/** "MMM d, yyyy" of an instant in the organization time zone. */
export function formatInstantDate(iso: string, timeZone: string): string {
  return zoned(iso, timeZone, { month: 'short', day: 'numeric', year: 'numeric' });
}

/** "h:mm AM" of an instant in the organization time zone. */
export function formatInstantTime(iso: string, timeZone: string): string {
  return zoned(iso, timeZone, { hour: 'numeric', minute: '2-digit' });
}

/** "MMM d, yyyy" of a calendar date (`yyyy-mm-dd`), never shifted by a time zone. */
export function formatCalendarDate(value: string): string {
  return zoned(`${value}T12:00:00Z`, 'UTC', { month: 'short', day: 'numeric', year: 'numeric' });
}

/** Today's calendar date (`yyyy-mm-dd`) in the organization time zone. */
export function todayIn(timeZone: string, now: Date = new Date()): string {
  try {
    return new Intl.DateTimeFormat('en-CA', {
      timeZone,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(now);
  } catch {
    return now.toISOString().slice(0, 10);
  }
}

export function addDays(date: string, days: number): string {
  const result = new Date(`${date}T12:00:00Z`);
  result.setUTCDate(result.getUTCDate() + days);
  return result.toISOString().slice(0, 10);
}

/** BR-14: due date = issue date + 0, 15 or 30 days (read-only, recalculated on change). */
export function dueDate(issueDate: string, terms: PaymentTerms): string {
  return addDays(issueDate, TERMS_DAYS[terms]);
}

export interface GenerateErrors {
  issueDate?: string;
  paymentTerms?: string;
  note?: string;
}

/** BR-17 client validation; the server decides again. */
export function validateGenerate(
  values: { issueDate: string; paymentTerms: PaymentTerms | null; note: string },
  today: string,
): GenerateErrors {
  const errors: GenerateErrors = {};
  const date = /^\d{4}-\d{2}-\d{2}$/.test(values.issueDate) ? values.issueDate : null;
  if (date === null || date > today || date < addDays(today, -30)) {
    errors.issueDate = ISSUE_DATE_MESSAGE();
  }
  if (!TERMS_OPTIONS.some((option) => option.code === values.paymentTerms)) {
    errors.paymentTerms = TERMS_MESSAGE();
  }
  if (values.note.trim().length > NOTE_MAX_LENGTH) {
    errors.note = NOTE_TOO_LONG_MESSAGE();
  }
  return errors;
}

/** BR-25: "Organization default • <rate>%" for a single rate, else "Quote tax rates". */
export function taxJurisdiction(taxLabel: string): string {
  const rate = /\(([\d.]+)%\)/.exec(taxLabel)?.[1];
  return rate === undefined
    ? t('billingReview.billingReviewFormat.quoteTaxRates')
    : t('billingReview.billingReviewFormat.organizationDefault', { rate: rate });
}
