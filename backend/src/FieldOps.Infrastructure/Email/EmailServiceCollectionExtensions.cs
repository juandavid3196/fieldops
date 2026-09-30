using System.Globalization;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>Service key of the pooled client used by the Resend adapter.</summary>
    public const string ResendHttpClientKey = "FieldOps.ResendEmailSender";

    /// <summary>
    /// Registers the validated <c>Email</c> settings, the provider chosen by
    /// <c>Email:Provider</c> as <see cref="IEmailSender"/> and the invitation
    /// delivery composed on it. Values are read one by one so an unparsable
    /// number fails validation on start (0) rather than throwing from the
    /// binder; secrets are never logged or echoed.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<EmailSettings>()
            .Configure(settings =>
            {
                var section = configuration.GetSection(EmailSettings.SectionName);

                settings.Provider = section["Provider"] ?? string.Empty;
                settings.SenderAddress = section["SenderAddress"] ?? string.Empty;
                settings.SenderName = section["SenderName"] ?? string.Empty;
                settings.Smtp.Host = section["Smtp:Host"] ?? string.Empty;
                settings.Smtp.Port = int.TryParse(section["Smtp:Port"], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                    ? port
                    : 0;
                settings.Smtp.EnableSsl = bool.TryParse(section["Smtp:EnableSsl"], out var enableSsl) && enableSsl;
                settings.Smtp.UserName = section["Smtp:UserName"];
                settings.Smtp.Password = section["Smtp:Password"];
                settings.Resend.ApiKey = section["Resend:ApiKey"];
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EmailSettings>, EmailSettingsValidator>();

        // The project has no Microsoft.Extensions.Http package: one pooled
        // client for the process, connections recycled so DNS changes apply.
        services.AddKeyedSingleton(ResendHttpClientKey, (_, _) => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        });

        services.AddSingleton<IEmailSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EmailSettings>>();

            return string.Equals(options.Value.Provider, EmailSettings.ResendProvider, StringComparison.Ordinal)
                ? new ResendEmailSender(
                    provider.GetRequiredKeyedService<HttpClient>(ResendHttpClientKey),
                    options,
                    provider.GetRequiredService<ILogger<ResendEmailSender>>())
                : new SmtpEmailSender(options, provider.GetRequiredService<ILogger<SmtpEmailSender>>());
        });

        services.AddSingleton<IInvitationDelivery, InvitationDelivery>();

        return services;
    }
}
