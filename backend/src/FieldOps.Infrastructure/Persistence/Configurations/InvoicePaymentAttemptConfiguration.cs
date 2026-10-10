using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

/// <summary>SA-16 (customer-invoice-payments): <c>invoice_payment_attempts</c>.</summary>
internal sealed class InvoicePaymentAttemptConfiguration : IEntityTypeConfiguration<InvoicePaymentAttempt>
{
    public void Configure(EntityTypeBuilder<InvoicePaymentAttempt> builder)
    {
        builder.ToTable("invoice_payment_attempts", table =>
        {
            // method::text, never the new enum literal (SA-14).
            table.HasCheckConstraint("ck_invoice_payment_attempts_method", "method::text IN ('card_online','bank_transfer')");
            table.HasCheckConstraint("ck_invoice_payment_attempts_amount", "amount > 0");
            table.HasCheckConstraint(
                "ck_invoice_payment_attempts_refunded_amount",
                "refunded_amount >= 0 AND refunded_amount <= amount");
            table.HasCheckConstraint(
                "ck_invoice_payment_attempts_payment_link",
                "(status = 'succeeded' OR status = 'partially_refunded' OR status = 'refunded') = (payment_id IS NOT NULL)");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(attempt => attempt.OrganizationId)
            .IsRequired();

        builder.Property(attempt => attempt.InvoiceId)
            .IsRequired();

        // PostgreSQL enum payment_method.
        builder.Property(attempt => attempt.Method)
            .IsRequired();

        // PostgreSQL enum payment_attempt_status, DEFAULT 'pending' (also the CLR default).
        builder.Property(attempt => attempt.Status)
            .HasDefaultValue(PaymentAttemptStatus.Pending)
            .IsRequired();

        builder.Property(attempt => attempt.Amount)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(attempt => attempt.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(attempt => attempt.IdempotencyKey)
            .IsRequired();

        builder.Property(attempt => attempt.ProviderPaymentIntentId)
            .HasMaxLength(255);

        builder.Property(attempt => attempt.PaymentId);

        builder.Property(attempt => attempt.FailureCategory)
            .HasMaxLength(40);

        builder.Property(attempt => attempt.RefundedAmount)
            .HasPrecision(14, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(attempt => attempt.ExpiresAt);

        builder.Property(attempt => attempt.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(attempt => attempt.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // provider_payment_intent_id varchar(255) UNIQUE
        builder.HasIndex(attempt => attempt.ProviderPaymentIntentId)
            .IsUnique();

        // UNIQUE (organization_id, idempotency_key)
        builder.HasIndex(attempt => new { attempt.OrganizationId, attempt.IdempotencyKey })
            .IsUnique();

        // CREATE UNIQUE INDEX ux_invoice_payment_attempts_pending
        //   ON invoice_payment_attempts (invoice_id, method) WHERE status = 'pending'
        builder.HasIndex(attempt => new { attempt.InvoiceId, attempt.Method })
            .HasDatabaseName("ux_invoice_payment_attempts_pending")
            .IsUnique()
            .HasFilter("status = 'pending'");

        // CREATE INDEX ix_invoice_payment_attempts_invoice ON invoice_payment_attempts (invoice_id, created_at DESC)
        builder.HasIndex(attempt => new { attempt.InvoiceId, attempt.CreatedAt })
            .HasDatabaseName("ix_invoice_payment_attempts_invoice")
            .IsDescending(false, true);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(attempt => attempt.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, invoice_id) REFERENCES invoices (organization_id, id)
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(attempt => new { attempt.OrganizationId, attempt.InvoiceId })
            .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, payment_id) REFERENCES payments (organization_id, id)
        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(attempt => new { attempt.OrganizationId, attempt.PaymentId })
            .HasPrincipalKey(payment => new { payment.OrganizationId, payment.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
