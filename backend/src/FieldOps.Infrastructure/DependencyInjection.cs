using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Branches;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Catalog;
using FieldOps.Application.Features.ChecklistTemplates;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.PasswordResets;
using FieldOps.Application.Features.PublicRequests;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.Users;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Authentication;
using FieldOps.Infrastructure.Email;
using FieldOps.Infrastructure.PasswordResets;
using FieldOps.Infrastructure.Pdf;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<ICatalogCategoryStore, CatalogCategoryStore>();
        services.AddScoped<IBranchScopeResolver, BranchScopeResolver>();
        services.AddScoped<ICustomerStore, CustomerStore>();
        services.AddScoped<ITeamStore, TeamStore>();
        services.AddScoped<ICustomerDetailStore, CustomerDetailStore>();
        services.AddScoped<IUserAccessStore, UserAccessStore>();
        services.AddScoped<IInvitationAcceptanceStore, InvitationAcceptanceStore>();
        services.AddScoped<IPasswordResetStore, PasswordResetStore>();
        services.AddScoped<IPublicServiceRequestStore, PublicServiceRequestStore>();
        services.AddScoped<IPublicRequestConfirmationSender, PublicRequestConfirmationSender>();
        services.AddScoped<IServiceRequestStore, ServiceRequestStore>();
        services.AddScoped<IRequestInformationNotifier, RequestInformationNotifier>();
        services.AddScoped<IAssessmentNotifier, AssessmentNotifier>();
        services.AddScoped<IQuoteStore, QuoteStore>();
        services.AddScoped<IQuoteNotifier, QuoteNotifier>();
        services.AddScoped<IQuoteLinkStore, QuoteLinkStore>();
        services.AddScoped<IWorkOrderStore, WorkOrderStore>();
        services.AddScoped<IDispatchStore, DispatchStore>();
        services.AddScoped<IBillingReviewStore, BillingReviewStore>();
        services.AddScoped<IInvoiceDeliveryStore, InvoiceDeliveryStore>();
        services.AddScoped<IInvoicePaymentStore>(provider => (InvoiceDeliveryStore)provider.GetRequiredService<IInvoiceDeliveryStore>());
        services.AddScoped<IInvoiceHubStore, InvoiceHubStore>();
        services.AddScoped<IPaymentReceiptNotifier, PaymentReceiptNotifier>();
        services.AddScoped<IInvoiceLinkStore, InvoiceLinkStore>();
        services.AddScoped<IInvoiceNotifier, InvoiceNotifier>();
        services.AddScoped<IVisitNotifier, VisitNotifier>();
        services.AddScoped<ITravelNotifier, TravelNotifier>();
        services.AddScoped<ITechnicianVisitStore, TechnicianVisitStore>();
        services.AddScoped<IChecklistTemplateStore, ChecklistTemplateStore>();
        services.AddSingleton<IQuotePdfRenderer, MigraDocQuotePdfRenderer>();
        services.AddSingleton<IInvoicePdfRenderer, MigraDocInvoicePdfRenderer>();
        services.AddEmail(configuration);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // In memory and per process: counters reset on restart.
        services.AddSingleton<ISignInThrottle, InMemorySignInThrottle>();
        services.AddSingleton<IPasswordResetEmailThrottle, InMemoryPasswordResetEmailThrottle>();

        // Bounded in-memory dispatch of reset emails after the response (BR-11).
        services.AddSingleton<PasswordResetEmailQueue>();
        services.AddSingleton<IPasswordResetEmailQueue>(provider => provider.GetRequiredService<PasswordResetEmailQueue>());
        services.AddHostedService<PasswordResetEmailDispatcher>();

        return services;
    }
}
