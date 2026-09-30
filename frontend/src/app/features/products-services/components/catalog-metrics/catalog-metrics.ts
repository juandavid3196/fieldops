import { Component, input } from '@angular/core';
import { Skeleton } from 'primeng/skeleton';
import { TooltipModule } from 'primeng/tooltip';

import { CatalogSummary } from '../../models/catalog.model';

type MetricKey = 'activeItems' | 'activeServices' | 'activeProducts' | 'inactiveItems';

interface Metric {
  readonly key: MetricKey;
  readonly label: string;
  readonly icon: string;
  readonly tone: 'primary' | 'info' | 'success' | 'danger';
}

const METRICS: readonly Metric[] = [
  { key: 'activeItems', label: 'Active items', icon: 'pi-box', tone: 'info' },
  { key: 'activeServices', label: 'Services', icon: 'pi-wrench', tone: 'primary' },
  { key: 'activeProducts', label: 'Products', icon: 'pi-box', tone: 'success' },
  { key: 'inactiveItems', label: 'Inactive', icon: 'pi-ban', tone: 'danger' },
];

export const METRICS_ERROR_TOOLTIP = "Couldn't load";

/** Four metric cards: icon + label + number; skeletons while loading, "—" with a tooltip on failure (BR-14, BR-15). */
@Component({
  selector: 'app-catalog-metrics',
  imports: [Skeleton, TooltipModule],
  templateUrl: './catalog-metrics.html',
  styleUrl: './catalog-metrics.scss',
})
export class CatalogMetrics {
  readonly summary = input<CatalogSummary | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);

  readonly metrics = METRICS;
  readonly errorTooltip = METRICS_ERROR_TOOLTIP;
}
