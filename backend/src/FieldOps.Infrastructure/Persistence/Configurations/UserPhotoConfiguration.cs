using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class UserPhotoConfiguration : IEntityTypeConfiguration<UserPhoto>
{
    public void Configure(EntityTypeBuilder<UserPhoto> builder)
    {
        builder.ToTable("user_photos");

        // PRIMARY KEY (user_id): one photo per user. No organization_id: the photo belongs to the user.
        builder.HasKey(photo => photo.UserId);

        builder.Property(photo => photo.UserId)
            .ValueGeneratedNever();

        builder.Property(photo => photo.ContentType)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(photo => photo.Content)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(photo => photo.SizeBytes)
            .IsRequired();

        builder.Property(photo => photo.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(photo => photo.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_user_photos_content_type",
                "content_type IN ('image/png','image/jpeg')");
            table.HasCheckConstraint(
                "ck_user_photos_size_bytes",
                "size_bytes BETWEEN 1 AND 5242880");
        });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(photo => photo.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
