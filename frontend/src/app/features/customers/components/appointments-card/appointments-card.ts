import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { Message } from 'primeng/message';
import { Skeleton } from 'primeng/skeleton';

import { AppointmentsResponse, RegionState } from '../../models/customer.model';
import { formatRange } from '../../utils/customer-detail-format';

export const SCHEDULE_LINK = '/schedule';

/** Upcoming appointments (BR-15). Presentational; links go to the dispatch calendar. */
@Component({
  selector: 'app-appointments-card',
  imports: [RouterLink, ButtonDirective, Message, Skeleton],
  templateUrl: './appointments-card.html',
  styleUrl: './appointments-card.scss',
})
export class AppointmentsCard {
  readonly state = input.required<RegionState<AppointmentsResponse>>();
  readonly canMutate = input(false);
  readonly retry = output<void>();

  readonly scheduleLink = SCHEDULE_LINK;

  readonly rows = computed(() => {
    const data = this.state().data;
    return (data?.items ?? []).map((item) => ({
      key: item.visitId,
      range: formatRange(item.startsAt, item.endsAt, data?.timezone ?? 'UTC'),
      job: `${item.jobNumber} · ${item.jobTitle}`,
      property: item.propertyName,
      technician: item.technicianName?.trim() || 'Unassigned',
    }));
  });
}
