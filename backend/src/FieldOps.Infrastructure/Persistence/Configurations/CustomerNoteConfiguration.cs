using FieldOps.Domain.Customers;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class CustomerNoteConfiguration
    : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.ToTable("customer_notes");

        builder.HasKey(note => note.Id);

        builder.Property(note => note.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(note => note.OrganizationId)
            .IsRequired();

        builder.Property(note => note.CustomerId)
            .IsRequired();

        builder.Property(note => note.AuthorUserId)
            .IsRequired();

        builder.Property(note => note.Note)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(note => note.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(note => new { note.OrganizationId, note.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(note => note.AuthorUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
