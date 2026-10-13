import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { HubPager } from '../../../invoices/components/hub-pager/hub-pager';
import { PortalPropertyFilter } from '../../components/portal-property-filter/portal-property-filter';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalStatusChip } from '../../components/portal-status-chip/portal-status-chip';
import { PortalQuotesService } from '../../services/portal-quote.service';
import { dateLabel, money } from '../../utils/portal-format';
import { PortalList } from '../../utils/portal-list';
import { quoteChip } from '../../utils/portal-status';

/** Quotes list (BR-30): newest sent first, property-filterable, 20 per page. */
@Component({
  selector: 'app-portal-quotes',
  imports: [
    ButtonDirective,
    HubPager,
    PortalPropertyFilter,
    PortalState,
    PortalStatusChip,
    RouterLink,
  ],
  templateUrl: './quotes.html',
  styleUrls: ['../../portal.scss', '../../portal-table.scss'],
})
export class PortalQuotes {
  private readonly quotes = inject(PortalQuotesService);

  readonly list = new PortalList((page, propertyId) => this.quotes.list(page, propertyId));
  readonly rows = computed(() =>
    (this.list.resource.data()?.items ?? []).map((quote) => ({
      quote,
      total: money(quote.total, quote.currency),
      chip: quoteChip(quote.status),
      validUntil: quote.validUntil === null ? '—' : dateLabel(quote.validUntil),
    })),
  );
}
