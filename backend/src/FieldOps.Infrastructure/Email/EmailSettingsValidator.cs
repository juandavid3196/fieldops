using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Rejects missing or malformed email settings on start (BR-17). Messages
/// name the key only, never the value.
/// </summary>
public sealed class EmailSettingsValidator(IHostEnvironment environment) : IValidateOptions<EmailSettings>
{
    private const string Section = EmailSettings.SectionName;

    public ValidateOptionsResult Validate(string? name, EmailSettings options)
    {
        var failures = new List<string>();

        var provider = options.Provider?.Trim();
        var isSmtp = string.Equals(provider, EmailSettings.SmtpProvider, StringComparison.Ordinal);
        var isResend = string.Equals(provider, EmailSettings.ResendProvider, StringComparison.Ordinal);

        if (!isSmtp && !isResend)
        {
            failures.Add($"'{Section}:Provider' is required and must be '{EmailSettings.SmtpProvider}' or '{EmailSettings.ResendProvider}'.");
        }
        else if (isSmtp && environment.IsProduction())
        {
            failures.Add($"'{Section}:Provider' must be '{EmailSettings.ResendProvider}' in Production.");
        }

        if (!MailAddress.TryCreate(options.SenderAddress, out var sender)
            || !string.Equals(sender.Address, options.SenderAddress.Trim(), StringComparison.Ordinal))
        {
            failures.Add($"'{Section}:SenderAddress' must be a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(options.SenderName))
        {
            failures.Add($"'{Section}:SenderName' is required.");
        }

        if (isSmtp)
        {
            if (string.IsNullOrWhiteSpace(options.Smtp.Host))
            {
                failures.Add($"'{Section}:Smtp:Host' is required.");
            }

            if (options.Smtp.Port is < 1 or > 65535)
            {
                failures.Add($"'{Section}:Smtp:Port' must be between 1 and 65535.");
            }

            if (string.IsNullOrEmpty(options.Smtp.UserName) != string.IsNullOrEmpty(options.Smtp.Password))
            {
                failures.Add($"'{Section}:Smtp:UserName' and 'Password' must be set together.");
            }
        }

        if (isResend && string.IsNullOrWhiteSpace(options.Resend.ApiKey))
        {
            failures.Add($"'{Section}:Resend:ApiKey' is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
