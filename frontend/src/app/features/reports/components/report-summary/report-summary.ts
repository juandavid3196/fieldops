import { Component, computed, input } from '@angular/core';

import { DeltaBadge } from '../../../business-overview/components/delta-badge/delta-badge';
import { Metric } from '../../models/reports.model';
import { SummaryCard, summaryCards } from '../../utils/report-view';

const POSITIVE = new Set(['revenue', 'estimatedGrossProfit', 'sentValue', 'acceptedValue']);
const NEGATIVE = new Set(['estimatedCost', 'overdue', 'overdueInvoices', 'overtimeHours']);

/** BR-06: summary cards with the optional previous-period comparison (never for invoice aging). */
@Component({
  selector: 'app-report-summary',
  imports: [DeltaBadge],
  templateUrl: './report-summary.html',
  styleUrl: './report-summary.scss',
})
export class ReportSummary {
  readonly metrics = input.required<readonly Metric[]>();
  readonly currency = input.required<string>();
  /** Comparison is on and applies to the report. */
  readonly compare = input(false);
  readonly percentDecimals = input<0 | 1>(0);
  readonly otherCurrencyCount = input<number | null>(null);

  /** Money in and out read green and orange; every other figure keeps the heading color. */
  tone(key: string): 'positive' | 'negative' | null {
    return POSITIVE.has(key) ? 'positive' : NEGATIVE.has(key) ? 'negative' : null;
  }

  readonly cards = computed<SummaryCard[]>(() =>
    summaryCards(this.metrics(), {
      currency: this.currency(),
      compare: this.compare(),
      percentDecimals: this.percentDecimals(),
    }),
  );
}
