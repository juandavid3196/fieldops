import { Component, computed, input } from '@angular/core';

import { VisitTime } from '../../models/technician-visits.model';
import {
  breakSeconds,
  deviationFor,
  formatEstimate,
  formatLabor,
  laborSeconds,
} from '../../utils/job-progress';

/** BR-18 time summary: labor and break time, estimate and deviation chip. */
@Component({
  selector: 'app-job-time',
  templateUrl: './job-time.html',
  styleUrl: './job-time.scss',
})
export class JobTime {
  readonly time = input.required<VisitTime>();
  /** Client clock in ms; the parent refreshes it at least every minute. */
  readonly now = input.required<number>();
  /** "Live" is shown only while the visit is `in_progress`. */
  readonly live = input(false);

  readonly labor = computed(() => laborSeconds(this.time(), this.now()));
  readonly laborText = computed(() => formatLabor(this.labor()));
  readonly breakText = computed(() => formatLabor(breakSeconds(this.time(), this.now())));
  readonly estimateText = computed(() => {
    const minutes = this.time().estimatedMinutes;
    return minutes === null ? null : formatEstimate(minutes);
  });
  readonly deviation = computed(() => deviationFor(this.labor(), this.time().estimatedMinutes));
}
