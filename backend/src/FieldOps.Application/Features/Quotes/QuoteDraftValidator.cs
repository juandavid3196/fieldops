using System.Globalization;
using System.Text.RegularExpressions;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Features.Quotes;

/// <summary>Messages, limits and machine codes of the quote builder (BR-10 to BR-22, BR-28).</summary>
public static class QuoteMessages
{
    public const int MaxLines = 100;

    public const int NoteMaxLength = 500;

    public const int TermsMaxLength = 2000;

    public const int EmailMessageMaxLength = 320;

    public const int MaxValidDays = 365;

    public const int DefaultValidDays = 30;

    public const string QuoteChangedCode = "quote_changed";

    public const string NoDraftCode = "no_draft";

    public const string QuoteChangedTitle = "This quote changed. Refresh to see the latest.";

    public const string NoDraftTitle = "This quote has no draft. Revise it to make changes.";

    public const string TypeMessage = "Choose a type.";

    public const string CatalogItemMessage = "This catalog item isn't available.";

    public const string NameRequiredMessage = "Enter an item name.";

    public const string NameTooLongMessage = "Item name must be 160 characters or fewer.";

    public const string DescriptionTooLongMessage = "Description must be 1,000 characters or fewer.";

    public const string QuantityMessage = "Enter a quantity greater than 0.";

    public const string UnitMessage = "Enter a unit.";

    public const string UnitPriceMessage = "Enter a price of 0 or more.";

    public const string UnitCostMessage = "Enter a cost of 0 or more.";

    public const string TaxableMessage = "Choose a tax option.";

    public const string OptionalMessage = "Choose whether this item is optional.";

    public const string TooManyLinesMessage = "A quote can have up to 100 lines.";

    public const string CustomerMessageTooLong = "Customer message must be 500 characters or fewer.";

    public const string InternalNoteTooLong = "Internal note must be 500 characters or fewer.";

    public const string TermsRequiredMessage = "Choose the payment terms.";

    public const string TermsCustomRequiredMessage = "Enter the terms.";

    public const string TermsCustomTooLongMessage = "Terms must be 2000 characters or fewer.";

    public const string ValidUntilMessage = "Choose a date between tomorrow and one year from today.";

    public const string EmailMessageRequired = "Enter a message.";

    public const string EmailMessageTooLong = "Message must be 320 characters or fewer.";

    public const string NoNonOptionalLineMessage = "Add at least one line that isn't optional.";

    public const string RequestIdMessage = "Choose a request.";
}

public sealed record QuoteLineText(
    Guid? CatalogItemId,
    string? Type,
    string? Name,
    string? Description,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? UnitCost,
    bool? Taxable,
    bool? IsOptional);

public sealed record QuoteTermsText(string? Preset, string? CustomText);

/// <summary>The raw draft body (API contracts DraftBody) before validation; client-sent amounts are not part of it.</summary>
public sealed record QuoteDraftText(
    IReadOnlyList<QuoteLineText>? Lines,
    decimal? DiscountTotal,
    string? CustomerMessage,
    string? InternalNote,
    QuoteTermsText? Terms,
    string? ValidUntil);

public sealed record QuoteLineDraft(
    Guid? CatalogItemId,
    CatalogItemType Type,
    string Name,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal UnitCost,
    bool Taxable,
    bool IsOptional);

/// <summary>A validated draft: normalized texts and typed values, in the submitted line order.</summary>
public sealed record QuoteDraft(
    IReadOnlyList<QuoteLineDraft> Lines,
    decimal DiscountTotal,
    string? CustomerMessage,
    string? InternalNote,
    string TermsText,
    DateOnly ValidUntil);

/// <summary>The payment term presets and their frozen texts (BR-18).</summary>
public static class QuoteTerms
{
    public const string DueOnCompletion = "due_on_completion";

    public const string Net15 = "net_15";

    public const string Net30 = "net_30";

    public const string Custom = "custom";

    public static string? PresetText(string? preset) => preset switch
    {
        DueOnCompletion => "Payment due upon completion.",
        Net15 => "Payment due within 15 days of the invoice date.",
        Net30 => "Payment due within 30 days of the invoice date.",
        _ => null,
    };

    /// <summary>A stored text equal to a preset text selects that preset; anything else is custom (BR-18).</summary>
    public static (string Preset, string? CustomText) Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (DueOnCompletion, null);
        }

        foreach (var preset in new[] { DueOnCompletion, Net15, Net30 })
        {
            if (string.Equals(PresetText(preset), text, StringComparison.Ordinal))
            {
                return (preset, null);
            }
        }

        return (Custom, text);
    }
}

/// <summary>Shape validation of a quote draft (BR-10, BR-14 format, BR-17 to BR-19) and of the email message (BR-21); all errors at once.</summary>
public static partial class QuoteDraftValidator
{
    private const decimal MaxQuantity = 999_999.999m;

    private const decimal MaxMoney = 999_999_999.99m;

    private static readonly Regex Whitespace = CollapseWhitespace();

