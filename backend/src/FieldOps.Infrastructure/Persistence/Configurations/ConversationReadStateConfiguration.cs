using FieldOps.Domain.Messaging;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ConversationReadStateConfiguration : IEntityTypeConfiguration<ConversationReadState>
{
    public void Configure(EntityTypeBuilder<ConversationReadState> builder)
    {
        builder.ToTable("conversation_read_states");

        builder.HasKey(state => new { state.ConversationId, state.UserId });

        builder.Property(state => state.OrganizationId)
            .IsRequired();

        builder.Property(state => state.LastReadAt)
            .IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(state => state.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, conversation_id) REFERENCES conversations (organization_id, id)
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(state => new { state.OrganizationId, state.ConversationId })
            .HasPrincipalKey(conversation => new { conversation.OrganizationId, conversation.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(state => state.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
