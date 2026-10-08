using System.Text.Json.Serialization;
using FieldOps.Application.Features.BillingReview;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PATCH /billing-review/work-orders/{id}/review. An absent <c>note</c> leaves the note unchanged; an explicit
/// null or empty text clears it. Unknown keys, such as an organization id, are ignored.
/// </summary>
public sealed class ReviewRequestBody
{
    private string? note;

    [JsonIgnore]
    public bool NoteSent { get; private set; }

    public string? Note
    {
        get => note;
        set
        {
            note = value;
            NoteSent = true;
        }
    }

    public bool? FollowUp { get; set; }

    public string? Reason { get; set; }

    public ReviewBodyText ToText() => new(NoteSent, Note, FollowUp, Reason);
}

/// <summary>Body of POST /billing-review/work-orders/{id}/invoice; the boolean is nullable so a missing one is a 400.</summary>
public sealed record GenerateInvoiceRequestBody(string? IssueDate, string? PaymentTerms, string? Note, bool? AcknowledgeVariances)
{
    public GenerateBodyText ToText() => new(IssueDate, PaymentTerms, Note, AcknowledgeVariances);
}
