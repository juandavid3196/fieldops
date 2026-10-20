using Microsoft.AspNetCore.Diagnostics;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Absorbs the <see cref="OperationCanceledException"/> raised when the client
/// abandons a request (navigation, reload, superseded search). The client is
/// gone, so the status is 499 with no body and nothing is logged as an error.
/// Registered before <see cref="GlobalExceptionHandler"/>.
/// </summary>
public sealed class RequestAbortedExceptionHandler : IExceptionHandler
{
    public const int ClientClosedRequestStatusCode = 499;

    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not OperationCanceledException
            || !httpContext.RequestAborted.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        httpContext.Response.StatusCode = ClientClosedRequestStatusCode;

        return ValueTask.FromResult(true);
    }
}
