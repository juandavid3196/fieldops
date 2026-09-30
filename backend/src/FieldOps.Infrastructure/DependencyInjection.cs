using System.Globalization;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Branches;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.Users;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Authentication;
using FieldOps.Infrastructure.Invitations;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure;

public static class DependencyInjection
{
    public const string PostgreSqlHealthCheckName = "postgresql";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(
            "FieldOpsDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'FieldOpsDatabase' was not found.");
        }

        services.AddDbContext<FieldOpsDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql
                        .MapEnum<UserStatus>("user_status")
                        .MapEnum<CustomerType>("customer_type")
                        .MapEnum<CatalogItemType>("catalog_item_type")
                        .MapEnum<RequestStatus>("request_status")
                        .MapEnum<AssessmentStatus>("assessment_status")
                        .MapEnum<MessageVisibility>("message_visibility")
                        .MapEnum<QuoteStatus>("quote_status")
                        .MapEnum<WorkOrderStatus>("work_order_status")
                        .MapEnum<VisitStatus>("visit_status")
                        .MapEnum<InvoiceStatus>("invoice_status")
                        .MapEnum<PaymentMethod>("payment_method")
                        .MapEnum<NotificationStatus>("notification_status"))
                .UseSnakeCaseNamingConvention());

        services
            .AddHealthChecks()
            .AddDbContextCheck<FieldOpsDbContext>(PostgreSqlHealthCheckName);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuthenticationStore, AuthenticationStore>();
        services.AddScoped<IOrganizationRegistrationStore, OrganizationRegistrationStore>();
        services.AddScoped<IOrganizationSettingsStore, OrganizationSettingsStore>();
        services.AddScoped<IOrganizationLogoStore, OrganizationLogoStore>();
        services.AddScoped<IBranchStore, BranchStore>();
        services.AddScoped<IUserAccessStore, UserAccessStore>();
        services.AddScoped<IInvitationAcceptanceStore, InvitationAcceptanceStore>();
        services.AddSmtpInvitationDelivery(configuration);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // In memory and per process: counters reset on restart.
        services.AddSingleton<ISignInThrottle, InMemorySignInThrottle>();

        return services;
    }

    // The SMTP adapter is the only delivery adapter (BR-15). Values are read
    // one by one so an unparsable number fails validation on start (0) rather
    // than throwing from the binder; secrets are never logged or echoed.
    private static IServiceCollection AddSmtpInvitationDelivery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SmtpSettings>()
            .Configure(settings =>
            {
                var section = configuration.GetSection(SmtpSettings.SectionName);

                settings.Host = section["Host"] ?? string.Empty;
                settings.Port = int.TryParse(section["Port"], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                    ? port
                    : 0;
                settings.EnableSsl = bool.TryParse(section["EnableSsl"], out var enableSsl) && enableSsl;
                settings.SenderAddress = section["SenderAddress"] ?? string.Empty;
                settings.SenderName = section["SenderName"] ?? "FieldOps";
                settings.UserName = section["UserName"];
                settings.Password = section["Password"];
            })
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SmtpSettings>, SmtpSettingsValidator>();
        services.AddSingleton<IInvitationDelivery, SmtpInvitationDelivery>();

        return services;
    }
}
