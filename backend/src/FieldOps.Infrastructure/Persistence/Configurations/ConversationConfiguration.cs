using FieldOps.Domain.Branches;
using FieldOps.Domain.Messaging;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public const string JobIndex = "ux_conversations_job";

    public const string TeamIndex = "ux_conversations_team";

    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", table =>
        {
            table.HasCheckConstraint("ck_conversations_type", "type IN ('job', 'team')");
            table.HasCheckConstraint("ck_conversations_job_work_order", "(type = 'job') = (work_order_id IS NOT NULL)");
        });

        builder.HasKey(conversation => conversation.Id);

        // UNIQUE (organization_id, id): target of the composite tenant foreign keys.
        builder.HasAlternateKey(conversation => new { conversation.OrganizationId, conversation.Id });

        builder.Property(conversation => conversation.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(conversation => conversation.OrganizationId)
            .IsRequired();

        builder.Property(conversation => conversation.BranchId)
            .IsRequired();

        builder.Property(conversation => conversation.Type)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(conversation => conversation.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // CREATE UNIQUE INDEX ux_conversations_job ON conversations (work_order_id) WHERE type = 'job'
        builder.HasIndex(conversation => conversation.WorkOrderId)
            .IsUnique()
            .HasDatabaseName(JobIndex)
            .HasFilter("type = 'job'");

        // CREATE UNIQUE INDEX ux_conversations_team ON conversations (branch_id) WHERE type = 'team'
        builder.HasIndex(conversation => conversation.BranchId)
            .IsUnique()
            .HasDatabaseName(TeamIndex)
            .HasFilter("type = 'team'");

        // CREATE INDEX ix_conversations_org_branch_last ON conversations (organization_id, branch_id, last_message_at DESC)
        builder.HasIndex(conversation => new { conversation.OrganizationId, conversation.BranchId, conversation.LastMessageAt })
            .HasDatabaseName("ix_conversations_org_branch_last")
            .IsDescending(false, false, true);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(conversation => conversation.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, branch_id) REFERENCES branches (organization_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(conversation => new { conversation.OrganizationId, conversation.BranchId })
            .HasPrincipalKey(branch => new { branch.OrganizationId, branch.Id })
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, work_order_id) REFERENCES work_orders (organization_id, id)
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(conversation => new { conversation.OrganizationId, conversation.WorkOrderId })
            .HasPrincipalKey(order => new { order.OrganizationId, order.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
