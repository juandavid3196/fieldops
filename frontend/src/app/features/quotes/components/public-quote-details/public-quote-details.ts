import { Component, computed, input, output } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { CheckIcon } from 'primeng/icons/check';

import { PublicQuote } from '../../models/public-quote.model';
import { customerInitials } from '../../utils/quote-format';
import { PublicQuotePhotos } from '../public-quote-photos/public-quote-photos';

/** Company card and job card (scope of work, assessment photos) of the quote link (BR-05). */
@Component({
  selector: 'app-public-quote-details',
  imports: [ButtonDirective, CheckIcon, PublicQuotePhotos],
  templateUrl: './public-quote-details.html',
  styleUrl: './public-quote-details.scss',
})
export class PublicQuoteDetails {
  readonly quote = input.required<PublicQuote>();
  readonly logoUrl = input<string | null>(null);

  readonly asked = output<void>();

  readonly initials = computed(() => customerInitials(this.quote().organization.name));
  readonly canAsk = computed(() => {
    const { quote, clarification } = this.quote();
    return (
      (quote.status === 'sent' || quote.status === 'clarification_requested') && !clarification
    );
  });
  readonly phoneHref = computed(() => {
    const phone = this.quote().organization.phone;
    return phone === null ? null : `tel:${phone.replace(/[^\d+]/g, '')}`;
  });
}
