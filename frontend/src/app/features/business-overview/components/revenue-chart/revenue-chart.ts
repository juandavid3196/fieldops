import { Component, computed, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Granularity, SeriesBucket } from '../../models/business-overview.model';
import {
  CHART_HEIGHT,
  CHART_TABS,
  CHART_WIDTH,
  ChartTab,
  buildChart,
} from '../../utils/chart-geometry';
import { money } from '../../utils/business-view';
import { PROFIT_HINT } from '../business-kpis/business-kpis';
import { InfoHint } from '../info-hint/info-hint';

import { t } from '../../../../core/i18n/translate';
export const CHART_EMPTY_TEXT = (): string => t('businessOverview.revenueChart.chartEmptyText');

/** BR-12: revenue, estimated profit and cash collected as inline SVG with a text alternative. */
@Component({
  selector: 'app-revenue-chart',
  imports: [TranslocoPipe, InfoHint],
  templateUrl: './revenue-chart.html',
  styleUrl: './revenue-chart.scss',
})
export class RevenueChart {
  readonly series = input.required<readonly SeriesBucket[]>();
  readonly granularity = input.required<Granularity>();
  readonly currency = input.required<string>();
  readonly compare = input.required<boolean>();

  readonly tab = signal<ChartTab>('revenue');
  readonly tabs = CHART_TABS;
  get hint(): string {
    return PROFIT_HINT();
  }
  get emptyText(): string {
    return CHART_EMPTY_TEXT();
  }
  readonly viewBox = `0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`;
  readonly width = CHART_WIDTH;

  readonly chart = computed(() =>
    buildChart(this.series(), this.tab(), this.compare(), this.granularity(), this.currency()),
  );
  /** Visually hidden table: one row per bucket with every plotted value. */
  readonly rows = computed(() => {
    const chart = this.chart();
    return chart.buckets.map((label, index) => ({
      label,
      values: chart.lines.map((line) => money(line.values[index], this.currency())),
    }));
  });
}
