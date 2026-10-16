import { FilterKey, LinkKey, ReportKey, SortDirection } from '../models/reports.model';

export type ColumnType =
  | 'text'
  | 'date'
  | 'instantDate'
  | 'instant'
  | 'money'
  | 'percentOne'
  | 'percentWhole'
  | 'count'
  | 'hours'
  | 'minutes'
  | 'variance'
  | 'label'
  | 'invoicing';

export interface ReportColumn {
  /** Row/totals property; also the `sort` value when the column is sortable. */
  readonly key: string;
  readonly label: string;
  readonly type: ColumnType;
  readonly sortable: boolean;
  readonly link?: LinkKey;
  /** Row property shown when it differs from `key` (the sort key), e.g. `varianceMinutes`. */
  readonly valueKey?: string;
  /** Row property holding the display label of a code (`statusLabel`). */
  readonly labelKey?: string;
  /** Totals property when it differs from `key`. */
  readonly totalKey?: string;
  /** Text shown for a `null` value (default "—"). */
  readonly fallback?: string;
  /** Totals cell format when it differs from the column type, and its unit suffix. */
  readonly totalType?: ColumnType;
  readonly totalUnit?: string;
}

export interface FilterChoice {
  readonly code: string;
  readonly label: string;
}

export interface FilterSpec {
  readonly key: FilterKey;
  readonly label: string;
  readonly allLabel: string;
  readonly choices?: readonly FilterChoice[];
  readonly source?: 'categories' | 'technicians';
}

export interface Definition {
  readonly term: string;
  readonly text: string;
}

export type ChartUnit = 'money' | 'count' | 'percent';

export interface ReportMeta {
  readonly key: ReportKey;
  readonly group: string;
  readonly name: string;
  readonly description: string;
  /** Plural noun of the footer and the announcement. */
  readonly noun: string;
  readonly chartTitle: string;
  readonly chartUnit: ChartUnit;
  readonly columns: readonly ReportColumn[];
  readonly filters: readonly FilterSpec[];
  readonly defaultSort: string;
  readonly defaultDirection: SortDirection;
  readonly definitions: readonly Definition[];
  /** Extra definition text shown after the terms (BR-20). */
  readonly notes: readonly string[];
  /** BR-09: the report ignores the period and the comparison. */
  readonly asOfToday: boolean;
}

export const PROFITABILITY_DISCLAIMER =
  'Profitability figures are operational estimates based on configured product and service costs. They exclude overhead, payroll, payroll taxes, depreciation, and other accounting expenses. They are not net income and not a substitute for accounting software.';

const col = (
  key: string,
  label: string,
  type: ColumnType,
  extra: Partial<ReportColumn> = {},
): ReportColumn => ({ key, label, type, sortable: true, ...extra });

const category: FilterSpec = {
  key: 'category',
  label: 'Service category',
  allLabel: 'All categories',
  source: 'categories',
};
const technician: FilterSpec = {
  key: 'technician',
  label: 'Technician',
  allLabel: 'All technicians',
  source: 'technicians',
};

