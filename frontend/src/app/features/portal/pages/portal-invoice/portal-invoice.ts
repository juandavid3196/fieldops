import { Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PublicInvoicePage } from '../../../invoices/pages/public-invoice/public-invoice';
import { InvoiceLinkApi } from '../../../invoices/services/invoice-link-api';
import { PortalState } from '../../components/portal-state/portal-state';
import { PortalInvoiceService } from '../../services/portal-invoice.service';

/**
 * `/portal/invoices/:invoiceId` (BR-31): the existing invoice and payment experience inside the
 * portal shell, served by the portal invoice service (session + id, never a token).
 */
@Component({
  selector: 'app-portal-invoice',
  imports: [PortalState, PublicInvoicePage, RouterLink],
  providers: [PortalInvoiceService, { provide: InvoiceLinkApi, useExisting: PortalInvoiceService }],
  template: `
    <a class="back" routerLink="/portal/invoices">
      <i class="pi pi-arrow-left" aria-hidden="true"></i>
      Back to Invoices
    </a>
    @if (missing()) {
      <app-portal-state
        state="not-found"
        backLink="/portal/invoices"
        backLabel="Back to Invoices"
      />
    } @else {
      <app-public-invoice [embedded]="true" (missing)="missing.set(true)" />
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
export class PortalInvoice {
  readonly missing = signal(false);
}
