import { Component, computed, inject, input, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { Menu } from 'primeng/menu';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { TranslocoPipe } from '@jsverse/transloco';

import { formatInstant, monthDay } from '../../../schedule/utils/schedule-range';
import { DayVisit } from '../../models/dashboard.model';
import { AppLink, JOBS_LINK, jobLink, visitLink } from '../../utils/dashboard-params';
import {
  rowAction,
  sortVisits,
  statusChip,
  technicianLabel,
  timing,
} from '../../utils/dashboard-view';

import { t } from '../../../../core/i18n/translate';
export const OPERATION_PAGE_SIZE = 8;

/** BR-10: the selected day's visits with status, time, job, customer, technician, timing, actions. */
@Component({
  selector: 'app-dashboard-operation',
  imports: [TranslocoPipe, Menu, Paginator, RouterLink],
  templateUrl: './dashboard-operation.html',
  styleUrl: './dashboard-operation.scss',
})
export class DashboardOperation {
  private readonly router = inject(Router);

  readonly visits = input.required<readonly DayVisit[]>();
  readonly date = input.required<string>();
  readonly isToday = input.required<boolean>();
  readonly timezone = input.required<string>();
  readonly boardLink = input.required<AppLink>();

  readonly jobsLink = JOBS_LINK;
  readonly pageSize = OPERATION_PAGE_SIZE;
  readonly descending = signal(false);
  readonly first = signal(0);
  readonly menuModel = signal<MenuItem[]>([]);
  private readonly menu = viewChild.required<Menu>('menu');

  readonly title = computed(() =>
    this.isToday()
      ? t('dashboard.dashboardOperation.todaysOperation')
      : t('dashboard.dashboardOperation.operation', { date: monthDay(this.date()) }),
  );
  readonly rows = computed(() => {
    const zone = this.timezone();
    const date = this.date();
    const sorted = sortVisits(this.visits(), this.descending());
    const start = Math.min(this.first(), Math.max(0, sorted.length - 1));
    return sorted.slice(start, start + OPERATION_PAGE_SIZE).map((visit) => ({
      visit,
      time: formatInstant(visit.scheduledStart, zone),
      chip: statusChip(visit),
      timing: timing(visit, zone),
      action: rowAction(visit, date),
      technician: technicianLabel(visit),
      job: jobLink(visit.workOrderId),
    }));
  });

  toggleSort(): void {
    this.descending.update((value) => !value);
    this.first.set(0);
  }

  onPage(event: PaginatorState): void {
    this.first.set(event.first ?? 0);
  }

  openMenu(event: Event, visit: DayVisit): void {
    const go = (link: AppLink) => () =>
      void this.router.navigate([...link.path], { queryParams: link.queryParams });
    this.menuModel.set([
      {
        label: t('dashboard.dashboardOperation.viewJob'),
        icon: 'pi pi-eye',
        command: go(jobLink(visit.workOrderId)),
      },
      {
        label: t('dashboard.dashboardOperation.openInSchedule'),
        icon: 'pi pi-calendar',
        command: go(visitLink(visit.branchId, this.date(), visit.visitId)),
      },
    ]);
    this.menu().toggle(event);
  }
}
