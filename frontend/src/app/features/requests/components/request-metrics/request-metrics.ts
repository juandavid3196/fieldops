import { Component, computed, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';

import { RequestMetrics } from '../../models/requests.model';

type Tone = 'positive' | 'negative' | 'neutral';

interface MetricView {
  readonly key: string;
  readonly label: string;
  readonly icon: string;
  readonly value: string;
  readonly delta: string;
  readonly direction: 'up' | 'down' | 'flat' | 'none';
  readonly tone: Tone;
  readonly comparison: string;
}

/** BR-07 delta text: whole % (percentage points for the conversion rate, same format); `null` is "—". */
export function deltaText(delta: number | null): string {
  return delta === null ? '—' : `${delta > 0 ? '+' : ''}${delta}%`;
}

function direction(delta: number | null): MetricView['direction'] {
  return delta === null ? 'none' : delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat';
}

function tone(delta: number | null, upIsPositive: boolean): Tone {
  if (delta === null || delta === 0) {
    return 'neutral';
  }
  return delta > 0 === upIsPositive ? 'positive' : 'negative';
}

/** Four metric cards (BR-07): skeletons while loading, "—" with an accessible name on failure. */
@Component({
  selector: 'app-request-metrics',
  imports: [Skeleton],
  templateUrl: './request-metrics.html',
  styleUrl: './request-metrics.scss',
})
export class RequestMetricsCards {
  readonly metrics = input<RequestMetrics | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly errorText = "Couldn't load";

  readonly items = computed<readonly MetricView[]>(() => {
    const data = this.metrics();
    const count = (
      key: string,
      label: string,
      icon: string,
      metric: { value: number; deltaPercent: number | null } | undefined,
      upIsPositive: boolean,
    ): MetricView => ({
      key,
      label,
      icon,
      value: metric === undefined ? '—' : String(metric.value),
      delta: deltaText(metric?.deltaPercent ?? null),
      direction: direction(metric?.deltaPercent ?? null),
      tone: tone(metric?.deltaPercent ?? null, upIsPositive),
      comparison: 'vs. previous week',
    });
    return [
      count('newToday', 'New today', 'pi-file', data?.newToday, true),
      count('awaitingResponse', 'Awaiting response', 'pi-comment', data?.awaitingResponse, false),
      count('assessmentsToday', 'Assessments today', 'pi-calendar', data?.assessmentsToday, true),
      {
        key: 'conversionRate',
        label: 'Conversion rate',
        icon: 'pi-chart-bar',
        value: data === null ? '—' : `${data.conversionRate.value}%`,
        delta: deltaText(data?.conversionRate.deltaPoints ?? null),
        direction: direction(data?.conversionRate.deltaPoints ?? null),
        tone: tone(data?.conversionRate.deltaPoints ?? null, true),
        comparison: 'vs. previous month',
      },
    ];
  });
}
