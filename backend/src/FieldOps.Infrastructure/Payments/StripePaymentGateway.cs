using System.Globalization;
using FieldOps.Application.Features.OnlinePayments;
using Microsoft.Extensions.Options;
using Stripe;

namespace FieldOps.Infrastructure.Payments;

/// <summary>
/// Stripe implementation of the payment gateway (customer-invoice-payments). It uses its own <see cref="StripeClient"/> and
/// never the global <c>StripeConfiguration</c>, a short timeout and at most one network retry. Intent creation sends the
/// attempt id as the provider idempotency key. Secrets, client secrets, payloads and card data are never logged.
/// </summary>
internal sealed class StripePaymentGateway : IPaymentGateway, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private const int MaxNetworkRetries = 1;

    private readonly StripeSettings settings;

    private readonly HttpClient? httpClient;

    private readonly PaymentIntentService? intents;

    public StripePaymentGateway(IOptions<StripeSettings> options)
    {
        settings = options.Value;

        if (settings.CardAvailable)
        {
            httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = RequestTimeout,
            };

            var client = new StripeClient(new StripeClientOptions
            {
                ApiKey = settings.SecretKey!.Trim(),
                HttpClient = new SystemNetHttpClient(httpClient, MaxNetworkRetries),
            });

            intents = new PaymentIntentService(client);
        }
    }

    public PaymentGatewayCapabilities Capabilities =>
        settings.CardAvailable
            ? new PaymentGatewayCapabilities(true, settings.PublishableKey!.Trim())
            : new PaymentGatewayCapabilities(false, null);

    public async Task<GatewayIntent> CreateIntentAsync(GatewayIntentRequest request, CancellationToken cancellationToken)
    {
        var service = Service();
        var minor = PaymentMinorUnits.ToMinor(request.Amount, request.Currency);

        try
        {
            var intent = await service.CreateAsync(
                new PaymentIntentCreateOptions
                {
                    Amount = minor,
                    Currency = request.Currency.Trim().ToLowerInvariant(),
                    AllowedPaymentMethodTypes = ["card"],
                    Metadata = new Dictionary<string, string>
                    {
                        ["organizationId"] = request.OrganizationId.ToString(),
                        ["invoiceId"] = request.InvoiceId.ToString(),
                        ["attemptId"] = request.AttemptId.ToString(),
                    },
                },
                new RequestOptions { IdempotencyKey = request.AttemptId.ToString() },
                cancellationToken);

            if (string.IsNullOrWhiteSpace(intent?.Id) || string.IsNullOrWhiteSpace(intent.ClientSecret))
            {
                throw new PaymentGatewayUnavailableException("The payment provider returned an incomplete intent.");
            }

            return new GatewayIntent(intent.Id, intent.ClientSecret);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new PaymentGatewayUnavailableException("The payment provider could not create the intent.", exception);
        }
    }

    public async Task<string> RetrieveClientSecretAsync(string intentId, CancellationToken cancellationToken)
    {
        try
        {
            var intent = await Service().GetAsync(intentId, cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(intent?.ClientSecret)
                ? throw new PaymentGatewayUnavailableException("The payment provider returned no client secret.")
                : intent.ClientSecret;
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new PaymentGatewayUnavailableException("The payment provider could not read the intent.", exception);
        }
    }

    public async Task<GatewayCancelResult> CancelIntentAsync(string intentId, CancellationToken cancellationToken)
    {
        if (intents is null)
        {
            return GatewayCancelResult.Unavailable;
        }

        try
        {
            var canceled = await intents.CancelAsync(intentId, cancellationToken: cancellationToken);

            return canceled?.Status == "canceled" ? GatewayCancelResult.Canceled : GatewayCancelResult.Unavailable;
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "payment_intent_unexpected_state")
        {
            // The intent is not cancelable: it is either already canceled, or succeeded or processing.
            try
            {
                var current = await intents.GetAsync(intentId, cancellationToken: cancellationToken);

                return current?.Status == "canceled" ? GatewayCancelResult.AlreadyCanceled : GatewayCancelResult.NotCancelable;
            }
            catch (Exception inner) when (IsProviderFailure(inner))
            {
                return GatewayCancelResult.Unavailable;
            }
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            return GatewayCancelResult.Unavailable;
        }
    }

    public async Task<GatewayCardDetails> GetCardDetailsAsync(string intentId, CancellationToken cancellationToken)
    {
        try
        {
            var intent = await Service().GetAsync(
                intentId,
                new PaymentIntentGetOptions { Expand = ["latest_charge"] },
                cancellationToken: cancellationToken);
            var card = intent?.LatestCharge?.PaymentMethodDetails?.Card;

            return new GatewayCardDetails(card?.Brand, card?.Last4);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new PaymentGatewayUnavailableException("The payment provider could not read the card details.", exception);
        }
    }

    public GatewayWebhookParse ParseWebhook(string payload, string? signature) =>
        StripeWebhookParser.Parse(payload, signature, settings.WebhookSecret?.Trim());

    public void Dispose() => httpClient?.Dispose();

    private PaymentIntentService Service() =>
        intents ?? throw new PaymentGatewayUnavailableException("Card payments are not configured.");

    private static bool IsProviderFailure(Exception exception) =>
        exception is StripeException or HttpRequestException or TaskCanceledException or TimeoutException;
}
