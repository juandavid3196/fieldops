import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { monthDay } from '../../../schedule/utils/schedule-range';
import { DashboardKpis } from '../../models/dashboard.model';
import { AppLink } from '../../utils/dashboard-params';
import { hoursText, onTimeTrend } from '../../utils/dashboard-view';

import { t } from '../../../../core/i18n/translate';
/** BR-06 … BR-09: the five KPI cards. */
@Component({
  selector: 'app-dashboard-kpis',
  imports: [TranslocoPipe, RouterLink],
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
    this.isToday()
      ? t('dashboard.dashboardKpis.todaysJobs')
      : t('dashboard.dashboardKpis.jobs', { date: monthDay(this.date()) }),
  );
  readonly jobsCaption = computed(() => {
    const jobs = this.kpis().jobs;
    return t('dashboard.dashboardKpis.completedActiveScheduled', {
      completed: jobs.completed,
      active: jobs.active,
      scheduled: jobs.scheduled,
    });
  });
  readonly utilization = computed(() => {
    const value = this.kpis().utilization;
    return {
      value: value.percent === null ? '—' : `${value.percent}%`,
      caption: t('dashboard.dashboardKpis.scheduledOfAvailable', {
        scheduledMinutes: hoursText(value.scheduledMinutes),
        availableMinutes: hoursText(value.availableMinutes),
      }),
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
