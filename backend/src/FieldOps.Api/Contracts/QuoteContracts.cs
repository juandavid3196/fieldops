using FieldOps.Application.Features.Quotes;

namespace FieldOps.Api.Contracts;

/// <summary>Body of POST /quotes (BR-05); the organization always comes from the session.</summary>
public sealed record CreateQuoteBody(Guid? RequestId);

public sealed record QuoteLineBody(
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

public sealed record QuoteTermsBody(string? Preset, string? CustomText);

/// <summary>
/// Body of POST /quotes/{id}/calculate (API contracts DraftBody). Unknown keys, such as client-sent amounts, are
/// not part of the contract and are ignored by deserialization.
/// </summary>
public sealed record DraftBody(
    List<QuoteLineBody>? Lines,
    decimal? DiscountTotal,
    string? CustomerMessage,
    string? InternalNote,
    QuoteTermsBody? Terms,
    string? ValidUntil)
{
    public QuoteDraftText ToText() => QuoteBodies.ToText(Lines, DiscountTotal, CustomerMessage, InternalNote, Terms, ValidUntil);
}

/// <summary>Body of PUT /quotes/{id}/draft (BR-22): the draft plus the concurrency token.</summary>
public sealed record SaveDraftBody(
    List<QuoteLineBody>? Lines,
    decimal? DiscountTotal,
    string? CustomerMessage,
    string? InternalNote,
    QuoteTermsBody? Terms,
    string? ValidUntil,
    string? UpdatedAt)
{
    public QuoteDraftText ToText() => QuoteBodies.ToText(Lines, DiscountTotal, CustomerMessage, InternalNote, Terms, ValidUntil);
}

/// <summary>Body of POST /quotes/{id}/send (BR-24): the draft, the concurrency token and the email message.</summary>
public sealed record SendQuoteBody(
    List<QuoteLineBody>? Lines,
    decimal? DiscountTotal,
    string? CustomerMessage,
    string? InternalNote,
    QuoteTermsBody? Terms,
    string? ValidUntil,
    string? UpdatedAt,
    string? EmailMessage)
{
    public QuoteDraftText ToText() => QuoteBodies.ToText(Lines, DiscountTotal, CustomerMessage, InternalNote, Terms, ValidUntil);
}

/// <summary>Body of revise and discard-draft (BR-28, BR-29).</summary>
public sealed record QuoteVersionActionBody(string? UpdatedAt);

/// <summary>Body of POST /quotes/{id}/resend-email (BR-26).</summary>
public sealed record ResendQuoteEmailBody(string? UpdatedAt, string? EmailMessage);

internal static class QuoteBodies
{
    public static QuoteDraftText ToText(
        List<QuoteLineBody>? lines,
        decimal? discountTotal,
        string? customerMessage,
        string? internalNote,
        QuoteTermsBody? terms,
        string? validUntil) =>
        new(
            lines?.Select(line => line is null
                    ? new QuoteLineText(null, null, null, null, null, null, null, null, null, null)
                    : new QuoteLineText(
                        line.CatalogItemId,
                        line.Type,
                        line.Name,
                        line.Description,
                        line.Quantity,
                        line.Unit,
                        line.UnitPrice,
                        line.UnitCost,
                        line.Taxable,
                        line.IsOptional))
                .ToList(),
            discountTotal,
            customerMessage,
            internalNote,
            terms is null ? null : new QuoteTermsText(terms.Preset, terms.CustomText),
            validUntil);
}
