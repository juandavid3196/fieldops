using System.Net;

namespace FieldOps.Domain.Quotes;

public sealed class QuoteResponse
{
    private static readonly QuoteStatus[] AllowedResponses =
    [
        QuoteStatus.Approved,
        QuoteStatus.Rejected,
        QuoteStatus.ClarificationRequested,
    ];

    private QuoteResponse()
    {
    }

    private QuoteResponse(
        Guid id,
        Guid quoteVersionId,
        QuoteStatus response,
        string responderName)
    {
        Id = id;
        QuoteVersionId = quoteVersionId;
        Response = response;
        ResponderName = responderName;
        RespondedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public QuoteStatus Response { get; private set; }

    public string ResponderName { get; private set; } = string.Empty;

    public Guid? ResponderContactId { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset RespondedAt { get; private set; }

    public IPAddress? IpAddress { get; private set; }

    public static QuoteResponse Create(
        Guid quoteVersionId,
        QuoteStatus response,
        string responderName)
    {
        if (quoteVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote version id is required.",
                nameof(quoteVersionId));
        }

        if (!AllowedResponses.Contains(response))
        {
            throw new ArgumentOutOfRangeException(
                nameof(response),
                response,
                "Response must be approved, rejected or clarification requested.");
        }

        if (string.IsNullOrWhiteSpace(responderName))
        {
            throw new ArgumentException(
                "Responder name is required.",
                nameof(responderName));
        }

        return new QuoteResponse(
            Guid.NewGuid(),
            quoteVersionId,
            response,
            responderName.Trim());
    }
}
