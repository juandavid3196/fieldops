namespace FieldOps.Api.Middleware;

/// <summary>
/// The request path as written to logs: the public organization slug of the
/// anonymous request form routes and the photo id of the public quote link are
/// replaced so logs never identify the tenant or its data.
/// </summary>
internal static class LoggedRequestPath
{
    private const string PublicPrefix = "/public/organizations/";

    private const string QuoteLinkPhotoPrefix = "/public/quote-links/photos/";

    public static string? Of(PathString path)
    {
        var value = path.Value;

        if (value is not null && value.StartsWith(QuoteLinkPhotoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return QuoteLinkPhotoPrefix + "{photoId}";
        }

        if (value is null || !value.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var next = value.IndexOf('/', PublicPrefix.Length);

        return next < 0
            ? PublicPrefix + "{slug}"
            : string.Concat(PublicPrefix, "{slug}", value.AsSpan(next));
    }
}
