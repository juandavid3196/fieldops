using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class PaymentAllocationConfiguration
    : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations", table => table.HasCheckConstraint(
            "ck_payment_allocations_amount",
            "amount > 0"));

        builder.HasKey(allocation => allocation.Id);

        builder.Property(allocation => allocation.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(allocation => allocation.PaymentId)
            .IsRequired();

        builder.Property(allocation => allocation.InvoiceId)
            .IsRequired();

        builder.Property(allocation => allocation.Amount)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(allocation => allocation.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (payment_id, invoice_id): one allocation per Payment and
        // Invoice combination.
        builder.HasIndex(allocation => new { allocation.PaymentId, allocation.InvoiceId })
            .IsUnique();

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(allocation => allocation.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(allocation => allocation.InvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
