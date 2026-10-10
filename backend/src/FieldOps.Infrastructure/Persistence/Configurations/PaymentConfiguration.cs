using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_amount", "amount > 0");

            // SA-15 (customer-invoice-payments): the constraints compare method::text, never the new enum literal.
            table.HasCheckConstraint("ck_payments_refunded_amount_range", "refunded_amount BETWEEN 0 AND amount");
            table.HasCheckConstraint("ck_payments_status_succeeded_no_refund", "(status = 'succeeded') = (refunded_amount = 0)");
            table.HasCheckConstraint("ck_payments_status_refunded_full", "(status = 'refunded') = (refunded_amount = amount)");
            table.HasCheckConstraint("ck_payments_card_online_no_receiver", "(method::text = 'card_online') = (received_by_user_id IS NULL)");
            table.HasCheckConstraint("ck_payments_card_online_last4", "(method::text = 'card_online') = (card_last4 IS NOT NULL)");
        });

        builder.HasKey(payment => payment.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(payment => new
        {
            payment.OrganizationId,
            payment.Id,
        });

        builder.Property(payment => payment.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(payment => payment.OrganizationId)
            .IsRequired();

        builder.Property(payment => payment.CustomerId)
            .IsRequired();

        builder.Property(payment => payment.PaymentNumber)
            .IsRequired();

        // PostgreSQL enum payment_method, mapped in FieldOpsDbContext.
        builder.Property(payment => payment.Method)
            .IsRequired();

        builder.Property(payment => payment.Amount)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(payment => payment.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(payment => payment.PaidAt)
            .IsRequired();

        builder.Property(payment => payment.ExternalReference)
            .HasMaxLength(160);

        builder.Property(payment => payment.Notes)
            .HasColumnType("text");

        builder.Property(payment => payment.RecordedByUserId);

        // SA-12 (invoices-payments-management): no default; SA-15 makes it nullable (null only for card_online).
        builder.Property(payment => payment.ReceivedByUserId);

        builder.Property(payment => payment.IdempotencyKey)
            .IsRequired();

        builder.Property(payment => payment.ReceiptStorageKey)
            .HasColumnType("text");

        builder.Property(payment => payment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // SA-15: PostgreSQL enum payment_status, DEFAULT 'succeeded' (also the CLR default, so the database default and an
        // explicit Succeeded are the same value).
        builder.Property(payment => payment.Status)
            .HasDefaultValue(PaymentStatus.Succeeded)
            .IsRequired();

        builder.Property(payment => payment.RefundedAmount)
            .HasPrecision(14, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(payment => payment.ReceiptNumber)
            .HasMaxLength(60);

        builder.Property(payment => payment.ReceiptSentAt);

        builder.Property(payment => payment.CardBrand)
            .HasMaxLength(20);

        builder.Property(payment => payment.CardLast4)
            .HasMaxLength(4)
            .IsFixedLength();

        builder.Ignore(payment => payment.NetAmount);

        // UNIQUE (organization_id, receipt_number): several null receipt numbers are allowed.
        builder.HasIndex(payment => new { payment.OrganizationId, payment.ReceiptNumber })
            .IsUnique();

        // UNIQUE (organization_id, payment_number)
        builder.HasIndex(payment => new { payment.OrganizationId, payment.PaymentNumber })
            .IsUnique();

        // CREATE INDEX ix_payments_customer_date
        //   ON payments (organization_id, customer_id, paid_at DESC)
        builder.HasIndex(payment => new
        {
            payment.OrganizationId,
            payment.CustomerId,
            payment.PaidAt,
        })
            .HasDatabaseName("ix_payments_customer_date")
            .IsDescending(false, false, true);

        // UNIQUE (organization_id, idempotency_key): the backstop of a duplicate record request (SA-12).
        builder.HasIndex(payment => new { payment.OrganizationId, payment.IdempotencyKey })
            .IsUnique();

        // CREATE INDEX ix_payments_org_paid_at ON payments (organization_id, paid_at DESC) (SA-13)
        builder.HasIndex(payment => new { payment.OrganizationId, payment.PaidAt })
            .HasDatabaseName("ix_payments_org_paid_at")
            .IsDescending(false, true);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(payment => payment.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(payment => new { payment.OrganizationId, payment.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(payment => payment.RecordedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(payment => payment.ReceivedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
