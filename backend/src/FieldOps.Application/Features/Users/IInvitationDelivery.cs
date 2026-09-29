namespace FieldOps.Application.Features.Users;

/// <summary>What the store knows about an invitation being delivered (no token).</summary>
public sealed record InvitationDeliveryContext(
    string RecipientEmail,
    string FirstName,
    string OrganizationName,
    string InviterName,
    string RoleName,
    DateTimeOffset ExpiresAt);

/// <summary>Delivery port input (BR-11); <see cref="AcceptLink"/> embeds the raw token.</summary>
public sealed record InvitationDeliveryMessage(
    string RecipientEmail,
    string FirstName,
    string OrganizationName,
    string InviterName,
    string RoleName,
    DateTimeOffset ExpiresAt,
    string AcceptLink);

/// <summary>Sends an invitation. A thrown exception rolls the mutation back.</summary>
public interface IInvitationDelivery
{
    Task SendAsync(InvitationDeliveryMessage message, CancellationToken cancellationToken);
}

/// <summary>Builds the accept link (AS-02) from the raw token.</summary>
public interface IInvitationLinkBuilder
{
    string BuildAcceptLink(string rawToken);
}
