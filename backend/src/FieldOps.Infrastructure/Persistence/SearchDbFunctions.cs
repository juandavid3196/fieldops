namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// CLR stubs of the database functions the global search calls (global-search SA-23). Only EF query translation uses
/// them; calling one outside a query is a programming error.
/// </summary>
internal static class SearchDbFunctions
{
    /// <summary>The IMMUTABLE wrapper over <c>unaccent</c> with an explicit dictionary (<c>public.fieldops_unaccent</c>).</summary>
    public const string UnaccentName = "fieldops_unaccent";

    public static string Unaccent(string value) =>
        throw new NotSupportedException("Unaccent is translated to the SA-23 database function and cannot run in memory.");
}
