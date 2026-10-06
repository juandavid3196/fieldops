using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.Quotes;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the quote email after the commit (quote-builder BR-26). A delivery failure never reaches the caller:
/// the result is <see cref="QuoteEmailStatus.Failed"/>, and only the quote id and the exception type are
/// logged, never the recipient, link, token or message.
/// </summary>
internal sealed class QuoteNotifier(
    IEmailSender emailSender,
    ILogger<QuoteNotifier> logger) : IQuoteNotifier
{
    public async Task<QuoteEmailStatus> SendAsync(QuoteEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(QuoteEmailComposer.Compose(email), cancellationToken);

            return QuoteEmailStatus.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Quote email failed. QuoteId={QuoteId} Category={Category}",
                email.Data.QuoteId,
                exception.GetType().Name);

            return QuoteEmailStatus.Failed;
        }
    }
}
