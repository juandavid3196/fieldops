import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { RequestRow } from '../../models/portal.model';
import { dateLabel } from '../../utils/portal-format';
import { requestChip } from '../../utils/portal-status';
import { PortalStatusChip } from '../portal-status-chip/portal-status-chip';

/** Active requests card (BR-23): up to three open requests. */
@Component({
  selector: 'app-active-requests-card',
  imports: [ButtonDirective, PortalStatusChip, RouterLink],
  templateUrl: './active-requests-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class ActiveRequestsCard {
  readonly requests = input.required<readonly RequestRow[]>();

  protected readonly rows = computed(() =>
    this.requests()
      .slice(0, 3)
      .map((request) => ({
        request,
        chip: requestChip(request.status),
        submitted: dateLabel(request.submittedOn),
      })),
  );
}
