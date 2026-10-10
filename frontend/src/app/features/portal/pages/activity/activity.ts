import { Component, inject } from '@angular/core';

import { HubPager } from '../../../invoices/components/hub-pager/hub-pager';
import { PortalActivityTable } from '../../components/portal-activity-table/portal-activity-table';
import { PortalPropertyFilter } from '../../components/portal-property-filter/portal-property-filter';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalDashboardService } from '../../services/portal-dashboard.service';
import { PortalList } from '../../utils/portal-list';

/** Recent activity (BR-26): paginated, 20 per page, property-filterable. */
@Component({
  selector: 'app-portal-activity',
  imports: [HubPager, PortalActivityTable, PortalPropertyFilter, PortalState],
  templateUrl: './activity.html',
  styleUrl: '../../portal.scss',
})
export class PortalActivity {
  private readonly dashboard = inject(PortalDashboardService);

  readonly list = new PortalList((page, propertyId) => this.dashboard.activity(page, propertyId));
}
