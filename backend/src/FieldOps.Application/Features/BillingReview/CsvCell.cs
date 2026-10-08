using System.Text;

namespace FieldOps.Application.Features.BillingReview;

/// <summary>CSV cell escaping: formula neutralization first, then RFC 4180 quoting (BR-21).</summary>
public static class CsvCell
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && FormulaStarts.Contains(text[0]))
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    public static string Row(IEnumerable<string?> cells) => string.Join(',', cells.Select(Escape));

    public static byte[] Document(IEnumerable<string> rows)
    {
        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            builder.Append(row).Append("\r\n");
        }

        return new UTF8Encoding(false).GetBytes(builder.ToString());
    }
}
