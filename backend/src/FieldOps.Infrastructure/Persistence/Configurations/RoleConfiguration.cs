using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(role => role.Id);

        // smallserial equivalent: smallint identity column.
        builder.Property(role => role.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(role => role.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(role => role.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(role => role.Description)
            .HasColumnType("text");

        builder.Property(role => role.IsCanonical)
            .IsRequired();

        // UNIQUE (code)
        builder.HasIndex(role => role.Code)
            .IsUnique();

        // Canonical roles seeded by the relational model.
        builder.HasData(
            new { Id = (short)1, Code = "owner", Name = "Owner", IsCanonical = true },
            new { Id = (short)2, Code = "dispatcher", Name = "Dispatcher", IsCanonical = true },
            new { Id = (short)3, Code = "technician", Name = "Technician", IsCanonical = true },
            new { Id = (short)4, Code = "accounting", Name = "Accounting", IsCanonical = true },
            new { Id = (short)5, Code = "operations_manager", Name = "Operations Manager", IsCanonical = true },
            new { Id = (short)6, Code = "viewer", Name = "Viewer", IsCanonical = true });
    }
}
