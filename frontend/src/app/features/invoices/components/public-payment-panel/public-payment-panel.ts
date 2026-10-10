import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';

import {
  BANK_UNAVAILABLE_MESSAGE,
  CARD_FAILURE_TITLE,
  CARD_STILL_CONFIRMING_MESSAGE,
  CARD_UNAVAILABLE_MESSAGE,
  PublicInvoice,
} from '../../models/invoice.model';
import { PublicPaymentFlow } from '../../services/public-payment-flow';
import { PublicBankTransfer } from '../public-bank-transfer/public-bank-transfer';
import { PublicCardForm } from '../public-card-form/public-card-form';

type Method = 'card' | 'bank' | 'cash';

/** "Pay invoice" panel (BR-28): method selector, the three method panels, overlay and alerts. */
@Component({
  selector: 'app-public-payment-panel',
  imports: [PublicBankTransfer, PublicCardForm],
  templateUrl: './public-payment-panel.html',
  styleUrl: './public-payment-panel.scss',
})
export class PublicPaymentPanel {
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  private readonly injector = inject(Injector);

  protected readonly flow = inject(PublicPaymentFlow);

  readonly invoice = input.required<PublicInvoice>();
  readonly reload = output<void>();
  readonly unavailable = output<void>();

  protected readonly failureTitle = CARD_FAILURE_TITLE;
  protected readonly stillConfirming = CARD_STILL_CONFIRMING_MESSAGE;
  protected readonly cardReason = CARD_UNAVAILABLE_MESSAGE;
  protected readonly bankReason = BANK_UNAVAILABLE_MESSAGE;

  private readonly chosen = signal<Method | null>(null);

  protected readonly cardAvailable = computed(() => {
    const card = this.invoice().paymentOptions?.card;
    return card !== undefined && card.available && card.publishableKey !== null;
  });
  protected readonly bankAvailable = computed(
    () => this.invoice().paymentOptions?.bankTransfer.available === true,
  );
  /** The first available method is selected until the customer picks another. */
  protected readonly method = computed<Method>(
    () => this.chosen() ?? (this.cardAvailable() ? 'card' : this.bankAvailable() ? 'bank' : 'cash'),
  );
  protected readonly cash = computed(
    () => this.invoice().paymentOptions?.cash ?? { phone: null, email: null },
  );
  protected readonly phoneHref = computed(() => {
    const phone = this.cash().phone;
    return phone === null ? null : `tel:${phone.replace(/[^\d+]/g, '')}`;
  });
  protected readonly questionHref = computed(() => {
    const invoice = this.invoice();
    const email = invoice.organization.email;
    return email === null
      ? null
      : `mailto:${email}?subject=${encodeURIComponent(`Invoice ${invoice.number}`)}`;
  });

  protected select(method: Method): void {
    this.chosen.set(method);
  }

  /** "Choose another payment method": drops the failure and moves focus to the selector. */
  protected chooseAnother(): void {
    this.flow.dismissFailure();
    afterNextRender(
      () =>
        this.host.querySelector<HTMLInputElement>('input[name="payment-method"]:checked')?.focus(),
      { injector: this.injector },
    );
  }
}
