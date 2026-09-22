using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(auditLog => auditLog.Id);

        // bigserial equivalent: bigint identity column.
        builder.Property(auditLog => auditLog.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(auditLog => auditLog.OrganizationId)
            .IsRequired();

        builder.Property(auditLog => auditLog.ActorUserId);

        builder.Property(auditLog => auditLog.Action)
            .HasMaxLength(100)
            .IsRequired();

        // Kept generic (no foreign key): an audit record may reference any
        // domain entity, so entity_type/entity_id are plain columns rather
        // than a typed relationship.
        builder.Property(auditLog => auditLog.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(auditLog => auditLog.EntityId);

        builder.Property(auditLog => auditLog.BranchId);

        builder.Property(auditLog => auditLog.BeforeData)
            .HasColumnType("jsonb");

        builder.Property(auditLog => auditLog.AfterData)
            .HasColumnType("jsonb");

        builder.Property(auditLog => auditLog.Metadata)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(auditLog => auditLog.IpAddress)
            .HasColumnType("inet");

        builder.Property(auditLog => auditLog.OccurredAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE INDEX ix_audit_entity
        //   ON audit_logs (organization_id, entity_type, entity_id, occurred_at DESC)
        builder.HasIndex(auditLog => new
        {
            auditLog.OrganizationId,
            auditLog.EntityType,
            auditLog.EntityId,
            auditLog.OccurredAt,
        })
            .HasDatabaseName("ix_audit_entity")
            .IsDescending(false, false, false, true);

        // CREATE INDEX ix_audit_actor
        //   ON audit_logs (organization_id, actor_user_id, occurred_at DESC)
        builder.HasIndex(auditLog => new
        {
            auditLog.OrganizationId,
            auditLog.ActorUserId,
            auditLog.OccurredAt,
        })
            .HasDatabaseName("ix_audit_actor")
            .IsDescending(false, false, true);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(auditLog => auditLog.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // Historical audit records must not be cascade deleted when a User
        // or Branch is removed: NoAction preserves them (and blocks the
        // removal of a User/Branch that still has audit history).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(auditLog => auditLog.ActorUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(auditLog => auditLog.BranchId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
