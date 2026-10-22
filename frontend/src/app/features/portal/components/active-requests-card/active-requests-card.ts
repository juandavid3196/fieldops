import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { TranslocoPipe } from '@jsverse/transloco';

import { RequestRow } from '../../models/portal.model';
import { dateLabel } from '../../utils/portal-format';
import { requestChip } from '../../utils/portal-status';
import { PortalStatusChip } from '../portal-status-chip/portal-status-chip';

/** Active requests card (BR-23): the two newest open requests (user layout decision 2026-10-13). */
@Component({
  selector: 'app-active-requests-card',
  imports: [TranslocoPipe, ButtonDirective, PortalStatusChip, RouterLink],
  templateUrl: './active-requests-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss', './active-requests-card.scss'],
})
export class ActiveRequestsCard {
  readonly requests = input.required<readonly RequestRow[]>();

  protected readonly rows = computed(() =>
    this.requests()
      .slice(0, 2)
      .map((request) => ({
        request,
        chip: requestChip(request.status),
        submitted: dateLabel(request.submittedOn),
      })),
  );
}
