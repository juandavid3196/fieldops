import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { BusinessKpis } from '../../models/business-overview.model';
import { Delta, delta, money, percentOne, percentWhole } from '../../utils/business-view';
import { DeltaBadge } from '../delta-badge/delta-badge';
import { InfoHint } from '../info-hint/info-hint';

import { t } from '../../../../core/i18n/translate';
interface KpiCard {
  readonly id: string;
  readonly label: string;
  readonly icon: string;
  readonly tone: 'blue' | 'teal';
  readonly hint: string | null;
  readonly value: string;
  readonly delta: Delta | null;
  readonly caption: string | null;
  readonly warning: boolean;
}

export const PROFIT_HINT = (): string => t('businessOverview.businessKpis.profitHint');
export const MARGIN_HINT = (): string => t('businessOverview.businessKpis.marginHint');

/** BR-05 … BR-08, BR-11: the six KPI cards; deltas only with comparison on. */
@Component({
  selector: 'app-business-kpis',
  imports: [TranslocoPipe, DeltaBadge, InfoHint],
  templateUrl: './business-kpis.html',
  styleUrl: './business-kpis.scss',
})
export class BusinessKpisRow {
  readonly kpis = input.required<BusinessKpis>();
  readonly currency = input.required<string>();
  readonly compare = input.required<boolean>();

  readonly cards = computed((): readonly KpiCard[] => {
    const kpis = this.kpis();
    const currency = this.currency();
    const compare = this.compare();
    const change = (current: number, previous: number): Delta | null =>
      compare ? delta(current, previous) : null;
    const overdue = kpis.outstanding.overdueAmount;
    return [
      {
        id: 'revenue',
        label: t('businessOverview.businessKpis.revenue'),
        icon: 'pi-chart-bar',
        tone: 'blue',
        hint: null,
        value: money(kpis.revenue.current, currency),
        delta: change(kpis.revenue.current, kpis.revenue.previous),
        caption: null,
        warning: false,
      },
      {
        id: 'profit',
        label: t('businessOverview.businessKpis.estimatedGrossProfit'),
        icon: 'pi-database',
        tone: 'teal',
        hint: PROFIT_HINT(),
        value: money(kpis.grossProfit.current, currency),
        delta: change(kpis.grossProfit.current, kpis.grossProfit.previous),
        caption: null,
        warning: false,
      },
      {
        id: 'margin',
        label: t('businessOverview.businessKpis.estimatedGrossMargin'),
        icon: 'pi-percentage',
        tone: 'teal',
        hint: MARGIN_HINT(),
        value: percentOne(kpis.grossMarginPercent.current),
        delta: null,
        caption: null,
        warning: false,
      },
      {
        id: 'conversion',
        label: t('businessOverview.businessKpis.quoteConversion'),
        icon: 'pi-file',
        tone: 'blue',
        hint: null,
        value: percentWhole(kpis.quoteConversion.percent),
        delta: null,
        caption: t('businessOverview.businessKpis.acceptedOfSent', {
          accepted: kpis.quoteConversion.accepted,
          sent: kpis.quoteConversion.sent,
        }),
        warning: false,
      },
      {
        id: 'outstanding',
        label: t('businessOverview.businessKpis.outstandingInvoices'),
        icon: 'pi-receipt',
        tone: 'blue',
        hint: null,
        value: money(kpis.outstanding.amount, currency),
        delta: null,
        caption:
          overdue > 0
            ? t('businessOverview.businessKpis.overdue', { value: money(overdue, currency) })
            : t('businessOverview.businessKpis.nothingOverdue'),
        warning: overdue > 0,
      },
      {
        id: 'jobs',
        label: t('businessOverview.businessKpis.jobsCompleted'),
        icon: 'pi-check-circle',
        tone: 'teal',
        hint: null,
        value: String(kpis.jobsCompleted.current),
        delta: change(kpis.jobsCompleted.current, kpis.jobsCompleted.previous),
        caption: null,
        warning: false,
      },
    ];
  });
}
