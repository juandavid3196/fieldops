using FieldOps.Application.Features.PasswordResets;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.Users;
using FieldOps.Domain.Users;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class PasswordResetStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IPasswordResetStore
{
    // Stored emails are already normalized, so an exact match uses UNIQUE (email).
    public Task<PasswordResetEligibleUser?> FindEligibleUserAsync(
        string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking()
            .Where(user => user.Email == normalizedEmail && user.Status == UserStatus.Active)
            .Select(user => new PasswordResetEligibleUser(user.Id, user.Email, user.FirstName))
            .SingleOrDefaultAsync(cancellationToken);

    // BR-03: lock the users row, delete the user's unused tokens, insert the new one.
    public async Task<bool> ReplaceAsync(Guid userId, string tokenHash, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM users
                WHERE id = {userId} AND status = 'active'
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            return false;
        }

        await dbContext.PasswordResetTokens
            .Where(token => token.UserId == userId && token.UsedAt == null)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.PasswordResetTokens.Add(PasswordResetToken.Create(userId, tokenHash, timeProvider.GetUtcNow()));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == PasswordResetTokenConfiguration.OpenUserIndexName)
        {
            // Fallback for a race the row lock should already prevent: the
            // concurrent request created the open token and its email.
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();

            return false;
        }

        return true;
    }

    public async Task<PasswordResetAccountView?> FindUsableAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var email = await (
            from token in dbContext.PasswordResetTokens.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on token.UserId equals user.Id
            where token.TokenHash == tokenHash
                && token.UsedAt == null
                && token.ExpiresAt > now
                && user.Status == UserStatus.Active
            select user.Email)
            .SingleOrDefaultAsync(cancellationToken);

        return email is null ? null : new PasswordResetAccountView(email);
    }

    // BR-14: lock the token row by hash, re-check BR-02 and BR-09 under the lock.
    public async Task<PasswordResetResult<NoValue>> ConfirmAsync(
        ConfirmPasswordResetRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var ids = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM password_reset_tokens
                WHERE token_hash = {request.TokenHash}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count != 1)
        {
            return PasswordResetResult<NoValue>.Gone();
        }

        var tokenId = ids[0];
        var now = timeProvider.GetUtcNow();
        var token = await dbContext.PasswordResetTokens.SingleOrDefaultAsync(
            candidate => candidate.Id == tokenId, cancellationToken);

        if (token is null || !token.IsUsable(now))
        {
            return PasswordResetResult<NoValue>.Gone();
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == token.UserId, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            return PasswordResetResult<NoValue>.Gone();
        }

        if (string.Equals(request.Password, user.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            // The token is not consumed; nothing was written.
            return PasswordResetResult<NoValue>.Invalid(
                "password",
                RegisterOrganizationCommandValidator.PasswordEqualsEmailMessage);
        }

        // Session claims carry the sign-in instant in milliseconds, so the
        // change instant is truncated the same way: a sign-in after this
        // commit always compares at or above it (BR-15).
        var changedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));

        token.MarkUsed(now);
        user.ChangePassword(request.PasswordHash, changedAt);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return PasswordResetResult<NoValue>.Ok(default);
    }
}
