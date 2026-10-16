import { Component, computed, input } from '@angular/core';

import { ReportChart as ReportChartData } from '../../models/reports.model';
import { ChartUnit } from '../../utils/report-catalog';
import { BarChart, barChart } from '../../utils/report-view';

export const CHART_EMPTY_MESSAGE = 'No data for this report and filters.';

/**
 * Bar charts of the five reports (grouped, stacked, horizontal) drawn with plain HTML/CSS. The
 * plotted values are also available as a visually hidden table (BR-23).
 */
@Component({
  selector: 'app-report-chart',
  templateUrl: './report-chart.html',
  styleUrl: './report-chart.scss',
})
export class ReportChart {
  readonly title = input.required<string>();
  readonly chart = input.required<ReportChartData>();
  readonly unit = input.required<ChartUnit>();
  readonly currency = input.required<string>();
  /** The report has no rows: the chart shows its empty text. */
  readonly noRows = input(false);

  readonly emptyMessage = CHART_EMPTY_MESSAGE;
  readonly model = computed<BarChart>(() => barChart(this.chart(), this.unit(), this.currency()));
  readonly empty = computed(() => this.noRows() || this.model().empty);
}
