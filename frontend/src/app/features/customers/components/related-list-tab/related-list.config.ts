import { Params } from '@angular/router';

import { STATUS_CHIPS as INVOICE_STATUS_CHIPS } from '../../../invoices/utils/invoices-hub-format';
import { ChipTone, JOB_STATUS_CHIPS } from '../../../jobs/utils/jobs-format';
import { QUOTE_STATUS_CHIPS } from '../../../quotes/utils/quotes-list-format';
import { STATUS_LABELS as REQUEST_STATUS_LABELS } from '../../../requests/models/requests.model';
import {
  RelatedInvoiceItem,
  RelatedItem,
  RelatedJobItem,
  RelatedKind,
  RelatedQuoteItem,
  RelatedRequestItem,
} from '../../models/customer.model';
import { formatMoney } from '../../utils/customer-detail-format';
import {
  formatDate,
  formatDateValue,
  formatMonthDay,
  formatTime,
} from '../../utils/customer-format';
import { recordLink } from '../../utils/record-link';

import { t } from '../../../../core/i18n/translate';

export interface RelatedCell {
  readonly label: string;
  readonly kind: 'primary' | 'text' | 'status' | 'person';
  readonly text: string;
  /** Secondary line under a primary cell (the job title). */
  readonly sub?: string | null;
  readonly tone?: ChipTone;
  readonly initials?: string;
}

export interface RelatedRow {
  readonly id: string;
  /** "<number>, <status>" (accessibility of the row link). */
  readonly ariaLabel: string;
  readonly link: { readonly path: string; readonly query: Params | null };
  readonly cells: readonly RelatedCell[];
}

export interface RowContext {
  readonly timezone: string;
  readonly currency: string;
}

export interface StatusOption {
  readonly code: string;
  readonly label: string;
}

export interface RelatedConfig {
  readonly kind: RelatedKind;
  /** Desktop grid template of the row (`--related-cols`). */
  readonly columns: string;
  readonly statuses: () => readonly StatusOption[];
  readonly toRow: (item: RelatedItem, context: RowContext) => RelatedRow;
}

const DASH = '—';

const REQUEST_TONES: Readonly<Record<string, ChipTone>> = {
  new: 'info',
  needs_review: 'warning',
  assessment_scheduled: 'info',
  ready_for_quote: 'warning',
  quoted: 'purple',
  converted: 'success',
  cancelled: 'neutral',
};

const requestStatus = (code: string): { label: string; tone: ChipTone } => ({
  label: Object.hasOwn(REQUEST_STATUS_LABELS, code)
    ? REQUEST_STATUS_LABELS[code as keyof typeof REQUEST_STATUS_LABELS]
    : code,
  tone: REQUEST_TONES[code] ?? 'neutral',
});

function chipOf<K extends string>(
  chips: Readonly<Record<K, { readonly label: string; readonly tone: string }>>,
  code: string,
): { label: string; tone: ChipTone } {
  return Object.hasOwn(chips, code)
    ? {
        label: chips[code as K].label,
        tone: chips[code as K].tone as ChipTone,
      }
    : { label: code, tone: 'neutral' };
}

function optionsOf<K extends string>(
  chips: Readonly<Record<K, { readonly label: string }>>,
): readonly StatusOption[] {
  return (Object.keys(chips) as K[]).map((code) => ({ code, label: chips[code].label }));
}

function row(
  item: { readonly id: string; readonly number: string },
  link: RelatedRow['link'],
  status: { label: string; tone: ChipTone },
  cells: readonly RelatedCell[],
): RelatedRow {
  return {
    id: item.id,
    ariaLabel: `${item.number}, ${status.label}`,
    link,
    cells: [
      ...cells,
      { label: t('common.status'), kind: 'status', text: status.label, tone: status.tone },
    ],
  };
}

const primary = (label: string, text: string, sub?: string | null): RelatedCell => ({
  label,
  kind: 'primary',
  text,
  sub,
});
const text = (label: string, value: string | null | undefined): RelatedCell => ({
  label,
  kind: 'text',
  text: value?.trim() ? value : DASH,
});

function requestRow(item: RelatedRequestItem, ctx: RowContext): RelatedRow {
  return row(item, recordLink('request', item.id), requestStatus(item.status), [
    primary(t('customers.relatedList.request'), item.number),
    text(t('customers.relatedList.service'), item.service),
    text(t('customers.relatedList.property'), item.propertyName),
    text(t('customers.relatedList.created'), formatDate(item.createdAt, ctx.timezone)),
  ]);
}

