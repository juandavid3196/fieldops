namespace FieldOps.Domain.WorkOrders;

/// <summary>How the customer acknowledged the completed service (mobile-job-completion BR-05).</summary>
public static class AcknowledgementMethods
{
    public const string Signed = "signed";

    public const string CustomerAbsent = "customer_absent";

    public const string CustomerRefused = "customer_refused";

    public const string RemoteConfirmation = "remote_confirmation";

    public static readonly string[] All = [Signed, CustomerAbsent, CustomerRefused, RemoteConfirmation];
}

/// <summary>Relationship of the signer to the customer (mobile-job-completion BR-06).</summary>
public static class SignerRelationships
{
    public static readonly string[] All = ["customer", "family_member", "tenant", "property_manager", "employee", "other"];
}

public sealed class CustomerSignoff
{
    public const int MaxSignatureBytes = 524_288;

    public const string PngMimeType = "image/png";

    private CustomerSignoff()
    {
    }

    private CustomerSignoff(Guid id, Guid visitId, bool accepted, DateTimeOffset signedAt)
    {
        Id = id;
        VisitId = visitId;
        Accepted = accepted;
        SignedAt = signedAt;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public string? SignerName { get; private set; }

    public Guid? SignerContactId { get; private set; }

    // Externally stored signature; this feature stores the PNG inline in SignatureContent instead (SA-03).
    public string? SignatureStorageKey { get; private set; }

    public bool Accepted { get; private set; }

    public string? Comments { get; private set; }

    public string? AbsenceReason { get; private set; }

    public DateTimeOffset SignedAt { get; private set; }

    public string AcknowledgementMethod { get; private set; } = AcknowledgementMethods.Signed;

    public string? SignerRelationship { get; private set; }

    // Inline PNG bytes (SA-03); never projected into any response, log or audit row.
    public byte[]? SignatureContent { get; private set; }

    public string? SignatureMimeType { get; private set; }

    public bool ReviewConfirmed { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public static CustomerSignoff Create(
        Guid visitId,
        string acknowledgementMethod,
        Guid recordedByUserId,
        DateTimeOffset signedAt,
        string? signerName = null,
        string? signerRelationship = null,
        byte[]? signatureContent = null,
        bool reviewConfirmed = false,
        string? comments = null,
        string? absenceReason = null)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        if (recordedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Recorded by user id is required.",
                nameof(recordedByUserId));
        }

        if (!AcknowledgementMethods.All.Contains(acknowledgementMethod))
        {
            throw new ArgumentException(
                "Unknown acknowledgement method.",
                nameof(acknowledgementMethod));
        }

        if (signerRelationship is not null && !SignerRelationships.All.Contains(signerRelationship))
        {
            throw new ArgumentException(
                "Unknown signer relationship.",
                nameof(signerRelationship));
        }

        var signed = acknowledgementMethod == AcknowledgementMethods.Signed;

        if (signed != (signatureContent is not null))
        {
            throw new ArgumentException(
                "Only a signed acknowledgement carries a signature.",
                nameof(signatureContent));
        }

        if (signatureContent is not null
            && (signatureContent.Length == 0 || signatureContent.Length > MaxSignatureBytes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(signatureContent),
                signatureContent.Length,
                "The signature must be between 1 byte and 512 KiB.");
        }

        var accepted = acknowledgementMethod is AcknowledgementMethods.Signed or AcknowledgementMethods.RemoteConfirmation;

        return new CustomerSignoff(Guid.NewGuid(), visitId, accepted, signedAt)
        {
            AcknowledgementMethod = acknowledgementMethod,
            SignerName = signerName,
            SignerRelationship = signerRelationship,
            SignatureContent = signatureContent,
            SignatureMimeType = signatureContent is null ? null : PngMimeType,
            ReviewConfirmed = reviewConfirmed,
            RecordedByUserId = recordedByUserId,
            Comments = comments,
            AbsenceReason = absenceReason,
        };
    }
}
