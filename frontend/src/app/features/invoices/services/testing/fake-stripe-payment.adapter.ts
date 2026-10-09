import {
  CardConfirmInput,
  CardConfirmOutcome,
  CardMountOptions,
  StripeCardHandle,
  StripePaymentAdapter,
} from '../stripe-payment.adapter';

/** Deterministic Stripe stand-in for specs: no network, no Stripe.js. */
export class FakeStripePaymentAdapter implements StripePaymentAdapter {
  mounts: CardMountOptions[] = [];
  validations = 0;
  confirms: CardConfirmInput[] = [];
  destroyed = 0;
  /** Next `validate()` result. */
  valid = true;
  confirmOutcome: CardConfirmOutcome = 'submitted';
  mountError: Error | null = null;

  mountCard(_host: HTMLElement, options: CardMountOptions): Promise<StripeCardHandle> {
    if (this.mountError !== null) {
      return Promise.reject(this.mountError);
    }
    this.mounts.push(options);
    return Promise.resolve({
      validate: () => {
        this.validations++;
        return Promise.resolve(this.valid);
      },
      confirm: (input) => {
        this.confirms.push(input);
        return Promise.resolve(this.confirmOutcome);
      },
      destroy: () => {
        this.destroyed++;
      },
    });
  }
}