/** BR-01 order, columns (BR-08 … BR-12) and definitions (BR-20). */
export const REPORT_CATALOG: readonly ReportMeta[] = [
  {
    key: 'job_profitability',
    group: 'Billing & payments',
    name: 'Job profitability',
    description: 'Compare revenue, estimated costs, and estimated gross margin for completed jobs.',
    noun: 'jobs',
    chartTitle: 'Revenue vs estimated cost by service category',
    chartUnit: 'money',
    columns: [
      col('job', 'Job', 'text', { link: 'job' }),
      col('completedAt', 'Completed', 'instantDate'),
      col('customer', 'Customer', 'text', { link: 'customer' }),
      col('service', 'Service', 'text'),
      col('technician', 'Technician', 'text', { link: 'technician', fallback: 'Unassigned' }),
      col('revenue', 'Revenue', 'money'),
      col('estimatedCost', 'Est. cost', 'money'),
      col('grossProfit', 'Est. gross profit', 'money'),
      col('margin', 'Est. margin', 'percentOne'),
      col('branch', 'Branch', 'text'),
      col('invoiced', 'Invoicing', 'invoicing', { sortable: false }),
    ],
    filters: [
      {
        key: 'status',
        label: 'Status',
        allLabel: 'All completed',
        choices: [
          { code: 'completed', label: 'Completed' },
          { code: 'approved_for_billing', label: 'Approved for billing' },
        ],
      },
      category,
      technician,
    ],
    defaultSort: 'completedAt',
    defaultDirection: 'desc',
    definitions: [
      {
        term: 'Revenue',
        text: 'Invoiced amount of recognized invoices for jobs completed in the period, net of discounts, excluding tax. Revenue is grouped by job completion, so it can differ from revenue by invoice date in Business Overview.',
      },
      {
        term: 'Estimated cost',
        text: 'Quantity × configured unit cost of the product or service on each invoice line.',
      },
      { term: 'Estimated gross profit', text: 'Revenue minus estimated cost.' },
      { term: 'Estimated gross margin', text: 'Estimated gross profit ÷ revenue × 100.' },
      {
        term: 'Not invoiced',
        text: 'Completed jobs with no issued invoice yet. They count as jobs but add no revenue or cost.',
      },
    ],
    notes: [PROFITABILITY_DISCLAIMER],
    asOfToday: false,
  },
  {
    key: 'invoice_aging',
    group: 'Billing & payments',
    name: 'Invoice aging',
    description: 'Review open balances by days past due as of today.',
    noun: 'invoices',
    chartTitle: 'Balance by aging bucket',
    chartUnit: 'money',
    columns: [
      col('invoice', 'Invoice', 'text', { link: 'invoice' }),
      col('customer', 'Customer', 'text', { link: 'customer' }),
      col('job', 'Job', 'text', { link: 'job', sortable: false }),
      col('issueDate', 'Issue date', 'date'),
      col('dueDate', 'Due date', 'date'),
      col('daysOverdue', 'Days overdue', 'count'),
      col('bucket', 'Bucket', 'label', { sortable: false, labelKey: 'bucketLabel' }),
      col('status', 'Status', 'label', { sortable: false, labelKey: 'statusLabel' }),
      col('total', 'Total', 'money'),
      col('balanceDue', 'Balance due', 'money'),
      col('branch', 'Branch', 'text'),
    ],
    filters: [
      {
        key: 'bucket',
        label: 'Bucket',
        allLabel: 'All buckets',
        choices: [
          { code: 'current', label: 'Current' },
          { code: '1_30', label: '1–30 days' },
          { code: '31_60', label: '31–60 days' },
          { code: '60_plus', label: '60+ days' },
        ],
      },
      {
        key: 'status',
        label: 'Status',
        allLabel: 'All statuses',
        choices: [
          { code: 'sent', label: 'Sent' },
          { code: 'partially_paid', label: 'Partially paid' },
          { code: 'overdue', label: 'Overdue' },
        ],
      },
    ],
    defaultSort: 'daysOverdue',
    defaultDirection: 'desc',
    definitions: [
      {
        term: 'Outstanding',
        text: 'Balance due of sent and partially paid invoices, as of today.',
      },
      { term: 'Overdue', text: 'Balance due of invoices past their due date.' },
      { term: 'Current', text: 'Open balance that is not past due.' },
      { term: '1–30 days', text: 'Open balance 1 to 30 days past due.' },
      { term: '31–60 days', text: 'Open balance 31 to 60 days past due.' },
      { term: '60+ days', text: 'Open balance more than 60 days past due.' },
    ],
    notes: [],
    asOfToday: true,
  },
  {
    key: 'quote_conversion',
    group: 'Sales & quotes',
    name: 'Quote conversion',
    description: 'Track quotes sent and accepted in the period.',
    noun: 'quotes',
    chartTitle: 'Quotes sent vs accepted',
    chartUnit: 'count',
    columns: [
      col('quote', 'Quote', 'text', { link: 'quote' }),
      col('customer', 'Customer', 'text', { link: 'customer' }),
      col('request', 'Request', 'text', { link: 'request', sortable: false }),
      col('service', 'Service', 'text', { sortable: false, fallback: 'Uncategorized' }),
      col('branch', 'Branch', 'text', { fallback: 'No branch' }),
      col('firstSentAt', 'First sent', 'instant', { totalType: 'count', totalUnit: ' sent' }),
      col('acceptedAt', 'Accepted', 'instant', { totalType: 'count', totalUnit: ' accepted' }),
      col('status', 'Status', 'label', { labelKey: 'statusLabel' }),
      col('total', 'Total', 'money'),
      col('acceptedValue', 'Accepted value', 'money', { sortable: false }),
    ],
    filters: [
      {
        key: 'event',
        label: 'Event',
        allLabel: 'Sent and accepted',
        choices: [
          { code: 'sent', label: 'Sent' },
          { code: 'accepted', label: 'Accepted' },
        ],
      },
      {
        key: 'status',
        label: 'Status',
        allLabel: 'All statuses',
        choices: [
          { code: 'awaiting_response', label: 'Awaiting response' },
          { code: 'clarification_requested', label: 'Clarification requested' },
          { code: 'expired', label: 'Expired' },
          { code: 'approved', label: 'Approved' },
          { code: 'rejected', label: 'Rejected' },
          { code: 'cancelled', label: 'Cancelled' },
        ],
      },
      category,
    ],
    defaultSort: 'firstSentAt',
    defaultDirection: 'desc',
    definitions: [
      { term: 'Quotes sent', text: 'Quotes first sent in the period.' },
      { term: 'Quotes accepted', text: 'Quotes approved by the customer in the period.' },
      { term: 'Conversion', text: 'Quotes accepted ÷ quotes sent × 100.' },
    ],
    notes: [
      'Conversion can exceed 100% when quotes sent in earlier periods are accepted in this one.',
    ],
    asOfToday: false,
  },
  {
    key: 'jobs_on_time',
    group: 'Operations',
    name: 'Completed jobs & on-time performance',
    description: 'Review scheduled visits, completion, on-time arrival, and duration.',
    noun: 'visits',
    chartTitle: 'Arrival performance',
    chartUnit: 'count',
    columns: [
      col('job', 'Job', 'text', { link: 'visit', totalType: 'count', totalUnit: ' visits' }),
      col('scheduledStart', 'Scheduled', 'instant'),
      col('customer', 'Customer', 'text'),
      col('service', 'Service', 'text', { sortable: false }),
      col('technician', 'Technician', 'text', { fallback: 'Unassigned' }),
      col('status', 'Status', 'label', {
        labelKey: 'statusLabel',
        totalKey: 'statusLabel',
        totalType: 'count',
        totalUnit: ' completed',
      }),
      col('arrivalLimit', 'Arrival limit', 'instant', { sortable: false }),
      col('actualStart', 'Actual start', 'instant', { sortable: false }),
      col('onTime', 'On-time result', 'label', {
        labelKey: 'onTimeLabel',
        totalKey: 'onTimeLabel',
        totalType: 'count',
        totalUnit: ' on time',
      }),
      col('estimatedMinutes', 'Est. duration', 'minutes', { sortable: false }),
      col('actualMinutes', 'Actual duration', 'minutes', { sortable: false }),
      col('variance', 'Variance', 'variance', { valueKey: 'varianceMinutes' }),
      col('branch', 'Branch', 'text'),
    ],
    filters: [
      {
        key: 'status',
        label: 'Status',
        allLabel: 'All statuses',
        choices: [
          { code: 'scheduled', label: 'Scheduled' },
          { code: 'active', label: 'Active' },
          { code: 'completed', label: 'Completed' },
        ],
      },
      {
        key: 'onTime',
        label: 'On-time result',
        allLabel: 'All results',
        choices: [
          { code: 'on_time', label: 'On time' },
          { code: 'late', label: 'Late' },
          { code: 'not_started', label: 'Not started' },
          { code: 'not_due', label: 'Not due yet' },
        ],
      },
      category,
      technician,
    ],
    defaultSort: 'scheduledStart',
    defaultDirection: 'desc',
    definitions: [
      {
        term: 'Scheduled visits',
        text: 'Visits scheduled in the period, excluding cancelled and unscheduled visits.',
      },
      { term: 'Completion rate', text: 'Completed visits ÷ scheduled visits × 100.' },
      {
        term: 'On-time arrival',
        text: 'Started by the arrival window end, or within 15 minutes of the scheduled start.',
      },
      {
        term: 'Duration variance',
        text: 'Actual duration minus estimated duration, for completed visits with both.',
      },
      { term: 'Jobs completed', text: 'Work orders completed in the period.' },
    ],
    notes: [],
    asOfToday: false,
  },
  {
    key: 'technician_utilization',
    group: 'Operations',
    name: 'Technician utilization',
    description: 'Compare available and scheduled hours, overtime, and delivery by technician.',
    noun: 'technicians',
    chartTitle: 'Utilization by technician',
    chartUnit: 'percent',
    columns: [
      col('technician', 'Technician', 'text', { link: 'technician' }),
      col('branch', 'Branch', 'text'),
      col('availableMinutes', 'Available h', 'hours'),
      col('scheduledMinutes', 'Scheduled h', 'hours'),
      col('utilization', 'Utilization', 'percentWhole'),
      col('overtimeMinutes', 'Overtime h', 'hours'),
      col('completedVisits', 'Completed visits', 'count'),
      col('onTime', 'On-time arrival', 'percentWhole'),
    ],
    filters: [technician],
    defaultSort: 'utilization',
    defaultDirection: 'desc',
    definitions: [
      {
        term: 'Available hours',
        text: 'Working hours from weekly availability, minus breaks and time off.',
      },
      { term: 'Scheduled hours', text: 'Hours of scheduled visits and assessments.' },
      { term: 'Utilization', text: 'Scheduled hours ÷ available hours × 100.' },
      {
        term: 'Scheduled overtime',
        text: 'Scheduled hours beyond the available hours of each day.',
      },
    ],
    notes: [],
    asOfToday: false,
  },
];