function quoteRow(item: RelatedQuoteItem, ctx: RowContext): RelatedRow {
  return row(item, recordLink('quote', item.id), chipOf(QUOTE_STATUS_CHIPS, item.status), [
    primary(t('customers.relatedList.quote'), item.number),
    text(
      t('customers.relatedList.relatedRequest'),
      `${item.request.number} · ${item.request.service}`,
    ),
    text(
      t('common.total'),
      typeof item.total === 'number' ? formatMoney(item.total, ctx.currency) : null,
    ),
    text(
      t('customers.relatedList.expires'),
      item.validUntil ? formatDateValue(item.validUntil, ctx.timezone) : null,
    ),
  ]);
}

function nextVisit(item: RelatedJobItem, zone: string): string | null {
  if (item.completedAt) {
    return t('customers.relatedList.completedOn', { date: formatMonthDay(item.completedAt, zone) });
  }
  return item.nextVisitAt
    ? `${formatMonthDay(item.nextVisitAt, zone)} · ${formatTime(item.nextVisitAt, zone)}`
    : null;
}

function jobRow(item: RelatedJobItem, ctx: RowContext): RelatedRow {
  const label = t('customers.relatedList.assignedTo');
  return row(item, recordLink('job', item.id), chipOf(JOB_STATUS_CHIPS, item.status), [
    primary(t('customers.relatedList.job'), item.number, item.title),
    text(t('customers.relatedList.property'), item.propertyName),
    text(t('customers.relatedList.nextVisit'), nextVisit(item, ctx.timezone)),
    item.technician
      ? { label, kind: 'person', text: item.technician.name, initials: item.technician.initials }
      : text(label, t('customers.relatedList.unassigned')),
  ]);
}

function invoiceRow(item: RelatedInvoiceItem, ctx: RowContext): RelatedRow {
  return row(item, recordLink('invoice', item.id), chipOf(INVOICE_STATUS_CHIPS, item.status), [
    primary(t('customers.relatedList.invoice'), item.number),
    text(t('customers.relatedList.relatedJob'), item.job.number),
    text(
      t('customers.relatedList.issued'),
      item.issueDate ? formatDateValue(item.issueDate, ctx.timezone) : null,
    ),
    text(
      t('customers.relatedList.due'),
      item.dueDate ? formatDateValue(item.dueDate, ctx.timezone) : null,
    ),
    text(t('customers.relatedList.balance'), formatMoney(item.balanceDue, ctx.currency)),
  ]);
}

/** Per-tab columns, status set and row builder (BR-04 to BR-08); labels reuse each module's maps. */
export const TAB_CONFIG: Readonly<Record<RelatedKind, RelatedConfig>> = {
  requests: {
    kind: 'requests',
    columns:
      'minmax(6rem, 0.9fr) minmax(8rem, 1.6fr) minmax(6rem, 1.1fr) minmax(7rem, 1fr) minmax(7rem, 1fr) 1.5rem',
    statuses: () =>
      (Object.keys(REQUEST_STATUS_LABELS) as (keyof typeof REQUEST_STATUS_LABELS)[]).map(
        (code) => ({ code, label: REQUEST_STATUS_LABELS[code] }),
      ),
    toRow: (item, ctx) => requestRow(item as RelatedRequestItem, ctx),
  },
  quotes: {
    kind: 'quotes',
    columns:
      'minmax(6rem, 0.9fr) minmax(9rem, 1.8fr) minmax(5rem, 0.8fr) minmax(7rem, 1fr) minmax(8rem, 1fr) 1.5rem',
    statuses: () => optionsOf(QUOTE_STATUS_CHIPS),
    toRow: (item, ctx) => quoteRow(item as RelatedQuoteItem, ctx),
  },
  jobs: {
    kind: 'jobs',
    columns:
      'minmax(8rem, 1.5fr) minmax(6rem, 1fr) minmax(7rem, 1.1fr) minmax(8rem, 1.2fr) minmax(7rem, 1fr) 1.5rem',
    statuses: () => optionsOf(JOB_STATUS_CHIPS),
    toRow: (item, ctx) => jobRow(item as RelatedJobItem, ctx),
  },
  invoices: {
    kind: 'invoices',
    columns:
      'minmax(7rem, 1.2fr) minmax(5rem, 0.9fr) minmax(6.5rem, 1fr) minmax(6.5rem, 1fr) minmax(5rem, 0.9fr) minmax(7rem, 1fr) 1.5rem',
    statuses: () => optionsOf(INVOICE_STATUS_CHIPS),
    toRow: (item, ctx) => invoiceRow(item as RelatedInvoiceItem, ctx),
  },
};
