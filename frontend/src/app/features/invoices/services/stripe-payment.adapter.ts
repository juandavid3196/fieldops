import { InjectionToken } from '@angular/core';
import type { Appearance, Stripe, StripeElements } from '@stripe/stripe-js';

/** BR-07: card data lives only inside the Stripe-hosted Payment Element mounted by this adapter. */
export interface CardMountOptions {
  readonly publishableKey: string;
  /** Balance in minor units (deferred-intent Elements, mode `payment`). */
  readonly amountMinor: number;
  readonly currency: string;
  /** Resolved colours, font and radius from the page tokens. */
  readonly appearance: Readonly<Record<string, string>>;
}

export interface CardConfirmInput {
  readonly clientSecret: string;
  readonly billingName: string;
  readonly returnUrl: string;
}

/** `error` never decides the payment: the server status does. */
export type CardConfirmOutcome = 'submitted' | 'redirecting' | 'error';

export interface StripeCardHandle {
  /** Validates the Payment Element; `false` when Stripe shows an inline error. */
  validate(): Promise<boolean>;
  confirm(input: CardConfirmInput): Promise<CardConfirmOutcome>;
  destroy(): void;
}

export interface StripePaymentAdapter {
  mountCard(host: HTMLElement, options: CardMountOptions): Promise<StripeCardHandle>;
}

class StripeCardElement implements StripeCardHandle {
  constructor(
    private readonly stripe: Stripe,
    private readonly elements: StripeElements,
    private readonly destroyElement: () => void,
  ) {}

  async validate(): Promise<boolean> {
    const { error } = await this.elements.submit();
    return error === undefined;
  }

  async confirm(input: CardConfirmInput): Promise<CardConfirmOutcome> {
    const result = await this.stripe.confirmPayment({
      elements: this.elements,
      clientSecret: input.clientSecret,
      confirmParams: {
        return_url: input.returnUrl,
        payment_method_data: { billing_details: { name: input.billingName } },
      },
      redirect: 'if_required',
    });
    if (result.error !== undefined) {
      return 'error';
    }
    return result.paymentIntent.status === 'requires_action' ? 'redirecting' : 'submitted';
  }

  destroy(): void {
    this.destroyElement();
  }
}

/** Real adapter: Stripe.js is imported lazily the first time a card form is shown. */
export class StripeJsPaymentAdapter implements StripePaymentAdapter {
  async mountCard(host: HTMLElement, options: CardMountOptions): Promise<StripeCardHandle> {
    const { loadStripe } = await import('@stripe/stripe-js/pure');
    const stripe = await loadStripe(options.publishableKey);
    if (stripe === null) {
      throw new Error('Stripe.js did not load.');
    }
    const elements = stripe.elements({
      mode: 'payment',
      amount: options.amountMinor,
      currency: options.currency.toLowerCase(),
      allowedPaymentMethodTypes: ['card'],
      appearance: { theme: 'stripe', variables: options.appearance } satisfies Appearance,
    });
    const element = elements.create('payment', {
      fields: { billingDetails: { name: 'never' } },
      wallets: { applePay: 'never', googlePay: 'never' },
    });
    element.mount(host);
    return new StripeCardElement(stripe, elements, () => element.destroy());
  }
}

export const STRIPE_PAYMENT_ADAPTER = new InjectionToken<StripePaymentAdapter>(
  'STRIPE_PAYMENT_ADAPTER',
  { providedIn: 'root', factory: () => new StripeJsPaymentAdapter() },
);
