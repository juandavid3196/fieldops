import { Component, computed, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';

import { CustomerMetrics } from '../../models/customer.model';
import { moneyFormat } from '../../utils/customer-format';

type MetricKey = 'totalCustomers' | 'activeCustomers' | 'newThisMonth' | 'outstandingBalance';

interface Metric {
  readonly key: MetricKey;
  readonly label: string;
  readonly icon: string;
  readonly tone: 'info' | 'success' | 'primary' | 'warning';
}

const METRICS: readonly Metric[] = [
  { key: 'totalCustomers', label: 'Total customers', icon: 'pi-users', tone: 'info' },
  { key: 'activeCustomers', label: 'Active customers', icon: 'pi-user', tone: 'success' },
  { key: 'newThisMonth', label: 'New this month', icon: 'pi-chart-bar', tone: 'primary' },
  { key: 'outstandingBalance', label: 'Outstanding balance', icon: 'pi-dollar', tone: 'warning' },
];

export const METRICS_ERROR_TOOLTIP = "Couldn't load";

/** Four metric cards (BR-05): skeletons while loading, "—" with an accessible name on failure. */
@Component({
  selector: 'app-customer-metrics',
  imports: [Skeleton],
  templateUrl: './customer-metrics.html',
  styleUrl: './customer-metrics.scss',
})
export class CustomerMetricsCards {
  readonly metrics = input<CustomerMetrics | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly items = METRICS;
  readonly errorText = METRICS_ERROR_TOOLTIP;

  private readonly money = computed(() => moneyFormat(this.metrics()?.currency ?? null));

  value(metric: Metric): string {
    const data = this.metrics();
    if (data === null) {
      return '—';
    }
    return metric.key === 'outstandingBalance'
      ? this.money().format(data.outstandingBalance)
      : data[metric.key].toLocaleString('en-US');
  }
}
