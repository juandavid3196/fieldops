using FieldOps.Domain.Customers;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerSignoffConfiguration
    : IEntityTypeConfiguration<CustomerSignoff>
{
    public void Configure(EntityTypeBuilder<CustomerSignoff> builder)
    {
        builder.ToTable("customer_signoffs");

        builder.HasKey(signoff => signoff.Id);

        builder.Property(signoff => signoff.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(signoff => signoff.VisitId)
            .IsRequired();

        builder.Property(signoff => signoff.SignerName)
            .HasMaxLength(180);

        builder.Property(signoff => signoff.SignerContactId);

        // The signature is optional; an absence reason may be recorded
        // instead when the customer was not present to sign.
        builder.Property(signoff => signoff.SignatureStorageKey)
            .HasColumnType("text");

        builder.Property(signoff => signoff.Accepted)
            .IsRequired();

        builder.Property(signoff => signoff.Comments)
            .HasColumnType("text");

        builder.Property(signoff => signoff.AbsenceReason)
            .HasColumnType("text");

        builder.Property(signoff => signoff.SignedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE visit_id: one signoff per visit.
        builder.HasIndex(signoff => signoff.VisitId)
            .IsUnique();

        // No ON DELETE CASCADE on this FK in the relational model, unlike
        // every other visit_* child table: the signed record is preserved
        // even if the visit itself would otherwise be removed.
        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(signoff => signoff.VisitId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(signoff => signoff.SignerContactId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
