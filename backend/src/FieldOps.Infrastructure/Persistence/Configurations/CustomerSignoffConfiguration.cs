using FieldOps.Domain.Customers;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerSignoffConfiguration
    : IEntityTypeConfiguration<CustomerSignoff>
{
    public void Configure(EntityTypeBuilder<CustomerSignoff> builder)
    {
        builder.ToTable("customer_signoffs", table =>
        {
            table.HasCheckConstraint(
                "ck_customer_signoffs_acknowledgement_method",
                "acknowledgement_method IN ('signed','customer_absent','customer_refused','remote_confirmation')");
            table.HasCheckConstraint(
                "ck_customer_signoffs_signer_relationship",
                "signer_relationship IN ('customer','family_member','tenant','property_manager','employee','other')");
            table.HasCheckConstraint(
                "ck_customer_signoffs_signature_mime_type",
                "signature_mime_type = 'image/png'");
            table.HasCheckConstraint(
                "ck_customer_signoffs_signature_content_pair",
                "(signature_content IS NULL) = (signature_mime_type IS NULL)");
            table.HasCheckConstraint(
                "ck_customer_signoffs_signed_has_signature",
                "(acknowledgement_method = 'signed') = (signature_content IS NOT NULL OR signature_storage_key IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_customer_signoffs_signature_size",
                "signature_content IS NULL OR octet_length(signature_content) <= 524288");
        });

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

        // SA-03 (mobile-job-completion): how the customer acknowledged the service and who recorded it.
        builder.Property(signoff => signoff.AcknowledgementMethod)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(signoff => signoff.SignerRelationship)
            .HasMaxLength(40);

        // Inline PNG signature, like SA-01 evidence; never projected into responses.
        builder.Property(signoff => signoff.SignatureContent)
            .HasColumnType("bytea");

        builder.Property(signoff => signoff.SignatureMimeType)
            .HasMaxLength(40);

        builder.Property(signoff => signoff.ReviewConfirmed)
            .HasDefaultValue(false)
            .HasSentinel(false)
            .IsRequired();

        builder.Property(signoff => signoff.RecordedByUserId)
            .IsRequired();

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

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(signoff => signoff.RecordedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<CustomerContact>()
            .WithMany()
            .HasForeignKey(signoff => signoff.SignerContactId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
