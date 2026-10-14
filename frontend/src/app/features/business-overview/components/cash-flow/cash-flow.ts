import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { CashFlow } from '../../models/business-overview.model';
import { money } from '../../utils/business-view';

/** BR-13: invoiced, collected, outstanding and overdue plus the receivables aging bars. */
@Component({
  selector: 'app-cash-flow',
  imports: [RouterLink],
  templateUrl: './cash-flow.html',
  styleUrl: './cash-flow.scss',
})
export class CashFlowCard {
  readonly cashFlow = input.required<CashFlow>();
  readonly currency = input.required<string>();

  readonly totals = computed(() => {
    const value = this.cashFlow();
    const currency = this.currency();
    return [
      { id: 'invoiced', label: 'Invoiced', text: money(value.invoiced, currency), warn: false },
      { id: 'collected', label: 'Collected', text: money(value.collected, currency), warn: false },
      {
        id: 'outstanding',
        label: 'Outstanding',
        text: money(value.outstanding, currency),
        warn: false,
      },
      {
        id: 'overdue',
        label: 'Overdue',
        text: money(value.overdue, currency),
        warn: value.overdue > 0,
      },
    ];
  });
  readonly aging = computed(() => {
    const { aging } = this.cashFlow();
    const buckets = [
      { id: 'current', label: 'Current', amount: aging.current },
      { id: '1-30', label: '1 – 30 days', amount: aging.days1To30 },
      { id: '31-60', label: '31 – 60 days', amount: aging.days31To60 },
      { id: '60+', label: '60+ days', amount: aging.days60Plus },
    ];
    const max = Math.max(1, ...buckets.map((bucket) => bucket.amount));
    return buckets.map((bucket) => ({
      ...bucket,
      text: money(bucket.amount, this.currency()),
      width: Math.max(0, (bucket.amount / max) * 100),
    }));
  });
}
