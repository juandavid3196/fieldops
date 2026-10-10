namespace FieldOps.Application.Features.PortalAccess;

/// <summary>Shared list paging of the portal (customer portal BR-34).</summary>
public sealed record PortalPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class PortalPaging
{
    public const int PageSize = 20;

    /// <summary>The page number of a query value: absent is 1; below 1 or not a number is invalid.</summary>
    public static bool TryParse(string? value, out int page)
    {
        page = 1;

        return string.IsNullOrWhiteSpace(value)
            || (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page) && page >= 1);
    }
}
