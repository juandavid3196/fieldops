import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Select } from 'primeng/select';
import { Skeleton } from 'primeng/skeleton';

import {
  ActivityEvent,
  DashboardPeriod,
  DashboardResponse,
  PerformanceResponse,
} from '../../models/dashboard.model';
import { JOBS_LINK } from '../../utils/dashboard-params';
import {
  activityView,
  chartModel,
  durationText,
  percentOf,
  plural,
  varianceText,
  varianceTone,
} from '../../utils/dashboard-view';
import { LoadStatus } from '../../services/dashboard-page.store';

export const ACTIVITY_LIMIT = 4;
export const PERFORMANCE_ERROR_MESSAGE = "We couldn't load performance.";

const PERIODS: { code: DashboardPeriod; label: string }[] = [
  { code: '7', label: 'Last 7 days' },
  { code: '30', label: 'Last 30 days' },
];

/** BR-13 … BR-15: estimated vs actual duration, service performance and live activity. */
@Component({
  selector: 'app-dashboard-analytics',
  imports: [FormsModule, RouterLink, Select, Skeleton],
  templateUrl: './dashboard-analytics.html',
  styleUrl: './dashboard-analytics.scss',
})
export class DashboardAnalytics {
  readonly durations = input.required<DashboardResponse['durations']>();
  readonly activity = input.required<readonly ActivityEvent[]>();
  readonly timezone = input.required<string>();
  readonly performance = input.required<PerformanceResponse | null>();
  readonly performanceStatus = input.required<LoadStatus>();
  readonly period = input.required<DashboardPeriod>();

  readonly periodChange = output<DashboardPeriod>();
  readonly retryPerformance = output<void>();

  readonly periods = PERIODS;
  readonly jobsLink = JOBS_LINK;
  readonly performanceError = PERFORMANCE_ERROR_MESSAGE;

  readonly durationCaption = computed(
    () => `Last 7 days · ${plural(this.durations().visitCount, 'visit', 'visits')}`,
  );
  readonly durationRows = computed(() => {
    const categories = this.durations().categories;
    const max = Math.max(1, ...categories.flatMap((c) => [c.estimatedMinutes, c.actualMinutes]));
    return categories.map((c) => ({
      id: c.categoryId,
      name: c.name,
      estimated: durationText(c.estimatedMinutes),
      actual: durationText(c.actualMinutes),
      // Bars use 80% of the track so the duration label fits after the longest one.
      estimatedWidth: (c.estimatedMinutes / max) * 80,
      actualWidth: (c.actualMinutes / max) * 80,
      variance: varianceText(c.estimatedMinutes, c.actualMinutes),
      varianceTone: varianceTone(c.estimatedMinutes, c.actualMinutes),
    }));
  });

  readonly stats = computed(() => {
    const value = this.performance();
    if (value === null) {
      return null;
    }
    const jobs = percentOf(value.jobsCompleted.completed, value.jobsCompleted.total);
    const fix = percentOf(value.firstTimeFix.fixed, value.firstTimeFix.total);
    return {
      jobsText: `${value.jobsCompleted.completed}/${value.jobsCompleted.total}`,
      jobsPercent: jobs,
      fixPercent: fix,
      rating: value.rating.average === null ? null : value.rating.average.toFixed(1),
      reviews:
        value.rating.count === 0 ? 'No reviews' : plural(value.rating.count, 'review', 'reviews'),
      chart: chartModel(value.daily),
    };
  });
  readonly chartTitle = computed(() => `Jobs completed (last ${this.period()} days)`);

  readonly events = computed(() =>
    this.activity()
      .slice(0, ACTIVITY_LIMIT)
      .map((event, index) => activityView(event, index, this.timezone())),
  );
}
