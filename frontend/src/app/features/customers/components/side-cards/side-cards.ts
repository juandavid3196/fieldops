import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Tag } from 'primeng/tag';
import { TranslocoPipe } from '@jsverse/transloco';

import { CustomerOverview } from '../../models/customer.model';
import {
  INVOICE_STATUS_LABELS,
  formatMoney,
  invoiceSeverity,
  preferredContact,
  sinceLabel,
} from '../../utils/customer-detail-format';
import { formatDate, formatPhone } from '../../utils/customer-format';
import { dateOnlyLabel } from '../../../quotes/utils/quote-format';

import { t } from '../../../../core/i18n/translate';
import { activeIntlLocale } from '../../../../core/i18n/active-locale';
export const INVOICES_LINK = '/coming-soon/invoices';

/**
 * Contact details, Customer summary, Billing and Tags cards (BR-03, BR-11, BR-12, BR-23).
 * Presentational; the pencils only ask the page to open the customer drawer.
 */
@Component({
  selector: 'app-side-cards',
  imports: [TranslocoPipe, RouterLink, Tag],
  templateUrl: './side-cards.html',
  styleUrl: './side-cards.scss',
})
export class SideCards {
  readonly overview = input.required<CustomerOverview>();
  readonly canMutate = input(false);

  /** A portal access change is running (the buttons are disabled). */
  readonly portalBusy = input(false);
  readonly portalError = input<string | null>(null);

  readonly editContact = output<void>();
  readonly editTags = output<void>();
  readonly invitePortal = output<void>();
  readonly removePortal = output<void>();

  readonly invoicesLink = INVOICES_LINK;

  /** "Portal access" line (BR-10), or `null` while the contract does not send the status. */
  readonly portal = computed(() => {
    const contact = this.overview().contact;
    const date = (value: string | null | undefined): string =>
      value ? dateOnlyLabel(value.slice(0, 10)) : '';
    switch (contact.portalStatus) {
      case 'active':
        return {
          status: 'active',
          text: t('customers.sideCards.activeSince', {
            portalLinkedOn: date(contact.portalLinkedOn),
          }),
        };
      case 'invited':
        return {
          status: 'invited',
          text: t('customers.sideCards.invitationSentExpires', {
            invitationExpiresOn: date(contact.invitationExpiresOn),
          }),
        };
      case 'not_invited':
        return { status: 'not_invited', text: t('customers.sideCards.notInvited') };
      default:
        return null;
    }
  });

  readonly view = computed(() => {
    const o = this.overview();
    const phone = o.contact.phone?.trim() ? o.contact.phone : null;
    return {
      email: o.contact.email,
      phone: phone === null ? null : formatPhone(phone),
      phoneHref: phone === null ? null : `tel:${phone.replace(/[^\d+]/g, '')}`,
      preferred: preferredContact(o.contact),
      totalJobs: o.summary.totalJobs.toLocaleString(activeIntlLocale()),
      lifetime: formatMoney(o.summary.lifetimeValue, o.currency),
      since: sinceLabel(o.summary.customerSince, o.timezone),
      lastService: o.summary.lastServiceAt
        ? formatDate(o.summary.lastServiceAt, o.timezone)
        : t('customers.sideCards.noCompletedService'),
      balance: formatMoney(o.outstandingBalance, o.currency),
      invoice: o.lastInvoice
        ? {
            number: o.lastInvoice.number,
            status: INVOICE_STATUS_LABELS[o.lastInvoice.status] ?? o.lastInvoice.status,
            severity: invoiceSeverity(o.lastInvoice.status),
          }
        : null,
      tags: [...o.tags].sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
      ),
    };
  });
}
