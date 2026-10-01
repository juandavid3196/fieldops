import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';
import { Tag } from 'primeng/tag';

import { RecentWorkResponse, RecentWorkType, RegionState } from '../../models/customer.model';
import { formatMoney, sentenceCase } from '../../utils/customer-detail-format';
import { formatDate } from '../../utils/customer-format';

const TYPE_LABELS: Readonly<Record<RecentWorkType, string>> = {
  request: 'Request',
  quote: 'Quote',
  job: 'Job',
};
const TYPE_LINKS: Readonly<Record<RecentWorkType, string>> = {
  request: '/coming-soon/requests',
  quote: '/coming-soon/quotes',
  job: '/coming-soon/work-orders',
};
const TYPE_SEVERITY: Readonly<Record<RecentWorkType, 'success' | 'info' | 'secondary'>> = {
  job: 'success',
  quote: 'info',
  request: 'secondary',
};

/** Recent work table (BR-14). Presentational; links go to Coming soon routes. */
@Component({
  selector: 'app-recent-work-card',
  imports: [RouterLink, ButtonDirective, Message, Skeleton, Tag],
  templateUrl: './recent-work-card.html',
  styleUrl: './recent-work-card.scss',
})
export class RecentWorkCard {
  readonly state = input.required<RegionState<RecentWorkResponse>>();
  readonly retry = output<void>();

  readonly viewAllLink = TYPE_LINKS.job;

  readonly rows = computed(() => {
    const data = this.state().data;
    return (data?.items ?? []).map((item) => ({
      key: `${item.type}-${item.id}`,
      type: TYPE_LABELS[item.type],
      severity: TYPE_SEVERITY[item.type],
      link: TYPE_LINKS[item.type],
      number: item.number,
      title: item.title,
      status: sentenceCase(item.status),
      date: formatDate(item.date, data?.timezone ?? 'UTC'),
      technician: item.technicianName?.trim() || '—',
      amount:
        item.amount === null || item.amount === undefined
          ? '—'
          : formatMoney(item.amount, data?.currency ?? null),
    }));
  });
}
