using FieldOps.Application.Features.OnlinePayments;
using Stripe;

namespace FieldOps.Infrastructure.Payments;

/// <summary>
/// Verifies a Stripe webhook with the endpoint secret and Stripe's default timestamp tolerance and reduces the four handled
/// event types to a <see cref="GatewayEvent"/> (customer-invoice-payments BR-09). Public so the integration tests sign and
/// parse with the very same code as the Stripe gateway. The payload is used exactly as received and is never logged.
/// </summary>
public static class StripeWebhookParser
{
    public static GatewayWebhookParse Parse(string payload, string? signature, string? webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(webhookSecret) || string.IsNullOrEmpty(payload))
        {
            return new GatewayWebhookParse.Invalid();
        }

        Event stripeEvent;

        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret, EventUtility.DefaultTimeTolerance, throwOnApiVersionMismatch: false);
        }
        catch (Exception exception) when (exception is StripeException or ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return new GatewayWebhookParse.Invalid();
        }

        return stripeEvent.Type switch
        {
            GatewayEventTypes.PaymentSucceeded or GatewayEventTypes.PaymentFailed or GatewayEventTypes.PaymentCanceled
                when stripeEvent.Data?.Object is PaymentIntent intent => FromIntent(stripeEvent, intent),
            GatewayEventTypes.ChargeRefunded when stripeEvent.Data?.Object is Charge charge => FromCharge(stripeEvent, charge),
            _ => new GatewayWebhookParse.Unhandled(),
        };
    }

    private static GatewayWebhookParse FromIntent(Event stripeEvent, PaymentIntent intent)
    {
        if (!PaymentMinorUnits.TryFromMinor(intent.Amount, intent.Currency, out var amount))
        {
            return new GatewayWebhookParse.Unhandled();
        }

        var error = intent.LastPaymentError;

        return new GatewayWebhookParse.Handled(new GatewayEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            intent.Id,
            Metadata(intent.Metadata, "attemptId"),
            Metadata(intent.Metadata, "organizationId"),
            Metadata(intent.Metadata, "invoiceId"),
            amount,
            intent.Currency.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(error?.DeclineCode) ? error?.Code : error.DeclineCode,
            0m));
    }

    private static GatewayWebhookParse FromCharge(Event stripeEvent, Charge charge)
    {
        if (!PaymentMinorUnits.TryFromMinor(charge.Amount, charge.Currency, out var amount)
            || !PaymentMinorUnits.TryFromMinor(charge.AmountRefunded, charge.Currency, out var refunded))
        {
            return new GatewayWebhookParse.Unhandled();
        }

        return new GatewayWebhookParse.Handled(new GatewayEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            charge.PaymentIntentId,
            Metadata(charge.Metadata, "attemptId"),
            Metadata(charge.Metadata, "organizationId"),
            Metadata(charge.Metadata, "invoiceId"),
            amount,
            charge.Currency.ToUpperInvariant(),
            null,
            refunded));
    }

    private static Guid? Metadata(IDictionary<string, string>? metadata, string key) =>
        metadata is not null && metadata.TryGetValue(key, out var text) && Guid.TryParse(text, out var id) && id != Guid.Empty ? id : null;
}
