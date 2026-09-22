using FieldOps.Domain.Branches;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", table =>
        {
            table.HasCheckConstraint(
                "ck_invoices_subtotal",
                "subtotal >= 0");
            table.HasCheckConstraint(
                "ck_invoices_tax_total",
                "tax_total >= 0");
            table.HasCheckConstraint(
                "ck_invoices_total",
                "total >= 0");
            table.HasCheckConstraint(
                "ck_invoices_amount_paid",
                "amount_paid >= 0");
            table.HasCheckConstraint(
                "ck_invoices_balance_due",
                "balance_due >= 0");
            table.HasCheckConstraint(
                "ck_invoices_date_range",
                "due_date IS NULL OR issue_date IS NULL OR due_date >= issue_date");
            // Only the static amount_paid <= total relationship can be
            // enforced here; keeping amount_paid/balance_due consistent
            // with the sum of PaymentAllocation.Amount requires
            // transactional domain logic (see Invoice.cs).
            table.HasCheckConstraint(
                "ck_invoices_amount_paid_le_total",
                "amount_paid <= total");
        });

        builder.HasKey(invoice => invoice.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(invoice => new
        {
            invoice.OrganizationId,
            invoice.Id,
        });

        builder.Property(invoice => invoice.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(invoice => invoice.OrganizationId)
            .IsRequired();

        builder.Property(invoice => invoice.BranchId)
            .IsRequired();

        builder.Property(invoice => invoice.InvoiceNumber)
            .IsRequired();

        builder.Property(invoice => invoice.WorkOrderId)
            .IsRequired();

        builder.Property(invoice => invoice.CustomerId)
            .IsRequired();

        // PostgreSQL enum invoice_status, mapped in FieldOpsDbContext.
        builder.Property(invoice => invoice.Status)
            .IsRequired();

        builder.Property(invoice => invoice.IssueDate)
            .HasColumnType("date");

        builder.Property(invoice => invoice.DueDate)
            .HasColumnType("date");

        builder.Property(invoice => invoice.Currency)
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(invoice => invoice.Subtotal)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(invoice => invoice.TaxTotal)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(invoice => invoice.Total)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(invoice => invoice.AmountPaid)
            .HasPrecision(14, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(invoice => invoice.BalanceDue)
            .HasPrecision(14, 2)
            .IsRequired();

        builder.Property(invoice => invoice.Notes)
            .HasColumnType("text");

        builder.Property(invoice => invoice.SentAt);

        builder.Property(invoice => invoice.VoidedAt);

        builder.Property(invoice => invoice.VoidReason)
            .HasColumnType("text");

        builder.Property(invoice => invoice.CreatedByUserId)
            .IsRequired();

        builder.Property(invoice => invoice.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(invoice => invoice.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, invoice_number)
        builder.HasIndex(invoice => new { invoice.OrganizationId, invoice.InvoiceNumber })
            .IsUnique();

        // CREATE INDEX ix_invoices_status_due ON invoices (organization_id, status, due_date)
        builder.HasIndex(invoice => new
        {
            invoice.OrganizationId,
            invoice.Status,
            invoice.DueDate,
        })
            .HasDatabaseName("ix_invoices_status_due");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(invoice => invoice.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(invoice => invoice.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        // work_order_id REFERENCES work_orders (id): single-column, not
        // tenant-composite, exactly as defined in the relational model.
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(invoice => invoice.WorkOrderId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, customer_id) REFERENCES customers (organization_id, id)
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(invoice => new { invoice.OrganizationId, invoice.CustomerId })
            .HasPrincipalKey(customer => new { customer.OrganizationId, customer.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(invoice => invoice.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