    /// <summary>Returns the validated draft, or null with <paramref name="errors"/> filled (keys <c>lines[i].key</c>).</summary>
    public static QuoteDraft? Validate(QuoteDraftText input, DateOnly today, Dictionary<string, string[]> errors)
    {
        var lines = new List<QuoteLineDraft>();
        var texts = input.Lines ?? [];

        if (texts.Count > QuoteMessages.MaxLines)
        {
            errors["lines"] = [QuoteMessages.TooManyLinesMessage];
        }
        else
        {
            for (var index = 0; index < texts.Count; index++)
            {
                var line = ValidateLine(texts[index], index, errors);

                if (line is not null)
                {
                    lines.Add(line);
                }
            }
        }

        var discount = input.DiscountTotal;

        if (discount is null or < 0 || Math.Round(discount.Value, 2) != discount)
        {
            errors["discountTotal"] = [QuoteCalculator.DiscountInvalidMessage];
        }

        var customerMessage = Optional(input.CustomerMessage, QuoteMessages.NoteMaxLength, "customerMessage", QuoteMessages.CustomerMessageTooLong, errors);
        var internalNote = Optional(input.InternalNote, QuoteMessages.NoteMaxLength, "internalNote", QuoteMessages.InternalNoteTooLong, errors);
        var terms = ValidateTerms(input.Terms, errors);
        var validUntil = ValidateValidUntil(input.ValidUntil, today, errors);

        return errors.Count > 0
            ? null
            : new QuoteDraft(lines, discount!.Value, customerMessage, internalNote, terms!, validUntil!.Value);
    }

    /// <summary>1–320 characters after trim (BR-21); null with an error otherwise.</summary>
    public static string? ValidateEmailMessage(string? message, Dictionary<string, string[]> errors)
    {
        var text = message?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            errors["emailMessage"] = [QuoteMessages.EmailMessageRequired];

            return null;
        }

        if (text.Length > QuoteMessages.EmailMessageMaxLength)
        {
            errors["emailMessage"] = [QuoteMessages.EmailMessageTooLong];

            return null;
        }

        return text;
    }

    private static QuoteLineDraft? ValidateLine(QuoteLineText line, int index, Dictionary<string, string[]> errors)
    {
        var before = errors.Count;

        void Fail(string key, string message) =>
            errors[string.Create(CultureInfo.InvariantCulture, $"lines[{index}].{key}")] = [message];

        CatalogItemType type = default;

        switch (line.Type?.Trim())
        {
            case "service":
                type = CatalogItemType.Service;
                break;
            case "product":
                type = CatalogItemType.Product;
                break;
            default:
                Fail("type", QuoteMessages.TypeMessage);
                break;
        }

        var name = Whitespace.Replace(line.Name?.Trim() ?? string.Empty, " ");

        if (name.Length == 0)
        {
            Fail("name", QuoteMessages.NameRequiredMessage);
        }
        else if (name.Length > 160)
        {
            Fail("name", QuoteMessages.NameTooLongMessage);
        }

        var description = line.Description?.Trim() ?? string.Empty;

        if (description.Length > 1000)
        {
            Fail("description", QuoteMessages.DescriptionTooLongMessage);
        }

        if (line.Quantity is not { } quantity
            || quantity <= 0
            || quantity > MaxQuantity
            || Math.Round(quantity, 3) != quantity)
        {
            Fail("quantity", QuoteMessages.QuantityMessage);
        }

        var unit = line.Unit is null ? "unit" : line.Unit.Trim();

        if (unit.Length is 0 or > 40)
        {
            Fail("unit", QuoteMessages.UnitMessage);
        }

        if (!IsMoney(line.UnitPrice))
        {
            Fail("unitPrice", QuoteMessages.UnitPriceMessage);
        }

        if (!IsMoney(line.UnitCost))
        {
            Fail("unitCost", QuoteMessages.UnitCostMessage);
        }

        if (line.Taxable is null)
        {
            Fail("taxable", QuoteMessages.TaxableMessage);
        }

        if (line.IsOptional is null)
        {
            Fail("isOptional", QuoteMessages.OptionalMessage);
        }

        return errors.Count > before
            ? null
            : new QuoteLineDraft(
                line.CatalogItemId,
                type,
                name,
                description,
                line.Quantity!.Value,
                unit,
                line.UnitPrice!.Value,
                line.UnitCost!.Value,
                line.Taxable!.Value,
                line.IsOptional!.Value);
    }

    private static bool IsMoney(decimal? value) =>
        value is { } amount && amount >= 0 && amount <= MaxMoney && Math.Round(amount, 2) == amount;

    private static string? Optional(string? value, int maxLength, string key, string message, Dictionary<string, string[]> errors)
    {
        var text = value?.Trim() ?? string.Empty;

        if (text.Length > maxLength)
        {
            errors[key] = [message];

            return null;
        }

        return text.Length == 0 ? null : text;
    }

    private static string? ValidateTerms(QuoteTermsText? terms, Dictionary<string, string[]> errors)
    {
        var preset = terms?.Preset?.Trim();

        if (preset == QuoteTerms.Custom)
        {
            var text = terms!.CustomText?.Trim() ?? string.Empty;

            if (text.Length == 0)
            {
                errors["terms.customText"] = [QuoteMessages.TermsCustomRequiredMessage];

                return null;
            }

            if (text.Length > QuoteMessages.TermsMaxLength)
            {
                errors["terms.customText"] = [QuoteMessages.TermsCustomTooLongMessage];

                return null;
            }

            return text;
        }

        var presetText = QuoteTerms.PresetText(preset);

        if (presetText is null)
        {
            errors["terms.preset"] = [QuoteMessages.TermsRequiredMessage];
        }

        return presetText;
    }

    private static DateOnly? ValidateValidUntil(string? text, DateOnly today, Dictionary<string, string[]> errors)
    {
        if (text is not null
            && DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date >= today.AddDays(1)
            && date <= today.AddDays(QuoteMessages.MaxValidDays))
        {
            return date;
        }

        errors["validUntil"] = [QuoteMessages.ValidUntilMessage];

        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();
}