const ALL_KEYS: readonly ReportKey[] = REPORT_CATALOG.map((report) => report.key);

/** BR-02 (UX only): the reports a role may see; the backend decides. */
const ROLE_REPORTS: Readonly<Record<string, readonly ReportKey[]>> = {
  owner: ALL_KEYS,
  operations_manager: ALL_KEYS,
  dispatcher: ['quote_conversion', 'jobs_on_time', 'technician_utilization'],
  accounting: ['job_profitability', 'invoice_aging'],
};

export function visibleKeysForRole(role: string | undefined): readonly ReportKey[] {
  return role !== undefined && Object.hasOwn(ROLE_REPORTS, role) ? ROLE_REPORTS[role] : [];
}

export function isReportKey(value: string | null): value is ReportKey {
  return ALL_KEYS.some((key) => key === value);
}

export function reportMeta(key: ReportKey): ReportMeta {
  return REPORT_CATALOG.find((report) => report.key === key) as ReportMeta;
}

export function sortKeys(meta: ReportMeta): readonly string[] {
  return meta.columns.filter((column) => column.sortable).map((column) => column.key);
}

/** BR-05 URL parameter of each filter; the API uses `categoryId` and `technicianId`. */
export const FILTER_KEYS: readonly FilterKey[] = [
  'status',
  'category',
  'technician',
  'bucket',
  'event',
  'onTime',
];

export const FILTER_API_NAMES: Readonly<Record<FilterKey, string>> = {
  status: 'status',
  category: 'categoryId',
  technician: 'technicianId',
  bucket: 'bucket',
  event: 'event',
  onTime: 'onTime',
};
