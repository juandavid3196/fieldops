using System.Text;
using FieldOps.Application.Features.Catalog;
using FieldOps.Domain.Customers;

namespace FieldOps.Application.Features.Customers;

/// <summary>A CSV row that passed field validation; branch code and tag names are resolved by the handler.</summary>
public sealed record CustomerCsvRow(int Line, CustomerValues Values, string BranchCode, IReadOnlyList<string> TagNames);

public sealed record CustomerCsvParse(
    IReadOnlyList<CustomerCsvRow> Rows,
    IReadOnlyList<CustomerRowError> RowErrors,
    int DataRowCount,
    string? FileError);

/// <summary>CSV template and strict parsing for the customer import (BR-17). Reuses the catalog RFC 4180 reader.</summary>
public static class CustomerCsv
{
    public const int MaxImportFileBytes = 1_048_576;

    public const int MaxImportRows = 500;

    public const int MaxRowErrors = 100;

    public const string FileSizeMessage = "Choose a CSV file of 1 MB or smaller.";

    public const string NotCsvMessage = "Choose a CSV file.";

    public const string HeaderMessage = "The file doesn't match the template.";

    public const string NoCustomersMessage = "The file has no customers.";

    public const string TooManyCustomersMessage = "Import up to 500 customers at a time.";

    public const string InvalidBooleanMessage = "Use true, false, yes or no.";

    public const string RowShapeMessage = "This row doesn't match the template.";

    public const string BranchCodeMessage = "Use the code of an active branch.";

    public const string TagsTooManyMessage = "Use up to 10 tags.";

    public const string TemplateFileName = "customers-template.csv";

    public static readonly string[] Columns =
    [
        "type", "company_name", "first_name", "last_name", "title", "email", "mobile_phone", "prefers_email",
        "prefers_sms", "address_line1", "city", "state", "zip_code", "branch_code", "tags", "service_instructions",
        "internal_note",
    ];

    private static readonly UTF8Encoding OutputUtf8 = new(false);

    private static readonly IReadOnlyDictionary<string, string> ColumnByKey =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CustomerRules.TypeKey] = "type",
            [CustomerRules.CompanyNameKey] = "company_name",
            [CustomerRules.FirstNameKey] = "first_name",
            [CustomerRules.LastNameKey] = "last_name",
            [CustomerRules.TitleKey] = "title",
            [CustomerRules.EmailKey] = "email",
            [CustomerRules.PhoneKey] = "mobile_phone",
            [CustomerRules.PreferredCommunicationKey] = "prefers_email",
            [CustomerRules.AddressLine1Key] = "address_line1",
            [CustomerRules.CityKey] = "city",
            [CustomerRules.StateRegionKey] = "state",
            [CustomerRules.PostalCodeKey] = "zip_code",
            [CustomerRules.ServiceInstructionsKey] = "service_instructions",
            [CustomerRules.InternalNoteKey] = "internal_note",
        };

    public static byte[] Template() => OutputUtf8.GetBytes(string.Join(',', Columns) + "\r\n");

    /// <summary>Header, row count, field rules and tag shape. Branch codes are checked by the caller.</summary>
    public static CustomerCsvParse Parse(byte[] bytes, bool unitedStates)
    {
        if (bytes.Length > MaxImportFileBytes)
        {
            return FileFailure(FileSizeMessage);
        }

        var text = CatalogCsv.Decode(bytes);
        var records = text is null ? null : CatalogCsv.Read(text);

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
            return FileFailure(NoCustomersMessage);
        }

        if (dataRows.Count > MaxImportRows)
        {
            return FileFailure(TooManyCustomersMessage);
        }

        var index = Columns.ToDictionary(column => column, column => Array.IndexOf(header, column), StringComparer.Ordinal);
        var rows = new List<CustomerCsvRow>();
        var rowErrors = new List<CustomerRowError>();

        foreach (var record in dataRows)
        {
            if (record.Cells.Length != Columns.Length)
            {
                rowErrors.Add(new CustomerRowError(record.Line, "row", RowShapeMessage));
                continue;
            }

            string Value(string column) => record.Cells[index[column]].Trim();

            var errors = new List<CustomerRowError>();

            var prefersEmail = ParseBoolean(Value("prefers_email"), true, "prefers_email", record.Line, errors);
            var prefersSms = ParseBoolean(Value("prefers_sms"), false, "prefers_sms", record.Line, errors);

            var values = CustomerRules.Validate(
                new CustomerInput(
                    Value("type"),
                    Value("company_name"),
                    Value("first_name"),
                    Value("last_name"),
                    Value("title"),
                    Value("email"),
                    Value("mobile_phone"),
                    prefersEmail ?? true,
                    prefersSms ?? false,
                    Value("address_line1"),
                    Value("city"),
                    Value("state"),
                    Value("zip_code"),
                    Value("service_instructions"),
                    Value("internal_note")),
                null,
                unitedStates,
                out var fieldErrors);

            // A boolean error already reported its own column; skip the derived channel message then.
            var booleanFailed = errors.Count > 0;

            foreach (var column in Columns)
            {
                var key = ColumnByKey.FirstOrDefault(pair => pair.Value == column).Key;

                if (key is null || !fieldErrors.TryGetValue(key, out var message))
                {
                    continue;
                }

                if (key == CustomerRules.PreferredCommunicationKey && booleanFailed)
                {
                    continue;
                }

                errors.Add(new CustomerRowError(record.Line, column, message));
            }

            var tagNames = ParseTags(Value("tags"), record.Line, errors);

            if (values is not null && errors.Count == 0)
            {
                rows.Add(new CustomerCsvRow(record.Line, values, Value("branch_code"), tagNames));
            }

            rowErrors.AddRange(errors);
        }

        return new CustomerCsvParse(rows, rowErrors, dataRows.Count, null);
    }

    /// <summary>Orders (row, column order) and truncates to the 100-entry cap.</summary>
    public static IReadOnlyList<CustomerRowError> Finalize(IEnumerable<CustomerRowError> errors) =>
        [.. errors
            .OrderBy(error => error.Row)
            .ThenBy(error => Array.IndexOf(Columns, error.Column))
            .Take(MaxRowErrors)];

    private static List<string> ParseTags(string text, int line, List<CustomerRowError> errors)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length > CustomerTag.NameMaxLength)
            {
                errors.Add(new CustomerRowError(line, "tags", CustomerMessages.TagNameTooLong));
            }
            else if (seen.Add(CustomerTag.NormalizeName(part)))
            {
                names.Add(part);
            }
        }

        if (names.Count > CustomerRules.MaxTags)
        {
            errors.Add(new CustomerRowError(line, "tags", TagsTooManyMessage));
        }

        return names;
    }

    private static bool? ParseBoolean(string text, bool whenEmpty, string column, int line, List<CustomerRowError> errors)
    {
        switch (text.ToLowerInvariant())
        {
            case "":
                return whenEmpty;
            case "true" or "yes":
                return true;
            case "false" or "no":
                return false;
            default:
                errors.Add(new CustomerRowError(line, column, InvalidBooleanMessage));
                return null;
        }
    }

    private static CustomerCsvParse FileFailure(string message) => new([], [], 0, message);
}
