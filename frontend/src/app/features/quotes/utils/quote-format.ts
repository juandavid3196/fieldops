import { formatDate, formatTime } from '../../customers/utils/customer-format';
import { initialsOf } from '../../requests/utils/requests-format';
import {
  Calculation,
  CustomerView,
  CustomerViewLine,
  DraftBody,
  QuoteDetail,
  QuoteStatus,
  QuoteVersion,
  TERMS_TEXTS,
  TermsBody,
} from '../models/quote.model';
import { EMAIL_LIMIT } from './quote-lines';

import { localized, t } from '../../../core/i18n/translate';
export const STATUS_LABELS: Readonly<Record<QuoteStatus, string>> = localized(() => ({
  draft: t('quotes.quoteFormat.draft'),
  sent: t('quotes.quoteFormat.sent'),
  clarification_requested: t('quotes.quoteFormat.clarificationRequested'),
  approved: t('quotes.quoteFormat.approved'),
  rejected: t('quotes.quoteFormat.rejected'),
  expired: t('quotes.quoteFormat.expired'),
  cancelled: t('quotes.quoteFormat.cancelled'),
}));

/** The browser time zone: the quote contract carries no organization time zone. */
export function localZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    return 'UTC';
  }
}

/** "MMM d, yyyy" of a calendar date (`YYYY-MM-DD`), never shifted by a time zone. */
export function dateOnlyLabel(value: string): string {
  return formatDate(`${value}T12:00:00Z`, 'UTC');
}

/** "MMM d, yyyy 'at' h:mm a" (BR-08). */
export function dateTimeAt(iso: string, zone: string): string {
  return t('quotes.quoteFormat.at', {
    value: formatDate(iso, zone),
    value2: formatTime(iso, zone),
  });
}

/** Relative save time (BR-08): "Just now" under one minute. */
export function relativeTime(iso: string, now: Date, zone: string): string {
  const elapsed = now.getTime() - new Date(iso).getTime();
  if (Number.isNaN(elapsed) || elapsed < 60_000) {
    return t('quotes.quoteFormat.justNow');
  }
  const minutes = Math.floor(elapsed / 60_000);
  if (minutes < 60) {
    return t('quotes.quoteFormat.minAgo', { minutes: minutes });
  }
  const hours = Math.floor(minutes / 60);
  return hours < 24 ? t('quotes.quoteFormat.hAgo', { hours: hours }) : formatDate(iso, zone);
}

/** BR-21 default email message, truncated to the 320-character limit. */
export function defaultEmailMessage(customerName: string, requestTitle: string): string {
  const first = customerName.trim().split(/\s+/)[0] ?? '';
  const text = t('quotes.quoteFormat.hiHereIsYour', {
    value: first.length > 0 ? first : t('quotes.quoteFormat.there'),
    requestTitle: requestTitle,
  });
  return text.slice(0, EMAIL_LIMIT);
}

/** Stored terms text of a frozen version (or the text of a draft-style terms object). */
export function termsText(terms: QuoteVersion['terms'] | TermsBody): string {
  if (terms === null) {
    return '';
  }
  if (typeof terms === 'string') {
    return terms;
  }
  return terms.preset === 'custom' ? (terms.customText ?? '') : TERMS_TEXTS[terms.preset];
}

export function customerInitials(name: string): string {
  return initialsOf(name) || '?';
}

/**
 * BR-23 preview model from the current form and its latest calculation. Customer-visible fields
 * only: no internal note, unit cost, margin, diagnosis or photos.
 */
export function previewView(
  detail: QuoteDetail,
  body: DraftBody,
  calculation: Calculation,
  versionNo: number,
  today: Date,
): CustomerView {
  const rows = body.lines.map((line, index): CustomerViewLine & { optional: boolean } => ({
    name: line.name,
    description: line.description,
    quantity: line.quantity,
    unit: line.unit,
    unitPrice: line.unitPrice,
    amount: calculation.lines[index]?.lineSubtotal ?? 0,
    optional: line.isOptional,
  }));
  return {
    organizationName: detail.organization.name,
    number: detail.displayNumber,
    versionNo,
    date: formatDate(today.toISOString(), localZone()),
    customerName: detail.customer.name,
    address: detail.customer.address,
    scope: detail.request.title,
    message: body.customerMessage,
    lines: rows.filter((row) => !row.optional),
    optionalLines: rows.filter((row) => row.optional),
    subtotal: calculation.subtotal,
    discountTotal: calculation.discountTotal,
    taxLabel: calculation.taxLabel,
    taxTotal: calculation.taxTotal,
    total: calculation.total,
    currency: calculation.currency,
    terms: termsText(body.terms),
    validUntil: dateOnlyLabel(body.validUntil),
  };
}

/** BR-30 model of a frozen sent version. */
export function versionView(detail: QuoteDetail, version: QuoteVersion): CustomerView {
  const rows = version.lines.map((line) => ({
    name: line.name,
    description: line.description,
    quantity: line.quantity,
    unit: line.unit,
    unitPrice: line.unitPrice,
    amount: line.lineSubtotal,
    optional: line.isOptional,
  }));
  return {
    organizationName: detail.organization.name,
    number: detail.displayNumber,
    versionNo: version.versionNo,
    date: formatDate(version.sentAt, localZone()),
    customerName: detail.customer.name,
    address: detail.customer.address,
    scope: version.scope,
    message: version.customerMessage,
    lines: rows.filter((row) => !row.optional),
    optionalLines: rows.filter((row) => row.optional),
    subtotal: version.subtotal,
    discountTotal: version.discountTotal,
    taxLabel: version.taxLabel,
    taxTotal: version.taxTotal,
    total: version.total,
    currency: version.currency,
    terms: termsText(version.terms),
    validUntil: dateOnlyLabel(version.validUntil),
  };
}
