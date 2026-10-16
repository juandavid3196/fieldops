using System.Net;

namespace FieldOps.Application.Features.Account;

/// <summary>Reads the session user's photo (BR-04).</summary>
public sealed class GetUserPhotoHandler(IUserPhotoStore store)
{
    public Task<UserPhotoContent?> HandleAsync(Guid userId, CancellationToken cancellationToken) =>
        store.GetAsync(userId, cancellationToken);
}

/// <summary>Result of <see cref="UploadUserPhotoHandler"/>.</summary>
public abstract record UploadUserPhotoResult
{
    private UploadUserPhotoResult()
    {
    }

    public sealed record Succeeded(UserPhotoMetadata Photo) : UploadUserPhotoResult;

    /// <summary>The <c>errors.file</c> message (BR-03).</summary>
    public sealed record Invalid(string Message) : UploadUserPhotoResult;
}

/// <summary>Validates content per BR-03, then stores it and its audit row (BR-07).</summary>
public sealed class UploadUserPhotoHandler(IUserPhotoStore store, TimeProvider timeProvider)
{
    public const string PhotoUpdatedAuditAction = "user.photo_updated";

    public async Task<UploadUserPhotoResult> HandleAsync(
        Guid userId,
        Guid organizationId,
        IPAddress? clientIp,
        byte[] content,
        string? declaredContentType,
        CancellationToken cancellationToken)
    {
        var check = UserPhotoContentValidator.Check(content, declaredContentType);

        if (!check.IsValid)
        {
            return new UploadUserPhotoResult.Invalid(check.Error!);
        }

        var photo = await store.UpsertAsync(
            userId, organizationId, check.ContentType!, content, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return new UploadUserPhotoResult.Succeeded(photo);
    }
}

/// <summary>Removes the photo; a no-op without audit when none exists.</summary>
public sealed class RemoveUserPhotoHandler(IUserPhotoStore store)
{
    public const string PhotoRemovedAuditAction = "user.photo_removed";

    public Task<bool> HandleAsync(
        Guid userId, Guid organizationId, IPAddress? clientIp, CancellationToken cancellationToken) =>
        store.DeleteAsync(userId, organizationId, clientIp, cancellationToken);
}
