namespace FieldOps.Api.Contracts;

/// <summary>
/// Bodies of the public quote-link endpoints (customer-quote-approval API contracts). The token is the only
/// credential: none carries an organization, quote, version or amount, and unknown properties are ignored.
/// Selected ids are read as text so a value that is not an id is the same 400 as an unknown optional line.
/// </summary>
public sealed record QuoteLinkTokenRequest(string? Token);

public sealed record QuoteLinkCalculateRequest(string? Token, List<string?>? SelectedOptionalLineIds);

public sealed record QuoteLinkApproveRequest(string? Token, List<string?>? SelectedOptionalLineIds, bool? AcceptTerms);

public sealed record QuoteLinkDeclineRequest(string? Token, string? Reason);

public sealed record QuoteLinkQuestionRequest(string? Token, string? Message);
