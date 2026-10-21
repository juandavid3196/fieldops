import { hasTranslation, t } from '../../../core/i18n/translate';

interface Template {
  readonly code: string;
  readonly part: 'title' | 'body';
  readonly pattern: RegExp;
  readonly params: readonly string[];
}

/**
 * BR-14: the stored subject and body of a notification are rendered from `template_code` with the
 * values the stored English text carries (the API does not expose the payload). A text that matches
 * no template is shown unchanged (the stored subject).
 */
const TEMPLATES: readonly Template[] = [
  { code: 'request.created', part: 'title', pattern: /^New service request$/, params: [] },
  {
    code: 'request.created',
    part: 'body',
    pattern: /^(.+) submitted (\S+)\.$/,
    params: ['name', 'number'],
  },
  {
    code: 'customer.message_sent',
    part: 'title',
    pattern: /^Customer sent a message$/,
    params: [],
  },
  {
    code: 'customer.message_sent',
    part: 'body',
    pattern: /^(.+) from (.+) sent a message from the customer portal\.$/,
    params: ['contact', 'customer'],
  },
  {
    code: 'quote.approved',
    part: 'title',
    pattern: /^Quote (\S+) was approved$/,
    params: ['number'],
  },
  {
    code: 'quote.approved',
    part: 'body',
    pattern: /^(.+) approved the quote for (.+)\.$/,
    params: ['customer', 'total'],
  },
  {
    code: 'quote.rejected',
    part: 'title',
    pattern: /^Quote (\S+) was declined$/,
    params: ['number'],
  },
  {
    code: 'quote.rejected',
    part: 'body',
    pattern: /^(.+) declined the quote\.$/,
    params: ['customer'],
  },
  {
    code: 'quote.clarification_requested',
    part: 'title',
    pattern: /^Question about quote (\S+)$/,
    params: ['number'],
  },
  {
    code: 'quote.clarification_requested',
    part: 'body',
    pattern: /^(.+) requested a clarification\.$/,
    params: ['customer'],
  },
  { code: 'visit.assigned', part: 'title', pattern: /^Visit assigned$/, params: [] },
  {
    code: 'visit.assigned',
    part: 'body',
    pattern: /^(\S+) was assigned to (.+) for (.+)\.$/,
    params: ['order', 'technician', 'when'],
  },
  { code: 'visit.on_the_way', part: 'title', pattern: /^Technician on the way$/, params: [] },
  {
    code: 'visit.on_the_way',
    part: 'body',
    pattern: /^(.+) is on the way to (\S+)\.$/,
    params: ['technician', 'order'],
  },
  { code: 'visit.started', part: 'title', pattern: /^Visit started$/, params: [] },
  {
    code: 'visit.started',
    part: 'body',
    pattern: /^(.+) started (\S+)\.$/,
    params: ['technician', 'order'],
  },
  { code: 'visit.paused', part: 'title', pattern: /^Visit paused$/, params: [] },
  {
    code: 'visit.paused',
    part: 'body',
    pattern: /^(.+) paused (\S+)\.$/,
    params: ['technician', 'order'],
  },
  { code: 'job.completed', part: 'title', pattern: /^Job completed$/, params: [] },
  {
    code: 'job.completed',
    part: 'body',
    pattern: /^(.+) completed (\S+)\.$/,
    params: ['technician', 'order'],
  },
  { code: 'invoice.sent', part: 'title', pattern: /^Invoice (\S+) was sent$/, params: ['number'] },
  {
    code: 'invoice.sent',
    part: 'body',
    pattern: /^(\S+) was sent to (.+) for (.+)\.$/,
    params: ['number', 'customer', 'total'],
  },
  {
    code: 'invoice.bank_transfer_reported',
    part: 'title',
    pattern: /^Bank transfer reported$/,
    params: [],
  },
  {
    code: 'invoice.bank_transfer_reported',
    part: 'body',
    pattern: /^(.+) reported a bank transfer of (.+) for (\S+)\.$/,
    params: ['customer', 'amount', 'number'],
  },
  { code: 'payment.received', part: 'title', pattern: /^Payment received$/, params: [] },
  {
    code: 'payment.received',
    part: 'body',
    pattern: /^Payment of (.+) was recorded for invoice (\S+)\.$/,
    params: ['amount', 'number'],
  },
];

/** Localized title or body of a notification; the stored text when its template is unknown. */
export function notificationText(
  stored: string,
  eventCode: string,
  part: 'title' | 'body',
): string {
  for (const template of TEMPLATES) {
    if (template.code !== eventCode || template.part !== part) {
      continue;
    }
    const match = template.pattern.exec(stored);
    const key = `notifications.templates.${eventCode}.${part}`;
    if (match !== null && hasTranslation(key)) {
      return t(
        key,
        Object.fromEntries(template.params.map((name, index) => [name, match[index + 1]])),
      );
    }
  }
  return stored;
}
