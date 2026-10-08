using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceAccessTokenConfiguration : IEntityTypeConfiguration<InvoiceAccessToken>
{
    public void Configure(EntityTypeBuilder<InvoiceAccessToken> builder)
    {
        builder.ToTable("invoice_access_tokens", table => table.HasCheckConstraint(
            "ck_invoice_access_tokens_expires_after_created",
            "expires_at > created_at"));

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(token => token.OrganizationId)
            .IsRequired();

        builder.Property(token => token.InvoiceId)
            .IsRequired();

        // Only the SHA-256 hex of the raw token is stored (invoice-draft-delivery BR-18).
        builder.Property(token => token.TokenHash)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(token => token.ExpiresAt)
            .IsRequired();

        builder.Property(token => token.RevokedAt);

        builder.Property(token => token.CreatedByUserId)
            .IsRequired();

        builder.Property(token => token.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // token_hash text NOT NULL UNIQUE
        builder.HasIndex(token => token.TokenHash)
            .IsUnique();

        // CREATE INDEX ix_invoice_access_tokens_invoice ON invoice_access_tokens (invoice_id) WHERE revoked_at IS NULL
        builder.HasIndex(token => token.InvoiceId)
            .HasDatabaseName("ix_invoice_access_tokens_invoice")
            .HasFilter("revoked_at IS NULL");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(token => token.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, invoice_id) REFERENCES invoices (organization_id, id)
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(token => new { token.OrganizationId, token.InvoiceId })
            .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
