using System.Threading.Channels;
using FieldOps.Application.Features.PasswordResets;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.PasswordResets;

/// <summary>
/// Bounded in-memory hand-off (BR-11): never blocks the request; a full queue
/// drops the message and logs the category only. Pending messages are lost on
/// restart (accepted: the user can request again).
/// </summary>
public sealed class PasswordResetEmailQueue : IPasswordResetEmailQueue
{
    public const int Capacity = 1_000;

    private readonly Channel<PasswordResetEmail> _channel;

    public PasswordResetEmailQueue(ILogger<PasswordResetEmailQueue> logger)
    {
        _channel = Channel.CreateBounded<PasswordResetEmail>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => logger.LogWarning("Password reset email dropped: {FailureCategory}", "QueueFull"));
    }

    public ChannelReader<PasswordResetEmail> Reader => _channel.Reader;

    public bool TryEnqueue(PasswordResetEmail email) => _channel.Writer.TryWrite(email);
}
