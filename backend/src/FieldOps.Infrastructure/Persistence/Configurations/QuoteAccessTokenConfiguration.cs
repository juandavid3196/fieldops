using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class QuoteAccessTokenConfiguration : IEntityTypeConfiguration<QuoteAccessToken>
{
    public void Configure(EntityTypeBuilder<QuoteAccessToken> builder)
    {
        builder.ToTable("quote_access_tokens", table => table.HasCheckConstraint(
            "ck_quote_access_tokens_expires_after_created",
            "expires_at > created_at"));

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(token => token.OrganizationId)
            .IsRequired();

        builder.Property(token => token.QuoteVersionId)
            .IsRequired();

        // Only the SHA-256 hex of the raw token is stored (quote-builder BR-27).
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

        // CREATE INDEX ix_quote_access_tokens_version ON quote_access_tokens (quote_version_id) WHERE revoked_at IS NULL
        builder.HasIndex(token => token.QuoteVersionId)
            .HasDatabaseName("ix_quote_access_tokens_version")
            .HasFilter("revoked_at IS NULL");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(token => token.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, quote_version_id) REFERENCES quote_versions (organization_id, id)
        builder.HasOne<QuoteVersion>()
            .WithMany()
            .HasForeignKey(token => new { token.OrganizationId, token.QuoteVersionId })
            .HasPrincipalKey(version => new { version.OrganizationId, version.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
