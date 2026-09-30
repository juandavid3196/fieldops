using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public const string OpenUserIndexName = "ux_password_reset_tokens_open_user";

    public const string ExpiresAfterCreatedConstraintName = "ck_password_reset_tokens_expires_after_created";

    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable(
            "password_reset_tokens",
            table => table.HasCheckConstraint(
                ExpiresAfterCreatedConstraintName,
                "expires_at > created_at"));

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(token => token.UserId)
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(token => token.ExpiresAt)
            .IsRequired();

        builder.Property(token => token.UsedAt);

        builder.Property(token => token.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (token_hash)
        builder.HasIndex(token => token.TokenHash)
            .IsUnique();

        // At most one unused token per user (BR-03).
        builder.HasIndex(token => token.UserId)
            .IsUnique()
            .HasDatabaseName(OpenUserIndexName)
            .HasFilter("used_at IS NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
