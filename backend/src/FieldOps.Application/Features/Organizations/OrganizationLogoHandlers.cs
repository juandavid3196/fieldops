using System.Net;

namespace FieldOps.Application.Features.Organizations;

/// <summary>Reads the session organization's logo (FR-09).</summary>
public sealed class GetOrganizationLogoHandler(IOrganizationLogoStore store)
{
    public Task<OrganizationLogoContent?> HandleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        store.GetAsync(organizationId, cancellationToken);
}

/// <summary>Result of <see cref="UploadOrganizationLogoHandler"/>.</summary>
public abstract record UploadOrganizationLogoResult
{
    private UploadOrganizationLogoResult()
    {
    }

    public sealed record Succeeded(OrganizationLogoMetadata Logo) : UploadOrganizationLogoResult;

    /// <summary>The <c>errors.file</c> message (BR-07).</summary>
    public sealed record Invalid(string Message) : UploadOrganizationLogoResult;
}

/// <summary>Validates content per BR-07/BR-08, then stores it and its audit row (FR-09).</summary>
public sealed class UploadOrganizationLogoHandler(IOrganizationLogoStore store, TimeProvider timeProvider)
{
    public const string LogoUpdatedAuditAction = "organization.logo_updated";

    public async Task<UploadOrganizationLogoResult> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        byte[] content,
        string? declaredContentType,
        CancellationToken cancellationToken)
    {
        var check = OrganizationLogoContentValidator.Check(content, declaredContentType);

        if (!check.IsValid)
        {
            return new UploadOrganizationLogoResult.Invalid(check.Error!);
        }

        var logo = await store.UpsertAsync(
            organizationId,
            check.ContentType!,
            content,
            actorUserId,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return new UploadOrganizationLogoResult.Succeeded(logo);
    }
}

/// <summary>Removes the logo; a no-op without audit when none exists (FR-09).</summary>
public sealed class RemoveOrganizationLogoHandler(IOrganizationLogoStore store, TimeProvider timeProvider)
{
    public const string LogoRemovedAuditAction = "organization.logo_removed";

    public Task<bool> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken) =>
        store.DeleteAsync(organizationId, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);
}
