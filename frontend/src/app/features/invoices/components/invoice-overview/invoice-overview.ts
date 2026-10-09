import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Skeleton } from 'primeng/skeleton';

import {
  METRICS_ERROR_MESSAGE,
  OverviewResponse,
  PAYMENTS_EMPTY_MESSAGE,
  PAYMENT_METHOD_LABELS,
  Region,
} from '../../models/invoices-hub.model';
import { averageDaysText, formatCalendarDate, money } from '../../utils/invoices-hub-format';

/**
 * Hub overview (BR-21, BR-24): `metrics` renders the five metric cards above the tabs; `summary`
 * renders the aging cards and the recent payments below them. Each part has its own skeleton.
 */
@Component({
  selector: 'app-invoice-overview',
  imports: [ButtonDirective, RouterLink, Skeleton],
  templateUrl: './invoice-overview.html',
  styleUrl: './invoice-overview.scss',
})
export class InvoiceOverview {
  readonly part = input.required<'metrics' | 'summary'>();
  readonly region = input.required<Region<OverviewResponse>>();
  readonly currency = input('USD');

  readonly retry = output<void>();
  readonly viewAll = output<void>();

  readonly errorMessage = METRICS_ERROR_MESSAGE;
  readonly emptyMessage = PAYMENTS_EMPTY_MESSAGE;

  readonly metrics = computed(() => {
    const data = this.region().data;
    if (data === null) {
      return [];
    }
    const { metrics } = data;
    const value = (amount: number): string => money(amount, metrics.currency);
    return [
      { label: 'Outstanding', icon: 'pi-file', tone: 'info', value: value(metrics.outstanding) },
      { label: 'Overdue', icon: 'pi-clock', tone: 'danger', value: value(metrics.overdue) },
      { label: 'Draft', icon: 'pi-file-edit', tone: 'neutral', value: value(metrics.draft) },
      {
        label: 'Paid this month',
        icon: 'pi-credit-card',
        tone: 'success',
        value: value(metrics.paidThisMonth),
      },
      {
        label: 'Average time to pay',
        icon: 'pi-chart-bar',
        tone: 'violet',
        value: averageDaysText(metrics.averageDaysToPay),
      },
    ];
  });

  readonly aging = computed(() => {
    const data = this.region().data;
    if (data === null) {
      return [];
    }
    const value = (amount: number): string => money(amount, this.currency());
    return [
      { label: 'Current', tone: 'success', value: value(data.aging.current) },
      { label: '1 – 30 days', tone: 'warning', value: value(data.aging.days1To30) },
      { label: '31 – 60 days', tone: 'orange', value: value(data.aging.days31To60) },
      { label: '60+ days', tone: 'danger', value: value(data.aging.days60Plus) },
    ];
  });

  readonly recent = computed(() =>
    (this.region().data?.recentPayments ?? []).map((payment) => ({
      payment,
      date: formatCalendarDate(payment.paidDate),
      amount: money(payment.amount, this.currency()),
      method: PAYMENT_METHOD_LABELS[payment.method],
    })),
  );
}
