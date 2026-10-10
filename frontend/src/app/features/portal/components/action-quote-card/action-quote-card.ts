import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { ActionQuote } from '../../models/portal.model';
import { dateLabel, money } from '../../utils/portal-format';

/** Action required card (BR-20): the quote awaiting the customer's decision. */
@Component({
  selector: 'app-action-quote-card',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './action-quote-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class ActionQuoteCard {
  readonly quote = input<ActionQuote | null>(null);
  readonly asked = output<void>();

  protected readonly total = computed(() => {
    const quote = this.quote();
    return quote === null ? '' : money(quote.total, quote.currency);
  });
  protected readonly expires = computed(() => {
    const validUntil = this.quote()?.validUntil ?? null;
    return validUntil === null ? null : dateLabel(validUntil);
  });
}
