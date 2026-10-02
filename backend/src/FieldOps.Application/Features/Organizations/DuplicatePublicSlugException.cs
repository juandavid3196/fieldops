namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Raised when saving a registration loses a concurrent unique-constraint
/// race on <c>organizations (public_slug)</c> (public service request BR-21).
/// The registration handler retries with the next free suffix.
/// </summary>
public sealed class DuplicatePublicSlugException(string publicSlug, Exception innerException)
    : Exception("The public slug is already in use.", innerException)
{
    public string PublicSlug { get; } = publicSlug;
}
