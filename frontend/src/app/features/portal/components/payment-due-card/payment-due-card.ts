import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';

import { PaymentDue } from '../../models/portal.model';
import { dateLabel, money } from '../../utils/portal-format';

/** Payment due card (BR-21): the invoice with a balance due. */
@Component({
  selector: 'app-payment-due-card',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './payment-due-card.html',
  styleUrls: ['../../portal.scss', '../../portal-card.scss'],
})
export class PaymentDueCard {
  readonly payment = input<PaymentDue | null>(null);

  protected readonly balance = computed(() => {
    const payment = this.payment();
    return payment === null ? '' : money(payment.balanceDue, payment.currency);
  });
  protected readonly due = computed(() => {
    const dueDate = this.payment()?.dueDate ?? null;
    return dueDate === null ? null : dateLabel(dueDate);
  });
}
