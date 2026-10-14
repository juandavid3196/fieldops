export type RangePreset =
  'this_month' | 'last_month' | 'last_30_days' | 'this_quarter' | 'this_year' | 'custom';

export interface BusinessBranch {
  readonly id: string;
  readonly name: string;
}

export interface BusinessOptions {
  readonly timezone: string;
  readonly currency: string;
  readonly today: string;
  readonly marginTarget: number;
  readonly branches: readonly BusinessBranch[];
}

/** The selection kept in the URL (BR-03, BR-04). `compare` never reaches the overview request. */
export interface BusinessRoute {
  readonly range: RangePreset;
  readonly from: string;
  readonly to: string;
  readonly branchId: string | null;
  readonly compare: boolean;
}

export interface BusinessQuery {
  readonly from: string;
  readonly to: string;
  readonly branchId: string | null;
}

export interface Money2 {
  readonly current: number;
  readonly previous: number;
}

export type Granularity = 'day' | 'week' | 'month';

export interface SeriesBucket {
  readonly start: string;
  readonly end: string;
  readonly revenue: number;
  readonly grossProfit: number;
  readonly collected: number;
  readonly previousRevenue: number;
  readonly previousGrossProfit: number;
  readonly previousCollected: number;
}

export interface BusinessKpis {
  readonly revenue: Money2;
  readonly grossProfit: Money2;
  readonly grossMarginPercent: {
    readonly current: number | null;
    readonly previous: number | null;
  };
  readonly quoteConversion: {
    readonly accepted: number;
    readonly sent: number;
    readonly percent: number | null;
  };
  readonly outstanding: {
    readonly amount: number;
    readonly overdueAmount: number;
    readonly overdueCount: number;
  };
  readonly jobsCompleted: Money2;
}

export interface CashFlow {
  readonly invoiced: number;
  readonly collected: number;
  readonly outstanding: number;
  readonly overdue: number;
  readonly aging: {
    readonly current: number;
    readonly days1To30: number;
    readonly days31To60: number;
    readonly days60Plus: number;
  };
}

export interface Pipeline {
  readonly requests: number;
  readonly quotesSent: number;
  readonly accepted: number;
  readonly jobsCreated: number;
}

export interface BranchRow {
  readonly branchId: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly revenue: number;
  readonly grossProfit: number;
  readonly marginPercent: number | null;
  readonly jobsCompleted: number;
  readonly onTimePercent: number | null;
  readonly rating: number | null;
  readonly reviewCount: number;
}

export interface BranchTotals {
  readonly revenue: number;
  readonly marginPercent: number | null;
  readonly jobsCompleted: number;
  readonly onTimePercent: number | null;
}

export interface ServiceRow {
  readonly categoryId: string;
  readonly name: string;
  readonly revenue: number;
  readonly grossProfit: number;
  readonly marginPercent: number | null;
  readonly jobCount: number;
  readonly averageJobValue: number;
}

export interface TeamCapacity {
  readonly technicians: number;
  readonly utilizationPercent: number | null;
  readonly scheduledMinutes: number;
  readonly availableMinutes: number;
  readonly overtimeMinutes: number;
  readonly previousOvertimeMinutes: number;
  readonly next7DaysPercent: number | null;
}

export type AlertType = 'overdue_invoices' | 'branch_margin' | 'service_margin' | 'overtime';

export interface BusinessAlert {
  readonly type: AlertType;
  readonly severity: 'critical' | 'warning';
  readonly count: number | null;
  readonly amount: number | null;
  readonly branchId: string | null;
  readonly categoryId: string | null;
  readonly name: string | null;
  readonly marginPercent: number | null;
  readonly overtimeMinutes: number | null;
  readonly previousOvertimeMinutes: number | null;
  readonly changePercent: number | null;
}

export interface BusinessOverview {
  readonly from: string;
  readonly to: string;
  readonly previousFrom: string;
  readonly previousTo: string;
  readonly timezone: string;
  readonly currency: string;
  readonly marginTarget: number;
  readonly granularity: Granularity;
  readonly kpis: BusinessKpis;
  readonly series: readonly SeriesBucket[];
  readonly cashFlow: CashFlow;
  readonly pipeline: Pipeline;
  readonly branches: readonly BranchRow[];
  readonly branchTotals: BranchTotals | null;
  readonly services: readonly ServiceRow[];
  readonly team: TeamCapacity;
  readonly alerts: readonly BusinessAlert[];
}
