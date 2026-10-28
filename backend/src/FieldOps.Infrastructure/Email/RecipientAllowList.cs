using System.Net.Mail;

namespace FieldOps.Infrastructure.Email;

/// <summary>Matching rules of <c>Email:AllowedRecipients</c>: exact address or <c>@domain</c>, case-insensitive.</summary>
internal static class RecipientAllowList
{
    public static bool IsValidEntry(string? entry)
    {
        var value = entry?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.StartsWith('@')
            ? value.Length > 3 && value.IndexOf('@', 1) < 0 && value.Contains('.') && !value.Contains(' ')
            : MailAddress.TryCreate(value, out var address) && string.Equals(address.Address, value, StringComparison.Ordinal);
    }

    public static bool IsAllowed(string recipient, IEnumerable<string> entries)
    {
        var address = recipient.Trim();

        return entries.Select(entry => entry.Trim()).Any(entry =>
            entry.StartsWith('@')
                ? address.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
                : string.Equals(address, entry, StringComparison.OrdinalIgnoreCase));
    }
}
