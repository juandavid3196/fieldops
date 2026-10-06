import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { QuoteDetail } from '../../models/quote.model';
import { customerInitials } from '../../utils/quote-format';

/**
 * Customer and Request cards of the quote editor (BR-08). Presentational; the customer **Edit**
 * link is hidden for guests (no customer id).
 */
@Component({
  selector: 'app-quote-customer-card',
  imports: [RouterLink],
  templateUrl: './quote-customer-card.html',
  styleUrl: './quote-customer-card.scss',
})
export class QuoteCustomerCard {
  readonly customer = input.required<QuoteDetail['customer']>();
  readonly request = input.required<QuoteDetail['request']>();

  readonly initials = computed(() => customerInitials(this.customer().name));
  readonly requestQueryParams = computed(() => ({ request: this.request().id }));
}
