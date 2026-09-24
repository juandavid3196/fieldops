using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Maps a server-raised <see cref="BadHttpRequestException"/> (for example a
/// body larger than the endpoint's request size limit) to ProblemDetails
/// with the exception's status code instead of a 500. Registered before
/// <see cref="GlobalExceptionHandler"/>. The client-caused failure is not
/// logged beyond the request line.
/// </summary>
public sealed class BadHttpRequestExceptionHandler(
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        var statusCode = badRequest.StatusCode;
        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Type = statusCode == StatusCodes.Status413PayloadTooLarge
                ? "https://tools.ietf.org/html/rfc9110#section-15.5.14"
                : null,
        };

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });

        return true;
    }
}
