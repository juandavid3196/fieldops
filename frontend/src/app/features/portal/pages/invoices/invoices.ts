import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { HubPager } from '../../../invoices/components/hub-pager/hub-pager';
import { PortalPropertyFilter } from '../../components/portal-property-filter/portal-property-filter';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalStatusChip } from '../../components/portal-status-chip/portal-status-chip';
import { PortalInvoicesService } from '../../services/portal-invoice.service';
import { dateLabel, money } from '../../utils/portal-format';
import { PortalList } from '../../utils/portal-list';
import { invoiceChip } from '../../utils/portal-status';

/** Invoices list (BR-31): newest issue date first, property-filterable, 20 per page. */
@Component({
  selector: 'app-portal-invoices',
  imports: [
    ButtonDirective,
    HubPager,
    PortalPropertyFilter,
    PortalState,
    PortalStatusChip,
    RouterLink,
  ],
  templateUrl: './invoices.html',
  styleUrls: ['../../portal.scss', '../../portal-table.scss'],
})
export class PortalInvoices {
  private readonly invoices = inject(PortalInvoicesService);

  readonly list = new PortalList((page, propertyId) => this.invoices.list(page, propertyId));
  readonly rows = computed(() =>
    (this.list.resource.data()?.items ?? []).map((invoice) => ({
      invoice,
      issued: dateLabel(invoice.issueDate),
      due: invoice.dueDate === null ? '—' : dateLabel(invoice.dueDate),
      total: money(invoice.total, invoice.currency),
      balance: money(invoice.balanceDue, invoice.currency),
      chip: invoiceChip(invoice.status),
    })),
  );
}
