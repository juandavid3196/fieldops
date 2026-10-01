using FieldOps.Domain.Branches;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianProfileConfiguration
    : IEntityTypeConfiguration<TechnicianProfile>
{
    public const string OrgUserIndexName = "ux_technician_profiles_org_user";

    /// <summary>Created by raw SQL in the migration: EF cannot model the <c>lower(email)</c> expression.</summary>
    public const string OrgEmailIndexName = "ux_technician_profiles_org_email";

    public const string EmployeeCodeIndexName = "ix_technician_profiles_organization_id_employee_code";

    public void Configure(EntityTypeBuilder<TechnicianProfile> builder)
    {
        builder.ToTable("technician_profiles", table => table.HasCheckConstraint(
            "ck_technician_profiles_status",
            "status IN ('active','inactive','suspended')"));

        builder.HasKey(technician => technician.Id);

        // UNIQUE (organization_id, id): target of composite tenant foreign keys.
        builder.HasAlternateKey(technician => new
        {
            technician.OrganizationId,
            technician.Id,
        });

        builder.Property(technician => technician.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(technician => technician.OrganizationId)
            .IsRequired();

        builder.Property(technician => technician.BranchId)
            .IsRequired();

        builder.Property(technician => technician.OrganizationUserId);

        builder.Property(technician => technician.EmployeeCode)
            .HasMaxLength(50);

        builder.Property(technician => technician.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(technician => technician.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(technician => technician.Email)
            .HasMaxLength(254);

        builder.Property(technician => technician.Phone)
            .HasMaxLength(40);

        // varchar(30) with CHECK, not a native PostgreSQL enum type: preserved
        // as a lowercase string conversion to match the relational model.
        // The sentinel keeps an explicit Active from being replaced by the
        // database default (also 'active') on insert.
        builder.Property(technician => technician.Status)
            .HasConversion(
                status => status.ToString().ToLowerInvariant(),
                value => Enum.Parse<TechnicianStatus>(value, ignoreCase: true))
            .HasMaxLength(30)
            .HasDefaultValueSql("'active'")
            .HasSentinel(TechnicianStatus.Active)
            .IsRequired();

        builder.Property(technician => technician.ColorHex)
            .HasMaxLength(7)
            .IsFixedLength();

        builder.Property(technician => technician.Notes)
            .HasColumnType("text");

        builder.Property(technician => technician.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(technician => technician.UpdatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (organization_id, employee_code)
        builder.HasIndex(technician => new { technician.OrganizationId, technician.EmployeeCode })
            .IsUnique();

        // CREATE UNIQUE INDEX ux_technician_profiles_org_user
        //   ON technician_profiles (organization_user_id) WHERE organization_user_id IS NOT NULL
        builder.HasIndex(technician => technician.OrganizationUserId)
            .IsUnique()
            .HasFilter("organization_user_id IS NOT NULL")
            .HasDatabaseName(OrgUserIndexName);

        // ux_technician_profiles_org_email ON (organization_id, lower(email)) WHERE email IS NOT NULL is an
        // expression index EF cannot model; the migration creates it with raw SQL (BR-22).

        // CREATE INDEX ix_technicians_branch_status
        //   ON technician_profiles (organization_id, branch_id, status)
        builder.HasIndex(technician => new
        {
            technician.OrganizationId,
            technician.BranchId,
            technician.Status,
        })
            .HasDatabaseName("ix_technicians_branch_status");

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(technician => technician.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(technician => technician.BranchId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, organization_user_id)
        //   REFERENCES organization_users (organization_id, id); optional link.
        builder.HasOne<OrganizationUser>()
            .WithMany()
            .HasForeignKey(technician => new
            {
                technician.OrganizationId,
                technician.OrganizationUserId,
            })
            .HasPrincipalKey(organizationUser => new
            {
                organizationUser.OrganizationId,
                organizationUser.Id,
            })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
