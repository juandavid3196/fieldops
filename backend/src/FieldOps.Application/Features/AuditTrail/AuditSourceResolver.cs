namespace FieldOps.Application.Features.AuditTrail;

/// <summary>Per-row source of an event in the BR-02 order.</summary>
public static class AuditSourceResolver
{
    public const string Customer = "Customer";

    public const string System = "System";

    /// <summary>Public-link actions written without an actor (BR-02 rule 2).</summary>
    private static readonly string[] PublicLinkCodes =
    [
        "service_request.created", "quote.approved", "quote.rejected", "quote.clarification_requested",
        "invoice.bank_transfer_reported", "invoice_payment_attempt.created", "visit.reschedule_requested",
        "customer.message_sent",
    ];

    public static string[] PublicLinkActions => PublicLinkCodes;

    public static bool IsPublicLinkCode(string code) => PublicLinkCodes.Contains(code, StringComparer.Ordinal);

    /// <param name="actorUserId">The actor of the row, or null.</param>
    /// <param name="actorIsMember">Whether the actor has a membership in the organization of the event.</param>
    /// <param name="code">The action code.</param>
    public static string Resolve(Guid? actorUserId, bool actorIsMember, string code)
    {
        if (actorUserId is not null && !actorIsMember)
        {
            return Customer;
        }

        if (actorUserId is null)
        {
            return IsPublicLinkCode(code) ? Customer : System;
        }

        return AuditCatalog.Resolve(code).Source;
    }

    /// <summary>The actor name of a row without an actor: "Customer" for public-link actions, else "System" (BR-06).</summary>
    public static string AnonymousActor(string code) => IsPublicLinkCode(code) ? Customer : System;
}
