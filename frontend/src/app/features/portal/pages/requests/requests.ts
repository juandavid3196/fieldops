import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { HubPager } from '../../../invoices/components/hub-pager/hub-pager';
import { PortalPropertyFilter } from '../../components/portal-property-filter/portal-property-filter';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalStatusChip } from '../../components/portal-status-chip/portal-status-chip';
import { PortalRequestsService } from '../../services/portal-requests.service';
import { dateLabel } from '../../utils/portal-format';
import { PortalList } from '../../utils/portal-list';
import { requestChip } from '../../utils/portal-status';

/** Requests list (BR-27): newest first, property-filterable, 20 per page. */
@Component({
  selector: 'app-portal-requests',
  imports: [
    ButtonDirective,
    HubPager,
    PortalPropertyFilter,
    PortalState,
    PortalStatusChip,
    RouterLink,
  ],
  templateUrl: './requests.html',
  styleUrls: ['../../portal.scss', '../../portal-table.scss'],
})
export class PortalRequests {
  private readonly requests = inject(PortalRequestsService);

  readonly list = new PortalList((page, propertyId) => this.requests.list(page, propertyId));
  readonly query = computed(() =>
    this.list.propertyId() === null ? {} : { propertyId: this.list.propertyId() },
  );
  readonly rows = computed(() =>
    (this.list.resource.data()?.items ?? []).map((request) => ({
      request,
      chip: requestChip(request.status),
      submitted: dateLabel(request.submittedOn),
    })),
  );
}
