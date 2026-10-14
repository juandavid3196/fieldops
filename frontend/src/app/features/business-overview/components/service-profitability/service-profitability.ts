import { Component, computed, input } from '@angular/core';

import { ServiceRow } from '../../models/business-overview.model';
import {
  BELOW_TARGET_TEXT,
  SERVICE_LIMIT,
  belowTarget,
  money,
  percentOne,
} from '../../utils/business-view';
import { MARGIN_COLUMN_HINT } from '../branch-performance/branch-performance';
import { InfoHint } from '../info-hint/info-hint';

export const SERVICES_EMPTY_TEXT = 'No invoiced services in this period.';

/** BR-16: the five best-selling categories with revenue, estimated margin and average job value. */
@Component({
  selector: 'app-service-profitability',
  imports: [InfoHint],
  templateUrl: './service-profitability.html',
  styleUrl: './service-profitability.scss',
})
export class ServiceProfitability {
  readonly services = input.required<readonly ServiceRow[]>();
  readonly currency = input.required<string>();
  readonly target = input.required<number>();

  readonly hint = MARGIN_COLUMN_HINT;
  readonly emptyText = SERVICES_EMPTY_TEXT;
  readonly warningText = BELOW_TARGET_TEXT;

  readonly rows = computed(() =>
    this.services()
      .slice(0, SERVICE_LIMIT)
      .map((service) => ({
        id: service.categoryId,
        name: service.name,
        revenue: money(service.revenue, this.currency()),
        margin: percentOne(service.marginPercent),
        below: belowTarget(service.marginPercent, this.target()),
        average: money(service.averageJobValue, this.currency()),
      })),
  );
}
