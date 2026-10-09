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
        builder.ToTable("payments", table => table.HasCheckConstraint(
            "ck_payments_amount",
            "amount > 0"));

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

        // SA-12 (invoices-payments-management): NOT NULL, no default.
        builder.Property(payment => payment.ReceivedByUserId)
            .IsRequired();

        builder.Property(payment => payment.IdempotencyKey)
            .IsRequired();

        builder.Property(payment => payment.ReceiptStorageKey)
            .HasColumnType("text");

        builder.Property(payment => payment.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

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
