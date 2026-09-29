using FieldOps.Application.Features.Users;

namespace FieldOps.Infrastructure.Invitations;

/// <summary>
/// Development delivery adapter (BR-11): sends nothing and logs nothing, so
/// neither the recipient email nor the raw token reaches any log.
/// </summary>
internal sealed class NoOpInvitationDelivery : IInvitationDelivery
{
    public Task SendAsync(InvitationDeliveryMessage message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
