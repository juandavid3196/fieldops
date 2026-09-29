using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Invitations;

/// <summary>
/// <c>Email:Smtp</c> settings (BR-15). Credentials come from user-secrets or
/// environment variables only; they are optional (Mailpit needs none).
/// </summary>
public sealed class SmtpSettings
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool EnableSsl { get; set; }

    public string SenderAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "FieldOps";

    public string? UserName { get; set; }

    public string? Password { get; set; }
}

/// <summary>Rejects missing or malformed SMTP settings on start; never echoes secret values.</summary>
public sealed class SmtpSettingsValidator : IValidateOptions<SmtpSettings>
{
    public ValidateOptionsResult Validate(string? name, SmtpSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add($"'{SmtpSettings.SectionName}:Host' is required.");
        }

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"'{SmtpSettings.SectionName}:Port' must be between 1 and 65535.");
        }

        if (!MailAddress.TryCreate(options.SenderAddress, out var sender)
            || !string.Equals(sender.Address, options.SenderAddress.Trim(), StringComparison.Ordinal))
        {
            failures.Add($"'{SmtpSettings.SectionName}:SenderAddress' must be a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(options.SenderName))
        {
            failures.Add($"'{SmtpSettings.SectionName}:SenderName' is required.");
        }

        if (string.IsNullOrEmpty(options.UserName) != string.IsNullOrEmpty(options.Password))
        {
            failures.Add($"'{SmtpSettings.SectionName}:UserName' and 'Password' must be set together.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
