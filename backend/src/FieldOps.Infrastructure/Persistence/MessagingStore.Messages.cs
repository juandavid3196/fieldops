using FieldOps.Application.Features.Messaging;
using FieldOps.Domain.Messaging;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>The message reads and writes of technician-messages (BR-08 to BR-10); see <c>MessagingStore.cs</c>.</summary>
internal sealed partial class MessagingStore
{
    public async Task<MessagingStoreResult<bool>> AuthorizeAsync(
        MessagingActor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<bool>.Failure(status);
        }

        return await MessagingAccess.Conversations(dbContext, caller).AnyAsync(conversation => conversation.Id == conversationId, cancellationToken)
            ? MessagingStoreResult<bool>.Ok(true)
            : MessagingStoreResult<bool>.Failure(MessagingStatus.NotFound);
    }

    public async Task<MessagingStoreResult<MessagePage>> ListMessagesAsync(
        MessagingActor actor, Guid conversationId, Guid? before, Guid? after, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<MessagePage>.Failure(status);
        }

        if (!await MessagingAccess.Conversations(dbContext, caller).AnyAsync(conversation => conversation.Id == conversationId, cancellationToken))
        {
            return MessagingStoreResult<MessagePage>.Failure(MessagingStatus.NotFound);
        }

        var organizationId = caller.OrganizationId;
        var messages = dbContext.ConversationMessages.AsNoTracking()
            .Where(message => message.OrganizationId == organizationId && message.ConversationId == conversationId);

        List<ConversationMessage> page;
        bool hasMore;

        if (after is { } afterId)
        {
            // The cursor message is resolved inside the authorized conversation: unknown or foreign is invalid.
            var anchor = await messages.Where(message => message.Id == afterId)
                .Select(message => new { message.CreatedAt, message.Id })
                .SingleOrDefaultAsync(cancellationToken);

            if (anchor is null)
            {
                return MessagingStoreResult<MessagePage>.Failure(MessagingStatus.InvalidCursor);
            }

            var anchorAt = anchor.CreatedAt;
            var anchorId = anchor.Id;
            page = await messages
                .Where(message => message.CreatedAt > anchorAt || (message.CreatedAt == anchorAt && message.Id.CompareTo(anchorId) > 0))
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Take(MessagingRules.MessagePageSize + 1)
                .ToListAsync(cancellationToken);
            hasMore = page.Count > MessagingRules.MessagePageSize;
            page = [.. page.Take(MessagingRules.MessagePageSize)];
        }
        else
        {
            if (before is { } beforeId)
            {
                var anchor = await messages.Where(message => message.Id == beforeId)
                    .Select(message => new { message.CreatedAt, message.Id })
                    .SingleOrDefaultAsync(cancellationToken);

                if (anchor is null)
                {
                    return MessagingStoreResult<MessagePage>.Failure(MessagingStatus.InvalidCursor);
                }

                var anchorAt = anchor.CreatedAt;
                var anchorId = anchor.Id;
                messages = messages.Where(message =>
                    message.CreatedAt < anchorAt || (message.CreatedAt == anchorAt && message.Id.CompareTo(anchorId) < 0));
            }

            // The newest 50 by created_at descending then id, returned in ascending order.
            page = await messages
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .Take(MessagingRules.MessagePageSize + 1)
                .ToListAsync(cancellationToken);
            hasMore = page.Count > MessagingRules.MessagePageSize;
            page = [.. page.Take(MessagingRules.MessagePageSize).Reverse()];
        }

        var authors = await AuthorsAsync(organizationId, page.Select(message => message.AuthorUserId), cancellationToken);

        return MessagingStoreResult<MessagePage>.Ok(
            new MessagePage([.. page.Select(message => ToView(message, authors, caller.UserId))], hasMore));
    }

    public async Task<MessagingStoreResult<SentMessage>> SendAsync(
        MessagingActor actor, Guid conversationId, Guid clientMessageId, string body, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<SentMessage>.Failure(status);
        }

        var organizationId = caller.OrganizationId;
        var userId = caller.UserId;

        if (!await MessagingAccess.Conversations(dbContext, caller).AnyAsync(conversation => conversation.Id == conversationId, cancellationToken))
        {
            return MessagingStoreResult<SentMessage>.Failure(MessagingStatus.NotFound);
        }

        async Task<ConversationMessage?> FindAsync() =>
            await dbContext.ConversationMessages.AsNoTracking()
                .Where(message => message.OrganizationId == organizationId
                    && message.ConversationId == conversationId
                    && message.AuthorUserId == userId
                    && message.ClientMessageId == clientMessageId)
                .SingleOrDefaultAsync(cancellationToken);

        async Task<MessagingStoreResult<SentMessage>> StoredAsync(ConversationMessage stored, bool created)
        {
            var authors = await AuthorsAsync(organizationId, [stored.AuthorUserId], cancellationToken);

            return MessagingStoreResult<SentMessage>.Ok(new SentMessage(ToView(stored, authors, userId), created));
        }

        // BR-09: a repeat of (conversation, author, clientMessageId) returns the stored message and writes nothing.
        if (await FindAsync() is { } existing)
        {
            return await StoredAsync(existing, false);
        }

        var message = ConversationMessage.Create(organizationId, conversationId, userId, clientMessageId, body, timeProvider.GetUtcNow());

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            dbContext.ConversationMessages.Add(message);
            await dbContext.SaveChangesAsync(cancellationToken);

            var createdAt = message.CreatedAt;

            // BR-03: last_message_at never moves backwards; BR-09: the author's read state advances to the new message.
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE conversations
                SET last_message_at = GREATEST(last_message_at, {createdAt})
                WHERE organization_id = {organizationId} AND id = {conversationId}
                """,
                cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO conversation_read_states (organization_id, conversation_id, user_id, last_read_at)
                VALUES ({organizationId}, {conversationId}, {userId}, {createdAt})
                ON CONFLICT (conversation_id, user_id)
                DO UPDATE SET last_read_at = GREATEST(conversation_read_states.last_read_at, EXCLUDED.last_read_at)
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: UniqueViolation,
            ConstraintName: ConversationMessageConfiguration.ClientMessageIndex,
        })
        {
            // A concurrent send of the same clientMessageId won; the transaction was rolled back on disposal.
            dbContext.ChangeTracker.Clear();

            return await FindAsync() is { } winner
                ? await StoredAsync(winner, false)
                : MessagingStoreResult<SentMessage>.Failure(MessagingStatus.NotFound);
        }

        return await StoredAsync(message, true);
    }

    public async Task<MessagingStoreResult<bool>> MarkReadAsync(
        MessagingActor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<bool>.Failure(status);
        }

        if (!await MessagingAccess.Conversations(dbContext, caller).AnyAsync(conversation => conversation.Id == conversationId, cancellationToken))
        {
            return MessagingStoreResult<bool>.Failure(MessagingStatus.NotFound);
        }

        var organizationId = caller.OrganizationId;
        var userId = caller.UserId;

        // BR-10: one statement, the latest message's created_at, never backwards; no messages inserts nothing.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO conversation_read_states (organization_id, conversation_id, user_id, last_read_at)
            SELECT {organizationId}, {conversationId}, {userId}, max(created_at)
            FROM conversation_messages
            WHERE organization_id = {organizationId} AND conversation_id = {conversationId}
            HAVING count(*) > 0
            ON CONFLICT (conversation_id, user_id)
            DO UPDATE SET last_read_at = GREATEST(conversation_read_states.last_read_at, EXCLUDED.last_read_at)
            """,
            cancellationToken);

        return MessagingStoreResult<bool>.Ok(true);
    }

    private static MessageView ToView(ConversationMessage message, IReadOnlyDictionary<Guid, MessageAuthor> authors, Guid callerUserId)
    {
        var mine = message.AuthorUserId == callerUserId;

        return new MessageView(
            message.Id,
            mine ? message.ClientMessageId : null,
            authors[message.AuthorUserId],
            mine,
            message.Body,
            message.CreatedAt);
    }
}
