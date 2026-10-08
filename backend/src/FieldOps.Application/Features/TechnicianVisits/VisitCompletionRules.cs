using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.TechnicianVisits;

/// <summary>The raw acknowledgment of the complete request; a null text means the field was not sent.</summary>
public sealed record CompleteInput(
    string? Method,
    string? SignerName,
    string? Relationship,
    string? Comment,
    string? ReviewConfirmed,
    bool SignatureSent,
    byte[]? SignatureContent,
    string? SignatureContentType);

/// <summary>A validated acknowledgment, already mapped to the BR-08 columns of the signoff and the visit.</summary>
public sealed record VisitCompletion(
    string Method,
    string? SignerName,
    string? Relationship,
    byte[]? Signature,
    bool ReviewConfirmed,
    string? Comments,
    string? AbsenceReason,
    string? WithoutSignatureReason);

/// <summary>A visit of a work order as the BR-09 rule sees it.</summary>
public readonly record struct OrderVisitState(Guid Id, int VisitNumber, VisitStatus Status);

/// <summary>
/// Pure rules of mobile-job-completion: the acknowledgment field matrix (BR-05 to BR-07) and the work order aggregate
/// (BR-09). Errors are keyed by the request field.
/// </summary>
public static class VisitCompletionRules
{
    public const int SignerNameMaxLength = 180;

    public const int CommentMaxLength = 1000;

    public const int SignatureMaxBytes = CustomerSignoff.MaxSignatureBytes;

    private const string NameRequired = "Enter the signer's name.";

    private const string NameTooLong = "Use 180 characters or fewer.";

    private const string RelationshipInvalid = "Select the relationship.";

    private const string SignatureMissing = "Add the customer's signature.";

    private const string SignatureInvalid = "Capture the signature again.";

    private const string ReasonRequired = "Enter the reason.";

    private const string ConfirmationRequired = "Enter how the customer confirmed.";

    private const string CommentTooLong = "Use 1000 characters or fewer.";

    private const string ReviewRequired = "Confirm that the customer reviewed the work.";

    private const string MethodRequired = "Choose how the customer acknowledged the service.";

    private const string NotAllowed = "This field doesn't apply to the selected method.";

    /// <summary>Validates the acknowledgment of the chosen method; returns null with the errors filled when it is invalid.</summary>
    public static VisitCompletion? Validate(CompleteInput input, Dictionary<string, string[]> errors)
    {
        var method = input.Method?.Trim();

        if (method is null || !AcknowledgementMethods.All.Contains(method, StringComparer.Ordinal))
        {
            errors["method"] = [MethodRequired];

            return null;
        }

        var signed = method == AcknowledgementMethods.Signed;
        var absent = method == AcknowledgementMethods.CustomerAbsent;
        var refused = method == AcknowledgementMethods.CustomerRefused;
        var remote = method == AcknowledgementMethods.RemoteConfirmation;

        var signerName = Name(input.SignerName, required: signed || remote, allowed: !absent, errors);
        var relationship = Relationship(input.Relationship, required: signed || remote, errors);
        var signature = Signature(input, signed, errors);
        var comment = Comment(input.Comment, required: !signed, remote ? ConfirmationRequired : ReasonRequired, errors);
        var reviewed = Review(input.ReviewConfirmed, required: signed || remote, errors);

        if (errors.Count > 0)
        {
            return null;
        }

        return new VisitCompletion(
            method,
            signerName,
            relationship,
            signature,
            reviewed,
            signed || remote ? comment : null,
            absent || refused ? comment : null,
            signed ? null : comment);
    }

    /// <summary>
    /// BR-09: the work order completes when it is scheduled or in progress, its last occurrence exists (one-time, or the
    /// recurring visit numbered by the recurrence count) and every non-cancelled visit is completed or approved once
    /// <paramref name="completingVisitId"/> is treated as completed.
    /// </summary>
    public static bool CompletesWorkOrder(
        WorkOrderStatus status,
        string jobType,
        short? recurrenceCount,
        IReadOnlyCollection<OrderVisitState> visits,
        Guid completingVisitId)
    {
        if (status is not (WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress))
        {
            return false;
        }

        var lastOccurrenceExists = jobType == WorkOrderJobTypes.OneTime
            || (recurrenceCount is { } count && visits.Any(visit => visit.VisitNumber == count));

        return lastOccurrenceExists
            && visits.All(visit => visit.Id == completingVisitId
                || visit.Status is VisitStatus.Cancelled or VisitStatus.Completed or VisitStatus.Approved);
    }

    private static string? Name(string? value, bool required, bool allowed, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (!allowed)
        {
            return Reject(trimmed, "signerName", errors);
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                errors["signerName"] = [NameRequired];
            }

            return null;
        }

        if (trimmed.Length > SignerNameMaxLength)
        {
            errors["signerName"] = [NameTooLong];

            return null;
        }

        return trimmed;
    }

    private static string? Relationship(string? value, bool required, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (!required)
        {
            return Reject(trimmed, "relationship", errors);
        }

        if (string.IsNullOrEmpty(trimmed) || !SignerRelationships.All.Contains(trimmed, StringComparer.Ordinal))
        {
            errors["relationship"] = [RelationshipInvalid];

            return null;
        }

        return trimmed;
    }

    private static byte[]? Signature(CompleteInput input, bool signed, Dictionary<string, string[]> errors)
    {
        if (!signed)
        {
            if (input.SignatureSent)
            {
                errors["signature"] = [NotAllowed];
            }

            return null;
        }

        if (!input.SignatureSent)
        {
            errors["signature"] = [SignatureMissing];

            return null;
        }

        var content = input.SignatureContent ?? [];

        // PNG by its file signature and the declared type, 1 byte to 512 KiB; the JPEG type is rejected here.
        if (content.Length is 0 or > SignatureMaxBytes
            || VisitEvidenceContentValidator.Check(content, input.SignatureContentType) is not { IsValid: true, ContentType: VisitEvidenceContentValidator.PngType })
        {
            errors["signature"] = [SignatureInvalid];

            return null;
        }

        return content;
    }

    private static string? Comment(string? value, bool required, string requiredMessage, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                errors["comment"] = [requiredMessage];
            }

            return null;
        }

        if (trimmed.Length > CommentMaxLength)
        {
            errors["comment"] = [CommentTooLong];

            return null;
        }

        return trimmed;
    }

    private static bool Review(string? value, bool required, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim();

        if (!required)
        {
            Reject(trimmed, "reviewConfirmed", errors);

            return false;
        }

        if (!string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            errors["reviewConfirmed"] = [ReviewRequired];

            return false;
        }

        return true;
    }

    // A field marked "—" for the method must not be sent: an empty value counts as not sent.
    private static string? Reject(string? trimmed, string field, Dictionary<string, string[]> errors)
    {
        if (!string.IsNullOrEmpty(trimmed))
        {
            errors[field] = [NotAllowed];
        }

        return null;
    }
}
