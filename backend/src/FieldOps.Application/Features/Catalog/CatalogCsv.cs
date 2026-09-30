using System.Globalization;
using System.Text;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Features.Catalog;

/// <summary>A parsed CSV record and the file line on which it starts.</summary>
public sealed record CsvRecord(int Line, string[] Cells);

/// <summary>Outcome of <see cref="CatalogCsv.ParseImport"/>.</summary>
public sealed record CatalogImportParse(
    IReadOnlyList<CatalogItemValues> Items,
    IReadOnlyList<CatalogRowError> RowErrors,
    IReadOnlyList<int> ItemRows,
    string? FileError);

/// <summary>CSV writing (BR-13 export/template) and strict parsing (BR-13 import) for the catalog.</summary>
public static class CatalogCsv
{
    public const int MaxImportFileBytes = 1_048_576;

    public const int MaxImportRows = 500;

    public const int MaxRowErrors = 100;

    public const string FileSizeMessage = "Choose a CSV file of 1 MB or smaller.";

    public const string NotCsvMessage = "Choose a CSV file.";

    public const string HeaderMessage = "The file doesn't match the template.";

    public const string NoItemsMessage = "The file has no items.";

    public const string TooManyItemsMessage = "Import up to 500 items at a time.";

    public const string InvalidBooleanMessage = "Use true, false, yes or no.";

    public const string RepeatedInFileMessage = "This name is repeated in the file for this type.";

    public const string RowShapeMessage = "This row doesn't match the template.";

    public const string TemplateFileName = "products-services-template.csv";

    public static readonly string[] Columns =
        ["type", "name", "description", "unit_cost", "unit_price", "taxable", "active"];

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly UTF8Encoding OutputUtf8 = new(false);

