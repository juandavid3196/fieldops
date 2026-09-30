using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.PasswordResets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.PasswordResets;

/// <summary>
/// Drains the reset email queue after the request has been answered (BR-11).
/// A failure is caught per item, logged by category only (never the address,
/// link or token) and the message is dropped; the token stays valid.
/// </summary>
internal sealed class PasswordResetEmailDispatcher(
    PasswordResetEmailQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordResetEmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(email, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down: pending messages are lost by design.
        }
    }

    private async Task DispatchAsync(PasswordResetEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            await sender.SendAsync(PasswordResetEmailComposer.Compose(email), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Password reset email dispatch failed: {FailureCategory}", ex.GetType().Name);
        }
    }
}
