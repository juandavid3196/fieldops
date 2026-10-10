import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { QuoteApproval } from '../../../quotes/pages/quote-approval/quote-approval';
import { QuoteLinkApi } from '../../../quotes/services/quote-link-api';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalQuoteService } from '../../services/portal-quote.service';

/**
 * `/portal/quotes/:quoteId` (BR-30): the existing quote page and actions inside the portal shell,
 * served by the portal quote service (session + id, never a token).
 */
@Component({
  selector: 'app-portal-quote',
  imports: [PortalState, QuoteApproval, RouterLink],
  providers: [PortalQuoteService, { provide: QuoteLinkApi, useExisting: PortalQuoteService }],
  template: `
    <a class="back" routerLink="/portal/quotes">
      <i class="pi pi-arrow-left" aria-hidden="true"></i>
      Back to Quotes
    </a>
    @if (missing()) {
      <app-portal-state state="not-found" backLink="/portal/quotes" backLabel="Back to Quotes" />
    } @else {
      <app-quote-approval [embedded]="true" (missing)="missing.set(true)" />
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 1rem;
      min-inline-size: 0;
    }

    .back {
      align-self: flex-start;
      font-weight: 600;
      color: var(--fo-color-link);
    }
  `,
})
export class PortalQuote {
  readonly missing = signal(false);
}
