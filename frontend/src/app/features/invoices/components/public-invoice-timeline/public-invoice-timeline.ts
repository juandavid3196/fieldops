import { Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { TimelineStep } from '../../utils/public-invoice-format';

/** BR-27 timeline: complete/current/pending with text, never colour alone. */
@Component({
  selector: 'app-public-invoice-timeline',
  imports: [TranslocoPipe],
  templateUrl: './public-invoice-timeline.html',
  styleUrl: './public-invoice-timeline.scss',
})
export class PublicInvoiceTimeline {
  readonly steps = input.required<readonly TimelineStep[]>();
}
