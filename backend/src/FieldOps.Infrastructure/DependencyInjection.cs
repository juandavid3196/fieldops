using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Infrastructure;

public static class DependencyInjection
{
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

        return services;
    }
}
