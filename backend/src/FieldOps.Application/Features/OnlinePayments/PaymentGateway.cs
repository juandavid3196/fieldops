namespace FieldOps.Application.Features.OnlinePayments;

/// <summary>What the configured provider can do (customer-invoice-payments BR-04): card payments need every key.</summary>
public sealed record PaymentGatewayCapabilities(bool CardAvailable, string? PublishableKey);

/// <summary>The intent of one attempt: the provider creates it for exactly this amount and currency (BR-06).</summary>
public sealed record GatewayIntentRequest(Guid AttemptId, Guid OrganizationId, Guid InvoiceId, decimal Amount, string Currency);

/// <summary>The provider intent id and the client secret handed to the browser; the secret is never stored, logged or audited (BR-07).</summary>
public sealed record GatewayIntent(string IntentId, string ClientSecret);

public enum GatewayCancelResult
{
    Canceled,
    AlreadyCanceled,
    NotCancelable,
    Unavailable,
}

/// <summary>The card facts read from the provider for a succeeded payment (BR-07): only brand and last four digits are kept.</summary>
public sealed record GatewayCardDetails(string? Brand, string? Last4);

/// <summary>
/// A verified provider event reduced to the facts FieldOps needs. <c>Amount</c> and <c>Currency</c> belong to the intent or
/// charge; <c>CumulativeRefunded</c> is the charge total refunded so far (BR-15). Metadata ids are only a fallback match.
/// </summary>
public sealed record GatewayEvent(
    string EventId,
    string Type,
    string? IntentId,
    Guid? AttemptId,
    Guid? OrganizationId,
    Guid? InvoiceId,
    decimal Amount,
    string Currency,
    string? FailureCode,
    decimal CumulativeRefunded);

public static class GatewayEventTypes
{
    public const string PaymentSucceeded = "payment_intent.succeeded";

    public const string PaymentFailed = "payment_intent.payment_failed";

    public const string PaymentCanceled = "payment_intent.canceled";

    public const string ChargeRefunded = "charge.refunded";
}

/// <summary>The result of verifying and parsing a webhook (BR-09).</summary>
public abstract record GatewayWebhookParse
{
    private GatewayWebhookParse()
    {
    }

    /// <summary>A missing or invalid signature, or an unreadable payload: 400, nothing stored.</summary>
    public sealed record Invalid : GatewayWebhookParse;

    /// <summary>A verified event of another type: 200, nothing stored.</summary>
    public sealed record Unhandled : GatewayWebhookParse;

    public sealed record Handled(GatewayEvent Event) : GatewayWebhookParse;
}

/// <summary>
/// The payment provider seam (customer-invoice-payments). Implementations never log secrets, client secrets or payloads.
/// Network and provider failures throw <see cref="PaymentGatewayUnavailableException"/>.
/// </summary>
public interface IPaymentGateway
{
    PaymentGatewayCapabilities Capabilities { get; }

    Task<GatewayIntent> CreateIntentAsync(GatewayIntentRequest request, CancellationToken cancellationToken);

    Task<string> RetrieveClientSecretAsync(string intentId, CancellationToken cancellationToken);

    /// <summary>Cancels the intent; <see cref="GatewayCancelResult.NotCancelable"/> when it succeeded or is processing.</summary>
    Task<GatewayCancelResult> CancelIntentAsync(string intentId, CancellationToken cancellationToken);

    Task<GatewayCardDetails> GetCardDetailsAsync(string intentId, CancellationToken cancellationToken);

    GatewayWebhookParse ParseWebhook(string payload, string? signature);
}

/// <summary>The provider could not be reached or refused a request (502 at intent creation, 500 for a webhook so it is retried).</summary>
public sealed class PaymentGatewayUnavailableException : Exception
{
    public PaymentGatewayUnavailableException()
        : base("The payment provider is unavailable.")
    {
    }

    public PaymentGatewayUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Minor-unit conversion for the two-decimal currencies the provider supports (AS-01); anything else is a gateway failure.</summary>
public static class PaymentMinorUnits
{
    private static readonly HashSet<string> TwoDecimalCurrencies = new(StringComparer.Ordinal)
    {
        "AED", "ARS", "AUD", "BGN", "BRL", "CAD", "CHF", "CNY", "COP", "CZK", "DKK", "EUR", "GBP", "HKD", "IDR", "ILS",
        "INR", "MXN", "MYR", "NOK", "NZD", "PEN", "PHP", "PLN", "RON", "SAR", "SEK", "SGD", "THB", "TRY", "USD", "ZAR",
    };

    public static bool IsSupported(string? currency) =>
        currency is not null && TwoDecimalCurrencies.Contains(currency.Trim().ToUpperInvariant());

    /// <summary>The amount in minor units; throws <see cref="PaymentGatewayUnavailableException"/> for an unsupported currency or precision.</summary>
    public static long ToMinor(decimal amount, string currency)
    {
        if (!IsSupported(currency) || amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            throw new PaymentGatewayUnavailableException("The amount or currency can't be sent to the payment provider.");
        }

        return decimal.ToInt64(amount * 100m);
    }

    public static bool TryFromMinor(long minor, string? currency, out decimal amount)
    {
        amount = 0m;

        if (!IsSupported(currency) || minor < 0)
        {
            return false;
        }

        amount = minor / 100m;

        return true;
    }
}
