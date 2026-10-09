import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { SpinnerIcon } from 'primeng/icons/spinner';
import { InputText } from 'primeng/inputtext';

import {
  NAME_MAX_LENGTH,
  NAME_REQUIRED_MESSAGE,
  NAME_TOO_LONG_MESSAGE,
  PublicInvoice,
} from '../../models/invoice.model';
import { PublicPaymentFlow } from '../../services/public-payment-flow';
import { STRIPE_PAYMENT_ADAPTER, StripeCardHandle } from '../../services/stripe-payment.adapter';
import { money, minorUnits } from '../../utils/public-invoice-format';
import { resolveStripeAppearance } from '../../utils/stripe-appearance';

/** Cardholder name, Stripe Payment Element and the Pay button (BR-28, BR-29). */
@Component({
  selector: 'app-public-card-form',
  imports: [ButtonDirective, InputText, SpinnerIcon],
  templateUrl: './public-card-form.html',
  styleUrl: './public-card-form.scss',
})
export class PublicCardForm {
  private readonly adapter = inject(STRIPE_PAYMENT_ADAPTER);
  private readonly document = inject(DOCUMENT);
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('element');
  private readonly nameInput = viewChild.required<ElementRef<HTMLInputElement>>('name');
  private handle: StripeCardHandle | null = null;
  private destroyed = false;

  protected readonly flow = inject(PublicPaymentFlow);

  readonly invoice = input.required<PublicInvoice>();
  /** "Choose another payment method" after a failure. */
  readonly chooseAnother = output<void>();

  protected readonly mounted = signal(false);
  protected readonly mountFailed = signal(false);
  protected readonly nameError = signal<string | null>(null);

  protected readonly balance = computed(() =>
    money(this.invoice().balanceDue, this.invoice().currency),
  );
  protected readonly buttonLabel = computed(() => {
    const phase = this.flow.phase();
    if (phase === 'failed') {
      return 'Try payment again';
    }
    return this.flow.overlay() ? `Processing ${this.balance()}` : `Pay ${this.balance()}`;
  });

  constructor() {
    this.flow.ensureKey();
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.handle?.destroy();
    });
    afterNextRender(() => void this.mount());
  }

  private async mount(): Promise<void> {
    const invoice = this.invoice();
    const key = invoice.paymentOptions?.card.publishableKey ?? null;
    if (key === null) {
      this.mountFailed.set(true);
      return;
    }
    try {
      const handle = await this.adapter.mountCard(this.host().nativeElement, {
        publishableKey: key,
        amountMinor: minorUnits(invoice.balanceDue),
        currency: invoice.currency,
        appearance: resolveStripeAppearance(this.document),
      });
      if (this.destroyed) {
        handle.destroy();
        return;
      }
      this.handle = handle;
      this.mounted.set(true);
    } catch {
      this.mountFailed.set(true);
    }
  }

  protected onName(event: Event): void {
    this.flow.cardholderName.set((event.target as HTMLInputElement).value);
    this.nameError.set(null);
  }

  protected pay(event: Event): void {
    event.preventDefault();
    const phase = this.flow.phase();
    if (this.handle === null || (phase !== 'idle' && phase !== 'failed')) {
      return;
    }
    const name = this.flow.cardholderName().trim();
    const error =
      name === ''
        ? NAME_REQUIRED_MESSAGE
        : name.length > NAME_MAX_LENGTH
          ? NAME_TOO_LONG_MESSAGE
          : null;
    this.nameError.set(error);
    if (error !== null) {
      this.nameInput().nativeElement.focus();
      return;
    }
    void this.flow.submit(this.handle, name);
  }
}
