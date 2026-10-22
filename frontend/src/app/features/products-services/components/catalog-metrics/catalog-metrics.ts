import { Component, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';
import { TooltipModule } from 'primeng/tooltip';
import { TranslocoPipe } from '@jsverse/transloco';

import { CatalogSummary } from '../../models/catalog.model';

import { localizedList, t } from '../../../../core/i18n/translate';
type MetricKey = 'activeItems' | 'activeServices' | 'activeProducts' | 'inactiveItems';

interface Metric {
  readonly key: MetricKey;
  readonly label: string;
  readonly icon: string;
  readonly tone: 'primary' | 'info' | 'success' | 'danger';
}

const METRICS: readonly Metric[] = localizedList(() => [
  {
    key: 'activeItems',
    label: t('productsServices.catalogMetrics.activeItems'),
    icon: 'pi-box',
    tone: 'info',
  },
  {
    key: 'activeServices',
    label: t('productsServices.catalogMetrics.services'),
    icon: 'pi-wrench',
    tone: 'primary',
  },
  {
    key: 'activeProducts',
    label: t('productsServices.catalogMetrics.products'),
    icon: 'pi-box',
    tone: 'success',
  },
  { key: 'inactiveItems', label: t('common.inactive'), icon: 'pi-ban', tone: 'danger' },
]);

export const METRICS_ERROR_TOOLTIP = (): string =>
  t('productsServices.catalogMetrics.metricsErrorTooltip');

/** Four metric cards: icon + label + number; skeletons while loading, "—" with a tooltip on failure (BR-14, BR-15). */
@Component({
  selector: 'app-catalog-metrics',
  imports: [TranslocoPipe, Skeleton, TooltipModule],
  templateUrl: './catalog-metrics.html',
  styleUrl: './catalog-metrics.scss',
})
export class CatalogMetrics {
  readonly summary = input<CatalogSummary | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly metrics = METRICS;
  get errorTooltip(): string {
    return METRICS_ERROR_TOOLTIP();
  }
}
