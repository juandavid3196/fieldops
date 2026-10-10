using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Payments;

/// <summary>
/// Rejects a Stripe key with the wrong prefix on start; absent or partial keys only disable card payments. Messages name the
/// key only, never the value.
/// </summary>
public sealed class StripeSettingsValidator : IValidateOptions<StripeSettings>
{
    public ValidateOptionsResult Validate(string? name, StripeSettings options)
    {
        var failures = new List<string>();

        if (Present(options.SecretKey)
            && !options.SecretKey!.StartsWith(StripeSettings.SecretKeyPrefix, StringComparison.Ordinal)
            && !options.SecretKey.StartsWith(StripeSettings.RestrictedKeyPrefix, StringComparison.Ordinal))
        {
            failures.Add($"'{StripeSettings.SectionName}:SecretKey' must start with '{StripeSettings.SecretKeyPrefix}' or '{StripeSettings.RestrictedKeyPrefix}'.");
        }

        if (Present(options.PublishableKey) && !options.PublishableKey!.StartsWith(StripeSettings.PublishableKeyPrefix, StringComparison.Ordinal))
        {
            failures.Add($"'{StripeSettings.SectionName}:PublishableKey' must start with '{StripeSettings.PublishableKeyPrefix}'.");
        }

        if (Present(options.WebhookSecret) && !options.WebhookSecret!.StartsWith(StripeSettings.WebhookSecretPrefix, StringComparison.Ordinal))
        {
            failures.Add($"'{StripeSettings.SectionName}:WebhookSecret' must start with '{StripeSettings.WebhookSecretPrefix}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
}

/// <summary>Rejects a present but invalid bank details key (not base64 of exactly 32 bytes); an absent key only disables bank transfer.</summary>
public sealed class BankDetailsSettingsValidator : IValidateOptions<BankDetailsSettings>
{
    public ValidateOptionsResult Validate(string? name, BankDetailsSettings options)
    {
        if (string.IsNullOrWhiteSpace(options.EncryptionKey) || AesGcmBankDetailsEncryptor.TryReadKey(options.EncryptionKey, out _))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"'{BankDetailsSettings.SectionName}:EncryptionKey' must be a base64 value of exactly {BankDetailsSettings.KeyBytes} bytes.");
    }
}
