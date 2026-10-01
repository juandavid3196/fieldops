using System.Text;
using System.Text.RegularExpressions;

namespace FieldOps.Application.Features.Customers;

/// <summary>Pure email and phone normalization shared by the form, the duplicate check and the CSV import (BR-10, BR-14).</summary>
public static partial class CustomerNormalizer
{
    public const int EmailMaxLength = 254;

    /// <summary>Trimmed and lower-cased; null when blank.</summary>
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>Syntactic validity of an already normalized email.</summary>
    public static bool IsValidEmail(string? normalizedEmail) =>
        normalizedEmail is { Length: > 0 and <= EmailMaxLength } && EmailPattern().IsMatch(normalizedEmail);

    /// <summary>
    /// Accepts digits, spaces, <c>()</c>, <c>-</c>, <c>.</c> and a leading <c>+</c>; after stripping, 10 to 15 digits;
    /// an 11-digit number starting with 1 keeps its last 10. Blank is valid and yields null.
    /// </summary>
    public static bool TryNormalizePhone(string? raw, out string? digits)
    {
        digits = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim();
        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c is >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (c == '+' && i == 0)
            {
                continue;
            }
            else if (c is not (' ' or '(' or ')' or '-' or '.'))
            {
                return false;
            }
        }

        var stripped = builder.ToString();

        if (stripped.Length == 11 && stripped[0] == '1')
        {
            stripped = stripped[1..];
        }

        if (stripped.Length is < 10 or > 15)
        {
            return false;
        }

        digits = stripped;

        return true;
    }

    /// <summary>"(512) 555-7832" for ten digits, the digits as stored otherwise.</summary>
    public static string? FormatPhone(string? digits) =>
        digits is { Length: 10 } && digits.All(char.IsAsciiDigit)
            ? $"({digits[..3]}) {digits.Substring(3, 3)}-{digits[6..]}"
            : digits;

    /// <summary>The digits of a search term.</summary>
    public static string DigitsOnly(string? text) =>
        new((text ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