    private static readonly IReadOnlyDictionary<string, string> ColumnByKey =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CatalogItemRules.TypeKey] = "type",
            [CatalogItemRules.NameKey] = "name",
            [CatalogItemRules.DescriptionKey] = "description",
            [CatalogItemRules.UnitCostKey] = "unit_cost",
            [CatalogItemRules.UnitPriceKey] = "unit_price",
        };

    public static string ExportFileName(DateOnly date) =>
        $"products-services-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";

    public static byte[] Template() => OutputUtf8.GetBytes(string.Join(',', Columns) + "\r\n");

    public static byte[] Export(IEnumerable<CatalogItemRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Columns)).Append("\r\n");

        foreach (var row in rows)
        {
            builder
                .AppendJoin(
                    ',',
                    Cell(row.Type),
                    Cell(row.Name),
                    Cell(row.Description ?? string.Empty),
                    Cell(row.UnitCost.ToString("F2", CultureInfo.InvariantCulture)),
                    Cell(row.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)),
                    Cell(row.IsTaxable ? "true" : "false"),
                    Cell(row.IsActive ? "true" : "false"))
                .Append("\r\n");
        }

        return OutputUtf8.GetBytes(builder.ToString());
    }

    /// <summary>Formula-injection neutralization, then RFC 4180 quoting.</summary>
    public static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    /// <summary>Decodes strict UTF-8 (BOM skipped); null when the bytes are not valid text.</summary>
    public static string? Decode(byte[] bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);

            return text.Contains('\0', StringComparison.Ordinal) ? null : text.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>RFC 4180 records with the start line of each; null when a quoted field never closes.</summary>
    public static List<CsvRecord>? Read(string text)
    {
        var records = new List<CsvRecord>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        var inQuotes = false;
        var cellWasQuoted = false;

        void EndRecord()
        {
            cells.Add(cell.ToString());
            cell.Clear();

            // A blank line is not a record.
            if (!(cells.Count == 1 && cells[0].Length == 0 && !cellWasQuoted))
            {
                records.Add(new CsvRecord(recordLine, [.. cells]));
            }

            cells.Clear();
            cellWasQuoted = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    cell.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when cell.Length == 0 && !cellWasQuoted:
                    inQuotes = true;
                    cellWasQuoted = true;
                    break;
                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    cellWasQuoted = false;
                    break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    EndRecord();
                    line++;
                    recordLine = line;
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        if (inQuotes)
        {
            return null;
        }

        if (cell.Length > 0 || cells.Count > 0 || cellWasQuoted)
        {
            EndRecord();
        }

        return records;
    }

    /// <summary>
    /// File-level and row-level parsing of an import (BR-13): header, row count, field rules and
    /// in-file duplicates. Existing-name duplicates are checked by the caller.
    /// </summary>
    public static CatalogImportParse ParseImport(byte[] bytes)
    {
        if (bytes.Length > MaxImportFileBytes)
        {
            return FileFailure(FileSizeMessage);
        }

        var text = Decode(bytes);
        var records = text is null ? null : Read(text);

        if (records is null || records.Count == 0)
        {
            return FileFailure(NotCsvMessage);
        }

        var header = records[0].Cells.Select(cell => cell.Trim().ToLowerInvariant()).ToArray();

        if (header.Length != Columns.Length
            || header.Distinct(StringComparer.Ordinal).Count() != Columns.Length
            || !Columns.All(column => header.Contains(column, StringComparer.Ordinal)))
        {
            return FileFailure(HeaderMessage);
        }

        var dataRows = records.Skip(1).ToList();

        if (dataRows.Count == 0)
        {
            return FileFailure(NoItemsMessage);
        }

        if (dataRows.Count > MaxImportRows)
        {
            return FileFailure(TooManyItemsMessage);
        }

        var index = Columns.ToDictionary(column => column, column => Array.IndexOf(header, column), StringComparer.Ordinal);
        var items = new List<CatalogItemValues>();
        var itemRows = new List<int>();
        var rowErrors = new List<CatalogRowError>();
        var seen = new HashSet<(CatalogItemType, string)>();

        foreach (var record in dataRows)
        {
            if (record.Cells.Length != Columns.Length)
            {
                rowErrors.Add(new CatalogRowError(record.Line, "row", RowShapeMessage));
                continue;
            }

            string Value(string column) => record.Cells[index[column]].Trim();

            var errors = new List<CatalogRowError>();

            var taxable = ParseBoolean(Value("taxable"), "taxable", record.Line, errors);
            var active = ParseBoolean(Value("active"), "active", record.Line, errors);

            var values = CatalogItemRules.Validate(
                new CatalogItemInput(
                    Value("type"),
                    record.Cells[index["name"]],
                    record.Cells[index["description"]],
                    Value("unit_cost"),
                    Value("unit_price"),
                    taxable,
                    active),
                out var fieldErrors);

            foreach (var column in Columns)
            {
                var key = ColumnByKey.FirstOrDefault(pair => pair.Value == column).Key;

                if (key is not null && fieldErrors.TryGetValue(key, out var message))
                {
                    errors.Add(new CatalogRowError(record.Line, column, message));
                }
            }

            if (values is not null && errors.Count == 0)
            {
                if (seen.Add((values.Type, CatalogItem.NormalizeName(values.Name))))
                {
                    items.Add(values);
                    itemRows.Add(record.Line);
                }
                else
                {
                    errors.Add(new CatalogRowError(record.Line, "name", RepeatedInFileMessage));
                }
            }

            rowErrors.AddRange(errors);
        }

        return new CatalogImportParse(items, rowErrors, itemRows, null);
    }

    /// <summary>Orders (row, column order) and truncates to the 100-entry cap.</summary>
    public static IReadOnlyList<CatalogRowError> Finalize(IEnumerable<CatalogRowError> errors) =>
        [.. errors
            .OrderBy(error => error.Row)
            .ThenBy(error => Array.IndexOf(Columns, error.Column))
            .Take(MaxRowErrors)];

    private static bool? ParseBoolean(string text, string column, int line, List<CatalogRowError> errors)
    {
        switch (text.ToLowerInvariant())
        {
            case "":
                return true;
            case "true" or "yes":
                return true;
            case "false" or "no":
                return false;
            default:
                errors.Add(new CatalogRowError(line, column, InvalidBooleanMessage));
                return null;
        }
    }

    private static CatalogImportParse FileFailure(string message) => new([], [], [], message);
}
