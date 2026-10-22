import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Pipeline } from '../../models/business-overview.model';

import { t } from '../../../../core/i18n/translate';
/** BR-14: four counts in a vertical sequence with icons and labels (not a cohort funnel). */
@Component({
  selector: 'app-sales-pipeline',
  imports: [TranslocoPipe],
  templateUrl: './sales-pipeline.html',
  styleUrl: './sales-pipeline.scss',
})
export class SalesPipeline {
  readonly pipeline = input.required<Pipeline>();

  readonly steps = computed(() => {
    const value = this.pipeline();
    return [
      {
        id: 'requests',
        label: t('businessOverview.salesPipeline.requests'),
        icon: 'pi-clipboard',
        count: value.requests,
      },
      {
        id: 'sent',
        label: t('businessOverview.salesPipeline.quotesSent'),
        icon: 'pi-send',
        count: value.quotesSent,
      },
      {
        id: 'accepted',
        label: t('businessOverview.salesPipeline.accepted'),
        icon: 'pi-check-circle',
        count: value.accepted,
      },
      {
        id: 'jobs',
        label: t('businessOverview.salesPipeline.jobsCreated'),
        icon: 'pi-wrench',
        count: value.jobsCreated,
      },
    ];
  });
}
