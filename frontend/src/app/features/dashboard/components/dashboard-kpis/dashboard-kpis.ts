import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';

import { monthDay } from '../../../schedule/utils/schedule-range';
import { DashboardKpis } from '../../models/dashboard.model';
import { AppLink } from '../../utils/dashboard-params';
import { hoursText, onTimeTrend } from '../../utils/dashboard-view';

/** BR-06 … BR-09: the five KPI cards. */
@Component({
  selector: 'app-dashboard-kpis',
  imports: [RouterLink],
  templateUrl: './dashboard-kpis.html',
  styleUrl: './dashboard-kpis.scss',
})
export class DashboardKpisRow {
  readonly kpis = input.required<DashboardKpis>();
  readonly date = input.required<string>();
  readonly isToday = input.required<boolean>();
  readonly boardLink = input.required<AppLink>();

  readonly reviewRequested = output<void>();

  readonly jobsLabel = computed(() =>
    this.isToday() ? "Today's jobs" : `Jobs · ${monthDay(this.date())}`,
  );
  readonly jobsCaption = computed(() => {
    const jobs = this.kpis().jobs;
    return `${jobs.completed} completed · ${jobs.active} active · ${jobs.scheduled} scheduled`;
  });
  readonly utilization = computed(() => {
    const value = this.kpis().utilization;
    return {
      value: value.percent === null ? '—' : `${value.percent}%`,
      caption: `${hoursText(value.scheduledMinutes)} scheduled of ${hoursText(value.availableMinutes)} available`,
    };
  });
  readonly onTime = computed(() => {
    const value = this.kpis().onTimeArrival;
    return {
      value: value.percent === null ? '—' : `${value.percent}%`,
      empty: value.eligible === 0 || value.percent === null,
      trend: onTimeTrend(value.percent, value.previousPercent),
    };
  });
}
