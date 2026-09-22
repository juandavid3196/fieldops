using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(user => user.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(user => user.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(user => user.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(user => user.Phone)
            .HasMaxLength(40);

        // PostgreSQL enum user_status, mapped in FieldOpsDbContext.
        builder.Property(user => user.Status)
            .IsRequired();

        builder.Property(user => user.EmailVerifiedAt);

        builder.Property(user => user.LastLoginAt);

        builder.Property(user => user.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (email)
        builder.HasIndex(user => user.Email)
            .IsUnique();
    }
}
