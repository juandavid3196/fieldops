using FieldOps.Api.Controllers;
using FieldOps.Application.Features.Organizations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Maps a <see cref="DuplicateEmailException"/> raised by a unique-constraint
/// race in organization registration (a concurrent request that also passed
/// the pre-insert check) to 409 ValidationProblemDetails with a field error
/// on owner.email (FR-06, AC-13). Registered before
/// <see cref="GlobalExceptionHandler"/>. Shares its field key and message
/// with the controller's pre-insert-check path so both never drift.
/// </summary>
public sealed class DuplicateEmailExceptionHandler(
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DuplicateEmailException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        var problemDetails = new ValidationProblemDetails(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [OrganizationRegistrationsController.DuplicateEmailKey] =
                    [OrganizationRegistrationsController.DuplicateEmailMessage],
            })
        {
            Status = StatusCodes.Status409Conflict,
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
