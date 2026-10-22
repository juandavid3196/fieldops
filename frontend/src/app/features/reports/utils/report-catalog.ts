import { FilterKey, LinkKey, ReportKey, SortDirection } from '../models/reports.model';

import { localized, localizedList, t } from '../../../core/i18n/translate';
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

export const PROFITABILITY_DISCLAIMER = (): string =>
  t('reports.reportCatalog.profitabilityDisclaimer');

const col = (
  key: string,
  label: string,
  type: ColumnType,
  extra: Partial<ReportColumn> = {},
): ReportColumn => ({ key, label, type, sortable: true, ...extra });

const category: FilterSpec = localized(() => ({
  key: 'category',
  label: t('reports.reportCatalog.serviceCategory'),
  allLabel: t('reports.reportCatalog.allCategories'),
  source: 'categories',
}));
const technician: FilterSpec = localized(() => ({
  key: 'technician',
  label: t('reports.reportCatalog.technician'),
  allLabel: t('reports.reportCatalog.allTechnicians'),
  source: 'technicians',
}));

/** BR-01 order, columns (BR-08 … BR-12) and definitions (BR-20). */
export const REPORT_CATALOG: readonly ReportMeta[] = localizedList(() => [
  {
    key: 'job_profitability',
    group: t('reports.reportCatalog.billingPayments'),
    name: t('reports.reportCatalog.jobProfitability'),
    description: t('reports.reportCatalog.compareRevenueEstimatedCosts'),
    noun: t('reports.reportCatalog.nounJobs'),
    chartTitle: t('reports.reportCatalog.revenueVsEstimatedCost'),
    chartUnit: 'money',
    columns: [
      col('job', t('reports.reportCatalog.job'), 'text', { link: 'job' }),
      col('completedAt', t('reports.reportCatalog.completed'), 'instantDate'),
      col('customer', t('common.customer'), 'text', { link: 'customer' }),
      col('service', t('reports.reportCatalog.service'), 'text'),
      col('technician', t('reports.reportCatalog.technician'), 'text', {
        link: 'technician',
        fallback: t('reports.reportCatalog.unassigned'),
      }),
      col('revenue', t('reports.reportCatalog.revenue'), 'money'),
      col('estimatedCost', t('reports.reportCatalog.estCost'), 'money'),
      col('grossProfit', t('reports.reportCatalog.estGrossProfit'), 'money'),
      col('margin', t('reports.reportCatalog.estMargin'), 'percentOne'),
      col('branch', t('reports.reportCatalog.branch'), 'text'),
      col('invoiced', t('reports.reportCatalog.invoicing'), 'invoicing', { sortable: false }),
    ],
    filters: [
      {
        key: 'status',
        label: t('common.status'),
        allLabel: t('reports.reportCatalog.allCompleted'),
        choices: [
          { code: 'completed', label: t('reports.reportCatalog.completed') },
          { code: 'approved_for_billing', label: t('reports.reportCatalog.approvedForBilling') },
        ],
      },
      category,
      technician,
    ],
    defaultSort: 'completedAt',
    defaultDirection: 'desc',
    definitions: [
      {
        term: t('reports.reportCatalog.revenue'),
        text: t('reports.reportCatalog.invoicedAmountOfRecognized'),
      },
      {
        term: t('reports.reportCatalog.estimatedCost'),
        text: t('reports.reportCatalog.quantityConfiguredUnitCost'),
      },
      {
        term: t('reports.reportCatalog.estimatedGrossProfit'),
        text: t('reports.reportCatalog.revenueMinusEstimatedCost'),
      },
      {
        term: t('reports.reportCatalog.estimatedGrossMargin'),
        text: t('reports.reportCatalog.estimatedGrossProfitRevenue'),
      },
      {
        term: t('reports.reportCatalog.notInvoiced'),
        text: t('reports.reportCatalog.completedJobsWithNo'),
      },
    ],
    notes: [PROFITABILITY_DISCLAIMER()],
    asOfToday: false,
  },
  {
    key: 'invoice_aging',
    group: t('reports.reportCatalog.billingPayments'),
    name: t('reports.reportCatalog.invoiceAging'),
    description: t('reports.reportCatalog.reviewOpenBalancesBy'),
    noun: t('reports.reportCatalog.nounInvoices'),
    chartTitle: t('reports.reportCatalog.balanceByAgingBucket'),
    chartUnit: 'money',
    columns: [
      col('invoice', t('reports.reportCatalog.invoice'), 'text', { link: 'invoice' }),
      col('customer', t('common.customer'), 'text', { link: 'customer' }),
      col('job', t('reports.reportCatalog.job'), 'text', { link: 'job', sortable: false }),
      col('issueDate', t('reports.reportCatalog.issueDate'), 'date'),
      col('dueDate', t('reports.reportCatalog.dueDate'), 'date'),
      col('daysOverdue', t('reports.reportCatalog.daysOverdue'), 'count'),
      col('bucket', t('reports.reportCatalog.bucket'), 'label', {
        sortable: false,
        labelKey: 'bucketLabel',
      }),
      col('status', t('common.status'), 'label', { sortable: false, labelKey: 'statusLabel' }),
      col('total', t('common.total'), 'money'),
      col('balanceDue', t('reports.reportCatalog.balanceDue'), 'money'),
      col('branch', t('reports.reportCatalog.branch'), 'text'),
    ],
    filters: [
      {
        key: 'bucket',
        label: t('reports.reportCatalog.bucket'),
        allLabel: t('reports.reportCatalog.allBuckets'),
        choices: [
          { code: 'current', label: t('reports.reportCatalog.current') },
          { code: '1_30', label: t('reports.reportCatalog.n130Days') },
          { code: '31_60', label: t('reports.reportCatalog.n3160Days') },
          { code: '60_plus', label: t('reports.reportCatalog.n60Days') },
        ],
      },
      {
        key: 'status',
        label: t('common.status'),
        allLabel: t('reports.reportCatalog.allStatuses'),
        choices: [
          { code: 'sent', label: t('reports.reportCatalog.sent') },
          { code: 'partially_paid', label: t('reports.reportCatalog.partiallyPaid') },
          { code: 'overdue', label: t('reports.reportCatalog.overdue') },
        ],
      },
    ],
    defaultSort: 'daysOverdue',
    defaultDirection: 'desc',
    definitions: [
      {
        term: t('reports.reportCatalog.outstanding'),
        text: t('reports.reportCatalog.balanceDueOfSent'),
      },
      {
        term: t('reports.reportCatalog.overdue'),
        text: t('reports.reportCatalog.balanceDueOfInvoices'),
      },
      {
        term: t('reports.reportCatalog.current'),
        text: t('reports.reportCatalog.openBalanceThatIs'),
      },
      {
        term: t('reports.reportCatalog.n130Days'),
        text: t('reports.reportCatalog.openBalance1To'),
      },
      {
        term: t('reports.reportCatalog.n3160Days'),
        text: t('reports.reportCatalog.openBalance31To'),
      },
      {
        term: t('reports.reportCatalog.n60Days'),
        text: t('reports.reportCatalog.openBalanceMoreThan'),
      },
    ],
    notes: [],
    asOfToday: true,
  },
  {
    key: 'quote_conversion',
    group: t('reports.reportCatalog.salesQuotes'),
    name: t('reports.reportCatalog.quoteConversion'),
    description: t('reports.reportCatalog.trackQuotesSentAnd'),
    noun: t('reports.reportCatalog.nounQuotes'),
    chartTitle: t('reports.reportCatalog.quotesSentVsAccepted'),
    chartUnit: 'count',
    columns: [
      col('quote', t('reports.reportCatalog.quote'), 'text', { link: 'quote' }),
      col('customer', t('common.customer'), 'text', { link: 'customer' }),
      col('request', t('reports.reportCatalog.request'), 'text', {
        link: 'request',
        sortable: false,
      }),
      col('service', t('reports.reportCatalog.service'), 'text', {
        sortable: false,
        fallback: t('reports.reportCatalog.uncategorized'),
      }),
      col('branch', t('reports.reportCatalog.branch'), 'text', {
        fallback: t('reports.reportCatalog.noBranch'),
      }),
      col('firstSentAt', t('reports.reportCatalog.firstSent'), 'instant', {
        totalType: 'count',
        totalUnit: ' sent',
      }),
      col('acceptedAt', t('reports.reportCatalog.accepted'), 'instant', {
        totalType: 'count',
        totalUnit: ' accepted',
      }),
      col('status', t('common.status'), 'label', { labelKey: 'statusLabel' }),
      col('total', t('common.total'), 'money'),
      col('acceptedValue', t('reports.reportCatalog.acceptedValue'), 'money', { sortable: false }),
    ],
    filters: [
      {
        key: 'event',
        label: t('reports.reportCatalog.event'),
        allLabel: t('reports.reportCatalog.sentAndAccepted'),
        choices: [
          { code: 'sent', label: t('reports.reportCatalog.sent') },
          { code: 'accepted', label: t('reports.reportCatalog.accepted') },
        ],
      },
      {
        key: 'status',
        label: t('common.status'),
        allLabel: t('reports.reportCatalog.allStatuses'),
        choices: [
          { code: 'awaiting_response', label: t('reports.reportCatalog.awaitingResponse') },
          {
            code: 'clarification_requested',
            label: t('reports.reportCatalog.clarificationRequested'),
          },
          { code: 'expired', label: t('reports.reportCatalog.expired') },
          { code: 'approved', label: t('reports.reportCatalog.approved') },
          { code: 'rejected', label: t('reports.reportCatalog.rejected') },
          { code: 'cancelled', label: t('reports.reportCatalog.cancelled') },
        ],
      },
      category,
    ],
    defaultSort: 'firstSentAt',
    defaultDirection: 'desc',
    definitions: [
      {
        term: t('reports.reportCatalog.quotesSent'),
        text: t('reports.reportCatalog.quotesFirstSentIn'),
      },
      {
        term: t('reports.reportCatalog.quotesAccepted'),
        text: t('reports.reportCatalog.quotesApprovedByThe'),
      },
      {
        term: t('reports.reportCatalog.conversion'),
        text: t('reports.reportCatalog.quotesAcceptedQuotesSent'),
      },
    ],
    notes: [t('reports.reportCatalog.conversionCanExceed100')],
    asOfToday: false,
  },
  {
    key: 'jobs_on_time',
    group: t('reports.reportCatalog.operations'),
    name: t('reports.reportCatalog.completedJobsOnTime'),
    description: t('reports.reportCatalog.reviewScheduledVisitsCompletion'),
    noun: t('reports.reportCatalog.nounVisits'),
    chartTitle: t('reports.reportCatalog.arrivalPerformance'),
    chartUnit: 'count',
    columns: [
      col('job', t('reports.reportCatalog.job'), 'text', {
        link: 'visit',
        totalType: 'count',
        totalUnit: ' visits',
      }),
      col('scheduledStart', t('reports.reportCatalog.scheduled'), 'instant'),
      col('customer', t('common.customer'), 'text'),
      col('service', t('reports.reportCatalog.service'), 'text', { sortable: false }),
      col('technician', t('reports.reportCatalog.technician'), 'text', {
        fallback: t('reports.reportCatalog.unassigned'),
      }),
      col('status', t('common.status'), 'label', {
        labelKey: 'statusLabel',
        totalKey: 'statusLabel',
        totalType: 'count',
        totalUnit: ' completed',
      }),
      col('arrivalLimit', t('reports.reportCatalog.arrivalLimit'), 'instant', { sortable: false }),
      col('actualStart', t('reports.reportCatalog.actualStart'), 'instant', { sortable: false }),
      col('onTime', t('reports.reportCatalog.onTimeResult'), 'label', {
        labelKey: 'onTimeLabel',
        totalKey: 'onTimeLabel',
        totalType: 'count',
        totalUnit: t('reports.reportCatalog.onTime'),
      }),
      col('estimatedMinutes', t('reports.reportCatalog.estDuration'), 'minutes', {
        sortable: false,
      }),
      col('actualMinutes', t('reports.reportCatalog.actualDuration'), 'minutes', {
        sortable: false,
      }),
      col('variance', t('reports.reportCatalog.variance'), 'variance', {
        valueKey: 'varianceMinutes',
      }),
      col('branch', t('reports.reportCatalog.branch'), 'text'),
    ],
    filters: [
      {
        key: 'status',
        label: t('common.status'),
        allLabel: t('reports.reportCatalog.allStatuses'),
        choices: [
          { code: 'scheduled', label: t('reports.reportCatalog.scheduled') },
          { code: 'active', label: t('common.active') },
          { code: 'completed', label: t('reports.reportCatalog.completed') },
        ],
      },
      {
        key: 'onTime',
        label: t('reports.reportCatalog.onTimeResult'),
        allLabel: t('reports.reportCatalog.allResults'),
        choices: [
          { code: 'on_time', label: t('reports.reportCatalog.onTime2') },
          { code: 'late', label: t('reports.reportCatalog.late') },
          { code: 'not_started', label: t('reports.reportCatalog.notStarted') },
          { code: 'not_due', label: t('reports.reportCatalog.notDueYet') },
        ],
      },
      category,
      technician,
    ],
    defaultSort: 'scheduledStart',
    defaultDirection: 'desc',
    definitions: [
      {
        term: t('reports.reportCatalog.scheduledVisits'),
        text: t('reports.reportCatalog.visitsScheduledInThe'),
      },
      {
        term: t('reports.reportCatalog.completionRate'),
        text: t('reports.reportCatalog.completedVisitsScheduledVisits'),
      },
      {
        term: t('reports.reportCatalog.onTimeArrival'),
        text: t('reports.reportCatalog.startedByTheArrival'),
      },
      {
        term: t('reports.reportCatalog.durationVariance'),
        text: t('reports.reportCatalog.actualDurationMinusEstimated'),
      },
      {
        term: t('reports.reportCatalog.jobsCompleted'),
        text: t('reports.reportCatalog.workOrdersCompletedIn'),
      },
    ],
    notes: [],
    asOfToday: false,
  },
  {
    key: 'technician_utilization',
    group: t('reports.reportCatalog.operations'),
    name: t('reports.reportCatalog.technicianUtilization'),
    description: t('reports.reportCatalog.compareAvailableAndScheduled'),
    noun: t('reports.reportCatalog.nounTechnicians'),
    chartTitle: t('reports.reportCatalog.utilizationByTechnician'),
    chartUnit: 'percent',
    columns: [
      col('technician', t('reports.reportCatalog.technician'), 'text', { link: 'technician' }),
      col('branch', t('reports.reportCatalog.branch'), 'text'),
      col('availableMinutes', t('reports.reportCatalog.availableH'), 'hours'),
      col('scheduledMinutes', t('reports.reportCatalog.scheduledH'), 'hours'),
      col('utilization', t('reports.reportCatalog.utilization'), 'percentWhole'),
      col('overtimeMinutes', t('reports.reportCatalog.overtimeH'), 'hours'),
      col('completedVisits', t('reports.reportCatalog.completedVisits'), 'count'),
      col('onTime', t('reports.reportCatalog.onTimeArrival'), 'percentWhole'),
    ],
    filters: [technician],
    defaultSort: 'utilization',
    defaultDirection: 'desc',
    definitions: [
      {
        term: t('reports.reportCatalog.availableHours'),
        text: t('reports.reportCatalog.workingHoursFromWeekly'),
      },
      {
        term: t('reports.reportCatalog.scheduledHours'),
        text: t('reports.reportCatalog.hoursOfScheduledVisits'),
      },
      {
        term: t('reports.reportCatalog.utilization'),
        text: t('reports.reportCatalog.scheduledHoursAvailableHours'),
      },
      {
        term: t('reports.reportCatalog.scheduledOvertime'),
        text: t('reports.reportCatalog.scheduledHoursBeyondThe'),
      },
    ],
    notes: [],
    asOfToday: false,
  },
]);

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
