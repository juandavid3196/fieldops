using System.Text;
using FieldOps.Application.Features.OnlinePayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The Stripe webhook (customer-invoice-payments BR-09, BR-10): anonymous, authorized only by the <c>Stripe-Signature</c> of
/// the raw body. Not an [ApiController] and without model binding: the body is read as received (UTF-8, at most 64 KB; a
/// larger one is a 413 through the existing bad request handler) and verified over those exact characters. It has no rate
/// limit; the signature, the size cap and the event deduplication apply. Nothing here logs the payload or the signature.
/// </summary>
[Route("webhooks/stripe")]
[AllowAnonymous]
public sealed class StripeWebhooksController(ProcessPaymentWebhookHandler handler) : ControllerBase
{
    public const int MaxBodyBytes = 64 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        var payload = await ReadBodyAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();

        // A verified event answers 200 whatever its type; a failure while applying it throws and the provider retries (500).
        return await handler.HandleAsync(payload, string.IsNullOrWhiteSpace(signature) ? null : signature, cancellationToken)
            ? Ok()
            : BadRequest();
    }

    // Bounded read: at most 64 KB are buffered, anything larger is a 413 without reading further.
    private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxBodyBytes)
        {
            throw new BadHttpRequestException("The request body is too large.", StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                throw new BadHttpRequestException("The request body is too large.", StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        return new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
