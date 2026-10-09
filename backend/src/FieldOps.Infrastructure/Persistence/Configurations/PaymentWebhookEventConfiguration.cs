using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

/// <summary>SA-17 (customer-invoice-payments): <c>payment_webhook_events</c>; no event payload is stored.</summary>
internal sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("payment_webhook_events", table => table.HasCheckConstraint(
            "ck_payment_webhook_events_outcome",
            "outcome IN ('applied','no_effect','ignored','needs_attention')"));

        builder.HasKey(webhookEvent => webhookEvent.Id);

        builder.Property(webhookEvent => webhookEvent.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(webhookEvent => webhookEvent.Provider)
            .HasMaxLength(20)
            .HasDefaultValue(PaymentWebhookProviders.Stripe)
            .IsRequired();

        builder.Property(webhookEvent => webhookEvent.ProviderEventId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(webhookEvent => webhookEvent.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(webhookEvent => webhookEvent.OrganizationId);

        builder.Property(webhookEvent => webhookEvent.AttemptId);

        builder.Property(webhookEvent => webhookEvent.Outcome)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(webhookEvent => webhookEvent.ReceivedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (provider, provider_event_id)
        builder.HasIndex(webhookEvent => new { webhookEvent.Provider, webhookEvent.ProviderEventId })
            .IsUnique();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(webhookEvent => webhookEvent.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<InvoicePaymentAttempt>()
            .WithMany()
            .HasForeignKey(webhookEvent => webhookEvent.AttemptId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
