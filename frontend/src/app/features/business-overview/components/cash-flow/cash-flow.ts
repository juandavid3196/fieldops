import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { CashFlow } from '../../models/business-overview.model';
import { money } from '../../utils/business-view';

import { t } from '../../../../core/i18n/translate';
/** BR-13: invoiced, collected, outstanding and overdue plus the receivables aging bars. */
@Component({
  selector: 'app-cash-flow',
  imports: [TranslocoPipe, RouterLink],
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
      {
        id: 'invoiced',
        label: t('businessOverview.cashFlow.invoiced'),
        text: money(value.invoiced, currency),
        warn: false,
      },
      {
        id: 'collected',
        label: t('businessOverview.cashFlow.collected'),
        text: money(value.collected, currency),
        warn: false,
      },
      {
        id: 'outstanding',
        label: t('businessOverview.cashFlow.outstanding'),
        text: money(value.outstanding, currency),
        warn: false,
      },
      {
        id: 'overdue',
        label: t('businessOverview.cashFlow.overdue'),
        text: money(value.overdue, currency),
        warn: value.overdue > 0,
      },
    ];
  });
  readonly aging = computed(() => {
    const { aging } = this.cashFlow();
    const buckets = [
      { id: 'current', label: t('businessOverview.cashFlow.current'), amount: aging.current },
      { id: '1-30', label: t('businessOverview.cashFlow.n130Days'), amount: aging.days1To30 },
      { id: '31-60', label: t('businessOverview.cashFlow.n3160Days'), amount: aging.days31To60 },
      { id: '60+', label: t('businessOverview.cashFlow.n60Days'), amount: aging.days60Plus },
    ];
    const max = Math.max(1, ...buckets.map((bucket) => bucket.amount));
    return buckets.map((bucket) => ({
      ...bucket,
      text: money(bucket.amount, this.currency()),
      width: Math.max(0, (bucket.amount / max) * 100),
    }));
  });
}
