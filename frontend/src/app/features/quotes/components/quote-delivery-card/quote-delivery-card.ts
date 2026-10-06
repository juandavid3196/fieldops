import { Component, computed, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Checkbox } from 'primeng/checkbox';
import { InputText } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';

import { NO_RECIPIENT_MESSAGE, SMS_UNAVAILABLE_MESSAGE } from '../../models/quote.model';
import { EMAIL_LIMIT } from '../../utils/quote-lines';

/**
 * Customer approval (information only, BR-20) and Delivery (BR-21) cards. Email is the only
 * channel; SMS is disabled with its helper text. The page owns the message and the Send action.
 */
@Component({
  selector: 'app-quote-delivery-card',
  imports: [FormsModule, Checkbox, InputText, Textarea],
  templateUrl: './quote-delivery-card.html',
  styleUrl: './quote-delivery-card.scss',
})
export class QuoteDeliveryCard {
  readonly recipient = input.required<string | null>();
  readonly message = model.required<string>();
  readonly error = input<string | null>(null);
  readonly recipientError = input<string | null>(null);

  readonly limit = EMAIL_LIMIT;
  readonly smsUnavailable = SMS_UNAVAILABLE_MESSAGE;
  readonly noRecipient = NO_RECIPIENT_MESSAGE;
  readonly recipientText = computed(() => this.recipient() ?? '');
  readonly count = computed(() => this.message().length);
}
