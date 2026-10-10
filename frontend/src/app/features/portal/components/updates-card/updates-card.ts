import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PortalUpdate } from '../../models/portal.model';
import { updateTimeLabel } from '../../utils/portal-format';

const TARGET_PATHS = {
  quote: '/portal/quotes',
  appointment: '/portal/appointments',
  invoice: '/portal/invoices',
} as const;

/** Link of the item an update refers to (BR-24). */
export function updateLink(update: PortalUpdate): string[] {
  return [TARGET_PATHS[update.target.kind], update.target.id];
}

/** Messages & updates card (BR-24, BR-25): the latest three events, unread ones with a dot. */
@Component({
  selector: 'app-updates-card',
  imports: [RouterLink],
  templateUrl: './updates-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class UpdatesCard {
  readonly updates = input.required<readonly PortalUpdate[]>();

  protected readonly rows = computed(() => {
    const now = new Date();
    return this.updates()
      .slice(0, 3)
      .map((update) => ({
        update,
        link: updateLink(update),
        time: updateTimeLabel(update.occurredAt, now),
      }));
  });
}
