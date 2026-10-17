import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ActiveVisitSummary } from '../../models/technician-visits.model';
import { formatInstantShortDate, statusLabelOf } from '../../utils/technician-format';

/** BR-17: unfinished past active visits, announced as a status region with Open active visit. */
@Component({
  selector: 'app-unresolved-banner',
  imports: [RouterLink],
  templateUrl: './unresolved-banner.html',
  styleUrl: './unresolved-banner.scss',
})
export class UnresolvedBanner {
  readonly visits = input.required<readonly ActiveVisitSummary[]>();
  readonly timezone = input.required<string>();

  readonly first = computed(() => this.visits()[0]);
  readonly heading = computed(() => {
    const start = this.first().scheduledStart;
    return start === null
      ? 'You have an unfinished job'
      : `You have an unfinished job from ${formatInstantShortDate(start, this.timezone())}`;
  });
  readonly firstLine = computed(() => {
    const visit = this.first();
    return `${visit.title} · #${visit.displayNumber} · ${statusLabelOf(visit.status)}`;
  });
  readonly more = computed(() => this.visits().length - 1);
}
