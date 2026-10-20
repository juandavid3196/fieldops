using FieldOps.Domain.Messaging;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldOps.Infrastructure.Persistence.Configurations;

internal sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public const string ClientMessageIndex = "ux_conversation_messages_client";

    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("conversation_messages", table =>
            table.HasCheckConstraint("ck_conversation_messages_body", "length(btrim(body)) > 0"));

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(message => message.OrganizationId)
            .IsRequired();

        builder.Property(message => message.ConversationId)
            .IsRequired();

        builder.Property(message => message.AuthorUserId)
            .IsRequired();

        builder.Property(message => message.ClientMessageId)
            .IsRequired();

        builder.Property(message => message.Body)
            .HasMaxLength(ConversationMessage.MaxBodyLength)
            .IsRequired();

        builder.Property(message => message.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // UNIQUE (conversation_id, author_user_id, client_message_id)
        builder.HasIndex(message => new { message.ConversationId, message.AuthorUserId, message.ClientMessageId })
            .IsUnique()
            .HasDatabaseName(ClientMessageIndex);

        // CREATE INDEX ix_conversation_messages_conversation ON conversation_messages (conversation_id, created_at DESC, id DESC)
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt, message.Id })
            .HasDatabaseName("ix_conversation_messages_conversation")
            .IsDescending(false, true, true);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(message => message.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction);

        // FOREIGN KEY (organization_id, conversation_id) REFERENCES conversations (organization_id, id)
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(message => new { message.OrganizationId, message.ConversationId })
            .HasPrincipalKey(conversation => new { conversation.OrganizationId, conversation.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(message => message.AuthorUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
