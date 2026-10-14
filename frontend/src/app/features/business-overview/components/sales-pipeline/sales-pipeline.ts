import { Component, computed, input } from '@angular/core';

import { Pipeline } from '../../models/business-overview.model';

/** BR-14: four counts in a vertical sequence with icons and labels (not a cohort funnel). */
@Component({
  selector: 'app-sales-pipeline',
  templateUrl: './sales-pipeline.html',
  styleUrl: './sales-pipeline.scss',
})
export class SalesPipeline {
  readonly pipeline = input.required<Pipeline>();

  readonly steps = computed(() => {
    const value = this.pipeline();
    return [
      { id: 'requests', label: 'Requests', icon: 'pi-clipboard', count: value.requests },
      { id: 'sent', label: 'Quotes sent', icon: 'pi-send', count: value.quotesSent },
      { id: 'accepted', label: 'Accepted', icon: 'pi-check-circle', count: value.accepted },
      { id: 'jobs', label: 'Jobs created', icon: 'pi-wrench', count: value.jobsCreated },
    ];
  });
}
