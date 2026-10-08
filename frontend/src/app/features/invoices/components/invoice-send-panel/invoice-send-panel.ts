import { Component, ElementRef, computed, input, output, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonDirective } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Textarea } from 'primeng/textarea';
import { ToggleSwitch } from 'primeng/toggleswitch';

import { PaymentTerms } from '../../../billing-review/models/billing-review.model';
import {
  TERMS_OPTIONS,
  formatCalendarDate,
  formatInstantDate,
  formatInstantTime,
} from '../../../billing-review/utils/billing-review-format';
import {
  DeliveryErrors,
  DeliveryField,
  DeliveryValues,
  InvoiceDetail,
  LATER_MESSAGE,
  MESSAGE_MAX_LENGTH,
  READ_ONLY_MESSAGE,
  SMS_UNAVAILABLE_MESSAGE,
} from '../../models/invoice.model';

let nextId = 0;

/** Pending action shown on its button; every action is disabled while one is pending. */
export type InvoiceAction = 'save' | 'send' | 'resend' | 'pdf';

/**
 * Right panel (BR-24, BR-25): "Send invoice" for a draft, "Delivery" once sent. Unsupported
 * options are visible, unchecked and disabled, and never part of the values.
 */
@Component({
  selector: 'app-invoice-send-panel',
  imports: [
    ButtonDirective,
    Checkbox,
    FormsModule,
    InputText,
    Select,
    SpinnerIcon,
    Textarea,
    ToggleSwitch,
  ],
  templateUrl: './invoice-send-panel.html',
  styleUrl: './invoice-send-panel.scss',
})
export class InvoiceSendPanel {
  private readonly recipientField = viewChild<ElementRef<HTMLInputElement>>('recipientField');
  private readonly termsField = viewChild(Select);
  private readonly messageField = viewChild<ElementRef<HTMLTextAreaElement>>('messageField');

  readonly invoice = input.required<InvoiceDetail>();
  readonly values = input.required<DeliveryValues>();
  /** `yyyy-mm-dd`, recomputed by the page when the terms change. */
  readonly dueDate = input.required<string>();
  readonly errors = input<DeliveryErrors>({});
  readonly pending = input<InvoiceAction | null>(null);

  readonly valuesChange = output<DeliveryValues>();
  readonly resend = output<void>();

  protected readonly id = `invoice-panel-${nextId++}`;
  protected readonly termsOptions = TERMS_OPTIONS.map((option) => ({ ...option }));
  protected readonly limit = MESSAGE_MAX_LENGTH;
  protected readonly smsUnavailable = SMS_UNAVAILABLE_MESSAGE;
  protected readonly laterMessage = LATER_MESSAGE;
  protected readonly readOnlyMessage = READ_ONLY_MESSAGE;

  /** Links the terms error to the select's focusable label (BR-29). */
  protected readonly termsPt = computed(() =>
    this.errors().paymentTerms
      ? { label: { 'aria-describedby': `${this.id}-terms-error`, 'aria-invalid': 'true' } }
      : {},
  );
  protected readonly isDraft = computed(() => this.invoice().status === 'draft');
  protected readonly locked = computed(
    () => !this.isDraft() || !this.invoice().canAct || this.pending() !== null,
  );
  protected readonly canResend = computed(
    () => this.invoice().status === 'sent' && this.invoice().canAct,
  );
  protected readonly dueText = computed(() =>
    this.dueDate() === '' ? '' : formatCalendarDate(this.dueDate()),
  );
  protected readonly sentText = computed(() => {
    const invoice = this.invoice();
    return invoice.sentAt === null
      ? null
      : `Sent ${formatInstantDate(invoice.sentAt, invoice.timezone)} at ${formatInstantTime(invoice.sentAt, invoice.timezone)}`;
  });

  protected update(change: Partial<DeliveryValues>): void {
    this.valuesChange.emit({ ...this.values(), ...change });
  }

  protected setTerms(terms: PaymentTerms): void {
    this.update({ paymentTerms: terms });
  }

  /** BR-26: focuses the first invalid field. */
  focusField(field: DeliveryField): void {
    switch (field) {
      case 'recipientEmail':
        this.recipientField()?.nativeElement.focus();
        break;
      case 'paymentTerms':
        this.termsField()?.focus();
        break;
      case 'message':
        this.messageField()?.nativeElement.focus();
        break;
    }
  }
}
