namespace FieldOps.Infrastructure.Payments;

/// <summary>
/// <c>Stripe</c> settings (customer-invoice-payments). The keys come from user-secrets or environment variables only and are
/// never logged or echoed. Card payments are available only when the secret, publishable and webhook keys are all set.
/// </summary>
public sealed class StripeSettings
{
    public const string SectionName = "Stripe";

    public const string SecretKeyPrefix = "sk_";

    public const string RestrictedKeyPrefix = "rk_";

    public const string PublishableKeyPrefix = "pk_";

    public const string WebhookSecretPrefix = "whsec_";

    public string? SecretKey { get; set; }

    public string? PublishableKey { get; set; }

    public string? WebhookSecret { get; set; }

    public bool CardAvailable =>
        !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(PublishableKey) && !string.IsNullOrWhiteSpace(WebhookSecret);
}

/// <summary><c>BankDetails</c> settings (BR-24): the base64 AES-256 key of the account number; absent means bank transfer is unavailable.</summary>
public sealed class BankDetailsSettings
{
    public const string SectionName = "BankDetails";

    public const int KeyBytes = 32;

    public string? EncryptionKey { get; set; }
}
