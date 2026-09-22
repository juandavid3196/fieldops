using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration
    : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(permission => permission.Id);

        // smallserial equivalent: smallint identity column.
        builder.Property(permission => permission.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(permission => permission.Code)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(permission => permission.Description)
            .HasColumnType("text")
            .IsRequired();

        // UNIQUE (code)
        builder.HasIndex(permission => permission.Code)
            .IsUnique();
    }
}
