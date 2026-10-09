import { Component, input } from '@angular/core';

import { TimelineStep } from '../../utils/public-invoice-format';

/** BR-27 timeline: complete/current/pending with text, never colour alone. */
@Component({
  selector: 'app-public-invoice-timeline',
  templateUrl: './public-invoice-timeline.html',
  styleUrl: './public-invoice-timeline.scss',
})
export class PublicInvoiceTimeline {
  readonly steps = input.required<readonly TimelineStep[]>();
}
