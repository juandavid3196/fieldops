using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Organizations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Payments;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the validated <c>Stripe</c> and <c>BankDetails</c> settings, the Stripe gateway, the bank details encryptor
    /// and the per-invoice throttle. Values are read one by one like the email settings; absent keys disable card payments or
    /// bank transfer without failing the start, while a malformed value fails it. Secrets are never logged or echoed.
    /// </summary>
    public static IServiceCollection AddOnlinePayments(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<StripeSettings>()
            .Configure(settings =>
            {
                var section = configuration.GetSection(StripeSettings.SectionName);

                settings.SecretKey = section["SecretKey"];
                settings.PublishableKey = section["PublishableKey"];
                settings.WebhookSecret = section["WebhookSecret"];
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<StripeSettings>, StripeSettingsValidator>();

        services
            .AddOptions<BankDetailsSettings>()
            .Configure(settings => settings.EncryptionKey = configuration.GetSection(BankDetailsSettings.SectionName)["EncryptionKey"])
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<BankDetailsSettings>, BankDetailsSettingsValidator>();

        services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        services.AddSingleton<IBankDetailsEncryptor, AesGcmBankDetailsEncryptor>();

        // In memory and per process: counters reset on restart.
        services.AddSingleton<IInvoiceActionThrottle, InMemoryInvoiceActionThrottle>();

        return services;
    }
}
