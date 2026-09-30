namespace FieldOps.Infrastructure.Email;

/// <summary>
/// <c>Email</c> settings (password recovery BR-17). Credentials and the
/// Resend key come from user-secrets or environment variables only.
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public const string SmtpProvider = "Smtp";

    public const string ResendProvider = "Resend";

    public string Provider { get; set; } = string.Empty;

    public string SenderAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public SmtpSettings Smtp { get; set; } = new();

    public ResendSettings Resend { get; set; } = new();
}

/// <summary><c>Email:Smtp</c>: credentials are optional (Mailpit needs none).</summary>
public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public bool EnableSsl { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }
}

/// <summary><c>Email:Resend</c>: the API key is a secret and is never logged or echoed.</summary>
public sealed class ResendSettings
{
    public string? ApiKey { get; set; }
}
