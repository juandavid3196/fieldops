namespace FieldOps.Domain.Invoices;

/// <summary>
/// SA-17: a provider event that was processed, kept only for idempotency (BR-10); no event payload is stored. Rows are
/// inserted by the webhook transaction with <c>ON CONFLICT DO NOTHING</c> and then updated with their outcome in the same
/// transaction, so the entity is mapped for the model and reads but has no factory.
/// </summary>
public sealed class PaymentWebhookEvent
{
    private PaymentWebhookEvent()
    {
    }

    public Guid Id { get; private set; }

    public string Provider { get; private set; } = PaymentWebhookProviders.Stripe;

    public string ProviderEventId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    public Guid? OrganizationId { get; private set; }

    public Guid? AttemptId { get; private set; }

    public string Outcome { get; private set; } = PaymentWebhookOutcomes.NoEffect;

    public DateTimeOffset ReceivedAt { get; private set; }
}

public static class PaymentWebhookProviders
{
    public const string Stripe = "stripe";
}

/// <summary>The outcomes allowed by the SA-17 check.</summary>
public static class PaymentWebhookOutcomes
{
    public const string Applied = "applied";

    public const string NoEffect = "no_effect";

    public const string Ignored = "ignored";

    public const string NeedsAttention = "needs_attention";

    public static readonly string[] All = [Applied, NoEffect, Ignored, NeedsAttention];
}
